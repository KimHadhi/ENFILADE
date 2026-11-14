using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A teammate's shove frees a friend a runner pins, as shoving a Hunter off a survivor does in Left 4 Dead (the user,
/// 2026-11-14: "러너 덮쳤을 때 아군이 밀쳐주면 풀려나게 마치 레포데처럼"). Any V of theirs (the gun's or the fists' shove,
/// the reload kick, the crouched sweep, the dash push) that reaches a zombie on a pinned friend, or the friend lying under
/// it, throws every zombie on them off at once: each is knocked down for knockdown seconds, falling away from the friend,
/// and the friend gets up as after any pin, nothing taking hold of them meanwhile (PlayerHold.ShovedFree: the shield
/// burst's grace).
/// The aim is generous: the friend's body, or whatever kneels on it, counts within reach along the floor from the eyes,
/// within angle around where the shover looks seen from above (looking down at a body at one's feet does as well as
/// looking ahead), within height of the shover's own floor, and with no wall between.
/// What the shover's PC sees decides it (the user's rule: what I see is the truth). It knocks the zombies down there,
/// which reaches the host that runs them at once, and tells the friend's own PC, which holds the pin
/// (PlayerNetworkCombat.ShoveFree) and throws off anything this PC had not yet seen on them. The shover sees and hears
/// their shove land (BashImpact here; the shove's sound and flash reach every PC through PlayerShoveAudio) and earns a
/// little of their gauge, as for getting a friend up (UltimateCharge Help.Rescued). A pinned player's own shove
/// (FightsPinned) never frees them here: their ways out are the shield burst and a Knight's kick (PlayerHold).
/// Numbers: Tables/Player.csv, row Rescue.
/// </summary>
[DisallowMultipleComponent]
public sealed class ShoveRescue : MonoBehaviour
{
    [SerializeField, Min(.5f), Tooltip("The friend's body, or a zombie on it, this near along the floor from the shover's eyes is reached (m).")]
    private float reach = 2.4f;
    [SerializeField, Range(10f, 360f), Tooltip("Within this angle around where the shover looks, seen from above (degrees, both sides together).")]
    private float angle = 140f;
    [SerializeField, Min(0f), Tooltip("No further above or below the shover's own floor than this (m).")]
    private float height = 1.2f;
    [SerializeField, Min(0f), Tooltip("Each zombie thrown off lies this long (s).")]
    private float knockdown = 2.5f;
    [SerializeField, Min(.2f), Tooltip("The shove's hit where it met them, this big (1: a plain shove's).")]
    private float hitSize = 1.3f;

    // Where on a body the shove aims (m above its feet: a friend lying, a zombie kneeling on them), how far a lying body
    // reaches along the floor either way of its middle (m), and how long a friend freed here is let be while word goes
    // round (s): their copy shows them pinned until their own PC's word comes back.
    private const float LyingUp = .3f, KneelUp = .55f, LyingHalf = .8f, Recent = .6f;

    private static readonly List<MeleeCombatTarget> on = new List<MeleeCombatTarget>();
    private static readonly List<(PlayerMovement friend, float at)> freed = new List<(PlayerMovement friend, float at)>();
    private static readonly RaycastHit[] hits = new RaycastHit[16];

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget()
    {
        on.Clear();
        freed.Clear();
    }

    /// <summary>This player's rescue numbers, made and bound to the table on first use.</summary>
    public static ShoveRescue For(PlayerMovement player)
    {
        if (player == null) return null;
        if (player.TryGetComponent(out ShoveRescue rescue)) return rescue;
        rescue = player.gameObject.AddComponent<ShoveRescue>();
        GameTables.Bind(PlayerMovement.TableName, "Rescue", rescue);
        return rescue;
    }

    /// <summary>
    /// A shove of this player's reached this zombie (ShoveSweep, the reload kick, the crouched sweep, the dash push): true
    /// when it was on a pinned friend, who is free now; the shove does nothing else to it, as for a counter. done: what
    /// this shove has met already, which the friend and everything thrown off them join.
    /// </summary>
    internal static bool Reached(PlayerMovement shover, MeleeCombatTarget target, Vector3 point, HashSet<Object> done = null)
    {
        if (shover == null || target == null || !target.TryGetComponent(out ZombieGrip grip) || !grip.IsHolding) return false;
        foreach (var friend in PlayerRoster.All)
        {
            if (!Freeable(shover, friend, done) || !grip.IsOn(friend)) continue;
            Free(shover, friend, point, done);
            return true;
        }
        return false;
    }

    /// <summary>
    /// The shove's reach from these eyes now (ShoveSweep each sample, the reload kick as the foot lands): every pinned
    /// friend whose body, or a zombie on it, lies within the generous reach is freed. True when one was.
    /// </summary>
    internal static bool Sweep(PlayerMovement shover, Vector3 eyes, Vector3 forward, HashSet<Object> done = null)
    {
        if (shover == null) return false;
        bool any = false;
        foreach (var friend in PlayerRoster.All)
        {
            if (!Freeable(shover, friend, done) || !Reaches(shover, eyes, forward, friend, out Vector3 point)) continue;
            Free(shover, friend, point, done);
            any = true;
        }
        return any;
    }

    /// <summary>The pinned friend this player's V would free from where they stand and look now, or null (PlayerLifeHUD's prompt).</summary>
    public static PlayerMovement Within(PlayerMovement player)
    {
        if (player == null || !player.IsInitialized || !player.InputAvailable || player.IsHeld || player.IsDowned || player.IsDead
            || player.CombatInputLocked) return null;
        Transform eyes = null;
        foreach (var friend in PlayerRoster.All)
        {
            if (!Freeable(player, friend, null)) continue;
            if (eyes == null) eyes = PlayerView.EyesOf(player);
            if (Reaches(player, eyes.position, eyes.forward, friend, out _)) return friend;
        }
        return null;
    }

    // Someone else this shove may free: pinned as this PC shows them, and not freed by this shove or a moment ago.
    private static bool Freeable(PlayerMovement shover, PlayerMovement friend, HashSet<Object> done)
        => friend != null && friend != shover && friend.IsInitialized && friend.IsPinned && (done == null || !done.Contains(friend))
            && !JustFreed(friend);

    private static bool JustFreed(PlayerMovement friend)
    {
        freed.RemoveAll(entry => entry.friend == null || Time.time - entry.at >= Recent);
        foreach (var entry in freed) if (entry.friend == friend) return true;
        return false;
    }

    // Every zombie on that friend, as this PC knows it (ZombieGrip.IsOn).
    private static void Gather(PlayerMovement friend)
    {
        on.Clear();
        foreach (var target in MeleeCombatTarget.Active)
            if (target != null && target.IsAvailable && target.TryGetComponent(out ZombieGrip grip) && grip.IsOn(friend)) on.Add(target);
    }

    // The nearest point of the friend's body, or of a zombie on it, within the generous reach of these eyes and with no
    // wall between. Which way a lying body lies isn't known on every PC, so its middle and a point either way along where
    // it faces stand for it.
    private static bool Reaches(PlayerMovement shover, Vector3 eyes, Vector3 forward, PlayerMovement friend, out Vector3 point)
    {
        var numbers = For(shover);
        Vector3 look = Vector3.ProjectOnPlane(forward, Vector3.up);
        if (look.sqrMagnitude < 1e-4f) look = Vector3.ProjectOnPlane(shover.transform.forward, Vector3.up);
        look = look.normalized;
        float within = Mathf.Cos(Mathf.Clamp(numbers.angle, 10f, 360f) * .5f * Mathf.Deg2Rad);
        float floor = shover.transform.position.y, best = float.PositiveInfinity;
        Vector3 found = default;
        Vector3 middle = friend.transform.position;
        Vector3 along = Vector3.ProjectOnPlane(friend.transform.forward, Vector3.up).normalized * LyingHalf;
        Consider(middle, LyingUp);
        Consider(middle + along, LyingUp);
        Consider(middle - along, LyingUp);
        Gather(friend);
        foreach (var target in on)
            if (target.TryGetComponent(out ZombieGrip grip)) Consider(grip.Feet, KneelUp);
        point = found;
        return best < float.PositiveInfinity;

        void Consider(Vector3 feet, float up)
        {
            if (Mathf.Abs(feet.y - floor) > numbers.height) return;
            Vector3 aim = feet + Vector3.up * up;
            Vector3 flat = Vector3.ProjectOnPlane(aim - eyes, Vector3.up);
            float distance = flat.magnitude;
            if (distance > numbers.reach || distance >= best) return;
            if (distance > .3f && Vector3.Dot(flat / distance, look) < within) return;
            if (!Clear(shover, eyes, aim)) return;
            best = distance;
            found = aim;
        }
    }

    // Nothing solid between the eyes and that point: bodies, loose props and falling dolls don't count.
    private static bool Clear(PlayerMovement shover, Vector3 from, Vector3 to)
    {
        Vector3 delta = to - from;
        float length = delta.magnitude;
        if (length < .05f) return true;
        int count = Physics.RaycastNonAlloc(from, delta / length, hits, length, shover.AbilitySettings.interactionLayers,
            QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var collider = hits[i].collider;
            var body = collider.attachedRigidbody;
            if ((body != null && !body.isKinematic) || PhysicsProp.Of(collider) != null
                || collider.GetComponentInParent<PlayerMovement>() != null || collider.GetComponentInParent<MeleeCombatTarget>() != null) continue;
            return false;
        }
        return true;
    }

    // The shover's PC decided: everything on the friend falls away from them (the host, which runs the zombies, hears it
    // at once), the friend's own PC lets go of them, and the shover sees their shove land and earns a little for it.
    private static void Free(PlayerMovement shover, PlayerMovement friend, Vector3 point, HashSet<Object> done)
    {
        var numbers = For(shover);
        Gather(friend);
        Vector3 middle = friend.transform.position;
        Vector3 ahead = Vector3.ProjectOnPlane(shover.transform.forward, Vector3.up).normalized;
        foreach (var target in on)
        {
            Vector3 away = Vector3.ProjectOnPlane(target.transform.position - middle, Vector3.up);
            away = away.sqrMagnitude > 1e-4f ? away.normalized : ahead;
            if (target.Steadfast) target.Interrupt(numbers.knockdown);
            else target.KnockDown(away, numbers.knockdown);
            if (done != null) done.Add(target);
        }
        if (done != null) done.Add(friend);
        freed.Add((friend, Time.time));
        // The pin is the friend's own PC's to end (PlayerHold): told across the room, or ended here when it is this PC's.
        if (friend.TryGetComponent(out PlayerHold hold)) hold.ShovedFree(numbers.knockdown);
        else if (friend.TryGetComponent(out PlayerNetworkCombat network)) network.ShoveFree(numbers.knockdown);
        Vector3 push = Vector3.ProjectOnPlane(point - shover.transform.position, Vector3.up);
        BashImpact.Play(point, push.sqrMagnitude > 1e-4f ? push.normalized : ahead, numbers.hitSize, shover);
        UltimateCharge.AwardHelp(UltimateCharge.Help.Rescued);
    }
}
