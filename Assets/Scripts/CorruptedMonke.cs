using UnityEngine;

// The monster's body: the same monke.obj the players use, but corrupted.
//  - dark, almost black body with glowing red eyes
//  - random triangles get thrown out of the body, shrunk or blown up, and re-rolled a few times a second
//  - every second or so the whole thing "tears": a horizontal slice of the body slides sideways for a split second
//  - the underside (where the legs would be) is eaten away: triangles shrink and drop like falling pixels
//  - glitch patches in red / magenta / cyan / white flash on random triangles (drawn unlit, so they glow in the dark)
//  - the long arms hang and sway, and reach forward and swing while it chases you
// The head keeps its shape much more than the rest, so it still reads as the same gorilla.
public class CorruptedMonke : MonoBehaviour
{
    public bool chasing;       // set by Monster
    public Light glow;         // the red light on its face, flickers during a tear

    public const float Scale = 1.55f;          // model units -> metres (its head ends up about 2 m high)
    const float TickTime = 1f / 30f;           // mesh updates per second
    const float ActiveRange = 70f;             // no updates when you are further away than this

    Mesh mesh;
    Vector3[] verts, armBuf;
    Color[] cols;
    Vector3[] faceNormal, faceCenter;
    float[] faceSize;          // keeps the big flat triangles from getting a colored patch as often as small ones
    int faceCount;
    float armPose, tickTimer, nextBurst = 1f, burstEnd, tearY, tearH, tearShift;
    Transform eyeL, eyeR;
    Vector3 eyeLBase, eyeRBase;

    // feet = the parent's origin: the model hangs on its arms with the hand tips on the floor
    public static CorruptedMonke Create(Transform parent)
    {
        if (!MonkeModel.Load()) return null;
        var root = new GameObject("CorruptedMonke");
        root.layer = GtagBootstrap.PlayerLayer;
        root.transform.SetParent(parent, false);
        root.transform.localPosition = new Vector3(0f, -MonkeModel.HandTipY * Scale, 0f);
        root.transform.localScale = Vector3.one * Scale;
        var c = root.AddComponent<CorruptedMonke>();
        c.Build();
        return c;
    }

    void Build()
    {
        mesh = MonkeModel.NewMesh();
        verts = (Vector3[])MonkeModel.Positions.Clone();
        armBuf = (Vector3[])MonkeModel.Positions.Clone();
        cols = new Color[verts.Length];
        faceCount = MonkeModel.Triangles.Length / 3;
        faceNormal = new Vector3[faceCount];
        faceCenter = new Vector3[faceCount];
        faceSize = new float[faceCount];
        for (int f = 0; f < faceCount; f++)
        {
            Vector3 a = MonkeModel.Positions[f * 3], b = MonkeModel.Positions[f * 3 + 1], c = MonkeModel.Positions[f * 3 + 2];
            faceCenter[f] = (a + b + c) / 3f;
            Vector3 cross = Vector3.Cross(b - a, c - a);
            faceNormal[f] = cross.normalized;
            faceSize[f] = Mathf.Clamp(0.0012f / Mathf.Max(cross.magnitude * 0.5f, 0.00001f), 0.25f, 1f);
        }
        mesh.colors = cols;

        // dark body (lit)
        var body = new GameObject("Body");
        body.layer = GtagBootstrap.PlayerLayer;
        body.transform.SetParent(transform, false);
        body.AddComponent<MeshFilter>().sharedMesh = mesh;
        var br = body.AddComponent<MeshRenderer>();
        Material bodyMat = MonkeModel.NewMaterial(new Color(0.09f, 0.05f, 0.06f));
        bodyMat.SetFloat("_Glossiness", 0.05f);
        br.sharedMaterial = bodyMat;
        br.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        br.receiveShadows = false;

        // glitch patches: the same mesh again, a hair bigger, unlit, showing only the triangles that have a vertex color
        var patch = new GameObject("Glitch");
        patch.layer = GtagBootstrap.PlayerLayer;
        patch.transform.SetParent(transform, false);
        patch.transform.localScale = Vector3.one * 1.012f;
        patch.AddComponent<MeshFilter>().sharedMesh = mesh;
        var pr = patch.AddComponent<MeshRenderer>();
        pr.sharedMaterial = MonkeModel.NewUnlitMaterial(Color.white);
        pr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        pr.receiveShadows = false;

        eyeL = MakeEye(-0.062f);
        eyeR = MakeEye(0.062f);
        eyeLBase = eyeL.localPosition;
        eyeRBase = eyeR.localPosition;
    }

    // Eye on the front of the face: x, y are measured on the model, z is found on the mesh surface.
    Transform MakeEye(float x)
    {
        float y = 0.555f, z = 0.2f, best = -9f;
        foreach (Vector3 p in MonkeModel.Positions)
            if (Mathf.Abs(p.x - x) < 0.03f && Mathf.Abs(p.y - y) < 0.03f && p.z > best) best = p.z;
        if (best > -9f) z = best;

        var e = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(e.GetComponent<Collider>());
        e.layer = GtagBootstrap.PlayerLayer;
        e.transform.SetParent(transform, false);
        e.transform.localPosition = new Vector3(x, y, z + 0.004f);
        e.transform.localScale = Vector3.one * 0.05f;
        e.GetComponent<Renderer>().sharedMaterial = MonkeModel.NewUnlitMaterial(new Color(1f, 0.05f, 0.05f));
        return e.transform;
    }

    void Update()
    {
        float dt = Time.deltaTime;
        armPose = Mathf.MoveTowards(armPose, chasing ? 1f : 0f, dt * 3f);

        GtagBootstrap b = GtagBootstrap.Instance;
        if (b != null && b.Cam != null && (b.Head.position - transform.position).sqrMagnitude > ActiveRange * ActiveRange) return;

        tickTimer -= dt;
        if (tickTimer > 0f) return;
        tickTimer = TickTime;
        Tick(Time.time);
    }

    void Tick(float t)
    {
        // a tear: for a moment everything glitches harder and one slice of the body slides sideways
        if (t >= nextBurst)
        {
            burstEnd = t + Random.Range(0.08f, 0.22f);
            nextBurst = burstEnd + Random.Range(0.4f, 1.8f);
            tearY = Random.Range(-0.5f, 0.65f);
            tearH = Random.Range(0.03f, 0.10f);
            tearShift = Random.Range(0.06f, 0.18f) * (Random.value < 0.5f ? -1f : 1f);
        }
        bool burst = t < burstEnd;

        // arms: hang and sway, or reach forward and swing while chasing
        float swing = Mathf.Sin(t * 9f);
        Vector3 idleL = MonkeModel.RestHandL + new Vector3(0.03f * Mathf.Sin(t * 1.3f), 0.02f * Mathf.Sin(t * 0.9f), 0f);
        Vector3 idleR = MonkeModel.RestHandR + new Vector3(-0.03f * Mathf.Sin(t * 1.1f), 0.02f * Mathf.Sin(t * 1.0f + 1f), 0f);
        Vector3 reachL = new Vector3(-0.40f, 0.10f, 0.85f + 0.12f * swing);
        Vector3 reachR = new Vector3(0.40f, 0.10f, 0.85f - 0.12f * swing);
        MonkeModel.Deform(armBuf, Vector3.Lerp(idleL, reachL, armPose), Vector3.Lerp(idleR, reachR, armPose));

        int roll = (int)(t * (burst ? 30f : 7f));     // how often the random triangles are re-rolled
        float popBody = burst ? 0.20f : 0.05f;        // share of body triangles thrown out
        float popHead = burst ? 0.06f : 0.01f;        // the head is hit much less
        float litBody = burst ? 0.28f : 0.05f;        // share of triangles showing a glitch color

        for (int f = 0; f < faceCount; f++)
        {
            int i0 = f * 3, i1 = i0 + 1, i2 = i0 + 2;
            Vector3 a = armBuf[i0], b = armBuf[i1], c = armBuf[i2];
            Vector3 center = (a + b + c) / 3f;
            Vector3 bc = faceCenter[f];
            bool head = bc.y > 0.40f;
            bool underside = bc.y < 0.10f && Mathf.Abs(bc.x) < 0.22f;

            // eaten underside: triangles shrink and fall like pixels
            if (underside)
            {
                float d = Mathf.Abs(Mathf.Sin(f * 12.9898f + t * 1.7f));
                float drop = d * d * 0.30f;
                float shrink = 1f - Mathf.Clamp01(drop * 3f) * 0.8f;
                Vector3 down = Vector3.down * drop;
                a = center + (a - center) * shrink + down;
                b = center + (b - center) * shrink + down;
                c = center + (c - center) * shrink + down;
                center += down;
            }

            // thrown-out triangles: pushed along their normal, shrunk or blown up
            bool popped = Hash01(f, roll) < (head ? popHead : popBody);
            if (popped)
            {
                float push = Mathf.Lerp(0.02f, 0.13f, Hash01(f + 7919, roll));
                float sc = Mathf.Lerp(0.35f, 1.5f, Hash01(f + 104729, roll));
                Vector3 n = faceNormal[f] * push;
                a = center + (a - center) * sc + n;
                b = center + (b - center) * sc + n;
                c = center + (c - center) * sc + n;
            }

            // the tear
            if (burst)
            {
                if (Mathf.Abs(a.y - tearY) < tearH) a.x += tearShift;
                if (Mathf.Abs(b.y - tearY) < tearH) b.x += tearShift;
                if (Mathf.Abs(c.y - tearY) < tearH) c.x += tearShift;
            }

            verts[i0] = a; verts[i1] = b; verts[i2] = c;

            // glitch color patch on this triangle (or none)
            float litChance = popped ? 0.6f : (underside ? 0.5f : litBody);
            if (head) litChance *= 0.35f;
            litChance *= faceSize[f];
            Color col = new Color(0f, 0f, 0f, 0f);
            if (Hash01(f + 31337, roll + 5) < litChance)
            {
                float k = Hash01(f + 4242, roll + 9);
                if (k < 0.68f) col = new Color(0.85f, 0.03f, 0.03f, 1f);     // red
                else if (k < 0.87f) col = new Color(0.85f, 0f, 0.65f, 1f);   // magenta
                else if (k < 0.96f) col = new Color(0f, 0.85f, 0.85f, 1f);   // cyan
                else col = Color.white;
            }
            cols[i0] = col; cols[i1] = col; cols[i2] = col;
        }

        mesh.vertices = verts;
        mesh.colors = cols;
        mesh.RecalculateNormals();   // every triangle owns its vertices, so these are exact flat normals
        mesh.RecalculateBounds();

        // eyes flicker during a tear
        float es = burst ? Random.Range(0.3f, 1.6f) : 1f + 0.15f * Mathf.Sin(t * 5f);
        float jitter = burst ? Random.Range(-0.012f, 0.012f) : 0f;
        eyeL.localScale = Vector3.one * (0.05f * es);
        eyeR.localScale = Vector3.one * (0.05f * es);
        eyeL.localPosition = eyeLBase + new Vector3(jitter, 0f, 0f);
        eyeR.localPosition = eyeRBase + new Vector3(jitter, 0f, 0f);
        if (glow != null) glow.intensity = burst ? Random.Range(0.2f, 2.2f) : 1f;
    }

    static float Hash01(int a, int b)
    {
        unchecked
        {
            uint h = (uint)a * 73856093u ^ (uint)b * 19349663u;
            h ^= h >> 13;
            h *= 0x5bd1e995u;
            h ^= h >> 15;
            return (h & 0xFFFFFFu) / 16777216f;
        }
    }

    void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
    }
}
