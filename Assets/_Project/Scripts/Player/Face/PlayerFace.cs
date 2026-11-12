using System;
using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// What a player's face shows, read from what is happening to them (the user, 2026-11-12, after Left 4 Dead 2's KSEP,
/// whose VRChat survivors make a face for shooting, melee, shoving, being shoved, jumping, nodding, shaking the head,
/// fidgeting and lying incapacitated). Downed they cry, held by a zombie they squeeze their eyes shut, hit they wince,
/// low on health they worry; a kill is smug, a headshot delighted; a punch, a kick or a shove fierce; a brute or an
/// elite first come near frightens them; aiming they narrow their eyes, sprinting and climbing they strain, standing
/// about they grow bored and then sleepy; a callout makes the face of its words (Callouts.csv face) while the mouth
/// moves to the voice, the body nods or points (Callouts.csv gesture, CalloutGesture) and the caller's face comes up on
/// every HUD's comms feed (CalloutFeedHUD). In front of the hub's mirror, in its dressing room and on the results stage
/// they are being looked at, and pose (CharacterFace.Showing).
/// Every PC works this out for its own copy of the player from what it already knows of them: health
/// (PlayerNetworkPose), down, held or pinned, aiming and guarding (PlayerNetworkCombat's stance), shots, punches, kicks
/// and shove impacts (their RPCs replay them on the copy, whose events fire here as on the owner), kills and headshots
/// (PlayerNetworkScore's numbers), where they stand and how they move (the pose) and the callouts they shout
/// (CalloutRpc → PlayerCallouts.Hear → Said). Only what the player's own PC alone can know goes over the network, as a
/// mood's id (Shared → PlayerNetworkCombat.FaceRpc → Show): their ultimate, a parry or a counter, a perfect evade, a
/// special kill and an execution's start.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerFace : MonoBehaviour
{
    // Tables/Faces.csv moods.
    private const string DeadMood = "dead", DownedMood = "downed", HeldMood = "held", HurtMood = "hurt", PainMood = "pain",
        UltimateMood = "ultimate", ScaredMood = "scared", ReliefMood = "relief", ParryMood = "parry", FierceMood = "fierce",
        GuardMood = "guard", DelightMood = "delight", SmugMood = "smug", WorryMood = "worry", ShootMood = "shoot",
        FocusMood = "focus", JumpMood = "jump", EffortMood = "effort", BoredMood = "bored", SleepyMood = "sleepy";
    // A callout's face when its row names none: its slot's (call.thanks), else this.
    private const string CallMood = "call";
    // How often the player's parts and face are looked for until found, and the zombies near for a fright (s).
    private const float FindEvery = .5f, FearEvery = .3f;
    // The frights remembered, so one brute frightens once (Mood.scareAgain).
    private const int FearMemory = 8;
    // A voiceless line's syllables, counted to know how long it is said (Say).
    private static readonly byte[] syllables = new byte[64];
    // Aiming counts from this share of the sight's blend; standing still under this speed (m/s).
    private const float AimingShare = .5f, StillSpeed = .3f;

    /// <summary>
    /// A moment of face this PC alone can know the reason for (this player's ultimate, a parry, an evade, a special kill,
    /// an execution): PlayerNetworkCombat sends the mood's id to the other PCs' copies.
    /// </summary>
    public event Action<string> Shared;

    private PlayerMovement movement;
    private WeaponController weapon;
    private PlayerMeleeCombat melee;
    private PlayerTally tally;
    private ShoveMoves kicks;
    private PlayerShoveAudio shoves;
    private PlayerExecution execution;
    private PlayerDamageReceiver receiver;
    private CharacterFace face;
    private CalloutGesture gesture;
    private bool special;
    private float findAt, fearAt, idle, lastHealth = -1f;
    private int kills = -1, headshots = -1;
    private bool wasDowned, wasDead, wasGrounded = true;
    private MovementState lastState;
    private readonly int[] feared = new int[FearMemory];
    private readonly float[] fearedAt = new float[FearMemory];
    private int fearNext;

    /// <summary>
    /// A callout <paramref name="speaker"/> said, heard on this PC (PlayerCallouts.Hear): their face says it with them
    /// for as long as the voice lasts, the mouth moving to it.
    /// </summary>
    public static void Said(PlayerMovement speaker, CalloutLines.Line line, AudioSource voice) =>
        Said(speaker, line, voice, Vector3.zero, false);

    /// <summary>
    /// As above, with what the callout is about (the zombie it pings, the spot it pins) for the body's point and the head's
    /// turn (CalloutGesture); the caller's face comes up in the callout feed too (CalloutFeedHUD).
    /// </summary>
    public static void Said(PlayerMovement speaker, CalloutLines.Line line, AudioSource voice, Vector3 toward, bool aims)
    {
        if (speaker != null && line != null && speaker.TryGetComponent(out PlayerFace face)) face.Say(line, voice, toward, aims);
    }

    /// <summary>This PC's player spent the full gauge on their class's ultimate (PerkCardScreen): everyone sees the face.</summary>
    public static void Ultimate(PlayerMovement player)
    {
        if (player != null && player.TryGetComponent(out PlayerFace face)) face.Share(UltimateMood);
    }

    /// <summary>On another player's copy: their PC sent a moment of face only it could know (PlayerNetworkCombat.FaceRpc).</summary>
    public void Show(string mood)
    {
        idle = 0f;
        Pulse(mood);
    }

    private void Say(CalloutLines.Line line, AudioSource voice, Vector3 toward, bool aims)
    {
        idle = 0f;
        if (movement == null) TryGetComponent(out movement);
        var clip = voice != null ? voice.clip : null;
        // A line its voice pack hasn't got is said all the same, for as long as its words take (FaceVoice.Seconds).
        float spoken = clip != null ? clip.length / Mathf.Max(.1f, Mathf.Abs(voice.pitch))
            : FaceVoice.Seconds(FaceVoice.VowelsOf(line.Text, syllables));
        if (face != null)
        {
            string mood = line.Face;
            if (string.IsNullOrEmpty(mood) || FaceTables.Find(mood) == null) mood = CallMood + "." + line.Slot;
            if (FaceTables.Find(mood) == null) mood = CallMood;
            face.Pulse(mood, 1f, spoken + FaceTables.Number("Talk.tail", .5f));
            face.Speak(voice, line.Text, spoken);
        }
        // The body says it too, and the face comes up on the squad's comms feed.
        if (gesture == null) gesture = GetComponentInChildren<CalloutGesture>();
        if (gesture != null) gesture.Play(CalloutGesture.Parse(line.Gesture), toward, aims);
        CalloutFeedHUD.Show(movement, spoken);
    }

    private void Share(string mood)
    {
        Pulse(mood);
        Shared?.Invoke(mood);
    }

    private void Pulse(string mood, float strength = 1f)
    {
        if (face != null) face.Pulse(mood, strength);
    }

    private void Update()
    {
        if (movement == null && !TryGetComponent(out movement)) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        if (Time.time >= findAt) Find();
        if (face == null) return;
        bool local = movement.IsLocal;
        // This PC's own face always moves: the mirror, F5 and the results stage show it from outside.
        face.Watched = local;
        var health = movement.DamageReceiver;
        if (health == null) return;
        bool dead = !health.IsAlive, downed = health.IsDowned && !dead;
        bool held = !dead && !downed && (movement.IsHeld || movement.IsPinned);
        bool up = !dead && !downed && !held;
        face.Hold(DeadMood, dead);
        face.Hold(DownedMood, downed);
        face.Hold(HeldMood, held);
        Wounds(health, dead, downed);
        face.Hold(WorryMood, up && health.Health / Mathf.Max(1f, health.MaximumHealth) < FaceTables.Number("Mood.lowHealth", .35f));
        bool guarding = up && melee != null && melee.IsGuarding;
        bool aiming = up && weapon != null && weapon.AimBlend > AimingShare;
        face.Hold(GuardMood, guarding);
        face.Hold(FocusMood, aiming);
        Move(up);
        Kills();
        if (up && Time.time >= fearAt)
        {
            fearAt = Time.time + FearEvery;
            Fear();
        }
        Idle(dt, up && !guarding && !aiming);
        face.Showing = up && Looked(local);
    }

    // The face (made again with every new body: PlayerBody.WearFace) and the parts whose events make faces, which may be
    // added after this one (PlayerNetworkCombat adds some as it spawns).
    private void Find()
    {
        findAt = Time.time + FindEvery;
        if (face == null) face = GetComponentInChildren<CharacterFace>();
        if (weapon == null && TryGetComponent(out weapon)) weapon.ShotFired += OnShot;
        if (melee == null && TryGetComponent(out melee)) melee.Punched += OnPunched;
        if (shoves == null && TryGetComponent(out shoves)) shoves.Played += OnShove;
        if (kicks == null && TryGetComponent(out kicks)) kicks.Kicked += OnKicked;
        if (execution == null && TryGetComponent(out execution)) execution.Started += OnExecution;
        if (receiver == null && movement.DamageReceiver != null)
        {
            receiver = movement.DamageReceiver;
            receiver.HitResolved += OnHit;
        }
        if (tally == null) TryGetComponent(out tally);
        if (!special)
        {
            SpecialKills.Scored += OnSpecial;
            special = true;
        }
    }

    private void OnDestroy()
    {
        if (weapon != null) weapon.ShotFired -= OnShot;
        if (melee != null) melee.Punched -= OnPunched;
        if (shoves != null) shoves.Played -= OnShove;
        if (kicks != null) kicks.Kicked -= OnKicked;
        if (execution != null) execution.Started -= OnExecution;
        if (receiver != null) receiver.HitResolved -= OnHit;
        if (special) SpecialKills.Scored -= OnSpecial;
        special = false;
    }

    // Health lost winces (harder the more it took at once); health given back, getting up or coming back is relief.
    private void Wounds(PlayerDamageReceiver health, bool dead, bool downed)
    {
        float now = health.Health, most = Mathf.Max(1f, health.MaximumHealth);
        if (lastHealth >= 0f && !dead)
        {
            float lost = lastHealth - now;
            if (lost >= FaceTables.Number("Mood.painFrom", 2f))
            {
                float share = lost / most;
                Pulse(share >= FaceTables.Number("Mood.bigHit", .25f) ? HurtMood : PainMood, Mathf.Clamp(.55f + share * 3f, .55f, 1f));
                idle = 0f;
            }
            else if (!downed && -lost >= FaceTables.Number("Mood.reliefFrom", 15f))
            {
                Pulse(ReliefMood);
                idle = 0f;
            }
        }
        if (!dead && !downed && (wasDowned || wasDead)) Pulse(ReliefMood);
        lastHealth = now;
        wasDowned = downed;
        wasDead = dead;
    }

    // Sprinting and climbing strain; a jump, a dive, a slide or a zipline is a whee; a barge is fierce.
    private void Move(bool up)
    {
        var state = movement.CurrentState;
        bool grounded = movement.IsGrounded;
        bool climbing = state == MovementState.Ladder || state == MovementState.WallClimb || state == MovementState.Mantle
            || state == MovementState.LedgeHang || state == MovementState.LedgeJump || state == MovementState.BarGrab
            || state == MovementState.BarSwing || state == MovementState.HeavyLanding || state == MovementState.LandingRoll;
        bool sprinting = grounded && state == MovementState.Standing && Speed() > FaceTables.Number("Mood.sprintSpeed", 6.5f);
        bool riding = state == MovementState.Zipline || state == MovementState.Sliding || state == MovementState.SupineSliding
            || state == MovementState.HeadFirstSliding;
        face.Hold(EffortMood, up && (climbing || sprinting));
        face.Hold(JumpMood, up && riding);
        if (up && state != lastState)
        {
            if (state == MovementState.Diving || state == MovementState.SupineDiving || state == MovementState.WallKick
                || state == MovementState.LacheRelease || state == MovementState.StepDodge || state == MovementState.Vault)
                Pulse(JumpMood);
            else if (state == MovementState.DashPush) Pulse(FierceMood);
        }
        if (up && wasGrounded && !grounded && movement.VerticalVelocity > 1f) Pulse(JumpMood);
        lastState = state;
        wasGrounded = grounded;
    }

    private void Kills()
    {
        if (tally == null) return;
        int k = tally.Kills, h = tally.Headshots;
        // Counts that went down are a new chapter's: only a rise is a kill.
        if (kills >= 0 && h > headshots) Pulse(DelightMood);
        else if (kills >= 0 && k > kills) Pulse(SmugMood);
        if ((kills >= 0 && k > kills) || h > headshots) idle = 0f;
        kills = k;
        headshots = h;
    }

    // A brute, an elite or a named zombie awake and near for the first time (a while) frightens them.
    private void Fear()
    {
        float reach = FaceTables.Number("Mood.scareReach", 22f), again = FaceTables.Number("Mood.scareAgain", 30f);
        Vector3 at = transform.position;
        var living = EnemyHealth.Living;
        for (int i = 0; i < living.Count; i++)
        {
            var enemy = living[i];
            if (enemy == null || !enemy.IsAlive || (enemy.transform.position - at).sqrMagnitude > reach * reach) continue;
            bool big = PlayerClass.Grade(enemy) >= 1 || (enemy.TryGetComponent(out ZombieType type) && type.IsMemoElite);
            if (!big || (enemy.TryGetComponent(out ZombieMind mind) && !mind.IsAwake)) continue;
            int id = enemy.GetInstanceID();
            if (Feared(id, again)) continue;
            feared[fearNext] = id;
            fearedAt[fearNext] = Time.time;
            fearNext = (fearNext + 1) % FearMemory;
            Pulse(ScaredMood);
            return;
        }
    }

    private bool Feared(int id, float again)
    {
        for (int i = 0; i < FearMemory; i++)
            if (feared[i] == id && fearedAt[i] > 0f && Time.time - fearedAt[i] < again) return true;
        return false;
    }

    // Standing about with nothing going on: bored, then sleepy (KSEP's fidget).
    private void Idle(float dt, bool free)
    {
        var state = movement.CurrentState;
        bool still = free && Speed() < StillSpeed && (state == MovementState.Standing || state == MovementState.Crouching);
        idle = still ? idle + dt : 0f;
        float bored = FaceTables.Number("Mood.idleBored", 10f), sleepy = FaceTables.Number("Mood.idleSleepy", 25f);
        face.Hold(BoredMood, idle >= bored && idle < sleepy);
        face.Hold(SleepyMood, idle >= sleepy);
    }

    // Being looked at: the results stage, this PC's player in the dressing room, anyone facing the hub's mirror close.
    private bool Looked(bool local)
    {
        if (ResultsShow.IsPlaying || (local && HubDressing.IsOpen)) return true;
        var mirror = HubMirror.Active;
        if (mirror == null || !mirror.isActiveAndEnabled) return false;
        Vector3 eyes = transform.position + Vector3.up * 1.5f;
        float ahead = mirror.InFront(eyes);
        if (ahead <= 0f || ahead > FaceTables.Number("Mood.mirrorReach", 4.5f)) return false;
        Vector3 off = eyes - mirror.Center - mirror.Normal * ahead;
        if (Mathf.Abs(Vector3.Dot(off, mirror.Across)) > mirror.Size.x * .5f + .5f) return false;
        var view = local ? PlayerView.EyesOf(movement) : null;
        Vector3 facing = Vector3.ProjectOnPlane(view != null ? view.forward : transform.forward, Vector3.up).normalized;
        return Vector3.Dot(facing, -mirror.Normal) > .6f;
    }

    private float Speed()
    {
        var velocity = movement.ActualVelocity;
        return new Vector2(velocity.x, velocity.z).magnitude;
    }

    // ---- What happens to them, as their events say (fired on every PC: the copy's RPCs replay them) ----

    private void OnShot(WeaponShot shot)
    {
        idle = 0f;
        Pulse(ShootMood);
    }

    private void OnPunched(float charge)
    {
        idle = 0f;
        Pulse(FierceMood, .7f + .3f * Mathf.Clamp01(charge));
    }

    private void OnShove(ShoveImpactKind impact)
    {
        idle = 0f;
        Pulse(FierceMood);
    }

    private void OnKicked()
    {
        idle = 0f;
        Pulse(FierceMood);
    }

    // ---- Only this player's own PC knows these: shared ----

    private void OnExecution(MeleeCombatTarget victim)
    {
        if (movement == null || !movement.IsLocal) return;
        idle = 0f;
        Share(FierceMood);
    }

    private void OnHit(DamageHit hit, DamageResult result)
    {
        if (movement == null || !movement.IsLocal) return;
        if (result == DamageResult.Parried || result == DamageResult.Countered) Share(ParryMood);
        else if (result == DamageResult.Evaded) Share(SmugMood);
    }

    private void OnSpecial(SpecialKills.Kind kind)
    {
        if (movement != null && movement.IsLocal && PlayerRoster.Local == movement) Share(DelightMood);
    }
}
