using UnityEngine;

// ClassCards: Assault: Broken Arrow. One part of the class; the rest is in ClassCards.cs and the files beside it.
public sealed partial class ClassCards
{
    // Watched every frame: health falling through its line, or a grab taking hold, calls the barrage (when it may be
    // called). Its shells are the barrage's (ClassCards.Barrage).
    private void BrokenArrow()
    {
        var body = movement.DamageReceiver;
        bool able = body != null && body.IsAlive && !movement.IsDead;
        bool high = able && body.MaximumHealth > 0f
            && body.Health / body.MaximumHealth * 100f > PlayerClass.Ability(AssaultRow, "brokenHealth", 30f);
        bool held = able && movement.IsHeld;
        if (able && PlayerClass.Is(PlayerClass.Assault) && PlayerPerks.Has(PlayerPerks.AssaultBroken) && Time.time >= brokenAgain
            && ((wasHigh && !high) || (held && !wasHeld)))
            CallBrokenArrow();
        wasHigh = high || !able;
        wasHeld = held;
    }

    // The same barrage as 데인저 클로즈, dropped on this player's own spot to get out (the user, 2026-11-14: "위험 상황일 때
    // 본인 위치로 데인저 클로즈 폭격을 떨궈서 탈출하는 용도로"): its shells fall round where they stood when it was called, and
    // whatever holds them is shelled first, which knocks it off them (a knocked-down holder lets go: PlayerHold).
    private void CallBrokenArrow()
    {
        brokenAgain = Time.time + Mathf.Max(1f, PlayerClass.Ability(AssaultRow, "brokenCooldown", 75f));
        CallBarrage(transform.position, PlayerClass.Ability(AssaultRow, "brokenDelay", 1.5f),
            Mathf.RoundToInt(PlayerPerks.Total(PlayerPerks.AssaultBroken)), true);
        AlertHUD.Self(Texts.Get("class.assault.broken.self"));
        KitSound.Play(KitSound.Cue.Alert, .8f);
    }
}
