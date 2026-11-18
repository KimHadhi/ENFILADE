using Unity.Netcode.Components;
using UnityEngine;

/// <summary>
/// A Schießhund's ultimate, the showdown (the user on the card page, 2026-11-14: "아이소 궁극기처럼 선택한 대상을 납치해서
/// 시순트와 그 둘만의 결투장으로 데려갈 수 있나? 복귀할 땐 환호성과 함께 시순트가 엄청 강화돼서 나오는 거지. 근데 이건 궁극기니까
/// 궁극기 결투로 따로 빼자."), run on the hound's own PC, which decides all of it and tells the others (ArenaNet; the user's
/// rule that the one who acts decides):
/// <list type="bullet">
/// <item>X with every card held and a zombie under the crosshair: a rift opens round the hound and round it, and a moment
/// later (Schiesshund.arenaPull) both are taken into the hound's arena far below the map (HoundArena). This PC moves the
/// zombie, there and back, through the zombie session's door (ZombieEffects.Move: the host carries it out and its copies
/// jump with it), once each way; the PC that runs it has the arena's walk-mesh laid for it and keeps it on the hound
/// meanwhile (ArenaPen). With no zombie under the crosshair, or one that can't be taken now (held by a player, holding
/// someone), or the hound held or climbing, it says why and the gauge stays full (Refusal, which
/// ClassCards.UltimateHeldBack asks before the gauge is spent).</item>
/// <item>There the two duel (HoundDuel: the zombie goes after the hound alone, the hound hits it harder, it carries the
/// bounty mark, so a Desperado round in its head ends it) for at most arenaSeconds, nobody else able to reach either.</item>
/// <item>The zombie falls (arenaLinger to see it fall), or time runs out: the hound comes back to the very spot it left,
/// and on time the zombie goes back to its own. The crowd roars (ArenaFx.Cheer) and the hound comes out strong: its guns
/// hit arenaBuffDamage% harder for arenaBuffSeconds (ClassKit.DamageFactor) and the Desperado is full.</item>
/// <item>Down in the arena, it ends at once: the hound is brought back where friends can get it up, without the roar,
/// and the zombie goes back too. Dead there, the zombie goes back and the hound's own respawn takes it.</item>
/// </list>
/// Held as time runs out (a grab, an execution of its own under way), the hound comes back once free, a few seconds at
/// most; a runner's pin is thrown off as a friend's shove throws it. Its foe also counts as its duel's foe for the Dead or
/// Alive card (ClassKit.CalledOut).
/// </summary>
[DisallowMultipleComponent]
public sealed class HoundShowdown : MonoBehaviour
{
    /// <summary>How a showdown ended: its zombie fell, time ran out, or the hound fell.</summary>
    public enum Ending : byte { Won, Time, Fell }

    private enum Phase : byte { None, Pulling, Fighting }

    private const string Row = PlayerClass.HoundRow;
    /// <summary>The words shown when the showdown can't be called (Texts.csv).</summary>
    public const string NoFoe = "class.schiesshund.arena.none", NotNow = "class.schiesshund.arena.busy";
    // Held as it should come back, how much longer it waits to be free before it comes back anyway (s).
    private const float MostHeld = 4f;

    private static HoundShowdown local;
    private PlayerMovement movement;
    private Phase phase;
    private EnemyHealth foe;
    private Vector3 houndFrom, foeFrom, houndTo, foeTo, center;
    private float houndYaw, foeYaw, pullAt, until, seconds, wonAt = -1f;
    private byte seat;
    // The Ace card held; the zombie moved into the arena (so it is to be moved back unless it falls there).
    private bool ace, foeTaken;
    // The crowd's strength on the way out: until when, how long it lasts, how much harder the guns hit (+%).
    private float cheerUntil = -99f, cheerTotal = 1f, cheerDamage;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => local = null;

    /// <summary>Whether this PC's hound is in its showdown now: being taken, or in the arena.</summary>
    public static bool Busy(PlayerMovement who) => Of(who) is HoundShowdown showdown && showdown.phase != Phase.None;

    /// <summary>How much of the crowd's strength is left on this PC's hound, 0 to 1 (ClassHUD's bar; 0: none).</summary>
    public static float CheerShare(PlayerMovement who)
        => Of(who) is HoundShowdown showdown && showdown.cheerUntil > Time.time ? Mathf.Clamp01((showdown.cheerUntil - Time.time) / showdown.cheerTotal) : 0f;

    /// <summary>Seconds of the crowd's strength left on this PC's hound, and of how many (its slot among the timed effects).</summary>
    public static float CheerLeft(PlayerMovement who) => Of(who) is HoundShowdown showdown ? showdown.cheerUntil - Time.time : 0f;
    public static float CheerSeconds(PlayerMovement who) => Of(who) is HoundShowdown showdown ? showdown.cheerTotal : 1f;

    /// <summary>How much harder this PC's hound's guns hit while the crowd's strength lasts (+%).</summary>
    public static float CheerDamage(PlayerMovement who) => Of(who) is HoundShowdown showdown ? showdown.cheerDamage : 0f;

    /// <summary>The factor the crowd's strength puts on this PC's hound's guns now (ClassKit.DamageFactor): 1 without it.</summary>
    public static float DamageFactor(PlayerMovement who) => CheerShare(who) > 0f ? 1f + CheerDamage(who) / 100f : 1f;

    private static HoundShowdown Of(PlayerMovement who) => local != null && who != null && local.movement == who ? local : null;

    /// <summary>
    /// Why this PC's hound can't call its showdown now, as a Texts id, or null when it can, with the zombie it would take:
    /// the one under the crosshair, alive, held by no player (an execution) and holding nobody, whom taking away would
    /// leave held by nothing. One knocked down may be taken: the door stands it up first (ZombieEffects.Move). The hound
    /// itself must stand free: not held, not pinned, not executing, not on a ladder, a ledge or a vault, and not in a
    /// showdown already.
    /// </summary>
    public static string Refusal(PlayerMovement hound, out EnemyHealth target)
    {
        target = null;
        if (hound == null || !hound.IsLocal) return NoFoe;
        if (Busy(hound) || hound.IsDead || hound.IsDowned || hound.IsHeld || hound.IsPinned || hound.IsParkour
            || (hound.Execution != null && hound.Execution.IsExecuting)) return NotNow;
        var ping = hound.GetComponent<PlayerPing>();
        target = ping != null ? ping.AimedFoe() as EnemyHealth : null;
        if (target == null || !target.IsAlive) { target = null; return NoFoe; }
        if ((target.TryGetComponent(out MeleeCombatTarget melee) && melee.IsHeld) || (target.TryGetComponent(out ZombieGrip grip) && grip.IsHolding))
            return NotNow;
        return null;
    }

    /// <summary>X: this PC's hound calls its showdown on the zombie under its crosshair. False when it can't now (Refusal).</summary>
    public static bool Begin(PlayerMovement hound)
    {
        if (Refusal(hound, out var target) != null) return false;
        if (!hound.TryGetComponent(out HoundShowdown showdown)) showdown = hound.gameObject.AddComponent<HoundShowdown>();
        showdown.movement = hound;
        local = showdown;
        showdown.Call(target);
        return true;
    }

    private void Call(EnemyHealth target)
    {
        foe = target;
        seconds = Mathf.Max(1f, PlayerClass.Ability(Row, "arenaSeconds", 12f));
        float pull = Mathf.Clamp(PlayerClass.Ability(Row, "arenaPull", .35f), 0f, 2f);
        float apart = Mathf.Clamp(PlayerClass.Ability(Row, "arenaApart", 10f), 2f, HoundArena.Radius * 2f - 3f);
        var network = GameSession.Network;
        seat = (byte)(network != null && network.IsListening ? network.LocalClientId % 32 : 0ul);
        houndFrom = transform.position;
        foeFrom = foe.transform.position;
        houndYaw = transform.eulerAngles.y;
        foeYaw = foe.transform.eulerAngles.y;
        foeTaken = false;
        // Each keeps its side: the hound stands where it stood from the zombie, the zombie across the floor from it.
        Vector3 way = Vector3.ProjectOnPlane(houndFrom - foeFrom, Vector3.up);
        if (way.sqrMagnitude < 1e-4f) way = -Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        way = way.sqrMagnitude > 1e-6f ? way.normalized : Vector3.back;
        center = HoundArena.CenterUnder(houndFrom, seat);
        houndTo = center + way * (apart * .5f);
        foeTo = center - way * (apart * .5f);
        ace = PlayerPerks.Has(PlayerPerks.HoundAce);
        pullAt = Time.time + pull;
        until = pullAt + seconds;
        wonAt = -1f;
        phase = Phase.Pulling;
        // Its foe carries the bounty mark, as a duel's foe does: its kill is a bounty, a Desperado round in its head ends it.
        var ping = GetComponent<PlayerPing>();
        if (ping != null) ping.Mark(foe, true);
        if (TryGetComponent(out ClassKit kit)) kit.CalledOut(foe, pull + seconds);
        ArenaNet.Send(Packet(ArenaNet.Step.Begin, Ending.Won, false, pull), movement, foe);
        MomentHUD.Tag(Texts.Get("moment.tag.showdown.word"), Texts.Format("moment.tag.showdown.under", seconds), PingMarks.BountyColor,
            MomentLook.Sigil.Hound);
    }

    private ArenaNet.Packet Packet(ArenaNet.Step step, Ending how, bool back, float pull) => new ArenaNet.Packet
    {
        Step = (byte)step, How = (byte)how, Seat = seat, Ace = ace, Back = back, Pull = pull, Seconds = seconds, FoeYaw = foeYaw,
        Center = center, HoundFrom = houndFrom, FoeFrom = foeFrom, HoundTo = houndTo, FoeTo = foeTo
    };

    private void Update()
    {
        if (phase == Phase.None || movement == null) return;
        float now = Time.time;
        if (phase == Phase.Pulling)
        {
            if (movement.IsDead || movement.IsDowned) { Finish(Ending.Fell, false); return; }
            if (now < pullAt) return;
            // Its foe fell before it could be taken (someone else's shot): no arena, but the crowd still roars for the claim.
            if (foe == null || !foe.IsAlive) { Finish(Ending.Won, false); return; }
            Go(houndTo, Heading(foeTo - houndTo));
            // The zombie, through the zombie session's door, once from this PC: the host puts it there facing the hound and
            // its copies jump with it.
            ZombieEffects.Move(foe, foeTo, Quaternion.Euler(0f, Heading(houndTo - foeTo), 0f));
            foeTaken = true;
            ArenaFx.Arrive(houndTo, 1.8f, true);
            ArenaFx.Arrive(foeTo, 1.8f, false);
            phase = Phase.Fighting;
            until = now + seconds;
            AlertHUD.Self(Texts.Format("class.schiesshund.arena.self", Mathf.RoundToInt(seconds)));
            return;
        }
        if (movement.IsDead) { Finish(Ending.Fell, false); return; }
        if (movement.IsDowned) { Finish(Ending.Fell, true); return; }
        if (foe == null || !foe.IsAlive)
        {
            // A moment to see it fall, then out.
            if (wonAt < 0f) wonAt = now;
            float due = wonAt + Mathf.Clamp(PlayerClass.Ability(Row, "arenaLinger", 1f), 0f, 5f);
            if (now >= due && Free(now - due)) Finish(Ending.Won, true);
            return;
        }
        if (now >= until && Free(now - until)) Finish(Ending.Time, true);
    }

    // Free to be taken back: not held, not in the middle of an execution; a runner's pin is thrown off here. Waited for
    // a few seconds at most.
    private bool Free(float waited)
    {
        if (waited >= MostHeld) return true;
        if (movement.Execution != null && movement.Execution.IsExecuting) return false;
        if (movement.IsPinned && TryGetComponent(out PlayerHold hold)) hold.ShovedFree(.8f);
        return !movement.IsHeld;
    }

    private void Finish(Ending how, bool back)
    {
        phase = Phase.None;
        if (back) Go(houndFrom, houndYaw);
        // Still standing down there (time ran out, or the hound fell): the zombie goes back to its own spot, as it faced.
        if (foeTaken && foe != null && foe.IsAlive) ZombieEffects.Move(foe, foeFrom, Quaternion.Euler(0f, foeYaw, 0f));
        foeTaken = false;
        ArenaNet.Send(Packet(ArenaNet.Step.End, how, back, 0f), movement, foe);
        foe = null;
        if (how == Ending.Fell) return;
        cheerTotal = Mathf.Max(.5f, PlayerClass.Ability(Row, "arenaBuffSeconds", 10f));
        cheerUntil = Time.time + cheerTotal;
        cheerDamage = Mathf.Max(0f, PlayerClass.Ability(Row, "arenaBuffDamage", 50f));
        if (TryGetComponent(out ClassKit kit)) kit.LoadDesperado();
        AlertHUD.Self(Texts.Format("class.schiesshund.arena.back", Mathf.RoundToInt(cheerDamage), Mathf.RoundToInt(cheerTotal)));
    }

    // At once, facing that way; for the other PCs a jump, never a slide down through the map. A ladder, a ledge or a
    // vault caught in the pull's moment lets go first, so it can't hold the hound to where it was.
    private void Go(Vector3 to, float heading)
    {
        if (movement.IsParkour) movement.ChangeState(PlayerMovement.MovementState.Standing);
        movement.MoveTo(to + Vector3.up * .05f);
        if (TryGetComponent(out PlayerLook look)) look.FaceYaw(heading);
        if (TryGetComponent(out NetworkTransform motion) && motion.IsSpawned && motion.CanCommitToTransform)
            motion.Teleport(transform.position, transform.rotation, transform.localScale);
    }

    private static float Heading(Vector3 way)
    {
        way.y = 0f;
        return way.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(way).eulerAngles.y : 0f;
    }
}
