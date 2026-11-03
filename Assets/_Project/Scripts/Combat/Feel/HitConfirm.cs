using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What the hits the local player lands show and sound like, once GunFeelDirector has decided they are theirs. The hits
/// of one frame (a shotgun's pellets, a swing through a crowd) are confirmed once, at the end of the frame: one hit
/// marker sized by the share of health they took, one dry tick, a metallic dink on top when enough of them were
/// headshots (GunFeel → Headshot Hit Share, Overwatch's pellet rule), and a thump for kills that climbs a little with each
/// kill of a streak (GunFeel → Kill Streak Pitch Step). A hit that breaks posture adds the break marker and a bone crack.
/// Sounds come from the HitConfirm sound bank (Art/Audio/HitConfirm/Body, Head, Kill, Break; volume and pitch in
/// Sounds.csv), so a sound is changed by swapping files; while the bank is missing a kill falls back to GunFeel's kill
/// sounds. Created on first use during Play.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(10000)]
public sealed class HitConfirm : MonoBehaviour
{
    private const string BankId = "HitConfirm";
    // A break this soon after the player's hit counts as theirs; a guest hears its copy's break from the host a bit later.
    private const float BreakWindow = .25f;
    private static readonly Dictionary<MeleeCombatTarget, float> recentHits = new Dictionary<MeleeCombatTarget, float>();
    private static readonly List<MeleeCombatTarget> stale = new List<MeleeCombatTarget>();
    private static HitConfirm instance;
    private static SoundBank bank;
    private static bool bankLoaded;
    private static int streak;
    private static float lastKillTime = -99f;
    private int hits, heads, kills, headKills, breaks;
    private float share;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        recentHits.Clear();
        instance = null;
        bank = null; bankLoaded = false;
        streak = 0; lastKillTime = -99f;
    }

    // After MeleeCombatTarget clears its event at subsystem registration.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Subscribe()
    {
        MeleeCombatTarget.AnyStaggered -= OnStaggered;
        MeleeCombatTarget.AnyStaggered += OnStaggered;
    }

    private static HitConfirm Get()
    {
        if (instance == null) instance = FeelAssets.CreateRuntimeRoot("Hit Confirm", groupName: "Audio").AddComponent<HitConfirm>();
        return instance;
    }

    /// <summary>A hit the local player landed; confirmed with the rest of this frame's hits.</summary>
    public static void Landed(CombatDamageReport report)
    {
        if (!Application.isPlaying) return;
        var self = Get();
        self.hits++;
        if (report.IsCritical) self.heads++;
        if (report.IsKillingBlow)
        {
            self.kills++;
            if (report.IsCritical) self.headKills++;
        }
        self.share += report.MaximumHealth > 0f ? report.Applied / report.MaximumHealth : 1f;
        Remember(report.Target);
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(this); return; }
        instance = this;
    }

    private void OnDestroy() { if (instance == this) instance = null; }

    private void LateUpdate()
    {
        if (hits > 0) Confirm();
        if (breaks > 0)
        {
            HitMarkerHUD.ShowBreak();
            Play("Break");
            breaks = 0;
        }
    }

    private void Confirm()
    {
        var settings = GunFeelSettings.Instance;
        bool head = heads > 0 && heads >= settings.headshotHitShare * hits, kill = kills > 0, headKill = headKills > 0;
        HitMarkerHUD.Show(kill ? (headKill ? HitMarkerKind.HeadKill : HitMarkerKind.BodyKill) : head ? HitMarkerKind.Head : HitMarkerKind.Body,
            Mathf.Min(1f, share));
        Play("Body");
        if (head || headKill) Play("Head");
        if (kill) Kill(settings, headKill, kills);
        hits = heads = kills = headKills = 0;
        share = 0f;
    }

    private static void Kill(GunFeelSettings settings, bool head, int count)
    {
        // The kill feed's streak window, so the climb follows the same kills as its numerals.
        float now = Time.unscaledTime;
        streak = now - lastKillTime <= CombatFeedHUD.StreakWindow ? streak + count : count;
        lastKillTime = now;
        float climb = 1f + Mathf.Min(streak - 1, settings.killStreakSteps) * settings.killStreakPitchStep;
        if (Play("Kill", climb)) return;
        FeelAudio.Play(head ? FeelSound.HeadshotKill : FeelSound.Kill, head ? settings.headshotKillSounds : settings.killSounds,
            settings.killVolume, Random.Range(.97f, 1.03f) * climb);
    }

    private static void Remember(Component target)
    {
        if (target == null || !target.TryGetComponent(out MeleeCombatTarget posture)) return;
        float now = Time.unscaledTime;
        if (recentHits.Count >= 16)
        {
            stale.Clear();
            foreach (var pair in recentHits)
                if (pair.Key == null || now - pair.Value > BreakWindow) stale.Add(pair.Key);
            foreach (var key in stale) recentHits.Remove(key);
        }
        recentHits[posture] = now;
    }

    private static void OnStaggered(MeleeCombatTarget target)
    {
        if (!Application.isPlaying || target == null || !recentHits.TryGetValue(target, out float at)) return;
        recentHits.Remove(target);
        if (Time.unscaledTime - at <= BreakWindow) Get().breaks++;
    }

    // One clip of the key at its Sounds.csv volume and pitch; false when the bank has none.
    private static bool Play(string key, float pitchScale = 1f)
    {
        if (!bankLoaded) { bank = SoundBank.Load(BankId); bankLoaded = true; }
        var clip = bank != null ? bank.Pick(key) : null;
        if (clip == null) return false;
        var tuning = bank.SettingsOf(key);
        FeelAudio.PlayClip(clip, tuning.Volume, tuning.RandomPitch() * pitchScale);
        return true;
    }
}
