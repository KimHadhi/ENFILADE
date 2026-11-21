using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A free physics object (fruit, crate, debris, a fuel can, Impact CFX's items). Its Rigidbody mass alone decides how it
/// reacts: walking leans on it with a force, V shove and bullets hit it with momentum (N·s), so a light prop flies and a
/// heavy one barely moves. A push never makes it faster than what pushed it. Parkour ignores it, so the player never hangs
/// from something that rolls away. Built from a model with ENFILADE → 모델 → 물리 소품 프리팹 만들기.
/// Whoever last set it moving is its attacker for a few seconds (the Source engine's physics attacker), so if it then
/// strikes an enemy hard enough the blow is theirs (PropStrike). Light enough, it can be carried in both hands and thrown
/// (PropCarry). In a room the PC whose player last touched it runs its motion and the others follow (PropNet), so a push
/// by another PC's player is left to that PC.
/// </summary>
[DisallowMultipleComponent, RequireComponent(typeof(Rigidbody))]
public sealed class PhysicsProp : MonoBehaviour
{
    // Upper bound for any single push, so a very light prop does not shoot off at bullet speed.
    private const float MaxPushSpeed = 12f;
    // The same enemy is struck again only after this long: one blow per meeting, however many contacts it makes (s).
    private const float StrikeAgain = .35f;

    private Rigidbody body;
    private PlayerMovement attacker;
    private float attackedAt = -99f, struckAt = -99f, halfSize = -1f;
    private EnemyHealth struck;
    private bool hostJudged, fixedBody;
    // How its body is smoothed between physics steps when physics runs it; none while it is moved by hand.
    private RigidbodyInterpolation smoothing;
    // Its own solid shape: off while it is carried, so it never shoves its carrier.
    private List<Collider> solids;

    // ---- Its motion between PCs (PropNet) ----
    /// <summary>Its name between PCs: the same prop has the same id on every PC.</summary>
    internal uint NetId;
    // The newest claim on its motion this PC knows of; whether this PC runs that motion now or follows another PC's word;
    // when this PC last told the others; how long it has lain still; the word it follows.
    internal ushort Epoch;
    internal bool Driving, Following;
    internal float SentAt = -99f, QuietFor;
    internal Vector3 FollowPosition;
    internal Quaternion FollowRotation = Quaternion.identity;

    internal Rigidbody Body => body != null ? body : body = GetComponent<Rigidbody>();

    /// <summary>
    /// One PC runs this prop's motion for everyone (a fuel can: CampaignCans on the host), so its strikes are judged there
    /// for whoever set it moving, not on the attacker's own PC (PropStrike); it is not carried by F nor shared by PropNet.
    /// </summary>
    public bool HostJudged
    {
        get => hostJudged;
        set { hostJudged = value; if (value) PropNet.Forget(this); }
    }

    /// <summary>Who carries it in both hands (PropCarry), as every PC knows it; null while it is loose.</summary>
    public PlayerMovement Holder { get; private set; }

    /// <summary>Whoever placed it made its body unmovable (kinematic): it is never carried or pushed.</summary>
    public bool FixedBody => fixedBody;

    /// <summary>Half its longest side (m), from what it shows.</summary>
    public float HalfSize => halfSize >= 0f ? halfSize : halfSize = MeasureHalf();

    /// <summary>The player who set it moving within PropStrike's credit window, or null.</summary>
    public PlayerMovement Attacker => attacker != null && Time.time - attackedAt <= PropStrike.CreditSeconds ? attacker : null;

    /// <summary>Its velocity as of the last physics step, before a contact changed it: the way a strike was going.</summary>
    public Vector3 Going { get; private set; }

    public static PhysicsProp Of(Collider collider)
    {
        var attached = collider != null ? collider.attachedRigidbody : null;
        return attached != null && attached.TryGetComponent(out PhysicsProp prop) ? prop : null;
    }

    /// <summary>A player set it moving (a throw, a kick, a shove, a shot, a lean): theirs for PropStrike's credit window.</summary>
    public void SetAttacker(GameObject by)
    {
        var player = by != null ? by.GetComponentInParent<PlayerMovement>() : null;
        if (player == null) return;
        attacker = player;
        attackedAt = Time.time;
    }

    /// <summary>The walking body leans on the prop with <paramref name="force"/> newtons this frame, never past its own speed.</summary>
    public void PushFromWalker(Vector3 walkerVelocity, float force, GameObject by = null)
    {
        if (!Touched(by)) return;
        var rb = Body;
        Vector3 horizontal = Vector3.ProjectOnPlane(walkerVelocity, Vector3.up);
        float speed = horizontal.magnitude;
        if (rb.isKinematic || force <= 0f || speed < .05f) return;
        Vector3 direction = horizontal / speed;
        float missing = speed - Vector3.Dot(rb.linearVelocity, direction);
        if (missing <= 0f) return;
        // Through the centre of mass: leaning on a prop moves it without flicking it into a spin.
        rb.AddForce(direction * Mathf.Min(missing, force * Time.deltaTime / rb.mass), ForceMode.VelocityChange);
        SetAttacker(by);
    }

    /// <summary>
    /// A body of <paramref name="bodyMass"/> kg moving at <paramref name="speed"/> runs into the prop and both carry on at
    /// one shared speed: a light prop leaves at the body's speed for almost nothing, one as heavy as the body halves both.
    /// Returns the body's speed afterwards (0 when the prop cannot move).
    /// </summary>
    public float Kick(Vector3 direction, float speed, float bodyMass, Vector3 point, GameObject by = null)
    {
        if (!Touched(by)) return 0f;
        var rb = Body;
        if (rb.isKinematic) return 0f;
        float target = Mathf.Min(MaxPushSpeed, bodyMass * speed / (bodyMass + rb.mass));
        float change = target - Vector3.Dot(rb.linearVelocity, direction);
        if (change <= 0f) return speed;
        rb.AddForceAtPosition(direction * change * rb.mass, point, ForceMode.Impulse);
        SetAttacker(by);
        return Mathf.Max(0f, speed - rb.mass * change / bodyMass);
    }

    /// <summary>A hit carrying <paramref name="momentum"/> (N·s) at a point: the prop gains momentum ÷ mass, at most <paramref name="maxSpeed"/>.</summary>
    public void Push(Vector3 momentum, Vector3 point, float maxSpeed = MaxPushSpeed, GameObject by = null)
    {
        if (!Touched(by)) return;
        var rb = Body;
        if (rb.isKinematic) return;
        Vector3 change = Vector3.ClampMagnitude(momentum / rb.mass, Mathf.Min(maxSpeed, MaxPushSpeed));
        // At the contact point, so a hit high on the prop also tips or spins it.
        if (change.sqrMagnitude <= .0001f) return;
        rb.AddForceAtPosition(change * rb.mass, point, ForceMode.Impulse);
        SetAttacker(by);
    }

    // A push may move it here: one by nobody's player (a blast), or by this PC's own player, who then runs its motion for
    // everyone (PropNet). Another PC's player's push is theirs to run and arrives as their PC's word. Nothing moves a prop
    // in someone's hands.
    private bool Touched(GameObject by)
    {
        if (Holder != null) return false;
        var player = by != null ? by.GetComponentInParent<PlayerMovement>() : null;
        if (hostJudged || player == null) return true;
        if (!player.IsLocal) return !PropNet.Shared;
        PropNet.Drive(this);
        return true;
    }

    /// <summary>Carried by <paramref name="holder"/> (null: let go), on this PC's copy (PropNet, PropCarry).</summary>
    internal void SetHolder(PlayerMovement holder)
    {
        Holder = holder;
        ApplyMode();
    }

    /// <summary>
    /// Its body as its state wants it: carried, moved by hand and touching nothing; following another PC's word, moved by
    /// hand but still solid (a bullet stops at it, F finds it); otherwise physics runs it.
    /// </summary>
    internal void ApplyMode()
    {
        var rb = Body;
        bool byHand = fixedBody || Holder != null || Following;
        if (rb.isKinematic != byHand) rb.isKinematic = byHand;
        // Moved by hand each frame, so physics must not blend it back toward its last step.
        var wanted = byHand ? RigidbodyInterpolation.None : smoothing;
        if (rb.interpolation != wanted) rb.interpolation = wanted;
        bool solid = Holder == null;
        foreach (var collider in Solids) if (collider != null && collider.enabled != solid) collider.enabled = solid;
        if (!byHand) rb.WakeUp();
    }

    /// <summary>Put at once at <paramref name="at"/>, moving as given (still, when it is moved by hand).</summary>
    internal void Teleport(Vector3 at, Quaternion turn, Vector3 velocity, Vector3 spin)
    {
        ApplyMode();
        var rb = Body;
        rb.position = at;
        rb.rotation = turn;
        transform.SetPositionAndRotation(at, turn);
        if (!rb.isKinematic)
        {
            rb.linearVelocity = velocity;
            rb.angularVelocity = spin;
        }
        Going = velocity;
    }

    private List<Collider> Solids
    {
        get
        {
            if (solids != null) return solids;
            solids = new List<Collider>();
            foreach (var collider in GetComponentsInChildren<Collider>(true)) if (!collider.isTrigger) solids.Add(collider);
            return solids;
        }
    }

    private float MeasureHalf()
    {
        bool any = false;
        var bounds = default(Bounds);
        foreach (var renderer in GetComponentsInChildren<Renderer>())
        {
            if (!any) { bounds = renderer.bounds; any = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        if (!any) return .2f;
        var size = bounds.size;
        return Mathf.Max(size.x, Mathf.Max(size.y, size.z)) * .5f;
    }

    private void Awake()
    {
        fixedBody = Body.isKinematic;
        smoothing = Body.interpolation;
    }

    private void OnEnable() => PropNet.Register(this);

    private void OnDestroy() => PropNet.Forget(this);

    /// <summary>A strike on <paramref name="enemy"/> may land now: not the same one again within a moment.</summary>
    internal bool MayStrike(EnemyHealth enemy)
    {
        if (enemy == struck && Time.time - struckAt < StrikeAgain) return false;
        struck = enemy;
        struckAt = Time.time;
        return true;
    }

    // Kept only while someone could be credited with a strike, so a resting prop costs nothing.
    private void FixedUpdate()
    {
        if (attacker != null) Going = Body.linearVelocity;
    }

    private void OnCollisionEnter(Collision collision) => PropStrike.Struck(this, Body, collision);
}
