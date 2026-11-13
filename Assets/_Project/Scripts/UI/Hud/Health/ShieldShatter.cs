using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The HUD's shield band breaking apart when the shield runs out (backlog 321): the band flashes white where it stood,
/// then splits into shards that jump up off the bar, spin, fall and fade in the band's cyan, shrinking as they go.
/// Made once with the HUD (AdaptiveHealthHUD), so a break creates nothing. Real time, held while paused.
/// The screen's part of the same moment: ShieldFeedback.
/// </summary>
public sealed class ShieldShatter
{
    private const int Count = 12;
    // Canvas units and seconds.
    private const float Seconds = .7f, FlashSeconds = .18f, WhiteSeconds = .05f, Gravity = -900f;
    private readonly Image flash;
    private readonly Image[] shards = new Image[Count];
    private readonly Vector2[] position = new Vector2[Count], velocity = new Vector2[Count], size = new Vector2[Count];
    private readonly float[] spin = new float[Count], angle = new float[Count];
    private Vector2 flashSize;
    private Color color;
    private float age = Seconds;

    public bool IsPlaying => age < Seconds;

    public ShieldShatter(RectTransform parent)
    {
        flash = Make("Shield Break Flash", parent);
        for (int i = 0; i < Count; i++) shards[i] = Make("Shield Shard " + i, parent);
    }

    private static Image Make(string name, RectTransform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.gameObject.layer = parent.gameObject.layer;
        rect.anchorMin = rect.anchorMax = Vector2.zero;
        rect.pivot = new Vector2(.5f, .5f);
        rect.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        var image = rect.gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        image.enabled = false;
        return image;
    }

    /// <summary>band: the part of the shield band that was lit, in the parent's units (lower left corner and size).</summary>
    public void Play(Rect band, Color tint)
    {
        if (band.width <= 0f || band.height <= 0f) return;
        color = tint; age = 0f;
        flashSize = band.size + new Vector2(4f, 4f);
        flash.rectTransform.anchoredPosition = band.center;
        // Pieces of uneven width along the lit part, each flying up and away from the band's middle.
        float x = band.xMin, piece = band.width / Count;
        for (int i = 0; i < Count; i++)
        {
            float width = piece * Random.Range(.7f, 1.3f);
            float centre = Mathf.Min(x + width * .5f, band.xMax - width * .25f);
            x += piece;
            size[i] = new Vector2(Mathf.Max(2f, width * .85f), band.height * Random.Range(1f, 1.5f));
            position[i] = new Vector2(centre, band.center.y);
            float side = (centre - band.center.x) / Mathf.Max(1f, band.width * .5f);
            velocity[i] = new Vector2(side * Random.Range(40f, 110f) + Random.Range(-35f, 35f), Random.Range(140f, 320f));
            spin[i] = Random.Range(-560f, 560f);
            angle[i] = Random.Range(-12f, 12f);
        }
        Apply();
    }

    public void Tick(float dt)
    {
        if (!IsPlaying || Time.timeScale <= 0f) return;
        dt = Mathf.Min(.05f, dt);
        age += dt;
        for (int i = 0; i < Count; i++)
        {
            velocity[i].y += Gravity * dt;
            position[i] += velocity[i] * dt;
            angle[i] += spin[i] * dt;
        }
        Apply();
    }

    private void Apply()
    {
        bool playing = IsPlaying;
        float flashLife = Mathf.Clamp01(age / FlashSeconds);
        flash.enabled = playing && flashLife < 1f;
        if (flash.enabled)
        {
            flash.rectTransform.sizeDelta = flashSize * (1f + .25f * flashLife);
            flash.color = new Color(1f, 1f, 1f, (1f - flashLife) * (1f - flashLife));
        }
        float t = Mathf.Clamp01(age / Seconds);
        Color shard = Color.Lerp(Color.white, color, Mathf.Clamp01(age / WhiteSeconds));
        shard.a = Mathf.Pow(1f - t, 1.5f);
        for (int i = 0; i < Count; i++)
        {
            var image = shards[i];
            image.enabled = playing;
            if (!playing) continue;
            var rect = image.rectTransform;
            rect.anchoredPosition = position[i];
            rect.sizeDelta = size[i] * Mathf.Lerp(1f, .45f, t);
            rect.localRotation = Quaternion.Euler(0f, 0f, angle[i]);
            image.color = shard;
        }
    }
}
