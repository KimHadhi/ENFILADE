using System;
using System.Collections;
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
public sealed class DeathScreen : MonoBehaviour, IHudPreview
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

    // ---- What the rest of the game calls ----

    /// <summary>
    /// The card, the film and the sting made, and the blur drawn once at a strength nobody sees, behind the loading screen
    /// (SceneWarmup), so the first death never waits on them.
    /// </summary>
    public static void Prepare()
    {
        _ = DeathSting.Clip;
        var mine = PlayerRoster.Local;
        if (mine != null && mine.TryGetComponent(out DeathScreen screen)) screen.Warm();
    }

    /// <summary>/death: the death screen without dying. Words: a zombie kind (brute, exploder …), fall, bleed or friend for
    /// the cause, wipe for 전멸, short for the short one.</summary>
    public static string Command(string args)
    {
        var mine = PlayerRoster.Local;
        if (mine == null || !mine.TryGetComponent(out DeathScreen screen)) return "이 PC의 플레이어가 아직 없습니다."; // dev-text
        return screen.StartPreview(args);
    }

    /// <summary>ENFILADE → HUD → 겹침 검사 (IHudPreview): the card in its settled place with sample words, no film or sound.</summary>
    public void Preview(bool show)
    {
        if (show)
        {
            if (phase != Phase.Hidden) Finish();
            held = true;
            card.Hold(Sample());
            return;
        }
        held = false;
        card.Hide();
    }

    // ---- Each frame ----

    private void LateUpdate()
    {
        if (player == null || receiver == null || !player.IsInitialized || held) return;
        if (tally == null) TryGetComponent(out tally);
        if (spectate == null) TryGetComponent(out spectate);
        float now = Time.unscaledTime;
        bool dead = !receiver.IsAlive, wiped = life != null && life.IsWiped, downed = receiver.IsDowned;
        if (!started) { started = true; NewLife(); }
        if (!dead && wasDead) NewLife();
        if (downed && !wasDowned) downedBy = RecentKiller();

        if (dead && !wasDead) Died(now, wiped);
        else if (wiped && !wasWiped) Wiped(now, dead);
        else if (previewing && now >= previewUntil)
        {
            previewing = false;
            Leave(now, look.outSeconds, false);
        }

        if (phase == Phase.Showing && !previewing)
        {
            // Back on their feet: a respawn, a wave, a Détente, or everyone back after the wipe. Cleared as the addon does.
            if (!dead && !wiped) Leave(now, look.clearSeconds, true);
            // Watching someone now: the world clears for it, and PlayerLifeHUD takes over.
            else if (!wipe && spectate != null && spectate.Current != PlayerSpectate.Mode.None) Leave(now, look.outSeconds, false);
            else if (look.holdSeconds > 0f && now - shownAt >= look.holdSeconds) Leave(now, look.outSeconds, false);
        }
        Drive(now);
        wasDead = dead;
        wasWiped = wiped;
        wasDowned = downed;
    }

    private void NewLife()
    {
        lifeStart = Time.time;
        killsAtStart = tally != null ? tally.Kills : 0;
        downedBy = null;
        hasFacts = false;
    }

    // Damage that reached them, for what killed them. A grab taking hold costs nothing by itself (DamageHit.TakesHold).
    private void OnBlow(DamageHit hit, DamageResult result)
    {
        if (result != DamageResult.Damaged || hit.TakesHold && !hit.IsFatal) return;
        lastBlow = hit;
        lastBlowAt = Time.time;
    }

    private void Died(float now, bool wiped)
    {
        previewing = false;
        // A recent blow killed them; otherwise they were down and bled out, put down by someone or by nobody known.
        string cause = Time.time - lastBlowAt <= RecentBlow ? Describe(lastBlow)
            : downedBy != null ? Texts.Format("death.cause.bleed_by", downedBy)
            : Texts.Get(wasDowned ? "death.cause.bleed" : "death.cause.unknown");
        // Alone, death is the wipe (PlayerLife), whether or not it has begun this frame.
        bool asWipe = wiped || !GameSession.IsOnline;
        lastFacts = Facts(cause, asWipe);
        hasFacts = true;
        if (GameSettings.Current.deathScreen == DeathScreenMode.Off) return;
        Show(now, lastFacts, asWipe, Current);
    }

    // The team is down (PlayerLife.BeginWipe, on every PC).
    private void Wiped(float now, bool dead)
    {
        if (life != null) wipeFrom = Mathf.Max(.01f, life.WipeLeft);
        if (GameSettings.Current.deathScreen == DeathScreenMode.Off) return;
        if (phase == Phase.Showing)
        {
            // Their own death's card becomes the wipe's: the word is slammed in again as 전멸.
            if (!wipe) card.Restamp(Texts.Get("death.title.wipe"), null, now);
            wipe = true;
            return;
        }
        var facts = dead && hasFacts ? lastFacts : Facts(Texts.Get("death.cause.wipe"), true);
        facts.Title = Texts.Get("death.title.wipe");
        Show(now, facts, true, Current);
    }

    private void Show(float now, DeathCard.Facts facts, bool asWipe, Look chosen)
    {
        look = chosen;
        wipe = asWipe;
        phase = Phase.Showing;
        shownAt = now;
        cutting = false;
        respawnFrom = life != null ? Mathf.Max(.01f, life.RespawnLeft) : 0f;
        if (asWipe && life != null && life.WipeLeft > 0f) wipeFrom = life.WipeLeft;
        facts.Next = Next(now, out _);
        card.Begin(facts, now);
        Grade(true);
        Sting();
    }

    private void Leave(float now, float seconds, bool cut)
    {
        if (phase != Phase.Showing) return;
        Levels(now, out colourFrom, out edgeFrom, out blurFrom, out inkFrom, out bloodFrom, out muffleFrom);
        phase = Phase.Leaving;
        leaveAt = now;
        leaveFor = Mathf.Max(.01f, seconds);
        if (!cut) return;
        cutting = true;
        cutAt = now;
    }

    private void Finish()
    {
        phase = Phase.Hidden;
        wipe = previewing = false;
        card.Hide();
        Grade(false);
        Muffle(0f);
    }

    private void Drive(float now)
    {
        if (cutting) Cut(now);
        if (phase == Phase.Hidden) return;
        Levels(now, out float colour, out float darkness, out float blurred, out float inkLevel, out float bloodLevel, out float muffled);
        float shown = phase == Phase.Showing ? 1f : 1f - Mathf.Clamp01((now - leaveAt) / leaveFor);
        ApplyGrade(colour, darkness, blurred);
        Muffle(muffled);
        string next = Next(now, out float fill);
        card.Tick(now, shown, next, fill, look.ink * inkLevel, look.blood * bloodLevel);
        if (phase == Phase.Leaving && now - leaveAt >= leaveFor) Finish();
    }

    // How far each part has come: in by its own time while showing, fading out together from where it got while leaving.
    private void Levels(float now, out float colour, out float darkness, out float blurred, out float inkLevel, out float bloodLevel, out float muffled)
    {
        if (phase == Phase.Showing)
        {
            float t = now - shownAt;
            colour = In(t, look.colourSeconds);
            darkness = In(t, look.vignetteSeconds);
            blurred = In(t - look.blurDelay, look.blurSeconds);
            inkLevel = In(t, look.inkSeconds);
            bloodLevel = Smooth((t - look.bloodDelay) / Mathf.Max(.01f, look.bloodSeconds));
            muffled = In(t, look.muffleSeconds);
            return;
        }
        float f = phase == Phase.Leaving ? 1f - Mathf.Clamp01((now - leaveAt) / leaveFor) : 0f;
        colour = colourFrom * f;
        darkness = edgeFrom * f;
        blurred = blurFrom * f;
        inkLevel = inkFrom * f;
        bloodLevel = bloodFrom * f;
        muffled = muffleFrom * f;
    }

    // The addon's ease: quick at first, settling at the end.
    private static float In(float t, float seconds)
    {
        if (seconds <= 0f) return t >= 0f ? 1f : 0f;
        float x = Mathf.Clamp01(t / seconds);
        return 1f - (1f - x) * (1f - x);
    }

    private static float Smooth(float x)
    {
        x = Mathf.Clamp01(x);
        return x * x * (3f - 2f * x);
    }

    // ---- The words ----

    private DeathCard.Facts Facts(string cause, bool asWipe) => new DeathCard.Facts
    {
        Title = Texts.Get(asWipe ? "death.title.wipe" : "death.title.dead"),
        Cause = cause,
        LastedSeconds = Mathf.Max(0f, Time.time - lifeStart),
        Kills = Mathf.Max(0, (tally != null ? tally.Kills : 0) - killsAtStart),
        Money = Wallet.Balance,
    };

    private DeathCard.Facts Sample() => new DeathCard.Facts
    {
        Title = Texts.Get("death.title.dead"),
        Cause = SampleCause(ZombieKind.Brute),
        LastedSeconds = 252f,
        Kills = 23,
        Money = 1250,
        Next = Texts.Format("life.respawn.seconds", 20),
    };

    // What comes next, and how far its countdown has run down (0-1; below 0 when it has none).
    private string Next(float now, out float fill)
    {
        fill = -1f;
        if (previewing)
        {
            float left = Mathf.Max(0f, previewUntil - now);
            fill = left / Mathf.Max(.01f, look.previewSeconds);
            int seconds = Mathf.Max(1, Mathf.CeilToInt(left));
            return previewWipe ? Texts.Format(WipeKey(), seconds) : Texts.Format("life.respawn.seconds", seconds);
        }
        if (life == null) return "";
        if (wipe || !GameSession.IsOnline)
        {
            float left = life.WipeLeft;
            fill = wipeFrom > 0f ? Mathf.Clamp01(left / wipeFrom) : -1f;
            return Texts.Format(WipeKey(), Mathf.Max(1, Mathf.CeilToInt(left)));
        }
        if (TowerRun.KeepsDead) return Texts.Get("life.respawn.tower");
        if (life.WaitsForWave) return Texts.Get("life.respawn.next_wave");
        fill = respawnFrom > 0f ? Mathf.Clamp01(life.RespawnLeft / respawnFrom) : -1f;
        return Texts.Format("life.respawn.seconds", Mathf.Max(1, Mathf.CeilToInt(life.RespawnLeft)));
    }

    // Where everyone comes back after a wipe (PlayerLife.Respawn's order: the tower's hall, the campaign's last rest stop,
    // the waves from the first, the spawn point).
    private string WipeKey()
    {
        if (TowerRun.TryCheckpoint(out _)) return "death.next.wipe.tower";
        if (CampaignRun.TryCheckpoint(out _)) return "death.next.wipe.campaign";
        if (waves == null) waves = FindFirstObjectByType<WaveManager>();
        if (waves != null && waves.IsRunning) return "death.next.wipe.waves";
        return "death.next.wipe.start";
    }

    // The killer's name in their colour, of the last blow if it is recent: who put them down.
    private string RecentKiller()
    {
        if (Time.time - lastBlowAt > RecentBlow) return null;
        var source = lastBlow.Source;
        if (source == null) return null;
        var someone = source.GetComponentInParent<PlayerMovement>();
        if (someone != null) return someone == player ? null : NameOf(someone);
        var enemy = source.GetComponentInParent<EnemyHealth>();
        return Pink(enemy != null ? CombatDamageEvents.DisplayNameOf(enemy) : CombatDamageEvents.DisplayNameOf(source.transform));
    }

    // The cause line of a killing blow: who (a zombie's kind in pink, an elite marked, a friend in their colour), then how.
    private string Describe(in DamageHit hit)
    {
        var source = hit.Source;
        if (source == null) return Texts.Get(hit.Kind == DamageKind.Fall ? "death.cause.fall" : "death.cause.unknown");
        var someone = source.GetComponentInParent<PlayerMovement>();
        if (someone != null)
            return someone == player ? Texts.Get("death.cause.self") : Texts.Format("death.cause.friend", NameOf(someone));
        var enemy = source.GetComponentInParent<EnemyHealth>();
        string name = enemy != null ? CombatDamageEvents.DisplayNameOf(enemy) : CombatDamageEvents.DisplayNameOf(source.transform);
        bool elite = enemy != null && (enemy.IsStandout
            || enemy.TryGetComponent(out ZombieType type) && type.Kind != ZombieKind.Shambler && type.Kind != ZombieKind.Runner);
        return Line(name, elite, HowKey(hit, enemy != null && enemy.TryGetComponent(out ZombieExploder _), enemy != null));
    }

    private static string HowKey(in DamageHit hit, bool exploder, bool zombie)
    {
        switch (hit.Method)
        {
            case DamageMethod.Feed: return "death.how.feed";
            case DamageMethod.Slam: return "death.how.slam";
            case DamageMethod.Pounce: return "death.how.pounce";
            case DamageMethod.Blast: return "death.how.blast";
            case DamageMethod.Bullet: return "death.how.bullet";
            case DamageMethod.Thrown: return "death.how.thrown";
        }
        if (exploder) return "death.how.blast";
        // A runner's bite at the neck, or being eaten while held: blows that kill where they land.
        return hit.IsFatal && zombie ? "death.how.bite" : null;
    }

    private static string SampleCause(ZombieKind kind)
    {
        string how = kind switch
        {
            ZombieKind.Brute => "death.how.slam",
            ZombieKind.Exploder => "death.how.blast",
            ZombieKind.Runner => "death.how.pounce",
            ZombieKind.Shambler => "death.how.bite",
            _ => null,
        };
        return Line(Texts.Get(ZombieType.NameId(kind)), kind != ZombieKind.Shambler && kind != ZombieKind.Runner, how);
    }

    // "<name>에게 당했다", the elite's small pink tag, then how in a smaller pale type.
    private static string Line(string name, bool elite, string howKey)
    {
        string line = Texts.Format("death.cause.by", Pink(name));
        if (elite) line += "  <size=60%><mark=#" + ColorUtility.ToHtmlStringRGB(KitStyle.Accent) + "55> " + Texts.Get("death.cause.elite") + " </mark></size>";
        if (!string.IsNullOrEmpty(howKey)) line += "  <size=72%><color=#FFFFFF8C>· " + Texts.Get(howKey) + "</color></size>";
        return line;
    }

    private static string Pink(string name) => "<color=#" + ColorUtility.ToHtmlStringRGB(KitStyle.Accent) + ">" + name + "</color>";

    private static string NameOf(PlayerMovement someone)
        => someone.TryGetComponent(out PlayerTally theirs)
            ? "<color=#" + ColorUtility.ToHtmlStringRGB(theirs.Color) + ">" + theirs.DisplayName + "</color>" : someone.name;

    // ---- The preview ----

    private string StartPreview(string args)
    {
        if (!receiver.IsAlive) return "죽어 있을 때는 미리 볼 수 없습니다. 살아난 뒤에 /death"; // dev-text
        bool asWipe = false, shortOne = GameSettings.Current.deathScreen == DeathScreenMode.Short;
        string cause = null;
        foreach (string word in (args ?? "").ToLowerInvariant().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            switch (word)
            {
                case "wipe": asWipe = true; break;
                case "short": shortOne = true; break;
                case "full": shortOne = false; break;
                case "fall": cause = Texts.Get("death.cause.fall"); break;
                case "bleed": cause = Texts.Format("death.cause.bleed_by", Pink(Texts.Get(ZombieType.NameId(ZombieKind.Shambler)))); break;
                case "friend": cause = Texts.Format("death.cause.friend", NameOf(player)); break;
                default:
                    if (!Enum.TryParse(word, true, out ZombieKind kind) || int.TryParse(word, out _))
                        return "모르는 말: " + word + " — /death [좀비 종류 / fall / bleed / friend] [wipe] [short / full]"; // dev-text
                    cause = SampleCause(kind);
                    break;
            }
        }
        cause ??= SampleCause(ZombieKind.Brute);
        if (phase != Phase.Hidden) Finish();
        var chosen = shortOne ? brief : full;
        float now = Time.unscaledTime;
        previewing = true;
        previewWipe = asWipe;
        previewUntil = now + Mathf.Max(.5f, chosen.previewSeconds);
        Show(now, Facts(cause, asWipe), asWipe, chosen);
        // dev-text
        return "데스 스크린 미리 보기 (" + (shortOne ? "짧게" : "켬") + ", " + chosen.previewSeconds.ToString("0.#") + "초). 설정이 끔이어도 보인다. "
            + "/death [좀비 종류 / fall / bleed / friend] [wipe] [short / full]";
    }

    // ---- The world's look ----

    private void Grade(bool on)
    {
        if (on && volume == null) MakeVolume();
        if (volume == null || volume.enabled == on) return;
        volume.enabled = on;
        if (on) PostProcessLoan.Take(this);
        else PostProcessLoan.Return(this);
    }

    private void MakeVolume()
    {
        var host = new GameObject("Death Grade");
        host.transform.SetParent(transform, false);
        volume = host.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = Priority;
        volume.weight = 1f;
        profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "Death Grade (Runtime)";
        profile.hideFlags = HideFlags.HideAndDontSave;
        grade = profile.Add<ColorAdjustments>(false);
        grade.saturation.overrideState = grade.colorFilter.overrideState = grade.contrast.overrideState = grade.postExposure.overrideState = true;
        edge = profile.Add<Vignette>(false);
        edge.intensity.overrideState = edge.smoothness.overrideState = edge.color.overrideState = edge.rounded.overrideState = true;
        edge.color.value = new Color(.07f, 0f, .02f);
        edge.smoothness.value = .4f;
        edge.rounded.value = false;
        blur = profile.Add<DepthOfField>(false);
        blur.mode.overrideState = blur.gaussianStart.overrideState = blur.gaussianEnd.overrideState = true;
        blur.gaussianMaxRadius.overrideState = blur.highQualitySampling.overrideState = true;
        blur.highQualitySampling.value = true;
        volume.sharedProfile = profile;
        volume.enabled = false;
        ApplyGrade(0f, 0f, 0f);
    }

    private void ApplyGrade(float colour, float darkness, float blurred)
    {
        if (grade == null) return;
        grade.saturation.value = look.saturation * colour;
        grade.colorFilter.value = Color.Lerp(Color.white, look.tint, colour);
        grade.contrast.value = look.contrast * colour;
        grade.postExposure.value = look.exposure * colour;
        edge.intensity.value = Mathf.Clamp01(look.vignette * darkness);
        bool blurs = look.blurRadius > 0f;
        blur.mode.value = blurs ? DepthOfFieldMode.Gaussian : DepthOfFieldMode.Off;
        // The blurred distance closes in evenly in octaves, from past the horizon to near the eyes.
        float near = Mathf.Max(.1f, look.blurNear);
        float end = Mathf.Exp(Mathf.Lerp(Mathf.Log(BlurOff), Mathf.Log(near), blurs ? blurred : 0f));
        blur.gaussianEnd.value = end;
        blur.gaussianStart.value = end * (1f - blurred);
        blur.gaussianMaxRadius.value = Mathf.Lerp(LeastRadius, Mathf.Clamp(look.blurRadius, LeastRadius, MostRadius), blurred);
    }

    private void Warm()
    {
        card.Build();
        Filter();
        if (!warming && phase == Phase.Hidden && isActiveAndEnabled) StartCoroutine(WarmGrade());
    }

    // The blur's passes drawn for a few frames while the loading screen is up, at a distance nothing in view reaches.
    private IEnumerator WarmGrade()
    {
        warming = true;
        look = full;
        Grade(true);
        ApplyGrade(0f, 0f, 0f);
        for (int frame = 0; frame < 3; frame++) yield return null;
        if (phase == Phase.Hidden) Grade(false);
        warming = false;
    }

    // ---- The sound ----

    private void Sting()
    {
        if (look.sting <= 0f) return;
        if (voice == null)
        {
            var host = new GameObject("Death Sting");
            host.transform.SetParent(transform, false);
            voice = host.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.loop = false;
            voice.spatialBlend = 0f;
            GameAudio.Bind(voice, AudioChannel.UI);
        }
        if (!bankLooked) { bankLooked = true; bank = SoundBank.Load(BankId); }
        var clip = bank != null ? bank.Pick(BankKey) : null;
        voice.Stop();
        voice.clip = clip != null ? clip : DeathSting.Clip;
        voice.pitch = 1f;
        GameAudio.SetGain(voice, Mathf.Clamp01(look.sting));
        voice.Play();
    }

    // Coming back cuts the sting almost at once, with a tiny ramp so it doesn't click (the addon's .06 s).
    private void Cut(float now)
    {
        if (voice == null) { cutting = false; return; }
        float f = 1f - Mathf.Clamp01((now - cutAt) / Mathf.Max(.01f, look.cutSeconds));
        GameAudio.SetGain(voice, Mathf.Clamp01(look.sting) * f);
        if (f > 0f) return;
        voice.Stop();
        cutting = false;
    }

    // The world's sound muffled on the listener (the UI channel bypasses it, so the sting stays clear). The low-pass is
    // the one SlowMotionSound uses: while the game is also slowed, the lower cutoff of the two holds.
    private void Muffle(float depth)
    {
        if (depth <= .001f || look.muffle <= 0f)
        {
            if (!muffling) return;
            muffling = false;
            if (muffle != null && SkullSlowMotion.Depth <= .001f) muffle.enabled = false;
            return;
        }
        if (muffle == null) muffle = Filter();
        if (muffle == null) return;
        muffling = true;
        muffle.enabled = true;
        // Heard pitch is logarithmic, so the cutoff falls evenly in octaves.
        float hertz = Mathf.Exp(Mathf.Lerp(Mathf.Log(OpenCutoff), Mathf.Log(Mathf.Clamp(look.muffle, 100f, OpenCutoff)), depth));
        muffle.cutoffFrequency = SkullSlowMotion.Depth > .001f ? Mathf.Min(muffle.cutoffFrequency, hertz) : hertz;
    }

    // The listener's low-pass, put on ahead switched off (adding it mid-sound can click), as SlowMotionSound does.
    private static AudioLowPassFilter Filter()
    {
        var listener = GameAudio.Listener;
        if (listener == null) return null;
        if (listener.TryGetComponent(out AudioLowPassFilter filter)) return filter;
        filter = listener.gameObject.AddComponent<AudioLowPassFilter>();
        filter.cutoffFrequency = OpenCutoff;
        filter.lowpassResonanceQ = 1f;
        filter.enabled = false;
        return filter;
    }
}
