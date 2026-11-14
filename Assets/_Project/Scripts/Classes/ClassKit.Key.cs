using UnityEngine;

// ClassKit: The class key. One part of the class; the rest is in ClassKit.cs and the files beside it.
public sealed partial class ClassKit
{
    private void Use()
    {
        if (PlayerClass.Is(PlayerClass.Assault))
        {
            // With 데인저 클로즈 ready the key is held to aim its barrage, and a tap comes back as the CHARGE (ClassCards).
            if (cards != null && cards.TakesKey()) return;
            TryCharge();
            return;
        }
        if (PlayerClass.Is(PlayerClass.Knight)) { Taunt(); return; }
        if (PlayerClass.Is(PlayerClass.Detente) && detente != null) { detente.Inject(); return; }
        if (PlayerClass.Is(PlayerClass.Schiesshund)) { Duel(); return; }
        if (PlayerClass.Is(PlayerClass.Point)) { GunKata(); return; }
        // No ability on the key for this class (a Point's and a Ghost's are always on; a Support's is SupportKit's), or not
        // built yet.
        KitSound.Play(KitSound.Cue.Refuse, .35f);
    }

    /// <summary>
    /// A CHARGE was sounded here (this PC's player's, or a friend's as their PC said it): this PC's player, alive and
    /// within its reach, charges with it. Its seconds and reach are the Assault's PC's (its 돌격 나팔 card lengthens it),
    /// sent with it (PlayerNetworkCombat.ChargeRpc): a friend charges as long as the Assault does, whatever this PC's own
    /// table and cards would say.
    /// </summary>
    public static void ReceiveCharge(Vector3 at, string said, float seconds, float reach)
    {
        var kit = Local;
        if (kit == null || kit.movement == null || kit.movement.IsDead) return;
        if ((kit.transform.position - at).sqrMagnitude > reach * reach) return;
        kit.chargeTotal = Mathf.Max(.1f, seconds);
        kit.chargeUntil = Time.time + kit.chargeTotal;
        KitSound.Play(KitSound.Cue.Charge, .9f);
        AlertHUD.Self(said);
        // And they shout back, as G&B's line answers its officer's charge (PlayerCallouts, Settings → 저절로 외치기).
        PlayerCallouts.JoinCharge();
    }
}
