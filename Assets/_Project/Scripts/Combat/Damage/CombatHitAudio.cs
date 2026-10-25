using System.Collections.Generic;
using UnityEngine;

public enum HitSound { None, HeadHit, BodyHit, HeadshotKill, BodyshotKill, CloseKill, PlayerHit, PlayerFall }
/// <summary>Per-region feedback switch from the reference script: 0 sound and effects, 1 effects only, 2 off.</summary>
public enum HitFeedbackLevel { SoundAndEffects, EffectsOnly, Off }

/// <summary>
/// Layered hit and kill sounds. Each event stacks short clips at fixed delays, so a kill reads heavier than a hit.
/// Hits dealt by the player (and hits on the player) play 2D; anything else plays 3D at the hit point.
/// Created on the first hit during Play; no scene setup. Clips and tuning live in Resources/Combat/Feedback.
/// </summary>
[DisallowMultipleComponent]
public sealed class CombatHitAudio : MonoBehaviour
{
    private enum Layer { ImpactBullet, ImpactPierce, FleshBreak, FleshSplat, BodyThud, BoneCrack, Tackle, Punch, MeleeHit }

    // Seconds after the hit. Every event starts with a layer at 0: unlike L4D2 there is no engine hit sound to lead,
    // and impact_bullet already peaks ~0.1 s into the clip, so the reference script's 0.095-0.295 s delays felt late.
    private static readonly (Layer layer, float delay)[]
        HeadHit = { (Layer.ImpactPierce, 0f), (Layer.ImpactBullet, 0f) },
        BodyHit = { (Layer.BodyThud, 0f) },
        HeadshotKill = { (Layer.FleshBreak, 0f), (Layer.ImpactPierce, 0f), (Layer.ImpactBullet, .03f), (Layer.BoneCrack, .1f) },
        BodyshotKill = { (Layer.BodyThud, 0f), (Layer.ImpactBullet, 0f) },
        CloseKill = { (Layer.FleshSplat, 0f), (Layer.BoneCrack, 0f) },
        SkeetBonus = { (Layer.Tackle, 0f) },
        PlayerHit = { (Layer.Punch, 0f) },
        PlayerFall = { (Layer.MeleeHit, 0f), (Layer.Tackle, 0f) };

    private const int SpatialVoices = 8;
    private struct Pending { public Layer layer; public float remaining, volume; public bool spatial; public Vector3 point; }

    private static CombatHitAudio instance;
    public static CombatHitAudio Instance => instance;
    private readonly List<Pending> pending = new List<Pending>();
    private readonly Dictionary<Layer, AudioClip> previous = new Dictionary<Layer, AudioClip>();
    private readonly AudioSource[] voices = new AudioSource[SpatialVoices];
    private AudioSource flat;
    private int nextVoice;
    public HitSound LastSound { get; private set; }
    public int PlayCount { get; private set; }
    public int PendingCount => pending.Count;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Subscribe()
    {
        CombatDamageEvents.Damaged -= OnDamaged;
        CombatDamageEvents.Damaged += OnDamaged;
    }

    private static void OnDamaged(CombatDamageReport report)
    {
        var hit = report.Hit;
        var settings = CombatFeedbackSettings.Instance;
        if (!Application.isPlaying || report.Target == null || settings == null || hit.Kind == DamageKind.Fall) return;
        bool kill = report.IsKillingBlow;
        HitSound sound; HitFeedbackLevel level;
        if (hit.Method == DamageMethod.Bullet)
        {
            // Bullets are critical only on headshots (AttackTrace).
            bool head = hit.IsCritical;
            sound = head ? (kill ? HitSound.HeadshotKill : HitSound.HeadHit) : (kill ? HitSound.BodyshotKill : HitSound.BodyHit);
            level = head ? settings.headshotFeedback : settings.bodyshotFeedback;
        }
        else if (kill) { sound = HitSound.CloseKill; level = settings.bodyshotFeedback; } // shove, melee, execution
        else return;
        if (level != HitFeedbackLevel.SoundAndEffects) return;
        // Skeet: killed during its attack wind-up, like a hunter shot out of a pounce.
        bool skeet = kill && report.Target.TryGetComponent(out EnemyCombatAI ai) && ai.IsPreparingAttack;
        bool byPlayer = hit.Source != null && hit.Source.GetComponentInParent<PlayerMovement>() != null;
        Get().Queue(sound, skeet, !byPlayer, hit.Point, settings.hitSoundVolume);
    }

    /// <summary>Called by PlayerDamageReceiver after a hit actually lands on the player.</summary>
    public static void PlayPlayerHit(DamageHit hit)
    {
        var settings = CombatFeedbackSettings.Instance;
        if (!Application.isPlaying || settings == null) return;
        Get().Queue(hit.Kind == DamageKind.Fall ? HitSound.PlayerFall : HitSound.PlayerHit, false, false, hit.Point, settings.playerHitVolume);
    }

    private static CombatHitAudio Get()
    {
        if (instance == null) instance = new GameObject("Combat Hit Audio").AddComponent<CombatHitAudio>();
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        flat = gameObject.AddComponent<AudioSource>();
        flat.playOnAwake = false; flat.spatialBlend = 0f;
        for (int i = 0; i < voices.Length; i++)
        {
            var voice = new GameObject("Hit Voice " + i).AddComponent<AudioSource>();
            voice.transform.SetParent(transform, false);
            voice.playOnAwake = false; voice.spatialBlend = 1f;
            voice.rolloffMode = AudioRolloffMode.Linear; voice.minDistance = 2f;
            voices[i] = voice;
        }
    }

    private void OnDestroy() { if (instance == this) instance = null; }

    private void OnDisable()
    {
        pending.Clear();
        if (flat != null) flat.Stop();
        foreach (var voice in voices) if (voice != null) voice.Stop();
    }

    private void Queue(HitSound sound, bool skeet, bool spatial, Vector3 point, float volume)
    {
        LastSound = sound; PlayCount++;
        foreach (var step in Steps(sound)) Add(step, spatial, point, volume);
        if (skeet) foreach (var step in SkeetBonus) Add(step, spatial, point, volume);
    }

    private void Add((Layer layer, float delay) step, bool spatial, Vector3 point, float volume)
    {
        var item = new Pending { layer = step.layer, remaining = step.delay, volume = volume, spatial = spatial, point = point };
        if (item.remaining <= 0f) Emit(item); else pending.Add(item);
    }

    private void Update()
    {
        // Unscaled so perfect-defense slow motion cannot stretch a 0.3 s kill stack; a paused game holds it.
        if (Time.timeScale <= 0f) return;
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            var item = pending[i];
            item.remaining -= Time.unscaledDeltaTime;
            if (item.remaining > 0f) { pending[i] = item; continue; }
            pending.RemoveAt(i); Emit(item);
        }
    }

    private void Emit(Pending item)
    {
        var settings = CombatFeedbackSettings.Instance;
        var clip = settings != null ? Pick(item.layer, Clips(settings, item.layer)) : null;
        if (clip == null) return;
        float volume = Mathf.Clamp01(item.volume);
        if (!item.spatial) { flat.PlayOneShot(clip, volume); return; }
        var voice = voices[nextVoice];
        nextVoice = (nextVoice + 1) % voices.Length;
        voice.transform.position = item.point;
        voice.maxDistance = Mathf.Max(voice.minDistance + .1f, settings.hitSoundDistance);
        voice.PlayOneShot(clip, volume);
    }

    private static (Layer layer, float delay)[] Steps(HitSound sound)
    {
        switch (sound)
        {
            case HitSound.HeadHit: return HeadHit;
            case HitSound.BodyHit: return BodyHit;
            case HitSound.HeadshotKill: return HeadshotKill;
            case HitSound.BodyshotKill: return BodyshotKill;
            case HitSound.CloseKill: return CloseKill;
            case HitSound.PlayerHit: return PlayerHit;
            case HitSound.PlayerFall: return PlayerFall;
            default: return System.Array.Empty<(Layer, float)>();
        }
    }

    private static AudioClip[] Clips(CombatFeedbackSettings settings, Layer layer)
    {
        switch (layer)
        {
            case Layer.ImpactBullet: return settings.impactBulletSounds;
            case Layer.ImpactPierce: return settings.impactPierceSounds;
            case Layer.FleshBreak: return settings.fleshBreakSounds;
            case Layer.FleshSplat: return settings.fleshSplatSounds;
            case Layer.BodyThud: return settings.bodyThudSounds;
            case Layer.BoneCrack: return settings.boneCrackSounds;
            case Layer.Tackle: return settings.tackleSounds;
            case Layer.Punch: return settings.punchSounds;
            default: return settings.shoveEnemySounds;
        }
    }

    /// <summary>Random clip that differs from this layer's previous pick whenever an alternative exists.</summary>
    private AudioClip Pick(Layer layer, AudioClip[] clips)
    {
        if (clips == null || clips.Length == 0) return null;
        previous.TryGetValue(layer, out AudioClip last);
        int alternatives = 0; AudioClip fallback = null;
        foreach (var clip in clips)
        {
            if (clip == null) continue;
            fallback = clip;
            if (clip != last) alternatives++;
        }
        AudioClip chosen = fallback;
        if (alternatives > 0)
        {
            int choice = Random.Range(0, alternatives);
            foreach (var clip in clips)
                if (clip != null && clip != last && choice-- == 0) { chosen = clip; break; }
        }
        previous[layer] = chosen;
        return chosen;
    }
}
