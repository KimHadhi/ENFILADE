using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A filling station's pump left to spill (the map gimmick card "주유소 기름 함정", after PAYDAY 2's Firestarter): [F] opens
/// its valve and fuel spreads over the forecourt round it; a shot into the spill or a blast near it lights it, and for its
/// seconds everything standing in the fire burns. Lure them into it first, and it takes a crowd. The one who lit it burns
/// the zombies on their own PC (their kills, the actor decides); each PC burns its own player if they stand in it.
/// Never used, nothing is lost. States: 0 shut, 1 spilled, 2 burning, 3 burnt out.
/// </summary>
public sealed class GimmickFuelTrap : GimmickSwitch, IGimmickShot
{
    private const byte Shut = 0, Spilled = 1, Burning = 2, Burnt = 3;
    // How long the spill takes to spread before it takes a light (s), and how often the fire bites (s).
    private const float Spread = 2f, BiteEvery = .5f;

    [SerializeField, Tooltip("Where the fuel spreads (world box): what stands in it burns.")]
    private Bounds pool;
    [SerializeField, Min(1f), Tooltip("How long it burns (s).")]
    private float seconds = 15f;
    [SerializeField, Min(0f), Tooltip("Damage a bite (every half second) to a zombie standing in it.")]
    private float zombieBite = 45f;
    [SerializeField, Min(0f), Tooltip("Damage a bite to a player standing in it.")]
    private float playerBite = 8f;
    [SerializeField, Tooltip("The spill's look (a dark sheen on the ground), shown once the valve is open; may be empty.")]
    private GameObject spill;

    private static readonly List<EnemyHealth> burning = new List<EnemyHealth>();
    private float spilledAt, litAt, nextBite;
    private PlayerMovement lighter;
    private GimmickFireLook fire;

    internal override string Offer(PlayerMovement mine) => State == Shut ? Texts.Get("gimmick.fuel.open") : null;

    internal override int Use(PlayerMovement mine) => State == Shut ? Spilled : -1;

    internal override bool Accepts(byte now, byte proposed) => proposed > now;

    public void Shot(PlayerMovement by, in DamageHit hit) => Ignite(by);

    // Lit by this PC's own player (a shot into it, a blast near it): their fire.
    private void Ignite(PlayerMovement by)
    {
        if (State != Spilled || Time.time - spilledAt < Spread) return;
        lighter = by;
        Set(Burning);
    }

    /// <summary>A blast on this PC from its own player: every spill in reach catches.</summary>
    internal static void Blast(Vector3 at, float radius, PlayerMovement by)
    {
        var map = MapGimmicks.Active;
        if (map == null) return;
        foreach (var each in map.Switches)
            if (each is GimmickFuelTrap trap && trap.pool.SqrDistance(at) <= radius * radius) trap.Ignite(by);
    }

    internal override void Show(byte state, bool fresh)
    {
        if (state >= Spilled && spill != null) spill.SetActive(state != Burnt);
        if (state == Spilled)
        {
            spilledAt = Time.time;
            if (fresh) GimmickSounds.PlayAt(GimmickSounds.Cue.Hiss, transform.position, .7f, 2f, 15f);
        }
        else if (state == Burning)
        {
            litAt = Time.time;
            nextBite = 0f;
            Fire(true);
            if (fresh) GimmickSounds.PlayAt(GimmickSounds.Cue.Crash, pool.center, .8f, 6f, 60f);
        }
        else if (state == Burnt) Fire(false);
    }

    internal override void Tick(float dt, bool host)
    {
        if (State != Burning) return;
        float on = Time.time - litAt;
        fire?.Tick();
        if (on >= seconds)
        {
            Fire(false);
            if (host) Set(Burnt);
            return;
        }
        if (Time.time < nextBite) return;
        nextBite = Time.time + BiteEvery;
        // This PC's own player, if they stand in it.
        var mine = Mine;
        if (mine != null && mine.DamageReceiver != null && pool.Contains(mine.transform.position + Vector3.up * .3f))
            mine.DamageReceiver.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), playerBite, DamageKind.Attack, mine.transform.position + Vector3.up,
                Vector3.up, gameObject, method: DamageMethod.Blast));
        // The zombies in it, on the lighter's PC.
        if (lighter == null || !lighter.IsLocal) return;
        burning.Clear();
        foreach (var enemy in EnemyHealth.Living)
            if (enemy != null && enemy.IsAlive && pool.Contains(enemy.transform.position + Vector3.up * .3f)) burning.Add(enemy);
        foreach (var enemy in burning)
            if (enemy != null && enemy.IsAlive)
                enemy.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), zombieBite, DamageKind.Attack, enemy.transform.position + Vector3.up,
                    Vector3.up, lighter.gameObject, method: DamageMethod.Blast, origin: pool.center));
        burning.Clear();
    }

    // The fire over the spill (GimmickFireLook), built the first time it is lit.
    private void Fire(bool on)
    {
        if (on && fire == null) fire = new GimmickFireLook(transform, pool);
        fire?.Set(on);
    }
}
