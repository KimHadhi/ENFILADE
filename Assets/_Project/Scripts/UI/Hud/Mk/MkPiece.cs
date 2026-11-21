using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One piece of HUD Mk II: a frame the size of the 1920 x 1080 reference screen, pinned to the screen corner (or edge
/// middle) the piece stands nearest, so its px from that corner stay what the addon draws at 1080p on any screen shape
/// (as HudFrame keeps the old HUD's margins). Inside it a piece places everything in px from the frame's top left, y
/// down, the addon's own numbers. MkHud moves every frame by the sway and turns it with a lean. Each frame is a direct
/// child of the Mk II canvas, so the HUD overlap check measures each piece on its own (HudOverlap.csv: a canvas is a
/// group).
/// </summary>
public abstract class MkPiece
{
    protected readonly MkHud Hud;
    public readonly RectTransform Frame;
    protected readonly CanvasGroup Group;

    /// <summary>The piece moves with the HUD's sway (all but the screen's edge glow).</summary>
    public virtual bool Sways => true;

    protected MkLook Look => Hud.Look;

    protected MkPiece(MkHud hud, RectTransform canvas, string name, Vector2 corner)
    {
        Hud = hud;
        Frame = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        Frame.SetParent(canvas, false);
        Frame.gameObject.layer = canvas.gameObject.layer;
        Frame.anchorMin = Frame.anchorMax = Frame.pivot = corner;
        var scaler = canvas.GetComponentInParent<CanvasScaler>();
        Frame.sizeDelta = scaler != null ? scaler.referenceResolution : new Vector2(1920f, 1080f);
        Group = Frame.gameObject.AddComponent<CanvasGroup>();
        Group.interactable = Group.blocksRaycasts = false;
    }

    /// <summary>This frame's look (real time).</summary>
    public abstract void Tick(float dt);

    /// <summary>The HUD is going: let go of whatever the piece listens to.</summary>
    public virtual void Dispose() { }

    /// <summary>A picture in the frame, centred on a point (px from the top left, y down).</summary>
    protected RawImage Picture(string name, float x, float y, float size)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(Frame, false);
        rect.gameObject.layer = Frame.gameObject.layer;
        rect.anchorMin = rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(.5f, .5f);
        rect.anchoredPosition = new Vector2(x, -y);
        rect.sizeDelta = new Vector2(size, size);
        var image = rect.gameObject.AddComponent<RawImage>();
        image.raycastTarget = false;
        image.enabled = false;
        return image;
    }

    /// <summary>Moves a picture made by Picture to another point.</summary>
    protected static void Place(RawImage image, float x, float y, float size)
    {
        var rect = image.rectTransform;
        var at = new Vector2(x, -y);
        if (rect.anchoredPosition != at) rect.anchoredPosition = at;
        var box = new Vector2(size, size);
        if (rect.sizeDelta != box) rect.sizeDelta = box;
    }

    /// <summary>Shows a picture with this texture and colour, or hides it when there is none.</summary>
    protected static void Show(RawImage image, Texture texture, Color colour)
    {
        bool on = texture != null && colour.a > .002f;
        if (image.enabled != on) image.enabled = on;
        if (!on) return;
        if (image.texture != texture) image.texture = texture;
        if (image.color != colour) image.color = colour;
    }
}

/// <summary>
/// The addon's bar with its loss trail (Nene.BL_Rect, BSS_Rect): what is shown follows the value a step each frame
/// (Status.follow); a change holds the trail where it was for Status.wait seconds, after which it follows the value at
/// Status.lag. A rise starts the trail from what is shown, so it never shows above the bar.
/// </summary>
public sealed class MkTrail
{
    public float Shown, Delta;
    private float lag, last = float.NaN, waitUntil;
    private bool trigger;

    public void Tick(float value, float dt, float follow, float wait, float lagRate)
    {
        float now = Time.unscaledTime;
        Shown = MkText.Follow(Shown, value, follow, dt);
        if (float.IsNaN(last) || !Mathf.Approximately(value, last))
        {
            waitUntil = now + wait;
            trigger = false;
            if (float.IsNaN(last) || Delta < value) Delta = Shown;
            last = value;
        }
        if (!trigger && now >= waitUntil) trigger = true;
        if (!trigger) return;
        lag = MkText.Follow(lag, value, lagRate, dt);
        Delta = lag;
    }
}
