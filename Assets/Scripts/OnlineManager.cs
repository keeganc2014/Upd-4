using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using Unity.Collections;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Multiplayer;
using Unity.Services.Vivox;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

// ONE public lobby for everybody + positional voice chat.
//   - Unity Multiplayer Services: anonymous sign-in, then CreateOrJoin the session "gtag-public-lobby"
//     (the first player creates it and hosts through Unity Relay, everyone else joins it).
//   - Netcode for GameObjects only carries small named messages: your head/hand positions ~15x a second.
//     Everyone else is drawn as a RemoteAvatar. No scene syncing, nothing to place in a scene.
//   - Vivox: one positional voice channel, so you hear people louder when they are near you.
//   - Moderator commands (kick / bring / freeze / send to stump) go to the host, which checks the owner code.
public class OnlineManager : MonoBehaviour
{
    public static OnlineManager Instance { get; private set; }

    const string SessionName = "gtag-public-lobby";
    const string VoiceChannel = "gtag-public-voice";
    const int MaxPlayers = 12;
    const string UpMsg = "gt_up", DownMsg = "gt_down", ModMsg = "gt_mod", CmdMsg = "gt_cmd";
    const float SendInterval = 1f / 15f;

    public string Status { get; private set; }
    public string VoiceStatus { get; private set; }
    public bool MicMuted { get; private set; }

    struct PoseData
    {
        public Vector3 head, l, r;
        public Quaternion headRot;
        public byte color, name, flags;
    }

    GtagBootstrap boot;
    NetworkManager nm;
    ISession session;
    readonly Dictionary<ulong, RemoteAvatar> avatars = new Dictionary<ulong, RemoteAvatar>();
    bool connected, voiceJoined, wantReconnect;
    float sendTimer, voiceTimer, blockedUntil;

    void Awake()
    {
        Instance = this;
        Status = "Online: starting...";
        VoiceStatus = "Voice: starting...";
    }

    void Start()
    {
        boot = GtagBootstrap.Instance;
        ConnectLoop();
    }

    static string Short(System.Exception e)
    {
        string m = e.Message;
        if (m.Length > 60) m = m.Substring(0, 60);
        return m.Replace('\n', ' ');
    }

    // ---------------- connect ----------------

    async void ConnectLoop()
    {
        try
        {
            Status = "Online: signing in...";
            await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
        catch (System.Exception e)
        {
            Status = "Offline: " + Short(e);
            Debug.LogException(e);
            Debug.LogError("[GtagTest] Unity Services failed. Is the project linked to your Unity project (Project Settings > Services)?");
            return;
        }

        StartVoice();

        while (this)
        {
            if (Time.time < blockedUntil)
            {
                Status = "Online: removed by a moderator";
                await Task.Delay(1000);
                continue;
            }

            await ConnectOnce();

            while (this && connected && !wantReconnect) await Task.Delay(500);
            wantReconnect = false;
            await LeaveQuietly();
            await Task.Delay(3000);
        }
    }

    async Task ConnectOnce()
    {
        connected = false;
        try
        {
            Status = "Online: joining the public lobby...";
            EnsureNetworkManager();
            var options = new SessionOptions { MaxPlayers = MaxPlayers }.WithRelayNetwork();
            session = await MultiplayerService.Instance.CreateOrJoinSessionAsync(SessionName, options);

            float waited = 0f;
            while (waited < 20f && !(nm.IsListening && (nm.IsServer || nm.IsConnectedClient)))
            {
                await Task.Delay(200);
                waited += 0.2f;
            }
            if (!nm.IsListening) throw new System.Exception("network did not start");

            RegisterHandlers();
            connected = true;
            Status = "Online";
        }
        catch (System.Exception e)
        {
            Status = "Offline: " + Short(e);
            Debug.LogException(e);
        }
    }

    async Task LeaveQuietly()
    {
        connected = false;
        ClearAvatars();
        try
        {
            if (session != null) await session.LeaveAsync();
        }
        catch (System.Exception) { }
        session = null;
        if (nm != null && nm.IsListening) nm.Shutdown();
    }

    public void Reconnect() { wantReconnect = true; blockedUntil = 0f; }

    void EnsureNetworkManager()
    {
        if (nm != null) return;
        if (NetworkManager.Singleton != null) { nm = NetworkManager.Singleton; return; }

        // built inactive so Awake runs only once the transport is assigned
        var go = new GameObject("NetworkManager");
        go.SetActive(false);
        nm = go.AddComponent<NetworkManager>();
        var utp = go.AddComponent<UnityTransport>();
        nm.NetworkConfig = new NetworkConfig();
        nm.NetworkConfig.NetworkTransport = utp;
        nm.NetworkConfig.EnableSceneManagement = false;   // each player keeps their own copy of the map
        nm.NetworkConfig.ConnectionApproval = false;
        go.SetActive(true);
    }

    void RegisterHandlers()
    {
        var cm = nm.CustomMessagingManager;
        cm.UnregisterNamedMessageHandler(UpMsg);
        cm.UnregisterNamedMessageHandler(DownMsg);
        cm.UnregisterNamedMessageHandler(ModMsg);
        cm.UnregisterNamedMessageHandler(CmdMsg);
        cm.RegisterNamedMessageHandler(UpMsg, OnUp);
        cm.RegisterNamedMessageHandler(DownMsg, OnDown);
        cm.RegisterNamedMessageHandler(ModMsg, OnMod);
        cm.RegisterNamedMessageHandler(CmdMsg, OnCmd);
        nm.OnClientDisconnectCallback -= OnDisconnected;
        nm.OnClientDisconnectCallback += OnDisconnected;
    }

    void OnDisconnected(ulong id)
    {
        if (nm == null) return;
        if (id == nm.LocalClientId && !nm.IsServer)
        {
            // we lost the host (or were kicked)
            connected = false;
            Status = "Online: connection lost - reconnecting";
            return;
        }
        RemoveAvatar(id);
    }

    // ---------------- poses ----------------

    static void WV3(FastBufferWriter w, Vector3 v)
    {
        w.WriteValueSafe(v.x); w.WriteValueSafe(v.y); w.WriteValueSafe(v.z);
    }

    static Vector3 RV3(FastBufferReader r)
    {
        float x, y, z;
        r.ReadValueSafe(out x); r.ReadValueSafe(out y); r.ReadValueSafe(out z);
        return new Vector3(x, y, z);
    }

    static void WritePose(FastBufferWriter w, PoseData p)
    {
        WV3(w, p.head);
        w.WriteValueSafe(p.headRot.x); w.WriteValueSafe(p.headRot.y); w.WriteValueSafe(p.headRot.z); w.WriteValueSafe(p.headRot.w);
        WV3(w, p.l); WV3(w, p.r);
        w.WriteByteSafe(p.color); w.WriteByteSafe(p.name); w.WriteByteSafe(p.flags);
    }

    static PoseData ReadPose(FastBufferReader r)
    {
        var p = new PoseData();
        p.head = RV3(r);
        float x, y, z, w;
        r.ReadValueSafe(out x); r.ReadValueSafe(out y); r.ReadValueSafe(out z); r.ReadValueSafe(out w);
        p.headRot = new Quaternion(x, y, z, w);
        p.l = RV3(r); p.r = RV3(r);
        r.ReadByteSafe(out p.color); r.ReadByteSafe(out p.name); r.ReadByteSafe(out p.flags);
        return p;
    }

    PoseData LocalPose()
    {
        var p = new PoseData();
        p.head = boot.Head.position;
        p.headRot = boot.Head.rotation;
        p.l = boot.LeftFollower.position;
        p.r = boot.RightFollower.position;
        p.color = (byte)PlayerProfile.ColorIdx;
        p.name = (byte)PlayerProfile.NameIdx;
        p.flags = (byte)(OwnerState.Unlocked ? 1 : 0);
        return p;
    }

    void SendPose()
    {
        PoseData p = LocalPose();
        using (var w = new FastBufferWriter(160, Allocator.Temp))
        {
            if (nm.IsServer)
            {
                w.WriteValueSafe(nm.LocalClientId);
                WritePose(w, p);
                foreach (ulong id in nm.ConnectedClientsIds)
                    if (id != nm.LocalClientId)
                        nm.CustomMessagingManager.SendNamedMessage(DownMsg, id, w, NetworkDelivery.UnreliableSequenced);
            }
            else
            {
                WritePose(w, p);
                nm.CustomMessagingManager.SendNamedMessage(UpMsg, NetworkManager.ServerClientId, w, NetworkDelivery.UnreliableSequenced);
            }
        }
    }

    // client -> host: pass it on to everybody else
    void OnUp(ulong sender, FastBufferReader reader)
    {
        if (nm == null || !nm.IsServer) return;
        PoseData p = ReadPose(reader);
        ApplyPose(sender, p);
        using (var w = new FastBufferWriter(160, Allocator.Temp))
        {
            w.WriteValueSafe(sender);
            WritePose(w, p);
            foreach (ulong id in nm.ConnectedClientsIds)
                if (id != sender && id != nm.LocalClientId)
                    nm.CustomMessagingManager.SendNamedMessage(DownMsg, id, w, NetworkDelivery.UnreliableSequenced);
        }
    }

    // host -> client
    void OnDown(ulong sender, FastBufferReader reader)
    {
        ulong id;
        reader.ReadValueSafe(out id);
        ApplyPose(id, ReadPose(reader));
    }

    void ApplyPose(ulong id, PoseData p)
    {
        if (nm == null || id == nm.LocalClientId) return;
        RemoteAvatar a;
        if (!avatars.TryGetValue(id, out a) || a == null)
        {
            a = RemoteAvatar.Create(id);
            avatars[id] = a;
        }
        a.SetPose(p.head, p.headRot, p.l, p.r, p.color, p.name, (p.flags & 1) != 0);
    }

    void RemoveAvatar(ulong id)
    {
        RemoteAvatar a;
        if (avatars.TryGetValue(id, out a) && a != null) Destroy(a.gameObject);
        avatars.Remove(id);
    }

    void ClearAvatars()
    {
        foreach (var kv in avatars) if (kv.Value != null) Destroy(kv.Value.gameObject);
        avatars.Clear();
    }

    // ---------------- list for the moderator page ----------------

    public List<ulong> RemoteIds()
    {
        var l = new List<ulong>(avatars.Keys);
        l.Sort();
        return l;
    }

    public string NameOf(ulong id)
    {
        return "Player " + id;
    }

    public void TeleportToPlayer(ulong id)
    {
        RemoteAvatar a;
        if (!avatars.TryGetValue(id, out a) || a == null) return;
        Vector3 h = a.HeadPosition;
        boot.TeleportToGround(new Vector3(h.x + 1f, h.y - 1.5f, h.z));
    }

    // ---------------- moderator commands ----------------

    // action: 1 kick, 2 bring to me, 3 freeze 10s, 4 send to the stump
    public void SendMod(byte action, ulong target, Vector3 myPosition)
    {
        if (!connected || nm == null || !nm.IsListening) return;
        if (nm.IsServer) { HandleMod(OwnerState.Code, action, target, myPosition); return; }
        using (var w = new FastBufferWriter(64, Allocator.Temp))
        {
            w.WriteValueSafe(OwnerState.Code);
            w.WriteByteSafe(action);
            w.WriteValueSafe(target);
            WV3(w, myPosition);
            nm.CustomMessagingManager.SendNamedMessage(ModMsg, NetworkManager.ServerClientId, w, NetworkDelivery.Reliable);
        }
    }

    // arrives at the host: only obeyed if it carries the owner code
    void OnMod(ulong sender, FastBufferReader reader)
    {
        if (nm == null || !nm.IsServer) return;
        int code; byte action; ulong target;
        reader.ReadValueSafe(out code);
        reader.ReadByteSafe(out action);
        reader.ReadValueSafe(out target);
        Vector3 pos = RV3(reader);
        HandleMod(code, action, target, pos);
    }

    void HandleMod(int code, byte action, ulong target, Vector3 pos)
    {
        if (code != OwnerState.Code) return;
        if (target == nm.LocalClientId) { ApplyCommand(action, pos); return; }

        using (var w = new FastBufferWriter(32, Allocator.Temp))
        {
            w.WriteByteSafe(action);
            WV3(w, pos);
            nm.CustomMessagingManager.SendNamedMessage(CmdMsg, target, w, NetworkDelivery.Reliable);
        }
        if (action == 1) StartCoroutine(KickLater(target));
    }

    IEnumerator KickLater(ulong id)
    {
        yield return new WaitForSeconds(0.5f);
        if (nm != null && nm.IsServer) nm.DisconnectClient(id);
    }

    // arrives at the player being moderated
    void OnCmd(ulong sender, FastBufferReader reader)
    {
        byte action;
        reader.ReadByteSafe(out action);
        Vector3 pos = RV3(reader);
        ApplyCommand(action, pos);
    }

    void ApplyCommand(byte action, Vector3 pos)
    {
        switch (action)
        {
            case 1: blockedUntil = Time.time + 120f; break;      // kicked: stay out for two minutes
            case 2: boot.TeleportToGround(pos); break;
            case 3: StartCoroutine(Freeze(10f)); break;
            case 4: boot.TeleportToSpawn(); break;
        }
    }

    IEnumerator Freeze(float seconds)
    {
        boot.Player.disableMovement = true;
        for (float t = 0f; t < seconds; t += Time.deltaTime)
        {
            boot.Body.velocity = Vector3.zero;
            yield return null;
        }
        boot.Player.disableMovement = false;
    }

    // ---------------- voice ----------------

    async void StartVoice()
    {
        try
        {
            VoiceStatus = "Voice: starting...";
            int mics = Microphone.devices.Length;   // also makes Unity add the microphone permission to the Android build
            Debug.Log("[GtagTest] Microphones found: " + mics);
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Microphone))
            {
                Permission.RequestUserPermission(Permission.Microphone);
                float waited = 0f;
                while (!Permission.HasUserAuthorizedPermission(Permission.Microphone) && waited < 30f)
                {
                    await Task.Delay(500);
                    waited += 0.5f;
                }
            }
#endif
            await VivoxService.Instance.InitializeAsync();
            await VivoxService.Instance.LoginAsync();
            await VivoxService.Instance.JoinPositionalChannelAsync(
                VoiceChannel, ChatCapability.AudioOnly,
                new Channel3DProperties(40, 3, 1.0f, AudioFadeModel.InverseByDistance));
            voiceJoined = true;
            VoiceStatus = MicMuted ? "Voice: mic muted" : "Voice: on";
        }
        catch (System.Exception e)
        {
            VoiceStatus = "Voice: " + Short(e);
            Debug.LogException(e);
        }
    }

    public void ToggleMic()
    {
        MicMuted = !MicMuted;
        if (!voiceJoined) return;
        if (MicMuted) VivoxService.Instance.MuteInputDevice();
        else VivoxService.Instance.UnmuteInputDevice();
        VoiceStatus = MicMuted ? "Voice: mic muted" : "Voice: on";
    }

    // ---------------- per frame ----------------

    void Update()
    {
        if (boot == null || boot.Cam == null) return;

        if (voiceJoined)
        {
            voiceTimer += Time.deltaTime;
            if (voiceTimer > 0.2f)
            {
                voiceTimer = 0f;
                // tells Vivox where you are, so players near you sound close
                VivoxService.Instance.Set3DPosition(boot.Cam.gameObject, VoiceChannel, true);
            }
        }

        if (!connected || nm == null || !nm.IsListening) return;

        sendTimer += Time.deltaTime;
        if (sendTimer >= SendInterval)
        {
            sendTimer = 0f;
            SendPose();
        }

        // drop players we have not heard from
        List<ulong> stale = null;
        foreach (var kv in avatars)
            if (kv.Value == null || Time.time - kv.Value.lastUpdate > 6f)
            {
                if (stale == null) stale = new List<ulong>();
                stale.Add(kv.Key);
            }
        if (stale != null) foreach (ulong id in stale) RemoveAvatar(id);

        Status = "Online: " + (avatars.Count + 1) + " in lobby";
    }

    void OnApplicationQuit()
    {
        if (session != null) session.LeaveAsync();
    }
}
