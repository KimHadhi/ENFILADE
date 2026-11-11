using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A small white figure standing on the health bar's right end that shows what the body is doing: standing with the
/// gun up, on one knee, flat on the belly, sliding, on the back, down and hurt, or climbing. The user crouched without
/// knowing it and wondered why the shamblers looked so big (2026-11-11: "앉아 있는 줄 모르고 어 쉠블러들이 왜 이리
/// 크지 싶었는데"), and asked for exactly this over the health bar. A change of stance pops the new figure in pink for a
/// blink and settles it white; the old one is gone at once, so nothing lingers. It keeps clear of the health number's
/// row (the shield's "+N" can reach past the bar) by standing just after it. Pictures: Resources/UI/Icons/Stance_*.png
/// (Tools/Icons/stance_icons.py); numbers: Hud.csv's Stance rows. Play only.
/// </summary>
public sealed class StanceFigure
{
    public const string Row = "Stance";

    /// <summary>What the figure can show, each a picture Stance_&lt;name&gt;.</summary>
    public enum Pose { None, Stand, Crouch, Prone, Slide, Supine, Downed, Climb }

    [Serializable]
    public sealed class Settings
    {
        [Tooltip("The figure's square side (px at 1080p); the pictures put the feet on its bottom edge.")]
        public float size = 40f;
        [Tooltip("How far above the bar's top edge the feet stand (px).")]
        public float lift = 2f;
        [Tooltip("Least room between the health number's row and the figure (px).")]
        public float clear = 8f;
        [Tooltip("How see-through the figure rests (0 to 1): it supports the bar, so it stays a little quiet.")]
        public float alpha = .88f;
        [Tooltip("Seconds a new stance pops in: from popScale and pink down to its size in white.")]
        public float popSeconds = .14f;
        [Tooltip("How large a new stance starts.")]
        public float popScale = 1.35f;
    }

    public readonly Settings Config = new Settings();
    private readonly RectTransform rect, row;
    private readonly RawImage image;
    private readonly Transform space;
    private static readonly Texture2D[] pictures = new Texture2D[Enum.GetValues(typeof(Pose)).Length];
    private static readonly Vector3[] corners = new Vector3[4];
    private Pose shown = Pose.None;
    private float popAge = float.MaxValue, barWidth, barHeight;

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
        rect.pivot = new Vector2(.5f, 0f); // feet on the bar: pops grow up from them
        rect.gameObject.AddComponent<LayoutElement>().ignoreLayout = true;
        image = rect.gameObject.AddComponent<RawImage>();
        image.raycastTarget = false;
        image.enabled = false;
    }

    /// <summary>The bar's size in the root's units: the figure stands over its right end.</summary>
    public void Place(float width, float height)
    {
        barWidth = width;
        barHeight = height;
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
        if (!image.enabled) return;
        float size = Mathf.Max(8f, Config.size);
        rect.sizeDelta = new Vector2(size, size);
        float x = Mathf.Max(barWidth - size * .5f, RowEnd() + Config.clear + size * .5f);
        rect.anchoredPosition = new Vector2(x, barHeight + Config.lift);

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

    // The right end of the number's row (its labels as drawn), in the bar's units; the row leans on its own.
    private float RowEnd()
    {
        if (row == null || space == null) return 0f;
        float end = float.NegativeInfinity;
        foreach (Transform child in row)
        {
            if (!child.gameObject.activeInHierarchy || child is not RectTransform label || !label.TryGetComponent(out Graphic drawn)
                || !drawn.enabled) continue;
            label.GetWorldCorners(corners);
            foreach (var corner in corners) end = Mathf.Max(end, space.InverseTransformPoint(corner).x);
        }
        return float.IsNegativeInfinity(end) ? 0f : end;
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
