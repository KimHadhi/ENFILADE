using System.Collections.Generic;
using UnityEngine;

// ZombieMind: what class cards and ultimates do to what a zombie knows. One part of the class; the rest is in
// ZombieMind.cs and the files beside it. The cards never call in here: ZombieEffects is their door, and it carries each
// call to the PCs that need it. Numbers: Tables/ZombieSenses (deathNotice, deathSuspicion, gasCough, gasStun).
public sealed partial class ZombieMind
{
    [Header("Cards: a death nearby")]
    [SerializeField, Min(0f), Tooltip("A zombie that isn't awake notices another die this near (m): half as far through a wall, less far eating or dozing. 0: nobody notices.")]
    private float deathNotice = 4f;
    [SerializeField, Range(0f, 1f), Tooltip("How much surer than alertAt the death makes it: it stares at the spot until that has worn off (forget a second), then walks over to look. It never wakes it by itself.")]
    private float deathSuspicion = .25f;
    [Header("Cards: blinding gas")]
    [SerializeField, Min(.1f), Tooltip("In blinding gas it coughs about this often (s).")]
    private float gasCough = 1.2f;
    [SerializeField, Min(0f), Tooltip("Each cough stops it this long (s): no walking, no swing.")]
    private float gasStun = .7f;

    // Gas reaches this far above and below where it burst (m), as the antiseptic mist does.
    private const float GasHeight = 3f;
    // In gas its walk stays slowed this long past each think (s), so the slow never lapses between two thinks.
    private const float GasLinger = .15f;

    private struct Gas
    {
        public Vector3 At;
        public float Radius, Until, Slow;
    }

    // Players no zombie's eyes make out for now and until when (game time), and when the last of them ends.
    private static readonly Dictionary<PlayerMovement, float> unseen = new Dictionary<PlayerMovement, float>();
    private static float unseenLast = float.NegativeInfinity;
    // Blinding gas hanging now, on this PC (every PC has every cloud).
    private static readonly List<Gas> gases = new List<Gas>();

    private EnemyHitReaction reaction;
    private float coughAt;
    // In blinding gas as of its last think (where it thinks).
    private bool gassed;

    internal float GasCough => gasCough;
    internal float GasStun => gasStun;

    private static void ClearCards()
    {
        unseen.Clear();
        unseenLast = float.NegativeInfinity;
        gases.Clear();
    }

    // ---- A death nearby --------------------------------------------------------------------------------------

    /// <summary>
    /// Where zombies think (the host, or alone): this zombie just died to this hit, and those standing by that aren't
    /// awake notice it. They turn and stare at the spot, and when nothing more comes of it they walk over to look, as
    /// they do for anything they are unsure of. A suppressed shot used to drop a zombie beside another that never
    /// turned its head (the user, 2026-11-14: "소음기 끼고 총으로 쏘면 바로 옆의 좀비가 죽어도 신경 안 쓰긴 하거든").
    /// It makes them surer of nothing but the spot: nobody is woken, and whoever killed it is only shown on their own
    /// stealth HUD that this one noticed. Not for a silent kill: an execution, a takedown, or a hit the killer's PC
    /// marked silent (DamageHit.Silent: a Ghost's marked headshot).
    /// </summary>
    internal static void DeathSeen(EnemyHealth dead, in DamageHit hit)
    {
        if (dead == null || hit.Silent || hit.Kind == DamageKind.Execution || minds.Count < 2) return;
        Vector3 at = dead.transform.position + Vector3.up;
        var killer = hit.Source != null ? hit.Source.GetComponentInParent<PlayerMovement>() : null;
        foreach (var mind in minds)
        {
            if (mind == null || mind.health == dead || !mind.Thinks || mind.horde || mind.IsAwake || mind.caged) continue;
            if (mind.melee != null && mind.melee.IsStunned) continue;
            if (mind.NoticesDeath(at)) mind.Suspect(at, killer);
        }
    }

    // Near enough to notice a body drop there: within deathNotice of its ears, half that through a wall (as a sound).
    private bool NoticesDeath(Vector3 at)
    {
        float reach = deathNotice * BusyHear;
        if (reach <= 0f) return false;
        Vector3 ear = Eye();
        float distance = Vector3.Distance(at, ear);
        if (distance > reach) return false;
        return distance <= .01f || distance <= reach * wallShare
            || !Physics.Raycast(at, (ear - at) / distance, distance, Hearing, QueryTriggerInteraction.Ignore);
    }

    // Unsure of something at this spot, though of nobody: it stops and turns to it, and once that has worn off without
    // its making anyone out, it goes over to look (Think takes an alert that fades into a search).
    private void Suspect(Vector3 at, PlayerMovement by)
    {
        suspicion = Mathf.Max(suspicion, Mathf.Min(.95f, alertAt + deathSuspicion));
        glanceUntil = 0f;
        lured = false;
        if (noticed == null && Alive(by)) noticed = by;
        Alert(at);
    }

    // ---- A player it cannot see ------------------------------------------------------------------------------

    /// <summary>No zombie's eyes make this player out now (ZombieEffects.Unseen): their sounds and their touch still count.</summary>
    public static bool IsUnseen(PlayerMovement player)
        => Time.time < unseenLast && player != null && unseen.TryGetValue(player, out float until) && Time.time < until;

    /// <summary>Someone is unseen now (EnemyTargeting then picks only among those it can see).</summary>
    internal static bool AnyUnseen => Time.time < unseenLast;

    /// <summary>
    /// Every PC (ZombieEffects.Unseen): for this long no zombie sees this player. Where zombies think, whoever was
    /// after them loses them at once, as in smoke: it goes to look where it last knew them, a horde zombie too, and has
    /// to make them out again by sound, or by touch (close enough before its eyes).
    /// </summary>
    internal static void Unsee(PlayerMovement player, float seconds)
    {
        if (player == null || seconds <= 0f) return;
        // Asked before they are unseen: who it is after is worked out by its eyes too (EnemyTargeting).
        foreach (var mind in minds)
        {
            if (mind == null || !mind.Thinks || !mind.IsAwake || !mind.BeyondTouch(player)) continue;
            if (!mind.TryGetComponent(out EnemyTargeting targeting) || targeting.Player != player) continue;
            mind.Lose(player.transform.position);
        }
        float until = Time.time + seconds;
        unseen[player] = until;
        unseenLast = Mathf.Max(unseenLast, until);
    }

    /// <summary>Its eyes can't make this player out now: unseen (ZombieEffects.Unseen) and not close enough to touch.</summary>
    internal bool CannotSee(PlayerMovement player) => IsUnseen(player) && BeyondTouch(player);

    private bool BeyondTouch(PlayerMovement player)
    {
        Vector3 flat = player.transform.position - transform.position;
        flat.y = 0f;
        return flat.sqrMagnitude > touchRange * touchRange;
    }

    // ---- Blinding gas ------------------------------------------------------------------------------------------

    /// <summary>Some blinding gas may hang now, on this PC (a quick no for the frames when none does).</summary>
    internal static bool AnyGas => gases.Count > 0;

    /// <summary>Every PC (ZombieEffects.Blind): blinding gas hangs here, this wide, for this long; zombies in it walk this share slower.</summary>
    internal static void AddGas(Vector3 at, float radius, float seconds, float slow)
    {
        if (radius <= 0f || seconds <= 0f) return;
        gases.Add(new Gas { At = at, Radius = radius, Until = Time.time + seconds, Slow = Mathf.Clamp01(slow) });
    }

    /// <summary>Whether this spot is in blinding gas now (every PC), and the most any cloud over it slows a zombie.</summary>
    public static bool InGas(Vector3 point, out float slow)
    {
        slow = 0f;
        bool inside = false;
        for (int i = gases.Count - 1; i >= 0; i--)
        {
            var gas = gases[i];
            if (Time.time >= gas.Until) { gases.RemoveAt(i); continue; }
            Vector3 flat = point - gas.At;
            if (Mathf.Abs(flat.y) > GasHeight) continue;
            flat.y = 0f;
            if (flat.sqrMagnitude > gas.Radius * gas.Radius) continue;
            inside = true;
            slow = Mathf.Max(slow, gas.Slow);
        }
        return inside;
    }

    // Where it thinks, each think: in blinding gas it has lost whoever it was after and makes out nothing while it
    // stays in it, by eye or ear (a horde zombie too: it has to find someone again once it is out, as after smoke). It
    // coughs, each cough stopping it a moment (its copies show their own: ZombieBody.CoughTick), and what walking it
    // does is slower. True while it is in the gas: its think ends there.
    private bool GasThink(float step)
    {
        float slow = 0f;
        gassed = gases.Count > 0 && InGas(transform.position, out slow);
        if (!gassed) return false;
        if (slow > 0f && (reaction != null || TryGetComponent(out reaction))) reaction.Slow(step + GasLinger, slow);
        if (IsAwake) Lose(transform.position);
        if (melee != null && Time.time >= coughAt)
        {
            coughAt = Time.time + gasCough * Random.Range(.8f, 1.2f);
            if (gasStun > 0f) melee.Interrupt(gasStun);
        }
        return true;
    }
}
