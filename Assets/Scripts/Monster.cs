using UnityEngine;

// A corrupted monke.obj (see CorruptedMonke) that WALKS on the forest floor. It stands and scans around until it sees you (or you get close),
// then chases until it loses you for a few seconds. It never climbs onto things above it and never leaves the ground.
public class Monster : MonoBehaviour
{
    public bool frozen;                 // "Freeze Monster" mod

    const float Radius = 0.45f;
    const float SightRange = 45f;       // how far it can see you
    const float CloseRange = 8f;        // it always notices you this close, even from behind
    const float ViewAngle = 130f;       // its field of view
    const float LoseTime = 6f;          // seconds after losing sight before it gives up
    const float StepUp = 0.6f;          // tallest step it can walk up
    const float KillDistance = 1.0f;    // horizontal reach
    const float Gravity = 20f;

    GtagBootstrap boot;
    Transform visual;
    CorruptedMonke corrupt;
    float grace, loseTimer, fallSpeed, scanDir = 1f;
    bool chasing;

    public static Monster Create(GtagBootstrap b)
    {
        var go = new GameObject("Monster");
        go.layer = GtagBootstrap.PlayerLayer;
        var m = go.AddComponent<Monster>();
        m.boot = b;
        m.Build();
        return m;
    }

    // The monster's origin is at its feet.
    void Build()
    {
        visual = new GameObject("Visual").transform;
        visual.SetParent(transform, false);
        corrupt = CorruptedMonke.Create(visual);
        if (corrupt == null) BuildFallbackBody();   // monke mesh missing: the old shadow shape
        BuildGlow();
    }

    void BuildFallbackBody()
    {
        Color dark = new Color(0.03f, 0.02f, 0.04f);
        Color red = new Color(1f, 0.05f, 0.05f);

        Part(PrimitiveType.Capsule, new Vector3(0f, 0.95f, 0f), new Vector3(0.9f, 0.95f, 0.7f), dark);
        Part(PrimitiveType.Sphere, new Vector3(0f, 1.8f, 0.05f), new Vector3(0.55f, 0.6f, 0.55f), dark);
        Part(PrimitiveType.Sphere, new Vector3(-0.13f, 1.85f, 0.27f), Vector3.one * 0.11f, red);
        Part(PrimitiveType.Sphere, new Vector3(0.13f, 1.85f, 0.27f), Vector3.one * 0.11f, red);
        Part(PrimitiveType.Capsule, new Vector3(-0.6f, 1.0f, 0.15f), new Vector3(0.2f, 0.7f, 0.2f), dark).localRotation = Quaternion.Euler(25f, 0f, 12f);
        Part(PrimitiveType.Capsule, new Vector3(0.6f, 1.0f, 0.15f), new Vector3(0.2f, 0.7f, 0.2f), dark).localRotation = Quaternion.Euler(25f, 0f, -12f);

    }

    void BuildGlow()
    {
        var lightGo = new GameObject("Glow");
        lightGo.transform.SetParent(transform, false);
        lightGo.transform.localPosition = new Vector3(0f, 1.7f, 0.55f);
        var l = lightGo.AddComponent<Light>();
        l.type = LightType.Point;
        l.color = new Color(1f, 0.1f, 0.1f);
        l.range = 5f;
        l.intensity = 1.0f;
        l.shadows = LightShadows.None;
        if (corrupt != null) corrupt.glow = l;
    }

    Transform Part(PrimitiveType type, Vector3 pos, Vector3 scale, Color c)
    {
        var p = GameObject.CreatePrimitive(type);
        Destroy(p.GetComponent<Collider>());
        p.layer = GtagBootstrap.PlayerLayer;
        p.transform.SetParent(visual, false);
        p.transform.localPosition = pos;
        p.transform.localScale = scale;
        p.GetComponent<Renderer>().material.color = c;
        return p.transform;
    }

    public void SetActive(bool on)
    {
        gameObject.SetActive(on);
        if (on) Respawn();
    }

    // Back at the gazebo, on the ground, a few seconds of mercy and not chasing yet.
    public void Respawn()
    {
        transform.position = boot.MonsterHome();
        transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        grace = 5f;
        chasing = false;
        loseTimer = 0f;
        fallSpeed = 0f;
    }

    void Update()
    {
        if (boot == null || boot.Player == null || !boot.Player.enabled) return;
        float dt = Time.deltaTime;
        if (grace > 0f) grace -= dt;

        Vector3 pos = transform.position;
        Vector3 target = boot.Head.position;
        Vector3 toFlat = new Vector3(target.x - pos.x, 0f, target.z - pos.z);
        float flatDist = toFlat.magnitude;

        // ---- kill: only if you are within its reach (a player up a tree is out of reach) ----
        float dy = target.y - pos.y;
        if (grace <= 0f && flatDist < KillDistance && dy > -0.3f && dy < 2.4f && !boot.Invincible && !boot.IsDying)
        {
            boot.Die();
            return;
        }

        // ---- senses ----
        if (!frozen && !boot.IsDying)
        {
            if (CanSee(pos, target, toFlat, flatDist)) { chasing = true; loseTimer = LoseTime; }
            else if (chasing)
            {
                loseTimer -= dt;
                if (loseTimer <= 0f) chasing = false;
            }

            if (chasing && flatDist > 0.05f)
            {
                float speed = PlayerProfile.MonsterSpeed * (grace > 0f ? 0.5f : 1f);
                Walk(toFlat / flatDist, speed * dt);
                FaceTowards(toFlat);
            }
            else
            {
                // standing guard: slowly look left and right
                transform.Rotate(0f, 22f * scanDir * dt, 0f);
                if (Random.value < 0.4f * dt) scanDir = -scanDir;
            }
        }

        if (corrupt != null) corrupt.chasing = chasing && !frozen;
        Ground(dt);
        visual.localPosition = new Vector3(0f, Mathf.Abs(Mathf.Sin(Time.time * (chasing ? 6f : 1.5f))) * (chasing ? 0.06f : 0.02f), 0f);
    }

    bool CanSee(Vector3 pos, Vector3 target, Vector3 toFlat, float flatDist)
    {
        Vector3 eye = pos + Vector3.up * 1.7f;
        float dist = Vector3.Distance(eye, target);
        if (flatDist < CloseRange) return true;                 // too close to miss
        if (dist > SightRange) return false;
        if (Vector3.Angle(transform.forward, toFlat) > ViewAngle * 0.5f) return false;   // outside its field of view
        return !Physics.Linecast(eye, target, 1 << 0, QueryTriggerInteraction.Ignore);    // no wall/tree in between
    }

    void FaceTowards(Vector3 flatDir)
    {
        if (flatDir.sqrMagnitude < 0.0001f) return;
        transform.rotation = Quaternion.Slerp(transform.rotation, Quaternion.LookRotation(flatDir), 8f * Time.deltaTime);
    }

    // Walk along the ground. Steps around obstacles, will not climb steep things or step off big drops.
    void Walk(Vector3 dir, float step)
    {
        float[] turns = { 0f, 40f, -40f, 80f, -80f, 120f, -120f };
        foreach (float t in turns)
        {
            Vector3 d = Quaternion.Euler(0f, t, 0f) * dir;
            if (TryStep(d, step)) return;
        }
    }

    bool TryStep(Vector3 d, float step)
    {
        Vector3 pos = transform.position;

        // wall check at knee/chest height
        RaycastHit hit;
        Vector3 origin = pos + Vector3.up * (StepUp + Radius);
        if (Physics.SphereCast(origin, Radius * 0.9f, d, out hit, step + 0.15f, 1 << 0, QueryTriggerInteraction.Ignore))
            return false;

        // ground at the new spot - the ray starts only just above the feet, so a roof or branch overhead is never hit
        Vector3 np = pos + d * step;
        if (!Physics.Raycast(np + Vector3.up * StepUp, Vector3.down, out hit, StepUp + 1.2f, 1 << 0, QueryTriggerInteraction.Ignore))
            return false;                                       // a drop - do not walk off
        if (hit.normal.y < 0.55f) return false;                 // too steep
        np.y = pos.y;
        transform.position = np;
        return true;
    }

    // Keep the feet on the floor below. Rises smoothly up gentle slopes, falls with gravity.
    void Ground(float dt)
    {
        Vector3 pos = transform.position;
        RaycastHit hit;
        if (Physics.Raycast(pos + Vector3.up * StepUp, Vector3.down, out hit, StepUp + 6f, 1 << 0, QueryTriggerInteraction.Ignore))
        {
            float gy = hit.point.y;
            if (gy >= pos.y)
            {
                pos.y = Mathf.MoveTowards(pos.y, gy, 6f * dt);
                fallSpeed = 0f;
            }
            else
            {
                fallSpeed += Gravity * dt;
                pos.y = Mathf.Max(gy, pos.y - fallSpeed * dt);
                if (pos.y <= gy) fallSpeed = 0f;
            }
        }
        transform.position = pos;
    }
}
