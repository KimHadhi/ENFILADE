using UnityEngine;

/// <summary>
/// What the class's own resource says this frame, as ClassHUD works it out for this PC's player: its ticks (an Assault's
/// adrenaline, a Ghost's Head Hunter stacks, a Schießhund's rounds, a Détente's doses, a Support's bag, a Point's two
/// guns reloading aside), whether the class key has something to do and the word for it, the line beside it (waits,
/// offers, the bag in numbers) and a mark over it (DEADEYE, DESPERADO). Made so the working-out lives in one place and
/// the look is another's: the old class plate draws it, and HUD Mk II's class tile draws the same (the user's pick of
/// the HUD drafts, 2026-11-22: HUD Mk II "좋아").
/// </summary>
public sealed class ClassReadout
{
    /// <summary>The most ticks a class shows.</summary>
    public const int MostTicks = 12;

    /// <summary>This PC's player's, filled every frame by ClassHUD; null before it first runs.</summary>
    public static ClassReadout Local { get; internal set; }

    public string ClassId = "", Title = "";
    /// <summary>The class's icon (Resources/UI/Icons), or null.</summary>
    public Texture Icon;
    /// <summary>How many ticks the class shows now (0 for none).</summary>
    public int Count;
    /// <summary>How full each tick is (0-1) and the colour it is drawn in on the class plate.</summary>
    public readonly float[] Fill = new float[MostTicks];
    public readonly Color[] Colour = new Color[MostTicks];
    /// <summary>The ticks are the Point's two guns reloading aside (their fill is the reload's share).</summary>
    public bool Guns;
    /// <summary>The class key has something to do now, the word for it and its colour.</summary>
    public bool KeyReady;
    public string Key = "", Word = "";
    public Color WordColour = Color.white;
    /// <summary>The line beside the ticks: a wait, an offer, the bag in numbers ("" for none).</summary>
    public string Note = "";
    /// <summary>A mark over the plate (DEADEYE, DESPERADO) and its colour.</summary>
    public bool MarkShown;
    public string Mark = "";
    public Color MarkColour = Color.white;
    /// <summary>The player is dead: nothing of the class shows.</summary>
    public bool Hidden;

    /// <summary>How many ticks are full.</summary>
    public int Lit
    {
        get
        {
            int lit = 0;
            for (int i = 0; i < Count && i < MostTicks; i++)
                if (Fill[i] >= .999f) lit++;
            return lit;
        }
    }
}
