using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Callouts (memo 4, the user's "Guts &amp; Blackpowder 스타일 콜아웃 시스템"). Holding the callout key (Settings → 조작;
/// the mouse's back side button until picked otherwise, as V, G&amp;B's key, is the shove here) opens a wheel of eight
/// (CalloutWheel). The mouse points into one and letting go, or a left click, says it. What is said fits the moment, the
/// way G&amp;B's lines do: 좀비 names the zombie under the crosshair, else the most dangerous one in sight, and pings it
/// for the team; 도와줘 says what has hold of the player; 요청 calls the team's Détente or Support by name when there is
/// one; 클래스 tells the class's state (CHARGE ready, the syringe empty…); 이동 pins the spot looked at; 말 opens a page
/// of eight short words. All of that is data: the wheel's slices and pages are rows of Tables/Player/CalloutSlots.csv
/// (a class or a moment may have a slice of its own), the lines and which wins are rows of Callouts.csv (CalloutLines),
/// and when a slice or a line fits is a condition its row writes out of facts (CalloutWhen, CalloutFacts: the class,
/// the campaign's phase and step, a friend down, the magazine empty, the ultimate ready ...), so a mod adds lines,
/// slices and pages without code. The numbers the facts count by are Player.csv's Callout row.
/// The wheel takes the view and the trigger only (CalloutWheel.Holder): the player walks on while it is up.
///
/// Everyone hears the speaker's own voice from where they stand (a Voice bank clip; the Windows TTS stand-ins stay silent
/// unless Player.csv Callout.ttsVoices turns them on), reads the words over their head (CalloutBubbles) and sees their
/// face say it, the mouth moving to the voice or, voiceless, through the words (Callouts.csv face, PlayerFace). Some are shouted without the wheel
/// (Settings → 조작 → 저절로 외치기): help a moment after something takes hold or on going down, the elite first come
/// near, a brute's coming (the player nearest it: its legs) and its fall (whoever shot its leg out: its head; both by
/// Spotted, from ZombieBody.Legs.cs), and the CHARGE, which friends near the Assault answer as G&amp;B's line answers its officer. Guards keep it from
/// spamming: a short gap between calls, a longer one before the same line, and a shout by itself keeps quiet when
/// someone just said the same. This PC's player decides and says; PlayerNetworkCombat tells the other PCs.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerCallouts : MonoBehaviour
{
    // The lines the shouts by themselves pick from (Callouts.csv slot): what a zombie is, and a cry for help. Which
    // slices the wheel has, and on which page, is CalloutSlots' table.
    private const string ZombieSlot = "zombie", HelpSlot = "help";
    /// <summary>Data/Resources/Tables/Player/Player.csv, row Callout: the guards and what the facts count as a horde, hurt and so on.</summary>
    public const string TableName = "Player", TableRow = "Callout";
    [Tooltip("Seconds between two of this player's callouts.")]
    public float callGap = .9f;
    [Tooltip("Seconds before the same line can be said again.")]
    public float sameLineGap = 3f;
    [Tooltip("A shout by itself waits this long after the last one (s).")]
    public float autoGap = 6f;
    [Tooltip("A shout by itself keeps quiet when anyone said the same line within this (s).")]
    public float heardGap = 5f;
    [Tooltip("Help is shouted this long after something takes hold, so a grab shaken off at once says nothing (s).")]
    public float heldShout = .35f;
    [Tooltip("A page of the wheel other than the first closes by itself when left this long (real s), as G&B's wheel can't be left open.")]
    public float pageSeconds = 6f;
    [Tooltip("How far a zombie can be named (m).")]
    public float sightReach = 45f;
    [Tooltip("The cone round the crosshair a zombie must be in to be named (degrees).")]
    public float sightCone = 35f;
    [Tooltip("Awake zombies within this count toward a horde (m).")]
    public float hordeReach = 20f;
    [Tooltip("Awake zombies within this count as right on top of the player (m).")]
    public float nearReach = 4f;
    [Tooltip("How far a spot can be pinned (m).")]
    public float spotReach = 60f;
    [Tooltip("So many awake zombies about are a horde (the fact 'horde').")]
    public int hordeCount = 8;
    [Tooltip("So many awake zombies on top of the player and they are surrounded (the fact 'surrounded').")]
    public int nearCount = 4;
    [Tooltip("Health share under which the player is hurt enough to cry for help (the fact 'hurt').")]
    public float hurtShare = .35f;
    [Tooltip("Health share under which the player wants a heal (the fact 'heal').")]
    public float healShare = .6f;
    [Tooltip("How long a thanks remembers its reason: being freed, got up or healed (s).")]
    public float thanksSeconds = 8f;
    [Tooltip("The text-to-speech stand-in voices play. Read from the table where a voice is heard (Voice), on any PC; here so the row has its field.")]
    public bool ttsVoices;
    // The crosshair's cast is this fat (m). The callout voice: full within this distance, gone at that (m); a shout
    // carries farther than a grunt; and where the mouth is above the feet.
    private const float Radius = .3f, VoiceNear = 3f, VoiceFar = 60f, MouthHeight = 1.6f;

    /// <summary>This PC's player's.</summary>
    public static PlayerCallouts Local { get; private set; }

    /// <summary>
    /// This PC's player said this line: the zombie it pinged (or null), and the spot it pinned when the bool says so.
    /// PlayerNetworkCombat tells the other PCs.
    /// </summary>
    public event Action<string, EnemyHealth, Vector3, bool, uint> Called;

    // When each line was last heard on this PC from anyone.
    private static readonly Dictionary<string, float> heard = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

    private PlayerMovement movement;
    private PlayerHold hold;
    private WeaponController weapon;
    private ClassKit kit;
    private InputAction key;
    private readonly RaycastHit[] hits = new RaycastHit[32];
    private readonly RaycastHit[] sight = new RaycastHit[16];
    private readonly string[] names = new string[CalloutSlots.Places], lines = new string[CalloutSlots.Places];
    // The page the wheel is on, and its slices as they stand now (CalloutSlots.Resolve: a place may be empty).
    private readonly CalloutSlots.Slot[] slices = new CalloutSlots.Slot[CalloutSlots.Places], peek = new CalloutSlots.Slot[CalloutSlots.Places];
    private string page = CalloutSlots.FirstPage;
    private string lastLine;
    private float lastCall = -99f, lastLineAt = -99f, lastAuto = -99f;
    private float heldSince = -1f, lastHealth = -1f, joinAt = -1f, pageAt;
    private bool wasHeld, wasDowned, wasDead;
    private int seed;
    // What the player has before them now (Sense, Track): the zombie a line would be about, a spot to pin, the crowd,
    // and when they were last freed, got up or healed. The facts read it (CalloutFacts).
    private readonly CalloutSense sense = new CalloutSense();
    // Kept, so asking what fits allocates nothing each frame the wheel is up.
    private Func<string, bool> fits;
    private CalloutWhen.Fact fact;
    private bool OnFirstPage => page == CalloutSlots.FirstPage;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Local = null;
        heard.Clear();
    }

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        hold = GetComponent<PlayerHold>();
        weapon = GetComponent<WeaponController>();
        GameTables.Bind(TableName, TableRow, this);
        fits = Fits;
        fact = (name, word) => CalloutFacts.Holds(sense, name, word);
    }

    private void OnDisable()
    {
        if (Local == this)
        {
            Local = null;
            CalloutWheel.Close();
        }
        if (kit != null) kit.Charged -= OnCharged;
        kit = null;
    }

    private void Update()
    {
        if (movement == null || !movement.IsLocal) return;
        Local = this;
        // The player's parts may be added after this one.
        if (hold == null) hold = GetComponent<PlayerHold>();
        if (weapon == null) weapon = GetComponent<WeaponController>();
        // The Assault's own CHARGE.
        if (kit == null && TryGetComponent(out kit)) kit.Charged += OnCharged;
        Track();
        if (joinAt >= 0f && Time.time >= joinAt)
        {
            joinAt = -1f;
            Shout(CalloutLines.Find("auto.charge.join"));
        }
        Wheel();
    }

    // ---- Shouted by themselves ----

    /// <summary>
    /// This PC's player first came near this elite (ZombieElite): they shout what it is, as a friend beside them would,
    /// when shouting by themselves is on (Settings → 조작).
    /// </summary>
    public static void Spotted(EnemyHealth enemy)
    {
        var mine = Local;
        if (mine == null || enemy == null || !enemy.IsAlive) return;
        mine.Sense();
        mine.sense.Target = enemy;
        mine.sense.Aimed = false;
        mine.Shout(mine.Pick(ZombieSlot));
    }

    /// <summary>
    /// An Assault's CHARGE reached this PC's player (ClassKit.ReceiveCharge): they answer it a moment later, each a little
    /// apart, as G&amp;B's line answers its officer.
    /// </summary>
    public static void JoinCharge()
    {
        var mine = Local;
        if (mine != null && mine.joinAt < 0f) mine.joinAt = Time.time + UnityEngine.Random.Range(.2f, .6f);
    }

    private void OnCharged(Vector3 at) => Shout(CalloutLines.Find("auto.charge.lead"));

    /// <summary>
    /// This PC's player threw their class throwable (PlayerGrenades): they shout its warning line by themselves ("수류탄!",
    /// "섬광!"), as a soldier does so friends look away or take cover, when shouting by themselves is on.
    /// </summary>
    public static void Thrown(string lineId)
    {
        var mine = Local;
        if (mine == null || string.IsNullOrEmpty(lineId)) return;
        mine.Shout(CalloutLines.Find(lineId));
    }

    // The edges the lines care about (freed, raised, healed), and help shouted when something takes hold or they go down.
    private void Track()
    {
        bool dead = movement.IsDead, held = movement.IsHeld && !dead, downed = movement.IsDowned && !dead;
        if (held && !wasHeld) heldSince = Time.time;
        if (!held) heldSince = -1f;
        if (!held && wasHeld && !dead) sense.FreedAt = Time.time;
        if (!downed && wasDowned && !dead) sense.RaisedAt = Time.time;
        if ((heldSince >= 0f && Time.time - heldSince >= heldShout) || (downed && !wasDowned))
        {
            heldSince = -1f;
            Sense();
            Shout(Pick(HelpSlot));
        }
        // Health up by another's hand (not this player's own syringe, nor coming back to life): a thanks has its reason.
        float health = movement.DamageReceiver != null ? movement.DamageReceiver.Health : 0f;
        bool ownDose = DetenteKit.Local != null && DetenteKit.Local.SinceInject < .5f;
        if (!dead && !wasDead && lastHealth > 0f && health > lastHealth + 5f && !ownDose) sense.HealedAt = Time.time;
        lastHealth = health;
        wasHeld = held;
        wasDowned = downed;
        wasDead = dead;
    }

    private void Shout(CalloutLines.Line line)
    {
        if (GameSettings.Current.calloutAuto && !movement.IsDead) Say(line, true);
    }

    // ---- The wheel ----

    private void Wheel()
    {
        if (key == null) key = KeyBindings.Find("Player/Callout");
        bool open = CalloutWheel.IsOpen;
        // The wheel is a screen, not an action of the body: it closes when its key or the mouse stops being the player's.
        if (movement.IsDead || !movement.InputAvailable || !TakesInput())
        {
            if (open) CalloutWheel.Close();
            return;
        }
        // The wheel's own pointing stays on the mouse (move to aim at a slot, left click says, right click closes): it is
        // a pointer menu, and only the key that holds it open is the player's to choose.
        var mouse = Mouse.current;
        bool pressed = key != null && key.WasPressedThisFrame();
        bool released = key != null && key.WasReleasedThisFrame();
        if (!open)
        {
            if (!pressed) return;
            page = CalloutSlots.FirstPage;
            // One seed for this opening: among lines that fit equally, the one shown is the one said.
            seed = UnityEngine.Random.Range(0, 1 << 20);
            Sense();
            Fill();
            CalloutWheel.Open(names, lines, Hint(), null);
            return;
        }
        if (mouse != null) CalloutWheel.Steer(mouse.delta.ReadValue());
        Sense();
        Fill();
        CalloutWheel.Show(names, lines, Hint());
        int hovered = CalloutWheel.Hovered;
        bool cancel = (mouse != null && mouse.rightButton.wasPressedThisFrame) || (!OnFirstPage && pressed)
            || (!OnFirstPage && Time.unscaledTime - pageAt > pageSeconds);
        if (cancel)
        {
            CalloutWheel.Close();
            return;
        }
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            if (hovered >= 0) Choose(hovered);
            return;
        }
        // A page opened from the wheel (말's words) stays up after the key is let go: a slice is clicked there.
        if (!released || !OnFirstPage) return;
        if (GameSettings.Current.calloutOnRelease && hovered >= 0) Choose(hovered);
        else CalloutWheel.Close();
    }

    private void Choose(int index)
    {
        var slot = index >= 0 && index < slices.Length ? slices[index] : null;
        // A place nothing fits is an empty slice: pointing at it says nothing and leaves the wheel as it is.
        if (slot == null) return;
        if (!string.IsNullOrEmpty(slot.Opens))
        {
            page = slot.Opens;
            pageAt = Time.unscaledTime;
            Fill();
            CalloutWheel.Page(names, lines, Hint(), slot.Title);
            return;
        }
        var line = Pick(slot.Id, seed);
        CalloutWheel.Close();
        Say(line, false);
    }

    // The page's slices as they stand now (CalloutSlots), each with its name and what it would say.
    private void Fill()
    {
        CalloutSlots.Resolve(page, fits, slices);
        for (int i = 0; i < slices.Length; i++)
        {
            var slot = slices[i];
            names[i] = slot != null ? slot.Title : "";
            if (slot == null) lines[i] = "";
            else if (!string.IsNullOrEmpty(slot.Opens)) lines[i] = Preview(slot.Opens);
            else
            {
                var line = Pick(slot.Id, seed);
                lines[i] = line != null ? line.Text : "";
            }
        }
    }

    // What a slice that opens a page shows under its name: three of that page's slices (top, bottom, lower left), as
    // "좋아 · 안 돼 · 미안 …" tells what 말 holds.
    private static readonly int[] PreviewPlaces = { 0, 4, 6 };
    private readonly System.Text.StringBuilder preview = new System.Text.StringBuilder();
    private string Preview(string opens)
    {
        CalloutSlots.Resolve(opens, fits, peek);
        preview.Clear();
        foreach (int place in PreviewPlaces)
        {
            if (place >= peek.Length || peek[place] == null) continue;
            if (preview.Length > 0) preview.Append(" · ");
            preview.Append(peek[place].Title);
        }
        return preview.Length > 0 ? preview.Append(" …").ToString() : "";
    }

    private string Hint() => Texts.Get(!OnFirstPage ? "callout.hint.page"
        : GameSettings.Current.calloutOnRelease ? "callout.hint.release" : "callout.hint.click");

    // The wheel's key is one of the body's keys, and the wheel points with the mouse: both must be the player's. The
    // wheel's own hold of the view doesn't count against it (InputHold.AnyBesides); another screen's does, as the Tab
    // board's pointer.
    private static bool TakesInput() => InputFocus.TakesPlayInput() && !InputHold.AnyBesides(CalloutWheel.Holder, Held.Look)
        && !GunCustomizeMenu.TakesKeyboard;

    // ---- Saying ----

    private CalloutLines.Line Pick(string slot, int pickSeed = -1) => CalloutLines.Pick(slot, fits, pickSeed);

    // Says it on this PC (and, through Called, on every other): the guards first.
    private bool Say(CalloutLines.Line line, bool auto)
    {
        if (line == null) return false;
        // A feeling this body can't show now (off its feet, its hands kept by a reload or a grenade) is never sent.
        if (line.Emote != null && (!TryGetComponent(out PlayerBody body) || !body.CanEmote(line.Emote))) return false;
        float now = Time.time;
        if (now - lastCall < callGap || (line.Id == lastLine && now - lastLineAt < sameLineGap)) return false;
        if (auto && (now - lastAuto < autoGap || (heard.TryGetValue(line.Id, out float at) && now - at < heardGap))) return false;
        lastCall = lastLineAt = now;
        lastLine = line.Id;
        if (auto) lastAuto = now;
        var marked = line.MarksTarget && sense.Target != null && sense.Target.IsAlive ? sense.Target : null;
        bool pins = line.MarksPoint && sense.HasSpot;
        // The shout's own seed goes with it: every PC plays the same take of the line, its mouth with it (SharedRandom;
        // SyncAudit.md, item 13).
        uint seed = SharedRandom.Seed(SharedRandom.LocalClient, ++shouts, 0xCA11u);
        using (SharedRandom.Scope(seed)) Hear(movement, line.Id, marked, sense.Spot, pins);
        Called?.Invoke(line.Id, marked, sense.Spot, pins, seed);
        return true;
    }

    // This PC's player's shouts so far, for each one's seed.
    private ulong shouts;

    /// <summary>
    /// A callout said by speaker, on this PC: their voice from where they stand, their words over their head (this
    /// player's own under the crosshair), the zombie it is about pinged for the team, the spot it points at pinned.
    /// </summary>
    public static void Hear(PlayerMovement speaker, string lineId, EnemyHealth target, Vector3 point, bool pins)
    {
        var line = CalloutLines.Find(lineId);
        if (speaker == null || line == null) return;
        heard[line.Id] = Time.time;
        // A feeling from the 감정 page is shown, not said (the user, 2026-11-22: "몸과 표정만, 말 없음"): the whole body plays
        // its clip and the face wears its mood for as long, with no voice, words or comms feed (PlayerBody.Emote).
        if (line.Emote != null)
        {
            float seconds = speaker.TryGetComponent(out PlayerBody body) ? body.Emote(line.Emote) : 0f;
            if (seconds > 0f) PlayerFace.Emoted(speaker, line, seconds);
            return;
        }
        var mouth = Voice(speaker, line.VoiceKey);
        CalloutBubbles.Say(speaker, line.Text);
        // Their face says it too (Callouts.csv face), the mouth moving to the voice (PlayerFace, CharacterFace), their body
        // with a nod or a point at what it is about (Callouts.csv gesture), and their face on the comms feed (CalloutFeedHUD).
        Vector3 toward = target != null ? target.transform.position + Vector3.up * 1.2f : point;
        PlayerFace.Said(speaker, line, mouth, toward, target != null || pins);
        // A Schießhund's bounty mark stays theirs: a plain ping on its enemy would take it over (PingMarks.Add).
        if (target != null && target.IsAlive && !Bountied(target)) PingMarks.Ping(target, speaker, false);
        if (pins) CalloutBubbles.Pin(speaker, point);
    }

    // Its own mouth, apart from the grunts' (CharacterSounds), so a blow taken mid-shout doesn't cut the words off.
    // Returns the source it plays on (the face's mouth follows it), null with no clip.
    private static AudioSource Voice(PlayerMovement speaker, string key)
    {
        var bank = SoundBank.Load("Voice");
        var clip = bank != null ? bank.Pick(key) : null;
        if (clip == null || (IsStandIn(clip) && !GameTables.Flag(VoiceTable, StandInRow, StandInField, false))) return null;
        var mouth = speaker.transform.Find("Callout Voice");
        AudioSource source;
        if (mouth == null)
        {
            source = new GameObject("Callout Voice").AddComponent<AudioSource>();
            source.transform.SetParent(speaker.transform, false);
            source.transform.localPosition = Vector3.up * MouthHeight;
            source.playOnAwake = false;
            source.rolloffMode = AudioRolloffMode.Logarithmic;
            source.minDistance = VoiceNear;
            source.maxDistance = VoiceFar;
            GameAudio.Bind(source, AudioChannel.Voice);
        }
        else source = mouth.GetComponent<AudioSource>();
        if (source == null) return null;
        // This player hears themself flat; anyone else from their mouth.
        source.spatialBlend = speaker.IsLocal ? 0f : 1f;
        var settings = bank.SettingsOf(key);
        source.Stop();
        source.clip = clip;
        source.pitch = Mathf.Max(.1f, settings.RandomPitch());
        GameAudio.SetGain(source, Mathf.Clamp01(settings.Volume));
        source.Play();
        return source;
    }

    // A text-to-speech stand-in (Tools/Audio/callout_voices.py writes "<id>_tts.wav"). They stay silent unless the table
    // turns them on (Player.csv Callout.ttsVoices; the user, 2026-11-13: "음성도 구글 TTS 쓰지 말고 일단 disable"): the line
    // is said voiceless then, its mouth moving through its words (CharacterFace.Speak), and a real recording dropped into
    // the same folder plays as before.
    // A key table reads "Row.field" keys as rows and fields (CsvTable): Callout.ttsVoices is row Callout, field ttsVoices.
    private const string VoiceTable = "Player", StandInRow = "Callout", StandInField = "ttsVoices";

    private static bool IsStandIn(AudioClip clip) => clip.name.EndsWith("_tts", StringComparison.OrdinalIgnoreCase);

    private static bool Bountied(EnemyHealth enemy)
    {
        foreach (var player in PlayerRoster.All)
            if (player != null && PingMarks.IsBountyOf(enemy, player)) return true;
        return false;
    }

    // ---- What fits now ----

    // A row's condition (Callouts.csv and CalloutSlots.csv, column when): facts joined by & | ! (CalloutWhen), each
    // answered by CalloutFacts from what this player has before them (sense). A word no fact answers fits nothing; the
    // console's /callouts names every such word in the tables.
    private bool Fits(string when) => CalloutWhen.Fits(when, fact);

    /// <summary>What this PC's player has before them now, freshly sensed: for the console's /callouts.</summary>
    internal CalloutSense SenseNow()
    {
        Sense();
        return sense;
    }

    // How dangerous a kind is to call out: the order of its own line.
    private static int Rank(EnemyHealth enemy) => CalloutLines.OrderOf(ZombieSlot, "kind:" + CalloutSense.KindOf(enemy));

    // What the player looks at: the enemy under the crosshair, else the most dangerous zombie in sight (the nearest of
    // equals); the spot looked at when no enemy is; and how many awake zombies are about.
    private void Sense()
    {
        sense.Speaker = movement;
        sense.Hold = hold;
        sense.Weapon = weapon;
        sense.HordeCount = hordeCount;
        sense.NearCount = nearCount;
        sense.HurtShare = hurtShare;
        sense.HealShare = healShare;
        sense.ThanksSeconds = thanksSeconds;
        sense.Target = null;
        sense.Aimed = sense.HasSpot = false;
        sense.Crowd = sense.Close = 0;
        var eyes = PlayerView.EyesOf(movement);
        if (eyes == null) return;
        Vector3 from = eyes.position, ahead = eyes.forward;
        int count = Physics.SphereCastNonAlloc(from, Radius, ahead, hits, sightReach + 75f, ~0, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        RaycastHit first = default;
        for (int i = 0; i < count; i++)
        {
            var hit = hits[i];
            if (hit.collider == null || hit.collider.transform.IsChildOf(transform)) continue;
            // A cast that starts inside something reports it at no distance: only an enemy's body counts there.
            if (hit.distance <= 0f && hit.collider.GetComponentInParent<EnemyHealth>() == null) continue;
            if (hit.distance < nearest)
            {
                nearest = hit.distance;
                first = hit;
            }
        }
        EnemyHealth aimedAt = null;
        if (first.collider != null)
        {
            var enemy = first.collider.GetComponentInParent<EnemyHealth>();
            if (enemy != null && enemy.IsAlive) aimedAt = enemy;
            else if (enemy == null && first.distance <= spotReach && first.collider.GetComponentInParent<PlayerMovement>() == null)
            {
                sense.Spot = first.point;
                sense.HasSpot = true;
            }
        }
        int best = int.MinValue;
        float bestDistance = float.PositiveInfinity, cone = Mathf.Cos(sightCone * Mathf.Deg2Rad);
        EnemyHealth seen = null;
        foreach (var enemy in EnemyHealth.Living)
        {
            if (enemy == null || !enemy.IsAlive) continue;
            float distance = Vector3.Distance(enemy.transform.position, transform.position);
            bool awake = !enemy.TryGetComponent(out ZombieMind mind) || mind.IsAwake;
            if (awake && distance < hordeReach) sense.Crowd++;
            if (awake && distance < nearReach) sense.Close++;
            if (aimedAt != null || distance > sightReach) continue;
            Vector3 body = enemy.transform.position + Vector3.up * 1.2f;
            if (Vector3.Dot((body - from).normalized, ahead) < cone) continue;
            int rank = Rank(enemy);
            if (rank < best || (rank == best && distance >= bestDistance) || !InSight(from, body, enemy)) continue;
            best = rank;
            bestDistance = distance;
            seen = enemy;
        }
        sense.Target = aimedAt != null ? aimedAt : seen;
        sense.Aimed = aimedAt != null;
    }

    // Nothing but this player stands between the eyes and the enemy's body.
    private bool InSight(Vector3 from, Vector3 body, EnemyHealth enemy)
    {
        Vector3 way = body - from;
        int count = Physics.RaycastNonAlloc(from, way.normalized, sight, way.magnitude, ~0, QueryTriggerInteraction.Ignore);
        float nearest = float.PositiveInfinity;
        Collider first = null;
        for (int i = 0; i < count; i++)
        {
            var hit = sight[i];
            if (hit.collider == null || hit.collider.transform.IsChildOf(transform)) continue;
            if (hit.distance < nearest)
            {
                nearest = hit.distance;
                first = hit.collider;
            }
        }
        return first == null || first.GetComponentInParent<EnemyHealth>() == enemy;
    }
}
