
using UnityEngine;
using UnityEngine.AI;

[RequireComponent(typeof(NavMeshAgent))]
[RequireComponent(typeof(EnemyHealth))]
public sealed class EnemyCombatAI : MonoBehaviour
{
    [Header("Attack Settings")]
    [SerializeField, Min(1f)] private float attackRange = 2f;
    [SerializeField, Min(0.2f)] private float attackInterval = 1f;
    [Tooltip("Seconds from the attack cue to the hit. Keep the hit just past the cue's loudest part: that is when players press.")]
    [SerializeField, Min(0.05f)] private float windup = .45f;
    [Tooltip("Each combo part has its own cue. Only parrying the last part opens a perfect counter.")]
    [SerializeField, Min(1)] private int comboHits = 1;
    [SerializeField, Min(0f)] private float damage = 10f;

    [Header("Aim Settings")]
    [SerializeField] private float aimHeight = 1f;
    [SerializeField] private float rayStartOffset = 0.4f;
    [SerializeField] private LayerMask hitLayers = ~0;

    private NavMeshAgent agent;
    private EnemyHealth enemyHealth;
    private MeleeCombatTarget meleeTarget;
    private EnemyTargeting targeting;
    private PlayerMovement player;
    private Renderer indicator;
    private MaterialPropertyBlock color;

    private float timer;
    private bool preparing;
    private Vector3 lockedAim;
    private int comboIndex;

    public bool IsPreparingAttack => preparing;

    private void Awake()
    {
        agent = GetComponent<NavMeshAgent>();
        enemyHealth = GetComponent<EnemyHealth>();
        meleeTarget = GetComponent<MeleeCombatTarget>();
        if (meleeTarget == null) meleeTarget = gameObject.AddComponent<MeleeCombatTarget>();
        targeting = GetComponent<EnemyTargeting>();
        if (targeting == null) targeting = gameObject.AddComponent<EnemyTargeting>();
        indicator = GetComponent<Renderer>();
        color = new MaterialPropertyBlock();
        timer = attackInterval;
    }

    private void Update() => Tick(Time.deltaTime);
    public void Tick(float dt)
    {
        if (dt <= 0f) return;
        if (PlayerExecution.Suppresses(this) || (meleeTarget != null && meleeTarget.IsStunned)) { CancelAttack(); return; }
        if (enemyHealth == null || !enemyHealth.IsAlive || enemyHealth.WavePaused)
            { CancelAttack(); return; }

        player = targeting.Player;
        if (player == null || !agent.isOnNavMesh)
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
                timer = attackInterval;
                UpdateIndicator(false);
                return;
            }

            timer += dt;

            if (timer >= attackInterval)
            {
                comboIndex = 0;
                BeginWindup();
            }

            return;
        }

        // 공격 준비 중에는 조준 위치를 변경하지 않는다.
        timer += dt;

        if (timer < windup)
            return;

        FireAttack();
    }
    private void BeginWindup()
    {
        preparing = true; timer = 0f;
        lockedAim = player.transform.position + Vector3.up * aimHeight;
        UpdateIndicator(true);
        CombatCueAudio.Play(gameObject, CombatCue.EnemyAttackStart);
    }

    private void FireAttack()
    {
        if (PlayerExecution.Suppresses(this) || (meleeTarget != null && meleeTarget.IsStunned)) { CancelAttack(); return; }
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
            gameObject, true, 0f, comboIndex >= Mathf.Max(1, comboHits) - 1
        );

        var result = AttackTrace.Raycast(
            hit,
            origin,
            direction,
            attackRange + rayStartOffset,
            hitLayers
        );
        if (!hit.IsComboFinisher && result != DamageResult.Parried && meleeTarget?.IsStunned != true)
        { comboIndex++; BeginWindup(); }
    }

    public void CancelAttack()
    {
        preparing = false;
        timer = 0f;
        comboIndex = 0;
        GetComponent<CombatCueAudio>()?.Stop();
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
