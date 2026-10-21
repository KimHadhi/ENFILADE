using UnityEngine;
using TMPro;
using UnityEngine.UI;

public class AmmoHUD : MonoBehaviour
{
    [Header("기존 무기 시스템")]
    [SerializeField] private PlayerWeaponSlots weaponSlots;

    [Header("오른쪽 아래 UI")]
    [SerializeField] private WeaponSlotSilhouette weaponIcon;
    [SerializeField] private TMP_Text ammoText;

    [Header("탄약 막대")]
    [SerializeField] private Image[] ammoMarks;

    [Header("재장전 원형 게이지")]
    [SerializeField] private Image reloadGauge;
    [SerializeField] private Image reloadGaugeBackground;

    private void OnEnable()
    {
        if (weaponSlots != null)
            weaponSlots.SelectionChanged += OnWeaponChanged;
    }

    private void Start()
    {
        if (weaponSlots != null)
            RefreshUI(weaponSlots.SelectedSlot);

        RefreshReloadGauge();
    }

    private void OnDisable()
    {
        if (weaponSlots != null)
            weaponSlots.SelectionChanged -= OnWeaponChanged;
    }

    private void Update()
    {
        if (weaponSlots == null)
            return;

        RefreshAmmo();
        RefreshReloadGauge();
    }

    private void OnWeaponChanged(int slot)
    {
        RefreshUI(slot);
    }

    private void RefreshUI(int slot)
    {
        bool hasWeapon =
            weaponSlots != null &&
            slot >= 0 &&
            slot < PlayerWeaponSlots.SlotCount &&
            weaponSlots.GetWeapon(slot) != null;

        if (weaponIcon != null)
        {
            weaponIcon.Kind = hasWeapon ? (weaponSlots.GetWeapon(slot).FireMode == WeaponFireMode.Automatic ? 1 : 2) : 0;
            weaponIcon.enabled = weaponSlots != null && slot >= 0;
            weaponIcon.color = hasWeapon ? Color.white : new Color(1f, 1f, 1f, .45f);
        }

        RefreshAmmo();
        RefreshReloadGauge();
    }

    private void RefreshAmmo()
    {
        if (ammoText == null || weaponSlots == null)
            return;

        int slot = weaponSlots.SelectedSlot;

        if (slot < 0 || slot >= PlayerWeaponSlots.SlotCount)
        {
            ammoText.text = "";
            UpdateAmmoMarks(0, 0);
            return;
        }

        WeaponRuntime runtime = weaponSlots.GetRuntime(slot);

        if (runtime == null || runtime.Data == null || runtime.Data.Category == WeaponCategory.Melee)
        {
            ammoText.text = runtime == null ? "\u00b7" : runtime.Data.DisplayName;
            UpdateAmmoMarks(0, 0);
            return;
        }

        int currentAmmo = runtime.Ammo;
        int maxAmmo = runtime.Data.MagazineSize;

        ammoText.text = currentAmmo + "/" + maxAmmo;

        UpdateAmmoMarks(currentAmmo, maxAmmo);
    }

    private void UpdateAmmoMarks(int currentAmmo, int maxAmmo)
    {
        if (ammoMarks == null)
            return;

        int visibleCount = maxAmmo > 0
            ? Mathf.CeilToInt(
                (float)currentAmmo / maxAmmo * ammoMarks.Length)
            : 0;

        for (int i = 0; i < ammoMarks.Length; i++)
        {
            if (ammoMarks[i] != null)
                ammoMarks[i].enabled = i < visibleCount;
        }
    }

    private void RefreshReloadGauge()
    {
        WeaponRuntime runtime = null;

        if (weaponSlots != null)
        {
            int slot = weaponSlots.SelectedSlot;

            if (slot >= 0 && slot < PlayerWeaponSlots.SlotCount)
                runtime = weaponSlots.GetRuntime(slot);
        }

        bool reloading =
            runtime != null &&
            runtime.Data != null &&
            runtime.IsReloading;

        // 재장전 중에만 배경과 게이지 표시
        if (reloadGaugeBackground != null)
            reloadGaugeBackground.enabled = reloading;

        if (reloadGauge != null)
            reloadGauge.enabled = reloading;

        if (!reloading || reloadGauge == null)
            return;

        float duration = runtime.Data.ReloadDuration;

        float progress = duration > 0f
            ? 1f - runtime.ReloadRemaining / duration
            : 1f;

        reloadGauge.fillAmount = Mathf.Clamp01(progress);
    }
}
