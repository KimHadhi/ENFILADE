using System;
using UnityEngine;

/// <summary>
/// The Support's improvised directional mine (Throwable.Claymore; the user's pick of 2026-11-12, "지뢰 느낌 좋은데?
/// 던지는 느낌보단 설치로 가도 되긴 하겠다"): two C4 bricks strapped to a phone detonator, set down where the crosshair is
/// (PlayerGrenades: held, G shows the spot and the fan it will guard, ClaymorePreview; let go, the hand puts it there).
/// On a floor it lies phone up and guards the way the player looked; on a wall its back is to the wall and it guards the
/// room. The phone takes GrenadeRules claymoreArm to come on (its light blinks, then holds with a click, and a faint
/// trip beam shows the fan's middle); armed, it fires when a zombie comes into its fan (claymoreCone degrees wide,
/// claymoreTrigger metres, no wall between): the frag's blast and gore, one way only (FragBlast's directional form, its
/// row's radius, damage and edge), and a brute stays on its feet. A Support keeps claymoreMost out at once: setting
/// another takes the oldest back (it tips over and goes, no bang).
/// On every PC: the owner's PC watches for zombies (its own copies of them) and fires it, its hits going as the owner's
/// through the copies as the frag's do; the other PCs show it and fire their copy where the owner's PC says.
/// </summary>
public sealed class ClaymoreMine : MonoBehaviour
{
    private const float RetireSeconds = .45f, LampSize = .022f;

    /// <summary>Its number among its owner's throwables (the network's).</summary>
    public ushort Id { get; private set; }
    public PlayerMovement Owner { get; private set; }
    /// <summary>On a wall (its back to it) rather than on a floor.</summary>
    public bool OnWall { get; private set; }
    /// <summary>Where its blast starts.</summary>
    public Vector3 Heart => transform.position + transform.forward * (OnWall ? .09f : .05f) + transform.up * (OnWall ? 0f : .05f);
    /// <summary>The way it fires (flat).</summary>
    public Vector3 Facing => transform.forward;

    private ThrowableInfo info;
    private Action<ClaymoreMine> fired;
    private Transform look;
    private LineRenderer lamp, beam;
    private bool mine, armed, gone, retiring;
    private float age, lookLeft, retireAge;
    private Vector3 lookScale;

    /// <summary>
    /// Sets one down at <paramref name="at"/> (on the surface), turned to <paramref name="facing"/> (its forward the way
    /// it fires). Mine: this PC's player put it there, so this PC watches for zombies and calls
    /// <paramref name="onFire"/> when it fires.
    /// </summary>
    public static ClaymoreMine Place(ushort id, Vector3 at, Quaternion facing, bool wall, PlayerMovement owner, bool mine, Action<ClaymoreMine> onFire)
    {
        var host = new GameObject("Claymore " + id);
        FeelAssets.GroupRuntimeRoot(host, "World", owner != null ? owner.gameObject : null);
        host.transform.SetPositionAndRotation(at, facing);
        var self = host.AddComponent<ClaymoreMine>();
        self.Id = id;
        self.Owner = owner;
        self.OnWall = wall;
        self.mine = mine;
        self.fired = onFire;
        self.info = Throwables.Of(Throwable.Claymore);
        self.Build();
        return self;
    }

    // The charge, lying phone up on a floor or with its back to a wall, sitting on the surface whatever the model's own
    // pivot (its bounds measured here); the phone's light; the trip beam, off until it is armed.
    private void Build()
    {
        look = new GameObject("Look").transform;
        look.SetParent(transform, false);
        // On a wall the phone faces out, the way it fires.
        look.localRotation = OnWall ? Quaternion.Euler(90f, 0f, 0f) : Quaternion.identity;
        var model = Throwables.Dress(Throwable.Claymore, look);
        if (LocalBounds(model, out Vector3 low, out Vector3 high))
        {
            Vector3 middle = (low + high) * .5f;
            // On the floor its underside on the surface; on a wall its back.
            Vector3 shift = OnWall ? new Vector3(-middle.x, -middle.y, -low.z) : new Vector3(-middle.x, -low.y, -middle.z);
            look.localPosition += shift;
        }
        lookScale = look.localScale;
        lamp = Line("Phone Light", LampSize);
        beam = Line("Trip Beam", .006f);
        beam.enabled = false;
    }

    // The model's box in this mine's own space, from its meshes' own bounds (exact however the mine is turned).
    private bool LocalBounds(GameObject model, out Vector3 low, out Vector3 high)
    {
        low = Vector3.one * float.MaxValue;
        high = Vector3.one * float.MinValue;
        bool any = false;
        foreach (var filter in model.GetComponentsInChildren<MeshFilter>(true))
        {
            if (filter.sharedMesh == null) continue;
            var box = filter.sharedMesh.bounds;
            var toMine = transform.worldToLocalMatrix * filter.transform.localToWorldMatrix;
            for (int corner = 0; corner < 8; corner++)
            {
                var point = box.center + Vector3.Scale(box.extents,
                    new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                point = toMine.MultiplyPoint3x4(point);
                low = Vector3.Min(low, point);
                high = Vector3.Max(high, point);
                any = true;
            }
        }
        return any;
    }

    private LineRenderer Line(string name, float width)
    {
        var host = new GameObject(name);
        host.transform.SetParent(transform, false);
        var line = host.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = 2;
        line.startWidth = line.endWidth = width;
        line.numCapVertices = 4;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = FeelAssets.Particle(Texture2D.whiteTexture, true);
        return line;
    }

    /// <summary>Taken back off (a newer one went down past claymoreMost): it tips over and goes, with no bang.</summary>
    public void Retire()
    {
        if (gone || retiring) return;
        retiring = true;
        retireAge = 0f;
        lamp.enabled = beam.enabled = false;
    }

    /// <summary>Its copy fires where the owner's PC says it did (the owner's own fired itself).</summary>
    public void BurstAt(Vector3 at)
    {
        if (gone) return;
        gone = true;
        FragBlast.Play(at, info, Owner, false, Facing, Cone);
        Destroy(gameObject);
    }

    private static float Cone => Mathf.Clamp(Throwables.Rule("claymoreCone", 60f), 5f, 359f);

    private void Update()
    {
        if (gone) return;
        float dt = Time.deltaTime;
        age += dt;
        if (retiring)
        {
            retireAge += dt;
            float share = Mathf.Clamp01(retireAge / RetireSeconds);
            look.localScale = lookScale * (1f - share);
            look.localRotation *= Quaternion.Euler(0f, 0f, 260f * dt);
            if (share >= 1f) { gone = true; Destroy(gameObject); }
            return;
        }
        float armAfter = Mathf.Max(0f, Throwables.Rule("claymoreArm", 1f));
        if (!armed && age >= armAfter)
        {
            armed = true;
            Throwables.Play("Pin", Heart, .6f, 1.8f, 12f, 1.6f);
        }
        Show();
        if (!mine || !armed || (lookLeft -= dt) > 0f) return;
        lookLeft = .1f;
        if (Spots()) Fire();
    }

    // The phone's light (blinking while it arms, held and slowly beating once armed) and the trip beam.
    private void Show()
    {
        Vector3 top = transform.position + (OnWall ? transform.forward * .1f + transform.up * .03f : transform.up * .1f) - transform.forward * .02f;
        bool blink = armed || Mathf.Repeat(age * 6f, 1f) < .5f;
        lamp.enabled = blink;
        if (blink)
        {
            float beat = armed ? .75f + .25f * Mathf.Sin(age * 4f) : 1f;
            lamp.startColor = lamp.endColor = new Color(1f, .12f, .08f, beat);
            lamp.SetPosition(0, top);
            lamp.SetPosition(1, top + transform.up * .001f);
        }
        if (!armed) return;
        beam.enabled = true;
        float reach = Mathf.Max(.5f, Throwables.Rule("claymoreTrigger", 4.5f));
        beam.startColor = new Color(1f, .1f, .08f, .22f);
        beam.endColor = new Color(1f, .1f, .08f, 0f);
        beam.SetPosition(0, Heart);
        beam.SetPosition(1, Heart + Facing * reach);
    }

    // A living zombie in its fan, near enough, with nothing solid between.
    private bool Spots()
    {
        float reach = Mathf.Max(.5f, Throwables.Rule("claymoreTrigger", 4.5f));
        float spread = Mathf.Cos(Cone * .5f * Mathf.Deg2Rad);
        Vector3 heart = Heart, way = Vector3.ProjectOnPlane(Facing, Vector3.up);
        if (way.sqrMagnitude < 1e-4f) return false;
        way.Normalize();
        foreach (var enemy in EnemyHealth.Living)
        {
            if (enemy == null || !enemy.IsAlive) continue;
            Vector3 chest = enemy.transform.position + Vector3.up;
            Vector3 flat = Vector3.ProjectOnPlane(chest - heart, Vector3.up);
            if (flat.sqrMagnitude > reach * reach || Mathf.Abs(chest.y - heart.y) > 2.5f) continue;
            if (flat.sqrMagnitude > .04f && Vector3.Dot(flat.normalized, way) < spread) continue;
            if (Throwables.Covered(heart, chest)) continue;
            return true;
        }
        return false;
    }

    // The owner's: it fires, its hits this PC's to land; the others are told where (PlayerGrenades → the network).
    private void Fire()
    {
        if (gone) return;
        gone = true;
        Vector3 at = Heart;
        Throwables.NoteThrower(Owner, Throwable.Claymore);
        FragBlast.Play(at, info, Owner, true, Facing, Cone);
        fired?.Invoke(this);
        Destroy(gameObject);
    }
}

/// <summary>
/// Where a held claymore would go (PlayerGrenades, while G is held): a ring on the spot and the fan it would guard,
/// lying flat at the charge's height (GrenadeRules claymoreCone wide, claymoreTrigger long), in the HUD's colours.
/// </summary>
internal sealed class ClaymorePreview
{
    private const int ArcPoints = 24;
    private readonly Transform owner;
    private LineRenderer fan;
    private readonly ThrowArc ring;

    public ClaymorePreview(Transform owner)
    {
        this.owner = owner;
        ring = new ThrowArc(owner);
    }

    public void Show(Vector3 at, Quaternion facing, bool wall)
    {
        if (fan == null) fan = MakeLine();
        fan.enabled = true;
        Vector3 forward = facing * Vector3.forward, up = facing * Vector3.up;
        Vector3 heart = at + forward * (wall ? .09f : .05f) + up * (wall ? 0f : .05f);
        Vector3 way = Vector3.ProjectOnPlane(forward, Vector3.up);
        if (way.sqrMagnitude < 1e-4f) way = Vector3.forward;
        way.Normalize();
        float reach = Mathf.Max(.5f, Throwables.Rule("claymoreTrigger", 4.5f));
        float half = Mathf.Clamp(Throwables.Rule("claymoreCone", 60f), 5f, 359f) * .5f;
        fan.positionCount = ArcPoints + 3;
        Vector3 start = heart + Vector3.up * .02f;
        fan.SetPosition(0, start);
        for (int i = 0; i <= ArcPoints; i++)
        {
            float angle = Mathf.Lerp(-half, half, i / (float)ArcPoints);
            fan.SetPosition(i + 1, start + Quaternion.AngleAxis(angle, Vector3.up) * way * reach);
        }
        fan.SetPosition(ArcPoints + 2, start);
        fan.startColor = fan.endColor = KitStyle.With(KitStyle.Accent, .85f);
        // The spot itself, on a floor (a ring on a wall would lie on the ground below it).
        if (wall) ring.HideRing();
        else ring.Ring(at, up, .12f, KitStyle.With(KitStyle.Ice, .9f));
    }

    public void Hide()
    {
        if (fan != null && fan.enabled) fan.enabled = false;
        ring.Hide();
    }

    private LineRenderer MakeLine()
    {
        var host = new GameObject("Claymore Fan") { hideFlags = HideFlags.DontSave };
        host.transform.SetParent(owner, false);
        var line = host.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.loop = false;
        line.startWidth = line.endWidth = ThrowArc.Width;
        line.numCornerVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = FeelAssets.Particle(Texture2D.whiteTexture, false);
        return line;
    }
}
