using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The death screen: the MW3 Death Screen addon for GMod (workshop 3833150063) as it is, with our death card on it
/// (2026-11-22, the user, after seeing it: "여기에 우리 데스 정보 보여주는 거 합치면 돼"). From the addon
/// (lua/autorun/mw3_deathscreen.lua), each part by its own clock from the moment of death:
/// - the world's colour (post-processing on the world camera only; the HUD camera never takes it): +.05 red, -.015
///   blue, brightness -.03, contrast x1.08, saturation .58, all grown in over 1.2 s, quick at first (ApplyGrade turns
///   GMod's colour modify into URP's grade);
/// - the blur: from .15 s over 2.4 s, the whole world alike (URP's bokeh stands in for GMod's blur 8);
/// - its two pictures under the HUD (DeathCard): the flecked dark-red vignette over 1.6 s, then its blood from 1 s
///   over 1.6 s, eased in and out;
/// - its whistle, once, at full volume (Art/Audio/UI/Death, through the UI bank); coming back clears everything in
///   .3 s, from wherever it had got, and cuts the whistle in .06 s.
/// Ours on top: the world's sound muffled through the listener's low-pass (the whistle, on the UI channel, stays clear),
/// and the card over the HUD (DeathCard): 사망 (전멸 for a wipe), what killed them (the zombie's kind, an elite marked,
/// how it did it), how long they lasted, their kills this life, the money they hold, and what comes next.
/// Only at a real death, never on going down. In a room it plays while the view is still in the fallen body's eyes and
/// hands over as soon as spectating starts (PlayerSpectate: a click, or Spectate.csv deathViewSeconds). Alone, death is
/// the wipe, so it holds until everyone is back. A wipe that comes later shows it again as 전멸 for everyone, the downed
/// and the spectating included. Coming back (a respawn, a wave, a Détente's Resurrection) clears it as the addon does.
/// While it is up, PlayerLifeHUD's own middle line waits under it (Covers); its spectate keys stay. Teammates already
/// hear of a death: "X 사망" at the top of their screen, a kill feed row and a sound (PlayerTally).
/// Settings → HUD → 데스 스크린: 켬 (Full: the addon's numbers), 짧게 (Short: ours, lighter and brief), 끔 (Off).
/// Numbers: Tables/DeathScreen (rows Full, Short). Console: /death previews it without dying. This PC's own player only.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.DeathScreen)]
public sealed partial class DeathScreen : MonoBehaviour, IHudPreview
{
    public const string TableName = "DeathScreen";
    private const string FullRow = "Full", ShortRow = "Short";
    // Over the low-health grade (200.5) and the kill screen (200), under PostProcessWarmup (300).
    private const float Priority = 210f;
    // A blow this recent (s) is what killed them; older, it only put them down and they bled out.
    private const float RecentBlow = 1.5f;
    // The world's sound with nothing muffling it (Hz).
    private const float OpenCutoff = 22000f;
    // The whistle's place in the UI sound bank (Art/Audio/UI/Death).
    private const string BankId = "UI", BankKey = "Death";

    /// <summary>One row of Tables/DeathScreen: how a death screen looks and sounds, and for how long. The defaults (and
    /// the Full row) are the addon's own numbers.</summary>
    [Serializable]
    public sealed class Look
    {
        [Tooltip("Seconds it holds before leaving; 0 holds until spectating starts, they live again or the wipe ends.")]
        public float holdSeconds;
        [Tooltip("How long /death's preview holds (s; the addon's preview is 5).")]
        public float previewSeconds = 5f;
        [Tooltip("Seconds for the world's colour change to come in all the way (the addon's 1.2).")]
        public float colourSeconds = 1.2f;
        [Tooltip("Added to the world's red at the full colour change, on the screen's 0-1 values (the addon's +.05).")]
        public float addRed = .05f;
        [Tooltip("Added to the world's green (the addon's 0).")]
        public float addGreen;
        [Tooltip("Added to the world's blue (the addon's -.015).")]
        public float addBlue = -.015f;
        [Tooltip("Added to every channel: negative darkens (the addon's -.03).")]
        public float brightness = -.03f;
        [Tooltip("Every channel multiplied by this (the addon's 1.08; 1 leaves it).")]
        public float contrast = 1.08f;
        [Tooltip("Share of the world's colour kept (the addon's .58; 0 is black and white, 1 leaves it).")]
        public float colour = .58f;
        [Tooltip("Opacity of the addon's vignette picture at its fullest (0-1; the addon's 1).")]
        public float vignette = 1f;
        [Tooltip("Seconds for the vignette to close in (the addon's 1.6).")]
        public float vignetteSeconds = 1.6f;
        [Tooltip("The blur at its fullest, as a share of URP's largest (0-1; 1 stands for the addon's 8). 0 turns it off.")]
        public float blur = 1f;
        [Tooltip("Seconds after death before the blur starts (the addon's .15).")]
        public float blurDelay = .15f;
        [Tooltip("Seconds for the blur to come in all the way (the addon's 2.4).")]
        public float blurSeconds = 2.4f;
        [Tooltip("Opacity of the addon's blood picture at its fullest (0-1; the addon's 1). 0 shows none.")]
        public float blood = 1f;
        [Tooltip("Seconds after death before the blood starts to show (the addon's 1).")]
        public float bloodDelay = 1f;
        [Tooltip("Seconds for the blood to come in all the way (the addon's 1.6).")]
        public float bloodSeconds = 1.6f;
        [Tooltip("The highest sound the muffled world keeps (Hz); lower is more muffled. 0 leaves it open. Ours, not the addon's.")]
        public float muffle = 650f;
        [Tooltip("Seconds for the world to go muffled.")]
        public float muffleSeconds = .35f;
        [Tooltip("The whistle's volume (0-1; the addon's 1), under the UI volume.")]
        public float whistle = 1f;
        [Tooltip("Seconds it takes to leave when spectating starts or a short one ends.")]
        public float outSeconds = .45f;
        [Tooltip("Seconds it takes to clear when they live again (the addon's .3).")]
        public float clearSeconds = .3f;
        [Tooltip("Seconds the whistle takes to cut out when they live again (the addon's .06).")]
        public float cutSeconds = .06f;
    }

    private enum Phase : byte { Hidden, Showing, Leaving }

    private readonly Look full = new Look();
    private readonly Look brief = new Look
    {
        holdSeconds = 1.4f, previewSeconds = 3f, colourSeconds = .6f, addRed = .03f, addBlue = -.01f, brightness = -.02f,
        contrast = 1.05f, colour = .72f, vignette = .7f, vignetteSeconds = .8f, blur = .5f, blurDelay = .1f, blurSeconds = 1f,
        blood = 0f, bloodDelay = 0f, bloodSeconds = 1f, muffle = 1400f, muffleSeconds = .25f, whistle = .75f, outSeconds = .35f,
    };
    private readonly DeathCard card = new DeathCard();

    private PlayerMovement player;
    private PlayerDamageReceiver receiver;
    private PlayerLife life;
    private PlayerSpectate spectate;
    private PlayerTally tally;
    private WaveManager waves;
    private Volume volume;
    private VolumeProfile profile;
    private ColorAdjustments grade;
    private DepthOfField blur;
    private AudioSource voice;
    private AudioLowPassFilter muffle;
    private SoundBank bank;
    private bool bankLooked, muffling, cutting, held, warming;

    private Look look;
    private Phase phase;
    private bool wipe, previewing, previewWipe;
    private float shownAt, leaveAt, leaveFor = .3f, cutAt, previewUntil;
    // Where each part had got when it began to leave: it fades from there, as the addon's screen does.
    private float colourFrom, vignetteFrom, blurFrom, bloodFrom, muffleFrom;
    // What the countdown line's hairline runs down from (s).
    private float respawnFrom, wipeFrom;

    // This life: since when (game time) and with how many kills already.
    private float lifeStart;
    private int killsAtStart;
    private bool started, wasDead, wasWiped, wasDowned;
    // The last blow that hurt them, and who put them down (in their colour), for a death by bleeding.
    private DamageHit lastBlow;
    private float lastBlowAt = float.NegativeInfinity;
    private string downedBy;
    // Their own death's card, for a wipe that comes while they are watching.
    private DeathCard.Facts lastFacts;
    private bool hasFacts;

    /// <summary>The death screen is up (or leaving): PlayerLifeHUD keeps its own middle line under it.</summary>
    public bool Covers => phase != Phase.Hidden;

    private Look Current => GameSettings.Current.deathScreen == DeathScreenMode.Short ? brief : full;

    private void Awake()
    {
        player = GetComponent<PlayerMovement>();
        receiver = GetComponent<PlayerDamageReceiver>();
        life = GetComponent<PlayerLife>();
        look = full;
        GameTables.BindNested(TableName, FullRow, this, full);
        GameTables.BindNested(TableName, ShortRow, this, brief);
    }

    private void OnEnable()
    {
        if (receiver != null) receiver.HitResolved += OnBlow;
    }

    private void OnDisable()
    {
        if (receiver != null) receiver.HitResolved -= OnBlow;
        if (phase != Phase.Hidden) Finish();
        Grade(false);
        Muffle(0f);
        held = warming = false;
    }

    private void OnDestroy()
    {
        card.Destroy();
        if (profile != null) Destroy(profile);
    }
}
