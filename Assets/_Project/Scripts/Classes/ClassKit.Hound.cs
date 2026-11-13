using UnityEngine;

// ClassKit: Schießhund: the bounty, Return Back, the Desperado. One part of the class; the rest is in ClassKit.cs and the files beside it.
public sealed partial class ClassKit
{
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
        slots?.NotifyAmmo();
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
        Dueled?.Invoke(foe, seconds, ace);
        KitSound.Play(KitSound.Cue.Bounty, .8f);
        // Its tag on this screen (the special-moment pick A); the line and the brackets are HoundDuel's, for everyone.
        MomentHUD.Duel(seconds);
        AlertHUD.Self(Texts.Get(ace ? "class.schiesshund.duel.ace" : "class.schiesshund.duel.self"));
    }
}
