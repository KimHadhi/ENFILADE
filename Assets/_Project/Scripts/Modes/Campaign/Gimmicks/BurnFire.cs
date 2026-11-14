using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The fire on a pile that only fire clears (the map gimmick card "불로만 치우는 바리케이드", after Guts &amp; Blackpowder's
/// Leipzig): it burns while its step's hold has fuel (CampaignRun.Burning: each can poured on the pile keeps it going a
/// while, and the hold's clock runs only while it burns), on every PC alike, and the zombies that come through it burn,
/// the host's doing (no one player's kills: the fire is the pile's). The pile itself sinks as the hold goes (a
/// CampaignGate on the same step).
/// </summary>
[DisallowMultipleComponent]
public sealed class BurnFire : MonoBehaviour
{
    private const float BiteEvery = .5f;

    [SerializeField, Tooltip("The hold that burns it (CampaignSteps id, a Hold with burn).")]
    private string step = "";
    [SerializeField, Tooltip("Where it burns (world box): zombies in it are hurt.")]
    private Bounds fire;
    [SerializeField, Min(0f), Tooltip("Damage a bite (every half second) to a zombie in it.")]
    private float zombieBite = 40f;

    private static readonly List<EnemyHealth> inside = new List<EnemyHealth>();
    private GimmickFireLook look;
    private float nextBite;

    private void Update()
    {
        bool on = CampaignRun.IsRunning(step) && CampaignRun.Burning;
        if (on && look == null) look = new GimmickFireLook(transform, fire);
        look?.Set(on);
        look?.Tick();
        if (!on || !MapGimmicks.IsHost || Time.time < nextBite) return;
        nextBite = Time.time + BiteEvery;
        inside.Clear();
        foreach (var enemy in EnemyHealth.Living)
            if (enemy != null && enemy.IsAlive && fire.Contains(enemy.transform.position + Vector3.up * .3f)) inside.Add(enemy);
        foreach (var enemy in inside)
            if (enemy != null && enemy.IsAlive)
                enemy.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), zombieBite, DamageKind.Attack, enemy.transform.position + Vector3.up,
                    Vector3.up, gameObject, method: DamageMethod.Blast, origin: fire.center));
        inside.Clear();
    }
}
