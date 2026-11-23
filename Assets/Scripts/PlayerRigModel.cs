using System.Collections.Generic;
using UnityEngine;

// The visible gorilla. First choice is monke.obj (Resources/Rig/monke_mesh.txt, see MonkeModel): its arms stretch to your hands.
// If that is missing, Assets/Resources/Rig/GorillaRig.fbx (exported from Gorilla_IK_Rig.blend with Tools/export_rig_fbx.py) is used;
// otherwise a simple built-in gorilla is drawn so the game always works.
// Body follows the head, hands follow the hand spheres, color comes from the player profile.
public class PlayerRigModel : MonoBehaviour
{
    Transform head, leftHand, rightHand;
    readonly List<Renderer> tint = new List<Renderer>();
    Transform armL, armR, modelHandL, modelHandR;
    MonkeSkin skin;
    float yaw;
    const float SkinScale = 0.8f;

    public static PlayerRigModel Create(Transform head, Transform leftFollower, Transform rightFollower)
    {
        var go = new GameObject("PlayerModel");
        go.layer = GtagBootstrap.HiddenLayer;   // your own body is not drawn by your own camera (it blocked the view)
        var m = go.AddComponent<PlayerRigModel>();
        m.head = head; m.leftHand = leftFollower; m.rightHand = rightFollower;
        m.skin = MonkeSkin.Create(go.transform, GtagBootstrap.HiddenLayer, SkinScale);
        if (m.skin != null)
        {
            m.tint.Add(m.skin.Body);
        }
        else
        {
            GameObject prefab = Resources.Load<GameObject>("Rig/GorillaRig");
            if (prefab != null) m.BuildImported(prefab); else m.BuildProcedural();
        }
        foreach (Renderer r in leftFollower.GetComponentsInChildren<Renderer>()) m.tint.Add(r);
        foreach (Renderer r in rightFollower.GetComponentsInChildren<Renderer>()) m.tint.Add(r);
        m.ApplyColor();
        PlayerProfile.Changed += m.ApplyColor;
        return m;
    }

    void OnDestroy() { PlayerProfile.Changed -= ApplyColor; }

    void BuildImported(GameObject prefab)
    {
        GameObject inst = Instantiate(prefab, transform);
        inst.transform.localPosition = new Vector3(0f, -0.55f, -0.1f);
        inst.transform.localRotation = Quaternion.identity;
        foreach (Collider c in inst.GetComponentsInChildren<Collider>()) Destroy(c);
        foreach (Camera c in inst.GetComponentsInChildren<Camera>()) Destroy(c);
        foreach (Light l in inst.GetComponentsInChildren<Light>()) Destroy(l);
        foreach (Transform t in inst.GetComponentsInChildren<Transform>(true))
        {
            t.gameObject.layer = GtagBootstrap.HiddenLayer;
            string n = t.name.ToLowerInvariant();
            if (!n.Contains("hand")) continue;
            bool left = n.EndsWith(".l") || n.EndsWith("_l") || n.Contains("left");
            bool right = n.EndsWith(".r") || n.EndsWith("_r") || n.Contains("right");
            if (left && modelHandL == null) modelHandL = t;
            else if (right && modelHandR == null) modelHandR = t;
        }
        foreach (Renderer r in inst.GetComponentsInChildren<Renderer>())
            foreach (Material m in r.materials)
                if (m != null && m.HasProperty("_Color")) { tint.Add(r); break; }
    }

    void BuildProcedural()
    {
        Part(PrimitiveType.Sphere, new Vector3(0f, -0.42f, -0.05f), new Vector3(0.4f, 0.48f, 0.3f), transform);
        armL = Part(PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.09f, 0.5f, 0.09f), transform);
        armR = Part(PrimitiveType.Cylinder, Vector3.zero, new Vector3(0.09f, 0.5f, 0.09f), transform);
    }

    Transform Part(PrimitiveType type, Vector3 localPos, Vector3 scale, Transform parent)
    {
        var p = GameObject.CreatePrimitive(type);
        Destroy(p.GetComponent<Collider>());
        p.layer = GtagBootstrap.HiddenLayer;
        p.transform.SetParent(parent, false);
        p.transform.localPosition = localPos;
        p.transform.localScale = scale;
        tint.Add(p.GetComponent<Renderer>());
        return p.transform;
    }

    void ApplyColor()
    {
        Color c = PlayerProfile.Color;
        foreach (Renderer r in tint)
            if (r != null) r.material.color = c;
    }

    void LateUpdate()
    {
        if (head == null) return;
        Vector3 fwd = head.forward; fwd.y = 0f;
        if (fwd.sqrMagnitude > 0.01f)
            yaw = Mathf.LerpAngle(yaw, Mathf.Atan2(fwd.x, fwd.z) * Mathf.Rad2Deg, 10f * Time.deltaTime);
        transform.SetPositionAndRotation(head.position, Quaternion.Euler(0f, yaw, 0f));
        if (skin != null) skin.Pose(head.position, transform.rotation, leftHand.position, rightHand.position);

        if (modelHandL != null) modelHandL.position = leftHand.position;
        if (modelHandR != null) modelHandR.position = rightHand.position;
        if (armL != null) Stretch(armL, transform.TransformPoint(-0.22f, -0.22f, -0.05f), leftHand.position);
        if (armR != null) Stretch(armR, transform.TransformPoint(0.22f, -0.22f, -0.05f), rightHand.position);
    }

    static void Stretch(Transform arm, Vector3 from, Vector3 to)
    {
        Vector3 d = to - from;
        float len = d.magnitude;
        if (len < 0.001f) return;
        arm.position = from + d * 0.5f;
        arm.rotation = Quaternion.FromToRotation(Vector3.up, d / len);
        arm.localScale = new Vector3(0.09f, len * 0.5f, 0.09f);
    }
}
