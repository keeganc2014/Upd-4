using System.Globalization;
using UnityEngine;

// monke.obj, stored as plain triangles in Assets/Resources/Rig/monke_mesh.txt (one triangle per line, 9 numbers).
// It is read here instead of imported, so it never depends on the model importer, and the mesh can be changed at runtime.
// The model faces +Z, its head is at the top, and its two long arms hang down at the sides (no legs).
public static class MonkeModel
{
    public const string Resource = "Rig/monke_mesh";

    // measured from the mesh (model units)
    public static readonly Vector3 HeadCenter = new Vector3(0f, 0.56f, 0.03f);
    public static readonly Vector3 RestHandL = new Vector3(-0.391f, -0.601f, -0.047f);
    public static readonly Vector3 RestHandR = new Vector3(0.391f, -0.601f, -0.047f);
    public const float HandTipY = -0.658f;   // lowest point of the model

    public static readonly Vector3 ShoulderL = new Vector3(-0.31f, 0.18f, -0.03f);   // where each arm pivots
    public static readonly Vector3 ShoulderR = new Vector3(0.31f, 0.18f, -0.03f);

    public static Vector3[] Positions;       // 3 per triangle, never edited
    public static int[] Triangles;           // 0,1,2,3,4,5... (every triangle owns its 3 vertices = flat shading)

    // Arm skinning data. Every vertex of an arm is stored relative to its shoulder: how far along the arm it is and how far to
    // the side of the arm's axis. Deform() then swings the arm round the shoulder to point at the hand and stretches it
    // lengthways, so the arm keeps its thickness however far the hand goes.
    static float[] armMask;                  // 0 = body/head, 1 = fully follows the arm, in between = shoulder blend
    static float[] armAlong;
    static Vector3[] armPerp;
    static Vector3 dirRestL, dirRestR;
    static float lenRestL, lenRestR;

    static bool tried;
    static Material template;

    public static bool Load()
    {
        if (Positions != null) return true;
        if (tried) return false;
        tried = true;

        TextAsset ta = Resources.Load<TextAsset>(Resource);
        if (ta == null)
        {
            Debug.LogWarning("[GtagTest] Assets/Resources/" + Resource + ".txt not found - monke.obj model not available.");
            return false;
        }

        string[] lines = ta.text.Split('\n');
        var pos = new System.Collections.Generic.List<Vector3>(4500);
        foreach (string line in lines)
        {
            string[] t = line.Trim().Split(' ');
            if (t.Length < 9) continue;
            for (int k = 0; k < 3; k++)
            {
                pos.Add(new Vector3(
                    float.Parse(t[k * 3], CultureInfo.InvariantCulture),
                    float.Parse(t[k * 3 + 1], CultureInfo.InvariantCulture),
                    float.Parse(t[k * 3 + 2], CultureInfo.InvariantCulture)));
            }
        }
        if (pos.Count < 3) return false;

        Positions = pos.ToArray();
        Triangles = new int[Positions.Length];
        for (int i = 0; i < Triangles.Length; i++) Triangles[i] = i;

        Vector3 aL = RestHandL - ShoulderL, aR = RestHandR - ShoulderR;
        lenRestL = aL.magnitude; dirRestL = aL / lenRestL;
        lenRestR = aR.magnitude; dirRestR = aR / lenRestR;

        // Vertices at the same position get the same numbers, so the mesh never tears.
        armMask = new float[Positions.Length];
        armAlong = new float[Positions.Length];
        armPerp = new Vector3[Positions.Length];
        for (int i = 0; i < Positions.Length; i++)
        {
            Vector3 p = Positions[i];
            float my = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.30f, 0.05f, p.y));                // above the shoulder: stays put
            float mx = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.10f, 0.21f, Mathf.Abs(p.x)));     // near the chest: stays put
            armMask[i] = my * mx;
            bool left = p.x < 0f;
            Vector3 rel = p - (left ? ShoulderL : ShoulderR);
            Vector3 d = left ? dirRestL : dirRestR;
            armAlong[i] = Vector3.Dot(rel, d);
            armPerp[i] = rel - d * armAlong[i];
        }
        return true;
    }

    // Swing both arms so the hands end up at handL / handR (model space, the stretched arm never gets shorter than a quarter).
    public static void Deform(Vector3[] outV, Vector3 handL, Vector3 handR)
    {
        Vector3 toL = handL - ShoulderL, toR = handR - ShoulderR;
        Quaternion qL = toL.sqrMagnitude > 0.0001f ? Quaternion.FromToRotation(dirRestL, toL.normalized) : Quaternion.identity;
        Quaternion qR = toR.sqrMagnitude > 0.0001f ? Quaternion.FromToRotation(dirRestR, toR.normalized) : Quaternion.identity;
        float kL = Mathf.Max(0.25f, toL.magnitude / lenRestL);
        float kR = Mathf.Max(0.25f, toR.magnitude / lenRestR);

        for (int i = 0; i < Positions.Length; i++)
        {
            float m = armMask[i];
            if (m <= 0f) { outV[i] = Positions[i]; continue; }
            Vector3 p = Positions[i];
            Vector3 bone;
            if (p.x < 0f) bone = ShoulderL + qL * (armPerp[i] + dirRestL * (armAlong[i] * kL));
            else bone = ShoulderR + qR * (armPerp[i] + dirRestR * (armAlong[i] * kR));
            outV[i] = Vector3.Lerp(p, bone, m);
        }
    }

    public static Mesh NewMesh()
    {
        var m = new Mesh();
        m.name = "Monke";
        m.MarkDynamic();
        m.vertices = (Vector3[])Positions.Clone();
        m.triangles = Triangles;
        m.RecalculateNormals();   // every triangle has its own vertices, so these are exact flat normals
        m.RecalculateBounds();
        return m;
    }

    // A lit material cloned from the one Unity gives primitives (so the shader is always included in the build).
    public static Material NewMaterial(Color c)
    {
        if (template == null)
        {
            var p = GameObject.CreatePrimitive(PrimitiveType.Cube);
            template = p.GetComponent<Renderer>().sharedMaterial;
            Object.Destroy(p);
        }
        var m = new Material(template);
        m.color = c;
        return m;
    }

    // Flat bright material that ignores lights and uses vertex colors (Sprites/Default is always in builds).
    public static Material NewUnlitMaterial(Color c)
    {
        Shader s = Shader.Find("Sprites/Default");
        if (s == null) return NewMaterial(c);
        var m = new Material(s);
        m.color = c;
        return m;
    }
}

// Draws monke.obj as a player body: the head sits on the player's head, the body turns with them,
// and the two arms swing and stretch to the hand positions.
public class MonkeSkin : MonoBehaviour
{
    Mesh mesh;
    Vector3[] verts;
    MeshRenderer rend;
    float scale = 1f;
    Vector3 lastL = new Vector3(9999f, 0f, 0f), lastR = new Vector3(9999f, 0f, 0f);

    public Renderer Body { get { return rend; } }

    // Returns null when the mesh file is missing (callers then use their old shapes).
    public static MonkeSkin Create(Transform parent, int layer, float scale)
    {
        if (!MonkeModel.Load()) return null;
        var go = new GameObject("Monke");
        go.layer = layer;
        go.transform.SetParent(parent, false);
        var s = go.AddComponent<MonkeSkin>();
        s.scale = scale;
        s.mesh = MonkeModel.NewMesh();
        s.verts = (Vector3[])MonkeModel.Positions.Clone();
        go.AddComponent<MeshFilter>().sharedMesh = s.mesh;
        s.rend = go.AddComponent<MeshRenderer>();
        s.rend.sharedMaterial = MonkeModel.NewMaterial(Color.white);
        s.rend.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        s.rend.receiveShadows = false;
        go.transform.localScale = Vector3.one * scale;
        return s;
    }

    // headWorld = where the player's head is, yaw = which way the body faces (turned around Y only),
    // handL / handR = world positions of the hands.
    public void Pose(Vector3 headWorld, Quaternion yaw, Vector3 handL, Vector3 handR)
    {
        Transform t = transform;
        t.SetPositionAndRotation(headWorld - yaw * (MonkeModel.HeadCenter * scale), yaw);

        Vector3 hl = t.InverseTransformPoint(handL);
        Vector3 hr = t.InverseTransformPoint(handR);
        if ((hl - lastL).sqrMagnitude < 0.000001f && (hr - lastR).sqrMagnitude < 0.000001f) return;
        lastL = hl;
        lastR = hr;

        MonkeModel.Deform(verts, hl, hr);
        mesh.vertices = verts;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
    }

    void OnDestroy()
    {
        if (mesh != null) Destroy(mesh);
    }
}
