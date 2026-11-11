using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>The katana's moves, the rows of Tables/Katana. The byte goes over the network (PlayerNetworkCombat).</summary>
public enum KatanaMove : byte { None, Slash1, Slash2, Heavy, Shove, Counter }

/// <summary>
/// One row of Tables/Katana: a move's timing (s from its start), its blow, and the arc the blade sweeps across the view
/// (degrees from;to, yaw to the right and pitch up). Its blow is judged in its hit window along that arc. The values here
/// are only what a missing row falls back to.
/// </summary>
[Serializable]
public sealed class KatanaAttack
{
    public float duration = 1f, refire = .5f, hitStart = .1f, hitEnd = .25f;
    public float damage = 70f, posture = 15f, reach = 1.9f, radius = .12f;
    public int maxTargets = 3;
    public Vector2 yaw = new Vector2(75f, -75f), pitch = new Vector2(12f, -14f);
    public float windup, follow = .12f, thrust, lunge = .15f, combo = .6f, moveShare = 1f, hitStop = .05f, stun;
    public Vector3 swingKick, hitKick;
    public float hitTrauma = .12f, swingPitch = 1.3f;

    /// <summary>It judges a blow of its own (the shove and the counter only show: their blows are the shove's and the counter's).</summary>
    public bool Strikes => damage > 0f && reach > 0f && maxTargets > 0;
    public float Window => Mathf.Max(.01f, hitEnd - hitStart);
    /// <summary>How far along its arc the blade is t s into the move: 0 as the hit window opens, 1 as it shuts, on past both.</summary>
    public float ArcAt(float t) => (t - hitStart) / Window;
    /// <summary>The way along the arc at s, in view space (unclamped: the arc carries on past its ends at the same pace).</summary>
    public Quaternion ArcTurn(float s)
        => Quaternion.Euler(-Mathf.LerpUnclamped(pitch.x, pitch.y, s), Mathf.LerpUnclamped(yaw.x, yaw.y, s), 0f);
}

/// <summary>
/// Bundle 5 of the addon port, the katana: the first melee weapon, in the melee slot (3). Left click is a light slash,
/// the two slashes taking turns right to left and back (Better Melees' E→W, W→E) while the presses keep coming; held past
/// heavyHold it is the heavy, an overhead cut (the guard and the alternate fire share the right button, so the hold of
/// the fire button is the free input, as the fists' charge already is). V is the body's own shove, as with a gun, and the
/// blade shows its hilt driving forward. The right button guards and parries in PlayerMeleeCombat, as the fists do:
/// its parry windows and the Knight's bonuses are theirs, the cone is TFA's 135° and a held block lets a share of the blow
/// through (TFA's: 10% for half a second, rising to 40% over the next). The blade can't be stopped mid-cut to guard.
/// A move's blow is judged on this PC only, in its hit window, by a ray from the eyes swept along the move's arc between
/// frames (AimSweepWindow, as the fists sweep, here at least every 5° where TFA casts nine), each ray ending at a wall or
/// in the first body it meets; each body is cut once a move, up to the row's maxTargets (TFA's and Horde's 3 and 5).
/// It meets a zombie's bone hitboxes before its capsule, so the head is a headshot (headshotMultiplier) and an arm or a
/// leg can come off through ZombieGore (DamageHit.Part). Hit enemies on someone else's PC take it from the copy there,
/// which tells the host, as fists and guns do (EnemyHealth). Damage and posture as the fists put them (an interrupt);
/// a heavy cut to the head stuns a moment (Horde's). A cut that lands stops time a moment and knocks the view.
/// Numbers: Tables/Katana (the moves) and Tables/Player.csv row Katana; the look is KatanaView, the sound KatanaAudio.
/// On someone else's copy the moves only play out, as their PC sends them (ShowAttack).
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerKatana : MonoBehaviour
{
    public const string TableName = "Katana", PlayerRow = "Katana";
    // The step into a cut starts this long before its hit window and takes this long (s).
    private const float LungeLead = .06f, LungeTime = .12f;
    private static readonly Color SparkColor = new Color(1f, .78f, .45f);

    [Header("Input")]
    [SerializeField, Min(.05f), Tooltip("Left button held this long (s) is the heavy cut; let go sooner, a light slash.")]
    private float heavyHold = .3f;
    [SerializeField, Min(0f), Tooltip("A press this soon (s) before the hands are free waits for them; sooner it is dropped.")]
    private float queueSeconds = .3f;
    [SerializeField, Min(0f), Tooltip("Drawing it out of the slot takes this long (s) before it can cut.")]
    private float drawSeconds = .3f;
    [Header("Blow")]
    [SerializeField, Min(1f)] private float headshotMultiplier = 1.5f;
    [SerializeField, Range(.01f, 1f), Tooltip("Time's pace through a landed cut's stop (the move's hitStop).")]
    private float hitStopScale = .05f;
    [SerializeField] private LayerMask hitLayers = ~0;
    [Header("Guard (TFA's block)")]
    [SerializeField, Range(30f, 180f)] private float guardAngle = 135f;
    [SerializeField, Tooltip("Share of a blocked blow that gets through: when the guard has just gone up; once it has been held long.")]
    private Vector2 guardLeak = new Vector2(.1f, .4f);
    [SerializeField, Min(0f)] private float guardLeakHold = .5f;
    [SerializeField, Min(0f)] private float guardLeakFade = 1f;

    private readonly KatanaAttack slash1 = new KatanaAttack(),
        slash2 = new KatanaAttack { yaw = new Vector2(-75f, 75f), pitch = new Vector2(-12f, 10f), hitStart = .04f, hitEnd = .2f, duration = 1.3f },
        heavy = new KatanaAttack { duration = 1f, refire = .9f, hitStart = .46f, hitEnd = .78f, damage = 140f, posture = 40f, reach = 2f,
            maxTargets = 5, yaw = new Vector2(15f, -10f), pitch = new Vector2(75f, -45f), windup = .25f, lunge = .35f, moveShare = .5f,
            hitStop = .09f, stun = .5f, hitTrauma = .2f, swingPitch = 1.08f },
        shove = new KatanaAttack { duration = .73f, refire = .5f, hitStart = .1f, hitEnd = .26f, damage = 0f, reach = 0f,
            yaw = new Vector2(6f, 0f), pitch = new Vector2(25f, 5f), follow = 0f, thrust = .25f, lunge = 0f, hitTrauma = 0f, swingPitch = 1.1f },
        counter = new KatanaAttack { duration = .5f, refire = .5f, hitStart = 0f, hitEnd = .1f, damage = 0f, reach = 0f,
            yaw = new Vector2(60f, -50f), pitch = new Vector2(35f, -25f), lunge = 0f, hitTrauma = 0f };

    private struct Contact
    {
        public Collider Collider;
        public IDamageReceiver Receiver;
        public Vector3 Point;
        public float Distance;
        // What the line met of a body: 3 its head box, 2 another bone box, 1 its capsule or any other body; 0 a wall.
        public int Rank;
        public bool Head;
        public BodyPart Part;
    }

    private PlayerMovement player;
    private PlayerWeaponSlots slots;
    private PlayerMeleeCombat melee;
    private PlayerDamageReceiver receiver;
    private KatanaAudio sounds;
    private CombatTimeEffects timeEffects;
    private CameraViewPunch punch;
    private Transform eyes, aim;
    private AimSweepWindow sweep;
    private readonly HashSet<UnityEngine.Object> contacted = new HashSet<UnityEngine.Object>();
    private readonly List<Contact> contacts = new List<Contact>();
    private readonly RaycastHit[] casts = new RaycastHit[32];
    private readonly Collider[] inside = new Collider[16];

    // The move under way: judged here (this PC's own cut) or only shown (someone else's, a shove, a counter).
    private bool judged, struck, landed, wallHit, wasActive, pressing, heavyStarted;
    private int hits;
    private long attackId;
    private float lungeLeft, pressedFor, drawLeft, lastRest;
    private Vector3 lungeDirection, swingDirection;
    private KatanaMove wanted, lastLight;
    private float lastLightAt = float.NegativeInfinity;

    /// <summary>The move under way, and how long it has run (s).</summary>
    public KatanaMove Move { get; private set; }
    public float Elapsed { get; private set; }
    /// <summary>Counts every move begun, so the view sees a move start again even when it is the same move.</summary>
    public int Serial { get; private set; }
    public KatanaAttack Attack => AttackOf(Move);
    /// <summary>How far it has come out of the slot (0-1).</summary>
    public float DrawShare => drawSeconds > 0f ? 1f - Mathf.Clamp01(drawLeft / drawSeconds) : 1f;
    public float GuardAngle => guardAngle;
    /// <summary>The katana is the weapon in hand, on this PC's player or as the other PC's slot shows it on their copy.</summary>
    public bool IsActive => isActiveAndEnabled && slots != null && slots.isActiveAndEnabled && MeleeWeapons.IsKatana(slots.SelectedWeapon)
        && player != null && player.IsInitialized && player.DamageReceiver.IsAlive;
    /// <summary>The guard may come up now: not while the blade is in its own cut, which it finishes first.</summary>
    public bool CanGuard => !judged || Attack == null || Elapsed < Attack.hitStart || Elapsed > Attack.hitEnd;

    /// <summary>This PC's player began a move: the other PCs show it (PlayerNetworkCombat).</summary>
    public event Action<KatanaMove> Swung;
    /// <summary>This PC's katana cut flesh, met a wall, parried or blocked, here: the other PCs hear it (and see a wall's sparks).</summary>
    public event Action<KatanaSound, Vector3> Struck;
    /// <summary>This PC's katana parried (true) or blocked a blow from this side of the view (+1 right): the blade swings to meet it (KatanaView).</summary>
    public event Action<bool, float> Deflected;

    public KatanaAttack AttackOf(KatanaMove move)
    {
        switch (move)
        {
            case KatanaMove.Slash1: return slash1;
            case KatanaMove.Slash2: return slash2;
            case KatanaMove.Heavy: return heavy;
            case KatanaMove.Shove: return shove;
            case KatanaMove.Counter: return counter;
            default: return null;
        }
    }

    private void OnEnable()
    {
        player = GetComponentInParent<PlayerMovement>();
        slots = GetComponent<PlayerWeaponSlots>();
        melee = GetComponent<PlayerMeleeCombat>();
        if (sweep == null) sweep = new AimSweepWindow(Sample);
        GameTables.Bind(PlayerMovement.TableName, PlayerRow, this);
        GameTables.BindNested(TableName, "Slash1", this, slash1);
        GameTables.BindNested(TableName, "Slash2", this, slash2);
        GameTables.BindNested(TableName, "Heavy", this, heavy);
        GameTables.BindNested(TableName, "Shove", this, shove);
        GameTables.BindNested(TableName, "Counter", this, counter);
        if (!Application.isPlaying) return;
        sounds = GetComponent<KatanaAudio>();
        if (sounds == null) sounds = gameObject.AddComponent<KatanaAudio>();
        sounds.Prepare();
        var camera = player != null ? player.GetComponentInChildren<Camera>() : null;
        if (camera != null)
        {
            // From the eyes, which stay with the body in the F5 view, as the fists' reach does.
            eyes = PlayerView.EyesOf(player);
            if (aim == null)
            {
                aim = new GameObject("Katana Sweep").transform;
                aim.SetParent(eyes, false);
            }
            if (GetComponent<KatanaView>() == null) gameObject.AddComponent<KatanaView>();
        }
        Wire();
        CancelAll();
    }

    private void OnDisable()
    {
        CancelAll();
        if (melee != null) { melee.Punched -= OnCounter; melee.Elbowed -= OnElbow; }
        if (receiver != null) receiver.HitResolved -= OnResolved;
        receiver = null;
    }

    private void OnDestroy()
    {
        if (aim != null) Destroy(aim.gameObject);
    }

    // The fists' counter (PlayerPerfectDefense) and the blows this player's guard met, once both are there.
    private void Wire()
    {
        if (melee == null) melee = GetComponent<PlayerMeleeCombat>();
        if (melee != null) { melee.Punched -= OnCounter; melee.Punched += OnCounter; melee.Elbowed -= OnElbow; melee.Elbowed += OnElbow; }
        if (receiver == null && player != null && player.DamageReceiver != null)
        {
            receiver = player.DamageReceiver;
            receiver.HitResolved += OnResolved;
        }
    }

    private void Update()
    {
        if (player == null) return;
        if (receiver == null || melee == null) Wire();
        bool active = IsActive;
        if (active != wasActive)
        {
            wasActive = active;
            CancelAll();
            if (active)
            {
                drawLeft = drawSeconds;
                lastRest = ShoveRest;
                sounds?.Play(KatanaSound.Draw);
            }
        }
        if (!active) return;
        float dt = player.DeltaTime;
        drawLeft = Mathf.Max(0f, drawLeft - dt);
        // A move that only shows (someone else's on their copy, a shove, a counter) plays out on its own clock here; this
        // PC's own cuts run in ProcessFrame.
        if (Move != KatanaMove.None && !judged) Advance(dt);
    }

    private void LateUpdate()
    {
        if (player == null || player.IsNetworkDriven || !IsActive || Time.timeScale <= 0f) return;
        // V is the body's own shove, as with a gun (PlayerCombatActions); a fresh one shows here as the hilt driving
        // forward, and its other PCs are told. Every shove adds to the shove's wait (ShoveFatigue), so that is where it shows.
        float rest = ShoveRest;
        if (rest > lastRest + .001f)
        {
            Show(KatanaMove.Shove);
            Swung?.Invoke(KatanaMove.Shove);
        }
        lastRest = rest;
    }

    private float ShoveRest => player != null && player.Abilities != null ? player.Abilities.Combat.Fatigue.Rest : 0f;

    /// <summary>
    /// This PC's player's left button with the katana out, after PlayerMeleeCombat has seen to the guard (which takes
    /// priority unless the blade is mid-cut, CanGuard). Light on a quick press, heavy on a long one.
    /// </summary>
    internal void ProcessFrame(float dt, MeleeInputFrame frame)
    {
        if (!IsActive || dt <= 0f) { Cancel(); return; }
        ReadFire(dt, frame);
        if (Move != KatanaMove.None && judged) Advance(dt);
        if (wanted != KatanaMove.None && Ready)
        {
            var move = wanted == KatanaMove.Heavy ? KatanaMove.Heavy : NextLight();
            wanted = KatanaMove.None;
            Begin(move);
        }
    }

    /// <summary>After the look has turned the eyes this frame: the cut's sweep includes it (as the fists' LateUpdate does).</summary>
    internal void LateTick()
    {
        var attack = Attack;
        if (!judged || attack == null || aim == null || !sweep.IsActive) return;
        Place(attack);
        lateTicking = true;
        sweep.Tick(0f, aim);
        lateTicking = false;
    }

    /// <summary>The legs' speed while a cut is under way (LocomotionState asks through PlayerMeleeCombat): a heavy one plants them.</summary>
    internal float LimitMoveSpeed(float speed, float walk)
    {
        var attack = Attack;
        if (!judged || attack == null || Elapsed > attack.hitEnd || attack.moveShare >= 1f) return speed;
        return Mathf.Min(speed, walk * Mathf.Max(0f, attack.moveShare));
    }

    /// <summary>
    /// TFA's block: the share of a blocked blow that gets through after the guard has been up this long (s): guardLeak.x
    /// for guardLeakHold seconds, then rising to guardLeak.y over guardLeakFade.
    /// </summary>
    internal float GuardLeak(float held)
    {
        float rise = guardLeakFade > 0f ? Mathf.Clamp01((held - guardLeakHold) / guardLeakFade) : held >= guardLeakHold ? 1f : 0f;
        return Mathf.Clamp01(Mathf.Lerp(guardLeak.x, guardLeak.y, rise));
    }

    /// <summary>The fists' counter landed or missed with the katana out (PlayerMeleeCombat.ShowCounterPunch): its cut's sound.</summary>
    internal void CounterLanded(bool landed)
    {
        if (!landed) return;
        sounds?.Play(KatanaSound.HitFlesh);
        Struck?.Invoke(KatanaSound.HitFlesh, transform.position);
    }

    /// <summary>On someone else's copy: their PC began this move. It only plays out here; their PC judged it.</summary>
    internal void ShowAttack(KatanaMove move)
    {
        if (IsActive) Show(move);
    }

    /// <summary>On someone else's copy: their katana cut flesh, met a wall, parried or blocked, at this point.</summary>
    internal void ShowStruck(KatanaSound sound, Vector3 at)
    {
        if (!isActiveAndEnabled) return;
        sounds?.Play(sound);
        if (sound == KatanaSound.HitWorld && at != Vector3.zero) ImpactVfx.ShieldSpark(at, (at - transform.position).normalized, SparkColor);
    }

    /// <summary>This PC's own cut and the presses toward the next stop (a guard, a dodge, a swap, an execution).</summary>
    public void Cancel()
    {
        wanted = KatanaMove.None;
        pressing = heavyStarted = false;
        lungeLeft = 0f;
        if (!judged) return;
        sweep?.Cancel();
        Move = KatanaMove.None;
        Elapsed = 0f;
        judged = struck = false;
    }

    // Everything, the moves that only show included: the katana went away.
    private void CancelAll()
    {
        Cancel();
        sweep?.Cancel();
        Move = KatanaMove.None;
        Elapsed = 0f;
        judged = struck = false;
    }

    // ---- Input ----

    private bool Ready => drawLeft <= 0f && (Move == KatanaMove.None || Attack == null || Elapsed >= Attack.refire);

    private float TimeUntilReady => Mathf.Max(drawLeft, Move == KatanaMove.None || Attack == null ? 0f : Attack.refire - Elapsed);

    private void ReadFire(float dt, MeleeInputFrame frame)
    {
        if (frame.AttackPressed) { pressing = true; pressedFor = 0f; heavyStarted = false; }
        if (!pressing) return;
        if (frame.AttackHeld)
        {
            pressedFor += dt;
            // Held past heavyHold the press is the heavy cut, as soon as the hands are free for it.
            if (!heavyStarted && pressedFor >= heavyHold && Ready)
            {
                heavyStarted = true;
                wanted = KatanaMove.None;
                Begin(KatanaMove.Heavy);
            }
            return;
        }
        // Let go: a quick press is a light slash; a long one whose heavy never got going still asks for it.
        pressing = false;
        if (heavyStarted) return;
        // Pressed too long before the hands are free it is dropped: mashing through a cut doesn't line up the next.
        if (TimeUntilReady > queueSeconds) return;
        wanted = pressedFor >= heavyHold ? KatanaMove.Heavy : KatanaMove.Slash1;
    }

    // The light slashes take turns while the presses keep coming (Better Melees: east to west, then back west to east):
    // the second goes back the way the first came. A pause longer than the first's combo starts again from the first.
    private KatanaMove NextLight()
    {
        if (lastLight != KatanaMove.Slash1) return KatanaMove.Slash1;
        return Time.time - lastLightAt <= slash1.refire + slash1.combo ? KatanaMove.Slash2 : KatanaMove.Slash1;
    }

    // ---- Moves ----

    // This PC's own move, judged here.
    private void Begin(KatanaMove move)
    {
        var attack = AttackOf(move);
        if (attack == null) return;
        sweep.Cancel();
        Move = move;
        Elapsed = 0f;
        Serial++;
        judged = true;
        struck = landed = wallHit = false;
        hits = 0;
        attackId = DamageHit.NewAttackId();
        contacted.Clear();
        lungeLeft = Mathf.Max(0f, attack.lunge);
        Transform view = eyes != null ? eyes : transform;
        lungeDirection = Vector3.ProjectOnPlane(view.forward, Vector3.up).normalized;
        if (move == KatanaMove.Slash1 || move == KatanaMove.Slash2) { lastLight = move; lastLightAt = Time.time; }
        Swung?.Invoke(move);
        // A cut whose window opens at once sweeps from now.
        Advance(0f);
    }

    // A move that only shows: someone else's (their PC judged it), this PC's shove, or the fists' counter.
    private void Show(KatanaMove move)
    {
        if (AttackOf(move) == null) return;
        sweep.Cancel();
        Move = move;
        Elapsed = 0f;
        Serial++;
        judged = false;
        struck = false;
    }

    private void Advance(float dt)
    {
        var attack = Attack;
        if (attack == null) { Move = KatanaMove.None; return; }
        Elapsed += dt;
        if (judged) Lunge(attack, dt);
        if (!struck && Elapsed >= attack.hitStart)
        {
            struck = true;
            // The whoosh as the blade sets off on its cut, on every PC (a copy plays it as its own move gets here).
            sounds?.Swing(Move == KatanaMove.Heavy, attack.swingPitch);
            if (judged)
            {
                Kick(attack.swingKick, 0f, false);
                if (attack.Strikes && aim != null)
                {
                    // Opened at the arc's very start and caught up to now, so a frame that lands late in the window
                    // doesn't skip the first part of the cut.
                    Place(attack, 0f);
                    sweep.Begin(aim, attack.Window);
                    float late = Elapsed - attack.hitStart;
                    if (late > 0f && sweep.IsActive)
                    {
                        Place(attack);
                        sweep.Tick(late, aim);
                    }
                }
            }
        }
        else if (judged && sweep.IsActive && aim != null)
        {
            Place(attack);
            sweep.Tick(dt, aim);
        }
        if (Elapsed >= attack.duration)
        {
            sweep.Cancel();
            Move = KatanaMove.None;
            judged = false;
        }
    }

    // A step into the cut, as the fists' punch steps in; only on the ground, and walls stop it (the motor moves it).
    private void Lunge(KatanaAttack attack, float dt)
    {
        if (lungeLeft <= 0f || dt <= 0f || Elapsed < attack.hitStart - LungeLead) return;
        if (!player.IsGrounded) { lungeLeft = 0f; return; }
        float step = Mathf.Min(lungeLeft, attack.lunge * dt / LungeTime);
        lungeLeft -= step;
        player.Motor.AddDisplacement(lungeDirection * step);
    }

    // The sweep's line from the eyes, where the arc is now; and which way the blade travels there, for the blow's direction.
    private void Place(KatanaAttack attack) => Place(attack, attack.ArcAt(Elapsed));

    private void Place(KatanaAttack attack, float s)
    {
        aim.localRotation = attack.ArcTurn(s);
        Vector3 travel = attack.ArcTurn(s + .05f) * Vector3.forward - attack.ArcTurn(s - .05f) * Vector3.forward;
        swingDirection = eyes != null && travel.sqrMagnitude > 1e-6f ? eyes.TransformDirection(travel.normalized) : aim.forward;
    }

    // ---- The blow ----

    // One line of the sweep: the first thing along it takes the blow. A wall stops the blade; a body takes the cut where
    // the line meets it best (its head box, else another bone box, else its capsule) and the line ends in it.
    private void Sample(Vector3 origin, Vector3 forward)
    {
        var attack = Attack;
        if (player == null || player.CombatInputLocked || attack == null || !judged) { sweep.Cancel(); return; }
        Collect(origin, forward, attack.reach, attack.radius);
        bool found = false;
        var best = default(Contact);
        foreach (var contact in contacts)
        {
            if (contact.Receiver == null)
            {
                if (!found) WallHit(contact.Point, forward);
                break;
            }
            if (!found) { best = contact; found = true; continue; }
            if (contact.Receiver != best.Receiver) break;
            if (contact.Rank > best.Rank) best = contact;
        }
        if (found) Strike(best, forward, attack);
    }

    private void Collect(Vector3 origin, Vector3 forward, float reach, float radius)
    {
        contacts.Clear();
        SyncBodies();
        radius = Mathf.Max(.01f, radius);
        int count = Physics.SphereCastNonAlloc(origin, radius, forward, casts, reach, hitLayers, QueryTriggerInteraction.Collide);
        RaycastHit[] results = casts;
        if (count == casts.Length)
        {
            // A full buffer may have left out the nearest wall.
            results = Physics.SphereCastAll(origin, radius, forward, reach, hitLayers, QueryTriggerInteraction.Collide);
            count = results.Length;
        }
        for (int i = 0; i < count; i++)
        {
            var hit = results[i];
            if (hit.distance <= 0f) continue; // Overlaps at the start are taken below.
            Add(hit.collider, hit.point, hit.distance, hit);
        }
        // A cast leaves out what its sphere starts inside (a body hard against the face).
        int overlapping = Physics.OverlapSphereNonAlloc(origin, radius, inside, hitLayers, QueryTriggerInteraction.Collide);
        for (int i = 0; i < overlapping; i++)
        {
            var collider = inside[i];
            if (!ShoveSweep.TryGetContactPoint(collider, origin, forward, radius, 0f, out Vector3 point)) continue;
            Vector3 delta = point - origin;
            if (delta.sqrMagnitude < .0001f) delta = collider.bounds.center - origin;
            if (Vector3.Dot(delta, forward) < -.01f) continue;
            Add(collider, point, 0f, null);
        }
        contacts.Sort((a, b) => a.Distance.CompareTo(b.Distance));
    }

    // Transforms reach physics only when someone syncs them (Auto Sync Transforms is off), and a zombie's bone boxes move
    // with its animation after the scripts' Update: once a frame before the first line of a cut, and again after the
    // animation for the lines LateTick casts, they catch up with what is on screen (as AttackTrace's SyncBodies does).
    private static int syncedStep = -1;
    private bool lateTicking;

    private void SyncBodies()
    {
        int step = Time.frameCount * 2 + (lateTicking ? 1 : 0);
        if (syncedStep == step) return;
        syncedStep = step;
        Physics.SyncTransforms();
    }

    private void Add(Collider collider, Vector3 point, float distance, RaycastHit? cast)
    {
        if (collider == null || collider.transform.IsChildOf(player.transform)) return;
        // What bullets pass, the blade passes too: flying gore, dropped magazines, thrown stones, the dead's capsules.
        if (GoreGib.IsPiece(collider) || DroppedMagazine.IsMagazine(collider) || ThrownStone.IsStone(collider)) return;
        var someone = collider.GetComponentInParent<PlayerMovement>();
        if (someone != null && someone.IsDead) return;
        bool bone = ZombieBody.IsHitbox(collider);
        // Other triggers are zones, pickups, a peeking player's boxes: not there to the blade.
        if (collider.isTrigger && !bone) return;
        var receiver = collider.GetComponentInParent<IDamageReceiver>();
        // A knocked-down zombie's doll stands in for its capsule: its bone boxes take the blade, as they take bullets.
        if (ZombieBody.Replaces(collider) && !(receiver is EnemyHealth)) return;
        bool head = false;
        if (receiver != null)
        {
            if (bone)
            {
                var region = collider.GetComponentInParent<WeaponHitRegion>();
                head = region != null && region.Kind == WeaponHitRegion.Region.Head;
            }
            // A plain capsule's top is its head; a dressed zombie's capsule leaves the head to its head box.
            else if (cast.HasValue && !ZombieBody.Replaces(collider)) head = WeaponHitRegion.IsHead(cast.Value);
        }
        contacts.Add(new Contact
        {
            Collider = collider, Receiver = receiver, Point = point, Distance = distance, Head = head,
            Rank = receiver == null ? 0 : bone ? (head ? 3 : 2) : 1, Part = bone ? ZombieGore.PartOf(collider) : BodyPart.None,
        });
    }

    private void Strike(Contact contact, Vector3 forward, KatanaAttack attack)
    {
        var identity = contact.Receiver as UnityEngine.Object;
        if (identity == null || !contacted.Add(identity)) return;
        float amount = attack.damage * (contact.Head ? headshotMultiplier : 1f) * ClassFactor();
        // Along the cut and into the body: where the blood sprays and a cut-off limb flies (ZombieGore).
        Vector3 direction = (swingDirection * .6f + forward * .4f).normalized;
        var result = contact.Receiver.ReceiveHit(new DamageHit(attackId, amount, DamageKind.Attack, contact.Point, direction,
            player.gameObject, true, attack.posture, isCritical: contact.Head, method: DamageMethod.Slash, part: contact.Part));
        if (result == DamageResult.Damaged)
        {
            // As a punch does: posture, and its attack stops; a heavy cut to the head stuns a moment (Horde's katana).
            contact.Collider.GetComponentInParent<MeleeCombatTarget>()?.ApplyPostureDamage(attack.posture, contact.Head ? attack.stun : 0f);
            hits++;
            if (!landed) Landed(contact.Point, attack, contact.Head);
            if (hits >= Mathf.Max(1, attack.maxTargets)) sweep.Cancel();
        }
        // A teammate's guard or a barricade rings like a wall.
        else if (result == DamageResult.Blocked || result == DamageResult.Parried) WallHit(contact.Point, forward);
    }

    // A class's own weapon hits harder in its hands (Classes.csv classWeapons and gunBonus); this PC's own player only.
    private float ClassFactor()
    {
        if (player == null || !player.IsLocal) return 1f;
        var info = PlayerClass.Current;
        return info != null && PlayerClass.OwnsWeapon(info, slots != null ? slots.SelectedWeapon : null) ? 1f + info.GunBonus / 100f : 1f;
    }

    // The first flesh of the cut: its sound once, a moment's stop and a knock to the view, harder for a heavy cut or a
    // head (Impactful Melee Hit's normal and big shakes, 12 to 20).
    private void Landed(Vector3 point, KatanaAttack attack, bool head)
    {
        landed = true;
        sounds?.Play(KatanaSound.HitFlesh);
        Struck?.Invoke(KatanaSound.HitFlesh, point);
        if (attack.hitStop > 0f && TimeEffects != null) TimeEffects.RequestSlowdown(this, hitStopScale, attack.hitStop);
        Kick(attack.hitKick, attack.hitTrauma * (head ? 1.6f : 1f), true);
    }

    // Steel on a wall (or a guard): its sound once a cut, sparks where it met, a small knock.
    private void WallHit(Vector3 point, Vector3 forward)
    {
        if (wallHit) return;
        wallHit = true;
        sounds?.Play(KatanaSound.HitWorld);
        ImpactVfx.ShieldSpark(point, forward, SparkColor);
        Struck?.Invoke(KatanaSound.HitWorld, point);
        Kick(new Vector3(.6f, 0f, 0f), .05f, true);
    }

    private CombatTimeEffects TimeEffects
    {
        get
        {
            if (timeEffects == null && !TryGetComponent(out timeEffects)) timeEffects = gameObject.AddComponent<CombatTimeEffects>();
            return timeEffects;
        }
    }

    // The view's knock: on game time for a swing, on real time for a hit so it lands inside the hit stop. This PC's own only.
    private void Kick(Vector3 degrees, float trauma, bool impact)
    {
        if (degrees == Vector3.zero && trauma <= 0f) return;
        if (punch == null)
        {
            if (player == null || !player.IsLocal || player.IsNetworkDriven) return;
            var camera = player.GetComponentInChildren<Camera>();
            if (camera == null) return;
            punch = CameraViewPunch.For(camera);
        }
        if (impact) punch.ImpactPunch(degrees, 0f, trauma);
        else punch.Punch(degrees, 0f, trauma);
    }

    // ---- Guard, parry and the counter ----

    // A blow this player's katana guard met (PlayerMeleeCombat.TryDefend): the metal ring over the fists' cue, and the
    // blade swings to meet it.
    private void OnResolved(DamageHit hit, DamageResult result)
    {
        if (!IsActive || (result != DamageResult.Parried && result != DamageResult.Blocked)) return;
        var attacker = hit.Source != null ? hit.Source.GetComponentInParent<MeleeCombatTarget>() : null;
        var sound = result == DamageResult.Blocked ? KatanaSound.Block
            : attacker != null && attacker.IsStaggered ? KatanaSound.ParryBreak : KatanaSound.Parry;
        sounds?.Play(sound);
        Struck?.Invoke(sound, hit.Point);
        float side = 0f;
        if (eyes != null && hit.Source != null)
        {
            Vector3 toward = hit.Source.transform.position - eyes.position;
            if (toward.sqrMagnitude > .0001f) side = Vector3.Dot(eyes.right, toward.normalized);
        }
        Deflected?.Invoke(result == DamageResult.Parried, side);
    }

    // The fists' counter (PlayerPerfectDefense, PlayerMeleeCombat.ShowCounterPunch) with the katana out is a quick cut;
    // on someone else's copy it comes the same way (PlayerNetworkCombat's punch).
    private void OnCounter(float charge) { if (IsActive) Show(KatanaMove.Counter); }
    private void OnElbow(int side) { if (IsActive) Show(KatanaMove.Counter); }
}
