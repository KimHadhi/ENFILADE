using UnityEngine;

/// <summary>Optional practice turret: lights up, commits its aim, then sends a real raycast hit through the damage API.</summary>
public sealed class MovementTrainingAttacker : MonoBehaviour
{
    [SerializeField, Min(0.2f)] private float interval = 2f;
    [SerializeField, Min(0.05f)] private float windup = 0.4f;
    [SerializeField, Min(0.1f)] private float range = 8f;
    [SerializeField, Min(0f)] private float damage = 10f;
    private float timer;
    private bool preparing;
    private Vector3 aim;
    private PlayerMovement player;
    private Renderer indicator;
    private MaterialPropertyBlock color;
    private void Awake() { indicator = GetComponent<Renderer>(); color = new MaterialPropertyBlock(); }
    private void Update()
    {
        if (player == null) player = FindFirstObjectByType<PlayerMovement>();
        if (player == null || !player.DamageReceiver.IsAlive || !player.InputAvailable) return;
        timer += Time.deltaTime;
        if (!preparing && timer >= interval && Vector3.Distance(transform.position, player.transform.position) <= range)
        { preparing = true; timer = 0f; aim = player.transform.position + Vector3.up; }
        if (indicator != null)
        {
            Color tint = preparing ? Color.yellow : new Color(0.8f, 0.15f, 0.15f);
            color.SetColor("_BaseColor", tint); color.SetColor("_Color", tint); indicator.SetPropertyBlock(color);
        }
        if (!preparing || timer < windup) return;
        preparing = false; timer = 0f;
        Vector3 direction = (aim - transform.position).normalized;
        AttackTrace.Raycast(new DamageHit(DamageHit.NewAttackId(), damage, DamageKind.Attack, aim, direction, gameObject),
            transform.position + direction * 0.4f, direction, range);
    }
}
