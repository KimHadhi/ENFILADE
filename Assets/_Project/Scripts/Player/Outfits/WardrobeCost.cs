using System.Diagnostics;
using UnityEngine;

/// <summary>
/// What standing a dressed character up costs, part by part, while the console's /wardrobe is up (WardrobeCostHUD shows
/// it and switches this on). The wardrobe stands the character up again for every look it shows, and the user feels
/// each one (2026-11-14: "그 미리 보기 할 때 렉 걸리는 거 최적화 탐색 좀"); nothing can be run or profiled from where the
/// code is written, so the measuring is built in, as the zombies' is (ZombieCost): the user tries clothes on with it up
/// and reads where the time goes, and the next round of cuts starts from numbers. Off, each Begin is one read of a
/// bool; on, a timed part costs two reads of the clock.
/// </summary>
public static class WardrobeCost
{
    /// <summary>
    /// The parts timed. Some lie inside others, and the readout takes them out of the outer one: Model, Dress, Sole and
    /// Face inside Stand; Place, Follow, Cover and Fit inside Dress; Measure inside Follow and Fit. Cloth stands apart:
    /// it is hung a moment after the character stands, in a frame of its own (PreviewCloth).
    /// </summary>
    public enum Part
    {
        /// <summary>One character stood up for a look, from making its model to its face and idle (MainMenuPreview.Show when nothing on the shelf had the look).</summary>
        Stand,
        /// <summary>Making the character's model and putting its colour set on.</summary>
        Model,
        /// <summary>OutfitDresser.Dress, all of it.</summary>
        Dress,
        /// <summary>Making each outfit's model and joining it to the body's bones.</summary>
        Place,
        /// <summary>OutfitFit.Follow: pieces taking the body's shape keys they lack.</summary>
        Follow,
        /// <summary>UnderwearFit.Fit and SkinCover.Release: what covers what, by the meshes' vertices.</summary>
        Cover,
        /// <summary>OutfitFit.Trim: layers fitted under what is worn over them.</summary>
        Fit,
        /// <summary>Measuring a pair of meshes afresh (FitTrim.Measure, FitFollow.Build): the dear part, done once per pair.</summary>
        Measure,
        /// <summary>DrawnSole: where the dressed body's soles are.</summary>
        Sole,
        /// <summary>The cloth hung on the standing character (PreviewCloth).</summary>
        Cloth,
        /// <summary>Its face (CharacterFace.Wear).</summary>
        Face
    }

    /// <summary>What is counted beside the times.</summary>
    public enum Tally
    {
        /// <summary>Characters stood up afresh, and looks taken back off the shelf instead.</summary>
        Stands, Shelf,
        /// <summary>Pairs of meshes measured afresh, and pairs remembered from before.</summary>
        Measured, Remembered,
        /// <summary>Fitted meshes made, and shape keys made for pieces that lacked them.</summary>
        Meshes, Keys,
        /// <summary>Cloth parts started.</summary>
        ClothParts
    }

    public const int Parts = 11, Tallies = 7;

    private static readonly long[] ticks = new long[Parts];
    private static readonly int[] calls = new int[Parts], tallies = new int[Tallies];

    /// <summary>Measuring: the readout is up.</summary>
    public static bool On { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Reset() => Switch(false);

    /// <summary>The clock as a timed part begins, or 0 while nothing is measured.</summary>
    public static long Begin() => On ? Stopwatch.GetTimestamp() : 0L;

    /// <summary>A timed part ends: what Begin gave it.</summary>
    public static void End(Part part, long began)
    {
        if (began == 0L) return;
        ticks[(int)part] += Stopwatch.GetTimestamp() - began;
        calls[(int)part]++;
    }

    public static void Count(Tally what, int by = 1)
    {
        if (On) tallies[(int)what] += by;
    }

    /// <summary>The readout comes up or goes: everything gathered so far is dropped.</summary>
    internal static void Switch(bool on)
    {
        On = on;
        System.Array.Clear(ticks, 0, Parts);
        System.Array.Clear(calls, 0, Parts);
        System.Array.Clear(tallies, 0, Tallies);
    }

    /// <summary>
    /// The readout takes what has gathered since it last did (each part's milliseconds and how often it ran, and the
    /// tallies), and the gathering starts over.
    /// </summary>
    internal static void Take(double[] milliseconds, int[] ran, int[] counted)
    {
        double each = 1000.0 / Stopwatch.Frequency;
        for (int i = 0; i < Parts; i++)
        {
            milliseconds[i] = ticks[i] * each;
            ran[i] = calls[i];
            ticks[i] = 0L;
            calls[i] = 0;
        }
        for (int i = 0; i < Tallies; i++)
        {
            counted[i] = tallies[i];
            tallies[i] = 0;
        }
    }
}
