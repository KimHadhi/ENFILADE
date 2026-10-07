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
    [SerializeField] private SlotView[] slots = new SlotView[3];
    [SerializeField, Min(0)] private float visibleSeconds = 3f;
    [SerializeField, Min(0)] private float fadeSeconds = 1f;
    [SerializeField, Range(1,1.3f)] private float selectedScale = 1.12f;
    [SerializeField, Min(1)] private float animationSpeed = 14f;
    [SerializeField, Min(1)] private float rowHeight = 88f;
    [SerializeField, Min(0)] private float rowGap = 10f;
    [SerializeField] private Color selectedColor = new Color(.58f,.95f,1f,1f);
    private PlayerWeaponSlots bound;
    private readonly float[] emphasis = new float[3];
    private float lastActivity = float.NegativeInfinity, nextSearch;

    private void OnEnable() { Bind(); }
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
    public void Reveal() { lastActivity = Time.unscaledTime; if (group != null) group.alpha = 1f; }
    private void LateUpdate() { Refresh(Time.unscaledDeltaTime); }

    public void Refresh(float deltaTime)
    {
        Bind();
        if (group == null || slots == null || slots.Length != 3) return;
        float age = Time.unscaledTime - lastActivity;
        group.alpha = age <= visibleSeconds ? 1f : fadeSeconds <= 0 ? 0f : 1f - Mathf.Clamp01((age-visibleSeconds)/fadeSeconds);
        group.blocksRaycasts = group.interactable = false;
        int selected = source != null ? source.SelectedSlot : -1;
        float total = rowGap * 2f;
        float blend = 1f - Mathf.Exp(-animationSpeed * Mathf.Max(0,deltaTime));
        for (int i=0;i<3;i++)
        {
            emphasis[i] = Mathf.Lerp(emphasis[i],i == selected ? 1f : 0f,blend);
            total += rowHeight * Mathf.Lerp(1f,selectedScale,emphasis[i]);
        }
        float top = total * .5f;
        for (int i=0;i<3;i++)
        {
            var view = slots[i]; if (view.rect == null) continue;
            float e = emphasis[i], scale = Mathf.Lerp(1f,selectedScale,e), height = rowHeight * scale;
            view.rect.localScale = new Vector3(scale,scale,1);
            view.rect.anchoredPosition = new Vector2(0,top - height*.5f); top -= height+rowGap;
            var data = source != null ? source.GetWeapon(i) : null;
            view.background.color = Color.Lerp(new Color(.035f,.06f,.095f,.58f),new Color(.07f,.2f,.25f,.9f),e);
            view.accent.color = new Color(selectedColor.r,selectedColor.g,selectedColor.b,e);
            view.outline.effectColor = new Color(selectedColor.r,selectedColor.g,selectedColor.b,e*.65f);
            view.number.color = Color.Lerp(new Color(.7f,.77f,.8f,1f),selectedColor,e);
            view.weaponName.text = data != null ? data.DisplayName : "";
            view.weaponName.gameObject.SetActive(i == selected && data != null);
            bool image = data != null && data.HudIcon != null;
            view.icon.sprite = image ? data.HudIcon : null;
            view.icon.enabled = image;
            view.icon.color = Color.Lerp(new Color(.72f,.79f,.83f,1),selectedColor,e);
            view.silhouette.enabled = !image;
            view.silhouette.Kind = data == null ? 0 : data.FireMode == WeaponFireMode.Automatic ? 1 : 2;
            view.silhouette.color = data == null ? new Color(.6f,.7f,.75f,.3f) : view.icon.color;
        }
    }
}
