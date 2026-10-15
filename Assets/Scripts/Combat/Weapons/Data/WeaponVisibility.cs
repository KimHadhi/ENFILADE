
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

        // 선택된 슬롯이 없으면 총을 숨김
        bool hasWeapon = weaponSlots.SelectedSlot >= 0;

        if (gun.activeSelf != hasWeapon)
        {
            gun.SetActive(hasWeapon);
        }
    }
}
