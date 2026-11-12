using System.Collections.Generic;
using System.Globalization;

/// <summary>Where a friend's fingertip may poke a character (SkinPoke): only these give under it.</summary>
public enum PokeZone { None, Cheek, Belly, Arm, Shoulder, HeadTop }

/// <summary>
/// The numbers of pointing and poking (Resources/Tables/Poke.csv, key,value; read on first use and again when the tables
/// reload): how long F is held before the hand points, how the arm comes up and reaches, where on a body each zone lies,
/// and per zone how deep the skin gives under a fingertip, how wide and soft the dent is and how it springs back
/// (PlayerPing, PlayerPoint, PointArm, PokeBody, SkinPoke). The chest, the bottom and the groin are no zone at all, so
/// they never give (the manager's call, 2026-11-12: playful and decent; the user can change it).
/// </summary>
public static class PokeTables
{
    public const string TableName = "Poke";

    /// <summary>How a zone gives under a fingertip and comes back when it lifts (m, s, Hz).</summary>
    public sealed class Feel
    {
        /// <summary>How far the fingertip goes in, and how long it takes to get there ("꾹").</summary>
        public float Press, PressTime;
        /// <summary>The deepest the skin gives; the curve of the dimple (bigger: wider and gentler, plusher); how much
        /// the skin gives just before the finger meets it, rounding the rim; how far out from the finger the dent fades.</summary>
        public float Deepest, Bowl, Soft, Reach;
        /// <summary>The spring back: how many times a second it swings, its damping ratio, and how much of its swing past
        /// the surface shows as a bulge.</summary>
        public float Hz, Bounce, Bulge;
        /// <summary>The poke's sound, as a share of its own pitch.</summary>
        public float Pitch;
    }

    private static readonly Dictionary<string, float> numbers = new Dictionary<string, float>();
    private static readonly Feel[] feels = new Feel[6];
    private static bool listening;

    /// <summary>F held this long (s) turns into a point; a shorter press stays the ping it always was.</summary>
    public static float HoldAfter => Number("Point.holdAfter", .25f);

    /// <summary>A number of Poke.csv by its key ("Row.column"), or <paramref name="fallback"/> when the table lacks it.</summary>
    public static float Number(string key, float fallback)
    {
        Listen();
        if (numbers.TryGetValue(key, out float value)) return value;
        value = fallback;
        int dot = key.IndexOf('.');
        if (dot > 0 && GameTables.TryGetRow(TableName, key.Substring(0, dot), out var row)
            && row.TryGetValue(key.Substring(dot + 1), out string cell)
            && float.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out float read))
            value = read;
        numbers[key] = value;
        return value;
    }

    /// <summary>How <paramref name="zone"/> gives and springs back (Poke.csv rows Cheek, Belly, Arm, Shoulder, HeadTop).</summary>
    public static Feel Of(PokeZone zone)
    {
        Listen();
        int index = (int)zone;
        if (index <= 0 || index >= feels.Length) index = (int)PokeZone.Cheek;
        if (feels[index] != null) return feels[index];
        string row = ((PokeZone)index).ToString();
        // The code's own numbers, for a table without the row: plush cheeks, a soft bouncy belly, firmer arms and
        // shoulders, the top of the head mostly hair over bone.
        var made = (PokeZone)index switch
        {
            PokeZone.Belly => Make(.018f, .16f, .024f, .026f, .005f, .05f, 3.6f, .22f, .75f, .85f),
            PokeZone.Arm => Make(.008f, .1f, .011f, .014f, .003f, .03f, 7f, .45f, .4f, 1.1f),
            PokeZone.Shoulder => Make(.006f, .1f, .009f, .012f, .003f, .028f, 8f, .5f, .35f, 1.05f),
            PokeZone.HeadTop => Make(.006f, .1f, .01f, .016f, .004f, .035f, 6f, .45f, .3f, 1.4f),
            _ => Make(.012f, .12f, .016f, .018f, .004f, .035f, 5f, .32f, .6f, 1.25f),
        };
        made.Press = Number(row + ".press", made.Press);
        made.PressTime = Number(row + ".pressTime", made.PressTime);
        made.Deepest = Number(row + ".deepest", made.Deepest);
        made.Bowl = Number(row + ".bowl", made.Bowl);
        made.Soft = Number(row + ".soft", made.Soft);
        made.Reach = Number(row + ".reach", made.Reach);
        made.Hz = Number(row + ".hz", made.Hz);
        made.Bounce = Number(row + ".bounce", made.Bounce);
        made.Bulge = Number(row + ".bulge", made.Bulge);
        made.Pitch = Number(row + ".pitch", made.Pitch);
        return feels[index] = made;
    }

    private static Feel Make(float press, float pressTime, float deepest, float bowl, float soft, float reach, float hz,
        float bounce, float bulge, float pitch) => new Feel
    {
        Press = press, PressTime = pressTime, Deepest = deepest, Bowl = bowl, Soft = soft, Reach = reach,
        Hz = hz, Bounce = bounce, Bulge = bulge, Pitch = pitch,
    };

    private static void Listen()
    {
        if (listening) return;
        listening = true;
        GameTables.Reloaded += Forget;
    }

    private static void Forget()
    {
        numbers.Clear();
        for (int i = 0; i < feels.Length; i++) feels[i] = null;
    }

    [UnityEngine.RuntimeInitializeOnLoadMethod(UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        if (listening) GameTables.Reloaded -= Forget;
        listening = false;
        Forget();
    }
}
