using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Zombies' voices (the user's memo of 2026-11-10, item 10: an attack should be heard coming, by a zombie's voice and a
/// small signal). The signal was there already: the cue every wind-up starts with (CombatCueAudio, EnemyAttackStart).
/// A voice was not: no zombie made a sound of its own. Now a zombie snarls as it winds up, and shrieks as it leaps,
/// from its own mouth, on every PC (each PC starts the wind-up itself, EnemyCombatAI.Telegraph, so nothing is sent).
/// Porting bundle 2 (RE6 Zombie Sound, L4D2 workshop 3675704366) gave it the rest of a voice: one that knows of nobody
/// (calm or searching) moans now and then, heard only close by, and one lying low (a calm pose, a crawl) only breathes;
/// it cries out once as it wakes; it cries out when hurt, surely at a hard hit, a reel or a knockdown, at a graze only
/// sometimes; and it dies with a last cry, unless the blow burst its head or took it down unaware. Each PC voices these
/// from what it already knows, so nothing more is sent: the mood the host shares (ZombieMind.Mood), the hits and deaths
/// every PC hears of (CombatDamageEvents, EnemyHealth), posture breaks and knockdowns (MeleeCombatTarget.AnyStaggered,
/// ZombieBody.Down).
/// The clips are the Zombie sound bank (Art/Audio/Enemies/Zombie: a folder is a key, Attack, Leap, Idle, Breathe,
/// Alert, Hurt and Death, with Male and Female folders under them for bodies named so), their volume and pitch per key
/// are in Tables/Sounds, the rest in Tables/ZombieVoice. A key with no clips is silent.
/// A horde starts many attacks a second and Unity plays 32 sounds at once: past that, gunshots go silent. So the
/// voices are a ring of a few sources, the least pressing (then the oldest) giving way and none cut by a less pressing
/// one; a zombie that has spoken keeps quiet a while; no two voices of a kind start together; and an attack on someone
/// else is voiced only some of the time and only within earshot. Most pressing first: an attack on this PC's own
/// player, any attack, a death, a hurt, a waking, a moan. A zombie has one mouth: what it is still saying gives way only
/// to something more pressing of its own, or to a leap, a reel or its death (Take).
/// An attack on this PC's own player comes first: it isn't left out by chance, a voice for somebody else never takes
/// its turn, a leap at them is always heard, it keeps its place when Unity runs short of voices, and its cue is louder
/// (SignalGain).
/// </summary>
[DisallowMultipleComponent]
public sealed class ZombieVoice : MonoBehaviour
{
    public const string TableName = "ZombieVoice", TableRow = "Default", BankId = "Zombie";
    // How often a bank that isn't there is looked for again (s, real time): the editor makes it once the clips are in.
    private const float BankRetry = 5f;
    // Where the mouth is taken to be on a body with no head bone to ask (m above the enemy's own position).
    private const float MouthHeight = 1.5f;

    // What a voice is, least pressing first. In a full ring a voice cuts only one that ranks no higher, and what a zombie
    // is still saying gives way only to a higher rank of its own (Take). The attacks keep the top, as before.
    private enum Rank : byte { Idle, Alert, Hurt, Death, Attack, AttackMine }
    // Unity's priority for each rank (0 most pressing, 256 least): out of real voices it drops the least pressing first,
    // then the quietest. A snarl meant for this PC's own player ranks with everything else (128), so close by it outlasts
    // far-off tails; one for somebody else goes first (160), and the rest before it. The player's own shots rank above all.
    private static readonly int[] Priorities = { 200, 180, 170, 165, 160, 128 };

    // A voice's folders in the bank: its own, and those for bodies named Male or Female.
    private readonly struct Keys
    {
        public readonly string Plain, Male, Female;
        public Keys(string plain) { Plain = plain; Male = plain + "/Male"; Female = plain + "/Female"; }
    }

    private static readonly Keys AttackKeys = new Keys("Attack"), LeapKeys = new Keys("Leap"), IdleKeys = new Keys("Idle"),
        BreatheKeys = new Keys("Breathe"), AlertKeys = new Keys("Alert"), HurtKeys = new Keys("Hurt"), DeathKeys = new Keys("Death");

    [SerializeField, Tooltip("Zombies have voices: attacks, moans, waking, pain and death.")]
    private bool speak = true;
    [SerializeField, Range(1, 16), Tooltip("Voices that can sound at once. One more stops the least pressing that is no more pressing than it, the oldest of those; if every one is more pressing it stays silent. Read once, when the ring is built.")]
    private int voices = 5;
    [SerializeField, Min(0f), Tooltip("A zombie that has voiced an attack keeps quiet this long (s): one that swings every second or so speaks at every other swing, a slow heavy one at each. A leap is voiced all the same.")]
    private float ownGap = 2f;
    [SerializeField, Min(0f), Tooltip("No two attack voices start closer together than this (s). Voices for this PC's own player wait only for one another.")]
    private float anyGap = .18f;
    [SerializeField, Range(0f, 1f), Tooltip("How many of the attacks aimed at another player are voiced. One aimed at this PC's own player is never left out by this.")]
    private float othersShare = .5f;
    [SerializeField, Range(0f, 1f), Tooltip("How often a body named Male or Female uses the clips that are neither.")]
    private float neutralShare = .25f;
    [SerializeField, Min(.1f), Tooltip("Full volume within this (m). Moans have their own (idleNear).")]
    private float near = 2.5f;
    [SerializeField, Min(1f), Tooltip("Silent from this far (m). A zombie further than this from the listener isn't voiced at all. Moans have their own (idleFar).")]
    private float far = 24f;
    [SerializeField, Range(0f, 1f), Tooltip("Volume of every voice, on top of each key's own in Tables/Sounds.")]
    private float volume = 1f;
    [SerializeField, Min(1f), Tooltip("A body this many times the usual size or more is a big one (a brute, a charger).")]
    private float bigSize = 1.15f;
    [SerializeField, Range(.3f, 1.5f), Tooltip("A big body's voice is pitched by this.")]
    private float bigPitch = .8f;
    [SerializeField, Range(0f, 3f), Tooltip("The wind-up cue of an attack aimed at this PC's own player is this many times as loud. A swing that does no harm (an exploder's) stays as it is.")]
    private float signalAtMine = 1.8f;

    [Header("Moans")]
    [SerializeField, Min(0f), Tooltip("A zombie that knows of nobody (calm or searching) moans about this often (s), each wait 0.6 to 1.4 times it. One lying low (a calm pose, a crawl) only breathes, if there are breaths. 0: never.")]
    private float idleEvery = 7f;
    [SerializeField, Min(.1f), Tooltip("No two moans start closer together than this (s), whoever makes them: a crowd keeps to about one moan this often.")]
    private float idleGap = 1.5f;
    [SerializeField, Min(.1f), Tooltip("A moan is at full volume within this (m).")]
    private float idleNear = 2f;
    [SerializeField, Min(1f), Tooltip("A moan is silent from this far (m); a zombie further than this from the listener doesn't moan.")]
    private float idleFar = 10f;

    [Header("Waking, pain and death")]
    [SerializeField, Min(0f), Tooltip("No two wakings are voiced closer together than this (s): a crowd woken at once cries out once.")]
    private float alertGap = 1f;
    [SerializeField, Min(0f), Tooltip("A zombie that cried out in pain doesn't again for this long (s).")]
    private float hurtGap = 1.5f;
    [SerializeField, Min(0f), Tooltip("No two cries of pain start closer together than this (s). One for a hit by this PC's own player waits only for another of its own.")]
    private float hurtAnyGap = .3f;
    [SerializeField, Range(0f, 1f), Tooltip("How many of the slight hits are voiced; more the harder the hit, every one from hurtBig up. A reel or a knockdown always is.")]
    private float hurtShare = .3f;
    [SerializeField, Range(.01f, 1f), Tooltip("A hit that takes this share of the zombie's whole health is a hard one, always voiced.")]
    private float hurtBig = .25f;
    [SerializeField, Min(0f), Tooltip("No two death cries start closer together than this (s). One for a kill by this PC's own player waits only for another of its own.")]
    private float deathGap = .25f;

    private static ZombieVoice instance;
    private AudioSource[] sources;
    // Who each source voices, how pressing that is and since when (real time). A source that has stopped playing is free.
    private GameObject[] speakers;
    private Rank[] ranks;
    private float[] started;
    private int next;
    private SoundBank bank;
    private float nextBankTry, nextVoice, nextMine, nextIdle, nextAlert, nextHurt, nextHurtMine, nextDeath, nextDeathMine;
    // When each zombie (by instance id) may moan next, or cry out in pain again. One whose time is past is forgotten.
    private readonly Dictionary<int, float> idleDue = new Dictionary<int, float>(), hurtDue = new Dictionary<int, float>();
    private readonly List<int> stale = new List<int>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget() => instance = null;

    // Hits and posture breaks reach every PC, once each (CombatDamageEvents, MeleeCombatTarget.AnyStaggered).
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Listen()
    {
        CombatDamageEvents.Damaged -= OnDamaged;
        CombatDamageEvents.Damaged += OnDamaged;
        MeleeCombatTarget.AnyStaggered -= OnStaggered;
        MeleeCombatTarget.AnyStaggered += OnStaggered;
    }

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
        => Application.isPlaying && zombie != null && Get().Attack(zombie, leap, atMine, sinceOwn);

    /// <summary>
    /// A zombie that knew of nobody does now: it noticed someone, was called or was struck awake (ZombieMind, as its
    /// mood changes on each PC). It cries out once, unless another waking was voiced a moment ago.
    /// </summary>
    public static void Alerts(Component zombie)
    {
        if (Application.isPlaying && zombie != null) Get().Alert(zombie);
    }

    /// <summary>A zombie reels or is knocked down (posture breaks here, ZombieBody.Down): a cry of pain not left to chance.</summary>
    public static void Reels(Component zombie)
    {
        if (Application.isPlaying && zombie != null) Get().Hurt(zombie, 1f, true, false);
    }

    /// <summary>
    /// A zombie dies, on each PC as it learns of it (EnemyHealth): whatever it was saying stops, and it gives a last cry
    /// unless the blow burst its head (ZombieGore.BurstsHead) or took it down before it knew anyone was there. Call it
    /// while the body still stands, before it falls as a corpse.
    /// </summary>
    public static void Dies(Component zombie, in DamageHit hit)
    {
        if (Application.isPlaying && zombie != null) Get().Death(zombie, hit);
    }

    // A hit, as each PC hears of it once: a zombie it left alive may cry out. Its death is Dies.
    private static void OnDamaged(CombatDamageReport report)
    {
        if (!Application.isPlaying || report.IsKillingBlow || !(report.Target is EnemyHealth enemy) || !enemy.IsAlive) return;
        Get().Hurt(enemy, report.Applied / Mathf.Max(1f, report.MaximumHealth), false, ByMine(report.Hit));
    }

    // A posture broken, once on each PC: it reels.
    private static void OnStaggered(MeleeCombatTarget target)
    {
        if (target != null && target.TryGetComponent(out EnemyHealth enemy)) Reels(enemy);
    }

    // Landed by this PC's own player.
    private static bool ByMine(in DamageHit hit)
        => hit.Source != null && hit.Source.GetComponentInParent<PlayerMovement>() is PlayerMovement player && player.IsLocal;

    // A zombie this PC has seen die (a copy waiting to be removed) says nothing more.
    private static bool Alive(Component zombie) => !zombie.TryGetComponent(out EnemyHealth health) || health.IsAlive;

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        GameTables.Bind(TableName, TableRow, this);
        sources = new AudioSource[Mathf.Clamp(voices, 1, 16)];
        for (int i = 0; i < sources.Length; i++)
        {
            var voice = new GameObject("Zombie Voice " + i).AddComponent<AudioSource>();
            voice.transform.SetParent(transform, false);
            voice.playOnAwake = false;
            voice.spatialBlend = 1f;
            voice.rolloffMode = AudioRolloffMode.Linear;
            // Less pressing than most sounds (128) until a voice says otherwise as it starts (Voice).
            voice.priority = 160;
            GameAudio.Bind(voice, AudioChannel.Combat);
            sources[i] = voice;
        }
        speakers = new GameObject[sources.Length];
        ranks = new Rank[sources.Length];
        started = new float[sources.Length];
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
    }

    private void Update()
    {
        if (!speak || sources == null || idleEvery <= 0f || Time.time < nextIdle) return;
        nextIdle = Time.time + idleGap * Random.Range(.7f, 1.3f);
        Mutter();
    }

    private bool Attack(Component zombie, bool leap, bool atMine, float sinceOwn)
    {
        if (!speak || sources == null) return false;
        // A leap is over in a third of a second and can pin: it is always heard, whatever the zombie said before.
        if (!leap && sinceOwn < ownGap) return false;
        // No two voices start together. One for this PC's own player waits only for another of its own, and a leap at
        // them for nothing: a voice for somebody else must never take the turn of the zombie about to hit me.
        if (atMine ? !leap && Time.time < nextMine : Time.time < nextVoice) return false;
        if (!atMine && Random.value > othersShare) return false;
        // A leap cuts in on whatever the zombie was saying (its snarl).
        if (!Voice(zombie, leap ? LeapKeys : AttackKeys, atMine ? Rank.AttackMine : Rank.Attack, leap, far, near)) return false;
        nextVoice = Time.time + anyGap;
        if (atMine) nextMine = nextVoice;
        return true;
    }

    // One zombie that knows of nobody, near enough to be heard and whose turn it is, picked at random: it moans, or if
    // it lies low (sitting, eating or crouched in a calm pose, or crawling) only breathes, and with no breaths keeps
    // quiet. Held, down or reeling, it says nothing. Silent, and nothing looked at, while the bank has neither.
    private void Mutter()
    {
        var ears = GameAudio.Listener;
        if (ears == null || !Ready()) return;
        bool moans = Has(IdleKeys), breathes = Has(BreatheKeys);
        if (!moans && !breathes) return;
        Vector3 at = ears.transform.position;
        float reach = idleFar * idleFar;
        ZombieMind chosen = null;
        bool chosenLow = false;
        int count = 0;
        var minds = ZombieMind.All;
        for (int i = 0; i < minds.Count; i++)
        {
            var mind = minds[i];
            if (mind == null || (mind.Mood != ZombieMood.Calm && mind.Mood != ZombieMood.Searching) || !mind.Living) continue;
            if ((mind.EyePoint - at).sqrMagnitude > reach) continue;
            if (idleDue.TryGetValue(mind.GetInstanceID(), out float due) && Time.time < due) continue;
            bool low = mind.HoldsPose || (mind.TryGetComponent(out ZombieType type) && type.Crawls);
            if (!(low ? breathes : moans)) continue;
            if (mind.TryGetComponent(out MeleeCombatTarget melee) && melee.IsStunned) continue;
            // Each one as likely as the others (one pass, keeping the n-th with a chance of 1 in n).
            if (Random.Range(0, ++count) != 0) continue;
            chosen = mind;
            chosenLow = low;
        }
        if (chosen == null) return;
        // Its next turn comes round in about idleEvery, heard or not.
        idleDue[chosen.GetInstanceID()] = Time.time + idleEvery * Random.Range(.6f, 1.4f);
        if (idleDue.Count > 64) Prune(idleDue);
        Voice(chosen, chosenLow ? BreatheKeys : IdleKeys, Rank.Idle, false, idleFar, idleNear);
    }

    private void Alert(Component zombie)
    {
        if (!speak || sources == null || Time.time < nextAlert || !Alive(zombie)) return;
        if (Voice(zombie, AlertKeys, Rank.Alert, false, far, near)) nextAlert = Time.time + alertGap;
    }

    // Share: of its whole health the hit took. Reel: a posture break or a knockdown, voiced whatever the share, cutting in
    // on whatever the zombie was saying (an attack it was winding up is over). Mine: a hit by this PC's own player.
    private void Hurt(Component zombie, float share, bool reel, bool mine)
    {
        if (!speak || sources == null || !Alive(zombie)) return;
        if (mine ? Time.time < nextHurtMine : Time.time < nextHurt) return;
        int id = zombie.gameObject.GetInstanceID();
        if (hurtDue.TryGetValue(id, out float due) && Time.time < due) return;
        // A slight hit only sometimes, the more often the harder it is; a hard one always.
        if (!reel && Random.value > Mathf.Lerp(hurtShare, 1f, share / Mathf.Max(.01f, hurtBig))) return;
        if (!Voice(zombie, HurtKeys, Rank.Hurt, reel, far, near)) return;
        hurtDue[id] = Time.time + hurtGap;
        if (hurtDue.Count > 64) Prune(hurtDue);
        nextHurt = Time.time + hurtAnyGap;
        if (mine) nextHurtMine = nextHurt;
    }

    private void Death(Component zombie, in DamageHit hit)
    {
        if (sources == null) return;
        // Dead, it says nothing more of what it was saying.
        Hush(zombie.gameObject);
        if (!speak || ZombieGore.BurstsHead(hit)) return;
        // Taken down from behind before it knew anyone was there (a stealth kill), it goes quietly; so does one a throwing
        // knife ended unaware, which counts as the same takedown (GunFeelDirector, ThrownKnife).
        if ((hit.Kind == DamageKind.Execution || hit.Method == DamageMethod.Thrown)
            && zombie.TryGetComponent(out ZombieMind mind) && mind.Unaware) return;
        bool mine = ByMine(hit);
        if (mine ? Time.time < nextDeathMine : Time.time < nextDeath) return;
        if (!Voice(zombie, DeathKeys, Rank.Death, true, far, near)) return;
        nextDeath = Time.time + deathGap;
        if (mine) nextDeathMine = nextDeath;
    }

    // Says it from this zombie's mouth, if the bank has clips of it for this body, the zombie is within reach of the
    // listener (m; full volume within full) and a source is to be had (Take). True if it sounds.
    private bool Voice(Component zombie, in Keys keys, Rank rank, bool cuts, float reach, float full)
    {
        if (!Ready()) return false;
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
        if (ears != null && (ears.transform.position - mouth).sqrMagnitude > reach * reach) return false;
        string key = KeyFor(keys, build);
        var clip = key != null ? bank.Pick(key) : null;
        if (clip == null) return false;
        int at = Take(zombie.gameObject, rank, cuts);
        if (at < 0) return false;
        var settings = bank.SettingsOf(key);
        var source = sources[at];
        source.priority = Priorities[(int)rank];
        source.transform.position = mouth;
        source.minDistance = full;
        source.maxDistance = Mathf.Max(full + 1f, reach);
        // Set once, as it starts: from here GameAudio.FollowWorldPitch carries it through a slowed game.
        source.pitch = settings.RandomPitch() * (size >= bigSize ? bigPitch : 1f);
        source.PlayOneShot(clip, Mathf.Clamp01(settings.Volume * volume));
        return true;
    }

    // The source a voice of this rank is to use, stopped, or -1 to stay silent. A zombie has one mouth: what it is still
    // saying gives way only to a more pressing voice of its own, or to one that cuts in (a leap, a reel, its death), and
    // two never sound from it at once. Otherwise a free source; else the least pressing voice no more pressing than this
    // one, the oldest of those. A voice never cuts a more pressing one.
    private int Take(GameObject who, Rank rank, bool cuts)
    {
        int free = -1, own = -1, yields = -1;
        for (int n = 0; n < sources.Length; n++)
        {
            int i = (next + n) % sources.Length;
            if (sources[i] == null) continue;
            if (!sources[i].isPlaying) { if (free < 0) free = i; continue; }
            if (speakers[i] == who) { own = i; continue; }
            if (ranks[i] > rank) continue;
            if (yields < 0 || ranks[i] < ranks[yields] || (ranks[i] == ranks[yields] && started[i] < started[yields])) yields = i;
        }
        int take = own >= 0 ? (cuts || rank > ranks[own] ? own : -1) : free >= 0 ? free : yields;
        if (take < 0) return -1;
        next = (take + 1) % sources.Length;
        sources[take].Stop();
        speakers[take] = who;
        ranks[take] = rank;
        started[take] = Time.unscaledTime;
        return take;
    }

    // Stops whatever this zombie is saying.
    private void Hush(GameObject who)
    {
        for (int i = 0; i < sources.Length; i++)
            if (sources[i] != null && speakers[i] == who && sources[i].isPlaying) sources[i].Stop();
    }

    // The bank, once the editor has made it from the clips; looked for again now and then until it is there.
    private bool Ready()
    {
        if (bank != null) return true;
        if (Time.unscaledTime < nextBankTry) return false;
        nextBankTry = Time.unscaledTime + BankRetry;
        bank = SoundBank.Load(BankId);
        return bank != null;
    }

    // The bank has clips of this voice for some body.
    private bool Has(in Keys keys) => bank.TryGet(keys.Plain, out _) || bank.TryGet(keys.Male, out _) || bank.TryGet(keys.Female, out _);

    // Forgets the zombies whose wait is over: they count as never heard.
    private void Prune(Dictionary<int, float> due)
    {
        stale.Clear();
        foreach (var pair in due)
            if (pair.Value <= Time.time) stale.Add(pair.Key);
        foreach (int id in stale) due.Remove(id);
    }

    // The clips for this voice on this body. Synty's zombies say which they are in their names
    // (SM_Chr_Zombie_Bride_Female_01); a modder's body may say nothing, and not every voice has clips of every sort.
    private string KeyFor(in Keys keys, string build)
    {
        string own = build == null ? null
            : build.IndexOf("_Female_", System.StringComparison.OrdinalIgnoreCase) >= 0 ? keys.Female
            : build.IndexOf("_Male_", System.StringComparison.OrdinalIgnoreCase) >= 0 ? keys.Male : null;
        bool hasPlain = bank.TryGet(keys.Plain, out _), hasOwn = own != null && bank.TryGet(own, out _);
        if (hasOwn && !(hasPlain && Random.value < neutralShare)) return own;
        if (hasPlain) return keys.Plain;
        if (bank.TryGet(keys.Male, out _)) return keys.Male;
        return bank.TryGet(keys.Female, out _) ? keys.Female : null;
    }
}
