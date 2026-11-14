using UnityEngine;

// ClassCards: The ultimate. One part of the class; the rest is in ClassCards.cs and the files beside it.
public sealed partial class ClassCards
{
    /// <summary>Whether this player's class has an ultimate for the full gauge once every card is taken (all seven do).</summary>
    public bool HasUltimate => PlayerClass.Current != null;

    /// <summary>
    /// Every card this player can be dealt is taken and the full gauge is spent on X (PerkCardScreen; classes-design's
    /// open question, "what a full gauge does once the cards are all taken"): the class's ultimate at once, its notice on
    /// every HUD and its look in the world (MomentFx.Ultimate). Knight: the shadow double; Assault: an A-10's gun run down the line
    /// it aims at and adrenaline full (ClassCards.GunRun); Ghost: unseen by every zombie for its seconds, every zombie near it shown through walls to the whole team
    /// (GhostCards.Phantom); Point: both guns loaded and the Gunslinger's
    /// moment; Détente: Second Life, the syringe full, one more Resurrection, every downed friend up and everyone's health
    /// full wherever they are, under a disinfectant cloud each that blinds the zombies in it (DetenteKit.Ultimate);
    /// Schießhund: its showdown, the zombie under the crosshair taken with it into an arena far below for a duel, and back to a crowd's roar, stronger (HoundShowdown); Support:
    /// the bag full and every teammate's shield, ammo and throwables full, each on their own PC (SupportKit.Restock).
    /// Numbers: ClassAbilities Cards.ultimateSeconds.
    /// </summary>
    public void Ultimate()
    {
        float seconds = Mathf.Max(1f, PlayerClass.Ability("Cards", "ultimateSeconds", 10f));
        if (PlayerClass.Is(PlayerClass.Knight)) ShadowFor(seconds);
        else if (PlayerClass.Is(PlayerClass.Assault))
        {
            // Adrenaline full (the CHARGE theirs to sound, its riders with it) and an A-10's gun run down the line they
            // aim at (ClassCards.GunRun; the user: "A10 공중 지원 느낌으로").
            kit.OrNull()?.UltimateAdrenaline();
            CallGunRun();
        }
        else if (PlayerClass.Is(PlayerClass.Ghost)) GhostCards.Local.OrNull()?.Phantom();
        else if (PlayerClass.Is(PlayerClass.Point)) kit.OrNull()?.ReloadAll();
        else if (PlayerClass.Is(PlayerClass.Detente)) DetenteKit.Local.OrNull()?.Ultimate();
        else if (PlayerClass.Is(PlayerClass.Schiesshund)) HoundShowdown.Begin(movement);
        else if (PlayerClass.Is(PlayerClass.Support)) SupportKit.Local.OrNull()?.Restock();
        // Everyone's notice of who fired it, the class's stinger and its look in the world (the special-moment picks of
        // 2026-11-12), no banner over the screen.
        CardNet.Show(CardFx.Kind.Ultimate, transform.position, transform.forward, MomentLook.IndexOf(PlayerClass.CurrentId));
    }
}
