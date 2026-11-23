using UnityEngine;
using UnityEngine.XR;
using GorillaLocomotion;

// Reads head/controller poses + buttons straight from Unity's XR input API (no extra packages needed),
// and falls back to keyboard/mouse when no headset is running (so you can test in the editor).
[DefaultExecutionOrder(-100)]
public class XRRigInput : MonoBehaviour
{
    public Transform head;       // the camera
    public Transform leftHand;   // tracked controller transforms (children of the player)
    public Transform rightHand;
    public Player player;
    public Rigidbody body;

    public float turnSpeed = 120f;      // overridden by the saved Settings > Turn value
    public float ArmScale = 1f;         // "Long Arms" mod

    public bool XRActive { get; private set; }
    public bool MenuTogglePressed { get; private set; }  // true for one frame
    public bool ClickPressed { get; private set; }       // true for one frame
    public bool FlyHeld { get; private set; }
    public bool LeftGrip { get; private set; }
    public bool RightGrip { get; private set; }

    float lastArmScale = 1f;
    InputDevice headDev, leftDev, rightDev;
    bool prevMenu, prevClick, originSet;
    float yaw, pitch;
    const float DesktopHeadHeight = 1.6f;

    void OnEnable() { Application.onBeforeRender += OnBeforeRender; }
    void OnDisable() { Application.onBeforeRender -= OnBeforeRender; }

    void Update()
    {
        MenuTogglePressed = false;
        ClickPressed = false;

        XRActive = XRSettings.isDeviceActive;
        if (XRActive) UpdateXR();
        else UpdateDesktop();

        // A new arm length moves the hands instantly. Tell the locomotion about it so it doesn't read that as a huge push.
        if (!Mathf.Approximately(ArmScale, lastArmScale))
        {
            lastArmScale = ArmScale;
            player.leftHandFollower.position = player.leftHandTransform.position;
            player.rightHandFollower.position = player.rightHandTransform.position;
            player.InitializeValues();
            body.velocity = Vector3.zero;
        }
    }

    // ---------------- XR ----------------

    void Refresh(ref InputDevice dev, XRNode node)
    {
        if (!dev.isValid) dev = InputDevices.GetDeviceAtXRNode(node);
    }

    void SetFloorOrigin()
    {
        var subs = new System.Collections.Generic.List<XRInputSubsystem>();
        SubsystemManager.GetInstances(subs);
        foreach (XRInputSubsystem s in subs) s.TrySetTrackingOriginMode(TrackingOriginModeFlags.Floor);
        originSet = subs.Count > 0;
    }

    void UpdateXR()
    {
        if (!originSet) SetFloorOrigin();
        if (player != null) player.disableMovement = false;   // desktop fallback may have disabled it before XR started
        Refresh(ref headDev, XRNode.Head);
        Refresh(ref leftDev, XRNode.LeftHand);
        Refresh(ref rightDev, XRNode.RightHand);

        ApplyHead();

        Vector3 p; Quaternion r;
        if (TryPose(leftDev, out p, out r)) { leftHand.localPosition = Reach(p); leftHand.localRotation = r; }
        if (TryPose(rightDev, out p, out r)) { rightHand.localPosition = Reach(p); rightHand.localRotation = r; }

        bool menu = ReadBool(leftDev, CommonUsages.primaryButton);      // X button
        bool click = ReadBool(rightDev, CommonUsages.triggerButton);    // right trigger
        FlyHeld = click;
        LeftGrip = ReadBool(leftDev, CommonUsages.gripButton);
        RightGrip = ReadBool(rightDev, CommonUsages.gripButton);

        MenuTogglePressed = menu && !prevMenu;
        ClickPressed = click && !prevClick;
        prevMenu = menu;
        prevClick = click;

        // smooth turn on the right thumbstick
        Vector2 stick;
        if (rightDev.isValid && rightDev.TryGetFeatureValue(CommonUsages.primary2DAxis, out stick))
        {
            if (Mathf.Abs(stick.x) > 0.25f && player != null)
                player.Turn(stick.x * PlayerProfile.TurnSpeed * Time.deltaTime);
        }
    }

    // Long Arms: push the hand away from the head by ArmScale
    Vector3 Reach(Vector3 handLocal)
    {
        if (Mathf.Approximately(ArmScale, 1f)) return handLocal;
        return head.localPosition + (handLocal - head.localPosition) * ArmScale;
    }

    void ApplyHead()
    {
        Vector3 p; Quaternion r;
        if (TryPose(headDev, out p, out r)) { head.localPosition = p; head.localRotation = r; }
    }

    void OnBeforeRender()
    {
        if (XRActive) ApplyHead();   // refresh head pose right before rendering (less latency)
    }

    static bool TryPose(InputDevice d, out Vector3 p, out Quaternion r)
    {
        p = Vector3.zero;
        r = Quaternion.identity;
        if (!d.isValid) return false;
        bool a = d.TryGetFeatureValue(CommonUsages.devicePosition, out p);
        bool b = d.TryGetFeatureValue(CommonUsages.deviceRotation, out r);
        return a && b;
    }

    static bool ReadBool(InputDevice d, InputFeatureUsage<bool> usage)
    {
        bool v;
        return d.isValid && d.TryGetFeatureValue(usage, out v) && v;
    }

    public void Pulse()
    {
        HapticCapabilities cap;
        if (rightDev.isValid && rightDev.TryGetHapticCapabilities(out cap) && cap.supportsImpulse)
            rightDev.SendHapticImpulse(0u, 0.4f, 0.06f);
    }

    // ---------------- Desktop fallback ----------------

    void UpdateDesktop()
    {
        if (player != null) player.disableMovement = true;  // hands are glued to the head here, so no arm-launching

        if (Input.GetMouseButton(1))
        {
            yaw += Input.GetAxis("Mouse X") * 3f;
            pitch -= Input.GetAxis("Mouse Y") * 3f;
            pitch = Mathf.Clamp(pitch, -85f, 85f);
        }

        head.localPosition = new Vector3(0f, DesktopHeadHeight, 0f);
        head.localRotation = Quaternion.Euler(pitch, yaw, 0f);
        leftHand.localPosition = head.localPosition + head.localRotation * new Vector3(-0.25f, -0.35f, 0.3f) * ArmScale;
        rightHand.localPosition = head.localPosition + head.localRotation * new Vector3(0.25f, -0.35f, 0.3f) * ArmScale;
        leftHand.localRotation = head.localRotation;
        rightHand.localRotation = head.localRotation;

        // WASD + space (right mouse button to look around)
        Vector3 f = Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        Vector3 rt = Quaternion.Euler(0f, yaw, 0f) * Vector3.right;
        Vector3 move = f * Input.GetAxisRaw("Vertical") + rt * Input.GetAxisRaw("Horizontal");
        if (move.sqrMagnitude > 1f) move.Normalize();
        Vector3 vel = body.velocity;
        body.velocity = new Vector3(move.x * 5f, vel.y, move.z * 5f);
        if (Input.GetKeyDown(KeyCode.Space) && Mathf.Abs(vel.y) < 0.1f)
            body.velocity = new Vector3(body.velocity.x, 5f, body.velocity.z);

        FlyHeld = Input.GetKey(KeyCode.LeftShift);
        LeftGrip = Input.GetKey(KeyCode.Q);
        RightGrip = Input.GetKey(KeyCode.E);

        MenuTogglePressed = Input.GetKeyDown(KeyCode.Tab);
        ClickPressed = Input.GetMouseButtonDown(0);
    }
}
