using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A HUD part answering something that happened to what it shows (ammo taken, health given back): a pale lid over it,
/// there in a frame and gone in a third of a second. The lid is a child of the part itself, so it stands on whatever
/// plane the part stands on and leans as it leans. The user's pick of the pickup drafts (2026-11-10, draft C): the list
/// at the bottom says what was taken (PickupFeedHUD) and the part it went to only blinks.
/// </summary>
[DisallowMultipleComponent]
public sealed class HudBlink : MonoBehaviour
{
    private const string LidName = "Blink";
    // In, held and out (real s), and the lid's opacity at its most.
    private const float InSeconds = .03f, HoldSeconds = .1f, OutSeconds = .35f, Most = .5f;

    private Image lid;
    private Color tint;
    private float age;

    /// <summary>Blinks <paramref name="target"/> in this colour; the lid reaches <paramref name="pad"/> px past its rect.</summary>
    public static void On(RectTransform target, Color color, float pad = 4f)
    {
        if (target == null || !target.gameObject.activeInHierarchy) return;
        var found = target.Find(LidName);
        var blink = found != null ? found.GetComponent<HudBlink>() : null;
        if (blink == null) blink = Make(target, pad);
        blink.tint = color;
        blink.age = 0f;
        blink.enabled = true;
        blink.transform.SetAsLastSibling();
        blink.Show();
    }

    private static HudBlink Make(RectTransform target, float pad)
    {
        var host = new GameObject(LidName, typeof(RectTransform), typeof(Image), typeof(LayoutElement), typeof(HudBlink));
        host.layer = target.gameObject.layer;
        var rect = (RectTransform)host.transform;
        rect.SetParent(target, false);
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(-pad, -pad); rect.offsetMax = new Vector2(pad, pad);
        // Not one of the part's own pieces: a layout group there leaves it alone.
        host.GetComponent<LayoutElement>().ignoreLayout = true;
        var blink = host.GetComponent<HudBlink>();
        blink.lid = host.GetComponent<Image>();
        blink.lid.raycastTarget = false;
        return blink;
    }

    private void Update()
    {
        age += Time.unscaledDeltaTime;
        if (age >= InSeconds + HoldSeconds + OutSeconds) { lid.enabled = false; enabled = false; return; }
        Show();
    }

    private void Show()
    {
        float shown = age < InSeconds ? age / InSeconds : age < InSeconds + HoldSeconds ? 1f : 1f - (age - InSeconds - HoldSeconds) / OutSeconds;
        lid.enabled = true;
        lid.color = new Color(tint.r, tint.g, tint.b, tint.a * Most * Mathf.Clamp01(shown));
    }
}
