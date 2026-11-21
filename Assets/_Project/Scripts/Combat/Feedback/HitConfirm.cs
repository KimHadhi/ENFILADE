using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What the hits the local player lands show and sound like, once GunFeelDirector has decided they are theirs. The hits
/// of one frame (a shotgun's pellets, a swing through a crowd) are confirmed once, at the end of the frame: one hit
/// marker sized by the share of health they took, and one confirm sound for the marker's kind, as the marker has one
/// look: a hit (Body), a headshot when enough of them were headshots (Head; GunFeel → Headshot Hit Share, Overwatch's
/// pellet rule), a kill that climbs a little with each kill of a streak (Kill; GunFeel → Kill Streak Pitch Step) with
/// the head's bright double tick over it for a headshot kill (HeadKill), or, when nothing of the frame did any harm, the
/// grey marker and its dull sound (Blocked). A kill also shows its picture under the aim point (KillIcons, chosen by its
/// KillMarks). A hit that breaks posture adds the break marker, its picture and its sound (Break). When any of them came
/// through or off something on the way (DamageHit.Pierced), the marker shows its pierced echo.
/// Each sound is a whole marker sound (the user's set of 2026-11-21: mwhitmarker, headmarker, newkillmarker, killmarker,
/// hitmarkfail, breakarmorr), so they are not stacked as the old dry tick under a dink was. They come from the HitConfirm
/// sound bank (Art/Audio/HitConfirm/&lt;key&gt;; volume and pitch in Sounds.csv), so a sound is changed by swapping files;
/// the earlier ones wait in BodyOld, HeadPing, KillOld and BreakOld. While the bank has no kill a kill falls back to
/// GunFeel's kill sounds. Created on first use during Play.
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
    private static int streak;
    private static float lastKillTime = -99f;
    private int hits, heads, kills, headKills, breaks, pierces, blocked;
    private float share;
    // The picture of this frame's most telling kill (KillIcons.Better).
    private KillIcons.Row killIcon;

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

    /// <summary>
    /// A hit the local player landed; confirmed with the rest of this frame's hits. marks: what a killing blow happened
    /// under (KillConditions), which picks its picture.
    /// </summary>
    public static void Landed(CombatDamageReport report, KillMarks marks = KillMarks.None)
    {
        if (!Application.isPlaying) return;
        var self = Get();
        self.hits++;
        if (report.IsCritical) self.heads++;
        if (report.Hit.Pierced) self.pierces++;
        if (report.IsKillingBlow)
        {
            self.kills++;
            if (report.IsCritical) self.headKills++;
            self.killIcon = KillIcons.Better(self.killIcon, KillIcons.ForKill(marks));
        }
        self.share += report.MaximumHealth > 0f ? report.Applied / report.MaximumHealth : 1f;
        Remember(report.Target);
    }

    /// <summary>
    /// A shot of the local player's that burst a dead body's head (ZombieGore: a zombie's corpse, or the body a calm zombie
    /// was eating): it shows and sounds as a headshot on a living head does, with this frame's other hits (the user,
    /// 2026-11-15: "히트 마커는 넣어 줘"). Nothing more follows it: no kill, number, kill screen or reward.
    /// </summary>
    public static void LandedOnDead(bool pierced)
    {
        if (!Application.isPlaying) return;
        var self = Get();
        self.hits++;
        self.heads++;
        if (pierced) self.pierces++;
        // The whole head went, as all of a living one's health goes to a killing headshot: the marker at its full size.
        self.share += 1f;
    }

    /// <summary>
    /// A kill of this PC's player that the host judged (KillMarks.JudgedByHost: their fuel can's blast or fire, a can they
    /// threw), heard as the host announces it (PlayerNetworkScore): shown and sounded as their own kill, with its picture,
    /// with this frame's other hits. Their PC never saw that hit land, so nothing else would show it there.
    /// </summary>
    public static void HostKill(KillMarks marks)
    {
        if (!Application.isPlaying) return;
        var self = Get();
        self.hits++;
        self.kills++;
        if ((marks & KillMarks.Headshot) != 0) { self.heads++; self.headKills++; }
        self.share += 1f;
        self.killIcon = KillIcons.Better(self.killIcon, KillIcons.ForKill(marks));
    }

    /// <summary>
    /// A shot of the local player's that reached a target and did nothing (a guard, a parry, invulnerability): the grey
    /// marker and its dull sound, unless a hit of the same frame did harm (a shotgun's other pellets), which speaks instead.
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
        if (hits > 0) Confirm();
        else if (blocked > 0)
        {
            HitMarkerHUD.Show(HitMarkerKind.Blocked);
            Play("Blocked");
        }
        blocked = 0;
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
            Mathf.Min(1f, share), pierces > 0);
        if (kill)
        {
            Kill(settings, headKill, kills);
            HitMarkerHUD.ShowKill(killIcon);
        }
        else Play(head ? "Head" : "Body");
        hits = heads = kills = headKills = pierces = 0;
        share = 0f;
        killIcon = null;
    }

    private static void Kill(GunFeelSettings settings, bool head, int count)
    {
        // The kill feed's streak window, so the climb follows the same kills as its numerals.
        float now = Time.unscaledTime;
        streak = now - lastKillTime <= CombatFeedHUD.StreakWindow ? streak + count : count;
        lastKillTime = now;
        float climb = 1f + Mathf.Min(streak - 1, settings.killStreakSteps) * settings.killStreakPitchStep;
        bool played = Play("Kill", climb);
        // A headshot kill rings the head's bright double tick over the kill's own sound, in tune with its climb.
        if (head) played |= Play("HeadKill", climb);
        if (played) return;
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
