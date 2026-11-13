using UnityEngine;

// ClassCards: The ultimate. One part of the class; the rest is in ClassCards.cs and the files beside it.
public sealed partial class ClassCards
{
    /// <summary>Whether this player's class has an ultimate for the full gauge once every card is taken (all seven do).</summary>
    public bool HasUltimate => PlayerClass.Current != null;

    /// <summary>
    /// Every card this player can be dealt is taken and the full gauge is spent on X (PerkCardScreen; classes-design's
    /// open question, "what a full gauge does once the cards are all taken"): the class's ultimate at once, its notice on
    /// every HUD and its look in the world (MomentFx.Ultimate). Knight: the shadow double; Assault: adrenaline full and the CHARGE, its phantom riders
    /// with it; Ghost: the Head Hunter full under the White Tiger's guard; Point: both guns loaded and the Gunslinger's
    /// moment; Détente: the syringe full and one more Resurrection; Schießhund: the Desperado's cylinder full; Support:
    /// the bag full. Numbers: ClassAbilities Cards.ultimateSeconds.
    /// </summary>
    public void Ultimate()
    {
        float seconds = Mathf.Max(1f, PlayerClass.Ability("Cards", "ultimateSeconds", 10f));
        if (PlayerClass.Is(PlayerClass.Knight)) ShadowFor(seconds);
        else if (PlayerClass.Is(PlayerClass.Assault))
        {
            kit.OrNull()?.UltimateCharge();
            if (!PlayerPerks.Has(PlayerPerks.AssaultCavalry)) Cavalry(transform.position);
        }
        else if (PlayerClass.Is(PlayerClass.Ghost))
        {
            kit.OrNull()?.FillHunter();
            tigerTotal = seconds;
            tigerUntil = Time.time + seconds;
        }
        else if (PlayerClass.Is(PlayerClass.Point)) kit.OrNull()?.ReloadAll();
        else if (PlayerClass.Is(PlayerClass.Detente)) DetenteKit.Local.OrNull()?.Ultimate();
        else if (PlayerClass.Is(PlayerClass.Schiesshund)) kit.OrNull()?.LoadDesperado();
        else if (PlayerClass.Is(PlayerClass.Support)) SupportKit.Local.OrNull()?.Restock();
        // Everyone's notice of who fired it, the class's stinger and its look in the world (the special-moment picks of
        // 2026-11-12), no banner over the screen.
        CardNet.Show(CardFx.Kind.Ultimate, transform.position, transform.forward, MomentLook.IndexOf(PlayerClass.CurrentId));
    }
}
