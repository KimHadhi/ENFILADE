using TMPro;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// The class's own resource, at the foot of the screen (stage 7, ClassKit): a dark slanted plate with the class's name,
/// and for an Assault its adrenaline as a row of leaning pips that fill pink, "[휠] CHARGE" beating on it once they are
/// full and a draining bar while a CHARGE lasts (a friend's too); for a Point its two guns' numbers, each with a line
/// that runs out while the gun put away reloads itself and a flash when it has; for a Ghost its Head Hunter stacks as
/// the pips, the newest flashing white as it comes and all of them as they are lost, and "DEADEYE" over the plate while
/// the gun in hand holds its first round since loading; for a Knight "[휠] 도발" while its taunt is ready, the wait in
/// its place while not, and the draining bar while it lasts. Classes whose abilities come later show their name only.
/// Local player only (PlayerMovement). HUD look: black, white and pink.
/// </summary>
[DisallowMultipleComponent]
public sealed class ClassHUD : MonoBehaviour
{
    private const int MostPips = 12;
    private const float Width = 300f, Height = 44f, Above = 112f, Slant = 12f, PipWidth = 14f, PipHeight = 14f, PipGap = 5f;

    private PlayerMovement movement;
    private ClassKit kit;
    private RectTransform plate, pipRow, chargeBar, slotRow;
    private Image chargeFill;
    private readonly Image[] pips = new Image[MostPips];
    private readonly Image[] slotLines = new Image[2], slotBacks = new Image[2];
    private TextMeshProUGUI title, note;
    private RawImage icon;
    private RectTransform keyRect;
    private KeyCapGraphic keyCap;
    private TextMeshProUGUI keyLabel, keyWord, barWord, deadeye;
    private InputAction ability;
    private string shownClass;
    private int shownPips;

    private void OnEnable()
    {
        movement = GetComponent<PlayerMovement>();
        kit = GetComponent<ClassKit>();
    }

    private void LateUpdate()
    {
        if (movement == null || !movement.IsLocal) return;
        if (kit == null) kit = GetComponent<ClassKit>();
        Build();
        string id = PlayerClass.CurrentId;
        if (id != shownClass) Show(id);
        bool dead = movement.IsDead;
        plate.gameObject.SetActive(!dead);
        if (dead || kit == null) return;
        if (PlayerClass.Is(PlayerClass.Assault)) Adrenaline();
        else if (PlayerClass.Is(PlayerClass.Point)) Guns();
        else if (PlayerClass.Is(PlayerClass.Ghost)) Hunter();
        else if (PlayerClass.Is(PlayerClass.Knight)) Taunt();
        // The bar along the foot: a Knight's own taunt, or a friend's CHARGE, which reaches any class.
        float taunt = kit.TauntShare, share = taunt > 0f ? taunt : kit.ChargeShare;
        chargeBar.gameObject.SetActive(share > 0f);
        if (share <= 0f) return;
        chargeFill.rectTransform.anchorMax = new Vector2(share, 1f);
        string word = Texts.Get(taunt > 0f ? "class.hud.taunt" : "class.hud.charge");
        if (barWord.text != word) barWord.text = word;
    }

    private void Show(string id)
    {
        shownClass = id;
        var info = PlayerClass.Find(id);
        title.text = info != null ? info.Title : id;
        icon.texture = KitButtons.Icon(MainMenuClasses.Icon(id));
        icon.enabled = icon.texture != null;
        bool assault = PlayerClass.Is(PlayerClass.Assault), point = PlayerClass.Is(PlayerClass.Point);
        bool ghost = PlayerClass.Is(PlayerClass.Ghost), knight = PlayerClass.Is(PlayerClass.Knight);
        pipRow.gameObject.SetActive(assault || ghost);
        slotRow.gameObject.SetActive(point);
        keyRect.gameObject.SetActive(false);
        deadeye.gameObject.SetActive(false);
        keyWord.text = Texts.Get(knight ? "class.hud.taunt" : "class.hud.charge");
        note.text = info != null && !info.Ready ? Texts.Get("class.hud.later") : "";
        shownPips = 0;
        if (assault) Pips(kit != null ? kit.StackMax : 8);
    }

    // As many pips as the resource holds (an Assault's adrenaline; a Ghost's Head Hunter, whose most the cards may raise).
    private void Pips(int most)
    {
        int count = Mathf.Clamp(most, 1, MostPips);
        if (count == shownPips) return;
        shownPips = count;
        for (int i = 0; i < MostPips; i++) pips[i].gameObject.SetActive(i < count);
    }

    // Adrenaline: a pip a stack, all beating with the key's prompt once full.
    private void Adrenaline()
    {
        int count = Mathf.Clamp(kit.StackMax, 1, MostPips), stacks = kit.Stacks;
        bool ready = kit.ChargeReady;
        float beat = ready ? .75f + .25f * Mathf.Sin(Time.unscaledTime * 9f) : 1f;
        for (int i = 0; i < count; i++)
            pips[i].color = i < stacks ? KitStyle.With(KitStyle.Accent, beat) : KitStyle.With(KitStyle.Ice, .14f);
        KeyPrompt(ready);
    }

    // A Ghost's Head Hunter: a pip a stack. The one that came flashes white, the one lost too, and all of them when a
    // hurt takes every stack. "DEADEYE" over the plate while the gun in hand holds its first round since loading.
    private void Hunter()
    {
        Pips(kit.HunterMax);
        int count = shownPips, stacks = Mathf.Min(kit.Hunter, count), change = kit.HunterChange;
        float flash = Mathf.Clamp01(1f - kit.SinceHunterChange / .3f);
        for (int i = 0; i < count; i++)
        {
            Color color = i < stacks ? KitStyle.Accent : KitStyle.With(KitStyle.Ice, .14f);
            bool changed = change == -2 || (change == 1 && i == stacks - 1) || (change == -1 && i == stacks);
            pips[i].color = flash > 0f && changed ? Color.Lerp(color, KitStyle.Ice, flash) : color;
        }
        bool ready = kit.DeadeyeReady;
        deadeye.gameObject.SetActive(ready);
        if (ready) deadeye.color = KitStyle.With(KitStyle.Accent, .78f + .22f * Mathf.Sin(Time.unscaledTime * 6f));
    }

    // A Knight's taunt: the key's prompt while it is ready, the seconds left to wait in its place while it isn't.
    private void Taunt()
    {
        bool ready = kit.TauntReady;
        KeyPrompt(ready);
        string wait = ready ? "" : Texts.Format("class.hud.taunt_wait", Mathf.CeilToInt(kit.TauntWait));
        if (note.text != wait) note.text = wait;
    }

    // "[휠] CHARGE" or "[휠] 도발" over the plate's right end, beating, while the class key has something to do.
    private void KeyPrompt(bool ready)
    {
        keyRect.gameObject.SetActive(ready);
        if (!ready) return;
        string key = KeyName();
        if (keyLabel.text != key)
        {
            keyLabel.text = key;
            float width = Mathf.Max(30f, keyLabel.GetPreferredValues(key).x + 16f);
            keyRect.sizeDelta = new Vector2(width, 24f);
            keyCap.SetVerticesDirty();
        }
        keyRect.localScale = Vector3.one * (1f + .06f * Mathf.Sin(Time.unscaledTime * 9f));
    }

    // A Point's two guns: the line under each runs out while it reloads put away, and flashes when it has.
    private void Guns()
    {
        for (int slot = 0; slot < 2; slot++)
        {
            float share = kit.AsideShare(slot), since = kit.SinceRefill(slot);
            slotLines[slot].rectTransform.anchorMax = new Vector2(share, 1f);
            slotLines[slot].color = KitStyle.Accent;
            float flash = Mathf.Clamp01(1f - since / .5f);
            slotBacks[slot].color = Color.Lerp(KitStyle.With(KitStyle.Ice, .12f), KitStyle.With(KitStyle.Accent, .85f), flash);
        }
    }

    private string KeyName()
    {
        if (ability == null && InputSystem.actions != null) ability = InputSystem.actions.FindAction("Player/ClassAbility", false);
        return KeyBindings.Name(ability);
    }

    private void Build()
    {
        if (plate != null) return;
        var host = new GameObject("Class HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        host.hideFlags = HideFlags.DontSave;
        host.transform.SetParent(transform, false);
        var canvas = host.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 76;
        var scaler = host.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;

        plate = new GameObject("Class", typeof(RectTransform)).GetComponent<RectTransform>();
        plate.SetParent(host.transform, false);
        plate.anchorMin = plate.anchorMax = plate.pivot = new Vector2(.5f, 0f);
        plate.anchoredPosition = new Vector2(0f, Above);
        plate.sizeDelta = new Vector2(Width, Height);
        plate.localRotation = Quaternion.Euler(0f, 0f, 1.5f);
        var back = KitStyle.Fill(KitStyle.Stretch("Plate", plate), KitStyle.With(KitStyle.Ink, .78f));
        KitSkew.On(back, Slant);
        var edge = KitStyle.Bar("Edge", plate, Vector2.zero, new Vector2(4f, Height), KitStyle.Accent);
        KitSkew.On(edge, Slant);

        icon = KitButtons.IconImage(plate, "", 28f, KitStyle.Ice);
        icon.rectTransform.anchorMin = icon.rectTransform.anchorMax = new Vector2(0f, .5f);
        icon.rectTransform.anchoredPosition = new Vector2(28f, 0f);
        title = KitStyle.Label(KitStyle.TopLeft("Title", plate, new Vector2(46f, 0f), new Vector2(80f, Height)), "", 16f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Ice);
        title.characterSpacing = 2f;
        note = KitStyle.Label(KitStyle.TopLeft("Note", plate, new Vector2(128f, 0f), new Vector2(160f, Height)), "", 13f,
            TextAlignmentOptions.MidlineLeft, KitStyle.Soft);
        note.fontStyle = FontStyles.Normal;

        pipRow = KitStyle.TopLeft("Adrenaline", plate, new Vector2(128f, (Height - PipHeight) * .5f), new Vector2(160f, PipHeight));
        for (int i = 0; i < MostPips; i++)
        {
            var pip = KitStyle.Fill(KitStyle.TopLeft("Pip", pipRow, new Vector2(i * (PipWidth + PipGap), 0f), new Vector2(PipWidth, PipHeight)),
                KitStyle.With(KitStyle.Ice, .14f));
            KitSkew.On(pip, 5f);
            pips[i] = pip;
        }

        slotRow = KitStyle.TopLeft("Guns", plate, new Vector2(128f, 7f), new Vector2(160f, Height - 14f));
        for (int slot = 0; slot < 2; slot++)
        {
            var box = KitStyle.TopLeft("Gun " + (slot + 1), slotRow, new Vector2(slot * 62f, 0f), new Vector2(52f, Height - 14f));
            slotBacks[slot] = KitStyle.Fill(box, KitStyle.With(KitStyle.Ice, .12f));
            KitSkew.On(slotBacks[slot], 6f);
            KitStyle.Label(box, (slot + 1).ToString(), 15f, TextAlignmentOptions.Center, KitStyle.Ice);
            var line = KitStyle.Stretch("Reload", box);
            line.anchorMin = Vector2.zero;
            line.anchorMax = new Vector2(0f, 0f);
            line.pivot = new Vector2(0f, 0f);
            line.sizeDelta = new Vector2(0f, 3f);
            line.anchoredPosition = Vector2.zero;
            slotLines[slot] = line.gameObject.AddComponent<Image>();
            slotLines[slot].raycastTarget = false;
        }

        // "[휠] CHARGE" over the right end once the adrenaline is full.
        keyRect = new GameObject("Charge key", typeof(RectTransform), typeof(CanvasRenderer), typeof(KeyCapGraphic)).GetComponent<RectTransform>();
        keyRect.SetParent(plate, false);
        keyRect.anchorMin = keyRect.anchorMax = keyRect.pivot = new Vector2(1f, .5f);
        keyRect.anchoredPosition = new Vector2(-84f, Height * .5f + 16f);
        keyCap = keyRect.GetComponent<KeyCapGraphic>();
        keyCap.raycastTarget = false;
        keyCap.color = KitStyle.Accent;
        keyLabel = KitStyle.Label(keyRect, "", 13f, TextAlignmentOptions.Center, KitStyle.Ice);
        var word = KitStyle.TopLeft("Charge word", keyRect, new Vector2(0f, 0f), new Vector2(80f, 24f));
        word.anchorMin = word.anchorMax = new Vector2(1f, .5f);
        word.pivot = new Vector2(0f, .5f);
        word.anchoredPosition = new Vector2(8f, 0f);
        keyWord = KitStyle.Label(word, Texts.Get("class.hud.charge"), 15f, TextAlignmentOptions.MidlineLeft, KitStyle.Accent);
        keyRect.gameObject.SetActive(false);

        // A Ghost's "DEADEYE", over the plate's right end where a key's prompt would be.
        var mark = KitStyle.TopLeft("Deadeye", plate, Vector2.zero, new Vector2(120f, 24f));
        mark.anchorMin = mark.anchorMax = mark.pivot = new Vector2(1f, .5f);
        mark.anchoredPosition = new Vector2(-14f, Height * .5f + 16f);
        deadeye = KitStyle.Label(mark, Texts.Get("class.hud.deadeye"), 15f, TextAlignmentOptions.MidlineRight, KitStyle.Accent);
        deadeye.characterSpacing = 3f;
        deadeye.gameObject.SetActive(false);

        // The CHARGE's time, draining along the plate's foot.
        chargeBar = KitStyle.TopLeft("Charge", plate, new Vector2(0f, Height + 4f), new Vector2(Width, 6f));
        KitSkew.On(KitStyle.Fill(chargeBar, KitStyle.With(KitStyle.Ice, .12f)), 3f);
        var fill = KitStyle.Stretch("Fill", chargeBar);
        chargeFill = fill.gameObject.AddComponent<Image>();
        chargeFill.color = KitStyle.Accent;
        chargeFill.raycastTarget = false;
        fill.anchorMin = Vector2.zero;
        fill.anchorMax = Vector2.one;
        barWord = KitStyle.Label(KitStyle.TopLeft("Charge text", chargeBar, new Vector2(Width - 90f, -2f), new Vector2(90f, 18f)),
            Texts.Get("class.hud.charge"), 12f, TextAlignmentOptions.MidlineRight, KitStyle.Accent);
        chargeBar.gameObject.SetActive(false);
    }

    private void OnDestroy()
    {
        if (plate == null) return;
        var host = plate.parent != null ? plate.parent.gameObject : plate.gameObject;
        if (Application.isPlaying) Destroy(host); else DestroyImmediate(host);
    }
}
