
using UnityEngine;

public class WeaponVisibility : MonoBehaviour
{
    [Header("무기 슬롯 시스템")]
    [SerializeField] private PlayerWeaponSlots weaponSlots;

    [Header("총 모델")]
    [SerializeField] private GameObject gun;

    private void Update()
    {
        if (weaponSlots == null || gun == null)
            return;

        bool hasWeapon = weaponSlots.SelectedWeapon != null && weaponSlots.SelectedWeapon.Category != WeaponCategory.Melee;

        if (gun.activeSelf != hasWeapon)
        {
            gun.SetActive(hasWeapon);
        }
    }
}
