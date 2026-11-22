using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What the hits the local player lands show and sound like, once GunFeelDirector has decided they are theirs. A hit and
/// a kill go to the hit feedback addon (HudAddons.HitFeedbackModule: the Black Ops VI Hitmarker's marker, its three-layer
/// sounds and its score popup, and Cool Killchains' skulls), which shows a frame's hits once. Ours stay only where the
/// addon has no element: a shot that reached a target and did nothing (a guard, a parry, invulnerability) gives its dull
/// sound (Blocked) unless a hit of the same frame did harm, and a hit that breaks posture adds the break burst under the
/// aim (PostureBreakHUD) and its sound (Break). Those two sounds come from the HitConfirm sound bank
/// (Art/Audio/HitConfirm/&lt;key&gt;; volume and pitch in Sounds.csv), so a sound is changed by swapping files. Created on
/// first use during Play.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.HitConfirm)]
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
    // This frame's hits (any of them keeps the blocked sound quiet), posture breaks and shots that did nothing.
    private int hits, breaks, blocked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        recentHits.Clear();
        instance = null;
        bank = null; bankLoaded = false;
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

    /// <summary>
    /// A hit the local player landed. marks: what a killing blow happened under (KillConditions), which picks the addon's
    /// kill row and skull. A hit on something that sounds its own hits (IOwnHitFeedback: the steel targets) is no hit on
    /// flesh (the addon's prop badge and item sound); fire is the flame badge. A practice target's kill shows and sounds
    /// but counts neither for the score nor the chain, as the game counts it in neither.
    /// </summary>
    public static void Landed(CombatDamageReport report, KillMarks marks = KillMarks.None)
    {
        if (!Application.isPlaying) return;
        bool flesh = !(report.Target is IOwnHitFeedback);
        if (report.IsKillingBlow) HudAddons.HitFeedbackModule.Kill(marks, report.IsCritical, flesh, !report.IsPractice);
        else if (report.Applied > 0f) HudAddons.HitFeedbackModule.Hit(report.IsCritical, flesh, (report.Hit.Marks & KillMarks.Fire) != 0);
        var self = Get();
        self.hits++;
        Remember(report.Target);
    }

    /// <summary>
    /// A shot of the local player's that burst a dead body's head (ZombieGore: a zombie's corpse, or the body a calm zombie
    /// was eating): it shows and sounds as a headshot on a living head does (the user, 2026-11-15: "히트 마커는 넣어 줘").
    /// Nothing more follows it: no kill, number, kill screen or reward.
    /// </summary>
    public static void LandedOnDead()
    {
        if (!Application.isPlaying) return;
        HudAddons.HitFeedbackModule.Hit(true, true, false);
        Get().hits++;
    }

    /// <summary>
    /// A kill of this PC's player that the host judged (KillMarks.JudgedByHost: their fuel can's blast or fire, a can they
    /// threw), heard as the host announces it (PlayerNetworkScore): shown and sounded as their own kill. Their PC never saw
    /// that hit land, so nothing else would show it there.
    /// </summary>
    public static void HostKill(KillMarks marks)
    {
        if (!Application.isPlaying) return;
        HudAddons.HitFeedbackModule.Kill(marks, (marks & KillMarks.Headshot) != 0);
        Get().hits++;
    }

    /// <summary>
    /// A shot of the local player's that reached a target and did nothing (a guard, a parry, invulnerability): its dull
    /// sound, unless a hit of the same frame did harm (a shotgun's other pellets), which speaks instead.
    /// </summary>
    public static void Blocked()
    {
        if (!Application.isPlaying) return;
        Get().blocked++;
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(this); return; }
        instance = this;
    }

    private void OnDestroy() { if (instance == this) instance = null; }

    private void LateUpdate()
    {
        if (hits == 0 && blocked > 0) Play("Blocked");
        hits = blocked = 0;
        if (breaks > 0)
        {
            PostureBreakHUD.Show();
            Play("Break");
            breaks = 0;
        }
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

    // One clip of the key at its Sounds.csv volume and pitch; nothing while the bank has none.
    private static void Play(string key)
    {
        if (!bankLoaded) { bank = SoundBank.Load(BankId); bankLoaded = true; }
        var clip = bank != null ? bank.Pick(key) : null;
        if (clip == null) return;
        var tuning = bank.SettingsOf(key);
        FeelAudio.PlayClip(clip, tuning.Volume, tuning.RandomPitch());
    }
}
