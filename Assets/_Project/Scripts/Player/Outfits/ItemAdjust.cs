using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// The player's own say over a piece that hangs from the body as one thing (a rigid piece fitted to the face, the
/// welding mask; a pair hung on measured spots, the Ene earrings), on top of the position it is worn in
/// (WearPosition): moved right or left, up or down, ahead or back, turned about each of those, made bigger or smaller,
/// from where dressing put it (the user, 2026-11-14: "이런 액세서리류는 맘대로 크기나 위치를 조정할 수 있을까?"; 2026-11-18:
/// "착용 위치·오프셋을 자유롭게 커스텀할 수 있도록"). Clothes bound to the body's bones (BOOTH outfits) can't move apart
/// from the body, so only these pieces offer it. Kept in the character's outfit look, one entry for each position the
/// item was nudged in ("adjust:&lt;item&gt;=…" for its first position, "adjust:&lt;item&gt;~&lt;position&gt;=…" for another), so a
/// mask moved a little on the face keeps that when it is worn back for a while and comes round again, and presets, the
/// hub's body and every other PC wear it the same.
/// Moves are metres of the model's own space along the position's own right, up and ahead (worn back, ahead is the way
/// the back of the head faces); turns are degrees about those; size scales about the piece's own spot (the mask's
/// glass, an earring's piercing point). A pair's left piece does everything mirrored, so "right" moves both outward and
/// each keeps its own spot. How far the sliders reach: Tables/ItemAdjust.csv.
/// The component (on the model's root) remembers how dressing put each piece on, so the wardrobe's sliders move it live
/// with nothing made again.
/// </summary>
[DisallowMultipleComponent]
public sealed class ItemAdjust : MonoBehaviour
{
    public const string Prefix = "adjust:", TableName = "ItemAdjust";
    /// <summary>Between an item and a position other than its first, in the look's entry.</summary>
    public const char At = '~';
    private const char Separator = '_';

    public readonly struct Values
    {
        public readonly float Right, Up, Ahead, Size, Yaw, Pitch, Roll;

        public Values(float right, float up, float ahead, float size, float yaw, float pitch, float roll)
        {
            Right = right;
            Up = up;
            Ahead = ahead;
            Size = size;
            Yaw = yaw;
            Pitch = pitch;
            Roll = roll;
        }

        public static readonly Values None = new Values(0f, 0f, 0f, 1f, 0f, 0f, 0f);
        public bool IsNone => !Moves && Mathf.Abs(Up) < 1e-4f && Mathf.Abs(Ahead) < 1e-4f && Mathf.Abs(Size - 1f) < 1e-3f;
        /// <summary>Whether it says more than up, ahead and size (what a look from before 2026-11-18 could say).</summary>
        public bool Moves => Mathf.Abs(Right) >= 1e-4f || Mathf.Abs(Yaw) >= .05f || Mathf.Abs(Pitch) >= .05f || Mathf.Abs(Roll) >= .05f;

        /// <summary>The seven as the wardrobe's sliders list them: right, up, ahead, size, yaw, pitch, roll.</summary>
        public float this[int axis] => axis == 0 ? Right : axis == 1 ? Up : axis == 2 ? Ahead : axis == 3 ? Size : axis == 4 ? Yaw : axis == 5 ? Pitch : Roll;

        public Values With(int axis, float value) => new Values(axis == 0 ? value : Right, axis == 1 ? value : Up, axis == 2 ? value : Ahead,
            axis == 3 ? value : Size, axis == 4 ? value : Yaw, axis == 5 ? value : Pitch, axis == 6 ? value : Roll);
    }

    // One hung piece as dressing put it, in its parent's space: its place, turn and size, the spot it turns and scales
    // about, one metre of the position's right, up and ahead, and which of a pair it is (-1 the left one, which mirrors).
    private struct Piece
    {
        public Transform Part;
        public Vector3 Position, Scale, Pivot, Right, Up, Ahead;
        public Quaternion Rotation;
        public float Side;
    }

    private string item = "";
    private readonly List<Piece> pieces = new List<Piece>();

    /// <summary>How far the sliders reach: moves (metres of the model, either way), turns (degrees, either way) and the size's two ends.</summary>
    public static float MoveRange => Mathf.Max(.001f, GameTables.Number(TableName, "Range", "move", .2f));
    public static float TurnRange => Mathf.Clamp(GameTables.Number(TableName, "Range", "turn", 180f), 1f, 180f);
    public static float LeastSize => Mathf.Clamp(GameTables.Number(TableName, "Range", "least", .3f), .05f, 1f);
    public static float MostSize => Mathf.Max(1f, GameTables.Number(TableName, "Range", "most", 2.5f));

    /// <summary>Whether the item hangs from this character as one thing, to be placed and nudged: a piece made for anyone (OutfitLibrary.AnySkin).</summary>
    public static bool For(OutfitItem item, string skinId) => item != null && OutfitCatalog.Model(item.id, skinId) is { ForAnyone: true };

    /// <summary>
    /// The look's entry for the item's nudge in a position: the item alone for its first position (and for an item with
    /// none), so a look saved before items had positions reads as it did.
    /// </summary>
    public static string Key(OutfitItem item, WearPosition position)
    {
        if (item == null) return Prefix;
        var offered = OutfitCatalog.PositionsOf(item);
        bool first = position == null || offered.Count == 0 || offered[0] == position || !Contains(offered, position);
        return first ? Prefix + item.id : Prefix + item.id + At + position.id;
    }

    private static bool Contains(IReadOnlyList<WearPosition> offered, WearPosition position)
    {
        foreach (var each in offered)
            if (each == position) return true;
        return false;
    }

    /// <summary>The nudge the look keeps for the item worn in this position; None when it has none.</summary>
    public static Values Of(OutfitLook look, OutfitItem item, WearPosition position)
    {
        if (look == null || item == null || !look.TryGet(Key(item, position), out var pick)) return Values.None;
        return Read(pick.Item);
    }

    /// <summary>
    /// A nudge as a look writes it: "up_ahead_size" (all a look could say before 2026-11-18, and still written when that is
    /// all there is to say), or "right_up_ahead_size_yaw_pitch_roll". None for anything else. Kept inside the sliders' reach.
    /// </summary>
    public static Values Read(string text)
    {
        string[] parts = (text ?? "").Split(Separator);
        if (parts.Length != 3 && parts.Length != 7) return Values.None;
        var numbers = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            if (!TableCells.TryNumber(parts[i], out numbers[i])) return Values.None;
        float move = MoveRange, turn = TurnRange;
        float Move(float metres) => Mathf.Clamp(metres, -move, move);
        float Turn(float degrees) => Mathf.Clamp(degrees, -turn, turn);
        float Size(float share) => Mathf.Clamp(share, LeastSize, MostSize);
        return parts.Length == 3
            ? new Values(0f, Move(numbers[0]), Move(numbers[1]), Size(numbers[2]), 0f, 0f, 0f)
            : new Values(Move(numbers[0]), Move(numbers[1]), Move(numbers[2]), Size(numbers[3]), Turn(numbers[4]), Turn(numbers[5]), Turn(numbers[6]));
    }

    /// <summary>A nudge as <see cref="Read"/> reads it; "" for None.</summary>
    public static string Write(Values values)
    {
        if (values.IsNone) return "";
        return values.Moves
            ? Number(values.Right) + Separator + Number(values.Up) + Separator + Number(values.Ahead) + Separator + Number(values.Size)
                + Separator + Angle(values.Yaw) + Separator + Angle(values.Pitch) + Separator + Angle(values.Roll)
            : Number(values.Up) + Separator + Number(values.Ahead) + Separator + Number(values.Size);
    }

    /// <summary>Keeps the nudge in the look for the item worn in this position; None takes the entry out.</summary>
    public static void Set(OutfitLook look, OutfitItem item, WearPosition position, Values values)
    {
        if (look == null || item == null) return;
        string text = Write(values);
        look.Set(Key(item, position), text.Length == 0 ? null : text);
    }

    private static string Number(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);
    private static string Angle(float value) => value.ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>The item's record on a freshly placed model (its root), emptied: dressing then says each piece it hung (<see cref="Hold"/>).</summary>
    internal static ItemAdjust Begin(Transform model, string itemId)
    {
        if (model == null) return null;
        if (!model.TryGetComponent(out ItemAdjust adjust)) adjust = model.gameObject.AddComponent<ItemAdjust>();
        adjust.item = itemId ?? "";
        adjust.pieces.Clear();
        return adjust;
    }

    /// <summary>
    /// Remembers how dressing put <paramref name="piece"/> on (call once it hangs from what it follows):
    /// <paramref name="pivot"/> the spot it turns and scales about, <paramref name="right"/>, <paramref name="up"/> and
    /// <paramref name="ahead"/> one metre of the model along the position's own axes, all in the world;
    /// <paramref name="side"/> -1 for a pair's left piece.
    /// </summary>
    internal void Hold(Transform piece, Vector3 pivot, Vector3 right, Vector3 up, Vector3 ahead, float side = 1f)
    {
        if (piece == null) return;
        var parent = piece.parent;
        pieces.Add(new Piece
        {
            Part = piece, Position = piece.localPosition, Rotation = piece.localRotation, Scale = piece.localScale,
            Pivot = parent != null ? parent.InverseTransformPoint(pivot) : pivot,
            Right = parent != null ? parent.InverseTransformVector(right) : right,
            Up = parent != null ? parent.InverseTransformVector(up) : up,
            Ahead = parent != null ? parent.InverseTransformVector(ahead) : ahead,
            Side = side < 0f ? -1f : 1f
        });
    }

    /// <summary>
    /// Takes the pieces as they stand now for how dressing put them: what a position's own row moved (WearPosition.at,
    /// turn, scale) is then part of the position, and the player's nudge counts from there.
    /// </summary>
    internal void Settle()
    {
        for (int i = 0; i < pieces.Count; i++)
        {
            var piece = pieces[i];
            if (piece.Part == null) continue;
            piece.Position = piece.Part.localPosition;
            piece.Rotation = piece.Part.localRotation;
            piece.Scale = piece.Part.localScale;
            pieces[i] = piece;
        }
    }

    /// <summary>Puts every piece where dressing put it, nudged by <paramref name="values"/>.</summary>
    public void Apply(Values values)
    {
        foreach (var piece in pieces)
        {
            if (piece.Part == null) continue;
            // A pair's left piece mirrors: right is outward for both, and yaw and roll turn the other way.
            var turn = Quaternion.AngleAxis(values.Yaw * piece.Side, Axis(piece.Up)) * Quaternion.AngleAxis(values.Pitch, Axis(piece.Right))
                * Quaternion.AngleAxis(values.Roll * piece.Side, Axis(piece.Ahead));
            piece.Part.localScale = piece.Scale * values.Size;
            piece.Part.localRotation = turn * piece.Rotation;
            piece.Part.localPosition = piece.Pivot + turn * ((piece.Position - piece.Pivot) * values.Size)
                + piece.Right * (values.Right * piece.Side) + piece.Up * values.Up + piece.Ahead * values.Ahead;
        }
    }

    private static Vector3 Axis(Vector3 along) => along.sqrMagnitude > 1e-12f ? along.normalized : Vector3.up;

    /// <summary>The item's record on a dressed model, or null.</summary>
    public static ItemAdjust On(GameObject model, string itemId)
    {
        if (model == null) return null;
        foreach (var adjust in model.GetComponentsInChildren<ItemAdjust>(true))
            if (string.Equals(adjust.item, itemId, StringComparison.OrdinalIgnoreCase)) return adjust;
        return null;
    }

    /// <summary>How many pieces it holds, and where each stands and how big it is in the world: the bench's measure of a position.</summary>
    internal int PieceCount => pieces.Count;

    internal bool TryPiece(int index, out Transform part, out Vector3 pivot)
    {
        part = null;
        pivot = default;
        if (index < 0 || index >= pieces.Count || pieces[index].Part == null) return false;
        part = pieces[index].Part;
        var parent = part.parent;
        pivot = parent != null ? parent.TransformPoint(pieces[index].Pivot) : pieces[index].Pivot;
        return true;
    }
}
