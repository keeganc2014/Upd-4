using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using GorillaLocomotion;

// Builds the whole game at runtime: map, lighting, gorilla rig, input, main menu, leaderboard board and monster.
// Nothing needs to be placed in the scene by hand - it starts itself in any scene.
public class GtagBootstrap : MonoBehaviour
{
    public const int PlayerLayer = 2;   // built-in "Ignore Raycast" - keeps the player out of hand raycasts
    public const int MenuLayer = 5;     // built-in "UI"
    public const int HiddenLayer = 8;   // your own body/arms: drawn for other players, never by your own camera
    public const int MirrorLayer = 9;   // the glass of the stump mirror (the mirror's own camera does not draw it)

    public static GtagBootstrap Instance { get; private set; }

    public Player Player { get; private set; }
    public Rigidbody Body { get; private set; }
    public Camera Cam { get; private set; }
    public Transform Head { get { return Cam.transform; } }
    public Transform LeftHand { get; private set; }
    public Transform RightHand { get; private set; }
    public Transform LeftFollower { get { return leftFollowerT; } }
    public Transform RightFollower { get { return rightFollowerT; } }
    public SphereCollider HeadCollider { get; private set; }
    public CapsuleCollider BodyCollider { get; private set; }
    public XRRigInput Rig { get; private set; }
    public Light Sun { get; private set; }
    public Bounds MapBounds { get; private set; }
    public Vector3 SpawnPoint { get; private set; }
    public Monster Monster { get; private set; }
    public bool Invincible { get; set; }
    public bool IsDying { get; private set; }
    public float RunTime { get { return Time.time - runStart; } }

    float runStart;
    Image overlay;
    Text overlayText;

    GameObject mapRoot;
    Transform leftFollowerT, rightFollowerT;

    static readonly Dictionary<string, string> TextureAlias = new Dictionary<string, string>
    {
        { "caverockarrow1", "caverockwitharrows" },
        { "caverockarrow2", "caverockwitharrows2" },
        { "clay", "clay3" },
        { "crackedcement", "crackedconcrete" },
        { "darkbrownpanel", "darkerbrownpanel" },
        { "tableclay", "claytable" },
        { "loglowres_1", "loglowres" },
        { "pitground", "ground" },
        { "riverbed", "ground" },
        { "riverwall", "rockwall" },
        { "pitwall", "rockwall" },
        { "minewall", "rockwall" },
    };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoStart()
    {
        if (Object.FindFirstObjectByType<GtagBootstrap>() != null) return;
        new GameObject("GtagBootstrap").AddComponent<GtagBootstrap>();
    }

    void Awake()
    {
        Instance = this;
    }

    IEnumerator Start()
    {
        BuildWorld();
        yield return new WaitForFixedUpdate();   // let physics register the freshly created map colliders
        Physics.SyncTransforms();

        SpawnPoint = FindSpawn();   // the stump (next to the computer)
        BuildPlayer(SpawnPoint);
        PlayerRigModel.Create(Cam.transform, leftFollowerT, rightFollowerT);
        BuildBoard();
        StumpMirror.Create(this);
        BuildOverlay();
        gameObject.AddComponent<OnlineManager>();

        // give XR tracking a moment to deliver real poses, then sync the locomotion state to them
        yield return new WaitForSeconds(0.6f);
        ResyncPlayer();
        Player.enabled = true;

        gameObject.AddComponent<MainMenu>();

        Monster = Monster.Create(this);
        SetMonsterEnabled(PlayerProfile.MonsterOn);
        runStart = Time.time;
    }

    public void SetMonsterEnabled(bool on)
    {
        if (Monster == null) return;
        Monster.SetActive(on);
    }

    void Update()
    {
        if (Player != null && Player.enabled && !IsDying && Player.transform.position.y < MapBounds.min.y - 30f)
        {
            TeleportToSpawn();
            runStart = Time.time;
        }
    }

    // ---------------- world ----------------

    void BuildWorld()
    {
        var lightGo = new GameObject("Sun");
        Sun = lightGo.AddComponent<Light>();
        Sun.type = LightType.Directional;
        Sun.intensity = 1f;
        Sun.shadows = LightShadows.None;
        lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.55f, 0.58f, 0.62f);

        GameObject prefab = Resources.Load<GameObject>("Map/GorillaTagMap");
        if (prefab != null)
        {
            mapRoot = Instantiate(prefab);
            mapRoot.name = "GorillaTagMap";
            PrepareMap(mapRoot);
        }
        else
        {
            Debug.LogWarning("[GtagTest] Assets/Resources/Map/GorillaTagMap.fbx not found - using a flat floor instead.");
        }

        MapBounds = ComputeBounds(mapRoot);
        if (mapRoot == null)
            MakeFallbackFloor();
        Debug.Log("[GtagTest] Map bounds: center " + MapBounds.center + " size " + MapBounds.size);
    }

    void MakeFallbackFloor()
    {
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "FallbackFloor";
        floor.transform.localScale = new Vector3(200f, 1f, 200f);
        floor.transform.position = new Vector3(0f, -0.5f, 0f);
        MapBounds = new Bounds(Vector3.zero, new Vector3(200f, 10f, 200f));
    }

    static Bounds ComputeBounds(GameObject root)
    {
        if (root == null) return new Bounds(Vector3.zero, new Vector3(100f, 10f, 100f));
        Renderer[] rs = root.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return new Bounds(root.transform.position, new Vector3(100f, 10f, 100f));
        Bounds b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return b;
    }

    void PrepareMap(GameObject root)
    {
        // textures shipped next to the model (Resources/Map/Textures)
        var tex = new Dictionary<string, Texture2D>();
        foreach (Texture2D t in Resources.LoadAll<Texture2D>("Map/Textures"))
            tex[t.name.ToLowerInvariant()] = t;

        // everything on the Default layer = grabbable by the locomotion
        bool hasColliders = root.GetComponentInChildren<Collider>() != null;
        foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>())
        {
            mf.gameObject.layer = 0;
            if (!hasColliders && mf.sharedMesh != null && mf.GetComponent<Collider>() == null)
                mf.gameObject.AddComponent<MeshCollider>().sharedMesh = mf.sharedMesh;
        }

        // re-link textures by material name if the importer didn't do it
        foreach (Renderer r in root.GetComponentsInChildren<Renderer>())
        {
            Material[] mats = r.materials;
            for (int i = 0; i < mats.Length; i++)
            {
                Material m = mats[i];
                if (m == null || m.mainTexture != null) continue;
                string key = BaseName(m.name);
                string alias;
                if (TextureAlias.TryGetValue(key, out alias)) key = alias;
                Texture2D t;
                if (tex.TryGetValue(key, out t)) m.mainTexture = t;
            }
            r.materials = mats;
        }
    }

    // "bark.001 (Instance)" -> "bark"
    static string BaseName(string n)
    {
        n = n.Replace(" (Instance)", "").ToLowerInvariant();
        int dot = n.LastIndexOf('.');
        if (dot > 0)
        {
            int num;
            if (int.TryParse(n.Substring(dot + 1), out num)) n = n.Substring(0, dot);
        }
        return n;
    }

    // ---------------- stump spawn ----------------

    Transform FindByName(string lowerName, bool contains)
    {
        if (mapRoot == null) return null;
        foreach (Transform t in mapRoot.GetComponentsInChildren<Transform>(true))
        {
            string n = t.name.ToLowerInvariant();
            if (contains ? n.Contains(lowerName) : n == lowerName) return t;
        }
        return null;
    }

    static bool BoundsOf(Transform t, out Bounds b)
    {
        b = new Bounds(t.position, Vector3.zero);
        Renderer[] rs = t.GetComponentsInChildren<Renderer>();
        if (rs.Length == 0) return false;
        b = rs[0].bounds;
        for (int i = 1; i < rs.Length; i++) b.Encapsulate(rs[i].bounds);
        return true;
    }

    // Players start at the stump: the area around the in-map computer. Falls back to the middle of the map.
    Vector3 FindSpawn()
    {
        Transform comp = FindByName("gorillacomputer", true);
        if (comp == null) comp = FindByName("computer", true);
        if (comp != null)
        {
            Bounds cb;
            Vector3 c = BoundsOf(comp, out cb) ? cb.center : comp.position;
            for (int i = 0; i < 80; i++)
            {
                float ang = i * 2.4f;
                float rad = 1.2f + i * 0.06f;
                Vector3 o = new Vector3(c.x + Mathf.Cos(ang) * rad, c.y + 3f, c.z + Mathf.Sin(ang) * rad);
                RaycastHit hit;
                if (Physics.Raycast(o, Vector3.down, out hit, 8f, 1 << 0, QueryTriggerInteraction.Ignore) && hit.normal.y > 0.7f)
                {
                    Debug.Log("[GtagTest] Spawning at the stump next to " + comp.name);
                    return hit.point;
                }
            }
        }
        Debug.LogWarning("[GtagTest] No computer/stump object found in the map - spawning in the middle of the map.");
        return FindGround(0);
    }

    // ---------------- leaderboard board ----------------

    static MeshFilter LargestMesh(Transform t)
    {
        MeshFilter best = null;
        float bestVol = -1f;
        foreach (MeshFilter mf in t.GetComponentsInChildren<MeshFilter>())
        {
            if (mf.sharedMesh == null) continue;
            Vector3 sz = mf.sharedMesh.bounds.size;
            Vector3 ls = mf.transform.lossyScale;
            float vol = Mathf.Abs(sz.x * ls.x) + Mathf.Abs(sz.y * ls.y) + Mathf.Abs(sz.z * ls.z);
            if (vol > bestVol) { bestVol = vol; best = mf; }
        }
        return best;
    }

    // Put the leaderboard on the map's scoreboard, lying flat against its face whatever way the board is turned.
    void BuildBoard()
    {
        Transform b = FindByName("scoreboard", false);
        if (b == null) b = FindByName("board", false);
        if (b == null) b = FindByName("scoreboard", true);

        Vector3 pos;
        Quaternion rot;
        float scale = 0.003f;
        MeshFilter mf = b != null ? LargestMesh(b) : null;
        if (mf != null)
        {
            Transform t = mf.transform;
            Bounds lb = mf.sharedMesh.bounds;
            Vector3 ls = t.lossyScale;
            float[] size = { lb.size.x * Mathf.Abs(ls.x), lb.size.y * Mathf.Abs(ls.y), lb.size.z * Mathf.Abs(ls.z) };
            Vector3[] axis = { t.right, t.up, t.forward };

            int thin = 0;
            if (size[1] < size[thin]) thin = 1;
            if (size[2] < size[thin]) thin = 2;

            Vector3 center = t.TransformPoint(lb.center);
            Vector3 normal = axis[thin].normalized;
            if (Vector3.Dot(SpawnPoint - center, normal) < 0f) normal = -normal;   // the side that faces the spawn

            // the more vertical of the other two axes is the board's height
            int o1 = (thin + 1) % 3, o2 = (thin + 2) % 3;
            int hAx = Mathf.Abs(Vector3.Dot(axis[o1], Vector3.up)) >= Mathf.Abs(Vector3.Dot(axis[o2], Vector3.up)) ? o1 : o2;
            int wAx = hAx == o1 ? o2 : o1;
            Vector3 up = axis[hAx];
            if (Vector3.Dot(up, Vector3.up) < 0f) up = -up;

            scale = Mathf.Clamp(Mathf.Min(size[wAx] / 400f, size[hAx] / 420f), 0.0015f, 0.008f);
            pos = center + normal * (size[thin] * 0.5f + 0.02f);
            rot = Quaternion.LookRotation(-normal, up);
            Debug.Log("[GtagTest] Leaderboard placed on '" + b.name + "' (size " + size[0].ToString("F2") + " x " + size[1].ToString("F2") + " x " + size[2].ToString("F2") + ")");
        }
        else
        {
            Vector3 toCenter = MapBounds.center - SpawnPoint; toCenter.y = 0f;
            if (toCenter.sqrMagnitude < 1f) toCenter = Vector3.forward;
            toCenter.Normalize();
            pos = SpawnPoint + toCenter * 3f + Vector3.up * 1.6f;
            rot = Quaternion.LookRotation(-toCenter, Vector3.up);
            Debug.LogWarning("[GtagTest] No scoreboard object found in the map - leaderboard placed near the spawn.");
        }
        LeaderboardBoard.Create(this, pos, rot, scale);
    }

    // ---------------- gazebo / monster home ----------------

    Transform FindGazebo()
    {
        Transform t = FindByName("gazebo", true);
        if (t == null) t = FindByName("campgroundstructure", true);   // the roofed camp structure with the picnic tables
        return t;
    }

    // A floor spot (not under a roof, branch or tree) around the gazebo. Used as the monster's home and respawn.
    public Vector3 MonsterHome()
    {
        Transform g = FindGazebo();
        if (g != null)
        {
            Bounds gb;
            BoundsOf(g, out gb);
            float baseY = gb.min.y;
            float inner = Mathf.Max(gb.extents.x, gb.extents.z) + 2f;
            for (int i = 0; i < 120; i++)
            {
                float ang = Random.Range(0f, Mathf.PI * 2f);
                float rad = inner + Random.Range(0f, 10f);
                Vector3 o = new Vector3(gb.center.x + Mathf.Cos(ang) * rad, gb.max.y + 5f, gb.center.z + Mathf.Sin(ang) * rad);
                RaycastHit hit;
                if (!Physics.Raycast(o, Vector3.down, out hit, gb.size.y + 40f, 1 << 0, QueryTriggerInteraction.Ignore)) continue;
                if (hit.normal.y < 0.8f) continue;
                if (hit.point.y > baseY + 2.5f) continue;        // that is a roof/branch, not the forest floor
                if (Physics.Raycast(hit.point + Vector3.up * 0.1f, Vector3.up, 6f, 1 << 0, QueryTriggerInteraction.Ignore)) continue;   // something overhead
                return hit.point;
            }
        }
        return FindForestFloor();
    }

    // Random open ground with nothing above it.
    public Vector3 FindForestFloor()
    {
        for (int i = 0; i < 60; i++)
        {
            Vector3 g = FindGround(1);
            if (!Physics.Raycast(g + Vector3.up * 0.1f, Vector3.up, 6f, 1 << 0, QueryTriggerInteraction.Ignore)) return g;
        }
        return FindGround(1);
    }

    // ---------------- monster death ----------------

    void BuildOverlay()
    {
        var go = new GameObject("DeathOverlay");
        go.transform.SetParent(Cam.transform, false);
        go.transform.localPosition = new Vector3(0f, 0f, 0.25f);
        go.layer = PlayerLayer;
        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(1600f, 1600f);
        rt.localScale = Vector3.one * 0.0004f;

        var imgGo = new GameObject("Red", typeof(RectTransform));
        imgGo.transform.SetParent(go.transform, false);
        imgGo.GetComponent<RectTransform>().sizeDelta = new Vector2(1600f, 1600f);
        overlay = imgGo.AddComponent<Image>();
        overlay.color = new Color(0.6f, 0f, 0f, 0f);
        overlay.raycastTarget = false;

        var tGo = new GameObject("Text", typeof(RectTransform));
        tGo.transform.SetParent(go.transform, false);
        tGo.GetComponent<RectTransform>().sizeDelta = new Vector2(900f, 200f);
        overlayText = tGo.AddComponent<Text>();
        Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (f == null) f = Font.CreateDynamicFontFromOSFont("Arial", 60);
        overlayText.font = f;
        overlayText.fontSize = 110;
        overlayText.fontStyle = FontStyle.Bold;
        overlayText.alignment = TextAnchor.MiddleCenter;
        overlayText.text = "YOU DIED";
        overlayText.color = new Color(1f, 1f, 1f, 0f);
        overlayText.raycastTarget = false;
        go.SetActive(false);
    }

    public void Die()
    {
        if (IsDying) return;
        StartCoroutine(DieRoutine());
    }

    IEnumerator DieRoutine()
    {
        IsDying = true;
        Player.locked = true;                    // caught: no swinging, no sliding, until you are back at the stump
        Body.velocity = Vector3.zero;
        Scores.AddDeath(RunTime);

        overlay.transform.parent.gameObject.SetActive(true);
        for (float t = 0f; t < 0.4f; t += Time.deltaTime)
        {
            SetOverlay(t / 0.4f);
            yield return null;
        }
        SetOverlay(1f);

        TeleportToSpawn();                       // back to the stump
        if (Monster != null && PlayerProfile.MonsterOn) Monster.Respawn();
        yield return new WaitForSeconds(1.2f);

        for (float t = 0f; t < 0.6f; t += Time.deltaTime)
        {
            SetOverlay(1f - t / 0.6f);
            yield return null;
        }
        overlay.transform.parent.gameObject.SetActive(false);
        ResyncPlayer();                          // fresh start: no leftover swing speed from before the death
        Player.locked = false;
        runStart = Time.time;
        IsDying = false;
    }

    void SetOverlay(float a)
    {
        overlay.color = new Color(0.6f, 0f, 0f, 0.9f * a);
        overlayText.color = new Color(1f, 1f, 1f, a);
    }

    // ---------------- ground sampling / teleport ----------------

    // attempt 0 = middle of the map, higher attempts = random spots
    public Vector3 FindGround(int attempt)
    {
        Bounds b = MapBounds;
        for (int i = 0; i < 300; i++)
        {
            float spread = attempt == 0 ? Mathf.Min(1f, i / 100f) : 1f;
            Vector3 p = b.center;
            p.x += Random.Range(-b.extents.x, b.extents.x) * spread;
            p.z += Random.Range(-b.extents.z, b.extents.z) * spread;
            if (attempt == 0 && i == 0) { p.x = b.center.x; p.z = b.center.z; }

            RaycastHit hit;
            Vector3 origin = new Vector3(p.x, b.max.y + 50f, p.z);
            if (Physics.Raycast(origin, Vector3.down, out hit, b.size.y + 120f, 1 << 0, QueryTriggerInteraction.Ignore)
                && hit.normal.y > 0.7f)
                return hit.point;
        }
        return new Vector3(b.center.x, b.min.y + 1f, b.center.z);
    }

    public void TeleportToGround(Vector3 ground)
    {
        Vector3 rig = Player.transform.position;
        Vector3 delta = new Vector3(ground.x - Head.position.x, (ground.y + 0.2f) - rig.y, ground.z - Head.position.z);
        MoveRig(rig + delta);
    }

    public void TeleportToSpawn() { TeleportToGround(SpawnPoint); }

    public void MoveRig(Vector3 newRigPosition)
    {
        Player.transform.position = newRigPosition;
        Body.position = newRigPosition;
        Body.velocity = Vector3.zero;
        Physics.SyncTransforms();
        ResyncPlayer();
    }

    public void ResyncPlayer()
    {
        Player.leftHandFollower.position = Player.leftHandTransform.position;
        Player.rightHandFollower.position = Player.rightHandTransform.position;
        Player.InitializeValues();
    }

    // ---------------- player rig ----------------

    void BuildPlayer(Vector3 spawn)
    {
        // built inactive so the Player component's Awake runs only after every field is assigned
        var playerGo = new GameObject("GorillaPlayer");
        playerGo.SetActive(false);
        playerGo.layer = PlayerLayer;
        playerGo.transform.position = spawn + Vector3.up * 0.2f;

        Body = playerGo.AddComponent<Rigidbody>();
        Body.mass = 1f;
        Body.useGravity = true;
        Body.interpolation = RigidbodyInterpolation.Interpolate;
        Body.constraints = RigidbodyConstraints.FreezeRotation;
        Body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;

        // head / camera
        var camGo = new GameObject("Main Camera");
        camGo.tag = "MainCamera";
        camGo.layer = PlayerLayer;
        camGo.transform.SetParent(playerGo.transform, false);
        camGo.transform.localPosition = new Vector3(0f, 1.6f, 0f);
        Cam = camGo.AddComponent<Camera>();
        Cam.nearClipPlane = 0.05f;
        Cam.cullingMask = ~(1 << HiddenLayer);
        Cam.farClipPlane = 1500f;
        Cam.clearFlags = CameraClearFlags.SolidColor;
        Cam.backgroundColor = new Color(0.45f, 0.62f, 0.85f);
        camGo.AddComponent<AudioListener>();
        HeadCollider = camGo.AddComponent<SphereCollider>();
        HeadCollider.radius = 0.15f;

        // body capsule hangs under the head (Player.cs keeps it upright)
        var bodyGo = new GameObject("Body Collider");
        bodyGo.layer = PlayerLayer;
        bodyGo.transform.SetParent(camGo.transform, false);
        bodyGo.transform.localPosition = new Vector3(0f, -0.24f, -0.06f);
        BodyCollider = bodyGo.AddComponent<CapsuleCollider>();
        BodyCollider.radius = 0.15f;
        BodyCollider.height = 0.5f;

        // tracked controller transforms
        LeftHand = new GameObject("LeftHand Controller").transform;
        LeftHand.SetParent(playerGo.transform, false);
        RightHand = new GameObject("RightHand Controller").transform;
        RightHand.SetParent(playerGo.transform, false);
        LeftHand.localPosition = new Vector3(-0.25f, 1.2f, 0.3f);
        RightHand.localPosition = new Vector3(0.25f, 1.2f, 0.3f);

        // visible hand spheres (these are what actually collide, via Player.cs spherecasts)
        Transform leftFollower = leftFollowerT = MakeFollower("LeftHand Follower", playerGo.transform);
        Transform rightFollower = rightFollowerT = MakeFollower("RightHand Follower", playerGo.transform);
        leftFollower.position = LeftHand.position;
        rightFollower.position = RightHand.position;

        // Player.cs with the same tuning values as the original Gorilla Rig prefab
        Player = playerGo.AddComponent<Player>();
        Player.enabled = false;
        Player.headCollider = HeadCollider;
        Player.bodyCollider = BodyCollider;
        Player.leftHandFollower = leftFollower;
        Player.rightHandFollower = rightFollower;
        Player.leftHandTransform = LeftHand;
        Player.rightHandTransform = RightHand;
        Player.velocityHistorySize = 8;
        Player.maxArmLength = 1.5f;
        Player.unStickDistance = 1f;
        Player.velocityLimit = 0.4f;
        Player.maxJumpSpeed = 6.5f;
        Player.jumpMultiplier = 1.1f;
        Player.minimumRaycastDistance = 0.05f;
        Player.defaultSlideFactor = 0.03f;
        Player.defaultPrecision = 0.995f;
        Player.locomotionEnabledLayers = 1 << 0;

        Rig = gameObject.AddComponent<XRRigInput>();
        Rig.head = camGo.transform;
        Rig.leftHand = LeftHand;
        Rig.rightHand = RightHand;
        Rig.player = Player;
        Rig.body = Body;

        playerGo.SetActive(true);   // Awake runs now with everything assigned
    }

    static Transform MakeFollower(string name, Transform parent)
    {
        var s = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        s.name = name;
        DestroyImmediate(s.GetComponent<Collider>());
        s.layer = PlayerLayer;
        s.transform.SetParent(parent, true);
        s.transform.localScale = Vector3.one * 0.1f;
        return s.transform;
    }
}
