using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
    [SerializeField] private PlayerWeaponSlots source;
    [SerializeField] private CanvasGroup group;
    [SerializeField] private SlotView[] slots = new SlotView[PlayerWeaponSlots.SlotCount];
    [SerializeField, Min(0)] private float visibleSeconds = 3f;
    [SerializeField, Min(0)] private float fadeSeconds = 1f;
    [SerializeField, Range(1,1.3f)] private float selectedScale = 1.12f;
    [SerializeField, Min(1)] private float animationSpeed = 14f;
    [SerializeField, Min(1)] private float rowHeight = 88f;
    [SerializeField, Min(0)] private float rowGap = 10f;
    [Header("Long weapon names")]
    [SerializeField, Min(1f)] private float nameScrollSpeed = 32f;
    [SerializeField, Min(0f)] private float nameScrollPause = .65f;
    [SerializeField] private Color selectedColor = new Color(.58f,.95f,1f,1f);
    private PlayerWeaponSlots bound;
    private readonly float[] emphasis = new float[PlayerWeaponSlots.SlotCount];
    private float lastActivity = float.NegativeInfinity, nextSearch;
    private bool restartNames;
    private readonly WeaponNameMarquee[] nameScrolls = new WeaponNameMarquee[PlayerWeaponSlots.SlotCount];

    private void OnEnable() { if (Application.isPlaying) UpgradeToFourSlots(); Bind(); }
    public void UpgradeToFourSlots()
    {
        if (slots == null || slots.Length != 3 || slots[2]?.rect == null) return;
        var original = slots[2];
        var clone = Instantiate(original.rect.gameObject, original.rect.parent).GetComponent<RectTransform>();
        clone.name = "Slot 4";
        Array.Resize(ref slots, PlayerWeaponSlots.SlotCount);
        slots[3] = new SlotView
        {
            rect = clone, background = clone.GetComponent<Image>(), outline = clone.GetComponent<Outline>(),
            accent = clone.Find("Selection accent").GetComponent<Image>(),
            icon = clone.Find("Weapon image").GetComponent<Image>(),
            silhouette = clone.Find("Fallback silhouette").GetComponent<WeaponSlotSilhouette>(),
            number = clone.Find("Slot number").GetComponent<TMP_Text>(),
            weaponName = Array.Find(clone.GetComponentsInChildren<TMP_Text>(true), text => text.name == "Weapon name")
        };
        slots[3].number.text = "4";
    }
    private void OnDisable() { if (bound != null) bound.SlotActivity -= Reveal; bound = null; }
    private void Bind()
    {
        if (source == null && Time.unscaledTime >= nextSearch)
        {
            nextSearch = Time.unscaledTime + .5f;
            PlayerWeaponSlots candidate = null; int count = 0;
            foreach (var item in FindObjectsByType<PlayerWeaponSlots>(FindObjectsInactive.Exclude,FindObjectsSortMode.None))
                if (item.gameObject.scene == gameObject.scene) { candidate = item; count++; }
            if (count == 1) source = candidate;
        }
        if (bound == source) return;
        if (bound != null) bound.SlotActivity -= Reveal;
        bound = source;
        if (bound != null) bound.SlotActivity += Reveal;
    }
    public void Reveal() { lastActivity = Time.unscaledTime; restartNames = true; if (group != null) group.alpha = 1f; }
    private void LateUpdate() { Refresh(Time.unscaledDeltaTime); }

    public void Refresh(float deltaTime)
    {
        Bind();
        if (group == null || slots == null || slots.Length != PlayerWeaponSlots.SlotCount) return;
        float age = Time.unscaledTime - lastActivity;
        float readableSeconds = visibleSeconds;
        group.blocksRaycasts = group.interactable = false;
        int selected = source != null ? source.SelectedSlot : -1;
        float total = rowGap * (PlayerWeaponSlots.SlotCount - 1);
        float blend = 1f - Mathf.Exp(-animationSpeed * Mathf.Max(0,deltaTime));
        for (int i=0;i<PlayerWeaponSlots.SlotCount;i++)
        {
            emphasis[i] = Mathf.Lerp(emphasis[i],i == selected ? 1f : 0f,blend);
            total += rowHeight * Mathf.Lerp(1f,selectedScale,emphasis[i]);
        }
        float top = total * .5f;
        for (int i=0;i<PlayerWeaponSlots.SlotCount;i++)
        {
            var view = slots[i]; if (view == null || view.rect == null) continue;
            float e = emphasis[i], scale = Mathf.Lerp(1f,selectedScale,e), height = rowHeight * scale;
            view.rect.localScale = new Vector3(scale,scale,1);
            view.rect.anchoredPosition = new Vector2(0,top - height*.5f); top -= height+rowGap;
            var data = source != null ? source.GetWeapon(i) : null;
            view.background.color = Color.Lerp(new Color(.035f,.06f,.095f,.58f),new Color(.07f,.2f,.25f,.9f),e);
            view.accent.color = new Color(selectedColor.r,selectedColor.g,selectedColor.b,e);
            view.outline.effectColor = new Color(selectedColor.r,selectedColor.g,selectedColor.b,e*.65f);
            view.number.color = Color.Lerp(new Color(.7f,.77f,.8f,1f),selectedColor,e);
            string category = i < 2 ? "RANGED" : i == 2 ? "MELEE" : "SPECIAL";
            string label = category + (i == selected ? " / " + (data != null ? data.DisplayName : "Unarmed") : "");
            view.weaponName.gameObject.SetActive(true);
            if (nameScrolls[i] == null)
                nameScrolls[i] = view.weaponName.GetComponent<WeaponNameMarquee>() ?? view.weaponName.gameObject.AddComponent<WeaponNameMarquee>();
            nameScrolls[i].Refresh(label, i == selected, deltaTime, nameScrollSpeed, nameScrollPause, restartNames);
            if (i == selected) readableSeconds = Mathf.Max(readableSeconds, nameScrolls[i].ReadDuration);
            view.number.text = (i + 1).ToString();
            bool image = data != null && data.HudIcon != null;
            view.icon.sprite = image ? data.HudIcon : null;
            view.icon.enabled = image;
            view.icon.color = Color.Lerp(new Color(.72f,.79f,.83f,1),selectedColor,e);
            view.silhouette.enabled = !image;
            view.silhouette.Kind = data == null ? 0 : data.FireMode == WeaponFireMode.Automatic ? 1 : 2;
            view.silhouette.color = data == null && i != selected ? new Color(.6f,.7f,.75f,.45f) : view.icon.color;
        }
        restartNames = false;
        group.alpha = age <= readableSeconds ? 1f : fadeSeconds <= 0 ? 0f : 1f - Mathf.Clamp01((age-readableSeconds)/fadeSeconds);
    }
}
