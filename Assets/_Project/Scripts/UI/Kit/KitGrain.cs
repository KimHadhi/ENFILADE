using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Film grain over a whole screen, very faint: grey noise jumping to a new spot 24 times a second, so a still menu
/// still feels alive. Takes no clicks.
/// </summary>
[DisallowMultipleComponent]
public sealed class KitGrain : MonoBehaviour
{
    private const float Rate = 24f, Alpha = .012f;

    private RawImage image;
    private int shownStep = -1;
    private readonly System.Random random = new System.Random(311);

    public static KitGrain Make(RectTransform canvas)
    {
        var rect = KitStyle.Stretch("Grain", canvas);
        var grain = rect.gameObject.AddComponent<KitGrain>();
        grain.image = rect.gameObject.AddComponent<RawImage>();
        grain.image.texture = KitShapes.Grain;
        grain.image.color = new Color(1f, 1f, 1f, Alpha);
        grain.image.raycastTarget = false;
        return grain;
    }

    private void Update()
    {
        int step = Mathf.FloorToInt(Time.unscaledTime * Rate);
        if (step == shownStep || image == null) return;
        shownStep = step;
        // One texel to about one screen pixel at 1080p, wherever the noise is read from.
        var size = ((RectTransform)transform).rect.size / 128f;
        image.uvRect = new Rect((float)random.NextDouble(), (float)random.NextDouble(), size.x, size.y);
    }
}
