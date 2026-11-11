using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Sounds of bodies breaking that no other script plays, each a Tables/SoundCues row played where it happens:
/// - Execution/Behind, Execution/Front, Execution/Survived: an execution's finishing blow (PlayerExecution). From behind
///   the neck snaps (a stealth takedown always comes from behind an unaware zombie); from the front the skull cracks; one
///   that lives through it (a brute loses only a share of its health) has its arm broken instead, so the blow still lands
///   as something even when it can't kill.
/// - Gore/HeadBurst: a killing blow to the head, which bursts it (the rule ZombieGore.Judge uses).
/// - Gore/LimbOff: an arm or leg torn off a living zombie (ZombieGore.Crippled).
/// - ZombieHit/Fist: meat under a player's punch landing on a zombie, beneath the punch's own hit (PlayerFistAudio).
/// CombatDamageEvents and ZombieGore.Crippled are raised once on every PC for every hit, so everyone near hears these, in
/// 3D, without network code of their own. None of them makes Noise: an execution should end quietly, not wake the zombie
/// beside it. The clips are Zombie bank keys (Art/Audio/Enemies/Zombie/Bone, Gore, Hit): Meaty Melee SFX and Fleshy CI Hit
/// Sounds (L4D2 workshop 3660295000, 3725589072); their volumes are Tables/Sounds rows. SoundPreview (/sound) plays any
/// cue through Play.
/// </summary>
[DisallowMultipleComponent]
public sealed class BodyBreakSounds : MonoBehaviour
{
    public const string ExecutionBehind = "Execution/Behind", ExecutionFront = "Execution/Front", ExecutionSurvived = "Execution/Survived";
    public const string HeadBurst = "Gore/HeadBurst", LimbOff = "Gore/LimbOff", FistOnZombie = "ZombieHit/Fist";
    // How far these carry (m): a body breaking is heard across a room, a punch's meat only up close.
    private const float BreakReach = 22f, FistReach = 12f;
    // A player's melee hit lands within arm's (or leg's) length; farther ones with the same method are blasts credited to
    // the player who set them off (a shot exploder), which shouldn't sound like a fist.
    private const float MeleeReach = 2.5f;
    // Where on a standing zombie its neck and its limbs are, above its feet (m), for a sound that has no hit point of its own.
    private const float NeckHeight = 1.45f, ArmHeight = 1.3f, LegHeight = .55f;

    private static BodyBreakSounds instance;
    private readonly List<Pending> pending = new List<Pending>();
    private readonly Dictionary<string, SoundBank> banks = new Dictionary<string, SoundBank>(StringComparer.OrdinalIgnoreCase);

    // A cue's later part (a "+0.1" delay in its row), played where and as its cue was.
    private struct Pending
    {
        public float At, Reach;
        public SoundCues.Item Item;
        public Vector3? Point;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget() => instance = null;

    // After SubsystemRegistration, where CombatDamageEvents and ZombieGore clear their events (the same point ZombieGore
    // itself subscribes at).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        CombatDamageEvents.Damaged -= OnDamaged;
        CombatDamageEvents.Damaged += OnDamaged;
        ZombieGore.Crippled -= OnCrippled;
        ZombieGore.Crippled += OnCrippled;
    }

    /// <summary>
    /// Plays a SoundCues row at a world point, or flat (no point) for a preview. False when there is no such row. Delayed
    /// parts follow on their own; the row's noise is not made.
    /// </summary>
    public static bool Play(string cueId, Vector3? point, float reach = BreakReach)
    {
        if (!Application.isPlaying || !SoundCues.TryFind(cueId, out var cue)) return false;
        var self = Instance();
        foreach (var item in cue.Items)
        {
            if (item.Delay > 0f) self.pending.Add(new Pending { At = Time.time + item.Delay, Item = item, Point = point, Reach = reach });
            else self.PlayItem(item, point, reach);
        }
        return true;
    }

    private static BodyBreakSounds Instance()
    {
        if (instance == null) instance = FeelAssets.CreateRuntimeRoot("Body Break Sounds", groupName: "Audio").AddComponent<BodyBreakSounds>();
        return instance;
    }

    private static void OnDamaged(CombatDamageReport report)
    {
        // Zombies only: practice targets and range dummies report hits too, and they have no neck to snap.
        if (!(report.Target is EnemyHealth enemy) || enemy == null) return;
        var hit = report.Hit;
        if (hit.Kind == DamageKind.Execution)
        {
            string cue = report.HealthAfter > 0f ? ExecutionSurvived : FromBehind(enemy, hit) ? ExecutionBehind : ExecutionFront;
            Play(cue, enemy.transform.position + Vector3.up * NeckHeight);
            return;
        }
        if (report.IsKillingBlow && (hit.Part == BodyPart.Head || (hit.IsCritical && hit.Method == DamageMethod.Bullet)) && Dressed(enemy))
        {
            Play(HeadBurst, hit.Point);
            return;
        }
        if (hit.Kind == DamageKind.Attack && hit.Method == DamageMethod.Other && ByPlayerUpClose(hit)) Play(FistOnZombie, hit.Point, FistReach);
    }

    private static void OnCrippled(EnemyHealth enemy, BodyPart part)
    {
        if (enemy == null) return;
        Play(LimbOff, enemy.transform.position + Vector3.up * (BodyParts.IsLeg(part) ? LegHeight : ArmHeight));
    }

    // A stealth takedown is on a zombie that never noticed anyone; any other execution counts as from behind when the
    // executor looked along the zombie's own facing as the blow fell.
    private static bool FromBehind(EnemyHealth enemy, in DamageHit hit)
    {
        if (enemy.TryGetComponent(out ZombieMind mind) && mind.Unaware) return true;
        Vector3 facing = Vector3.ProjectOnPlane(enemy.transform.forward, Vector3.up), along = Vector3.ProjectOnPlane(hit.Direction, Vector3.up);
        return facing.sqrMagnitude > 1e-4f && along.sqrMagnitude > 1e-4f && Vector3.Dot(facing.normalized, along.normalized) > .2f;
    }

    // Only a zombie wearing its body has a head to burst (a bare capsule shows no gore either).
    private static bool Dressed(EnemyHealth enemy) => enemy.TryGetComponent(out ZombieBody body) && body.Model != null;

    private static bool ByPlayerUpClose(in DamageHit hit)
    {
        var source = hit.Source;
        if (source == null || source.GetComponentInParent<PlayerMovement>() == null) return false;
        return (hit.Point - source.transform.position).sqrMagnitude <= MeleeReach * MeleeReach;
    }

    private void PlayItem(SoundCues.Item item, Vector3? point, float reach)
    {
        var bank = Bank(item.Bank);
        var clip = bank != null ? bank.Pick(item.Key) : null;
        if (clip == null) return;
        var settings = bank.SettingsOf(item.Key);
        float volume = item.Volume * settings.Volume;
        if (point.HasValue) FeelAudio.PlayClipAt(clip, point.Value, volume, settings.RandomPitch(), reach);
        else FeelAudio.PlayClip(clip, volume, settings.RandomPitch());
    }

    private SoundBank Bank(string id)
    {
        if (banks.TryGetValue(id, out var bank) && bank != null) return bank;
        return banks[id] = SoundBank.Load(id);
    }

    private void Update()
    {
        if (pending.Count == 0) return;
        float now = Time.time;
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            var due = pending[i];
            if (due.At > now) continue;
            pending.RemoveAt(i);
            PlayItem(due.Item, due.Point, due.Reach);
        }
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }
}
