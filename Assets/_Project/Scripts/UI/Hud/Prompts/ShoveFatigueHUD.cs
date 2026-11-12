using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shove fatigue under the crosshair, as Left 4 Dead shows it: an open palm that warms in amber one shove before the
/// shoves start to slow, glows from the start-th on and runs to red at the most, with a ring that drains through the
/// wait. While the next shove can't come yet, a slash crosses the palm. Hidden while fresh.
/// </summary>
[DisallowMultipleComponent]
public sealed class ShoveFatigueHUD : MonoBehaviour
{
    // Below the crosshair, 1080p px.
    private const float Below = 78f, HandSize = 26f, RingSize = 44f;
    private static readonly Color Amber = new Color(1f, .72f, .3f), Ember = new Color(1f, .32f, .22f);

    private PlayerMovement player;
    private GameObject host;
    private CanvasGroup group;
    private RawImage hand;
    private Image ring, slash;

    private void Awake() => player = GetComponent<PlayerMovement>();

    private void Start()
    {
        host = new GameObject("Shove Fatigue", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        FeelUI.SetupOverlay(host, 26);
        group = host.GetComponent<CanvasGroup>();
        group.alpha = 0f;
        group.interactable = group.blocksRaycasts = false;
        var root = FeelUI.Rect("Icon", host.transform);
        root.anchoredPosition = new Vector2(0f, -Below);
        ring = FeelUI.Rect("Ring", root).gameObject.AddComponent<Image>();
        ring.sprite = CombatUIAssets.Ring;
        ring.type = Image.Type.Filled;
        ring.fillMethod = Image.FillMethod.Radial360;
        ring.fillOrigin = (int)Image.Origin360.Top;
        ring.raycastTarget = false;
        ring.rectTransform.sizeDelta = new Vector2(RingSize, RingSize);
        hand = FeelUI.Rect("Hand", root).gameObject.AddComponent<RawImage>();
        hand.texture = CombatUIAssets.ShoveHand;
        hand.raycastTarget = false;
        hand.rectTransform.sizeDelta = new Vector2(HandSize, HandSize);
        slash = FeelUI.Rect("Slash", root).gameObject.AddComponent<Image>();
        slash.raycastTarget = false;
        slash.rectTransform.sizeDelta = new Vector2(3.5f, RingSize * .82f);
        slash.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);
    }

    private void LateUpdate()
    {
        if (group == null || player == null || !player.IsInitialized) return;
        var settings = player.AbilitySettings;
        var fatigue = player.Abilities.Combat.Fatigue;
        int count = fatigue.Count, start = Mathf.Max(1, settings.shoveFatigueStart), most = Mathf.Max(start, settings.shoveFatigueMost);
        bool tired = count >= start, blocked = tired && fatigue.Rest > 0f;
        float shown = !player.DamageReceiver.IsAlive ? 0f : tired ? 1f : count == start - 1 ? .45f : 0f;
        group.alpha = Mathf.MoveTowards(group.alpha, shown, Time.unscaledDeltaTime * 6f);
        if (group.alpha <= 0f) return;
        Color tint = Color.Lerp(Amber, Ember, Mathf.InverseLerp(start, most, count));
        hand.color = blocked ? new Color(tint.r, tint.g, tint.b, .55f) : tint;
        ring.color = new Color(tint.r, tint.g, tint.b, blocked ? .9f : .3f);
        ring.fillAmount = blocked ? fatigue.RestShare : 1f;
        slash.enabled = blocked;
        slash.color = tint;
    }

    private void OnDestroy() { if (host != null) Destroy(host); }
}
