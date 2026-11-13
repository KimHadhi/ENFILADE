using TMPro;
using UnityEngine;
using UnityEngine.UI;

// FieldPickups: The [F] prompt (the HUD's kit, beside the crosshair) and what a pickup shows. One part of the class; the rest is in FieldPickups.cs and the files beside it.
public sealed partial class FieldPickups
{
    private void ShowPrompt()
    {
        bool shown = targeted >= 0;
        if (!shown && promptHost == null) return;
        Build();
        if (promptRoot.gameObject.activeSelf != shown) promptRoot.gameObject.SetActive(shown);
        if (!shown) return;
        var row = rows[items[targeted].Row];
        var tint = ColorOf(row.Kind);
        bool flash = Time.unscaledTime - pressedAt < KeyFlash;
        keyBox.color = refusal != null ? KitStyle.With(KitStyle.Ice, .45f) : flash ? KitStyle.Accent : KitStyle.Ice;
        // The interact key as the player set it in the key window (F by default).
        SetText(keyLabel, KeyBindings.Name("Player/Interact"));
        SetText(nameLabel, row.Name);
        SetText(amountLabel, refusal ?? AmountOf(row));
        // A gun shows its own picture beside the plate (WeaponIcons), so the one under the crosshair reads at a glance.
        var picture = row.Kind == Kind.Gun ? WeaponIcons.For(row.Weapon) : null;
        if (gunPicture.sprite != picture) gunPicture.sprite = picture;
        if (gunPicture.gameObject.activeSelf != (picture != null)) gunPicture.gameObject.SetActive(picture != null);
        amountLabel.color = refusal != null ? KitStyle.Soft : tint;
        underline.color = refusal != null ? KitStyle.Faint : tint;
    }

    private static void SetText(TMP_Text label, string text)
    {
        if (label.text != text) label.text = text;
    }

    private static string AmountOf(Row row) => row.Kind switch
    {
        Kind.Gun => Texts.Get("pickup.gives.gun"),
        Kind.Ammo => Texts.Format("pickup.gives.ammo", Mathf.RoundToInt(row.Amount)),
        Kind.Heal => Texts.Format("pickup.gives.heal", Mathf.RoundToInt(row.Amount)),
        Kind.Shield => Texts.Format("pickup.gives.shield", Mathf.RoundToInt(row.Amount)),
        _ => "+" + Wallet.Format((long)row.Amount),
    };

    // What was taken, on the taker's own PC (the user's pick of the pickup drafts, 2026-11-10, draft C, after "줍는 소리,
    // 화면 효과, 줍는 동작, 주울 때 뜨는 글자 다 부족"): a row in the list at the bottom of the screen, its kind's own
    // sound, a blink of the HUD part it went to, and the gun in hand dipping as the body goes for it. Money has no part
    // to blink: its number counts up by itself.
    private static void Pop(Kind kind, string name, string amount, WeaponData gun = null)
    {
        Color tint = ColorOf(kind);
        PickupFeedHUD.Show(IconOf(kind), tint, name, amount, kind == Kind.Gun ? WeaponIcons.For(gun) : null);
        KitSound.Play(kind switch
        {
            Kind.Gun => KitSound.Cue.PickGun,
            Kind.Ammo => KitSound.Cue.PickAmmo,
            Kind.Heal => KitSound.Cue.PickHeal,
            Kind.Shield => KitSound.Cue.PickShield,
            _ => KitSound.Cue.PickMoney
        }, .8f);
        if (kind == Kind.Gun || kind == Kind.Ammo) AmmoHUD.Blink(KitStyle.Ice);
        else if (kind == Kind.Heal || kind == Kind.Shield) AdaptiveHealthHUD.Blink(KitStyle.Ice, kind == Kind.Shield);
        var player = PlayerRoster.Local;
        var weapons = player != null ? player.GetComponentInChildren<WeaponController>() : null;
        if (weapons != null) FPSBody.Holding(weapons).OrNull()?.Reach();
    }

    // The list's icon for a kind; a gun has none and shows a gun's outline.
    private static Texture IconOf(Kind kind) => kind switch
    {
        Kind.Ammo => KitButtons.Icon("Bullet"),
        Kind.Heal => KitButtons.Icon("Heart"),
        Kind.Shield => KitButtons.Icon("Armor"),
        Kind.Money => MoneyIcon.Texture,
        _ => null
    };

    private void Build()
    {
        if (promptHost != null) return;
        promptHost = new GameObject("Pickup Prompt", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        promptHost.transform.SetParent(transform, false);
        var canvas = promptHost.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 85;
        var scaler = promptHost.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;

        // The [F] prompt: a key square, then a dark plate with the name and what it gives over a line of its colour,
        // leaning at its own angle just below and right of the crosshair.
        promptRoot = FeelUI.Rect("Prompt", promptHost.transform);
        promptRoot.anchoredPosition = new Vector2(70f, -50f);
        promptRoot.localRotation = Quaternion.Euler(0f, 0f, 5f);
        keyBox = Panel("Key", promptRoot, new Vector2(0f, 0f), new Vector2(52f, 52f), KitStyle.Ice);
        keyLabel = FeelUI.ScoreLabel("F", keyBox.rectTransform, 34f, TextAlignmentOptions.Center);
        keyLabel.rectTransform.anchorMin = Vector2.zero; keyLabel.rectTransform.anchorMax = Vector2.one;
        keyLabel.rectTransform.offsetMin = keyLabel.rectTransform.offsetMax = Vector2.zero;
        keyLabel.text = "F";
        keyLabel.color = KitStyle.Ink;
        // A longer key name ("SPACE", "좌클릭") shrinks to the square instead of spilling out of it.
        keyLabel.enableAutoSizing = true;
        keyLabel.fontSizeMin = 12f;
        keyLabel.fontSizeMax = 34f;
        var plate = Panel("Plate", promptRoot, new Vector2(58f, -4f), new Vector2(270f, 44f), KitStyle.With(KitStyle.Night, .82f));
        underline = Panel("Line", promptRoot, new Vector2(58f, -44f), new Vector2(270f, 4f), KitStyle.Ice);
        nameLabel = FeelUI.ScoreLabel("Name", plate.rectTransform, 24f, TextAlignmentOptions.Left);
        Fill(nameLabel.rectTransform, new Vector2(14f, 0f), new Vector2(-110f, 0f));
        amountLabel = FeelUI.ScoreLabel("Gives", plate.rectTransform, 20f, TextAlignmentOptions.Right);
        Fill(amountLabel.rectTransform, new Vector2(120f, 0f), new Vector2(-12f, 0f));
        // Right of the plate, on its middle (the plate's top is 4 over the middle, its height 44), leaning with the prompt:
        // a gun's picture (its canvas's shape, WeaponIcons).
        float tall = WeaponIcons.Number("Prompt", "height", 30f), gap = WeaponIcons.Number("Prompt", "gap", 10f);
        gunPicture = Panel("Gun", promptRoot, new Vector2(58f + 270f + gap, 18f - tall * .5f),
            new Vector2(tall * WeaponIcons.Aspect, tall), Color.white);
        gunPicture.preserveAspect = true;
        gunPicture.gameObject.SetActive(false);
        promptRoot.gameObject.SetActive(false);
    }

    // A box with its top-left corner at `at` (y down from the parent's middle).
    private static Image Panel(string name, RectTransform parent, Vector2 at, Vector2 size, Color color)
    {
        var image = new GameObject(name, typeof(RectTransform), typeof(Image)).GetComponent<Image>();
        var rect = image.rectTransform;
        rect.SetParent(parent, false);
        rect.anchorMin = rect.anchorMax = new Vector2(.5f, .5f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(at.x, -at.y);
        rect.sizeDelta = size;
        image.color = color;
        image.raycastTarget = false;
        return image;
    }

    private static void Fill(RectTransform rect, Vector2 min, Vector2 max)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = min; rect.offsetMax = max;
    }
}
