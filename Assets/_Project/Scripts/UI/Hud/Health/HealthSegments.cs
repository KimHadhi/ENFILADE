using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Health as ten cells, each a tenth of the maximum, filled from the left: green, yellow under 40%, red under 25%, as in
/// Left 4 Dead. The cell being used up shows how much of it is left. Teammates' marks and the team panel share it.
/// A shield shows as a thin band of cells over the health's (ShowShield), each over the health cell worth the same, in
/// the shield's own colour, the way the player's own bar wears its shield (AdaptiveHealthHUD): a friend's vitals read
/// the same wherever they are drawn. Before 2026-11-18 a friend's shield was nowhere on the screen.
/// </summary>
public sealed class HealthSegments
{
    public const int Count = 10;
    private const float Gap = 2f;
    private static readonly Color Green = new Color(.35f, .88f, .4f), Amber = new Color(1f, .82f, .25f), Blood = new Color(1f, .27f, .22f),
        Empty = new Color(0f, 0f, 0f, .55f);
    private readonly RectTransform root;
    private readonly Image[] cells = new Image[Count];
    // The shield's band, made the first time a shield shows: most of a game nobody holds one.
    private readonly Vector2 cell;
    private readonly float bandHeight, bandGap;
    private RectTransform band;
    private Image[] bandCells;
    private float bandShare = -1f;
    private static Color shieldColor;
    private static bool shieldColorRead;

    public RectTransform Rect => root;
    public float Width { get; }

    /// <summary>How far the shield's band reaches over the health cells' top (px): its gap and its height.</summary>
    public float BandRoom => bandGap + bandHeight;

    /// <summary>
    /// The shield's colour: Tables/Player Shield.color, the one the player's own shield light, break and bar wear, so a
    /// friend's shield is the same blue as one's own.
    /// </summary>
    public static Color ShieldColor
    {
        get
        {
            if (shieldColorRead) return shieldColor;
            shieldColorRead = true;
            GameTables.Reloaded -= ForgetShieldColor;
            GameTables.Reloaded += ForgetShieldColor;
            string text = GameTables.Text(PlayerMovement.TableName, "Shield", "color", "");
            if (!ColorUtility.TryParseHtmlString(text.Trim(), out shieldColor)) shieldColor = KitStyle.Ice;
            return shieldColor;
        }
    }

    private static void ForgetShieldColor() => shieldColorRead = false;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => shieldColorRead = false;

    /// <summary>
    /// <paramref name="shieldBand"/> is the shield band's height over the health cells and <paramref name="shieldGap"/>
    /// the room between the two (px); whoever lays the cells out leaves BandRoom free over them.
    /// </summary>
    public HealthSegments(Transform parent, Vector2 cell, float shieldBand, float shieldGap)
    {
        this.cell = cell;
        bandHeight = Mathf.Max(1f, shieldBand);
        bandGap = Mathf.Max(0f, shieldGap);
        root = FeelUI.Rect("Health Cells", parent);
        Width = Count * cell.x + (Count - 1) * Gap;
        root.sizeDelta = new Vector2(Width, cell.y);
        for (int i = 0; i < Count; i++)
        {
            var rect = FeelUI.Rect("Cell", root);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, .5f);
            rect.anchoredPosition = new Vector2(i * (cell.x + Gap), 0f);
            rect.sizeDelta = cell;
            cells[i] = rect.gameObject.AddComponent<Image>();
            cells[i].raycastTarget = false;
        }
    }

    public static Color ColorFor(float share) => share < .25f ? Blood : share < .4f ? Amber : Green;
    public static Color Downed => Blood;

    public void Show(float share, Color color)
    {
        share = Mathf.Clamp01(share);
        for (int i = 0; i < Count; i++)
        {
            float left = Mathf.Clamp01(share * Count - i);
            cells[i].color = left <= 0f ? Empty : left >= 1f ? color : new Color(color.r, color.g, color.b, Mathf.Lerp(.3f, .85f, left));
        }
    }

    /// <summary>
    /// The shield over the health: <paramref name="share"/> is the shield as a share of the maximum health, so a band cell
    /// is worth what the health cell under it is (more than the maximum fills all ten). 0 or less takes the band away.
    /// </summary>
    public void ShowShield(float share)
    {
        share = Mathf.Clamp01(share);
        if (share == bandShare) return;
        bandShare = share;
        if (share <= 0f)
        {
            if (band != null && band.gameObject.activeSelf) band.gameObject.SetActive(false);
            return;
        }
        if (band == null) BuildBand();
        if (!band.gameObject.activeSelf) band.gameObject.SetActive(true);
        Color color = ShieldColor;
        for (int i = 0; i < Count; i++)
        {
            float left = Mathf.Clamp01(share * Count - i);
            bandCells[i].enabled = left > 0f;
            if (left > 0f) bandCells[i].color = left >= 1f ? color : new Color(color.r, color.g, color.b, color.a * Mathf.Lerp(.3f, .85f, left));
        }
    }

    // The band's cells, each right over its health cell.
    private void BuildBand()
    {
        band = FeelUI.Rect("Shield Cells", root);
        band.anchorMin = band.anchorMax = band.pivot = new Vector2(0f, .5f);
        band.anchoredPosition = new Vector2(0f, cell.y * .5f + bandGap + bandHeight * .5f);
        band.sizeDelta = new Vector2(Width, bandHeight);
        bandCells = new Image[Count];
        for (int i = 0; i < Count; i++)
        {
            var rect = FeelUI.Rect("Cell", band);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, .5f);
            rect.anchoredPosition = new Vector2(i * (cell.x + Gap), 0f);
            rect.sizeDelta = new Vector2(cell.x, bandHeight);
            bandCells[i] = rect.gameObject.AddComponent<Image>();
            bandCells[i].raycastTarget = false;
        }
    }

    public void SetActive(bool on)
    {
        if (root.gameObject.activeSelf != on) root.gameObject.SetActive(on);
    }
}
