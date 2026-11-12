using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// The special moments' looks as the user picked them on 2026-11-12 (pick page 4asv6far7KKwg5QVjS2Yih). The pick page's
/// main note: "컷인 띠의 저 깔끔함과 시인성이 좋다 … 왼쪽 아래에 궁극기 썼다고 나오는 게 팀원들한테도 뜨면 누가 궁극기 썼는지
/// 다른 데 보고 있어도 바로 알 것 같아서".
/// Every moment keeps one language:
/// - the crosshair and the middle of the screen stay clear, and nothing whitens or inverts the screen;
/// - one thing leads each moment, and it is over in under a second;
/// - each class has its own colour and sigil, so two moments never read as one template.
/// What the others see in the world, the one who did it sees too (the user: "내가 보는 화면이 진실이라서 남이 보는 세상과
/// … 똑같았으면").
/// This class holds each class's colour, its sigil (a few strokes in a 100-unit box, y up), its ultimate's names, and the
/// numbers in Tables/MomentFx.csv.
/// </summary>
public static class MomentLook
{
    public const string TableName = "MomentFx";

    public enum Sigil : byte { Knight, Assault, Ghost, Point, Detente, Hound, Support, Reversal, Star, Target, Kata, Tiger }

    /// <summary>
    /// One stroke of a sigil, in a 100-unit box with y up:
    /// - its points, closed or open;
    /// - drawn white or in the moment's colour;
    /// - filled (a convex shape) or traced.
    /// </summary>
    public sealed class Stroke
    {
        public Vector2[] Points;
        public bool Closed, White, Filled;
        public float Length;
    }

    private static readonly Dictionary<Sigil, Stroke[]> sigils = new Dictionary<Sigil, Stroke[]>();
    private static readonly Dictionary<Sigil, float> lengths = new Dictionary<Sigil, float>();

    // ---- The classes ----

    /// <summary>A class's colour (Tables/MomentFx.csv Class.&lt;id&gt;); the HUD stays black, white and pink.</summary>
    public static Color ColorOf(string classId)
    {
        string id = Id(classId);
        if (TryText("Class", id, out string text) && ColorUtility.TryParseHtmlString(text, out Color read)) return read;
        return id switch
        {
            PlayerClass.Knight => new Color(.663f, .545f, 1f),
            PlayerClass.Assault => new Color(1f, .784f, .239f),
            PlayerClass.Ghost => new Color(.902f, .937f, 1f),
            PlayerClass.Point => new Color(1f, .541f, .239f),
            PlayerClass.Detente => new Color(1f, .231f, .231f),
            PlayerClass.Schiesshund => new Color(.122f, .922f, .82f),
            PlayerClass.Support => new Color(.31f, .702f, 1f),
            _ => KitStyle.Accent,
        };
    }

    /// <summary>A class's own sigil (the C drafts, with the user's notes).</summary>
    public static Sigil SigilOf(string classId) => Id(classId) switch
    {
        PlayerClass.Knight => Sigil.Knight,
        PlayerClass.Assault => Sigil.Assault,
        PlayerClass.Ghost => Sigil.Ghost,
        PlayerClass.Point => Sigil.Point,
        PlayerClass.Detente => Sigil.Detente,
        PlayerClass.Schiesshund => Sigil.Hound,
        PlayerClass.Support => Sigil.Support,
        _ => Sigil.Star,
    };

    /// <summary>The ultimate's name in big type on the notice (Ult.&lt;id&gt;.title, words in Texts.csv moment.ult.&lt;id&gt;.title).</summary>
    public static string UltTitle(string classId) => Text("Ult", Id(classId) + ".title", UltId(classId, "title"));

    /// <summary>What it does, in a few Korean words under the name (Ult.&lt;id&gt;.what, words in Texts.csv).</summary>
    public static string UltWhat(string classId) => Text("Ult", Id(classId) + ".what", UltId(classId, "what"));

    // The Texts row a class's ultimate words fall back to when MomentFx.csv has no key for them: the class's own row,
    // else the plain ULTIMATE / 궁극기 one. The words live in Texts.csv only, never in code.
    private static string UltId(string classId, string part)
    {
        string own = "moment.ult." + Id(classId) + "." + part;
        return Texts.Has(own) ? own : "moment.ult.any." + part;
    }

    private static string Id(string classId) => string.IsNullOrEmpty(classId) ? "" : classId.Trim().ToLowerInvariant();

    // The classes by number, for a class sent between PCs as one small number (CardNet).
    private static readonly string[] Classes =
        { PlayerClass.Knight, PlayerClass.Assault, PlayerClass.Ghost, PlayerClass.Point, PlayerClass.Detente, PlayerClass.Schiesshund, PlayerClass.Support };

    /// <summary>A class's number for sending (-1 for none).</summary>
    public static int IndexOf(string classId) => System.Array.IndexOf(Classes, Id(classId));

    /// <summary>The class sent as this number ("" for none).</summary>
    public static string ClassAt(int index) => index >= 0 && index < Classes.Length ? Classes[index] : "";

    // ---- The table ----

    /// <summary>A number from Tables/MomentFx.csv, row.key, or the fallback.</summary>
    public static float Number(string row, string key, float fallback)
        => TryText(row, key, out string text) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;

    /// <summary>A colour from Tables/MomentFx.csv (#RRGGBB or #RRGGBBAA), or the fallback.</summary>
    public static Color Colour(string row, string key, Color fallback)
        => TryText(row, key, out string text) && ColorUtility.TryParseHtmlString(text, out Color read) ? read : fallback;

    /// <summary>
    /// Words from Tables/MomentFx.csv: the cell names a Texts.csv row (moment.ult.knight.what …) where they are written,
    /// or holds plain words; the fallback (a Texts id) when the table has no such key.
    /// </summary>
    public static string Text(string row, string key, string fallback) => Texts.Cell(TryText(row, key, out string text) ? text : fallback);

    /// <summary>Whether a moment's look is on (row.on, 1 by default).</summary>
    public static bool On(string row) => Number(row, "on", 1f) > .5f;

    private static bool TryText(string row, string key, out string text)
    {
        text = null;
        if (!GameTables.TryGetRow(TableName, row, out var cells) || !cells.TryGetValue(key, out text)) return false;
        text = text?.Trim();
        return !string.IsNullOrEmpty(text);
    }

    // ---- The sigils ----

    /// <summary>A sigil's strokes (100-unit box, y up), made once.</summary>
    public static Stroke[] Strokes(Sigil sigil)
    {
        if (sigils.TryGetValue(sigil, out var made)) return made;
        var list = new List<Stroke>();
        switch (sigil)
        {
            case Sigil.Knight:
                // A kite shield, the sword through it and its guard.
                Add(list, true, false, false, new Vector2(-30f, 36f), new Vector2(30f, 36f), new Vector2(30f, 4f), new Vector2(0f, -42f), new Vector2(-30f, 4f));
                Add(list, false, false, false, new Vector2(0f, 50f), new Vector2(0f, -28f));
                Add(list, false, false, false, new Vector2(-15f, 24f), new Vector2(15f, 24f));
                break;
            case Sigil.Assault:
                // Three rank chevrons.
                for (int i = 0; i < 3; i++)
                    Add(list, false, false, false, new Vector2(-32f, 28f - i * 22f), new Vector2(0f, 44f - i * 22f), new Vector2(32f, 28f - i * 22f));
                break;
            case Sigil.Ghost:
                // A scope's ring and ticks with three claw marks across it.
                Circle(list, Vector2.zero, 32f, false);
                Add(list, false, false, false, new Vector2(0f, 37f), new Vector2(0f, 50f));
                Add(list, false, false, false, new Vector2(37f, 0f), new Vector2(50f, 0f));
                Add(list, false, false, false, new Vector2(0f, -37f), new Vector2(0f, -50f));
                Add(list, false, false, false, new Vector2(-37f, 0f), new Vector2(-50f, 0f));
                Add(list, false, false, false, new Vector2(-2f, 24f), new Vector2(-18f, -16f));
                Add(list, false, false, false, new Vector2(10f, 22f), new Vector2(-6f, -20f));
                Add(list, false, false, false, new Vector2(22f, 18f), new Vector2(6f, -22f));
                break;
            case Sigil.Point:
                // Two pistols crossed (the user: "문양은 두 총이 서로 크로스돼 있는 모양으로"): each a barrel and a grip.
                Add(list, false, false, false, new Vector2(-30f, -30f), new Vector2(38f, 38f));
                Add(list, false, false, false, new Vector2(-22f, -22f), new Vector2(-10f, -36f), new Vector2(-2f, -28f));
                Add(list, false, false, false, new Vector2(30f, -30f), new Vector2(-38f, 38f));
                Add(list, false, false, false, new Vector2(22f, -22f), new Vector2(10f, -36f), new Vector2(2f, -28f));
                break;
            case Sigil.Detente:
                // A ring with the white cross in it (the user: "얘는 백십자 문양 넣자").
                Circle(list, Vector2.zero, 42f, false);
                Add(list, true, true, true, new Vector2(-8f, 28f), new Vector2(8f, 28f), new Vector2(8f, -28f), new Vector2(-8f, -28f));
                Add(list, true, true, true, new Vector2(-28f, 8f), new Vector2(28f, 8f), new Vector2(28f, -8f), new Vector2(-28f, -8f));
                break;
            case Sigil.Hound:
                // The bounty's nested diamond, an eye (the user: "눈 모양 좋네").
                Add(list, true, false, false, new Vector2(0f, 46f), new Vector2(46f, 0f), new Vector2(0f, -46f), new Vector2(-46f, 0f));
                Add(list, true, false, false, new Vector2(0f, 20f), new Vector2(20f, 0f), new Vector2(0f, -20f), new Vector2(-20f, 0f));
                Add(list, false, false, false, new Vector2(-62f, 0f), new Vector2(-48f, 0f));
                Add(list, false, false, false, new Vector2(48f, 0f), new Vector2(62f, 0f));
                break;
            case Sigil.Support:
                // A crate with three rounds in it.
                Add(list, true, false, false, new Vector2(-34f, 34f), new Vector2(34f, 34f), new Vector2(34f, -34f), new Vector2(-34f, -34f));
                foreach (float x in new[] { -15f, 0f, 15f })
                    Add(list, true, false, false, new Vector2(x - 5f, -20f), new Vector2(x + 5f, -20f), new Vector2(x + 5f, 6f), new Vector2(x, 18f), new Vector2(x - 5f, 6f));
                break;
            case Sigil.Reversal:
                Arc(list, Vector2.zero, 32f, 50f, 330f);
                Add(list, false, false, false, new Vector2(17f, -20f), new Vector2(28f, -16f), new Vector2(30f, -30f));
                break;
            case Sigil.Star:
                for (int i = 0; i < 8; i++)
                {
                    float a = i * Mathf.PI / 4f + .2f, r1 = i % 2 == 1 ? 34f : 48f;
                    Add(list, false, false, false, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 14f, new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r1);
                }
                break;
            case Sigil.Target:
                Circle(list, Vector2.zero, 28f, false);
                Add(list, false, false, false, new Vector2(0f, 46f), new Vector2(0f, 12f));
                Add(list, false, false, false, new Vector2(0f, -12f), new Vector2(0f, -46f));
                Add(list, false, false, false, new Vector2(46f, 0f), new Vector2(12f, 0f));
                Add(list, false, false, false, new Vector2(-12f, 0f), new Vector2(-46f, 0f));
                break;
            case Sigil.Kata:
                // The Gun Kata, apart from the ultimate's crossed guns: a cylinder turning, three arcs of its spin round it.
                Add(list, true, false, false, Hexagon(17f));
                for (int i = 0; i < 3; i++) Arc(list, Vector2.zero, 36f, 20f + i * 120f, 95f + i * 120f);
                break;
            case Sigil.Tiger:
                // A white tiger's head in outline (the user: "백호 외곽선이 나왔으면 멋있을 듯"): ears, cheeks and jaw, eyes,
                // nose and the stripes of the brow and cheeks.
                Add(list, true, true, false, new Vector2(-30f, 46f), new Vector2(-40f, 26f), new Vector2(-46f, 6f), new Vector2(-42f, -12f),
                    new Vector2(-30f, -30f), new Vector2(-13f, -42f), new Vector2(0f, -46f), new Vector2(13f, -42f), new Vector2(30f, -30f),
                    new Vector2(42f, -12f), new Vector2(46f, 6f), new Vector2(40f, 26f), new Vector2(30f, 46f), new Vector2(18f, 33f),
                    new Vector2(0f, 37f), new Vector2(-18f, 33f));
                Add(list, false, true, false, new Vector2(-25f, 9f), new Vector2(-11f, 4f));
                Add(list, false, true, false, new Vector2(25f, 9f), new Vector2(11f, 4f));
                Add(list, false, true, false, new Vector2(-7f, -11f), new Vector2(0f, -17f), new Vector2(7f, -11f));
                Add(list, false, true, false, new Vector2(0f, -17f), new Vector2(0f, -27f));
                Add(list, false, true, false, new Vector2(0f, 31f), new Vector2(0f, 19f));
                Add(list, false, true, false, new Vector2(-9f, 33f), new Vector2(-6f, 23f));
                Add(list, false, true, false, new Vector2(9f, 33f), new Vector2(6f, 23f));
                Add(list, false, true, false, new Vector2(-46f, -1f), new Vector2(-31f, -4f));
                Add(list, false, true, false, new Vector2(46f, -1f), new Vector2(31f, -4f));
                Add(list, false, true, false, new Vector2(-42f, -14f), new Vector2(-30f, -12f));
                Add(list, false, true, false, new Vector2(42f, -14f), new Vector2(30f, -12f));
                break;
        }
        float total = 0f;
        foreach (var stroke in list)
        {
            if (stroke.Filled) { stroke.Length = 0f; continue; }
            float length = 0f;
            int count = stroke.Points.Length;
            for (int i = 1; i < count; i++) length += Vector2.Distance(stroke.Points[i - 1], stroke.Points[i]);
            if (stroke.Closed && count > 2) length += Vector2.Distance(stroke.Points[count - 1], stroke.Points[0]);
            stroke.Length = length;
            total += length;
        }
        var strokes = list.ToArray();
        sigils[sigil] = strokes;
        lengths[sigil] = Mathf.Max(1f, total);
        return strokes;
    }

    /// <summary>The traced length of a sigil (units), for drawing it as if by one pen.</summary>
    public static float LengthOf(Sigil sigil)
    {
        Strokes(sigil);
        return lengths.TryGetValue(sigil, out float length) ? length : 1f;
    }

    /// <summary>
    /// Walks a sigil drawn up to <paramref name="progress"/> of its traced length (0 to 1):
    /// - <paramref name="segment"/> gets each traced piece as (from, to, white), in sigil units;
    /// - <paramref name="fill"/> gets each filled shape (its points, white) once the pen has passed half its way.
    /// </summary>
    public static void Walk(Sigil sigil, float progress, System.Action<Vector2, Vector2, bool> segment, System.Action<Vector2[], bool> fill)
    {
        var strokes = Strokes(sigil);
        float left = LengthOf(sigil) * Mathf.Clamp01(progress);
        foreach (var stroke in strokes)
        {
            if (stroke.Filled)
            {
                if (progress >= .5f) fill?.Invoke(stroke.Points, stroke.White);
                continue;
            }
            int count = stroke.Points.Length, segments = stroke.Closed && count > 2 ? count : count - 1;
            for (int i = 0; i < segments && left > 0f; i++)
            {
                Vector2 a = stroke.Points[i], b = stroke.Points[(i + 1) % count];
                float length = Vector2.Distance(a, b);
                if (length <= 0f) continue;
                if (left >= length) { segment?.Invoke(a, b, stroke.White); left -= length; }
                else { segment?.Invoke(a, Vector2.Lerp(a, b, left / length), stroke.White); left = 0f; }
            }
        }
    }

    private static void Add(List<Stroke> list, bool closed, bool white, bool filled, params Vector2[] points)
        => list.Add(new Stroke { Points = points, Closed = closed, White = white, Filled = filled });

    private static void Circle(List<Stroke> list, Vector2 middle, float radius, bool white)
    {
        var points = new Vector2[40];
        for (int i = 0; i < points.Length; i++)
        {
            float a = Mathf.PI * .5f - i / (float)points.Length * Mathf.PI * 2f;
            points[i] = middle + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }
        list.Add(new Stroke { Points = points, Closed = true, White = white });
    }

    private static void Arc(List<Stroke> list, Vector2 middle, float radius, float fromDegrees, float toDegrees)
    {
        var points = new Vector2[24];
        for (int i = 0; i < points.Length; i++)
        {
            float a = Mathf.Lerp(fromDegrees, toDegrees, i / (points.Length - 1f)) * Mathf.Deg2Rad;
            points[i] = middle + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }
        list.Add(new Stroke { Points = points });
    }

    private static Vector2[] Hexagon(float radius)
    {
        var points = new Vector2[6];
        for (int i = 0; i < 6; i++)
        {
            float a = Mathf.PI / 6f + i * Mathf.PI / 3f;
            points[i] = new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }
        return points;
    }
}
