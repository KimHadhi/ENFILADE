using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The ENFILADE tower's own bit of HUD, at the top of the screen (TowerRun fills it every frame): the scanners' count in
/// big numbers once someone's palm is on one (and the doors' count after), and a row of the four lanes, each with where
/// it is and what it is doing, this player's own in pink. The objective itself stays on the mission HUD.
/// </summary>
[DisallowMultipleComponent]
public sealed class TowerHUD : MonoBehaviour
{
    // Px at 1080p: the count's plate and how far down it hangs, a lane's plate and the gap between them.
    private static readonly Vector2 CountSize = new Vector2(250f, 132f), LaneSize = new Vector2(214f, 46f);
    private const float CountTop = 96f, LanesTop = 240f, LaneGap = 10f;

    private RectTransform countRoot, laneRoot;
    private TextMeshProUGUI number, caption;
    private readonly Image[] lanePlates = new Image[TowerLayout.Lanes];
    private readonly TextMeshProUGUI[] laneTexts = new TextMeshProUGUI[TowerLayout.Lanes];
    private int shownSecond = -1;
    private float pop;

    /// <summary>The HUD, made on an overlay canvas of its own under the banners.</summary>
    public static TowerHUD Make(Transform owner)
    {
        var host = new GameObject("Tower HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        host.transform.SetParent(owner, false);
        var hud = host.AddComponent<TowerHUD>();
        hud.Build();
        return hud;
    }

    private void Build()
    {
        FeelUI.SetupOverlay(gameObject, 60);
        countRoot = FeelUI.Rect("Count", transform);
        Top(countRoot, CountTop, CountSize);
        var plate = FeelUI.Panel("Plate", countRoot, KitStyle.Panel);
        KitStyle.Stretch(plate.rectTransform);
        KitSkew.On(plate, 14f);
        var edge = FeelUI.Panel("Edge", countRoot, KitStyle.Accent);
        edge.rectTransform.anchorMin = new Vector2(0f, 0f);
        edge.rectTransform.anchorMax = new Vector2(1f, 0f);
        edge.rectTransform.pivot = new Vector2(.5f, 0f);
        edge.rectTransform.sizeDelta = new Vector2(0f, 6f);
        KitSkew.On(edge, 14f);
        caption = Text("Caption", countRoot, 24f, KitStyle.Soft);
        caption.rectTransform.anchoredPosition = new Vector2(0f, 40f);
        caption.rectTransform.sizeDelta = new Vector2(CountSize.x + 200f, 32f);
        number = Text("Number", countRoot, 84f, KitStyle.Ice);
        number.rectTransform.anchoredPosition = new Vector2(0f, -12f);
        number.rectTransform.sizeDelta = new Vector2(CountSize.x, 96f);
        countRoot.gameObject.SetActive(false);

        laneRoot = FeelUI.Rect("Lanes", transform);
        float width = TowerLayout.Lanes * LaneSize.x + (TowerLayout.Lanes - 1) * LaneGap;
        Top(laneRoot, LanesTop, new Vector2(width, LaneSize.y));
        for (int i = 0; i < TowerLayout.Lanes; i++)
        {
            var slot = FeelUI.Rect("Lane " + Letter(i), laneRoot);
            slot.sizeDelta = LaneSize;
            slot.anchoredPosition = new Vector2(-width * .5f + LaneSize.x * .5f + i * (LaneSize.x + LaneGap), 0f);
            lanePlates[i] = FeelUI.Panel("Plate", slot, KitStyle.Panel);
            KitStyle.Stretch(lanePlates[i].rectTransform);
            KitSkew.On(lanePlates[i], 10f);
            laneTexts[i] = Text("Words", slot, 21f, KitStyle.Text);
            KitStyle.Stretch(laneTexts[i].rectTransform);
        }
        laneRoot.gameObject.SetActive(false);
    }

    /// <summary>The lane's letter: A, B, C, D.</summary>
    public static string Letter(int lane) => ((char)('A' + Mathf.Clamp(lane, 0, 25))).ToString();

    /// <summary>The count this frame (whole seconds rounded up), or hidden.</summary>
    public void Count(bool show, float seconds, string words)
    {
        if (countRoot.gameObject.activeSelf != show) countRoot.gameObject.SetActive(show);
        if (!show) { shownSecond = -1; return; }
        int second = Mathf.Max(0, Mathf.CeilToInt(seconds));
        if (second != shownSecond)
        {
            shownSecond = second;
            number.text = second.ToString();
            pop = 1f;
        }
        caption.text = words ?? "";
        pop = Mathf.MoveTowards(pop, 0f, Time.unscaledDeltaTime * 5f);
        float scale = 1f + .28f * pop * pop;
        number.rectTransform.localScale = new Vector3(scale, scale, 1f);
        number.color = second <= 3 ? Color.Lerp(KitStyle.Ice, KitStyle.Accent, .35f + .65f * pop) : KitStyle.Ice;
    }

    /// <summary>Whether the lane row shows this frame.</summary>
    public void ShowLanes(bool show)
    {
        if (laneRoot.gameObject.activeSelf != show) laneRoot.gameObject.SetActive(show);
    }

    /// <summary>One lane's plate: its words, and pink when it is this player's.</summary>
    public void Lane(int lane, string words, bool mine, bool faded)
    {
        if (lane < 0 || lane >= TowerLayout.Lanes) return;
        laneTexts[lane].text = words ?? "";
        laneTexts[lane].color = mine ? KitStyle.Ink : faded ? KitStyle.Soft : KitStyle.Text;
        lanePlates[lane].color = mine ? KitStyle.Accent : faded ? KitStyle.With(KitStyle.Panel, .55f) : KitStyle.Panel;
    }

    private static void Top(RectTransform rect, float down, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, 1f);
        rect.pivot = new Vector2(.5f, 1f);
        rect.anchoredPosition = new Vector2(0f, -down);
        rect.sizeDelta = size;
    }

    private static TextMeshProUGUI Text(string name, Transform parent, float size, Color color)
    {
        var text = FeelUI.Rect(name, parent).gameObject.AddComponent<TextMeshProUGUI>();
        text.raycastTarget = false;
        FontRoles.Apply(text, FontRoles.Label);
        text.fontSize = size;
        text.fontStyle = FontStyles.Bold;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.color = color;
        return text;
    }
}
