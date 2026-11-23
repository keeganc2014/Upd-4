using UnityEngine;

// A standing mirror next to the stump, so you can see your monke model (your own body is hidden from your own camera,
// the mirror camera is the one that draws it).
// It is a real mirror, not a fixed picture: a second camera sits where your eye would be *behind* the glass and looks out
// through the glass, so the reflection follows your head. It only renders while you are near it and not facing away.
[DefaultExecutionOrder(200)]   // after the body models have been posed for this frame
public class StumpMirror : MonoBehaviour
{
    const float HalfW = 0.7f, HalfH = 0.9f;   // the glass is 1.4 m x 1.8 m
    const float SpotDistance = 2.6f;          // how far from the stump spot it stands
    const float ShowRange = 12f;              // further away than this the camera is switched off

    GtagBootstrap boot;
    Camera cam;
    RenderTexture rt;
    Transform glass;                          // glass.forward points INTO the mirror, the player stands on the -forward side
    Renderer glassRenderer;

    public static StumpMirror Create(GtagBootstrap b)
    {
        var go = new GameObject("StumpMirror");
        var m = go.AddComponent<StumpMirror>();
        m.boot = b;
        m.Build();
        return m;
    }

    // ---------------- build ----------------

    // A flat, open spot a few metres from the stump (towards the middle of the map if it can), facing the stump.
    void FindSpot(out Vector3 ground, out Vector3 dir)
    {
        Vector3 spawn = boot.SpawnPoint;
        Vector3 toCenter = boot.MapBounds.center - spawn;
        toCenter.y = 0f;
        toCenter = toCenter.sqrMagnitude < 1f ? Vector3.forward : toCenter.normalized;
        Vector3 start = spawn + Vector3.up * 1.3f;

        for (int i = 0; i < 16; i++)
        {
            int k = (i + 1) / 2;
            float ang = i == 0 ? 0f : (i % 2 == 1 ? 1f : -1f) * k * 22.5f;
            Vector3 d = Quaternion.Euler(0f, ang, 0f) * toCenter;
            RaycastHit hit;
            if (Physics.SphereCast(start, 0.6f, d, out hit, SpotDistance + 1f, 1 << 0, QueryTriggerInteraction.Ignore)) continue;   // something in the way
            Vector3 p = spawn + d * SpotDistance;
            if (!Physics.Raycast(p + Vector3.up * 3f, Vector3.down, out hit, 8f, 1 << 0, QueryTriggerInteraction.Ignore)) continue;   // no floor
            if (hit.normal.y < 0.8f || Mathf.Abs(hit.point.y - spawn.y) > 0.6f) continue;                                          // slope / step
            ground = hit.point;
            dir = d;
            return;
        }
        dir = toCenter;
        ground = spawn + dir * SpotDistance;
        Debug.LogWarning("[GtagTest] No clear spot for the mirror near the stump - placed it in the way.");
    }

    void Build()
    {
        Vector3 ground, dir;
        FindSpot(out ground, out dir);
        transform.position = ground + Vector3.up * (0.25f + HalfH);
        transform.rotation = Quaternion.LookRotation(dir, Vector3.up);   // forward = away from the stump, the glass faces the stump

        // frame, back panel and two legs (on the Default layer, so hands can grab / bump them)
        Color wood = new Color(0.28f, 0.17f, 0.09f), dark = new Color(0.12f, 0.08f, 0.05f);
        float t = 0.09f;
        Box("Top", new Vector3(0f, HalfH + t * 0.5f, -0.02f), new Vector3(2f * HalfW + 2f * t, t, 0.1f), wood);
        Box("Bottom", new Vector3(0f, -HalfH - t * 0.5f, -0.02f), new Vector3(2f * HalfW + 2f * t, t, 0.1f), wood);
        Box("Left", new Vector3(-HalfW - t * 0.5f, 0f, -0.02f), new Vector3(t, 2f * HalfH, 0.1f), wood);
        Box("Right", new Vector3(HalfW + t * 0.5f, 0f, -0.02f), new Vector3(t, 2f * HalfH, 0.1f), wood);
        Box("Back", Vector3.zero, new Vector3(2f * HalfW + 2f * t, 2f * HalfH + 2f * t, 0.06f), dark);
        float legTop = -0.2f, legBottom = -(0.25f + HalfH);   // local heights: up to the panel, down to the floor
        Box("LegL", new Vector3(-0.5f, (legTop + legBottom) * 0.5f, 0.07f), new Vector3(0.08f, legTop - legBottom, 0.08f), dark);
        Box("LegR", new Vector3(0.5f, (legTop + legBottom) * 0.5f, 0.07f), new Vector3(0.08f, legTop - legBottom, 0.08f), dark);

        // the glass: a quad on its own layer (the mirror camera does not draw this layer). The picture is flipped left-right,
        // because the mirror camera looks at the player from the other side.
        var g = new GameObject("Glass");
        g.layer = GtagBootstrap.MirrorLayer;
        g.transform.SetParent(transform, false);
        g.transform.localPosition = new Vector3(0f, 0f, -0.032f);
        glass = g.transform;
        g.AddComponent<MeshFilter>().sharedMesh = QuadMesh();
        glassRenderer = g.AddComponent<MeshRenderer>();
        glassRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        glassRenderer.receiveShadows = false;

        rt = new RenderTexture(448, 576, 16);   // same shape as the glass
        rt.name = "MirrorTexture";
        rt.antiAliasing = 1;
        rt.filterMode = FilterMode.Bilinear;
        rt.Create();
        Material mat = MonkeModel.NewUnlitMaterial(Color.white);
        mat.mainTexture = rt;
        glassRenderer.sharedMaterial = mat;

        // the camera that draws the reflection. It is switched off and drawn by hand, only when needed.
        var cg = new GameObject("MirrorCamera");
        cg.transform.SetParent(transform, false);
        cam = cg.AddComponent<Camera>();
        cam.enabled = false;
        cam.targetTexture = rt;
        cam.cullingMask = ~(1 << GtagBootstrap.MirrorLayer);   // everything, including your own hidden body
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = boot.Cam.backgroundColor;
        cam.farClipPlane = 300f;
        cam.allowHDR = false;
        cam.allowMSAA = false;
        cam.stereoTargetEye = StereoTargetEyeMask.None;        // a plain flat picture, even in a headset
        Debug.Log("[GtagTest] Mirror placed near the stump at " + transform.position);
    }

    void Box(string name, Vector3 localPos, Vector3 size, Color c)
    {
        var b = GameObject.CreatePrimitive(PrimitiveType.Cube);   // has a collider on the Default layer
        b.name = name;
        b.transform.SetParent(transform, false);
        b.transform.localPosition = localPos;
        b.transform.localScale = size;
        b.GetComponent<Renderer>().material.color = c;
    }

    static Mesh QuadMesh()
    {
        var m = new Mesh();
        m.name = "MirrorGlass";
        m.vertices = new[]
        {
            new Vector3(-HalfW, -HalfH, 0f), new Vector3(-HalfW, HalfH, 0f),
            new Vector3(HalfW, HalfH, 0f), new Vector3(HalfW, -HalfH, 0f)
        };
        m.uv = new[] { new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f), new Vector2(0f, 0f) };   // u flipped
        m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        m.RecalculateNormals();
        m.RecalculateBounds();
        return m;
    }

    // ---------------- reflection ----------------

    void LateUpdate()
    {
        if (boot == null || boot.Cam == null || cam == null) return;
        glassRenderer.enabled = PlayerProfile.MirrorOn;      // off = just the dark back panel
        if (!PlayerProfile.MirrorOn) return;

        Vector3 gp = glass.position;
        Vector3 n = -glass.forward;                          // out of the glass, towards the player
        Vector3 eye = boot.Head.position;
        float d = Vector3.Dot(eye - gp, n);                  // how far in front of the glass your eye is
        if (d < 0.1f) return;                                // beside or behind it
        Vector3 toMirror = gp - eye;
        if (toMirror.sqrMagnitude > ShowRange * ShowRange) return;
        if (Vector3.Dot(boot.Head.forward, toMirror.normalized) < -0.25f) return;   // you are looking away

        // the eye as seen in the glass: the same distance behind it
        Vector3 reflectedEye = eye - 2f * d * n;
        Quaternion rot = Quaternion.LookRotation(n, Vector3.up);
        cam.transform.SetPositionAndRotation(reflectedEye, rot);

        // the view is exactly the window of the glass seen from the reflected eye (an off-axis frustum), nearest plane = the glass,
        // so the wall and everything behind the mirror is cut away
        Vector3 right = rot * Vector3.right, up = rot * Vector3.up;
        float l = float.MaxValue, r = float.MinValue, bt = float.MaxValue, tp = float.MinValue;
        for (int i = 0; i < 4; i++)
        {
            Vector3 corner = gp + glass.right * (HalfW * ((i & 1) == 0 ? -1f : 1f)) + glass.up * (HalfH * ((i & 2) == 0 ? -1f : 1f));
            Vector3 a = corner - reflectedEye;
            float x = Vector3.Dot(a, right), y = Vector3.Dot(a, up);
            l = Mathf.Min(l, x); r = Mathf.Max(r, x);
            bt = Mathf.Min(bt, y); tp = Mathf.Max(tp, y);
        }
        cam.projectionMatrix = Frustum(l, r, bt, tp, d, cam.farClipPlane);
        cam.backgroundColor = boot.Cam.backgroundColor;      // follows Darkness
        cam.Render();
    }

    static Matrix4x4 Frustum(float l, float r, float b, float t, float n, float f)
    {
        var m = new Matrix4x4();
        m[0, 0] = 2f * n / (r - l); m[0, 2] = (r + l) / (r - l);
        m[1, 1] = 2f * n / (t - b); m[1, 2] = (t + b) / (t - b);
        m[2, 2] = -(f + n) / (f - n); m[2, 3] = -2f * f * n / (f - n);
        m[3, 2] = -1f;
        return m;
    }

    void OnDestroy()
    {
        if (rt != null) { rt.Release(); Destroy(rt); }
    }
}
