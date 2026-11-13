using UnityEngine;

// ClassKit: Knight: the taunt. One part of the class; the rest is in ClassKit.cs and the files beside it.
public sealed partial class ClassKit
{
    private void Taunt()
    {
        if (Time.time < tauntReadyAt) { KitSound.Play(KitSound.Cue.Refuse, .5f); return; }
        tauntTotal = Mathf.Max(.1f, PlayerClass.Ability(KnightRow, "tauntSeconds", 6f));
        tauntWait = Mathf.Max(tauntTotal, PlayerClass.Ability(KnightRow, "tauntCooldown", 25f));
        tauntUntil = Time.time + tauntTotal;
        tauntReadyAt = Time.time + tauntWait;
        Vector3 at = transform.position;
        ReceiveTaunt(movement, at, true);
        AlertHUD.Self(Texts.Get("class.knight.taunt.self"));
        Taunted?.Invoke(at);
    }

    /// <summary>
    /// A Knight taunted at <paramref name="at"/> (this PC's own, or a friend's as their PC said it): the zombies this PC
    /// runs that are already fighting within reach go after them (real only on the host or alone; EnemyTargeting), and
    /// every PC sees the ring go out and hears it, a friend's the softer the further off.
    /// </summary>
    public static void ReceiveTaunt(PlayerMovement knight, Vector3 at, bool mine)
    {
        float reach = Mathf.Max(0f, PlayerClass.Ability(KnightRow, "tauntRadius", 12f));
        EnemyTargeting.Taunt(knight, at, reach, PlayerClass.Ability(KnightRow, "tauntSeconds", 6f));
        TauntRing.Show(at, reach);
        float volume = 1f;
        var local = Local != null ? Local.movement : null;
        if (!mine && local != null) volume = Mathf.Clamp01(1f - Vector3.Distance(local.transform.position, at) / 40f) * .7f;
        KitSound.Play(KitSound.Cue.Taunt, volume);
    }
}
