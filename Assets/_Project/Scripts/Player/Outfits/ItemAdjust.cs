using System;
using System.Globalization;
using UnityEngine;

/// <summary>
/// The player's own nudge of a piece made for anyone (a rigid piece fitted to the face, the welding mask; one hung on
/// measured spots, the Ene earrings): up or down, ahead or back, bigger or smaller, from where dressing put it (the
/// user, 2026-11-14: "이런 액세서리류는 맘대로 크기나 위치를 조정할 수 있을까?", after the mask still sat low on some
/// faces). Clothes bound to the body's bones (BOOTH outfits) can't move apart from the body, so only these pieces offer
/// it. Kept in the character's outfit look as "adjust:&lt;item&gt;=up_ahead_size" (OutfitLook), so presets, the hub's body
/// and every other PC wear it the same. Up and ahead are metres of the model's own space along the character's up and
/// the way it faces (worn back, the way the back of the head faces); size scales about the piece's own spot (the mask's
/// glass). Slider ranges: Tables/ItemAdjust.csv.
/// The component remembers how dressing put the piece on, so the wardrobe's sliders move it live with nothing made again.
/// </summary>
[DisallowMultipleComponent]
public sealed class ItemAdjust : MonoBehaviour
{
    public const string Prefix = "adjust:", TableName = "ItemAdjust";
    private const char Separator = '_';

    public readonly struct Values
    {
        public readonly float Up, Ahead, Size;

        public Values(float up, float ahead, float size)
        {
            Up = up;
            Ahead = ahead;
            Size = size;
        }

        public static readonly Values None = new Values(0f, 0f, 1f);
        public bool IsNone => Mathf.Abs(Up) < 1e-4f && Mathf.Abs(Ahead) < 1e-4f && Mathf.Abs(Size - 1f) < 1e-3f;
    }

    private string item = "";
    // As dressing put it, in its parent's space: its place and size, the spot it scales about, and one metre of the
    // model's up and ahead.
    private Vector3 basePosition, baseScale, pivot, up, ahead;

    /// <summary>How far the sliders reach: up and ahead (metres of the model, either way) and the size's two ends.</summary>
    public static float MoveRange => Mathf.Max(.001f, GameTables.Number(TableName, "Range", "move", .06f));
    public static float LeastSize => Mathf.Clamp(GameTables.Number(TableName, "Range", "least", .5f), .05f, 1f);
    public static float MostSize => Mathf.Max(1f, GameTables.Number(TableName, "Range", "most", 1.6f));

    /// <summary>Whether the item can be nudged on this character: a piece made for anyone (OutfitLibrary.AnySkin).</summary>
    public static bool For(OutfitItem item, string skinId) => item != null && OutfitCatalog.Model(item.id, skinId) is { ForAnyone: true };

    /// <summary>The nudge the look keeps for the item; None when it has none.</summary>
    public static Values Of(OutfitLook look, string itemId)
    {
        if (look == null || string.IsNullOrEmpty(itemId) || !look.TryGet(Prefix + itemId, out var pick)) return Values.None;
        string[] parts = pick.Item.Split(Separator);
        if (parts.Length != 3 || !TableCells.TryNumber(parts[0], out float upBy) || !TableCells.TryNumber(parts[1], out float aheadBy)
            || !TableCells.TryNumber(parts[2], out float size))
            return Values.None;
        float move = MoveRange;
        return new Values(Mathf.Clamp(upBy, -move, move), Mathf.Clamp(aheadBy, -move, move), Mathf.Clamp(size, LeastSize, MostSize));
    }

    /// <summary>Keeps the nudge in the look; None takes the entry out.</summary>
    public static void Set(OutfitLook look, string itemId, Values values)
    {
        if (look == null || string.IsNullOrEmpty(itemId)) return;
        look.Set(Prefix + itemId, values.IsNone ? null
            : Number(values.Up) + Separator + Number(values.Ahead) + Separator + Number(values.Size));
    }

    private static string Number(float value) => value.ToString("0.####", CultureInfo.InvariantCulture);

    /// <summary>
    /// Remembers how dressing put <paramref name="piece"/> on (call once it hangs from its bone): <paramref name="pivot"/>
    /// the spot it scales about, <paramref name="up"/> and <paramref name="ahead"/> one metre of the model's up and of the
    /// way the character faces, all in the world.
    /// </summary>
    internal static ItemAdjust Mark(Transform piece, string itemId, Vector3 pivot, Vector3 up, Vector3 ahead)
    {
        if (piece == null) return null;
        if (!piece.TryGetComponent(out ItemAdjust adjust)) adjust = piece.gameObject.AddComponent<ItemAdjust>();
        var parent = piece.parent;
        adjust.item = itemId ?? "";
        adjust.basePosition = piece.localPosition;
        adjust.baseScale = piece.localScale;
        adjust.pivot = parent != null ? parent.InverseTransformPoint(pivot) : pivot;
        adjust.up = parent != null ? parent.InverseTransformVector(up) : up;
        adjust.ahead = parent != null ? parent.InverseTransformVector(ahead) : ahead;
        return adjust;
    }

    /// <summary>Puts the piece where dressing put it, nudged by <paramref name="values"/>.</summary>
    public void Apply(Values values)
    {
        var piece = transform;
        piece.localScale = baseScale * values.Size;
        piece.localPosition = pivot + (basePosition - pivot) * values.Size + up * values.Up + ahead * values.Ahead;
    }

    /// <summary>The item's piece on a dressed model, or null.</summary>
    public static ItemAdjust On(GameObject model, string itemId)
    {
        if (model == null) return null;
        foreach (var adjust in model.GetComponentsInChildren<ItemAdjust>(true))
            if (string.Equals(adjust.item, itemId, StringComparison.OrdinalIgnoreCase)) return adjust;
        return null;
    }
}
