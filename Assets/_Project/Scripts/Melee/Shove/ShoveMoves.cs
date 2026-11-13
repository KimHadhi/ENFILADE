using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What V does besides the plain shove, by state. Moving, it is the plain Left 4 Dead 2 shove (ShoveSweep). Standing
/// still, the shove also lunges at an enemy just beyond its reach, as ArcCW's bash does (sh_bash.lua DoLunge). With the
/// gun reloading it is a kick instead, after Mighty Foot Engaged (GMod 1987521428): the reload goes on, the swing comes
/// at 0.2 s and the foot lands at 0.3 s, out from the middle of the body towards where the eyes look and as far as the
/// body is tall, shoving whoever it meets; the kicker rocks back on contact. The kick counters as a shove does
/// (ShoveCounter; user, 2026-11-10: V while reloading should be an answer to runners too): the foot landing on a zombie
/// late in its grab or leap counters it, and so does a grab or a leap reaching the kicker while the foot is out or just
/// after. Crouched, V is a leg sweep instead, gun or fists (user, 2026-11-11 #33: Guilty Gear Strive's Ramlethal
/// crouching sweep): one leg goes round the front at the floor, sweepArc degrees in sweepSeconds, and everyone it passes
/// within sweepRadius is knocked off their feet (a big body only staggers); the reload, if any, goes on. While the leg is
/// out it counters as a shove does. Every PC sees the leg go round (PlayerBody.Sweep) and the arc it leaves on the floor
/// (SweepArcVfx), through the kick's own event. Numbers: Tables/Player.csv, row Shove.
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
    [Header("Crouched sweep (Guilty Gear Strive, Ramlethal's crouching sweep)")]
    [SerializeField, Min(.05f), Tooltip("The leg going round (s).")]
    private float sweepSeconds = .32f;
    [SerializeField, Range(30f, 360f), Tooltip("How far round it goes, from one side through the front to the other (degrees).")]
    private float sweepArc = 220f;
    [SerializeField, Min(.3f), Tooltip("Whom it reaches, from the middle of the body (m).")]
    private float sweepRadius = 1.9f;
    [SerializeField, Min(0f), Tooltip("Damage to each one it sweeps.")]
    private float sweepDamage = 10f;
    [SerializeField, Min(0f), Tooltip("They lie this long (s). A big body only staggers.")]
    private float sweepKnockdown = 1.6f;
    [SerializeField, Min(.1f), Tooltip("From one sweep to the next (s).")]
    private float sweepInterval = .9f;
    [SerializeField, Tooltip("The view drops and tilts with it (m down; degrees down;roll).")]
    private Vector3 sweepView = new Vector3(.1f, 4f, 6f);

    private PlayerMovement player;
    private Vector3 lungeVelocity, sweepForward;
    private float kickClock = -1f, nextKick, sweepClock = -1f, nextSweep, sweepPrev;
    private bool swung;
    private readonly RaycastHit[] hits = new RaycastHit[16];
    private readonly Collider[] around = new Collider[48];
    private readonly HashSet<MeleeCombatTarget> swept = new HashSet<MeleeCombatTarget>();

    /// <summary>The sweep's numbers, for the leg every PC shows (PlayerBody.Sweep).</summary>
    public float SweepSeconds => sweepSeconds;
    public float SweepArc => sweepArc;

    /// <summary>A kick left, on this player's own PC or, shown, on another's copy: the body plays it.</summary>
    public event Action Kicked;
    /// <summary>
    /// A shove with a gun in the hands began (the crowd shove, not a kick, sweep or dash), on this player's own PC or,
    /// shown, on another's copy: the gun swings through its bash (FPSBody.Bash). The shove's own PC judges what it hit.
    /// </summary>
    public event Action Bashed;

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

    /// <summary>On someone else's copy: their kick left, or crouched their sweep. Their PC judged what it hit.</summary>
    internal void ShowKick()
    {
        if (player != null && player.IsCrouching) BeginSweep(false);
        Kicked?.Invoke();
    }

    /// <summary>A shove with a gun began on this player's own PC (PlayerCombatActions.TryShove): the gun bashes, here and
    /// on every other PC (PlayerNetworkCombat sends it on).</summary>
    internal void Bash() => Bashed?.Invoke();

    /// <summary>On someone else's copy: their gun shove began. Their PC judged what it hit.</summary>
    internal void ShowBash() => Bashed?.Invoke();

    /// <summary>
    /// Crouched V, gun or fists: the leg sweep. False while the last one is still in its interval. With the fists out the
    /// punch comes straight back after it as the spinning backfist (MeleeCombos link).
    /// </summary>
    internal bool TrySweep()
    {
        if (player == null || Time.time < nextSweep || kickClock >= 0f || sweepClock >= 0f) return false;
        nextSweep = Time.time + sweepInterval;
        BeginSweep(true);
        // The other PCs hear of it as a kick; crouched, their copy sweeps (ShowKick), and so does the body (PlayerBody.Sweep).
        Kicked?.Invoke();
        MeleeCombos.For(player).OrNull()?.OpenLink();
        return true;
    }

    // The leg starts round from the side, along where the body faces now. mine: this player's own, which judges the hits.
    private void BeginSweep(bool mine)
    {
        sweepForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        SweepArcVfx.Play(transform, sweepForward, sweepArc, sweepRadius, sweepSeconds);
        if (!mine) return;
        sweepClock = 0f; sweepPrev = sweepArc * .5f; swept.Clear();
        GetComponent<PlayerFistAudio>().OrNull()?.PlaySwing();
        ShoveCounter.Shoved(player, sweepForward);
        CombatHeadMotion.For(player).OrNull()?.Move(new Vector3(0f, -sweepView.x, 0f), new Vector3(sweepView.y, 0f, -sweepView.z),
            .05f, sweepSeconds * .6f, .2f);
    }

    // The foot's way round this frame, eased out (quick from the side, slowing past the far one); everyone it passes, once.
    private void TickSweep(float dt)
    {
        sweepClock += dt;
        float t = Mathf.Clamp01(sweepClock / sweepSeconds), eased = 1f - (1f - t) * (1f - t);
        float angle = Mathf.Lerp(sweepArc * .5f, -sweepArc * .5f, eased);
        // While the leg is out it stands as a shove: a grab or a leap meeting it now is countered (ShoveCounter).
        ShoveCounter.Shoved(player, sweepForward);
        SweepPast(angle, sweepPrev);
        sweepPrev = angle;
        if (t >= 1f) sweepClock = -1f;
    }

    private void SweepPast(float from, float to)
    {
        var settings = player.AbilitySettings;
        Vector3 centre = transform.position;
        int count = Physics.OverlapSphereNonAlloc(centre + Vector3.up * .5f, sweepRadius, around, settings.interactionLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var collider = around[i];
            if (collider == null || collider.transform.IsChildOf(transform)) continue;
            var target = collider.GetComponentInParent<MeleeCombatTarget>();
            if (target == null || !target.IsAvailable || swept.Contains(target)) continue;
            Vector3 to3 = target.transform.position - centre, flat = Vector3.ProjectOnPlane(to3, Vector3.up);
            if (flat.magnitude > sweepRadius + .3f || Mathf.Abs(to3.y) > 1f) continue;
            float bearing = flat.sqrMagnitude > 1e-4f ? Vector3.SignedAngle(sweepForward, flat, Vector3.up) : 0f;
            // A little slack either side, so a frame's step never jumps a body.
            if (bearing < from - 8f || bearing > to + 8f) continue;
            if (!LegReaches(centre + Vector3.up * .35f, target)) continue;
            swept.Add(target);
            SweepStrike(target, collider, flat.sqrMagnitude > 1e-4f ? flat.normalized : sweepForward, settings);
        }
    }

    // Nothing solid between the swinging leg and that body.
    private bool LegReaches(Vector3 from, MeleeCombatTarget target)
    {
        Vector3 delta = target.transform.position + Vector3.up * .35f - from;
        float length = delta.magnitude;
        if (length < .05f) return true;
        int count = Physics.RaycastNonAlloc(from, delta / length, hits, length, player.AbilitySettings.interactionLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            Transform hit = hits[i].collider.transform;
            if (hit.IsChildOf(transform) || hit.IsChildOf(target.transform) || hits[i].collider.GetComponentInParent<MeleeCombatTarget>() != null) continue;
            if (PhysicsProp.Of(hits[i].collider) != null) continue;
            return false;
        }
        return true;
    }

    // Swept off their feet, falling the way the leg goes; a big body only staggers back a little. A grab or a leap late in
    // its windup is countered instead, as by a shove.
    private void SweepStrike(MeleeCombatTarget target, Collider collider, Vector3 away, MovementAbilitySettings settings)
    {
        Vector3 point = collider.bounds.ClosestPoint(transform.position + Vector3.up * .25f);
        var audio = GetComponent<PlayerShoveAudio>().OrNull() ?? gameObject.AddComponent<PlayerShoveAudio>();
        if (ShoveCounter.TryShoveCounter(player, target, point)) { audio.Play(ShoveImpactKind.Enemy); return; }
        var receiver = target.GetComponent<IDamageReceiver>() ?? collider.GetComponentInParent<IDamageReceiver>();
        bool landed = true;
        if (receiver != null)
            landed = receiver.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), sweepDamage, DamageKind.Attack, point, away,
                player.gameObject, true, settings.shovePostureDamage, method: DamageMethod.Shove)) == DamageResult.Damaged;
        if (landed)
        {
            // The leg takes the feet the way it goes, so they fall across it rather than straight back.
            Vector3 fall = (away * .5f + Quaternion.AngleAxis(-90f, Vector3.up) * away).normalized;
            if (target.BodySize != EnemyBodySize.Large) target.KnockDown(fall, sweepKnockdown);
            else
            {
                target.ApplyPostureDamage(settings.shovePostureDamage);
                target.Interrupt(settings.shoveStun);
                target.ReceiveShove(away * settings.shovePushSpeed * .5f, point);
            }
            BashImpact.Play(point, away, .8f, player);
        }
        audio.Play(ShoveImpactKind.Enemy);
    }

    /// <summary>A counter hands the shove straight back (ShoveCounter): the next kick needn't wait for the last.</summary>
    internal void RefreshKick() => nextKick = 0f;

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
        if (sweepClock >= 0f)
        {
            if (!player.DamageReceiver.IsAlive || player.CombatInputLocked || !player.IsCrouching) sweepClock = -1f;
            else TickSweep(dt);
        }
        if (kickClock < 0f) return;
        if (!player.DamageReceiver.IsAlive || player.CombatInputLocked) { kickClock = -1f; return; }
        kickClock += dt;
        // While the foot is out it stands as a shove under way: a grab or a leap reaching them now, or within the
        // counter's grace of the foot landing, is countered (ShoveCounter.TryHitCounter).
        Transform eyes = PlayerView.EyesOf(player);
        ShoveCounter.Shoved(player, eyes != null ? eyes.forward : transform.forward);
        if (!swung && kickClock >= kickSwingTime) Swing();
        if (kickClock >= kickHitTime) { kickClock = -1f; Land(); }
    }

    private void Swing()
    {
        swung = true;
        GetComponent<PlayerFistAudio>().OrNull()?.PlaySwing();
        GetComponentInChildren<PlayerCameraEffects>().OrNull()?.ApplyCameraImpulse(
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
        var audio = GetComponent<PlayerShoveAudio>().OrNull() ?? gameObject.AddComponent<PlayerShoveAudio>();
        // A zombie late in a grab or a leap is countered instead, as by a shove (ShoveCounter): its own return and show.
        if (posture != null && posture.IsAvailable && ShoveCounter.TryShoveCounter(player, posture, point))
        {
            audio.Play(ShoveImpactKind.Enemy);
            return;
        }
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
        if (landed && (posture != null || receiver != null)) BashImpact.Play(point, push, 1f, player);
        audio.Play(posture != null || receiver != null ? ShoveImpactKind.Enemy : ShoveImpactKind.World);
        player.HorizontalVelocity -= push * (player.IsGrounded ? kickPushback : kickPushback * 2f);
    }

    private void OnDisable() { lungeVelocity = Vector3.zero; kickClock = sweepClock = -1f; }
}
