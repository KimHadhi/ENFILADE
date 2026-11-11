using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Which kick it is, picked by the pose it leaves from: the clip the body plays (PlayerBody.Kick) and how hard it lands.</summary>
public enum KickKind : byte { Stand, Crouch, Down, Drop, DropStill }

/// <summary>
/// What V does besides the plain shove, by state, and the kick. Moving, V is the plain Left 4 Dead 2 shove (ShoveSweep).
/// Standing still, the shove also lunges at an enemy just beyond its reach, as ArcCW's bash does (sh_bash.lua DoLunge).
/// The kick is Mighty Foot Engaged's (GMod 1987521428). It has its own key (Player/Kick, rebindable in Settings) and works
/// with anything in hand, and V while the gun reloads is the same kick: the hands and the reload go on, the swing comes at
/// 0.2 s and the foot lands at 0.3 s, out from the middle of the body towards where the eyes look and as far as the body
/// stands tall, shoving whoever it meets; the kicker rocks back on contact. Off the ground it is a dropkick: twice the
/// damage, and the zombie is thrown down. A shut rest-stop door it meets bursts open and a breakable door comes off now
/// and then, kicking whoever stands behind it too. The kick counters as a shove does (ShoveCounter; user, 2026-11-10: V
/// while reloading should be an answer to runners too): the foot landing on a zombie late in its grab or leap counters
/// it, and so does a grab or a leap reaching the kicker while the foot is out or just after. Numbers: Tables/Player.csv,
/// row Shove.
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
    [Header("Kick (Mighty Foot Engaged)")]
    [SerializeField, Min(0f), Tooltip("From the press to the swing: view punch and whoosh (s).")]
    private float kickSwingTime = .2f;
    [SerializeField, Min(0f), Tooltip("From the press to the foot landing (s).")]
    private float kickHitTime = .3f;
    [SerializeField, Min(.1f), Tooltip("From one kick to the next (s): Mighty Foot's 0.75 plus its 0.5 delay.")]
    private float kickInterval = 1.25f;
    [SerializeField, Min(.1f), Tooltip("The foot reaches this share of the standing body's height, crouched or not (Mighty Foot: the standing hull's height).")]
    private float kickRangeShare = 1f;
    [SerializeField, Min(.01f), Tooltip("Half the width of the box the foot sweeps when the straight line misses (m). Mighty Foot: 4 units.")]
    private float kickRadius = .1f;
    [SerializeField, Tooltip("Damage, least;most, picked at random. Mighty Foot: 20;30.")]
    private Vector2 kickDamage = new Vector2(20f, 30f);
    [SerializeField, Min(0f), Tooltip("The kicker rocks back this fast on contact (m/s). Mighty Foot: 100 units/s.")]
    private float kickPushback = 2.5f;
    [SerializeField, Tooltip("View punch at the swing (degrees; pitch;random yaw either way). Mighty Foot: -10;2.")]
    private Vector2 kickViewPunch = new Vector2(-10f, 2f);
    [SerializeField, Range(0f, 90f), Tooltip("Looking down more than this (degrees), a kick on the ground is the downward one. Mighty Foot: 40.")]
    private float kickDownAngle = 40f;
    [Header("Dropkick")]
    [SerializeField, Min(0f), Tooltip("In the air slower than this across the ground (m/s), the dropkick plays its still clip. Beatrun: 100 units/s.")]
    private float dropkickStillSpeed = 2.5f;
    [SerializeField, Min(0f), Tooltip("A kick off the ground does this many times the damage and the posture damage. Mighty Foot: 2.")]
    private float dropkickDamageScale = 2f;
    [SerializeField, Min(0f), Tooltip("And throws whoever it meets this many times as fast as a shove does.")]
    private float dropkickPushScale = 2f;
    [SerializeField, Min(.05f), Tooltip("The throw slows to a stop over this long (s), then the zombie falls, as from a dash push.")]
    private float dropkickThrowTime = .35f;
    [SerializeField, Min(0f), Tooltip("How long it lies after the throw (s). 0: no fall, only the bigger push. A big body or a brute only takes the push.")]
    private float dropkickKnockdown = 1.2f;
    [Header("Door kick (Mighty Foot Engaged)")]
    [SerializeField, Range(0f, 1f), Tooltip("Chance a kick takes a breakable door off. Mighty Foot: 20%. A rest stop's door only flies open.")]
    private float doorBreakChance = .2f;
    [SerializeField, Min(0f), Tooltip("Zombies up to this far behind a door the kick burst open or broke are kicked too (m).")]
    private float doorReach = 1.2f;

    private PlayerMovement player;
    private Vector3 lungeVelocity;
    private float kickClock = -1f, nextKick;
    private bool swung;
    private KickKind kind;
    private readonly RaycastHit[] hits = new RaycastHit[32];
    private readonly HashSet<MeleeCombatTarget> struck = new HashSet<MeleeCombatTarget>();

    /// <summary>A kick left, on this player's own PC or, shown, on another's copy: the body plays the clip for its pose.</summary>
    public event Action<KickKind> Kicked;
    /// <summary>A kick is under way on this player's own PC: from the press until the foot lands (kickHitTime). The leg
    /// clip plays on longer, on every PC (PlayerBody.Kick).</summary>
    public bool IsKicking => kickClock >= 0f;
    private bool IsDropkick => kind == KickKind.Drop || kind == KickKind.DropStill;

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

    /// <summary>
    /// A kick, from the kick key or from V while the gun reloads: it leaves the hands and the reload alone. False while
    /// the last one is still going.
    /// </summary>
    internal bool TryKick()
    {
        if (player == null || Time.time < nextKick || player.IsDead || player.IsDowned) return false;
        nextKick = Time.time + kickInterval;
        kickClock = 0f; swung = false;
        kind = Pose();
        Kicked?.Invoke(kind);
        return true;
    }

    // The pose it leaves from picks the kick (Mighty Foot's clip choice; Beatrun's two air kicks): off the ground a
    // dropkick, moving or nearly still; crouched; looking down past kickDownAngle; otherwise standing.
    private KickKind Pose()
    {
        if (!player.IsGrounded)
            return Vector3.ProjectOnPlane(player.ActualVelocity, Vector3.up).magnitude > dropkickStillSpeed ? KickKind.Drop : KickKind.DropStill;
        if (player.IsCrouching) return KickKind.Crouch;
        Transform eyes = PlayerView.EyesOf(player);
        return eyes != null && -eyes.forward.y > Mathf.Sin(kickDownAngle * Mathf.Deg2Rad) ? KickKind.Down : KickKind.Stand;
    }

    /// <summary>On someone else's copy: their kick left. Their PC judged what it hit.</summary>
    internal void ShowKick(KickKind shown) => Kicked?.Invoke(shown);

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
        if (kickClock < 0f) return;
        // Dead or down, caught in an execution or a guard break, or the legs taken by a climb, a slide or a dive: no
        // foot lands.
        if (!player.DamageReceiver.IsAlive || player.IsDowned || player.CombatInputLocked
            || player.IsParkour || player.IsSliding || player.IsDiving) { kickClock = -1f; return; }
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
        GetComponent<PlayerFistAudio>()?.PlaySwing();
        GetComponentInChildren<PlayerCameraEffects>()?.ApplyCameraImpulse(
            new Vector3(kickViewPunch.x, UnityEngine.Random.Range(-kickViewPunch.y, kickViewPunch.y), 0f));
    }

    // Out from the middle of the body towards the point the eyes rest on, as far as the body stands tall: a line first,
    // then a thin box if it found nothing.
    private void Land()
    {
        var settings = player.AbilitySettings;
        Transform aim = PlayerView.EyesOf(player);
        if (aim == null) return;
        var capsule = GetComponent<CharacterController>();
        Vector3 from = capsule != null ? transform.TransformPoint(capsule.center) : transform.position + Vector3.up;
        // The standing height even crouched, as Mighty Foot's hull: a crouched capsule's height left the foot half its reach.
        float length = player.StandingColliderHeight * Mathf.Abs(transform.lossyScale.y) * kickRangeShare;
        Vector3 look = aim.position + aim.forward * 100f;
        if (First(aim.position, aim.forward, 100f, 0f, settings.interactionLayers, out RaycastHit seen)) look = seen.point;
        Vector3 direction = look - from;
        direction = direction.sqrMagnitude > 1e-4f ? direction.normalized : aim.forward;
        if (!First(from, direction, length, 0f, settings.interactionLayers, out RaycastHit hit)
            && !First(from, direction, length, kickRadius, settings.interactionLayers, out hit)) return;
        Strike(hit, direction, settings);
    }

    // A line, or a box of that half width: world-aligned, as Source's hull traces are.
    private bool First(Vector3 from, Vector3 direction, float length, float halfWidth, LayerMask layers, out RaycastHit first)
    {
        first = default;
        int count = halfWidth > 0f
            ? Physics.BoxCastNonAlloc(from, Vector3.one * halfWidth, direction, hits, Quaternion.identity, length, layers, QueryTriggerInteraction.Ignore)
            : Physics.RaycastNonAlloc(from, direction, hits, length, layers, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        for (int i = 0; i < count; i++)
        {
            if (hits[i].collider.transform.IsChildOf(transform) || hits[i].distance >= nearest) continue;
            nearest = hits[i].distance; first = hits[i];
        }
        return nearest < float.PositiveInfinity;
    }

    // What the foot met. A zombie late in a grab or a leap is countered, as by a shove (ShoveCounter): its own return and
    // show. A door gives way or holds (KickDoor). Anything else takes the kick (Hurt), and the kicker rocks back off it.
    private void Strike(in RaycastHit hit, Vector3 direction, MovementAbilitySettings settings)
    {
        var collider = hit.collider;
        Vector3 point = hit.distance > 0f ? hit.point : collider.bounds.center;
        Vector3 push = Flat(direction);
        var posture = collider.GetComponentInParent<MeleeCombatTarget>();
        var receiver = collider.GetComponentInParent<IDamageReceiver>();
        if (posture != null && posture.IsAvailable && ShoveCounter.TryShoveCounter(player, posture, point))
        {
            Audio().Play(ShoveImpactKind.Enemy);
            return;
        }
        if (posture == null && receiver == null && KickDoor(collider, hit, point, push, settings)) return;
        Hurt(collider, point, push, settings);
        Audio().Play(posture != null || receiver != null ? ShoveImpactKind.Enemy : ShoveImpactKind.World);
        Rock(push);
    }

    // Mighty Foot's door kick. A shut rest-stop door (CampaignDoor) bursts open, the host deciding as for [F]
    // (CampaignRun.KickOpen); a door that can break (IBreakable: BreakableDoor) comes off doorBreakChance of the time, for
    // every PC (DoorBreaks). Either way whoever stands just behind it is kicked as well. A door that holds rocks the kicker
    // back as a wall does. False for anything that is no door, and for a rest-stop door standing open.
    private bool KickDoor(Collider collider, in RaycastHit hit, Vector3 point, Vector3 push, MovementAbilitySettings settings)
    {
        var slider = collider.GetComponentInParent<CampaignDoor>();
        var breakable = slider == null ? collider.GetComponentInParent<IBreakable>() : null;
        if (breakable == null && (slider == null || !slider.Closed)) return false;
        // Measured before it goes: a broken door's collider goes with it.
        Bounds door = collider.bounds;
        Vector3 away = hit.distance > 0f ? Flat(-hit.normal) : push;
        bool through = slider != null ? CampaignRun.KickOpen(slider) : UnityEngine.Random.value < doorBreakChance;
        if (through && breakable != null) breakable.Break(point, -away, settings.shovePushSpeed);
        Audio().Play(ShoveImpactKind.Door);
        if (through) HitBehind(door, away, settings);
        else Rock(push);
        return true;
    }

    // Whoever stands within doorReach behind the door the kick burst open or broke is kicked too, on through the doorway.
    private void HitBehind(Bounds door, Vector3 away, MovementAbilitySettings settings)
    {
        if (doorReach <= 0f) return;
        Vector3 across = Vector3.Cross(Vector3.up, away);
        Vector3 extents = door.extents;
        float halfWidth = Mathf.Abs(across.x) * extents.x + Mathf.Abs(across.z) * extents.z;
        float halfThickness = Mathf.Abs(away.x) * extents.x + Mathf.Abs(away.z) * extents.z;
        Vector3 center = door.center + away * (halfThickness + doorReach * .5f);
        var half = new Vector3(Mathf.Max(.1f, halfWidth), Mathf.Max(.1f, extents.y), doorReach * .5f);
        struck.Clear();
        foreach (var collider in Physics.OverlapBox(center, half, Quaternion.LookRotation(away), settings.interactionLayers,
                     QueryTriggerInteraction.Ignore))
        {
            if (collider.transform.IsChildOf(transform)) continue;
            var target = collider.GetComponentInParent<MeleeCombatTarget>();
            if (target == null || !target.IsAvailable || !struck.Add(target)) continue;
            Hurt(collider, collider.bounds.center, away, settings);
        }
    }

    // The kick's blow: Mighty Foot's damage with a shove's posture damage, stun and push; a dropkick dropkickDamageScale
    // times the damage and posture, thrown dropkickPushScale times as fast and down to the ground as a dash push throws,
    // but a big body or a brute (MeleeCombatTarget.Steadfast) only gives way further. Props are pushed. True when it took it.
    private bool Hurt(Collider collider, Vector3 point, Vector3 push, MovementAbilitySettings settings)
    {
        var posture = collider.GetComponentInParent<MeleeCombatTarget>();
        var receiver = collider.GetComponentInParent<IDamageReceiver>();
        var shove = posture != null ? posture : collider.GetComponentInParent<IShoveReceiver>();
        bool drop = IsDropkick;
        float scale = drop ? dropkickDamageScale : 1f, pushScale = drop ? dropkickPushScale : 1f;
        float postureDamage = settings.shovePostureDamage * scale;
        Vector3 thrown = push * (settings.shovePushSpeed * pushScale);
        bool landed = true;
        if (receiver != null)
        {
            var result = receiver.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), UnityEngine.Random.Range(kickDamage.x, kickDamage.y) * scale,
                DamageKind.Attack, point, push, player.gameObject, true, postureDamage, method: DamageMethod.Shove));
            landed = result == DamageResult.Damaged;
        }
        if (landed && posture != null)
        {
            if (drop && dropkickKnockdown > 0f && !posture.Steadfast && posture.BodySize != EnemyBodySize.Large)
            {
                posture.ApplyPostureDamage(postureDamage, 0f, false);
                posture.Barge(thrown, dropkickThrowTime, dropkickKnockdown);
                return true;
            }
            posture.ApplyPostureDamage(postureDamage);
            posture.Interrupt(settings.shoveStun);
        }
        var prop = PhysicsProp.Of(collider);
        if (landed && shove != null) shove.ReceiveShove(thrown, point);
        else if (prop != null) prop.Push(push * (settings.propShoveImpulse * pushScale), point, settings.shovePushSpeed * pushScale);
        return landed;
    }

    // The kicker rocks back off what the foot met, twice as fast in the air (Mighty Foot's pushback).
    private void Rock(Vector3 push) => player.HorizontalVelocity -= push * (player.IsGrounded ? kickPushback : kickPushback * 2f);

    private Vector3 Flat(Vector3 direction)
    {
        Vector3 flat = Vector3.ProjectOnPlane(direction, Vector3.up);
        if (flat.sqrMagnitude < 1e-4f) flat = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        return flat.normalized;
    }

    private PlayerShoveAudio Audio() => TryGetComponent(out PlayerShoveAudio audio) ? audio : gameObject.AddComponent<PlayerShoveAudio>();

    private void OnDisable() { lungeVelocity = Vector3.zero; kickClock = -1f; }
}
