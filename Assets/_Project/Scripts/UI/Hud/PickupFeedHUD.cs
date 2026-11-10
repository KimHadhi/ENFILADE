using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// What this player just picked up, listed at the bottom middle of the screen as Left 4 Dead lists it: the user's pick
/// of the pickup drafts (2026-11-10, draft C "아래 목록"; the old "+ name amount" beside the crosshair said too little
/// and sat on the fight). One dark slanted plate a thing: its icon on a square of its kind's colour, its name, and what
/// it gave at the right end in the HUD's pink. The newest is the bottom one and the older step up; each is gone after a
/// couple of seconds. The list turns with a lean as the rest of the HUD does. FieldPickups feeds it. Built on first
/// use, on real time.
/// </summary>
[DisallowMultipleComponent]
public sealed class PickupFeedHUD : MonoBehaviour
{
    private const int Most = 4;
    // A row (px at 1080p): how far over the screen's bottom the newest stands, its size and lean, the gap to the next,
    // its icon's square and the type's sizes.
    private const float Bottom = 214f, Width = 430f, Height = 50f, Slant = 10f, Gap = 8f, Badge = 36f, NameSize = 25f, AmountSize = 22f;
    // In, all of it, out (real s).
    private const float InSeconds = .16f, Life = 2.4f, OutSeconds = .3f;
    private static readonly Color Plate = new Color(.024f, .024f, .031f, .88f);
    private static PickupFeedHUD instance;

    private sealed class Row
    {
        public RectTransform root;
        public CanvasGroup group;
        public Image badge;
        public RawImage icon;
        public WeaponSlotSilhouette gun;
        public TextMeshProUGUI name, amount;
        public float age, y;
    }

    private readonly List<Row> shown = new List<Row>();
    private readonly Stack<Row> spare = new Stack<Row>();
    private RectTransform list;

    /// <summary>
    /// One more thing taken: its kind's icon (null for a gun, which shows a gun's outline) and colour, its name and what
    /// it gave.
    /// </summary>
    public static void Show(Texture icon, Color tint, string name, string amount)
    {
        if (!Application.isPlaying) return;
        if (instance == null)
        {
            var host = new GameObject("Pickup Feed", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            instance = host.AddComponent<PickupFeedHUD>();
        }
        instance.Add(icon, tint, name, amount);
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        FeelUI.SetupOverlay(gameObject, 27);
        list = FeelUI.Rect("List", transform);
        list.anchorMin = list.anchorMax = new Vector2(.5f, 0f);
        list.anchoredPosition = new Vector2(0f, Bottom);
    }

    private void OnDestroy() { if (instance == this) instance = null; }

    private void Add(Texture icon, Color tint, string name, string amount)
    {
        if (shown.Count >= Most) Retire(0);
        var row = spare.Count > 0 ? spare.Pop() : Build();
        shown.Add(row);
        row.age = 0f;
        row.y = 0f;
        row.badge.color = tint;
        row.icon.gameObject.SetActive(icon != null);
        row.gun.gameObject.SetActive(icon == null);
        row.icon.texture = icon;
        row.name.text = name;
        row.amount.text = amount;
        row.root.gameObject.SetActive(true);
        row.root.SetAsLastSibling();
        Layout(row, 0);
    }

    private void Retire(int index)
    {
        var row = shown[index];
        shown.RemoveAt(index);
        row.root.gameObject.SetActive(false);
        spare.Push(row);
    }

    private void LateUpdate()
    {
        if (shown.Count == 0) return;
        float dt = Time.unscaledDeltaTime;
        list.localRotation = Quaternion.Euler(0f, 0f, HudCamera.LeanRoll);
        for (int i = shown.Count - 1; i >= 0; i--)
        {
            shown[i].age += dt;
            if (shown[i].age >= Life) Retire(i);
        }
        // The newest is last in the list and lowest on the screen.
        for (int i = 0; i < shown.Count; i++)
        {
            int up = shown.Count - 1 - i;
            shown[i].y = Mathf.Lerp(shown[i].y, up * (Height + Gap), 1f - Mathf.Exp(-16f * dt));
            Layout(shown[i], up);
        }
    }

    private static void Layout(Row row, int up)
    {
        float arrive = 1f - Mathf.Pow(1f - Mathf.Clamp01(row.age / InSeconds), 3f), leave = Mathf.Clamp01((row.age - (Life - OutSeconds)) / OutSeconds);
        row.root.anchoredPosition = new Vector2(0f, row.y);
        row.root.localScale = Vector3.one * Mathf.Lerp(.9f, 1f, arrive);
        // Those above the newest stand back a little.
        row.group.alpha = arrive * (1f - leave) * (up == 0 ? 1f : .78f);
    }

    private Row Build()
    {
        var row = new Row { root = FeelUI.Rect("Row", list) };
        row.group = row.root.gameObject.AddComponent<CanvasGroup>();
        row.group.blocksRaycasts = row.group.interactable = false;
        var plate = FeelUI.Rect("Plate", row.root).gameObject.AddComponent<Image>();
        plate.raycastTarget = false;
        plate.color = Plate;
        plate.rectTransform.sizeDelta = new Vector2(Width, Height);
        KitSkew.On(plate, Slant);

        row.badge = FeelUI.Rect("Badge", row.root).gameObject.AddComponent<Image>();
        row.badge.raycastTarget = false;
        row.badge.rectTransform.sizeDelta = Vector2.one * Badge;
        row.badge.rectTransform.anchoredPosition = new Vector2(-Width * .5f + 14f + Badge * .5f, 0f);
        KitSkew.On(row.badge, Slant * Badge / Height);
        row.icon = FeelUI.Rect("Icon", row.badge.transform).gameObject.AddComponent<RawImage>();
        row.icon.raycastTarget = false;
        row.icon.color = KitStyle.Ink;
        row.icon.rectTransform.sizeDelta = Vector2.one * (Badge - 10f);
        row.gun = HudShapes.Make<WeaponSlotSilhouette>("Gun", row.badge.transform, KitStyle.Ink);
        row.gun.rectTransform.sizeDelta = Vector2.one * (Badge - 4f);
        row.gun.Kind = 1;

        float left = -Width * .5f + 14f + Badge + 14f, right = Width * .5f - 18f;
        row.name = Words("Name", row.root, NameSize, TextAlignmentOptions.MidlineLeft, KitStyle.Ice);
        row.name.rectTransform.pivot = new Vector2(0f, .5f);
        row.name.rectTransform.anchoredPosition = new Vector2(left, 0f);
        row.name.rectTransform.sizeDelta = new Vector2(right - left - 120f, Height);
        row.name.overflowMode = CombatUIAssets.ClippedOverflow(row.name);
        row.amount = Words("Gives", row.root, AmountSize, TextAlignmentOptions.MidlineRight, KitStyle.Accent);
        row.amount.rectTransform.pivot = new Vector2(1f, .5f);
        row.amount.rectTransform.anchoredPosition = new Vector2(right, 0f);
        row.amount.rectTransform.sizeDelta = new Vector2(190f, Height);
        row.root.gameObject.SetActive(false);
        return row;
    }

    // The list's type: the score font, flat on the plate.
    private static TextMeshProUGUI Words(string name, Transform parent, float size, TextAlignmentOptions alignment, Color color)
    {
        var text = FeelUI.ScoreLabel(name, parent, size, alignment);
        if (text.font != null && text.font.material != null) text.fontSharedMaterial = text.font.material;
        text.fontStyle = FontStyles.Bold;
        text.color = color;
        return text;
    }
}
