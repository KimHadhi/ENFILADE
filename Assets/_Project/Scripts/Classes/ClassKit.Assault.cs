using UnityEngine;

// ClassKit: Assault: adrenaline. One part of the class; the rest is in ClassKit.cs and the files beside it.
public sealed partial class ClassKit
{
    // Any foe's kill by this player: a zombie's, or a practice target's on the hub's range, where the classes are tried
    // out (the user, 2026-11-12); a practice kill stays out of the run's counts elsewhere (GunFeelDirector).
    private void OnDamaged(CombatDamageReport report)
    {
        if (movement == null || !movement.IsLocal || !report.IsKillingBlow || !(report.Target is ICombatFoe)) return;
        if (!PlayerPerks.IsMine(report.Hit.Source)) return;
        if (PlayerClass.Is(PlayerClass.Schiesshund) && PingMarks.IsBountyOf(report.Target, movement)) Bounty(report.Target);
        else if (PlayerClass.Is(PlayerClass.Schiesshund) && DyeMarks.IsDyedBy(report.Target, movement)) DyedKill(report.Target);
        if (!PlayerClass.Is(PlayerClass.Assault)) return;
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
}
