using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Teammates' health just above the player's own at the bottom left, on the HUD's left plane: a card each, stacked
/// upward, with a hexagon in their seat colour, the name and number over ten health cells. Each card leans back a little
/// further than the one below. Only up to Hud.csv's Team.limit teammates: with more, the panel goes away and only the
/// marks over their heads remain (TeammateMarkerHUD), the way big rooms elsewhere do it. Someone down shows the time
/// they have left in red; someone dead stays listed, greyed.
/// </summary>
[DisallowMultipleComponent]
public sealed class TeamHealthPanel : MonoBehaviour
{
    private const float CardHeight = 32f, Gap = 5f, BadgeWidth = 28f, TextLeft = 40f, Pad = 6f, LineHeight = 17f;
    private static readonly Vector2 Cell = new Vector2(9f, 6f);
    private static readonly Color Gone = new Color(.6f, .62f, .66f), Plate = new Color(.04f, .04f, .06f, .45f),
        Ink = new Color(.06f, .07f, .09f);

    [Tooltip("Most teammates shown. With more, the panel hides and only the marks over their heads remain.")]
    [SerializeField] private int limit = 3;
    [Tooltip("Left edge of the cards from the screen's left (px at 1080p).")]
    [SerializeField] private float left = 74f;
    [Tooltip("Bottom of the lowest card from the screen's bottom (px at 1080p), just above the player's own health.")]
    [SerializeField] private float bottom = 292f;
    [Tooltip("How much further back the lowest card leans than the plane it sits on (°). Positive sends the right side away.")]
    [SerializeField] private float yaw = 8f;
    [Tooltip("Extra lean for each card higher up the stack (°).")]
    [SerializeField] private float fan = 2.5f;
    [Tooltip("Turn within the screen (°). Positive raises the right side.")]
    [SerializeField] private float roll = 1.5f;

    private sealed class Card
    {
        public RectTransform root;
        public Image badge;
        public RawImage picture;
        public TextMeshProUGUI initial, name, value;
        public HealthSegments cells;
    }

    private readonly List<Card> cards = new List<Card>();
    private PlayerMovement player;
    private GameObject host;
    private RectTransform plane;

    private void Awake()
    {
        player = GetComponent<PlayerMovement>();
        GameTables.Bind(HudCamera.TableName, "Team", this);
    }

    private void Start()
    {
        host = new GameObject("Team Health", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        FeelUI.SetupHud(host, 25);
        plane = HudPlane.Create("Left Plane", host.transform, HudPlane.Side.Left);
    }

    private Card Build()
    {
        var card = new Card { root = FeelUI.Rect("Card", plane) };
        card.root.anchorMin = card.root.anchorMax = Vector2.zero;
        card.root.pivot = new Vector2(0f, .5f);
        card.cells = new HealthSegments(card.root, Cell);
        card.root.sizeDelta = new Vector2(TextLeft + card.cells.Width + Pad, CardHeight);

        var plate = FeelUI.Rect("Plate", card.root);
        plate.anchorMin = Vector2.zero; plate.anchorMax = Vector2.one;
        plate.offsetMin = new Vector2(BadgeWidth + Pad, 0f); plate.offsetMax = Vector2.zero;
        var plateImage = plate.gameObject.AddComponent<Image>();
        plateImage.color = Plate; plateImage.raycastTarget = false;
        plate.SetAsFirstSibling();

        var badge = FeelUI.Rect("Badge", card.root);
        badge.anchorMin = badge.anchorMax = badge.pivot = new Vector2(0f, .5f);
        badge.sizeDelta = new Vector2(BadgeWidth, CardHeight);
        card.badge = badge.gameObject.AddComponent<Image>();
        card.badge.sprite = CombatUIAssets.Hexagon; card.badge.raycastTarget = false;
        card.initial = FeelUI.ScoreLabel("Initial", badge, 15f, TextAlignmentOptions.Center);
        card.initial.rectTransform.anchorMin = Vector2.zero; card.initial.rectTransform.anchorMax = Vector2.one;
        card.initial.rectTransform.offsetMin = card.initial.rectTransform.offsetMax = Vector2.zero;
        card.initial.color = Ink;
        // Their own profile picture over the badge, once their PC has sent it (PlayerNetworkPose.Portrait, C14).
        var picture = FeelUI.Rect("Picture", badge);
        picture.anchorMin = picture.anchorMax = picture.pivot = new Vector2(.5f, .5f);
        picture.sizeDelta = new Vector2(BadgeWidth, BadgeWidth);
        card.picture = picture.gameObject.AddComponent<RawImage>();
        card.picture.raycastTarget = false;
        card.picture.enabled = false;

        var cells = card.cells.Rect;
        cells.anchorMin = cells.anchorMax = cells.pivot = Vector2.zero;
        cells.anchoredPosition = new Vector2(TextLeft, 5f);
        float line = 5f + Cell.y + 1f;
        card.name = FeelUI.ScoreLabel("Name", card.root, 14f, TextAlignmentOptions.BottomLeft);
        Place(card.name.rectTransform, Vector2.zero, new Vector2(TextLeft, line), card.cells.Width * .66f);
        // A long name stops short of the number.
        card.name.overflowMode = CombatUIAssets.ClippedOverflow(card.name);
        card.value = FeelUI.ScoreLabel("Value", card.root, 15f, TextAlignmentOptions.BottomRight);
        Place(card.value.rectTransform, new Vector2(1f, 0f), new Vector2(-Pad, line), card.cells.Width * .4f);
        card.root.gameObject.SetActive(false);
        return card;
    }

    private static void Place(RectTransform rect, Vector2 pivot, Vector2 position, float width)
    {
        rect.anchorMin = rect.anchorMax = pivot;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = new Vector2(width, LineHeight);
    }

    private void LateUpdate()
    {
        if (plane == null || player == null) return;
        int count = 0;
        foreach (var other in PlayerRoster.All)
            if (IsTeammate(other)) count++;
        int used = 0;
        if (count <= limit)
            foreach (var other in PlayerRoster.All)
            {
                if (!IsTeammate(other)) continue;
                if (used == cards.Count) cards.Add(Build());
                var card = cards[used];
                card.root.anchoredPosition = new Vector2(left, bottom + used * (CardHeight + Gap) + CardHeight * .5f);
                float tilt = GameSettings.Current.hudTilt; // Settings → HUD 기울기
                card.root.localRotation = Quaternion.Euler(0f, -(yaw + used * fan) * tilt, roll * tilt);
                Show(card, other);
                used++;
            }
        for (int i = used; i < cards.Count; i++)
            if (cards[i].root.gameObject.activeSelf) cards[i].root.gameObject.SetActive(false);
    }

    private bool IsTeammate(PlayerMovement other) => other != player && other.IsInitialized && other.DamageReceiver != null;

    private static void Show(Card card, PlayerMovement other)
    {
        if (!card.root.gameObject.activeSelf) card.root.gameObject.SetActive(true);
        var health = other.DamageReceiver;
        bool named = other.TryGetComponent(out PlayerTally tally);
        string displayName = named ? tally.DisplayName : other.name;
        card.name.text = displayName;
        card.initial.text = string.IsNullOrEmpty(displayName) ? "?" : displayName.Substring(0, 1).ToUpperInvariant();
        var portrait = other.TryGetComponent(out PlayerNetworkPose pose) ? pose.Portrait : null;
        card.picture.texture = portrait;
        card.picture.enabled = portrait != null;
        card.picture.color = health.IsAlive ? Color.white : Gone;
        card.initial.enabled = portrait == null;
        Color seat = named ? tally.Color : Color.white;
        if (!health.IsAlive)
        {
            card.name.color = card.value.color = card.badge.color = Gone;
            card.value.text = "사망";
            card.cells.Show(0f, Gone);
            return;
        }
        card.name.color = card.badge.color = seat;
        if (other.IsDowned)
        {
            float remaining = other.TryGetComponent(out DownedAura aura) ? 1f - aura.Urgency : 0f;
            card.value.text = "쓰러짐";
            card.value.color = HealthSegments.Downed;
            card.cells.Show(remaining, HealthSegments.Downed);
            return;
        }
        float share = Mathf.Clamp01(health.Health / health.MaximumHealth);
        Color color = HealthSegments.ColorFor(share);
        card.value.text = Mathf.CeilToInt(health.Health).ToString();
        card.value.color = color;
        card.cells.Show(share, color);
    }

    private void OnDestroy() { if (host != null) Destroy(host); }
}
