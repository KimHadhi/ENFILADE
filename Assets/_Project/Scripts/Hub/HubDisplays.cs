using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The hub's things to read in the world (memo 46): small world-space canvases for the chapter cards, the class
/// lockers' doors, the gun rack's labels and the boards, in the menus' kit (KitStyle: near-black, white, one pink, the
/// score font), and the gun rack's guns themselves, copies of the FPSBody guns with nothing but their looks.
/// A canvas is 400 units to the metre (2.5 mm each): a 30-unit word is 7.5 cm tall, readable from a step or two away.
/// </summary>
public static class HubDisplays
{
    public const float PixelsPerMetre = 400f;

    /// <summary>A world canvas <paramref name="metres"/> across on <paramref name="parent"/>. A canvas reads right for a
    /// viewer looking along its forward, so <paramref name="turn"/> points that forward into the wall or table it sits on.</summary>
    public static RectTransform Board(Transform parent, string name, Vector2 metres, Vector3 local, Quaternion turn)
    {
        var host = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        // World space first: an overlay canvas (the default) drives its own rect to the screen's size.
        host.GetComponent<Canvas>().renderMode = RenderMode.WorldSpace;
        host.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f;
        var rect = host.GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.localPosition = local;
        rect.localRotation = turn;
        rect.sizeDelta = metres * PixelsPerMetre;
        rect.localScale = Vector3.one / PixelsPerMetre;
        return rect;
    }

    /// <summary>Words filling <paramref name="parent"/> (or the box given), in the score font.</summary>
    public static TextMeshProUGUI Words(RectTransform parent, string text, float size, Color color, TextAlignmentOptions alignment,
        Vector2? at = null, Vector2? box = null)
    {
        var label = new GameObject("Words", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        var rect = label.rectTransform;
        rect.SetParent(parent, false);
        if (box.HasValue)
        {
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
            rect.anchoredPosition = at.HasValue ? new Vector2(at.Value.x, -at.Value.y) : Vector2.zero;
            rect.sizeDelta = box.Value;
        }
        else
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
        var font = GunFeelSettings.Instance != null ? GunFeelSettings.Instance.scoreFont : null;
        if (font == null && CombatFeedbackSettings.Instance != null) font = CombatFeedbackSettings.Instance.promptFont;
        if (font != null) label.font = font;
        label.text = text;
        label.fontSize = size;
        label.color = color;
        label.fontStyle = FontStyles.Bold;
        label.alignment = alignment;
        label.textWrappingMode = TextWrappingModes.NoWrap;
        label.overflowMode = TextOverflowModes.Ellipsis;
        label.raycastTarget = false;
        return label;
    }

    /// <summary>A flat colour block at <paramref name="at"/> (px from the top left) of <paramref name="size"/>.</summary>
    public static Image Block(RectTransform parent, string name, Vector2 at, Vector2 size, Color color, bool cut = false)
    {
        var rect = KitStyle.TopLeft(name, parent, at, size);
        return cut ? KitStyle.Cut(rect, color) : KitStyle.Fill(rect, color);
    }

    /// <summary>A picture at <paramref name="at"/> of <paramref name="size"/>; none shows as a dark block.</summary>
    public static RawImage Picture(RectTransform parent, string name, Vector2 at, Vector2 size, Texture texture)
    {
        var rect = KitStyle.TopLeft(name, parent, at, size);
        var image = rect.gameObject.AddComponent<RawImage>();
        image.texture = texture;
        image.color = texture != null ? Color.white : KitStyle.With(KitStyle.Night, .9f);
        image.raycastTarget = false;
        return image;
    }

    /// <summary>
    /// A copy of <paramref name="prefab"/> with nothing but its looks: no scripts, colliders, bodies, sounds or animators.
    /// Made switched off and switched on a frame later, after its scripts are gone, so none of them ever wakes.
    /// </summary>
    public static Transform Showpiece(MonoBehaviour runner, GameObject prefab, Transform parent, System.Action<Transform> woke = null)
    {
        var holder = new GameObject(prefab.name);
        holder.SetActive(false);
        holder.transform.SetParent(parent, false);
        var copy = Object.Instantiate(prefab, holder.transform);
        copy.name = prefab.name;
        // Last first: a part another needs (a body a script asks for) goes after the one that needs it.
        var parts = copy.GetComponentsInChildren<Component>(true);
        for (int i = parts.Length - 1; i >= 0; i--)
        {
            var part = parts[i];
            if (part == null || part is Transform || part is Renderer || part is MeshFilter) continue;
            Object.Destroy(part);
        }
        runner.StartCoroutine(WakeLater(holder, woke));
        return holder.transform;
    }

    private static IEnumerator WakeLater(GameObject holder, System.Action<Transform> woke)
    {
        yield return null;
        if (holder == null) yield break;
        holder.SetActive(true);
        woke?.Invoke(holder.transform);
    }

    /// <summary>"1장 · 옥상 탈출" → "01" and "옥상 탈출"; a name with no chapter in front gives "EX" and the whole name
    /// (as the title screen's map card reads them).</summary>
    public static void SplitChapter(string display, out string number, out string name)
    {
        number = "EX";
        name = display ?? "";
        int dot = name.IndexOf('·');
        if (dot < 0) return;
        string digits = "";
        foreach (char c in name.Substring(0, dot)) if (char.IsDigit(c)) digits += c;
        if (digits.Length == 0 || !int.TryParse(digits, out int chapter)) return;
        number = chapter.ToString("00");
        name = name.Substring(dot + 1).Trim();
    }

    /// <summary>The bounds of everything <paramref name="root"/> draws, in the world.</summary>
    public static Bounds Measure(Transform root)
    {
        var parts = root.GetComponentsInChildren<Renderer>(true);
        if (parts.Length == 0) return new Bounds(root.position, Vector3.zero);
        var bounds = parts[0].bounds;
        foreach (var part in parts) bounds.Encapsulate(part.bounds);
        return bounds;
    }
}
