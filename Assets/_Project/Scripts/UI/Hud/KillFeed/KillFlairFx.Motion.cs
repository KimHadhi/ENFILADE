using UnityEngine;
using UnityEngine.UI;

// KillFlairFx: motion. One part of the class; the rest is in KillFlairFx.cs and the files beside it.
public sealed partial class KillFlairFx
{
    private static void Slammed(RectTransform piece, CanvasGroup group, float t, float show, float tilt)
    {
        float s = Mathf.Clamp01(t / Slam);
        // 2.3x and flat, down past its size, and back: a slam, not a fade.
        float scale = s < .7f ? Mathf.Lerp(2.3f, .92f, Ease.Out(s / .7f)) : Mathf.Lerp(.92f, 1f, (s - .7f) / .3f);
        piece.localScale = Vector3.one * scale;
        piece.localRotation = Quaternion.Euler(0f, 0f, tilt);
        group.alpha = (t >= 0f ? Mathf.Clamp01(s / .4f) : 0f) * show;
    }

    private void Specks(float t)
    {
        for (int i = 0; i < specks.Length; i++)
        {
            var speck = specks[i];
            if (speck == null) continue;
            float s = Mathf.Clamp01(t / .26f);
            speck.rectTransform.anchoredPosition = speckWay[i] * Ease.Out(s);
            var c = speck.color;
            c.a = t < 0f ? 0f : 1f - s;
            speck.color = c;
        }
    }

    // Full until the flair's seconds are up, then gone over Fade.
    private static float Out(float t, float life) => t < 0f ? 0f : 1f - Mathf.Clamp01((t - life) / Fade);

    private static float Back(float s) { s = Mathf.Clamp01(s); return 1f + 2.2f * Mathf.Pow(s - 1f, 3f) + 1.2f * Mathf.Pow(s - 1f, 2f); }

    private static Color Clear(Color c) { c.a = 0f; return c; }

    private void HideAll()
    {
        if (layer != null) layer.gameObject.SetActive(false);
        foreach (var piece in new Component[] { stamp, seal, chip, strike, frame, tick, tag, veil, beam, markRing, spark, ring, slash })
            if (piece != null) piece.gameObject.SetActive(false);
        foreach (var group in new[] { echoes, rays, crosses })
            foreach (var piece in group)
                if (piece != null) piece.gameObject.SetActive(false);
        if (mark != null) mark.rectTransform.localScale = Vector3.one;
    }

    private static void SetAlpha(Graphic graphic, float alpha)
    {
        var c = graphic.color;
        c.a = alpha;
        graphic.color = c;
    }

    private static CanvasGroup Group(RectTransform piece)
    {
        var group = piece.gameObject.AddComponent<CanvasGroup>();
        group.interactable = group.blocksRaycasts = false;
        return group;
    }

    private static Image Fill(string name, RectTransform parent, float inset)
    {
        var image = FeelUI.Rect(name, parent).gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        Stretch(image.rectTransform, inset);
        return image;
    }

    private static Image SpriteImage(string name, RectTransform parent, Sprite sprite)
    {
        var image = FeelUI.Rect(name, parent).gameObject.AddComponent<Image>();
        image.sprite = sprite;
        image.raycastTarget = false;
        return image;
    }

    private static RawImage Raw(string name, RectTransform parent, Texture texture)
    {
        var image = FeelUI.Rect(name, parent).gameObject.AddComponent<RawImage>();
        image.texture = texture;
        image.raycastTarget = false;
        return image;
    }

    private static Image Stroke(string name, RectTransform parent)
    {
        var image = FeelUI.Rect(name, parent).gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        image.rectTransform.pivot = new Vector2(0f, .5f);
        return image;
    }

    private static void Stretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.one * inset; rect.offsetMax = -Vector2.one * inset;
    }

    /// <summary>Lets a row's flair material go with the row.</summary>
    private sealed class OwnedMaterial : MonoBehaviour
    {
        private Material owned;
        public void Own(Material material) => owned = material;
        private void OnDestroy() { if (owned != null) Destroy(owned); }
    }
}
