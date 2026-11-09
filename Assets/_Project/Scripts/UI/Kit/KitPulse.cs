using UnityEngine;
using UnityEngine.UI;

/// <summary>A one-off flash on an image (usually a KitButtons.Lid): it jumps to a colour and fades back to clear.</summary>
[DisallowMultipleComponent]
public sealed class KitPulse : MonoBehaviour
{
    private Image image;
    private Color color;
    private float startAt, seconds;

    /// <summary>Flashes <paramref name="target"/> in <paramref name="flash"/>, fading over <paramref name="length"/> (s).</summary>
    public static void Play(Image target, Color flash, float length = .35f)
    {
        if (target == null) return;
        var pulse = target.TryGetComponent(out KitPulse found) ? found : target.gameObject.AddComponent<KitPulse>();
        pulse.image = target;
        pulse.color = flash;
        pulse.startAt = Time.unscaledTime;
        pulse.seconds = Mathf.Max(.01f, length);
        pulse.enabled = true;
        pulse.Update();
    }

    private void Update()
    {
        if (image == null) { enabled = false; return; }
        float k = Mathf.Clamp01((Time.unscaledTime - startAt) / seconds);
        var now = color;
        now.a = color.a * (1f - k) * (1f - k);
        image.color = now;
        if (k >= 1f) enabled = false;
    }
}
