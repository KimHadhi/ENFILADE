using UnityEngine;

// ClassKit: The class key. One part of the class; the rest is in ClassKit.cs and the files beside it.
public sealed partial class ClassKit
{
    private void Use()
    {
        if (PlayerClass.Is(PlayerClass.Assault))
        {
            if (stacks < StackMax) { KitSound.Play(KitSound.Cue.Refuse, .5f); return; }
            stacks = 0;
            Vector3 at = transform.position;
            ReceiveCharge(at, Texts.Get("class.assault.charge.self"));
            // Everyone it takes glows gold on this PC (the friends' PCs light them as they hear of it: ChargeRpc).
            ChargeGlow.Sounded(at);
            MomentHUD.Charge(PlayerClass.Ability(AssaultRow, "chargeSeconds", 7f), PlayerClass.Ability(AssaultRow, "chargeRadius", 15f));
            Charged?.Invoke(at);
            // The phantom cavalry card rides out with it (ClassCards).
            cards.OrNull()?.Charged(at);
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
    /// within its reach, charges with it.
    /// </summary>
    public static void ReceiveCharge(Vector3 at, string said)
    {
        var kit = Local;
        if (kit == null || kit.movement == null || kit.movement.IsDead) return;
        float reach = PlayerClass.Ability(AssaultRow, "chargeRadius", 15f);
        if ((kit.transform.position - at).sqrMagnitude > reach * reach) return;
        kit.chargeTotal = Mathf.Max(.1f, PlayerClass.Ability(AssaultRow, "chargeSeconds", 7f));
        kit.chargeUntil = Time.time + kit.chargeTotal;
        KitSound.Play(KitSound.Cue.Charge, .9f);
        AlertHUD.Self(said);
        // And they shout back, as G&B's line answers its officer's charge (PlayerCallouts, Settings → 저절로 외치기).
        PlayerCallouts.JoinCharge();
    }
}
