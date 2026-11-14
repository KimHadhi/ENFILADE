using System.Collections.Generic;
using UnityEngine;

// ClassKit: Assault: adrenaline, its CHARGE, and the cards that ride on them. One part of the class; the rest is in ClassKit.cs and the files beside it.
public sealed partial class ClassKit
{
    // The CHARGE this PC's Assault sounded last, as its PC reckoned it (돌격 나팔 lengthens it): the friends' PCs are told
    // these with it (PlayerNetworkCombat.ChargeRpc), so it lasts and reaches as far on every screen as on the Assault's.
    private float soundedSeconds = 7f, soundedReach = 15f;
    // 제압 사격: the shot under way (its pellets come one by one) and the zombies it has pinned down already, once each.
    private long suppressShot = -1;
    private readonly HashSet<EnemyHealth> suppressed = new HashSet<EnemyHealth>();
    private readonly List<EnemyHealth> pinning = new List<EnemyHealth>();

    /// <summary>How long and how far this PC's player's last CHARGE went, as their PC sends it with the CHARGE.</summary>
    public float SoundedSeconds => soundedSeconds;
    public float SoundedReach => soundedReach;

    // Any foe's kill by this player: a zombie's, or a practice target's on the hub's range, where the classes are tried
    // out (the user, 2026-11-12); a practice kill stays out of the run's counts elsewhere (GunFeelDirector).
    private void OnDamaged(CombatDamageReport report)
    {
        if (movement == null || !movement.IsLocal || !report.IsKillingBlow || !(report.Target is ICombatFoe)) return;
        if (!PlayerPerks.IsMine(report.Hit.Source))
        {
            // 엄호 사격 (assault_cover): in a CHARGE a friend's kill counts toward this Assault's adrenaline as its own
            // does. Every PC hears of every kill with whose it was (EnemyNetwork's ShowHit; the host its guests' as they
            // send them), so nothing new travels for it.
            if (Covered(report.Hit.Source)) Stack();
            return;
        }
        if (PlayerClass.Is(PlayerClass.Schiesshund) && PingMarks.IsBountyOf(report.Target, movement)) Bounty(report.Target);
        else if (PlayerClass.Is(PlayerClass.Schiesshund) && DyeMarks.IsDyedBy(report.Target, movement)) DyedKill(report.Target);
        if (!PlayerClass.Is(PlayerClass.Assault)) return;
        Stack();
    }

    // A friend's kill while this Assault charges, with the 엄호 사격 card held.
    private bool Covered(GameObject source)
        => source != null && PlayerClass.Is(PlayerClass.Assault) && PlayerPerks.Has(PlayerPerks.AssaultCover) && chargeUntil > Time.time
            && source.GetComponentInParent<PlayerMovement>() is PlayerMovement friend && friend != movement;

    // One stack more, held a while from now before it ebbs.
    private void Stack()
    {
        stacks = Mathf.Min(StackMax, stacks + 1);
        lastKill = Time.time;
        nextEbb = lastKill + PlayerClass.Ability(AssaultRow, "stackHold", 6f);
    }

    // Past its hold, adrenaline ebbs a stack at a time.
    private void Ebb()
    {
        if (stacks <= 0 || Time.time < nextEbb) return;
        stacks--;
        nextEbb = Time.time + Mathf.Max(.1f, PlayerClass.Ability(AssaultRow, "stackDecay", 1.5f));
    }

    /// <summary>
    /// An Assault's class key pressed (or tapped, with 데인저 클로즈 held: ClassCards hands the tap back here): full
    /// adrenaline is spent on a CHARGE, else the key only refuses.
    /// </summary>
    internal void TryCharge()
    {
        if (!PlayerClass.Is(PlayerClass.Assault)) return;
        if (stacks < StackMax) { KitSound.Play(KitSound.Cue.Refuse, .5f); return; }
        stacks = 0;
        Vector3 at = transform.position;
        // Its time and reach as this PC reckons them, the 돌격 나팔 card's seconds in them (Assault.chargeSeconds), and the
        // same numbers to every friend (Charged, PlayerNetworkCombat.ChargeRpc).
        soundedSeconds = Mathf.Max(.1f, PlayerClass.Ability(AssaultRow, "chargeSeconds", 7f));
        soundedReach = Mathf.Max(0f, PlayerClass.Ability(AssaultRow, "chargeRadius", 15f));
        ReceiveCharge(at, Texts.Get("class.assault.charge.self"), soundedSeconds, soundedReach);
        // Everyone it takes glows gold on this PC (the friends' PCs light them as they hear of it: ChargeRpc).
        ChargeGlow.Sounded(at, soundedSeconds, soundedReach);
        MomentHUD.Charge(soundedSeconds, soundedReach);
        Charged?.Invoke(at);
        // The phantom cavalry card rides out with it (ClassCards).
        cards.OrNull()?.Charged(at);
    }

    /// <summary>
    /// An Assault's ultimate (ClassCards.Ultimate): adrenaline full while the A-10 makes its run, the CHARGE theirs to
    /// sound when they like (its riders with it, the 러프 라이더스 card held). It used to sound the CHARGE at once as well; the
    /// run is the moment now, and one big moment at a time reads better (the user's restraint: memory vfx-restraint).
    /// </summary>
    internal void UltimateAdrenaline()
    {
        if (!PlayerClass.Is(PlayerClass.Assault)) return;
        stacks = StackMax;
        lastKill = Time.time;
        nextEbb = lastKill + PlayerClass.Ability(AssaultRow, "stackHold", 6f);
    }

    /// <summary>
    /// An Assault in a CHARGE barrels on: no grab, leap or hook takes hold of them while it lasts (PlayerHold asks, on their
    /// own PC; ClassAbilities Assault.chargeUnholdable). The user asked on the card page (2026-11-14, of 선두): "CHARGE
    /// 중엔 그랩기 안 당하는 거 맞지?". It wasn't so until then. A CHARGE that took them as a friend counts too; lying down
    /// it does not.
    /// </summary>
    public static bool Unholdable(PlayerMovement player)
    {
        var kit = Local;
        return player != null && kit != null && kit.movement == player && PlayerClass.Is(PlayerClass.Assault)
            && kit.chargeUntil > Time.time && !player.IsDowned && PlayerClass.Ability(AssaultRow, "chargeUnholdable", 1f) > .5f;
    }

    /// <summary>
    /// 선두 (assault_lead): the share of a blow this PC's Assault takes in a CHARGE (1 without the card or out of one).
    /// PlayerClass.TakenFactor multiplies it in on their own PC, where their hurt is judged.
    /// </summary>
    public static float LeadShare(PlayerMovement player)
    {
        var kit = Local;
        if (player == null || kit == null || kit.movement != player || !PlayerClass.Is(PlayerClass.Assault) || kit.chargeUntil <= Time.time
            || !PlayerPerks.Has(PlayerPerks.AssaultLead)) return 1f;
        return Mathf.Clamp01(1f - PlayerPerks.Total(PlayerPerks.AssaultLead) / 100f);
    }

    // 제압 사격 (assault_suppress; the user, 2026-11-14: "총 갈기고 있으면 적 좀비가 위축하는 느낌"): every round this PC's
    // Assault fires pins down the zombies it hits or passes close by, each once a shot. Pinned, a zombie walks and winds
    // up slower a moment and cowers (ZombieEffects.Suppress, the zombie session's door: called here on the shooter's PC
    // once, it reaches the host and the other PCs by itself); a brute or a named one half as much. Rounds keep it up; a
    // round that passes far off does nothing.
    private void OnFire(WeaponShot shot)
    {
        if (movement == null || !movement.IsLocal || !PlayerClass.Is(PlayerClass.Assault) || !PlayerPerks.Has(PlayerPerks.AssaultSuppress)) return;
        float seconds = PlayerClass.Ability(AssaultRow, "suppressSeconds", 1.5f);
        if (seconds <= 0f) return;
        if (shot.AttackId != suppressShot)
        {
            suppressShot = shot.AttackId;
            suppressed.Clear();
        }
        float reach = Mathf.Max(0f, PlayerClass.Ability(AssaultRow, "suppressReach", 1.5f));
        pinning.Clear();
        // The round's way: from the muzzle to where it stopped, then each stretch it went on through or off something.
        Vector3 a = shot.Origin, b = shot.Trace.End;
        Near(a, b, reach);
        if (shot.Later != null)
            foreach (var leg in shot.Later)
            {
                a = b;
                b = leg.Trace.End;
                Near(a, b, reach);
            }
        // What it hit is pinned however big (a brute's bone hit at its edge may be off its middle by more than the reach).
        if (shot.Trace.HasHit && shot.Trace.Hit.collider != null && shot.Trace.Hit.collider.GetComponentInParent<EnemyHealth>() is EnemyHealth hit
            && hit.IsAlive && suppressed.Add(hit)) pinning.Add(hit);
        if (pinning.Count == 0) return;
        float walk = Mathf.Clamp01(PlayerPerks.Total(PlayerPerks.AssaultSuppress) / 100f);
        float windup = Mathf.Max(0f, PlayerClass.Ability(AssaultRow, "suppressWindup", 30f) / 100f);
        float firm = Mathf.Clamp01(PlayerClass.Ability(AssaultRow, "suppressFirm", 50f) / 100f);
        foreach (var enemy in pinning)
        {
            // A brute or a named one takes a share of it (the door applies the numbers as given).
            bool big = enemy.Rank == EnemyRank.Named || (enemy.TryGetComponent(out MeleeCombatTarget melee) && melee.Steadfast);
            float share = big ? firm : 1f;
            ZombieEffects.Suppress(enemy, seconds, walk * share, windup * share);
        }
    }

    // The zombies within reach of one stretch of a round's way, not yet pinned by this shot: their bodies taken as a line
    // from the feet to the head, so a round over a head or past the legs counts as one past the chest does.
    private void Near(Vector3 a, Vector3 b, float reach)
    {
        float most = reach * reach;
        Vector3 low = Vector3.Min(a, b) - Vector3.one * (reach + 1.8f), high = Vector3.Max(a, b) + Vector3.one * (reach + .2f);
        foreach (var enemy in EnemyHealth.Living)
        {
            if (enemy == null || !enemy.IsAlive || suppressed.Contains(enemy)) continue;
            Vector3 feet = enemy.transform.position;
            if (feet.x < low.x || feet.y < low.y || feet.z < low.z || feet.x > high.x || feet.y > high.y || feet.z > high.z) continue;
            if (Gap(a, b, feet + Vector3.up * .2f, feet + Vector3.up * 1.7f) > most) continue;
            suppressed.Add(enemy);
            pinning.Add(enemy);
        }
    }

    // The squared distance between two stretches, segments p1-q1 and p2-q2 at their closest (Ericson, Real-Time Collision
    // Detection, 5.1.9).
    private static float Gap(Vector3 p1, Vector3 q1, Vector3 p2, Vector3 q2)
    {
        Vector3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
        float a = Vector3.Dot(d1, d1), e = Vector3.Dot(d2, d2), f = Vector3.Dot(d2, r), s, t;
        if (a <= 1e-8f && e <= 1e-8f) return r.sqrMagnitude;
        if (a <= 1e-8f)
        {
            s = 0f;
            t = Mathf.Clamp01(f / e);
        }
        else
        {
            float c = Vector3.Dot(d1, r);
            if (e <= 1e-8f)
            {
                t = 0f;
                s = Mathf.Clamp01(-c / a);
            }
            else
            {
                float b = Vector3.Dot(d1, d2), denominator = a * e - b * b;
                s = denominator > 1e-8f ? Mathf.Clamp01((b * f - c * e) / denominator) : 0f;
                t = (b * s + f) / e;
                if (t < 0f)
                {
                    t = 0f;
                    s = Mathf.Clamp01(-c / a);
                }
                else if (t > 1f)
                {
                    t = 1f;
                    s = Mathf.Clamp01((b - c) / a);
                }
            }
        }
        return (p1 + d1 * s - (p2 + d2 * t)).sqrMagnitude;
    }
}
