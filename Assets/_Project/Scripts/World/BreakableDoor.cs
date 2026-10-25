using UnityEngine;

[RequireComponent(typeof(Collider))]
public class BreakableDoor : MonoBehaviour, IBreakable
{
    [Header("Effects")]
    [SerializeField] private GameObject fracturePrefab; // 산산조각 나는 문 파편 프리팹 (선택)
    [SerializeField] private AudioClip breakSound;       // 파괴 효과음 (선택)

    private bool isBroken = false;

    public void Break(Vector3 hitPoint, Vector3 hitNormal, float impactSpeed)
    {
        if (isBroken) return;
        isBroken = true;

        // 1. 사운드 재생
        if (breakSound != null)
        {
            AudioSource.PlayClipAtPoint(breakSound, hitPoint);
        }

        // 2. 파편 프리팹 생성 (파편에 Rigidbody가 있다면 날아가는 효과)
        if (fracturePrefab != null)
        {
            GameObject fractured = Instantiate(fracturePrefab, transform.position, transform.rotation);
            // 파편들의 Rigidbody에 충돌 방향으로 힘을 주는 연출 추가 가능
        }

        // 3. 기존 문 즉시 비활성화 또는 제거 (충돌체가 꺼져야 플레이어가 바로 뚫고 감)
        Destroy(gameObject);
    }
}