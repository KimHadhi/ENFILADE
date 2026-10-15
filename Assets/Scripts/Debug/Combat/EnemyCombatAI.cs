
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(EnemyHealth))]
public sealed class EnemyCombatAI : MonoBehaviour
{
    [Header("Attack Settings")]
    [SerializeField, Min(1f)] private float attackRange = 2f;
    [SerializeField, Min(0.2f)] private float attackInterval = 2f;
    [SerializeField, Min(0.05f)] private float windup = 0.6f;
    [SerializeField, Min(0f)] private float damage = 10f;

    [Header("Aim Settings")]
    [SerializeField] private float aimHeight = 1f;
    [SerializeField] private float rayStartOffset = 0.4f;
    [SerializeField] private LayerMask hitLayers = ~0;

    private NavMeshAgent agent;
    private EnemyHealth enemyHealth;
    private PlayerMovement player;
    private Renderer indicator;
    private MaterialPropertyBlock color;

    private float timer;
    private bool preparing;
    private Vector3 lockedAim;

    public bool IsPreparingAttack => preparing;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        enemyHealth = GetComponent<EnemyHealth>();
        indicator = GetComponent<Renderer>();
        color = new MaterialPropertyBlock();
    }

    private void Start()
    {
        player = FindFirstObjectByType<PlayerMovement>();
    }

    private void Update()
    {
        if (enemyHealth == null || !enemyHealth.IsAlive || enemyHealth.WavePaused)
            return;

        if (player == null)
        {
            player = FindFirstObjectByType<PlayerMovement>();
            return;
        }

        if (!player.IsInitialized ||
            !player.DamageReceiver.IsAlive ||
            !agent.isOnNavMesh)
        {
            CancelAttack();
            return;
        }

        float distance = Vector3.Distance(
            transform.position,
            player.transform.position
        );

        if (!preparing)
        {
            if (distance > attackRange)
            {
                timer = 0f;
                UpdateIndicator(false);
                return;
            }

            timer += Time.deltaTime;

            if (timer >= attackInterval)
            {
                preparing = true;
                timer = 0f;

                lockedAim = player.transform.position
                    + Vector3.up * aimHeight;

                UpdateIndicator(true);
            }

            return;
        }

        // 공격 준비 중에는 조준 위치를 변경하지 않는다.
        timer += Time.deltaTime;

        if (timer < windup)
            return;

        FireAttack();
    }

    private void FireAttack()
    {
        preparing = false;
        timer = 0f;
        UpdateIndicator(false);

        Vector3 origin = transform.position;
        Vector3 direction = (lockedAim - origin).normalized;

        if (direction.sqrMagnitude < 0.0001f)
            return;

        // 적 자신의 충돌체 안에서 광선이 시작되는 것을 줄인다.
        origin += direction * rayStartOffset;

        DamageHit hit = new DamageHit(
            DamageHit.NewAttackId(),
            damage,
            DamageKind.Attack,
            lockedAim,
            direction,
            gameObject
        );

        AttackTrace.Raycast(
            hit,
            origin,
            direction,
            attackRange + rayStartOffset,
            hitLayers
        );
    }

    private void CancelAttack()
    {
        preparing = false;
        timer = 0f;
        UpdateIndicator(false);
    }

    private void UpdateIndicator(bool isPreparing)
    {
        if (indicator == null)
            return;

        // 평소에는 회색, 공격 준비 중에는 노란색
        Color tint = isPreparing
            ? Color.yellow
            : new Color(0.5f, 0.5f, 0.5f);

        indicator.GetPropertyBlock(color);
        color.SetColor("_BaseColor", tint);
        color.SetColor("_Color", tint);
        indicator.SetPropertyBlock(color);
    }
}
