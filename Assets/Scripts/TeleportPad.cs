using UnityEngine;

public class TeleportPad : MonoBehaviour
{
    [Header("순간이동 목적지 Transform")]
    [SerializeField] private Transform destination;

    [Header("반응할 태그")]
    [SerializeField] private string targetTag = "Player";

    private void OnTriggerEnter(Collider other)
    {
        if (!other.CompareTag(targetTag) || destination == null) return;

        // 1. CharacterController를 사용하는 경우
        // CharacterController가 활성화된 상태에서 transform.position을 바꾸면 내부 캐시로 인해 이동이 씹힐 수 있습니다.
        if (other.TryGetComponent<CharacterController>(out var controller))
        {
            controller.enabled = false;
            other.transform.position = destination.position;
            other.transform.rotation = destination.rotation;
            controller.enabled = true;
            return;
        }

        // 2. Rigidbody(3D)를 사용하는 경우
        // 물리 관성을 초기화하고 이동시킵니다.
        if (other.TryGetComponent<Rigidbody>(out var rb))
        {
            rb.linearVelocity = Vector3.zero; // Unity 6 기준 (2022 이하는 rb.velocity = Vector3.zero)
            rb.angularVelocity = Vector3.zero;
            rb.position = destination.position;
            rb.rotation = destination.rotation;
            return;
        }

        // 3. 단순 Transform 기반인 경우
        other.transform.position = destination.position;
        other.transform.rotation = destination.rotation;
    }
}