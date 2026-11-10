using UnityEngine;

/// <summary>
/// Zombies' voices (the user's memo of 2026-11-10, item 10: an attack should be heard coming, by a zombie's voice and a
/// small signal). The signal was there already: the cue every wind-up starts with (CombatCueAudio, EnemyAttackStart).
/// A voice was not: no zombie made a sound of its own. Now a zombie snarls as it winds up, and shrieks as it leaps,
/// from its own mouth, on every PC (each PC starts the wind-up itself, EnemyCombatAI.Telegraph, so nothing is sent).
/// The clips are the Zombie sound bank (Art/Audio/Enemies/Zombie: a folder is a key, Attack and Leap, with Male and
/// Female folders under them for bodies named so), their volume and pitch per key are in Tables/Sounds, the rest in
/// Tables/ZombieVoice.
/// A horde starts many attacks a second and Unity plays 32 sounds at once: past that, gunshots go silent. So the
/// voices are a ring of a few sources, the oldest giving way; a zombie that has spoken keeps quiet a while; no two
/// voices start together; and an attack on someone else is voiced only some of the time and only within earshot. An
/// attack on this PC's own player is always voiced, and its cue is louder (SignalGain).
/// </summary>
[DisallowMultipleComponent]
public sealed class ZombieVoice : MonoBehaviour
{
    public const string TableName = "ZombieVoice", TableRow = "Default", BankId = "Zombie";
    private const string Attack = "Attack", AttackMale = "Attack/Male", AttackFemale = "Attack/Female";
    private const string Leap = "Leap", LeapMale = "Leap/Male", LeapFemale = "Leap/Female";
    // How often a bank that isn't there is looked for again (s, real time): the editor makes it once the clips are in.
    private const float BankRetry = 5f;
    // Where the mouth is taken to be on a body with no head bone to ask (m above the enemy's own position).
    private const float MouthHeight = 1.5f;

    [SerializeField, Tooltip("Zombies voice their attacks.")]
    private bool speak = true;
    [SerializeField, Range(1, 16), Tooltip("Voices that can sound at once; one more stops the oldest. Read once, when the ring is built.")]
    private int voices = 5;
    [SerializeField, Min(0f), Tooltip("A zombie that has spoken keeps quiet this long (s). A leap is voiced all the same.")]
    private float ownGap = 2.5f;
    [SerializeField, Min(0f), Tooltip("No two voices start closer together than this (s).")]
    private float anyGap = .18f;
    [SerializeField, Range(0f, 1f), Tooltip("How many of the attacks aimed at another player are voiced. One aimed at this PC's own player always is.")]
    private float othersShare = .5f;
    [SerializeField, Range(0f, 1f), Tooltip("How often a body named Male or Female uses the clips that are neither.")]
    private float neutralShare = .25f;
    [SerializeField, Min(.1f), Tooltip("Full volume within this (m).")]
    private float near = 2.5f;
    [SerializeField, Min(1f), Tooltip("Silent from this far (m). A zombie further than this from the listener isn't voiced at all.")]
    private float far = 24f;
    [SerializeField, Range(0f, 1f), Tooltip("Volume of every voice, on top of each key's own in Tables/Sounds.")]
    private float volume = 1f;
    [SerializeField, Min(1f), Tooltip("A body this many times the usual size or more is a big one (a brute, a charger).")]
    private float bigSize = 1.15f;
    [SerializeField, Range(.3f, 1.5f), Tooltip("A big body's voice is pitched by this.")]
    private float bigPitch = .8f;
    [SerializeField, Range(0f, 3f), Tooltip("The wind-up cue of an attack aimed at this PC's own player is this many times as loud.")]
    private float signalAtMine = 1.8f;

    private static ZombieVoice instance;
    private VoiceRing ring;
    private SoundBank bank;
    private float nextBankTry, nextVoice;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget() => instance = null;

    private static ZombieVoice Get()
    {
        if (instance == null) instance = FeelAssets.CreateRuntimeRoot("Zombie Voices", groupName: "Audio").AddComponent<ZombieVoice>();
        return instance;
    }

    /// <summary>Builds the ring while a map loads, so the first snarl of a fight makes no objects (CombatWarmup).</summary>
    public static void Prepare()
    {
        if (Application.isPlaying) Get();
    }

    /// <summary>
    /// How loud the wind-up cue of this attack is, as a multiple of its own volume: more when it is meant for this PC's
    /// own player, who has to answer it (EnemyCombatAI.Telegraph).
    /// </summary>
    public static float SignalGain(bool atMine) => atMine && Application.isPlaying ? Get().signalAtMine : 1f;

    /// <summary>
    /// A zombie winds up an attack: it snarls, or shrieks as it leaps. AtMine: the attack is aimed at this PC's own
    /// player. SinceOwn: how long ago this zombie last spoke (s, game time). True if it was voiced.
    /// </summary>
    public static bool Attacks(Component zombie, bool leap, bool atMine, float sinceOwn)
        => Application.isPlaying && zombie != null && Get().Say(zombie, leap, atMine, sinceOwn);

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        GameTables.Bind(TableName, TableRow, this);
        var sources = new AudioSource[Mathf.Clamp(voices, 1, 16)];
        for (int i = 0; i < sources.Length; i++)
        {
            var voice = new GameObject("Zombie Voice " + i).AddComponent<AudioSource>();
            voice.transform.SetParent(transform, false);
            voice.playOnAwake = false;
            voice.spatialBlend = 1f;
            voice.rolloffMode = AudioRolloffMode.Linear;
            // Less pressing than a gun's source (128): when Unity runs out of real voices, a snarl gives way before a shot.
            voice.priority = 160;
            GameAudio.Bind(voice, AudioChannel.Combat);
            sources[i] = voice;
        }
        ring = new VoiceRing(sources);
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private bool Say(Component zombie, bool leap, bool atMine, float sinceOwn)
    {
        if (!speak || ring == null) return false;
        // A leap is over in a third of a second and can pin: it is always heard, whatever the zombie said before.
        if (!leap && sinceOwn < ownGap) return false;
        if (Time.time < nextVoice) return false;
        if (!atMine && Random.value > othersShare) return false;
        if (bank == null)
        {
            if (Time.unscaledTime < nextBankTry) return false;
            nextBankTry = Time.unscaledTime + BankRetry;
            bank = SoundBank.Load(BankId);
            if (bank == null) return false;
        }
        Vector3 mouth = zombie.transform.position + Vector3.up * MouthHeight;
        string build = null;
        float size = 1f;
        if (zombie.TryGetComponent(out ZombieBody body) && body.Model != null)
        {
            var head = body.Head;
            if (head != null) mouth = head.position;
            build = body.Model.name;
            size = Mathf.Abs(body.Model.transform.lossyScale.y);
        }
        var ears = GameAudio.Listener;
        if (ears != null && (ears.transform.position - mouth).sqrMagnitude > far * far) return false;
        string key = KeyFor(leap, build);
        var clip = key != null ? bank.Pick(key) : null;
        if (clip == null) return false;
        var settings = bank.SettingsOf(key);
        var source = ring.Take();
        source.transform.position = mouth;
        source.minDistance = near;
        source.maxDistance = Mathf.Max(near + 1f, far);
        // Set once, as it starts: from here GameAudio.FollowWorldPitch carries it through a slowed game.
        source.pitch = settings.RandomPitch() * (size >= bigSize ? bigPitch : 1f);
        source.PlayOneShot(clip, Mathf.Clamp01(settings.Volume * volume));
        nextVoice = Time.time + anyGap;
        return true;
    }

    // The clips for this voice on this body. Synty's zombies say which they are in their names
    // (SM_Chr_Zombie_Bride_Female_01); a modder's body may say nothing, and not every voice has clips of every sort.
    private string KeyFor(bool leap, string build)
    {
        string plain = leap ? Leap : Attack, male = leap ? LeapMale : AttackMale, female = leap ? LeapFemale : AttackFemale;
        string own = build == null ? null
            : build.IndexOf("_Female_", System.StringComparison.OrdinalIgnoreCase) >= 0 ? female
            : build.IndexOf("_Male_", System.StringComparison.OrdinalIgnoreCase) >= 0 ? male : null;
        bool hasPlain = bank.TryGet(plain, out _), hasOwn = own != null && bank.TryGet(own, out _);
        if (hasOwn && !(hasPlain && Random.value < neutralShare)) return own;
        if (hasPlain) return plain;
        if (bank.TryGet(male, out _)) return male;
        return bank.TryGet(female, out _) ? female : null;
    }
}
