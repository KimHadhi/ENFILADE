using System;
using UnityEngine;

/// <summary>
/// What V does besides the plain shove, by state. Moving, it is the plain Left 4 Dead 2 shove (ShoveSweep). Standing
/// still, the shove also lunges at an enemy just beyond its reach, as ArcCW's bash does (sh_bash.lua DoLunge). With the
/// gun reloading it is a kick instead, after Mighty Foot Engaged (GMod 1987521428): the reload goes on, the swing comes
/// at 0.2 s and the foot lands at 0.3 s, out from the middle of the body towards where the eyes look and as far as the
/// body is tall, shoving whoever it meets; the kicker rocks back on contact. Numbers: Tables/Player.csv, row Shove.
/// </summary>
[DisallowMultipleComponent]
public sealed class ShoveMoves : MonoBehaviour
{
    [Header("Standing lunge (ArcCW bash)")]
    [SerializeField, Min(0f), Tooltip("Lunges at an enemy up to this far beyond the shove's reach (m). ArcCW's LungeLength: 64 units.")]
    private float lungeLength = 1.6f;
    [SerializeField, Min(0f), Tooltip("Lunge speed per metre to the enemy, on the ground (1/s). ArcCW: 5.")]
    private float lungeRate = 5f;
    [SerializeField, Min(0f), Tooltip("The same in the air (1/s). ArcCW: 2.5.")]
    private float airLungeRate = 2.5f;
    [SerializeField, Min(.1f), Tooltip("How fast the lunge dies away (1/s): GMod's ground friction, 8, so it covers ArcCW's distance (5/8 of the gap).")]
    private float lungeFriction = 8f;
    [SerializeField, Min(0f), Tooltip("Slower than this (m/s) with no move key held is standing still.")]
    private float standingSpeed = .5f;
    [Header("Reload kick (Mighty Foot Engaged)")]
    [SerializeField, Min(0f), Tooltip("From the press to the swing: view punch and whoosh (s).")]
    private float kickSwingTime = .2f;
    [SerializeField, Min(0f), Tooltip("From the press to the foot landing (s).")]
    private float kickHitTime = .3f;
    [SerializeField, Min(.1f), Tooltip("From one kick to the next (s): Mighty Foot's 0.75 plus its 0.5 delay.")]
    private float kickInterval = 1.25f;
    [SerializeField, Min(.1f), Tooltip("The foot reaches this share of the standing body's height (Mighty Foot: the hull height).")]
    private float kickRangeShare = 1f;
    [SerializeField, Min(.01f), Tooltip("Radius of the foot's sweep when the straight line misses (m). Mighty Foot: 4 units.")]
    private float kickRadius = .1f;
    [SerializeField, Tooltip("Damage, least;most, picked at random. Mighty Foot: 20;30.")]
    private Vector2 kickDamage = new Vector2(20f, 30f);
    [SerializeField, Min(0f), Tooltip("The kicker rocks back this fast on contact (m/s). Mighty Foot: 100 units/s.")]
    private float kickPushback = 2.5f;
    [SerializeField, Tooltip("View punch at the swing (degrees; pitch;random yaw either way). Mighty Foot: -10;2.")]
    private Vector2 kickViewPunch = new Vector2(-10f, 2f);

    private PlayerMovement player;
    private Vector3 lungeVelocity;
    private float kickClock = -1f, nextKick;
    private bool swung;
    private readonly RaycastHit[] hits = new RaycastHit[16];

    /// <summary>A kick left, on this player's own PC or, shown, on another's copy: the body plays it.</summary>
    public event Action Kicked;
    /// <summary>A kick is under way on this player's own PC: from the press until the foot lands (kickHitTime). The leg
    /// clip plays on longer, on every PC (PlayerBody.Kick).</summary>
    public bool IsKicking => kickClock >= 0f;

    private void Awake() => player = GetComponent<PlayerMovement>();

    /// <summary>The player's shove moves, made and bound to the table on first use.</summary>
    public static ShoveMoves For(PlayerMovement player)
    {
        if (player == null) return null;
        if (player.TryGetComponent(out ShoveMoves moves)) return moves;
        moves = player.gameObject.AddComponent<ShoveMoves>();
        GameTables.Bind(PlayerMovement.TableName, "Shove", moves);
        return moves;
    }

    /// <summary>Standing still, a shove that has someone just out of its reach lunges at them.</summary>
    internal void Lunge(Transform aim, float reach, LayerMask layers)
    {
        if (player == null || aim == null || player.IsCrouching || !IsStandingStill()) return;
        // The first thing the eyes rest on, past this body's own parts (ArcCW's eye trace).
        if (!First(aim.position, aim.forward, reach + lungeLength, 0f, layers, out RaycastHit hit) || hit.distance <= reach) return;
        var target = hit.collider.GetComponentInParent<MeleeCombatTarget>();
        if (target == null || !target.IsAvailable) return;
        // ArcCW only ever lunges along or below the view.
        Vector3 direction = aim.forward;
        direction.y = Mathf.Min(direction.y, 0f);
        direction.Normalize();
        lungeVelocity = Vector3.ProjectOnPlane(direction, Vector3.up) * ((player.IsGrounded ? lungeRate : airLungeRate) * hit.distance);
    }

    private bool IsStandingStill()
    {
        Vector3 moving = Vector3.ProjectOnPlane(player.ActualVelocity, Vector3.up);
        return player.CurrentInput.sqrMagnitude < .01f && moving.magnitude < standingSpeed;
    }

    /// <summary>V while the gun reloads: a kick, which leaves the reload alone. False while the last one is still going.</summary>
    internal bool TryKick()
    {
        if (player == null || Time.time < nextKick) return false;
        nextKick = Time.time + kickInterval;
        kickClock = 0f; swung = false;
        Kicked?.Invoke();
        return true;
    }

    /// <summary>On someone else's copy: their kick left. Their PC judged what it hit.</summary>
    internal void ShowKick() => Kicked?.Invoke();

    private void Update()
    {
        if (player == null || !player.IsInitialized || player.IsNetworkDriven) return;
        float dt = player.DeltaTime;
        if (dt <= 0f) return;
        if (lungeVelocity.sqrMagnitude > .0025f)
        {
            // Moved as the source's friction would slow it, whatever this game's own ground friction does meanwhile.
            player.Motor.AddDisplacement(lungeVelocity * ((1f - Mathf.Exp(-lungeFriction * dt)) / lungeFriction));
            lungeVelocity *= Mathf.Exp(-lungeFriction * dt);
        }
        else lungeVelocity = Vector3.zero;
        if (kickClock < 0f) return;
        if (!player.DamageReceiver.IsAlive || player.CombatInputLocked) { kickClock = -1f; return; }
        kickClock += dt;
        if (!swung && kickClock >= kickSwingTime) Swing();
        if (kickClock >= kickHitTime) { kickClock = -1f; Land(); }
    }

    private void Swing()
    {
        swung = true;
        GetComponent<PlayerFistAudio>()?.PlaySwing();
        GetComponentInChildren<PlayerCameraEffects>()?.ApplyCameraImpulse(
            new Vector3(kickViewPunch.x, UnityEngine.Random.Range(-kickViewPunch.y, kickViewPunch.y), 0f));
    }

    // Out from the middle of the body towards the point the eyes rest on, as far as the body is tall: a line first,
    // then a thin sweep if it found nothing.
    private void Land()
    {
        var settings = player.AbilitySettings;
        Transform aim = PlayerView.EyesOf(player);
        var capsule = GetComponent<CharacterController>();
        Vector3 from = capsule != null ? transform.TransformPoint(capsule.center) : transform.position + Vector3.up;
        float height = capsule != null ? capsule.height * Mathf.Abs(transform.lossyScale.y) : 1.75f;
        float length = height * kickRangeShare;
        Vector3 look = aim.position + aim.forward * 100f;
        if (First(aim.position, aim.forward, 100f, 0f, settings.interactionLayers, out RaycastHit seen)) look = seen.point;
        Vector3 direction = (look - from).normalized;
        if (!First(from, direction, length, 0f, settings.interactionLayers, out RaycastHit hit)
            && !First(from, direction, length, kickRadius, settings.interactionLayers, out hit)) return;
        Strike(hit, direction, settings);
    }

    private bool First(Vector3 from, Vector3 direction, float length, float radius, LayerMask layers, out RaycastHit first)
    {
        first = default;
        int count = radius > 0f
            ? Physics.SphereCastNonAlloc(from, radius, direction, hits, length, layers, QueryTriggerInteraction.Ignore)
            : Physics.RaycastNonAlloc(from, direction, hits, length, layers, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            if (hits[i].collider.transform.IsChildOf(transform) || hits[i].distance >= nearest) continue;
            nearest = hits[i].distance; first = hits[i];
        }
        return nearest < float.PositiveInfinity;
    }

    // What a shove does to whatever the foot met, with Mighty Foot's damage.
    private void Strike(RaycastHit hit, Vector3 direction, MovementAbilitySettings settings)
    {
        var collider = hit.collider;
        Vector3 point = hit.distance > 0f ? hit.point : collider.bounds.center;
        Vector3 push = Vector3.ProjectOnPlane(direction, Vector3.up).normalized;
        var posture = collider.GetComponentInParent<MeleeCombatTarget>();
        var receiver = collider.GetComponentInParent<IDamageReceiver>();
        var shove = posture != null ? posture : collider.GetComponentInParent<IShoveReceiver>();
        bool landed = true;
        if (receiver != null)
        {
            var result = receiver.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), UnityEngine.Random.Range(kickDamage.x, kickDamage.y),
                DamageKind.Attack, point, push, player.gameObject, true, settings.shovePostureDamage, method: DamageMethod.Shove));
            landed = result == DamageResult.Damaged;
        }
        if (landed && posture != null)
        {
            posture.ApplyPostureDamage(settings.shovePostureDamage);
            posture.Interrupt(settings.shoveStun);
        }
        var prop = PhysicsProp.Of(collider);
        if (landed && shove != null) shove.ReceiveShove(push * settings.shovePushSpeed, point);
        else if (prop != null) prop.Push(push * settings.propShoveImpulse, point, settings.shovePushSpeed);
        var audio = GetComponent<PlayerShoveAudio>() ?? gameObject.AddComponent<PlayerShoveAudio>();
        audio.Play(posture != null || receiver != null ? ShoveImpactKind.Enemy : ShoveImpactKind.World);
        player.HorizontalVelocity -= push * (player.IsGrounded ? kickPushback : kickPushback * 2f);
    }

    private void OnDisable() { lungeVelocity = Vector3.zero; kickClock = -1f; }
}
