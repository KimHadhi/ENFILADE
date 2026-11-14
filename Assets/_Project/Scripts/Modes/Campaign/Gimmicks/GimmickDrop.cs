using UnityEngine;

/// <summary>
/// A load hanging from a crane over where the horde comes (the map gimmick card "매달린 컨테이너 떨구기", after PAYDAY 2's
/// Slaughterhouse): shoot its cable (GimmickTarget) and it falls, crushing everything under it. Lure them under it first
/// and it takes a crowd; never used, nothing is lost (a hit gives +, a miss gives nothing). The shooter's PC judges who
/// was under it as it lands (the actor decides); every PC sees it fall. States: 0 hanging, 1 fallen.
/// </summary>
public sealed class GimmickDrop : GimmickSwitch, IGimmickShot
{
    private const byte Hanging = 0, Fallen = 1;
    // How long the fall takes (s): a free fall from where it hangs, near enough.
    private const float FallSeconds = .75f;

    [SerializeField, Tooltip("What falls: the load and its rigging (its cable goes with it).")]
    private Transform load;
    [SerializeField, Tooltip("How far it falls (world, down).")]
    private float drop = 16f;
    [SerializeField, Tooltip("Where it lands (world box): everything inside dies.")]
    private Bounds crush;
    [SerializeField, Tooltip("The cable that breaks, hidden once it has.")]
    private GameObject cable;

    private Vector3 hangsAt;
    private float fellAt = -1f;
    private bool landed;
    private PlayerMovement shooter;

    private void Awake()
    {
        if (load != null) hangsAt = load.position;
    }

    public void Shot(PlayerMovement by, in DamageHit hit)
    {
        if (State != Hanging) return;
        shooter = by;
        Set(Fallen);
    }

    internal override bool Accepts(byte now, byte proposed) => proposed > now;

    internal override void Show(byte state, bool fresh)
    {
        if (state != Fallen) return;
        if (cable != null) cable.SetActive(false);
        if (!fresh)
        {
            landed = true;
            if (load != null) load.position = hangsAt + Vector3.down * drop;
            return;
        }
        fellAt = Time.time;
        GimmickSounds.PlayAt(GimmickSounds.Cue.Snap, hangsAt, 1f, 4f, 50f);
    }

    internal override void Tick(float dt, bool host)
    {
        if (State != Fallen || landed || fellAt < 0f || load == null) return;
        float t = Mathf.Clamp01((Time.time - fellAt) / FallSeconds);
        load.position = hangsAt + Vector3.down * (drop * t * t);
        if (t < 1f) return;
        landed = true;
        GimmickSounds.PlayAt(GimmickSounds.Cue.Crash, crush.center, 1f, 8f, 90f);
        // The shooter's PC: whoever stood under it (their kills, as a frag's are).
        if (shooter == null) return;
        // Gathered first: a kill takes the body off the living as it goes.
        under.Clear();
        foreach (var enemy in EnemyHealth.Living)
            if (enemy != null && enemy.IsAlive && crush.Contains(enemy.transform.position + Vector3.up * .5f)) under.Add(enemy);
        foreach (var enemy in under)
        {
            if (enemy == null || !enemy.IsAlive) continue;
            enemy.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), enemy.Health + 1f, DamageKind.Attack,
                enemy.transform.position + Vector3.up, Vector3.down, shooter.gameObject, method: DamageMethod.Blast, origin: crush.center));
        }
        shooter = null;
        under.Clear();
    }

    private static readonly System.Collections.Generic.List<EnemyHealth> under = new System.Collections.Generic.List<EnemyHealth>();
}
