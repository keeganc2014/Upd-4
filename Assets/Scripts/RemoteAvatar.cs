using UnityEngine;

// What you see of another player: a gorilla made of simple shapes, tinted with their color, with a name tag.
// The owner gets a gold OWNER title above the name.
public class RemoteAvatar : MonoBehaviour
{
    public float lastUpdate;

    Vector3 headPos, lPos, rPos;
    Quaternion headRot = Quaternion.identity;
    bool first = true;
    Transform head, body, handL, handR, armL, armR, nameTag;
    TextMesh tagText;
    Renderer[] tintRenderers;
    MonkeSkin skin;
    const float SkinScale = 0.8f;
    int shownName = -1, shownColor = -1;
    bool shownOwner;

    public Vector3 HeadPosition { get { return head != null ? head.position : headPos; } }

    public static RemoteAvatar Create(ulong id)
    {
        var go = new GameObject("Player_" + id);
        go.layer = GtagBootstrap.PlayerLayer;
        var a = go.AddComponent<RemoteAvatar>();
        a.Build();
        return a;
    }

    void Build()
    {
        head = Part(PrimitiveType.Sphere, new Vector3(0.28f, 0.26f, 0.28f));
        body = Part(PrimitiveType.Sphere, new Vector3(0.4f, 0.5f, 0.3f));
        handL = Part(PrimitiveType.Sphere, Vector3.one * 0.1f);
        handR = Part(PrimitiveType.Sphere, Vector3.one * 0.1f);
        armL = Part(PrimitiveType.Cylinder, new Vector3(0.07f, 0.4f, 0.07f));
        armR = Part(PrimitiveType.Cylinder, new Vector3(0.07f, 0.4f, 0.07f));
        tintRenderers = GetComponentsInChildren<Renderer>();
        skin = MonkeSkin.Create(transform, GtagBootstrap.PlayerLayer, SkinScale);
        if (skin != null)
        {
            // the monke.obj body replaces the simple shapes (they stay as invisible trackers)
            foreach (Renderer r in tintRenderers) r.enabled = false;
            tintRenderers = new Renderer[] { skin.Body };
        }

        var tagGo = new GameObject("NameTag");
        tagGo.transform.SetParent(transform, false);
        nameTag = tagGo.transform;
        Font f = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        if (f == null) f = Resources.GetBuiltinResource<Font>("Arial.ttf");
        if (f == null) f = Font.CreateDynamicFontFromOSFont("Arial", 32);
        tagText = tagGo.AddComponent<TextMesh>();
        tagText.font = f;
        tagGo.GetComponent<MeshRenderer>().sharedMaterial = f.material;
        tagText.fontSize = 64;
        tagText.characterSize = 0.04f;
        tagText.anchor = TextAnchor.LowerCenter;
        tagText.alignment = TextAlignment.Center;
        tagText.richText = true;
        tagGo.layer = GtagBootstrap.PlayerLayer;
    }

    Transform Part(PrimitiveType type, Vector3 scale)
    {
        var p = GameObject.CreatePrimitive(type);
        Destroy(p.GetComponent<Collider>());
        p.layer = GtagBootstrap.PlayerLayer;
        p.transform.SetParent(transform, false);
        p.transform.localScale = scale;
        return p.transform;
    }

    public void SetPose(Vector3 h, Quaternion hr, Vector3 l, Vector3 r, int colorIdx, int nameIdx, bool owner)
    {
        headPos = h; headRot = hr; lPos = l; rPos = r;
        lastUpdate = Time.time;
        if (first)
        {
            first = false;
            head.position = h; handL.position = l; handR.position = r;
            head.rotation = hr;
        }
        if (colorIdx != shownColor || nameIdx != shownName || owner != shownOwner)
        {
            shownColor = colorIdx; shownName = nameIdx; shownOwner = owner;
            Color c = PlayerProfile.Colors[Mathf.Clamp(colorIdx, 0, PlayerProfile.Colors.Length - 1)];
            foreach (Renderer rd in tintRenderers) rd.material.color = c;
            string n = PlayerProfile.Names[Mathf.Clamp(nameIdx, 0, PlayerProfile.Names.Length - 1)];
            tagText.text = owner ? "<color=#FFD700><b>OWNER</b></color>\n" + n : n;
        }
    }

    void LateUpdate()
    {
        if (first) return;
        float k = 1f - Mathf.Exp(-18f * Time.deltaTime);   // smooth between the ~15 updates per second
        head.position = Vector3.Lerp(head.position, headPos, k);
        head.rotation = Quaternion.Slerp(head.rotation, headRot, k);
        handL.position = Vector3.Lerp(handL.position, lPos, k);
        handR.position = Vector3.Lerp(handR.position, rPos, k);

        Vector3 fwd = head.forward; fwd.y = 0f;
        Quaternion yaw = fwd.sqrMagnitude > 0.01f ? Quaternion.LookRotation(fwd) : Quaternion.identity;
        body.position = head.position + yaw * new Vector3(0f, -0.42f, -0.06f);
        body.rotation = yaw;

        Stretch(armL, head.position + yaw * new Vector3(-0.22f, -0.2f, -0.05f), handL.position);
        Stretch(armR, head.position + yaw * new Vector3(0.22f, -0.2f, -0.05f), handR.position);
        if (skin != null) skin.Pose(head.position, yaw, handL.position, handR.position);

        // name tag above the head, turned towards whoever is looking
        nameTag.position = head.position + Vector3.up * 0.3f;
        GtagBootstrap gb = GtagBootstrap.Instance;
        if (gb != null && gb.Cam != null) nameTag.rotation = Quaternion.LookRotation(nameTag.position - gb.Cam.transform.position, Vector3.up);
    }

    static void Stretch(Transform arm, Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        float len = d.magnitude;
        if (len < 0.001f) return;
        arm.position = from + d * 0.5f;
        arm.rotation = Quaternion.FromToRotation(Vector3.up, d / len);
        arm.localScale = new Vector3(0.07f, len * 0.5f, 0.07f);
    }
}
