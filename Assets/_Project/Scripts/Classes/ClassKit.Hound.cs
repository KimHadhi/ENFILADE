using UnityEngine;

// ClassKit: Schießhund: the bounty, Return Back, the Desperado, and its cards Blood Scent, Dead or Alive and Quick Draw. One part of the class; the rest is in ClassKit.cs and the files beside it.
public sealed partial class ClassKit
{
    // The Schießhund's cards with code here (the user's picks on the card page, 2026-11-14).
    private const string HoundTrailCard = "hound_trail", HoundDoaCard = "hound_doa";
    // The foe its last duel or showdown called out, and until when, for Dead or Alive.
    private Component duelFoe;
    private float duelEnds = -99f;
    // What a look round a practice target's kill finds (Blood Scent on the hub's range).
    private static readonly Collider[] trailHits = new Collider[64];

    /// <summary>
    /// This Schießhund called <paramref name="foe"/> out for <paramref name="seconds"/>: its class key's duel, or its
    /// showdown (HoundShowdown). Its fall at the hound's hand within that time cuts the duel's wait (Dead or Alive).
    /// </summary>
    internal void CalledOut(Component foe, float seconds)
    {
        duelFoe = foe;
        duelEnds = Time.time + Mathf.Max(0f, seconds);
    }

    /// <summary>
    /// A factor on how long <paramref name="who"/>'s gun takes to come out (PlayerClass.DrawFactor): this PC's hound's
    /// Desperado comes out in ClassAbilities Schiesshund.desperadoDraw of its time (the 빠른 뽑기 card halves it: the user's
    /// pick, 2026-11-14), every other gun in its own time.
    /// </summary>
    internal static float DesperadoDraw(PlayerMovement who)
    {
        var kit = Local;
        if (kit == null || who == null || kit.movement != who || kit.weapon == null) return 1f;
        var desperado = kit.Desperado;
        return desperado != null && kit.weapon.Runtime == desperado ? Mathf.Clamp(PlayerClass.Ability(HoundRow, "desperadoDraw", 1f), .05f, 1f) : 1f;
    }

    // Dead or Alive (데드 오어 얼라이브, a card): the foe its duel called out fell at its hand while the duel lasted, so the
    // next contract comes sooner: what is left of the duel's wait is cut by the card's share, all of it at the top level.
    private void DeadOrAlive(Component target)
    {
        if (duelFoe == null || target == null || duelFoe.gameObject != target.gameObject || Time.time > duelEnds + .5f) return;
        duelFoe = null;
        float share = Mathf.Clamp01(PlayerPerks.Total(HoundDoaCard) / 100f), left = duelReadyAt - Time.time;
        if (share > 0f && left > 0f) duelReadyAt = Time.time + left * (1f - share);
    }

    // Blood Scent (피 냄새, a card): the mark's kill puts the mark at once on the nearest foe as big or bigger within
    // reach (PlayerClass.Grade), so the hunt runs on without a press of F; with nothing near enough it simply ends. The
    // mark goes as F's does, for everyone to see.
    private void Trail(Component dead)
    {
        if (!PlayerPerks.Has(HoundTrailCard) || dead == null) return;
        float reach = Mathf.Max(0f, PlayerClass.Ability(HoundRow, "trailReach", 15f));
        if (reach <= 0f) return;
        int grade = PlayerClass.Grade(dead);
        Vector3 at = dead.transform.position;
        Component next = null;
        float best = reach * reach;
        foreach (var enemy in EnemyHealth.Living) Consider(enemy, dead, grade, at, ref next, ref best);
        // A practice target's kill on the hub's range looks among the range's targets round it.
        if (dead is ICombatFoe practice && practice.IsPractice)
        {
            int count = Physics.OverlapSphereNonAlloc(at, reach, trailHits, ~0, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++) Consider(CombatFoe.Of(trailHits[i]), dead, grade, at, ref next, ref best);
        }
        if (next == null) return;
        var ping = GetComponent<PlayerPing>();
        if (ping != null) ping.Mark(next, true);
    }

    private static void Consider(Component foe, Component dead, int grade, Vector3 at, ref Component next, ref float best)
    {
        if (foe == null || foe.gameObject == dead.gameObject || !CombatFoe.IsAlive(foe) || PlayerClass.Grade(foe) < grade) return;
        float far = (foe.transform.position - at).sqrMagnitude;
        if (far > best) return;
        best = far;
        next = foe;
    }

    /// <summary>
    /// A Desperado round is going into the head of this Schießhund's own mark (PlayerClass.Finish, as AttackTrace weighs
    /// the hit): when it dies of it, BOUNTY.
    /// </summary>
    internal void Finishing(Component target, Vector3 point, Vector3 from)
    {
        finishing = target;
        finishAt = Time.time;
        finishPoint = point;
        finishFrom = from;
    }

    // Its own mark died at its hand (a zombie, or a range target in the hub): points toward the Desperado's next round
    // (an elite's count more), a Return Back stack, and the bounty's look and sound (its money is CombatFeedHUD's, with
    // the kill, which a practice kill never reaches).
    private void Bounty(Component target)
    {
        var enemy = target as EnemyHealth;
        EarnRounds(Mathf.Max(1, Mathf.RoundToInt(PlayerClass.IsElite(enemy) ? PlayerClass.Ability(HoundRow, "elitePoints", 2f) : 1f)));
        returnStacks = Mathf.Min(Mathf.Max(1, Mathf.RoundToInt(PlayerClass.Ability(HoundRow, "returnMax", 5f))), ReturnStacks + 1);
        returnTotal = Mathf.Max(.1f, PlayerClass.Ability(HoundRow, "returnSeconds", 8f));
        returnUntil = Time.time + returnTotal;
        bountyAt = Time.time;
        bool claimed = finishing == target && Time.time - finishAt < 1f;
        finishing = null;
        KitSound.Play(KitSound.Cue.Bounty, claimed ? 1f : .7f);
        // In the world, for everyone: the Desperado's grand burst where its round went in, or a plain bounty's coins.
        Vector3 ground = target.transform.position;
        Vector3 head = claimed ? finishPoint : CombatFoe.Top(target) - Vector3.up * .15f;
        Vector3 from = claimed ? finishFrom : transform.position + Vector3.up * 1.5f;
        BountyBurst.Play(head, from, ground, claimed);
        Claimed?.Invoke(head, from, ground, claimed);
        // The hunter sees the claim in the world as everyone does (the special-moment picks), no banner over the screen.
        DeadOrAlive(target);
        Trail(target);
    }

    // A zombie its own bounty dye splashed died at its hand (DyeMarks; the user's pick, "현상금 염료탄"): points toward
    // the Desperado's next round, as a little bounty, without a bounty's look, sound or Return Back. A dyed elite that
    // also carries the real mark counts as the mark's kill (OnDamaged asks for the mark first).
    private void DyedKill(Component target) => EarnRounds(DyeMarks.KillPoints(target, movement));

    // Points toward the Desperado's next round: every roundPoints of them load one, while the cylinder has room.
    private void EarnRounds(int points)
    {
        if (points <= 0) return;
        bountyPoints += points;
        int need = Mathf.Max(1, Mathf.RoundToInt(PlayerClass.Ability(HoundRow, "roundPoints", 2f)));
        var desperado = Desperado;
        while (bountyPoints >= need)
        {
            bountyPoints -= need;
            if (desperado != null && desperado.Ammo < RoundMax) desperado.LoadRound();
        }
        slots.OrNull()?.NotifyAmmo();
    }

    // The class key on a foe: a duel with it (HoundDuel). Refused while it waits, or with no foe under the crosshair.
    private void Duel()
    {
        if (Time.time < duelReadyAt) { KitSound.Play(KitSound.Cue.Refuse, .5f); return; }
        var ping = GetComponent<PlayerPing>();
        var foe = ping != null ? ping.AimedFoe() : null;
        if (foe == null) { KitSound.Play(KitSound.Cue.Refuse, .4f); return; }
        float seconds = Mathf.Max(.5f, PlayerClass.Ability(HoundRow, "duelSeconds", 12f));
        duelWait = Mathf.Max(seconds, PlayerClass.Ability(HoundRow, "duelCooldown", 30f));
        duelReadyAt = Time.time + duelWait;
        bool ace = PlayerPerks.Has(PlayerPerks.HoundAce);
        // Its foe carries the bounty mark too, whatever it was on: the duel's kill is a bounty, and a Desperado round in
        // its head ends it.
        ping.Mark(foe, true);
        HoundDuel.Begin(movement, foe, seconds, ace);
        CalledOut(foe, seconds);
        Dueled?.Invoke(foe, seconds, ace);
        KitSound.Play(KitSound.Cue.Bounty, .8f);
        // Its tag on this screen (the special-moment pick A); the line and the brackets are HoundDuel's, for everyone.
        MomentHUD.Duel(seconds);
        AlertHUD.Self(Texts.Get(ace ? "class.schiesshund.duel.ace" : "class.schiesshund.duel.self"));
    }
}
