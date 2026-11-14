using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// A fuel truck on the apron (the map gimmick card "번지는 연료 불"; the user's note "급유차 터지면 미션 진행은 어떻게
/// 됨?", answered "임무는 그대로 이어져요"): a shot or a blast sets it off at any time; if nobody does, it goes up by
/// itself goOff seconds into its step (the tug's hold), as the set piece always did (its CampaignBlast, now fired from
/// here). Its blast takes the zombies round it, and its fuel spreads into a lane of fire across the apron that burns for
/// seconds: whatever stands in it burns, and the lane carves the walk mesh, so the horde goes round it. The mission goes on
/// as ever. Whoever set it off burns the zombies on their own PC (their kills, the actor decides; the host when it went by
/// itself); each PC burns its own player. A wipe that sends the run back sets it up again. States: 0 standing, 1 burning,
/// 2 burnt out.
/// </summary>
public sealed class GimmickFuelTruck : GimmickSwitch, IGimmickShot
{
    private const byte Standing = 0, Burning = 1, Burnt = 2;
    private const float BiteEvery = .5f;

    [SerializeField, Tooltip("The step it goes off in by itself (CampaignSteps id).")]
    private string step = "";
    [SerializeField, Min(0f), Tooltip("How long into its step it goes off by itself (s).")]
    private float goOff = 35f;
    [SerializeField, Tooltip("Its blast's look, sound and char (fired from here).")]
    private CampaignBlast blast;
    [SerializeField, Min(0f), Tooltip("How far its blast reaches (m).")]
    private float blastRadius = 9f;
    [SerializeField, Min(0f), Tooltip("Its blast's damage to a zombie in reach.")]
    private float blastZombie = 400f;
    [SerializeField, Min(0f), Tooltip("Its blast's damage to a player in reach.")]
    private float blastPlayer = 35f;
    [SerializeField, Tooltip("The lane its fuel spreads into (world box): what stands in it burns.")]
    private Bounds lane;
    [SerializeField, Min(1f), Tooltip("How long the lane burns (s).")]
    private float seconds = 40f;
    [SerializeField, Min(0f), Tooltip("Damage a bite (every half second) to a zombie in the fire.")]
    private float zombieBite = 45f;
    [SerializeField, Min(0f), Tooltip("Damage a bite to a player in the fire.")]
    private float playerBite = 8f;
    [SerializeField, Tooltip("A carving obstacle over the lane, on while it burns, so the horde goes round.")]
    private NavMeshObstacle block;

    private static readonly List<EnemyHealth> caught = new List<EnemyHealth>();
    private PlayerMovement lighter;
    private GimmickFireLook fire;
    private float runningSince = -1f, litAt, nextBite;
    private int wipesSeen;

    private void Start()
    {
        wipesSeen = CampaignRun.Wipes;
        if (block != null) block.enabled = false;
    }

    public void Shot(PlayerMovement by, in DamageHit hit) => SetOff(by);

    private void SetOff(PlayerMovement by)
    {
        if (State != Standing) return;
        lighter = by;
        Set(Burning);
    }

    /// <summary>A blast on this PC from its own player: a truck in reach goes up.</summary>
    internal static void Blast(Vector3 at, float radius, PlayerMovement by)
    {
        var map = MapGimmicks.Active;
        if (map == null) return;
        foreach (var each in map.Switches)
            if (each is GimmickFuelTruck truck && (truck.transform.position - at).sqrMagnitude <= radius * radius) truck.SetOff(by);
    }

    internal override bool Accepts(byte now, byte proposed) => now == Standing && proposed == Burning;

    internal override void Tick(float dt, bool host)
    {
        if (host) HostTick();
        if (State != Burning) return;
        fire?.Tick();
        if (Time.time - litAt >= seconds)
        {
            Fire(false);
            if (host) Set(Burnt);
            return;
        }
        if (Time.time < nextBite) return;
        nextBite = Time.time + BiteEvery;
        Hurt(lane, playerBite, zombieBite);
    }

    // The host: it goes up by itself in its step; a wipe sets it up again.
    private void HostTick()
    {
        int wipes = CampaignRun.Wipes;
        if (wipes != wipesSeen)
        {
            wipesSeen = wipes;
            runningSince = -1f;
            if (State != Standing) Set(Standing);
            return;
        }
        bool running = CampaignRun.IsRunning(step);
        if (!running) { runningSince = -1f; return; }
        if (runningSince < 0f) runningSince = Time.time;
        if (State == Standing && Time.time - runningSince >= goOff) SetOff(null);
    }

    internal override void Show(byte state, bool fresh)
    {
        if (state == Standing)
        {
            Fire(false);
            lighter = null;
            return;
        }
        if (state == Burnt) { Fire(false); return; }
        litAt = Time.time;
        nextBite = Time.time + BiteEvery;
        Fire(true);
        if (!fresh) return;
        if (blast != null) blast.Fire();
        // The blast itself, once: the zombies round it on the setter's PC (the host's when it went by itself), each
        // player on their own.
        Hurt(new Bounds(transform.position, Vector3.one * blastRadius * 2f), blastPlayer, blastZombie, blastRadius);
    }

    // Damage to this PC's own player in the box, and to the zombies in it on the PC whose they are to burn.
    private void Hurt(Bounds box, float player, float zombie, float radius = 0f)
    {
        var mine = Mine;
        if (mine != null && mine.DamageReceiver != null && player > 0f && Inside(box, mine.transform.position, radius))
            mine.DamageReceiver.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), player, DamageKind.Attack, mine.transform.position + Vector3.up,
                Vector3.up, gameObject, method: DamageMethod.Blast));
        bool mineToBurn = lighter != null ? lighter.IsLocal : MapGimmicks.IsHost;
        if (!mineToBurn || zombie <= 0f) return;
        caught.Clear();
        foreach (var enemy in EnemyHealth.Living)
            if (enemy != null && enemy.IsAlive && Inside(box, enemy.transform.position, radius)) caught.Add(enemy);
        var source = lighter != null ? lighter.gameObject : gameObject;
        foreach (var enemy in caught)
            if (enemy != null && enemy.IsAlive)
                enemy.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), zombie, DamageKind.Attack, enemy.transform.position + Vector3.up,
                    Vector3.up, source, method: DamageMethod.Blast, origin: box.center));
        caught.Clear();
    }

    // In the box (a lane), or within radius of its middle when one is given (the blast).
    private bool Inside(Bounds box, Vector3 at, float radius)
        => radius > 0f ? (at - transform.position).sqrMagnitude <= radius * radius : box.Contains(at + Vector3.up * .3f);

    private void Fire(bool on)
    {
        if (on && fire == null) fire = new GimmickFireLook(transform, lane);
        fire?.Set(on);
        if (block != null) block.enabled = on;
    }
}
