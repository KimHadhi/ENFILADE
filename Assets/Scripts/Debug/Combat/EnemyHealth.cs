
using System;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class EnemyHealth : MonoBehaviour, IDamageReceiver
{
    [SerializeField, Min(1f)]
    private float maximumHealth = 100f;

    public float Health { get; private set; }
    public bool IsAlive => Health > 0f;

    public event Action<EnemyHealth> Died;

    private void Awake()
    {
        Health = maximumHealth;
    }

    public DamageResult ReceiveHit(DamageHit hit)
    {
        if (!isActiveAndEnabled || !IsAlive ||
            hit.Id <= 0 || hit.Amount <= 0f ||
            float.IsNaN(hit.Amount) || float.IsInfinity(hit.Amount))
        {
            return DamageResult.Ignored;
        }

        Health = Mathf.Max(0f, Health - hit.Amount);

        Debug.Log(
            $"[{name}] 피해: {hit.Amount}, 남은 체력: {Health}/{maximumHealth}",
            this
        );

        if (!IsAlive)
        {
            Die();
        }

        return DamageResult.Damaged;
    }

    private void Die()
    {
        Debug.Log($"[{name}] 사망!", this);

        // 웨이브 관리자에게 처치 사실을 알린다.
        Died?.Invoke(this);

        Destroy(gameObject);
    }
}
