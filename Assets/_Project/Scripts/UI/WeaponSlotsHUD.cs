using System;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The weapon slots (user, 2026-11-12: "무기 슬롯 리디자인하고 …"; of the four drafts: "슬롯 디자인은 A랑 C 정말 마음에
/// 들었고"), in the look the player picks in Settings → HUD → 무기 슬롯 (GameSettings.weaponSlots):
/// <para>Plates (draft A, the default): a column of dark plates at the HUD's right, their top left and bottom right corners
/// cut, each leaning its own way and standing its own distance out (never one shared slant), a leaning number tab at its
/// left. The slot taken in hand kicks out to the left with a white flash and grows: its tab turns pink, a pink line runs
/// down its left side, its icon lifts and turns full white, its name and kind show under it, and the HUD strip's pink corner
/// and shard light at its top right.</para>
/// <para>Cards (draft C): a row of small cards over the ammo, each with its number and a small icon, an empty slot striped.
/// The one in hand widens into a light card with the gun drawn dark on it, its name and kind, and a pink line drawn along
/// its top.</para>
/// Each weapon shows its own picture (WeaponIcons), kept to the icons' one canvas so a pistol stays smaller than a rifle; a
/// weapon without one shows the vector outline (WeaponSlotSilhouette). A name too long for its room scrolls to its end and
/// back. Built here in code; the prefab's first slot views stay hidden. Numbers: Tables/WeaponSlots.csv.
/// </summary>
[DisallowMultipleComponent]
public sealed class WeaponSlotsHUD : MonoBehaviour
{
    [Serializable] public sealed class SlotView
    {
        public RectTransform rect;
        public Image background, accent, icon;
        public Outline outline;
        public WeaponSlotSilhouette silhouette;
        public TMP_Text number, weaponName;
    }

    public const string TableName = "WeaponSlots";
    private const int Count = PlayerWeaponSlots.SlotCount;
    private static readonly Color PlateDark = new Color(.055f, .055f, .067f, .8f), PlateHeld = new Color(.1f, .1f, .12f, .95f),
        CardDark = new Color(.055f, .055f, .067f, .82f), Stripes = new Color(1f, 1f, 1f, .45f);

    [SerializeField] private PlayerWeaponSlots source;
    [SerializeField] private CanvasGroup group;
    [Tooltip("The prefab's first slot views. They stay hidden: the slots are built in code in the look picked in Settings → HUD.")]
    [SerializeField] private SlotView[] slots = new SlotView[Count];
    [SerializeField, Min(0)] private float visibleSeconds = 3f;
    [SerializeField, Min(0)] private float fadeSeconds = 1f;
    [Header("Long weapon names")]
    [SerializeField, Min(1f)] private float nameScrollSpeed = 24f;
    [SerializeField, Min(0f)] private float nameScrollPause = 1f;

    private PlayerWeaponSlots bound;
    private float lastActivity = float.NegativeInfinity;
    private bool restartNames, made;
    private readonly Slot[] built = new Slot[Count];
    private RectTransform holder;
    private WeaponSlotLook look;
    private Shape shape;
    private int shownSelected = -2;
    private AmmoHUD ammo;
    private float ammoLookedAt = -99f;
    private readonly System.Collections.Generic.List<RectTransform> roomRects = new System.Collections.Generic.List<RectTransform>();
    private static Texture2D stripes;

    // One look's numbers (WeaponSlots.csv), read when the look is built.
    private sealed class Shape
    {
        public float Width, Height, WidthHeld, HeightHeld, Gap, Cut, Icon, IconHeld, IconAlpha, Kick, Grow, Flash, Bar, Right, Bottom;
        public readonly float[] Lean = new float[Count], Shift = new float[Count];
    }

    private sealed class Slot
    {
        public RectTransform root, nameView;
        public SlotShape plate, flash, tab, corner, shard;
        public Image icon, bar;
        public WeaponSlotSilhouette outline;
        public RawImage hatch;
        public TextMeshProUGUI number, empty, name, kind;
        public float on, speed, popAt = -99f, scrolled, nameWidth, kindWidth;
        public string shownName, shownKind;
    }

    private void OnEnable() => Bind();
    private void OnDisable() { if (bound != null) bound.SlotActivity -= Reveal; bound = null; HudRoom.Free(this); }

    private void Bind()
    {
        if (source == null && PlayerRoster.Local != null) source = PlayerRoster.Local.GetComponent<PlayerWeaponSlots>();
        if (bound == source) return;
        if (bound != null) bound.SlotActivity -= Reveal;
        bound = source;
        if (bound != null) bound.SlotActivity += Reveal;
    }

    public void Reveal() { lastActivity = Time.unscaledTime; restartNames = true; if (group != null) group.alpha = 1f; }
    private void LateUpdate() => Refresh(Time.unscaledDeltaTime);

    public void Refresh(float deltaTime)
    {
        Bind();
        if (group == null) return;
        group.blocksRaycasts = group.interactable = false;
        var wanted = GameSettings.Current.weaponSlots;
        if (!made || wanted != look) Build(wanted);
        float now = Time.unscaledTime, dt = Mathf.Max(0f, deltaTime);
        int selected = source != null ? source.SelectedSlot : -1;
        if (selected != shownSelected)
        {
            // The slot just taken in hand kicks and flashes; nothing does on the first look at the slots.
            if (shownSelected != -2 && selected >= 0 && selected < Count) built[selected].popAt = now;
            shownSelected = selected;
        }
        for (int i = 0; i < Count; i++) Spring(built[i], i == selected ? 1f : 0f, dt, shape.Grow);
        float readable = visibleSeconds;
        if (look == WeaponSlotLook.Plates) LayPlates(selected, now, dt, ref readable);
        else
        {
            PlaceOverAmmo(now);
            LayCards(selected, now, dt, ref readable);
        }
        restartNames = false;
        float age = now - lastActivity;
        group.alpha = age <= readable ? 1f : fadeSeconds <= 0f ? 0f : 1f - Mathf.Clamp01((age - readable) / fadeSeconds);
        TakeRoom();
    }

    // While the slots show, the room they take on the right edge, so a passing piece (a callout window) steps clear of
    // them rather than over them (HudRoom; the overlap check measured a callout window 107.5 px over the plates in play).
    private void TakeRoom()
    {
        roomRects.Clear();
        if (group.alpha > .02f)
            for (int i = 0; i < Count; i++)
                if (built[i] != null && built[i].root != null) roomRects.Add(built[i].root);
        if (roomRects.Count > 0 && HudRoom.Measure(roomRects, out float near, out float far, out float top, out float bottom))
            HudRoom.Take(this, near, far, top, bottom);
        else HudRoom.Free(this);
    }

    // ---- Plates (A) ----

    private void LayPlates(int selected, float now, float dt, ref float readable)
    {
        float total = shape.Gap * (Count - 1);
        for (int i = 0; i < Count; i++) total += Mathf.LerpUnclamped(shape.Height, shape.HeightHeld, built[i].on);
        float top = total * .5f;
        for (int i = 0; i < Count; i++)
        {
            var slot = built[i];
            float on = Mathf.Clamp01(slot.on), height = Mathf.LerpUnclamped(shape.Height, shape.HeightHeld, slot.on);
            slot.root.sizeDelta = new Vector2(shape.Width, height);
            slot.root.anchoredPosition = new Vector2(shape.Shift[i] + Kick(slot, now), top - height * .5f);
            top -= height + shape.Gap;
            var data = Weapon(i);
            slot.plate.color = Color.Lerp(PlateDark, PlateHeld, on);
            slot.plate.Edge(KitStyle.With(KitStyle.Accent, on), 3f);
            slot.flash.color = new Color(1f, 1f, 1f, Flash(slot, now));
            slot.tab.color = Color.Lerp(KitStyle.Ice, KitStyle.Accent, on);
            slot.number.color = Color.Lerp(KitStyle.Night, Color.white, on);
            slot.corner.color = KitStyle.With(KitStyle.Accent, on);
            slot.shard.color = KitStyle.With(KitStyle.Accent, on);
            // The icon sits at the plate's middle and lifts a tenth of it to make room for the name under it.
            float iconHeight = shape.Icon / WeaponIcons.Aspect;
            var iconRect = slot.icon.rectTransform;
            iconRect.sizeDelta = new Vector2(shape.Icon, iconHeight);
            iconRect.anchoredPosition = new Vector2(-16f, height * .1f * on);
            ShowWeapon(slot, data, KitStyle.With(KitStyle.Ice, Mathf.Lerp(shape.IconAlpha, 1f, on)));
            slot.empty.gameObject.SetActive(data == null);
            slot.empty.color = KitStyle.Soft;
            // Name and kind under the icon, from the tab's right to the plate's right end.
            Meta(slot, data, i, 26f, 9f, shape.Width - 26f - 16f, KitStyle.Ice, on, dt, i == selected, ref readable);
        }
    }

    private Slot MakePlate(int index)
    {
        var slot = new Slot { root = FeelUI.Rect("Slot " + (index + 1), holder) };
        var root = slot.root;
        root.anchorMin = root.anchorMax = Vector2.zero;
        root.pivot = new Vector2(1f, .5f);
        // Leans are the drafts' CSS degrees, clockwise.
        root.localRotation = Quaternion.Euler(0f, 0f, -shape.Lean[index]);
        slot.plate = MakeShape(root, "Plate", SlotShape.Kind.Plate, shape.Cut);
        Stretch(slot.plate.rectTransform);
        slot.icon = MakePicture(root, new Vector2(1f, .5f));
        slot.outline = MakeOutline(root, new Vector2(1f, .5f));
        slot.empty = FeelUI.ScoreLabel("Empty", root, 13f, TextAlignmentOptions.MidlineRight);
        Place(slot.empty.rectTransform, new Vector2(1f, .5f), new Vector2(1f, .5f), new Vector2(-20f, 0f), new Vector2(120f, 20f));
        slot.empty.text = Texts.Get("hud.weapon_slot.empty");
        MakeMeta(slot, 15f);
        slot.corner = MakeShape(root, "Corner", SlotShape.Kind.Corner);
        Place(slot.corner.rectTransform, Vector2.one, Vector2.one, new Vector2(1f, 1f), new Vector2(16f, 16f));
        slot.shard = MakeShape(root, "Shard", SlotShape.Kind.Shard);
        Place(slot.shard.rectTransform, Vector2.one, Vector2.one, new Vector2(9f, 7f), new Vector2(7f, 7f));
        slot.flash = MakeShape(root, "Flash", SlotShape.Kind.Plate, shape.Cut);
        Stretch(slot.flash.rectTransform);
        // The number's tab stands out past the plate's left side, over everything on it.
        slot.tab = MakeShape(root, "Tab", SlotShape.Kind.Tab, 0f, 5f);
        Place(slot.tab.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(-12f, -9f), new Vector2(28f, 28f));
        slot.number = MakeNumber(slot.tab.rectTransform, index, 20f);
        Stretch(slot.number.rectTransform);
        slot.number.alignment = TextAlignmentOptions.Center;
        return slot;
    }

    // ---- Cards (C) ----

    private void LayCards(int selected, float now, float dt, ref float readable)
    {
        // Right to left from the row's right end, bottoms on one line.
        float x = 0f;
        for (int i = Count - 1; i >= 0; i--)
        {
            var slot = built[i];
            float on = Mathf.Clamp01(slot.on);
            float width = Mathf.LerpUnclamped(shape.Width, shape.WidthHeld, slot.on), height = Mathf.LerpUnclamped(shape.Height, shape.HeightHeld, slot.on);
            slot.root.sizeDelta = new Vector2(width, height);
            slot.root.anchoredPosition = new Vector2(x, 0f);
            x -= width + shape.Gap;
            var data = Weapon(i);
            slot.plate.color = Color.Lerp(CardDark, KitStyle.Ice, on);
            slot.flash.color = new Color(1f, 1f, 1f, Flash(slot, now));
            slot.number.color = Color.Lerp(KitStyle.Soft, KitStyle.Accent, on);
            // The pink line is drawn along the top as the card turns, and is simply gone once it isn't in hand.
            slot.bar.rectTransform.localScale = new Vector3(i == selected ? Ease((now - slot.popAt) / Mathf.Max(.01f, shape.Bar)) : 0f, 1f, 1f);
            // The icon a little under the middle, lifting as the card opens for its name under it; drawn dark on the light card.
            float iconWidth = Mathf.LerpUnclamped(shape.Icon, shape.IconHeld, slot.on);
            var iconRect = slot.icon.rectTransform;
            iconRect.sizeDelta = new Vector2(iconWidth, iconWidth / WeaponIcons.Aspect);
            iconRect.anchoredPosition = new Vector2(0f, Mathf.Lerp(-.06f, .08f, on) * height);
            Color ink = Color.Lerp(KitStyle.With(Color.white, shape.IconAlpha), KitStyle.Night, on);
            ShowWeapon(slot, data, ink);
            slot.hatch.gameObject.SetActive(data == null);
            slot.hatch.color = Color.Lerp(Stripes, KitStyle.With(KitStyle.Night, .45f), on);
            slot.hatch.rectTransform.anchoredPosition = new Vector2(0f, -.08f * height);
            Meta(slot, data, i, 12f, 8f, width - 24f, KitStyle.Night, on, dt, i == selected, ref readable);
        }
    }

    private Slot MakeCard(int index)
    {
        var slot = new Slot { root = FeelUI.Rect("Slot " + (index + 1), holder) };
        var root = slot.root;
        root.anchorMin = root.anchorMax = Vector2.zero;
        root.pivot = new Vector2(1f, 0f);
        root.localRotation = Quaternion.Euler(0f, 0f, -shape.Lean[index]);
        slot.plate = MakeShape(root, "Card", SlotShape.Kind.Plate, shape.Cut);
        Stretch(slot.plate.rectTransform);
        slot.icon = MakePicture(root, new Vector2(.5f, .5f));
        slot.outline = MakeOutline(root, new Vector2(.5f, .5f));
        slot.hatch = FeelUI.Rect("Empty", root).gameObject.AddComponent<RawImage>();
        slot.hatch.raycastTarget = false;
        slot.hatch.texture = Stripes8();
        // One texel a pixel, so the stripes stay 3 px wide whatever the card's size.
        slot.hatch.uvRect = new Rect(0f, 0f, 26f / 8f, 14f / 8f);
        slot.hatch.rectTransform.sizeDelta = new Vector2(26f, 14f);
        MakeMeta(slot, 13f);
        slot.bar = FeelUI.Rect("Line", root).gameObject.AddComponent<Image>();
        slot.bar.raycastTarget = false;
        slot.bar.color = KitStyle.Accent;
        var bar = slot.bar.rectTransform;
        bar.anchorMin = new Vector2(0f, 1f); bar.anchorMax = Vector2.one; bar.pivot = new Vector2(0f, 1f);
        bar.sizeDelta = new Vector2(0f, 3f); bar.anchoredPosition = Vector2.zero;
        slot.flash = MakeShape(root, "Flash", SlotShape.Kind.Plate, shape.Cut);
        Stretch(slot.flash.rectTransform);
        slot.number = MakeNumber(root, index, 17f);
        Place(slot.number.rectTransform, new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(7f, -5f), new Vector2(24f, 18f));
        slot.number.alignment = TextAlignmentOptions.TopLeft;
        return slot;
    }

    // Over the ammo, wherever the HUD's layout and lean put it: Cards.right px in from its right end and Cards.bottom px up
    // from its foot, turned as it is. Without an ammo display, at the bottom right of the slots' own box.
    private void PlaceOverAmmo(float now)
    {
        if (ammo == null && now - ammoLookedAt > 1f)
        {
            ammoLookedAt = now;
            var canvas = GetComponentInParent<Canvas>();
            ammo = canvas != null ? canvas.rootCanvas.GetComponentInChildren<AmmoHUD>(true) : null;
            if (ammo == null) ammo = FindAnyObjectByType<AmmoHUD>(FindObjectsInactive.Include);
        }
        if (ammo != null && ammo.transform is RectTransform rect)
        {
            Rect r = rect.rect;
            holder.position = rect.TransformPoint(new Vector3(r.xMax - shape.Right, r.yMin + shape.Bottom, 0f));
            holder.rotation = rect.rotation;
            return;
        }
        holder.anchorMin = holder.anchorMax = new Vector2(1f, 0f);
        holder.anchoredPosition = new Vector2(-shape.Right, shape.Bottom);
        holder.localRotation = Quaternion.identity;
    }

    // ---- both ----

    private void Build(WeaponSlotLook wanted)
    {
        look = wanted;
        made = true;
        shape = Read(wanted);
        if (slots != null)
            foreach (var view in slots)
                if (view != null && view.rect != null) view.rect.gameObject.SetActive(false);
        if (holder != null) Destroy(holder.gameObject);
        bool plates = wanted == WeaponSlotLook.Plates;
        holder = FeelUI.Rect(plates ? "Plates" : "Cards", transform);
        holder.anchorMin = holder.anchorMax = holder.pivot = plates ? new Vector2(1f, .5f) : new Vector2(1f, 0f);
        holder.anchoredPosition = Vector2.zero;
        for (int i = 0; i < Count; i++) built[i] = plates ? MakePlate(i) : MakeCard(i);
        shownSelected = -2;
    }

    private WeaponData Weapon(int index) => source != null ? source.GetWeapon(index) : null;

    // The weapon's own picture, kept to its canvas's shape, or the vector outline where it has none; nothing for an empty slot.
    private static void ShowWeapon(Slot slot, WeaponData data, Color ink)
    {
        var picture = WeaponIcons.For(data);
        slot.icon.sprite = picture;
        slot.icon.enabled = picture != null;
        slot.icon.color = ink;
        bool stand = data != null && picture == null;
        slot.outline.enabled = stand;
        if (!stand) return;
        slot.outline.Kind = data.FireMode == WeaponFireMode.Automatic ? 1 : 2;
        slot.outline.rectTransform.anchorMin = slot.outline.rectTransform.anchorMax = slot.icon.rectTransform.anchorMin;
        slot.outline.rectTransform.pivot = slot.icon.rectTransform.pivot;
        slot.outline.rectTransform.anchoredPosition = slot.icon.rectTransform.anchoredPosition;
        slot.outline.rectTransform.sizeDelta = new Vector2(slot.icon.rectTransform.sizeDelta.y * 2.2f, slot.icon.rectTransform.sizeDelta.y * 1.3f);
        slot.outline.color = ink;
    }

    private void MakeMeta(Slot slot, float size)
    {
        slot.nameView = FeelUI.Rect("Name", slot.root);
        slot.nameView.gameObject.AddComponent<RectMask2D>();
        slot.nameView.anchorMin = slot.nameView.anchorMax = slot.nameView.pivot = Vector2.zero;
        slot.name = FeelUI.ScoreLabel("Text", slot.nameView, size, TextAlignmentOptions.BottomLeft);
        slot.name.fontStyle = FontStyles.Bold;
        var text = slot.name.rectTransform;
        text.anchorMin = text.anchorMax = text.pivot = Vector2.zero;
        slot.kind = FeelUI.ScoreLabel("Kind", slot.root, 10f, TextAlignmentOptions.BottomLeft);
        FontRoles.Apply(slot.kind, FontRoles.HudBold);
        slot.kind.fontStyle = FontStyles.Bold;
        slot.kind.characterSpacing = 16f;
        slot.kind.color = KitStyle.Accent;
        var kind = slot.kind.rectTransform;
        kind.anchorMin = kind.anchorMax = kind.pivot = Vector2.zero;
    }

    // The held weapon's name and its slot's kind side by side from (left, bottom), in room px, showing as the slot opens; a
    // name too long for what the kind leaves scrolls to its end and back, holding at each end, and keeps the slots up as
    // long as that takes.
    private void Meta(Slot slot, WeaponData data, int index, float left, float bottom, float room, Color ink, float on, float dt,
        bool held, ref float readable)
    {
        bool shown = data != null && on > .01f;
        slot.nameView.gameObject.SetActive(shown);
        slot.kind.gameObject.SetActive(shown);
        if (!shown) return;
        string kind = index < 2 ? "RANGED" : index == 2 ? "MELEE" : "SPECIAL";
        if (kind != slot.shownKind)
        {
            slot.shownKind = slot.kind.text = kind;
            slot.kindWidth = Mathf.Ceil(slot.kind.GetPreferredValues(kind).x) + 2f;
        }
        string name = data.DisplayName ?? "";
        if (name != slot.shownName)
        {
            slot.shownName = slot.name.text = name;
            slot.nameWidth = Mathf.Ceil(slot.name.GetPreferredValues(name).x) + 2f;
            slot.scrolled = 0f;
        }
        const float gap = 8f, lineHeight = 20f;
        float view = Mathf.Max(10f, Mathf.Min(slot.nameWidth, room - slot.kindWidth - gap));
        slot.nameView.sizeDelta = new Vector2(view, lineHeight);
        slot.nameView.anchoredPosition = new Vector2(left, bottom);
        slot.kind.rectTransform.sizeDelta = new Vector2(slot.kindWidth, lineHeight);
        slot.kind.rectTransform.anchoredPosition = new Vector2(left + view + gap, bottom);
        float overflow = Mathf.Max(0f, slot.nameWidth - view), offset = 0f;
        if (held && overflow > 0f)
        {
            float speed = Mathf.Max(1f, nameScrollSpeed), pause = Mathf.Max(0f, nameScrollPause), travel = overflow / speed;
            slot.scrolled = restartNames ? 0f : slot.scrolled + dt;
            float t = Mathf.Repeat(slot.scrolled, travel + pause * 2f);
            offset = -Mathf.Clamp(t - pause, 0f, travel) * speed;
            readable = Mathf.Max(readable, travel + pause * 2f);
        }
        slot.name.rectTransform.sizeDelta = new Vector2(slot.nameWidth, lineHeight);
        slot.name.rectTransform.anchoredPosition = new Vector2(offset, 0f);
        slot.name.color = KitStyle.With(ink, on);
        slot.kind.alpha = on;
    }

    // ---- motion ----

    // A spring toward 1 for the slot in hand and 0 for the rest: there in about `seconds`, a little past and back, as the
    // drafts' cubic-bezier(.2, .9, .3, 1.2) opened.
    private static void Spring(Slot slot, float target, float dt, float seconds)
    {
        float omega = 4.2f / Mathf.Max(.03f, seconds);
        dt = Mathf.Min(dt, .05f);
        slot.speed += ((target - slot.on) * omega * omega - slot.speed * 1.2f * omega) * dt;
        slot.on += slot.speed * dt;
    }

    // The plate taken in hand kicks out to the left and back past its place: the drafts' kick (-14 px, +3 px at 55%, home in .16 s).
    private float Kick(Slot slot, float now)
    {
        float k = (now - slot.popAt) / .16f;
        if (k < 0f || k >= 1f || shape.Kick <= 0f) return 0f;
        return k < .55f ? Mathf.Lerp(-shape.Kick, 3f, Ease(k / .55f)) : Mathf.Lerp(3f, 0f, (k - .55f) / .45f);
    }

    private float Flash(Slot slot, float now)
    {
        float t = (now - slot.popAt) / Mathf.Max(.01f, shape.Flash);
        return t < 0f || t >= 1f ? 0f : .9f * (1f - Ease(t));
    }

    private static float Ease(float s) { s = Mathf.Clamp01(s); return 1f - (1f - s) * (1f - s) * (1f - s); }

    // ---- pieces ----

    private static SlotShape MakeShape(RectTransform parent, string name, SlotShape.Kind kind, float cut = 0f, float shear = 0f)
    {
        var shape = FeelUI.Rect(name, parent).gameObject.AddComponent<SlotShape>();
        shape.Set(kind, cut, shear);
        return shape;
    }

    private static Image MakePicture(RectTransform parent, Vector2 anchor)
    {
        var image = FeelUI.Rect("Weapon", parent).gameObject.AddComponent<Image>();
        image.raycastTarget = false;
        image.preserveAspect = true;
        image.rectTransform.anchorMin = image.rectTransform.anchorMax = image.rectTransform.pivot = anchor;
        return image;
    }

    private static WeaponSlotSilhouette MakeOutline(RectTransform parent, Vector2 anchor)
    {
        var outline = FeelUI.Rect("Outline", parent).gameObject.AddComponent<WeaponSlotSilhouette>();
        outline.raycastTarget = false;
        outline.rectTransform.anchorMin = outline.rectTransform.anchorMax = outline.rectTransform.pivot = anchor;
        outline.enabled = false;
        return outline;
    }

    private static TextMeshProUGUI MakeNumber(RectTransform parent, int index, float size)
    {
        var number = FeelUI.ScoreLabel("Number", parent, size, TextAlignmentOptions.Center);
        FontRoles.Apply(number, FontRoles.HudBold);
        number.fontStyle = FontStyles.Bold | FontStyles.Italic;
        number.text = (index + 1).ToString(CultureInfo.InvariantCulture);
        return number;
    }

    private static void Place(RectTransform rect, Vector2 anchor, Vector2 pivot, Vector2 position, Vector2 size)
    {
        rect.anchorMin = rect.anchorMax = anchor;
        rect.pivot = pivot;
        rect.anchoredPosition = position;
        rect.sizeDelta = size;
    }

    private static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
    }

    // An empty card's diagonal stripes (the drafts' repeating 45° gradient): 3 px on, 5 off, tiling.
    private static Texture2D Stripes8()
    {
        if (stripes != null) return stripes;
        stripes = new Texture2D(8, 8, TextureFormat.RGBA32, false)
        {
            name = "Slot Stripes (Runtime)", wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point, hideFlags = HideFlags.DontSave
        };
        var pixels = new Color32[64];
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                pixels[y * 8 + x] = new Color32(255, 255, 255, (byte)((x + y) % 8 < 3 ? 255 : 0));
        stripes.SetPixels32(pixels);
        stripes.Apply();
        return stripes;
    }

    // ---- numbers ----

    private static Shape Read(WeaponSlotLook which)
    {
        bool plates = which == WeaponSlotLook.Plates;
        string row = plates ? "Plates" : "Cards";
        var shape = new Shape
        {
            Width = Number(row, "width", plates ? 240f : 74f),
            Height = Number(row, "height", plates ? 60f : 58f),
            WidthHeld = Number(row, "widthHeld", plates ? 240f : 196f),
            HeightHeld = Number(row, "heightHeld", plates ? 80f : 76f),
            Gap = Number(row, "gap", 14f),
            Cut = Number(row, "cut", plates ? 10f : 8f),
            Icon = Number(row, "icon", plates ? 136f : 62f),
            IconHeld = Number(row, "iconHeld", plates ? 136f : 150f),
            IconAlpha = Number(row, "iconAlpha", plates ? .5f : .62f),
            Kick = Number(row, "kick", plates ? 14f : 0f),
            Grow = Number(row, "grow", plates ? .14f : .16f),
            Flash = Number(row, "flash", .14f),
            Bar = Number(row, "bar", .12f),
            Right = Number(row, "right", 20f),
            Bottom = Number(row, "bottom", 104f),
        };
        Numbers(row, "lean", shape.Lean, plates ? new[] { -2f, 1.6f, -1.1f, 2.2f } : new[] { -3f, 2.2f, -1.4f, 2.8f });
        Numbers(row, "shift", shape.Shift, plates ? new[] { 0f, -10f, -3f, -14f } : new[] { 0f, 0f, 0f, 0f });
        return shape;
    }

    private static float Number(string row, string field, float fallback)
        => GameTables.TryGetRow(TableName, row, out var cells) && cells.TryGetValue(field, out string raw)
            && float.TryParse(raw.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;

    // One number per slot, ;-separated; a missing or unreadable one keeps its fallback.
    private static void Numbers(string row, string field, float[] into, float[] fallback)
    {
        string[] parts = GameTables.TryGetRow(TableName, row, out var cells) && cells.TryGetValue(field, out string raw)
            ? raw.Split(';') : Array.Empty<string>();
        for (int i = 0; i < into.Length; i++)
            into[i] = i < parts.Length && float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
                ? value : i < fallback.Length ? fallback[i] : 0f;
    }
}
