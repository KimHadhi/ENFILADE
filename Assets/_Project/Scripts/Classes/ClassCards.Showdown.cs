// ClassCards: The Schießhund's showdown, asked before the gauge is spent. One part of the class; the rest is in ClassCards.cs and the files beside it.
public sealed partial class ClassCards
{
    /// <summary>
    /// Whether this player's ultimate is held back now, said on the screen and the gauge left full (PerkCardScreen asks
    /// before it spends it): a Schießhund's showdown wants a zombie under the crosshair it can take into its arena now
    /// (HoundShowdown.Refusal). Every other class's goes off whatever it is looking at.
    /// </summary>
    public bool UltimateHeldBack()
    {
        if (!PlayerClass.Is(PlayerClass.Schiesshund)) return false;
        string refusal = HoundShowdown.Refusal(movement, out _);
        if (refusal == null) return false;
        KitSound.Play(KitSound.Cue.Refuse, .5f);
        AlertHUD.Self(Texts.Get(refusal));
        return true;
    }
}
