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
/// of eight short words. The lines, when each fits and which wins are rows of Tables/Callouts.csv (CalloutLines).
///
/// Everyone hears the speaker's own voice from where they stand (a Voice bank clip, Windows TTS stand-ins until there
/// are recordings), reads the words over their head (CalloutBubbles) and sees their face say it, the mouth moving to
/// the voice (Callouts.csv face, PlayerFace). Some are shouted without the wheel
/// (Settings → 조작 → 저절로 외치기): help a moment after something takes hold or on going down, the elite first come
/// near, a brute's coming (the player nearest it: its legs) and its fall (whoever shot its leg out: its head; both by
/// Spotted, from ZombieBody.Legs.cs), and the CHARGE, which friends near the Assault answer as G&amp;B's line answers its officer. Guards keep it from
/// spamming: a short gap between calls, a longer one before the same line, and a shout by itself keeps quiet when
/// someone just said the same. This PC's player decides and says; PlayerNetworkCombat tells the other PCs.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerCallouts : MonoBehaviour
{
    // The wheel clockwise from the top, and the page 말 opens (Tables/Callouts.csv slots).
    private static readonly string[] Slots = { "zombie", "move", "request", "class", "talk", "retreat", "help", "thanks" };
    private static readonly string[] Words = { "yes", "follow", "nice", "hurry", "no", "wait", "sorry", "quiet" };
    private const string TalkSlot = "talk", ZombieSlot = "zombie", HelpSlot = "help";
    // Guards (s): between two of this player's callouts, before the same line again; a shout by itself also waits AutoGap
    // after the last one, and keeps quiet when anyone said the same line within HeardGap.
    private const float Gap = .9f, SameGap = 3f, AutoGap = 6f, HeardGap = 5f;
    // Help is shouted this long after something takes hold, so a grab shaken off at once says nothing (s).
    private const float HeldShout = .35f;
    // The 말 page closes by itself when left this long (real s), as G&B's wheel can't be left open.
    private const float PageSeconds = 6f;
    // Looking: how far a zombie can be named (m), the cone round the crosshair it must be in (°), the reach of the crowd
    // counted for horde and surrounded, how far a spot can be pinned; the crosshair's cast is this fat (m).
    private const float SightReach = 45f, SightCone = 35f, HordeReach = 20f, NearReach = 4f, SpotReach = 60f, Radius = .3f;
    private const int HordeCount = 8, NearCount = 4;
    // Health shares: low enough to cry for help, to want a heal. How long a thanks remembers its reason (s).
    private const float HurtShare = .35f, HealShare = .6f, ThanksSeconds = 8f;
    // The callout voice: full within this distance, gone at that (m); a shout carries farther than a grunt.
    private const float VoiceNear = 3f, VoiceFar = 60f, MouthHeight = 1.6f;

    /// <summary>This PC's player's.</summary>
    public static PlayerCallouts Local { get; private set; }

    /// <summary>
    /// This PC's player said this line: the zombie it pinged (or null), and the spot it pinned when the bool says so.
    /// PlayerNetworkCombat tells the other PCs.
    /// </summary>
    public event Action<string, EnemyHealth, Vector3, bool> Called;

    // When each line was last heard on this PC from anyone.
    private static readonly Dictionary<string, float> heard = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

    private PlayerMovement movement;
    private PlayerHold hold;
    private WeaponController weapon;
    private ClassKit kit;
    private InputAction key;
    private readonly RaycastHit[] hits = new RaycastHit[32];
    private readonly RaycastHit[] sight = new RaycastHit[16];
    private readonly string[] names = new string[8], lines = new string[8];
    private string lastLine;
    private float lastCall = -99f, lastLineAt = -99f, lastAuto = -99f;
    private float freedAt = -99f, raisedAt = -99f, healedAt = -99f, heldSince = -1f, lastHealth = -1f, joinAt = -1f, pageAt;
    private bool wasHeld, wasDowned, wasDead, onPage;
    private int seed;
    // What the player looks at now (Sense): the zombie a line would be about and whether the crosshair is on it, a spot to
    // pin, and how many awake zombies are about and right on top of them.
    private EnemyHealth target;
    private bool aimed, hasSpot;
    private Vector3 spot;
    private int crowd, close;

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
    }

    private void OnDisable()
    {
        if (Local == this)
        {
            Local = null;
            CalloutWheel.Close(false);
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
        mine.target = enemy;
        mine.aimed = false;
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

    // The edges the lines care about (freed, raised, healed), and help shouted when something takes hold or they go down.
    private void Track()
    {
        bool dead = movement.IsDead, held = movement.IsHeld && !dead, downed = movement.IsDowned && !dead;
        if (held && !wasHeld) heldSince = Time.time;
        if (!held) heldSince = -1f;
        if (!held && wasHeld && !dead) freedAt = Time.time;
        if (!downed && wasDowned && !dead) raisedAt = Time.time;
        if ((heldSince >= 0f && Time.time - heldSince >= HeldShout) || (downed && !wasDowned))
        {
            heldSince = -1f;
            Sense();
            Shout(Pick(HelpSlot));
        }
        // Health up by another's hand (not this player's own syringe, nor coming back to life): a thanks has its reason.
        float health = movement.DamageReceiver != null ? movement.DamageReceiver.Health : 0f;
        bool ownDose = DetenteKit.Local != null && DetenteKit.Local.SinceInject < .5f;
        if (!dead && !wasDead && lastHealth > 0f && health > lastHealth + 5f && !ownDose) healedAt = Time.time;
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
        if (movement.IsDead || !movement.InputAvailable || !TakesInput())
        {
            if (open) CalloutWheel.Close(false);
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
            onPage = false;
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
        bool cancel = (mouse != null && mouse.rightButton.wasPressedThisFrame) || (onPage && pressed)
            || (onPage && Time.unscaledTime - pageAt > PageSeconds);
        if (cancel)
        {
            CalloutWheel.Close(false);
            return;
        }
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            if (hovered >= 0) Choose(hovered, true);
            return;
        }
        // The 말 page stays up after the key is let go: a word is clicked there.
        if (!released || onPage) return;
        if (GameSettings.Current.calloutOnRelease && hovered >= 0) Choose(hovered, false);
        else CalloutWheel.Close(false);
    }

    private void Choose(int index, bool clicked)
    {
        if (!onPage && Slots[index] == TalkSlot)
        {
            onPage = true;
            pageAt = Time.unscaledTime;
            Fill();
            CalloutWheel.Page(names, lines, Hint(), Texts.Get("callout.slot." + TalkSlot));
            return;
        }
        var line = Pick(onPage ? Words[index] : Slots[index], seed);
        // A click that picked mustn't also fire the gun once the wheel lets go of the hands.
        CalloutWheel.Close(clicked);
        Say(line, false);
    }

    // Each slot's name and what it would say now.
    private void Fill()
    {
        var slots = onPage ? Words : Slots;
        for (int i = 0; i < slots.Length; i++)
        {
            names[i] = Texts.Get("callout.slot." + slots[i]);
            if (!onPage && slots[i] == TalkSlot)
            {
                lines[i] = Texts.Get("callout.slot." + Words[0]) + " · " + Texts.Get("callout.slot." + Words[4]) + " · "
                    + Texts.Get("callout.slot." + Words[6]) + " …";
                continue;
            }
            var line = Pick(slots[i], seed);
            lines[i] = line != null ? line.Text : "";
        }
    }

    private string Hint() => Texts.Get(onPage ? "callout.hint.page"
        : GameSettings.Current.calloutOnRelease ? "callout.hint.release" : "callout.hint.click");

    private static bool TakesInput() => Cursor.lockState == CursorLockMode.Locked && Application.isFocused && !DevConsole.TakesKeyboard
        && !ShopMenu.TakesKeyboard && !PerkCardScreen.TakesKeyboard && !GunCustomizeMenu.TakesKeyboard;

    // ---- Saying ----

    private CalloutLines.Line Pick(string slot, int pickSeed = -1) => CalloutLines.Pick(slot, Fits, pickSeed);

    // Says it on this PC (and, through Called, on every other): the guards first.
    private bool Say(CalloutLines.Line line, bool auto)
    {
        if (line == null) return false;
        float now = Time.time;
        if (now - lastCall < Gap || (line.Id == lastLine && now - lastLineAt < SameGap)) return false;
        if (auto && (now - lastAuto < AutoGap || (heard.TryGetValue(line.Id, out float at) && now - at < HeardGap))) return false;
        lastCall = lastLineAt = now;
        lastLine = line.Id;
        if (auto) lastAuto = now;
        var marked = line.MarksTarget && target != null && target.IsAlive ? target : null;
        bool pins = line.MarksPoint && hasSpot;
        Hear(movement, line.Id, marked, spot, pins);
        Called?.Invoke(line.Id, marked, spot, pins);
        return true;
    }

    /// <summary>
    /// A callout said by speaker, on this PC: their voice from where they stand, their words over their head (this
    /// player's own under the crosshair), the zombie it is about pinged for the team, the spot it points at pinned.
    /// </summary>
    public static void Hear(PlayerMovement speaker, string lineId, EnemyHealth target, Vector3 point, bool pins)
    {
        var line = CalloutLines.Find(lineId);
        if (speaker == null || line == null) return;
        heard[line.Id] = Time.time;
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
        if (clip == null) return null;
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

    private static bool Bountied(EnemyHealth enemy)
    {
        foreach (var player in PlayerRoster.All)
            if (player != null && PingMarks.IsBountyOf(enemy, player)) return true;
        return false;
    }

    // ---- What fits now ----

    // A row's condition (Tables/Callouts.csv when): its words are listed in the table's #설명 row.
    private bool Fits(string when)
    {
        if (string.IsNullOrEmpty(when) || when == "always") return true;
        if (when.StartsWith("kind:", StringComparison.OrdinalIgnoreCase))
            return target != null && string.Equals(KindOf(target), when.Substring(5), StringComparison.OrdinalIgnoreCase);
        // That kind, down on its belly crawling (a brute whose leg was shot out: its head is the way now).
        if (when.StartsWith("crawl:", StringComparison.OrdinalIgnoreCase))
            return target != null && target.TryGetComponent(out ZombieType crawler) && crawler.Crawls
                && string.Equals(KindOf(target), when.Substring(6), StringComparison.OrdinalIgnoreCase);
        switch (when)
        {
            case "none": return target == null;
            case "horde": return crowd >= HordeCount;
            case "pinned": return hold != null && hold.IsPinned;
            case "hooked": return hold != null && hold.IsDragged;
            case "eaten": return hold != null && hold.IsEaten;
            case "grabbed": return movement.IsHeld;
            case "downed": return movement.IsDowned;
            case "surrounded": return close >= NearCount;
            case "hurt": return HealthShare() < HurtShare;
            case "heal": return HealthShare() < HealShare;
            case "heal.detente": return HealthShare() < HealShare && TeamHas(PlayerClass.Detente);
            case "ammo": return LowOnAmmo();
            case "ammo.support": return LowOnAmmo() && TeamHas(PlayerClass.Support);
            case "reloading": return weapon != null && weapon.Runtime != null && weapon.Runtime.IsReloading;
            case "spot": return hasSpot;
            case "objective": return CampaignRun.TryObjective(out _, out _, out _);
            case "freed": return Time.time - freedAt < ThanksSeconds;
            case "revived": return Time.time - raisedAt < ThanksSeconds;
            case "healed": return Time.time - healedAt < ThanksSeconds;
            case "knight.taunt": return PlayerClass.Is(PlayerClass.Knight) && ClassKit.Local != null && ClassKit.Local.TauntReady;
            case "assault.charge": return ClassKit.Local != null && ClassKit.Local.ChargeReady;
            case "ghost.elite": return PlayerClass.Is(PlayerClass.Ghost) && aimed && target != null && PlayerClass.IsElite(target);
            case "detente.full": return PlayerClass.Is(PlayerClass.Detente) && DetenteKit.Local != null && !DetenteKit.Local.Empty;
            case "detente.empty": return PlayerClass.Is(PlayerClass.Detente) && DetenteKit.Local != null && DetenteKit.Local.Empty;
            case "schiesshund.mark": return PlayerClass.Is(PlayerClass.Schiesshund) && PingMarks.BountyOf(movement) != null;
            case "support.full":
                return PlayerClass.Is(PlayerClass.Support) && SupportKit.Local != null && SupportKit.Local.Supply >= SupportKit.Local.GiveCost;
            case "support.empty":
                return PlayerClass.Is(PlayerClass.Support) && SupportKit.Local != null && SupportKit.Local.Supply < SupportKit.Local.GiveCost;
            // Only the wheel's own calls say these; the auto ones are found by id.
            case "charge.lead":
            case "charge.join":
                return false;
            // A class's id alone: the line is that class's.
            default: return PlayerClass.Is(when);
        }
    }

    private float HealthShare()
    {
        var receiver = movement.DamageReceiver;
        return receiver != null ? receiver.Health / Mathf.Max(1f, receiver.MaximumHealth) : 1f;
    }

    // The gun in hand has a magazine or less left besides the one in it.
    private bool LowOnAmmo()
    {
        var runtime = weapon != null ? weapon.Runtime : null;
        return runtime != null && runtime.Data != null && runtime.ReserveAmmo <= runtime.Data.MagazineSize;
    }

    // Someone else in the room plays this class.
    private bool TeamHas(string classId)
    {
        foreach (var player in PlayerRoster.All)
            if (player != null && player != movement && !player.IsDead && player.TryGetComponent(out PlayerTally tally)
                && string.Equals(tally.ClassId, classId, StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    private static string KindOf(EnemyHealth enemy)
        => enemy != null && enemy.TryGetComponent(out ZombieType type) ? type.Kind.ToString().ToLowerInvariant() : "shambler";

    // How dangerous a kind is to call out: the order of its own line.
    private static int Rank(EnemyHealth enemy) => CalloutLines.OrderOf(ZombieSlot, "kind:" + KindOf(enemy));

    // What the player looks at: the enemy under the crosshair, else the most dangerous zombie in sight (the nearest of
    // equals); the spot looked at when no enemy is; and how many awake zombies are about.
    private void Sense()
    {
        target = null;
        aimed = hasSpot = false;
        crowd = close = 0;
        var eyes = PlayerView.EyesOf(movement);
        if (eyes == null) return;
        Vector3 from = eyes.position, ahead = eyes.forward;
        int count = Physics.SphereCastNonAlloc(from, Radius, ahead, hits, SightReach + 75f, ~0, QueryTriggerInteraction.Ignore);
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
        if (first.collider != null)
        {
            var enemy = first.collider.GetComponentInParent<EnemyHealth>();
            if (enemy != null && enemy.IsAlive)
            {
                target = enemy;
                aimed = true;
            }
            else if (enemy == null && first.distance <= SpotReach && first.collider.GetComponentInParent<PlayerMovement>() == null)
            {
                spot = first.point;
                hasSpot = true;
            }
        }
        int best = int.MinValue;
        float bestDistance = float.PositiveInfinity, cone = Mathf.Cos(SightCone * Mathf.Deg2Rad);
        foreach (var enemy in EnemyHealth.Living)
        {
            if (enemy == null || !enemy.IsAlive) continue;
            float distance = Vector3.Distance(enemy.transform.position, transform.position);
            bool awake = !enemy.TryGetComponent(out ZombieMind mind) || mind.IsAwake;
            if (awake && distance < HordeReach) crowd++;
            if (awake && distance < NearReach) close++;
            if (aimed || distance > SightReach) continue;
            Vector3 body = enemy.transform.position + Vector3.up * 1.2f;
            if (Vector3.Dot((body - from).normalized, ahead) < cone) continue;
            int rank = Rank(enemy);
            if (rank < best || (rank == best && distance >= bestDistance) || !InSight(from, body, enemy)) continue;
            best = rank;
            bestDistance = distance;
            target = enemy;
        }
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
