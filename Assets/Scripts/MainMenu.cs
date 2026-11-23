using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using GorillaLocomotion;

// One main menu, worn on your LEFT HAND and turned to face you.
// Left X opens/closes it. Poke a button with your right hand + pull the right trigger.
// Desktop: Tab opens it, aim + left click.
// Home -> Player / Settings are open to everyone. Mods and Moderator are locked behind the owner code (typed on the menu).
public class MainMenu : MonoBehaviour
{
    class Btn
    {
        public Image bg;
        public Text label;
        public Func<string> text;
        public Func<bool> isOn;
        public Action click;
    }

    class Page
    {
        public GameObject go;
        public string title;
        public readonly List<Btn> buttons = new List<Btn>();
    }

    GtagBootstrap boot;
    XRRigInput rig;
    Player player;
    Rigidbody body;

    GameObject panel;
    Text titleText, footerText, keypadText;
    readonly Dictionary<string, Page> pages = new Dictionary<string, Page>();
    readonly Dictionary<Collider, Btn> byCollider = new Dictionary<Collider, Btn>();
    Page current;
    Btn hover;
    bool open;

    // keypad
    string pendingPage = "home";
    string entered = "";
    string keypadMessage = "";

    // moderator
    int targetIdx;

    // mod state
    int speedIdx, gravIdx, armIdx;
    bool fly, noclip, platforms, flashlight, darkness, freeze, god;
    bool hasSaved;
    Vector3 savedRig;
    readonly GameObject[] plat = new GameObject[2];
    Light torch;

    static readonly string[] SpeedNames = { "Normal", "Fast", "Super" };
    static readonly float[] JumpMult = { 1.1f, 1.3f, 1.5f };
    static readonly float[] MaxJump = { 6.5f, 9f, 13f };
    static readonly float[] GravScale = { 1f, 0.5f, 0.25f };
    static readonly float[] ArmScales = { 1f, 1.5f, 2f, 3f };
    const float BaseArmLength = 1.5f;
    const float FlySpeed = 6f;
    const float PanelScale = 0.0006f;   // 1 canvas px = 0.6 mm -> the panel is about 22 x 31 cm, like a watch screen

    Color cNormal = new Color(0.17f, 0.17f, 0.2f, 0.97f);
    Color cOn = new Color(0.15f, 0.45f, 0.2f, 0.97f);
    Color cHover = new Color(0.75f, 0.45f, 0.1f, 0.97f);
    Color cLocked = new Color(0.3f, 0.12f, 0.12f, 0.97f);

    bool Unlocked { get { return OwnerState.Unlocked; } }

    void Start()
    {
        boot = GtagBootstrap.Instance;
        rig = boot.Rig;
        player = boot.Player;
        body = boot.Body;

        BuildPanel();
        BuildTorch();
        Show("home");
        panel.SetActive(false);
    }

    // ---------------- pages ----------------

    void BuildPanel()
    {
        Font font = GetFont();

        panel = new GameObject("MainMenu");
        panel.layer = GtagBootstrap.MenuLayer;
        var canvas = panel.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = panel.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(360f, 520f);
        rt.localScale = Vector3.one * PanelScale;

        MakeRect("Background", panel.transform, Vector2.zero, new Vector2(360f, 520f)).gameObject
            .AddComponent<Image>().color = new Color(0.04f, 0.04f, 0.05f, 0.95f);

        titleText = MakeText("Title", panel.transform, new Vector2(0f, 228f), new Vector2(340f, 40f), font, 26);
        titleText.fontStyle = FontStyle.Bold;
        footerText = MakeText("Footer", panel.transform, new Vector2(0f, -238f), new Vector2(350f, 30f), font, 14);
        footerText.color = new Color(0.7f, 0.8f, 0.7f);

        // ---- Home ----
        Page home = NewPage("home", "MAIN MENU");
        Add(home, font, () => "Player", null, () => Show("player"));
        Add(home, font, () => "Settings", null, () => Show("settings"));
        Add(home, font, () => Unlocked ? "Mods" : "Mods [LOCK]", () => Unlocked, () => Go("mods"));
        Add(home, font, () => Unlocked ? "Moderator" : "Moderator [LOCK]", () => Unlocked, () => Go("moderator"));
        Add(home, font, () => "Go to Stump", null, () => boot.TeleportToSpawn());
        Add(home, font, () => Unlocked ? "Lock Menu" : "Owner Code", null, () => { if (Unlocked) OwnerState.Unlocked = false; else Go("home"); });
        Add(home, font, () => "Close", null, () => SetOpen(false));

        // ---- Mods ----
        Page mods = NewPage("mods", "MODS");
        Add(mods, font, () => "Speed: " + SpeedNames[speedIdx], () => speedIdx > 0, () => { speedIdx = (speedIdx + 1) % 3; ApplySpeed(); });
        Add(mods, font, () => "Gravity: " + GravScale[gravIdx] + "x", () => gravIdx > 0, () => { gravIdx = (gravIdx + 1) % 3; ApplyGravity(); });
        Add(mods, font, () => "Long Arms: " + ArmScales[armIdx] + "x", () => armIdx > 0, () => { armIdx = (armIdx + 1) % ArmScales.Length; ApplyArms(); });
        Add(mods, font, () => "Fly: " + (fly ? "ON" : "OFF"), () => fly, () => { fly = !fly; });
        Add(mods, font, () => "Noclip: " + (noclip ? "ON" : "OFF"), () => noclip, () => SetNoclip(!noclip));
        Add(mods, font, () => "Platforms: " + (platforms ? "ON" : "OFF"), () => platforms, () => { platforms = !platforms; if (!platforms) ClearPlatforms(); });
        Add(mods, font, () => "Invincible: " + (god ? "ON" : "OFF"), () => god, () => { god = !god; boot.Invincible = god; });
        Add(mods, font, () => "Freeze Monster: " + (freeze ? "ON" : "OFF"), () => freeze, () => { freeze = !freeze; ApplyFreeze(); });
        Add(mods, font, () => "Flashlight: " + (flashlight ? "ON" : "OFF"), () => flashlight, () => { flashlight = !flashlight; torch.enabled = flashlight; });
        Add(mods, font, () => "Darkness: " + (darkness ? "ON" : "OFF"), () => darkness, () => SetDarkness(!darkness));
        Add(mods, font, () => "Random Spot", null, () => boot.TeleportToGround(boot.FindForestFloor()));
        Add(mods, font, () => hasSaved ? "Save Pos (set)" : "Save Pos", null, () => { savedRig = player.transform.position; hasSaved = true; });
        Add(mods, font, () => "Load Pos", null, () => { if (hasSaved) boot.MoveRig(savedRig); });
        Add(mods, font, () => "Reset Mods", null, ResetMods);
        Add(mods, font, () => "Back", null, () => Show("home"));

        // ---- Moderator ----
        Page mod = NewPage("moderator", "MODERATOR");
        Add(mod, font, () => "Player: " + TargetName(), null, CycleTarget);
        Add(mod, font, () => "Kick", null, () => ModAction(1));
        Add(mod, font, () => "Bring To Me", null, () => ModAction(2));
        Add(mod, font, () => "Go To Player", null, GoToTarget);
        Add(mod, font, () => "Freeze 10s", null, () => ModAction(3));
        Add(mod, font, () => "Send To Stump", null, () => ModAction(4));
        Add(mod, font, () => "Back", null, () => Show("home"));

        // ---- Settings ----
        Page set = NewPage("settings", "SETTINGS");
        Add(set, font, () => "Turn: " + PlayerProfile.TurnNames[PlayerProfile.TurnIdx], null, () => PlayerProfile.TurnIdx = PlayerProfile.TurnIdx + 1);
        Add(set, font, () => "Monster: " + (PlayerProfile.MonsterOn ? "ON" : "OFF"), () => PlayerProfile.MonsterOn, ToggleMonster);
        Add(set, font, () => "Monster: " + PlayerProfile.MonsterNames[PlayerProfile.MonsterSpeedIdx], null, () => PlayerProfile.MonsterSpeedIdx = PlayerProfile.MonsterSpeedIdx + 1);
        Add(set, font, () => "Mirror: " + (PlayerProfile.MirrorOn ? "ON" : "OFF"), () => PlayerProfile.MirrorOn, () => PlayerProfile.MirrorOn = !PlayerProfile.MirrorOn);
        Add(set, font, () => OnlineManager.Instance != null && OnlineManager.Instance.MicMuted ? "Mic: MUTED" : "Mic: ON", () => OnlineManager.Instance != null && !OnlineManager.Instance.MicMuted, () => { if (OnlineManager.Instance != null) OnlineManager.Instance.ToggleMic(); });
        Add(set, font, () => "Reconnect", null, () => { if (OnlineManager.Instance != null) OnlineManager.Instance.Reconnect(); });
        Add(set, font, () => "Reset Board", null, () => Scores.Clear());
        Add(set, font, () => "Back", null, () => Show("home"));

        // ---- Player ----
        Page pl = NewPage("player", "PLAYER");
        Add(pl, font, () => "Name: " + PlayerProfile.Name, null, () => PlayerProfile.NameIdx = PlayerProfile.NameIdx + 1);
        Add(pl, font, () => "Color: " + PlayerProfile.ColorNames[PlayerProfile.ColorIdx], null, () => PlayerProfile.ColorIdx = PlayerProfile.ColorIdx + 1);
        Add(pl, font, () => "Back", null, () => Show("home"));

        // ---- Keypad ----
        Page pad = NewPage("keypad", "OWNER CODE");
        keypadText = MakeText("Code", pad.go.transform, new Vector2(0f, 175f), new Vector2(330f, 50f), font, 30);
        string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", "Clear", "0", "Back" };
        for (int i = 0; i < keys.Length; i++)
        {
            string k = keys[i];
            Vector2 pos = new Vector2(-110f + (i % 3) * 110f, 100f - (i / 3) * 62f);
            Action click;
            if (k == "Clear") click = () => { entered = ""; keypadMessage = ""; };
            else if (k == "Back") click = () => Show("home");
            else click = () => PressKey(k);
            AddAt(pad, font, pos, new Vector2(100f, 54f), () => k, null, click);
        }
    }

    Page NewPage(string id, string title)
    {
        var p = new Page { title = title };
        p.go = MakeRect("Page_" + id, panel.transform, Vector2.zero, new Vector2(360f, 520f)).gameObject;
        pages[id] = p;
        return p;
    }

    void Show(string id)
    {
        foreach (var kv in pages) kv.Value.go.SetActive(kv.Key == id);
        current = pages[id];
        titleText.text = current.title;
        hover = null;
    }

    // go to a locked page, asking for the code first if needed
    void Go(string id)
    {
        if (Unlocked && id != "home") { Show(id); return; }
        pendingPage = id;
        entered = "";
        keypadMessage = "";
        Show("keypad");
    }

    void PressKey(string k)
    {
        if (entered.Length >= 6) entered = "";
        entered += k;
        string code = OwnerState.Code.ToString();
        if (entered == code)
        {
            OwnerState.Unlocked = true;
            keypadMessage = "";
            entered = "";
            Show(pendingPage);
        }
        else if (entered.Length >= code.Length)
        {
            keypadMessage = "WRONG CODE";
            entered = "";
        }
    }

    void AddAt(Page page, Font font, Vector2 pos, Vector2 size, Func<string> text, Func<bool> isOn, Action click)
    {
        RectTransform r = MakeRect("Button", page.go.transform, pos, size);
        var img = r.gameObject.AddComponent<Image>();
        img.color = cNormal;

        var box = r.gameObject.AddComponent<BoxCollider>();
        box.size = new Vector3(size.x, size.y, 120f);
        box.isTrigger = true;

        Text t = MakeText("Label", r, Vector2.zero, size - new Vector2(4f, 4f), font, 19);

        var b = new Btn { bg = img, label = t, text = text, isOn = isOn, click = click };
        page.buttons.Add(b);
        byCollider[box] = b;
    }

    void Add(Page page, Font font, Func<string> text, Func<bool> isOn, Action click)
    {
        int index = page.buttons.Count;
        Vector2 pos = new Vector2(index % 2 == 0 ? -85f : 85f, 175f - (index / 2) * 50f);
        AddAt(page, font, pos, new Vector2(160f, 44f), text, isOn, click);
    }

    static Font GetFont()
    {
        Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (f == null) f = Font.CreateDynamicFontFromOSFont("Arial", 24);
        return f;
    }

    static RectTransform MakeRect(string name, Transform parent, Vector2 pos, Vector2 size)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.layer = GtagBootstrap.MenuLayer;
        go.transform.SetParent(parent, false);
        var r = go.GetComponent<RectTransform>();
        r.anchorMin = r.anchorMax = new Vector2(0.5f, 0.5f);
        r.pivot = new Vector2(0.5f, 0.5f);
        r.sizeDelta = size;
        r.anchoredPosition = pos;
        return r;
    }

    static Text MakeText(string name, Transform parent, Vector2 pos, Vector2 size, Font font, int fontSize)
    {
        RectTransform r = MakeRect(name, parent, pos, size);
        var t = r.gameObject.AddComponent<Text>();
        t.font = font;
        t.fontSize = fontSize;
        t.alignment = TextAnchor.MiddleCenter;
        t.color = Color.white;
        t.raycastTarget = false;
        return t;
    }

    void BuildTorch()
    {
        var go = new GameObject("Flashlight");
        go.transform.SetParent(boot.Head, false);
        torch = go.AddComponent<Light>();
        torch.type = LightType.Spot;
        torch.range = 35f;
        torch.spotAngle = 60f;
        torch.intensity = 3f;
        torch.shadows = LightShadows.None;
        torch.enabled = false;
    }

    // ---------------- update ----------------

    void Update()
    {
        if (boot == null || !player.enabled) return;

        if (rig.MenuTogglePressed) SetOpen(!open);
        if (boot.IsDying && open) SetOpen(false);

        UpdateMenu();
        UpdateFly();
        UpdatePlatforms();
    }

    void SetOpen(bool value)
    {
        open = value;
        panel.SetActive(open);
        if (open) Show("home");
        hover = null;
    }

    void UpdateMenu()
    {
        if (!open) return;

        // worn on the left hand, turned to face your head
        Vector3 headPos = boot.Head.position;
        Vector3 pos;
        if (rig.XRActive)
        {
            Vector3 lp = boot.LeftHand.position;
            pos = lp + boot.LeftHand.up * 0.06f + (headPos - lp).normalized * 0.04f;
        }
        else
        {
            pos = headPos + boot.Head.forward * 0.55f + boot.Head.right * -0.12f + boot.Head.up * -0.05f;
        }
        panel.transform.position = pos;
        panel.transform.rotation = Quaternion.LookRotation(pos - headPos, boot.Head.up);

        // what is being pointed at / poked?
        hover = null;
        Collider hit = null;
        if (rig.XRActive)
        {
            Collider[] hits = Physics.OverlapSphere(boot.RightHand.position, 0.025f, 1 << GtagBootstrap.MenuLayer, QueryTriggerInteraction.Collide);
            if (hits.Length > 0) hit = hits[0];
        }
        else
        {
            RaycastHit rh;
            Ray ray = new Ray(boot.Head.position, boot.Head.forward);
            if (Physics.Raycast(ray, out rh, 3f, 1 << GtagBootstrap.MenuLayer, QueryTriggerInteraction.Collide))
                hit = rh.collider;
        }
        if (hit != null) byCollider.TryGetValue(hit, out hover);

        if (hover != null && rig.ClickPressed)
        {
            Action click = hover.click;
            rig.Pulse();
            click();   // may switch pages, so it is called last
            hover = null;
        }

        foreach (Btn b in current.buttons)
        {
            b.label.text = b.text();
            Color c = cNormal;
            if (b.isOn != null && b.isOn()) c = cOn;
            if (b == hover) c = cHover;
            b.bg.color = c;
        }

        if (current == pages["keypad"])
            keypadText.text = keypadMessage != "" ? keypadMessage : (entered.Length == 0 ? "- - -" : new string('*', entered.Length));

        OnlineManager om = OnlineManager.Instance;
        footerText.text = om != null ? om.Status + "  |  " + om.VoiceStatus : "";
    }

    void UpdateFly()
    {
        bool flying = fly || noclip;
        body.useGravity = !flying;
        if (!flying) return;

        if (rig.FlyHeld && !open && !boot.IsDying)
        {
            Vector3 dir = rig.XRActive ? boot.RightHand.forward : boot.Head.forward;
            body.velocity = dir * FlySpeed;
        }
        else
        {
            body.velocity = Vector3.zero;
        }
    }

    void UpdatePlatforms()
    {
        if (!platforms) return;
        HandPlatform(0, rig.LeftGrip, boot.LeftHand);
        HandPlatform(1, rig.RightGrip, boot.RightHand);
    }

    void HandPlatform(int i, bool grip, Transform hand)
    {
        if (grip && plat[i] == null)
        {
            var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
            p.name = "Platform";
            p.transform.localScale = new Vector3(0.5f, 0.04f, 0.5f);
            p.transform.position = hand.position + Vector3.down * 0.05f;
            p.transform.rotation = Quaternion.Euler(0f, hand.eulerAngles.y, 0f);
            p.GetComponent<Renderer>().material.color = new Color(0.2f, 0.75f, 0.3f);
            plat[i] = p;
        }
        else if (!grip && plat[i] != null)
        {
            Destroy(plat[i]);
            plat[i] = null;
        }
    }

    void ClearPlatforms()
    {
        for (int i = 0; i < plat.Length; i++)
        {
            if (plat[i] != null) Destroy(plat[i]);
            plat[i] = null;
        }
    }

    // ---------------- mods / settings ----------------

    void ApplySpeed()
    {
        player.jumpMultiplier = JumpMult[speedIdx];
        player.maxJumpSpeed = MaxJump[speedIdx];
    }

    void ApplyGravity()
    {
        Physics.gravity = Vector3.down * 9.81f * GravScale[gravIdx];
    }

    // Longer reach, but the push you get from each swing stays the same as normal arms.
    void ApplyArms()
    {
        rig.ArmScale = ArmScales[armIdx];
        player.maxArmLength = BaseArmLength * ArmScales[armIdx];
        player.handMoveScale = 1f / ArmScales[armIdx];
    }

    void ApplyFreeze()
    {
        if (boot.Monster != null) boot.Monster.frozen = freeze;
    }

    void ToggleMonster()
    {
        PlayerProfile.MonsterOn = !PlayerProfile.MonsterOn;
        boot.SetMonsterEnabled(PlayerProfile.MonsterOn);
        ApplyFreeze();
    }

    void SetNoclip(bool on)
    {
        noclip = on;
        player.locomotionEnabledLayers = on ? (LayerMask)0 : (LayerMask)1;
        boot.HeadCollider.isTrigger = on;
        boot.BodyCollider.isTrigger = on;
        body.velocity = Vector3.zero;
        boot.ResyncPlayer();

        // turning noclip off while inside a wall would trap you - go back to the stump instead
        if (!on && Physics.CheckSphere(boot.Head.position, 0.25f, 1 << 0, QueryTriggerInteraction.Ignore))
            boot.TeleportToSpawn();
    }

    void SetDarkness(bool on)
    {
        darkness = on;
        boot.Sun.intensity = on ? 0.04f : 1f;
        RenderSettings.ambientLight = on ? new Color(0.02f, 0.02f, 0.03f) : new Color(0.55f, 0.58f, 0.62f);
        RenderSettings.fog = on;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogDensity = 0.035f;
        RenderSettings.fogColor = Color.black;
        boot.Cam.backgroundColor = on ? Color.black : new Color(0.45f, 0.62f, 0.85f);
        if (on && !flashlight) { flashlight = true; torch.enabled = true; }
    }

    void ResetMods()
    {
        speedIdx = 0; gravIdx = 0; armIdx = 0;
        fly = false; platforms = false; flashlight = false; freeze = false; god = false;
        torch.enabled = false;
        ClearPlatforms();
        if (noclip) SetNoclip(false);
        if (darkness) SetDarkness(false);
        boot.Invincible = false;
        ApplySpeed(); ApplyGravity(); ApplyArms(); ApplyFreeze();
        body.useGravity = true;
    }

    // ---------------- moderator ----------------

    ulong CurrentTarget(out bool ok)
    {
        ok = false;
        OnlineManager om = OnlineManager.Instance;
        if (om == null) return 0;
        List<ulong> ids = om.RemoteIds();
        if (ids.Count == 0) return 0;
        targetIdx = Mathf.Clamp(targetIdx, 0, ids.Count - 1);
        ok = true;
        return ids[targetIdx];
    }

    string TargetName()
    {
        bool ok;
        ulong id = CurrentTarget(out ok);
        return ok ? OnlineManager.Instance.NameOf(id) : "none online";
    }

    void CycleTarget()
    {
        OnlineManager om = OnlineManager.Instance;
        if (om == null) return;
        int n = om.RemoteIds().Count;
        if (n > 0) targetIdx = (targetIdx + 1) % n;
    }

    void ModAction(byte action)
    {
        bool ok;
        ulong id = CurrentTarget(out ok);
        if (ok) OnlineManager.Instance.SendMod(action, id, player.transform.position);
    }

    void GoToTarget()
    {
        bool ok;
        ulong id = CurrentTarget(out ok);
        if (ok) OnlineManager.Instance.TeleportToPlayer(id);
    }

    void OnDestroy()
    {
        Physics.gravity = Vector3.down * 9.81f;
    }
}
