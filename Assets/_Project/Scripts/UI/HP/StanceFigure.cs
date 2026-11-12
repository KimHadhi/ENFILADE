using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A small white figure beside the health bar's right end that shows what the body is doing: standing with the gun up,
/// on one knee, flat on the belly, sliding, on the back, down and hurt, or climbing. The user crouched without knowing it
/// and wondered why the shamblers looked so big (2026-11-11: "앉아 있는 줄 모르고 어 쉠블러들이 왜 이리 크지 싶었는데"),
/// and asked for exactly this by the health bar. A change of stance pops the new figure in pink for a blink and settles
/// it white; the old one is gone at once, so nothing lingers.
/// <para>It first stood on top of the bar's right end, inside the health number's row, where the shield band, the
/// shield's "+N", the heal "+N" and the "위험" tag all reached it; the user found it crowded (2026-11-12: "지금 너무 겹침.
/// 위치 좀 여유 있게"). Now it has a slot of its own: beside the bar's end (or the shield band's, if that reaches further)
/// at the bar's height, Stance.gap clear of it, and what the bar shows past its end starts past the figure instead
/// (<see cref="End"/>). The number row sits higher than the bar, so the figure is clear of it too; the row's letters are
/// still measured every frame, as the row leans on its own, and should they ever come within Stance.clear the figure
/// steps right past them. It leans at an angle of its own on top of the bar's (Stance.roll and yaw, scaled by
/// Settings → HUD 기울기), Persona-style, yet stays the bar's child: it keeps to the bar's end however the bar leans,
/// slides or shakes, and HudTilt.csv leaves it out of the bar's outline, so the bar's wedge (HudBridge) and spacing are
/// worked out from the bar alone, as before.</para>
/// Pictures: Resources/UI/Icons/Stance_*.png (Tools/Icons/stance_icons.py); numbers: Hud.csv's Stance rows. Play only.
/// </summary>
public sealed class StanceFigure
{
    public const string Row = "Stance";

    /// <summary>What the figure can show, each a picture Stance_&lt;name&gt;.</summary>
    public enum Pose { None, Stand, Crouch, Prone, Slide, Supine, Downed, Climb }

    [Serializable]
    public sealed class Settings
    {
        [Tooltip("The figure's square side (px at 1080p); the pictures put the feet near its bottom edge.")]
        public float size = 40f;
        [Tooltip("Least room between the bar's right end (the shield band's, if it reaches further) and the figure even " +
            "at the top of its pop (px); at rest it has popScale's share more. The bar's own marks past its end (the " +
            "\"위험\" tag, heal +N) start past the figure.")]
        public float gap = 8f;
        [Tooltip("How far the figure's middle sits above the bar's middle (px). A little below keeps its head, popping " +
            "and leaning included, under the thin band (HudBridge) that may run past the bar's end under the shield's +N.")]
        public float rise = -3f;
        [Tooltip("Least room between the figure and the health number row's letters, the shield's +N included (px). " +
            "Closer, and the figure steps right past them.")]
        public float clear = 10f;
        [Tooltip("The figure's own turn within the screen on top of the bar's (°), scaled by Settings → HUD 기울기. " +
            "Positive is anticlockwise: the right side rises.")]
        public float roll = 5f;
        [Tooltip("The figure's own turn about the vertical on top of the bar's (°), scaled the same way. Positive sends " +
            "its right side away.")]
        public float yaw = -10f;
        [Tooltip("How see-through the figure rests (0 to 1): it supports the bar, so it stays a little quiet.")]
        public float alpha = .88f;
        [Tooltip("Seconds a new stance pops in: from popScale and pink down to its size in white.")]
        public float popSeconds = .14f;
        [Tooltip("How large a new stance starts; it grows from its middle, inside its own slot.")]
        public float popScale = 1.35f;
    }

    public readonly Settings Config = new Settings();
    private readonly RectTransform rect, row;
    private readonly RawImage image;
    private readonly Transform space;
    private static readonly Texture2D[] pictures = new Texture2D[Enum.GetValues(typeof(Pose)).Length];
    private Pose shown = Pose.None;
    private float popAge = float.MaxValue, reach, barHeight;
    private Vector2 middle;

    /// <summary>
    /// The right edge of the figure's slot in the bar's units: the bar's own marks past its end (the "위험" tag, heal +N,
    /// HealthBarFlair) go past this, so they never land on the figure.
    /// </summary>
    public float End { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget() => Array.Clear(pictures, 0, pictures.Length);

    /// <param name="root">The bar's own root (AdaptiveHealthHUD's visuals), lower-left pivot: the figure leans with the bar.</param>
    /// <param name="numberRow">The health number's row, which the figure keeps clear of.</param>
    public StanceFigure(RectTransform root, RectTransform numberRow)
    {
        space = root;
        row = numberRow;
        rect = new GameObject("Stance Figure", typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(root, false);
        rect.gameObject.layer = root.gameObject.layer;
        rect.anchorMin = rect.anchorMax = Vector2.zero;
        // Its middle: its own lean and the pop turn and grow it about there, so both stay inside its slot.
        rect.pivot = new Vector2(.5f, .5f);
        rect.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        image = rect.gameObject.AddComponent<RawImage>();
        image.raycastTarget = false;
        image.enabled = false;
    }

    /// <summary>
    /// The bar as drawn, in the root's units: how far right it reaches (the health cells or the shield band, whichever
    /// is longer) and its height. The figure stands past that.
    /// </summary>
    public void Place(float barReach, float height)
    {
        reach = barReach;
        barHeight = height;
        Settle();
    }

    public void Tick(PlayerMovement player, float dt)
    {
        var pose = Of(player);
        if (pose != shown)
        {
            shown = pose;
            image.texture = Picture(pose);
            image.enabled = image.texture != null;
            popAge = 0f;
        }
        // Kept current while hidden too: End holds the bar's marks in one place whether the figure shows or not.
        Settle();
        if (!image.enabled) return;
        float size = Mathf.Max(8f, Config.size);
        rect.sizeDelta = new Vector2(size, size);
        rect.anchoredPosition = middle;
        float tilt = GameSettings.Current.hudTilt;
        rect.localRotation = Quaternion.Euler(0f, -Config.yaw * tilt, Config.roll * tilt);

        popAge += dt;
        float t = Mathf.Clamp01(popAge / Mathf.Max(.01f, Config.popSeconds));
        float eased = 1f - (1f - t) * (1f - t) * (1f - t);
        rect.localScale = Vector3.one * Mathf.Lerp(Mathf.Max(1f, Config.popScale), 1f, eased);
        // Down and hurt stays pink: it is the one stance that asks for help.
        Color rest = shown == Pose.Downed ? KitStyle.Accent : Color.white;
        Color tint = Color.Lerp(KitStyle.Accent, rest, eased);
        tint.a = Mathf.Clamp01(Config.alpha) + (1f - Mathf.Clamp01(Config.alpha)) * (1f - eased);
        image.color = tint;
    }

    // The figure's slot: gap past the bar's reach, on the bar's height (plus rise), and past the number row's letters
    // should they ever come within clear of it. The slot is as wide as the figure at the top of its pop, so a new stance
    // growing for a blink keeps the gap too; at rest it has that much more room on both sides.
    private void Settle()
    {
        float size = Mathf.Max(8f, Config.size);
        // Its own lean turns the square, whose corners then reach this far from its middle.
        float turn = Mathf.Min(45f, Mathf.Abs(Config.roll * GameSettings.Current.hudTilt)) * Mathf.Deg2Rad;
        float half = size * .5f * (Mathf.Cos(turn) + Mathf.Sin(turn));
        float popped = half * Mathf.Max(1f, Config.popScale);
        float height = barHeight * .5f + Config.rise;
        float left = PastLetters(reach + Mathf.Max(0f, Config.gap), popped * 2f, height - half, height + half);
        middle = new Vector2(left + popped, height);
        End = left + popped * 2f;
    }

    // Moves the box from left (width wide, bottom to top) right past any of the number row's letters that come within
    // clear of it. The row leans on its own, so its letters are measured where they are drawn, in the bar's units.
    private float PastLetters(float left, float width, float bottom, float top)
    {
        if (row == null || space == null) return left;
        float clear = Mathf.Max(0f, Config.clear);
        for (int pass = 0; pass < 3; pass++)
        {
            bool moved = false;
            for (int i = 0; i < row.childCount; i++)
            {
                if (!(row.GetChild(i) is RectTransform label) || !label.gameObject.activeInHierarchy || !Drawn(label, out Rect box))
                    continue;
                if (box.yMin >= top + clear || box.yMax <= bottom - clear) continue;
                if (box.xMin >= left + width + clear || box.xMax <= left - clear) continue;
                left = box.xMax + clear;
                moved = true;
            }
            if (!moved) break;
        }
        return left;
    }

    // What a label draws, as a box in the bar's units: a text by its letters standing on their baseline (as HudTilt
    // measures them; a text's own bounds reach down to the font's descender, below the figures), anything else by its rect.
    private bool Drawn(RectTransform label, out Rect box)
    {
        box = default;
        if (!label.TryGetComponent(out Graphic drawn) || !drawn.enabled) return false;
        Rect area = label.rect;
        if (drawn is TMP_Text text)
        {
            var info = text.textInfo;
            if (info?.characterInfo == null) return false;
            float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
            int count = Mathf.Min(info.characterCount, info.characterInfo.Length);
            for (int i = 0; i < count; i++)
            {
                var letter = info.characterInfo[i];
                if (!letter.isVisible) continue;
                xMin = Mathf.Min(xMin, letter.bottomLeft.x); xMax = Mathf.Max(xMax, letter.topRight.x);
                yMin = Mathf.Min(yMin, letter.baseLine); yMax = Mathf.Max(yMax, letter.topRight.y);
            }
            if (xMax <= xMin || yMax <= yMin) return false;
            area = Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }
        float x0 = float.MaxValue, y0 = float.MaxValue, x1 = float.MinValue, y1 = float.MinValue;
        for (int corner = 0; corner < 4; corner++)
        {
            var local = new Vector3(corner < 2 ? area.xMin : area.xMax, corner % 2 == 0 ? area.yMin : area.yMax, 0f);
            Vector3 at = space.InverseTransformPoint(label.TransformPoint(local));
            x0 = Mathf.Min(x0, at.x); y0 = Mathf.Min(y0, at.y);
            x1 = Mathf.Max(x1, at.x); y1 = Mathf.Max(y1, at.y);
        }
        box = Rect.MinMaxRect(x0, y0, x1, y1);
        return true;
    }

    /// <summary>The stance a player's body is in, as the figure shows it.</summary>
    public static Pose Of(PlayerMovement player)
    {
        if (player == null || player.IsDead) return Pose.None;
        if (player.IsDowned) return Pose.Downed;
        if (player.IsProning) return Pose.Prone;
        switch (player.CurrentState)
        {
            case PlayerMovement.MovementState.Crouching:
                return Pose.Crouch;
            case PlayerMovement.MovementState.Proning:
            case PlayerMovement.MovementState.Diving:
            case PlayerMovement.MovementState.HeadFirstSliding:
                return Pose.Prone;
            case PlayerMovement.MovementState.Sliding:
                return Pose.Slide;
            case PlayerMovement.MovementState.Supine:
            case PlayerMovement.MovementState.SupineSliding:
            case PlayerMovement.MovementState.SupineDiving:
                return Pose.Supine;
            case PlayerMovement.MovementState.Ladder:
            case PlayerMovement.MovementState.WallClimb:
            case PlayerMovement.MovementState.LedgeHang:
            case PlayerMovement.MovementState.BarGrab:
            case PlayerMovement.MovementState.BarSwing:
            case PlayerMovement.MovementState.Zipline:
                return Pose.Climb;
            default:
                return Pose.Stand;
        }
    }

    private static Texture2D Picture(Pose pose)
    {
        if (pose == Pose.None) return null;
        int index = (int)pose;
        if (pictures[index] == null) pictures[index] = Resources.Load<Texture2D>("UI/Icons/Stance_" + pose.ToString().ToLowerInvariant());
        return pictures[index];
    }
}
