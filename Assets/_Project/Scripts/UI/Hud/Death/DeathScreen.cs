using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The death screen, after the GMod addon the user pointed to (MW3 Death Screen, workshop 3833150063; 2026-11-12: "게리모드에
/// 애드온 새로운 거 하나 추가했는데 그거처럼 데스 스크린도 만들어 봐"). On death the addon drains the world to a red-brown cast
/// over 1.2 s, closes a dark flecked vignette in over 1.6 s, blurs the screen from .15 s over 2.4 s, fades a blood film in
/// after 1 s and plays its "whistle" (a muffled roar with a ringing tone); coming back clears it in .3 s and cuts the
/// sound. It has no words. Its textures and sound come from Call of Duty, so none of them are used here.
/// Ours keeps that mood in the HUD's own look and also says what happened:
/// - the world (post-processing on the world camera only; the HUD camera never takes it): colour drained toward grey
///   with a faint pink-red cast, more contrast, a little darker, a dark vignette, and a blur that starts at the horizon
///   and closes in;
/// - a film under the HUD: an ink edge with specks, then blood creeping in from the corners (DeathFilmGraphic);
/// - the sound: the world muffled through a low-pass on the listener, and our own sting over it on the UI channel
///   (DeathSting), which the muffling leaves clear;
/// - the card over the HUD (DeathCard): 사망 (전멸 for a wipe), what killed them (the zombie's kind, an elite marked,
///   how it did it), how long they lasted, their kills this life, the money they hold, and what comes next.
/// Only at a real death, never on going down. In a room it plays while the view is still in the fallen body's eyes and
/// hands over as soon as spectating starts (PlayerSpectate: a click, or Spectate.csv deathViewSeconds). Alone, death is
/// the wipe, so it holds until everyone is back. A wipe that comes later shows it again as 전멸 for everyone, the downed
/// and the spectating included. Coming back (a respawn, a wave, a Détente's Resurrection) clears it as the addon does.
/// While it is up, PlayerLifeHUD's own middle line waits under it (Covers); its spectate keys stay. Teammates already
/// hear of a death: "X 사망" at the top of their screen, a kill feed row and a sound (PlayerTally).
/// Settings → HUD → 데스 스크린: 켬 (Full), 짧게 (Short), 끔 (Off). Numbers: Tables/DeathScreen (rows Full, Short).
/// Console: /death previews it without dying. This PC's own player only.
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
    // The world's sound with nothing muffling it (Hz), and where the blur sits while off (m): past anything in view.
    private const float OpenCutoff = 22000f, BlurOff = 100000f;
    // The blur sizes URP takes.
    private const float LeastRadius = .5f, MostRadius = 1.5f;
    // The UI sound bank's clip that plays instead of the made sting, when the bank has one (Art/Audio/UI/Death).
    private const string BankId = "UI", BankKey = "Death";

    /// <summary>One row of Tables/DeathScreen: how a death screen looks and sounds, and for how long.</summary>
    [Serializable]
    public sealed class Look
    {
        [Tooltip("Seconds it holds before leaving; 0 holds until spectating starts, they live again or the wipe ends.")]
        public float holdSeconds;
        [Tooltip("How long /death's preview holds (s).")]
        public float previewSeconds = 5f;
        [Tooltip("Seconds for the world's colour to drain all the way.")]
        public float colourSeconds = 1.2f;
        [Tooltip("Saturation at its most drained (-100 is black and white).")]
        public float saturation = -65f;
        [Tooltip("Colour filter at its most drained: a faint pink-red cast.")]
        public Color tint = new Color(1f, .85f, .87f);
        [Tooltip("Contrast added (0-100).")]
        public float contrast = 12f;
        [Tooltip("Exposure (negative darkens).")]
        public float exposure = -.35f;
        [Tooltip("Strength of the world's dark vignette (0-1).")]
        public float vignette = .42f;
        [Tooltip("Seconds for the vignette to close in.")]
        public float vignetteSeconds = 1.6f;
        [Tooltip("Seconds after death before the blur starts.")]
        public float blurDelay = .15f;
        [Tooltip("Seconds for the blur to close in all the way.")]
        public float blurSeconds = 2.2f;
        [Tooltip("At its fullest everything farther than this is blurred (m).")]
        public float blurNear = 1.5f;
        [Tooltip("Size of the blur (.5-1.5); 0 turns it off.")]
        public float blurRadius = 1.5f;
        [Tooltip("Strength of the ink edge and specks under the HUD (0-1).")]
        public float ink = .7f;
        [Tooltip("Seconds for the ink edge to come in.")]
        public float inkSeconds = 1.4f;
        [Tooltip("Strength of the blood film under the HUD (0-1); 0 shows none.")]
        public float blood = .75f;
        [Tooltip("Seconds after death before the blood starts to show.")]
        public float bloodDelay = 1f;
        [Tooltip("Seconds for the blood to come in all the way.")]
        public float bloodSeconds = 1.6f;
        [Tooltip("The highest sound the muffled world keeps (Hz); lower is more muffled. 0 leaves it open.")]
        public float muffle = 650f;
        [Tooltip("Seconds for the world to go muffled.")]
        public float muffleSeconds = .35f;
        [Tooltip("The sting's volume (0-1), under the UI volume.")]
        public float sting = .9f;
        [Tooltip("Seconds it takes to leave when spectating starts or a short one ends.")]
        public float outSeconds = .45f;
        [Tooltip("Seconds it takes to clear when they live again (the addon's .3).")]
        public float clearSeconds = .3f;
        [Tooltip("Seconds the sting takes to cut out when they live again.")]
        public float cutSeconds = .06f;
    }

    private enum Phase : byte { Hidden, Showing, Leaving }

    private readonly Look full = new Look();
    private readonly Look brief = new Look
    {
        holdSeconds = 1.4f, previewSeconds = 3f, colourSeconds = .6f, saturation = -45f, tint = new Color(1f, .9f, .92f), contrast = 8f,
        exposure = -.2f, vignette = .32f, vignetteSeconds = .8f, blurDelay = .1f, blurSeconds = 1f, blurNear = 3f, blurRadius = 1f,
        ink = .5f, inkSeconds = .7f, blood = 0f, bloodDelay = 0f, bloodSeconds = 1f, muffle = 1400f, muffleSeconds = .25f,
        sting = .75f, outSeconds = .35f,
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
    private Vignette edge;
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
    private float colourFrom, edgeFrom, blurFrom, inkFrom, bloodFrom, muffleFrom;
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
