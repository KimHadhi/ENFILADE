using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What a hit the local player lands shows and sounds like, once GunFeelDirector has decided it is theirs: the hit marker
/// (its kind, sized by the share of health the hit took), a dry tick on every hit, a metallic dink on top for headshots,
/// and a thump on kills that climbs a little with each kill of a streak (GunFeel → Kill Streak Pitch Step). A hit that
/// breaks posture adds the break marker and a bone crack. Sounds come from the HitConfirm sound bank
/// (Art/Audio/HitConfirm/Body, Head, Kill, Break; volume and pitch in Sounds.csv), so a sound is changed by swapping files;
/// while the bank is missing a kill falls back to GunFeel's kill sounds.
/// </summary>
public static class HitConfirm
{
    private const string BankId = "HitConfirm";
    // A break this soon after the player's hit counts as theirs; a guest hears its copy's break from the host a bit later.
    private const float BreakWindow = .25f;
    private static readonly Dictionary<MeleeCombatTarget, float> recentHits = new Dictionary<MeleeCombatTarget, float>();
    private static readonly List<MeleeCombatTarget> stale = new List<MeleeCombatTarget>();
    private static SoundBank bank;
    private static bool bankLoaded;
    private static int streak;
    private static float lastKillTime = -99f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        recentHits.Clear();
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

    /// <summary>A hit the local player landed.</summary>
    public static void Landed(CombatDamageReport report)
    {
        bool kill = report.IsKillingBlow, head = report.IsCritical;
        float share = report.MaximumHealth > 0f ? report.Applied / report.MaximumHealth : 1f;
        HitMarkerHUD.Show(kill ? (head ? HitMarkerKind.HeadKill : HitMarkerKind.BodyKill) : head ? HitMarkerKind.Head : HitMarkerKind.Body, share);
        Remember(report.Target);
        Play("Body");
        if (head) Play("Head");
        if (kill) Kill(head);
    }

    private static void Kill(bool head)
    {
        var settings = GunFeelSettings.Instance;
        // The kill feed's streak window, so the climb follows the same kills as its numerals.
        float now = Time.unscaledTime;
        streak = now - lastKillTime <= CombatFeedHUD.StreakWindow ? streak + 1 : 1;
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
        if (Time.unscaledTime - at > BreakWindow) return;
        HitMarkerHUD.ShowBreak();
        Play("Break");
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
