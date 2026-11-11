using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// A special kill's name stamped just under the crosshair (SpecialKills): a near-black slanted plate slams in flat and
/// wide with the word on it, flashing the rule's colour for its first frames, a thin line in that colour sweeps through
/// behind it, and the reason slides in underneath ("산탄이 전부 한 놈에게"), so the bonus says what it was for (the user's
/// rule: an effect comes with a reason it can be read by). It holds about half a second and squashes away: short and loud,
/// nothing lingers. Several at once stack upward, the newest underneath. A bright two-note ting goes with each.
/// Real time, so slow motion never drags it. It also keeps SpecialKills listening to this PC's gun every frame and holds
/// the rules of Tables/SpecialKills.csv. Made in every scene as it loads; its pieces are built on the first stamp.
/// </summary>
[DisallowMultipleComponent]
public sealed class SpecialKillStamp : MonoBehaviour
{
    [Serializable]
    public sealed class Rule
    {
        [Tooltip("Money paid on top of the kill (a kill is 100).")]
        public int points = 50;
        [Tooltip("The rule's own number: meatshot the share of the pellets that must go in (1 every one), railgun how near " +
            "the head the blow must land (m), collateral the kills one shot needs, hattrick the headshot kills in a row.")]
        public float need;
        [Tooltip("The colour of its flash, its edge and its sweep, and of its line in the score popup.")]
        public Color colour = KitStyle.Accent;
    }

    /// <summary>The rules by kind.</summary>
    public sealed class RuleSet
    {
        internal readonly Rule[] rules = new Rule[Enum.GetValues(typeof(SpecialKills.Kind)).Length];
        public Rule Of(SpecialKills.Kind kind) => rules[(int)kind];
    }

    private sealed class Stamp
    {
        public RectTransform Root, Plate, Sweep, NoteRect;
        public Image Back, Edge, Line;
        public TextMeshProUGUI Word, Note;
        public CanvasGroup Group;
        public Color Colour;
        public float Age = float.MaxValue, Lift, Rise;
    }

    // Where the newest stamp stands under the crosshair (px at 1080p), how far each older one rises, its lean.
    private const float Below = 150f, Step = 76f, Lean = -4f;
    // Its life (real s): slam, sweep, reason, hold, leave.
    private const float SlamSeconds = .07f, FlashSeconds = .06f, SweepFrom = .02f, SweepSeconds = .12f, SweepFade = .2f,
        NoteFrom = .07f, NoteSeconds = .1f, Hold = .55f, LeaveSeconds = .12f;
    private const float WordSize = 44f, NoteSize = 18f, PlateHeight = 58f, PlatePad = 30f, SlantPx = 14f, EdgeWidth = 6f, SweepLength = 780f;
    private const int MostShown = 3, Rate = 44100;

    private static SpecialKillStamp instance;
    private readonly RuleSet rules = new RuleSet();
    private readonly List<Stamp> stamps = new List<Stamp>();
    private readonly Stack<Stamp> spare = new Stack<Stamp>();
    private RectTransform canvasRect;
    private AudioSource voice;
    private AudioClip ting;

    /// <summary>Tables/SpecialKills.csv, row by kind.</summary>
    public static RuleSet Rules => Get().rules;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget() => instance = null;

    // In every scene from its start, so SpecialKills hears this PC's very first shot.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Boot()
    {
        SceneManager.sceneLoaded -= OnLoaded;
        SceneManager.sceneLoaded += OnLoaded;
        Get();
    }

    private static void OnLoaded(Scene scene, LoadSceneMode mode) => Get();

    private static SpecialKillStamp Get()
    {
        if (instance == null)
        {
            var host = new GameObject("Special Kills", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            instance = host.AddComponent<SpecialKillStamp>();
        }
        return instance;
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        FeelUI.SetupOverlay(gameObject, 31);
        canvasRect = (RectTransform)transform;
        foreach (SpecialKills.Kind kind in Enum.GetValues(typeof(SpecialKills.Kind)))
        {
            var rule = new Rule { need = Default(kind) };
            rules.rules[(int)kind] = rule;
            GameTables.BindNested(SpecialKills.TableName, SpecialKills.Key(kind), this, rule);
        }
    }

    private void OnDestroy() { if (instance == this) instance = null; }

    // What each rule asks when its row is missing.
    private static float Default(SpecialKills.Kind kind) => kind switch
    {
        SpecialKills.Kind.Meatshot => 1f,
        SpecialKills.Kind.Railgun => .32f,
        SpecialKills.Kind.Collateral => 2f,
        SpecialKills.Kind.HatTrick => 3f,
        _ => 0f,
    };

    /// <summary>Stamps <paramref name="word"/> with its reason under it.</summary>
    public static void Show(string word, string note, Color colour)
    {
        if (!Application.isPlaying) return;
        Get().Add(word, note, colour);
    }

    private void Add(string word, string note, Color colour)
    {
        // Past the most shown the oldest makes way at once.
        if (stamps.Count >= MostShown)
        {
            Retire(stamps[0]);
            stamps.RemoveAt(0);
        }
        var stamp = spare.Count > 0 ? spare.Pop() : Build();
        stamp.Word.text = word;
        stamp.Note.text = note;
        stamp.Colour = colour;
        stamp.Age = 0f;
        stamp.Lift = 0f;
        stamp.Rise = 0f;
        float width = stamp.Word.GetPreferredValues(word).x + PlatePad * 2f;
        stamp.Plate.sizeDelta = new Vector2(width, PlateHeight);
        stamp.Edge.rectTransform.anchoredPosition = new Vector2(-width * .5f + EdgeWidth * .5f, 0f);
        stamp.Edge.color = colour;
        stamp.Line.color = colour;
        stamp.Root.gameObject.SetActive(true);
        stamp.Root.SetAsLastSibling();
        stamps.Add(stamp);
        PlayTing();
    }

    private void Retire(Stamp stamp)
    {
        stamp.Root.gameObject.SetActive(false);
        stamp.Age = float.MaxValue;
        spare.Push(stamp);
    }

    private void Update()
    {
        SpecialKills.Watch();
        if (stamps.Count == 0) return;
        float dt = Time.unscaledDeltaTime;
        for (int i = stamps.Count - 1; i >= 0; i--)
        {
            var stamp = stamps[i];
            stamp.Age += dt;
            if (stamp.Age >= Hold + LeaveSeconds) { Retire(stamp); stamps.RemoveAt(i); continue; }
            // Older ones rise to make room under them for the newest.
            stamp.Lift = Mathf.Lerp(stamp.Lift, (stamps.Count - 1 - i) * Step, 1f - Mathf.Exp(-18f * dt));
            Pose(stamp);
        }
    }

    private void Pose(Stamp stamp)
    {
        float age = stamp.Age;
        // Slam: in flat and wide, then square.
        float slam = Ease(age / SlamSeconds);
        float leave = Mathf.Clamp01((age - Hold) / LeaveSeconds);
        var scale = new Vector3(Mathf.Lerp(1.7f, 1f, slam), Mathf.Lerp(.55f, 1f, slam) * Mathf.Lerp(1f, .3f, leave * leave), 1f);
        stamp.Plate.localScale = scale;
        stamp.Root.anchoredPosition = new Vector2(0f, -Below + stamp.Lift + leave * 10f);
        stamp.Group.alpha = Mathf.Clamp01(age / .03f) * (1f - leave);
        // The plate flashes the rule's colour for its first frames, then goes dark under the word.
        Color dark = KitStyle.With(KitStyle.Night, .86f);
        stamp.Back.color = Color.Lerp(stamp.Colour, dark, Mathf.Clamp01(age / FlashSeconds));
        stamp.Word.color = Color.Lerp(Color.white, KitStyle.Text, Mathf.Clamp01(age / .1f));
        // A thin line sweeps out through it either way, then fades.
        float sweep = Ease((age - SweepFrom) / SweepSeconds);
        stamp.Sweep.sizeDelta = new Vector2(SweepLength * sweep, 3f);
        Color line = stamp.Colour;
        line.a = age < SweepFrom ? 0f : 1f - Mathf.Clamp01((age - SweepFrom - SweepSeconds) / SweepFade);
        stamp.Line.color = line;
        // The reason slides in from the left under the plate.
        float note = Ease((age - NoteFrom) / NoteSeconds);
        stamp.NoteRect.anchoredPosition = new Vector2(Mathf.Lerp(-24f, 0f, note), -PlateHeight * .5f - 16f);
        stamp.Note.alpha = note;
    }

    private static float Ease(float t)
    {
        t = Mathf.Clamp01(t);
        return 1f - (1f - t) * (1f - t) * (1f - t);
    }

    private Stamp Build()
    {
        var stamp = new Stamp { Root = FeelUI.Rect("Special Kill", canvasRect) };
        stamp.Root.localRotation = Quaternion.Euler(0f, 0f, Lean);
        stamp.Group = stamp.Root.gameObject.AddComponent<CanvasGroup>();
        stamp.Group.interactable = stamp.Group.blocksRaycasts = false;

        stamp.Sweep = FeelUI.Rect("Sweep", stamp.Root);
        stamp.Line = KitStyle.Fill(stamp.Sweep, Color.clear);

        stamp.Plate = FeelUI.Rect("Plate", stamp.Root);
        var back = KitStyle.Stretch("Back", stamp.Plate);
        stamp.Back = KitStyle.Fill(back, KitStyle.Night);
        KitSkew.On(stamp.Back, SlantPx);
        var edge = FeelUI.Rect("Edge", stamp.Plate);
        edge.sizeDelta = new Vector2(EdgeWidth, PlateHeight);
        stamp.Edge = KitStyle.Fill(edge, KitStyle.Accent);
        KitSkew.On(stamp.Edge, SlantPx);
        stamp.Word = FeelUI.ScoreLabel("Word", stamp.Plate, WordSize, TextAlignmentOptions.Center);
        stamp.Word.rectTransform.sizeDelta = new Vector2(900f, PlateHeight);
        stamp.Word.fontStyle = FontStyles.Bold | FontStyles.Italic;
        stamp.Word.characterSpacing = 4f;

        stamp.Note = FeelUI.ScoreLabel("Reason", stamp.Root, NoteSize, TextAlignmentOptions.Center);
        stamp.NoteRect = stamp.Note.rectTransform;
        stamp.NoteRect.sizeDelta = new Vector2(700f, 26f);
        stamp.Note.color = new Color(1f, 1f, 1f, .88f);
        stamp.Root.gameObject.SetActive(false);
        return stamp;
    }

    // ---- The ting: a snap and two bright notes a fourth apart, the second ringing a little ----

    private void PlayTing()
    {
        if (voice == null)
        {
            voice = gameObject.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.spatialBlend = 0f;
            GameAudio.Bind(voice, AudioChannel.UI);
        }
        if (ting == null) ting = MakeTing();
        voice.PlayOneShot(ting, .55f);
    }

    private static AudioClip MakeTing()
    {
        var data = new float[(int)(Rate * .34f)];
        var random = new System.Random(7351);
        int snap = (int)(Rate * .012f);
        for (int i = 0; i < snap; i++)
        {
            float t = i / (float)Rate;
            data[i] += ((float)random.NextDouble() * 2f - 1f) * Mathf.Exp(-t / .0025f) * .55f;
        }
        Note(data, 0f, 1975.5f, .06f, .32f);
        Note(data, .045f, 2637f, .16f, .42f);
        float peak = 0f;
        foreach (float sample in data) peak = Mathf.Max(peak, Mathf.Abs(sample));
        if (peak > 0f) for (int i = 0; i < data.Length; i++) data[i] *= .8f / peak;
        var clip = AudioClip.Create("Special Kill Ting", data.Length, 1, Rate, false);
        clip.SetData(data, 0);
        clip.hideFlags = HideFlags.HideAndDontSave;
        return clip;
    }

    // A struck note from <at> (s) with a partial above it that dies sooner.
    private static void Note(float[] data, float at, float pitch, float decay, float level)
    {
        int from = (int)(at * Rate);
        for (int i = from; i < data.Length; i++)
        {
            float t = (i - from) / (float)Rate, turn = 2f * Mathf.PI * pitch * t;
            float wave = Mathf.Sin(turn) * Mathf.Exp(-t / decay) + .4f * Mathf.Sin(turn * 2.76f) * Mathf.Exp(-t / (decay * .5f));
            data[i] += wave * Mathf.Min(1f, t / .002f) * level;
        }
    }
}
