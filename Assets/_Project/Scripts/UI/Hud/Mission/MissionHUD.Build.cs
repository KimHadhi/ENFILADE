using TMPro;
using UnityEngine;
using UnityEngine.UI;

// MissionHUD: its parts, made in code (GameHud; until 2026-11-23 they were GameHUD.prefab's, saved in that prefab's old
// orange and mint). Laid where the prefab laid them, which MissionHUD reads as their homes (FindParts); MissionBarLook
// then shapes and dresses the bar from Hud.csv, and Tone the key cap and the hint.
public sealed partial class MissionHUD
{
    private const string HudText = "HudText";

    /// <summary>The objective block on the HUD's left side: the tracker, the bar, the Tab key cap, its hint, the alert.</summary>
    internal static MissionHUD Build(Transform side)
    {
        var block = FeelUI.Rect("Mission", side);
        block.anchorMin = Vector2.zero;
        block.anchorMax = Vector2.one;
        // The side is a 100 px box; this reaches the whole 1920 x 1080 canvas round it.
        block.sizeDelta = new Vector2(1820f, 980f);
        var hud = block.gameObject.AddComponent<MissionHUD>();
        hud.tracker = block.gameObject.AddComponent<MissionTracker>();

        // The bar: its ! tab, its words, its count. MissionBarLook gives each its place and size.
        var bar = At("MissionBar", block, new Vector2(0f, 1f), new Vector2(70f, -100f), new Vector2(380f, 48f), new Vector2(0f, .5f));
        GameHud.Image(bar, HudTone.Box);
        var badge = FeelUI.Rect("ObjectiveBadge", bar);
        badge.anchorMin = Vector2.zero;
        badge.anchorMax = new Vector2(0f, 1f);
        badge.sizeDelta = new Vector2(49f, 0f);
        badge.anchoredPosition = new Vector2(24.5f, 0f);
        badge.gameObject.AddComponent<MissionBadgeGraphic>().raycastTarget = false;
        GameHud.Text(GameHud.Fill("Symbol", badge), FontRoles.HudBold, 32f, TextAlignmentOptions.Center, HudTone.Ink).text = "!";
        var title = GameHud.Fill("Title", bar);
        title.offsetMin = new Vector2(61f, 4f);
        title.offsetMax = new Vector2(-16f, -4f);
        hud.title = GameHud.Text(title, FontRoles.HudBold, 25f, TextAlignmentOptions.MidlineLeft, HudTone.Text);
        hud.title.enableAutoSizing = true;
        hud.title.fontSizeMin = 18f;
        hud.title.fontSizeMax = 25f;
        hud.title.text = Texts.Get("objective.waiting");
        var count = FeelUI.Rect("ProgressCounter", bar);
        count.anchorMin = new Vector2(1f, 0f);
        count.anchorMax = Vector2.one;
        count.pivot = new Vector2(1f, .5f);
        count.sizeDelta = new Vector2(100f, 0f);
        GameHud.Image(count, HudTone.Box);
        var edge = FeelUI.Rect("Divider", count);
        edge.anchorMin = Vector2.zero;
        edge.anchorMax = new Vector2(0f, 1f);
        edge.anchoredPosition = new Vector2(1f, 0f);
        edge.sizeDelta = new Vector2(2f, -18f);
        GameHud.Image(edge, HudTone.Text);
        var number = GameHud.Fill("Count", count);
        number.offsetMin = new Vector2(10f, 0f);
        number.offsetMax = new Vector2(-8f, 0f);
        hud.counter = GameHud.Text(number, FontRoles.Label, 23f, TextAlignmentOptions.Center, HudTone.Text);
        hud.missionBar = bar;
        hud.counterRoot = count.gameObject;

        // Under the bar: the scoreboard key's cap and what it opens.
        var cap = At(KeyCapName, block, new Vector2(0f, 1f), new Vector2(91f, -152f), new Vector2(42f, 25f), new Vector2(.5f, .5f));
        GameHud.Image(cap, HudTone.Box);
        GameHud.Text(GameHud.Fill("Label", cap), FontRoles.HudBold, 15f, TextAlignmentOptions.Center, HudTone.Text);
        var hint = At("BoardHint", block, new Vector2(0f, 1f), new Vector2(238f, -152f), new Vector2(234f, 25f), new Vector2(.5f, .5f));
        hud.boardHint = GameHud.Text(hint, FontRoles.HudBold, 18f, TextAlignmentOptions.MidlineLeft, HudTone.Grey);

        // The new-objective alert draws its own card and ribbon (ObjectiveAlert); HudRig lays it flat over the screen.
        hud.alert = At("ObjectiveAlert", block, new Vector2(.5f, 1f), new Vector2(0f, -195f), new Vector2(440f, 96f), new Vector2(.5f, .5f));
        hud.alertGroup = hud.alert.gameObject.AddComponent<CanvasGroup>();
        hud.alertGroup.alpha = 0f;
        hud.alertGroup.blocksRaycasts = false;
        return hud;
    }

    private static RectTransform At(string name, Transform parent, Vector2 anchor, Vector2 at, Vector2 size, Vector2 pivot)
    {
        var rect = FeelUI.Rect(name, parent);
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = at;
        rect.sizeDelta = size;
        return rect;
    }
}
