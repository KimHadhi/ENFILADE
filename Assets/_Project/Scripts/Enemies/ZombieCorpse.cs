using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Multiplayer plan stage 4B: a dead zombie's own body stays where it fell. On death ZombieBody hands its character over
/// (out of the enemy, which is removed as before) and this lays a doll from a shared pool over it (RagdollCorpse.Bind):
/// the body goes limp and takes the doll's pose each frame, so physics never writes a real bone. The PC whose player
/// landed the killing blow runs the physics and sends the fall through that player (PlayerNetworkCorpse); every other
/// PC's corpse follows it, so the killer never waits on the network and the others run no physics for it. A kill by no
/// player falls on the host. Every body keeps its doll as long as it lies, so a shot or a blow always pushes it (on
/// another PC the push goes to the runner), whatever it was and however it died (the user, 2026-11-09: a runner's body
/// stopped taking shots; the way that could happen, a doll given back mid-fight, is gone). At rest it sleeps and nothing
/// is sent. One that never comes to rest (caught on something), or the oldest past the most a map keeps moving, is held
/// still where it lies, kinematic: it costs no physics and is still shot and pushed, which lets it go again. A body lies
/// a while, then sinks away; past the most a map keeps, the oldest goes first. If the PC running a fall stops sending
/// (its player left), this PC takes the body over where it is, and it is cleared soon. Any enemy whose body is humanoid
/// falls this way (ZombieBody wears its own, or one of the pack's). Numbers: Tables/ZombieCorpse.
/// </summary>
[DisallowMultipleComponent]
public sealed class ZombieCorpse : MonoBehaviour
{
    public const string TableName = "ZombieCorpse", TableRow = "Default";
    private const string EnemyLayer = "Enemy";
    // Bodies lie on the players' own layer, as players' corpses do: everything the players test their movement and
    // camera against leaves that layer out (it is their own), so a body never stops them standing up, never reads as a
    // ledge and never pushes the camera into them (on Default, sliding through a zombie it knocked down did). Bodies
    // land on the world and on each other; living zombies (Enemy) pass through, and players are told to (PassThrough).
    private const int PlayerLayerFallback = 31;
    private static int DollLayer
    {
        get
        {
            foreach (var player in PlayerRoster.All) if (player != null) return player.gameObject.layer;
            return PlayerLayerFallback;
        }
    }
    // Dolls made ahead per rig, one a frame, so deaths don't hitch: this many at first, then while fewer than Spare wait
    // free, up to MostMade. A death that still finds none free makes one on the spot.
    private const int Stocked = 6, Spare = 2, MostMade = 24;
    // How often a fall is sent (s): every network tick, as a player's corpse is.
    private const float SendInterval = .03f;
    // A fall heard before its corpse is here waits this long (s).
    private const float WaitingLife = 3f;
    // How long a doll must have stayed still before it counts as at rest (s). How often the players are told again to
    // pass through it: a capsule switched off and on (a spawn) forgets, and someone may join while it lies there (s).
    private const float StillTime = .3f, PassThroughEvery = .5f;

    [SerializeField, Min(0f), Tooltip("Momentum a hit gives the corpse per point of damage (N·s).")]
    private float pushPerDamage = 1f;
    [SerializeField, Min(0f), Tooltip("Momentum of even the weakest hit (N·s).")]
    private float leastPush = 8f;
    [SerializeField, Min(0f), Tooltip("Speed a hit can add to one body part (m/s).")]
    private float mostPushSpeed = 5f;
    [SerializeField, Min(0f), Tooltip("Momentum the killing blow gives the upper body (N·s).")]
    private float deathPush = 40f;
    [SerializeField, Min(0f), Tooltip("How hard the muscles hold the pose it died in, as how quickly they pull it back (Hz).")]
    private float muscleHold = 6f;
    [SerializeField, Min(0f), Tooltip("How long the muscles take to give out (s). 0 drops the body limp at once.")]
    private float muscleSeconds = .3f;
    [SerializeField, Min(0f), Tooltip("What stays in the joints once the muscles have gone (1/s).")]
    private float jointFriction = 1.5f;
    [SerializeField, Min(0f), Tooltip("Below this speed (m/s) for a moment the body is at rest: it stops sending until pushed again.")]
    private float settleSpeed = .15f;
    [SerializeField, Min(0f), Tooltip("A fall counts as at rest no sooner than this (s).")]
    private float shortestFall = 1.5f;
    [SerializeField, Min(0f), Tooltip("A body that keeps moving this long without coming to rest (stuck, jittering) is held still as it is, still to be pushed (s).")]
    private float longestFall = 8f;
    [SerializeField, Min(0f), Tooltip("How long the body lies once at rest before it sinks away (s).")]
    private float lieSeconds = 30f;
    [SerializeField, Min(.05f), Tooltip("How long sinking into the floor takes (s).")]
    private float sinkSeconds = 2f;
    [SerializeField, Min(0f), Tooltip("How deep it sinks before it is gone (m).")]
    private float sinkDepth = .6f;
    [SerializeField, Min(1), Tooltip("Most corpses a map keeps lying; past it the oldest sinks at once.")]
    private int mostCorpses = 24;
    [SerializeField, Min(1), Tooltip("Most bodies moving under physics at once; past it the oldest at rest is held still as it lies, still to be pushed.")]
    private int mostDolls = 12;
    [SerializeField, Min(.1f), Tooltip("Following another PC's fall: with no word from it this long (s), the body stops where it is.")]
    private float orphanWait = 1f;
    [SerializeField, Min(0f), Tooltip("A body stopped that way lies this long before it sinks (s).")]
    private float orphanLie = 3f;

    private static readonly Dictionary<Avatar, Stack<RagdollCorpse>> stowed = new Dictionary<Avatar, Stack<RagdollCorpse>>();
    private static readonly Dictionary<Avatar, List<RagdollCorpse>> made = new Dictionary<Avatar, List<RagdollCorpse>>();
    private static readonly List<ZombieCorpse> lying = new List<ZombieCorpse>();
    private static readonly Dictionary<ulong, ZombieCorpse> byId = new Dictionary<ulong, ZombieCorpse>();
    private static readonly Dictionary<ulong, (ZombieCorpseFall fall, float at)> waiting = new Dictionary<ulong, (ZombieCorpseFall, float)>();
    private static readonly List<ulong> stale = new List<ulong>();
    // Living bodies bigger than the pack's (MarkGrown), and the dolls made for one such body alone.
    private static readonly HashSet<Animator> grownBodies = new HashSet<Animator>();
    private static readonly HashSet<RagdollCorpse> oneOff = new HashSet<RagdollCorpse>();
    private static int stockedFrame = -1;

    private readonly List<SkinnedMeshRenderer> skins = new List<SkinnedMeshRenderer>();
    private RagdollCorpse doll;
    private Animator animator;
    // The bone in the middle of its trunk, which the doll moves: where a zombie makes the body out from (ZombieMind).
    private Transform torso;
    private PlayerNetworkCorpse runner, sender;
    private CorpsePose pose;
    private Vector3 sinkFrom;
    private ulong id;
    private bool simulated, hasPose, sinking, sentRest, heardRest, boundsHeld, boundsFollow, held, orphaned;
    private float age, stillFor, movingFor, nextSend, nextPassThrough, lastPoseAt, lieUntil = float.PositiveInfinity, sinkAge;

    private static LayerMask Solid => ~((1 << DollLayer) | LayerMask.GetMask(EnemyLayer));

    /// <summary>Every dead zombie's body on this PC, sinking ones too: zombies that come upon one grow suspicious (ZombieMind).</summary>
    internal static IReadOnlyList<ZombieCorpse> Lying => lying;
    /// <summary>Sinking away: no longer a body to be found.</summary>
    internal bool Sinking => sinking;
    /// <summary>When it fell (game time).</summary>
    internal float FellAt { get; private set; }
    /// <summary>The player whose blow killed it, if a player's did.</summary>
    internal PlayerMovement Killer { get; private set; }
    /// <summary>The middle of its trunk as it lies.</summary>
    internal Vector3 Torso => torso != null ? torso.position : transform.position;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearStatics()
    {
        stowed.Clear(); made.Clear(); lying.Clear(); byId.Clear(); waiting.Clear(); grownBodies.Clear(); oneOff.Clear();
        stockedFrame = -1;
    }

    // ---- Laying ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Who runs a kill's fall: the player who landed it, or the host's player for a kill by no player. Here says whether
    /// that is this PC (always, alone).
    /// </summary>
    internal static PlayerMovement RunnerOf(in DamageHit hit, bool online, out bool here)
    {
        var killer = hit.Source != null ? hit.Source.GetComponentInParent<PlayerMovement>() : null;
        if (!online) { here = true; return killer; }
        if (killer != null) { here = killer.IsLocal; return killer; }
        here = NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
        foreach (var player in PlayerRoster.All)
            if (player != null && player.TryGetComponent(out NetworkObject network) && network.IsOwnedByServer) return player;
        return null;
    }

    /// <summary>
    /// The character leaves the dead enemy and falls as its corpse. Simulated: this PC runs the fall (and, in a room,
    /// sends it as id); otherwise it follows the runner's. Its playable graph must be gone already; human is its bones
    /// (RagdollCorpse.HumanBones), read while it lived. Lying is the doll it already lies on (knocked down), if any: the
    /// corpse carries on with it, still moving.
    /// </summary>
    internal static void Lay(GameObject body, Animator animator, Transform[] human, in DamageHit hit, bool simulated, ulong id,
        PlayerMovement runner, Vector3 velocity, RagdollCorpse lying = null)
    {
        body.transform.SetParent(null, true);
        var corpse = body.AddComponent<ZombieCorpse>();
        var doll = lying != null ? lying : TakeDoll(animator, human);
        // From here nothing but the doll moves the bones.
        animator.writeDefaultValuesOnDisable = false;
        animator.enabled = false;
        corpse.animator = animator;
        var chest = human != null ? human[(int)HumanBodyBones.Chest] : null;
        corpse.torso = chest != null ? chest : human != null ? human[(int)HumanBodyBones.Hips] : null;
        body.GetComponentsInChildren(true, corpse.skins);
        corpse.Begin(doll, hit, simulated, id, runner, velocity, lying != null);
    }

    /// <summary>The body of enemy <paramref name="id"/>'s corpse lying on this PC, or null: a throwing knife a friend put in it finds its bone (ThrowingKnives).</summary>
    internal static Animator AnimatorOf(ulong id) => id != 0UL && byId.TryGetValue(id, out var corpse) && corpse != null ? corpse.animator : null;

    private void Awake() => GameTables.Bind(TableName, TableRow, this);

    private void Begin(RagdollCorpse taken, in DamageHit hit, bool simulated, ulong id, PlayerMovement killer, Vector3 velocity,
        bool falling)
    {
        this.id = id;
        this.simulated = simulated;
        FellAt = Time.time;
        Killer = hit.Source != null ? hit.Source.GetComponentInParent<PlayerMovement>() : null;
        runner = killer != null ? killer.GetComponent<PlayerNetworkCorpse>() : null;
        lying.Add(this);
        if (id != 0UL) byId[id] = this;
        HitFlashRenderer.For(this)?.KillFlash(hit.Point, hit.IsCritical ? .16f : .11f);
        doll = taken;
        if (doll == null) { lieUntil = lieSeconds; Trim(mostCorpses); return; }
        doll.Lay(simulated, muscleSeconds);
        doll.Struck += OnStruck;
        LooseBounds();
        PassEveryoneThrough();
        if (simulated)
        {
            // No limb motion was tracked while it lived, so every part carries on at the body's speed; a body already
            // down keeps the motion it has (its muscles are long gone).
            if (!falling)
            {
                doll.Launch(velocity, 0f, 0f);
                doll.Brace(muscleHold, muscleSeconds, jointFriction);
            }
            if (hit.Direction.sqrMagnitude > 1e-4f) doll.Knock(hit.Direction.normalized * deathPush);
        }
        else if (waiting.TryGetValue(id, out var early))
        {
            waiting.Remove(id);
            Show(early.fall);
        }
        Trim(mostCorpses);
        TrimDolls(mostDolls);
    }

    // ---- Each frame ------------------------------------------------------------------------------------------

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        age += dt;
        // A knockdown it died in may still hand the Animator back when its enemy recovers on this PC.
        if (animator != null && animator.enabled) animator.enabled = false;
        if (doll != null)
        {
            if (simulated) Run(dt);
            else Follow(dt);
        }
        if (doll != null && Time.time >= nextPassThrough) PassEveryoneThrough();
        if (!sinking && age >= lieUntil) StartSinking();
        if (sinking) Sink(dt);
    }

    // At rest it sleeps and sends nothing; a push wakes it, and it sends again until it rests again.
    private void Run(float dt)
    {
        if (held) return;
        doll.PoseBody();
        stillFor = doll.Settled(settleSpeed) ? stillFor + dt : 0f;
        bool still = AtRest;
        movingFor = still ? 0f : movingFor + dt;
        if (still) { Rest(); HoldBounds(); }
        else LooseBounds();
        if (id != 0UL && !orphaned)
        {
            if (!still && Time.time >= nextSend) Send(false);
            else if (still && !sentRest) Send(true);
        }
        // Never coming to rest (caught on something, jittering): held as it is, still to be pushed.
        if (movingFor >= longestFall) Hold();
    }

    // Players walk through bodies rather than climbing them, as through their own (PlayerRagdoll).
    private void PassEveryoneThrough()
    {
        nextPassThrough = Time.time + PassThroughEvery;
        foreach (var player in PlayerRoster.All)
            if (player != null && player.TryGetComponent(out CharacterController walker) && walker.enabled) doll.PassThrough(walker);
    }

    private bool AtRest => simulated ? held || (age >= shortestFall && stillFor >= StillTime) : heardRest;

    // From its first rest the body lies its time out.
    private void Rest()
    {
        if (float.IsPositiveInfinity(lieUntil)) lieUntil = age + lieSeconds;
    }

    private void Send(bool rested)
    {
        nextSend = Time.time + SendInterval;
        sentRest = rested;
        if (sender == null)
        {
            var local = PlayerRoster.Local;
            if (local == null || !local.TryGetComponent(out sender)) return;
        }
        sender.SendZombieFall(new ZombieCorpseFall(id, doll.Capture(), rested));
    }

    private void Follow(float dt)
    {
        if (hasPose) doll.Follow(pose, dt);
        doll.PoseBody();
        if (heardRest)
        {
            // Once it has eased onto the resting pose.
            if (Time.time - lastPoseAt >= StillTime) HoldBounds();
            return;
        }
        LooseBounds();
        // Its runner went quiet mid-fall (it left): this PC takes the body over where it is, and it is cleared soon.
        float quiet = hasPose ? Time.time - lastPoseAt : age;
        if (quiet < orphanWait) return;
        TakeOver();
        lieUntil = Mathf.Min(lieUntil, age + orphanLie);
    }

    // Runs the body here from now on, pushed by this PC's own blows; it sends nothing (each PC left behind does this).
    private void TakeOver()
    {
        if (doll == null || simulated) return;
        simulated = orphaned = true;
        runner = null;
        doll.Simulate();
    }

    private void Show(in ZombieCorpseFall fall)
    {
        pose = fall.Pose;
        hasPose = true;
        lastPoseAt = Time.time;
        heardRest = fall.Rested;
        if (heardRest) Rest();
    }

    // Held still where it lies, kinematic, keeping its doll; running it, a last pose goes out so the others stop where
    // this one does. A shot or a blow lets it go again (Wake).
    private void Hold()
    {
        if (doll == null || held || !simulated) return;
        if (id != 0UL && !orphaned) Send(true);
        Rest();
        held = true;
        doll.Hold();
        HoldBounds();
    }

    // Struck while held: physics moves it again, and it is watched and sent until it rests again.
    private void Wake()
    {
        if (!held || doll == null) return;
        held = false;
        stillFor = movingFor = 0f;
        sentRest = false;
        doll.Thaw();
        LooseBounds();
    }

    /// <summary>Sinking away (or gone): the doll goes back to the pool and the body keeps its last pose.</summary>
    private void Release()
    {
        HoldBounds();
        if (doll == null) return;
        doll.Struck -= OnStruck;
        Give(doll);
        doll = null;
    }

    // A skinned mesh is drawn only where its bounds say, and these rigs size them from the Root bone at the feet, which
    // stays where the zombie stood while the doll carries the hips away: a body that fell away from that spot vanished
    // whenever the spot left the screen. While it moves the bounds follow the bones (as players' bodies always do); at
    // rest they are fixed round where it lies, so a still body costs no more than a living one.
    private void LooseBounds()
    {
        if (boundsFollow) return;
        boundsFollow = true;
        boundsHeld = false;
        foreach (var skin in skins) if (skin != null) skin.updateWhenOffscreen = true;
    }

    private void HoldBounds()
    {
        if (boundsHeld || !boundsFollow) return;
        boundsHeld = true;
        boundsFollow = false;
        foreach (var skin in skins)
        {
            if (skin == null) continue;
            Bounds seen = skin.bounds;
            Transform space = skin.rootBone != null ? skin.rootBone : skin.transform;
            Vector3 min = Vector3.positiveInfinity, max = Vector3.negativeInfinity;
            for (int corner = 0; corner < 8; corner++)
            {
                Vector3 point = seen.center + Vector3.Scale(seen.extents,
                    new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f));
                Vector3 local = space.InverseTransformPoint(point);
                min = Vector3.Min(min, local);
                max = Vector3.Max(max, local);
            }
            skin.localBounds = new Bounds((min + max) * .5f, max - min);
            skin.updateWhenOffscreen = false;
        }
    }

    private void StartSinking()
    {
        Release();
        sinking = true;
        sinkFrom = transform.position;
    }

    private void Sink(float dt)
    {
        sinkAge += dt;
        float done = Mathf.Clamp01(sinkAge / sinkSeconds);
        transform.position = sinkFrom - Vector3.up * (done * done * sinkDepth);
        if (done >= 1f) Destroy(gameObject);
    }

    // Past the most a map keeps, the oldest still lying sink at once.
    private static void Trim(int most)
    {
        lying.RemoveAll(corpse => corpse == null);
        int over = -most;
        foreach (var corpse in lying) if (!corpse.sinking) over++;
        for (int i = 0; i < lying.Count && over > 0; i++)
        {
            if (lying[i].sinking) continue;
            lying[i].StartSinking();
            over--;
        }
    }

    // Past the most bodies a map keeps moving, the oldest at rest are held still where they lie; if a fight keeps that
    // many moving, the oldest moving one is. Held, they keep their dolls: still shot and pushed, which lets them go again.
    private static void TrimDolls(int most)
    {
        int over = -most;
        foreach (var corpse in lying) if (corpse != null && corpse.doll != null && corpse.simulated && !corpse.held) over++;
        for (int pass = 0; pass < 2 && over > 0; pass++)
            for (int i = 0; i < lying.Count && over > 0; i++)
            {
                var corpse = lying[i];
                if (corpse == null || corpse.doll == null || !corpse.simulated || corpse.held || (pass == 0 && !corpse.AtRest)) continue;
                corpse.Hold();
                over--;
            }
    }

    private void OnStruck(DamageHit hit)
    {
        if (doll == null || hit.Direction.sqrMagnitude < 1e-4f) return;
        ImpactVfx.Blood(hit.Point, hit.Direction, false, false, hit.Method == DamageMethod.Bullet, doll);
        Vector3 impulse = hit.Direction.normalized * Mathf.Max(leastPush, hit.Amount * pushPerDamage);
        if (simulated)
        {
            Wake();
            doll.Push(hit.Point, impulse, mostPushSpeed);
        }
        else if (runner != null) runner.PushZombie(id, hit.Point, impulse);
    }

    private void OnDestroy()
    {
        lying.Remove(this);
        if (id != 0UL && byId.TryGetValue(id, out var self) && self == this) byId.Remove(id);
        if (doll == null) return;
        doll.Struck -= OnStruck;
        Give(doll);
        doll = null;
    }

    // ---- Network ---------------------------------------------------------------------------------------------

    /// <summary>
    /// A fall another PC runs: a corpse's, or a knocked-down zombie's (ZombieBody). One heard before its body is down
    /// here waits a moment for it.
    /// </summary>
    internal static void Receive(in ZombieCorpseFall fall)
    {
        if (byId.TryGetValue(fall.Id, out var corpse) && corpse != null)
        {
            if (!corpse.simulated && corpse.doll != null) corpse.Show(fall);
            return;
        }
        if (ZombieBody.Follows(fall.Id, out var down))
        {
            down.Show(fall);
            return;
        }
        stale.Clear();
        foreach (var pair in waiting) if (Time.time - pair.Value.at > WaitingLife) stale.Add(pair.Key);
        foreach (ulong old in stale) waiting.Remove(old);
        waiting[fall.Id] = (fall, Time.time);
    }

    /// <summary>A hit another PC landed on a corpse this PC runs.</summary>
    internal static void TakePush(ulong id, Vector3 point, Vector3 impulse)
    {
        if (byId.TryGetValue(id, out var corpse) && corpse != null && corpse.simulated && corpse.doll != null)
        {
            corpse.Wake();
            corpse.doll.Push(point, impulse, corpse.mostPushSpeed);
        }
    }

    /// <summary>The newest fall heard for this id before its body went down here, if one waits.</summary>
    internal static bool TakeWaiting(ulong id, out ZombieCorpseFall fall)
    {
        fall = default;
        if (!waiting.TryGetValue(id, out var early) || Time.time - early.at > WaitingLife) return false;
        waiting.Remove(id);
        fall = early.fall;
        return true;
    }

    // ---- Dolls ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Makes dolls ahead for this living body's rig, at most one a frame: a few at first, then more while lying bodies
    /// hold most of them, so a death finds one free.
    /// </summary>
    internal static void Stock(Animator live)
    {
        if (live == null || live.avatar == null || stockedFrame == Time.frameCount || grownBodies.Contains(live)) return;
        var all = Made(live.avatar);
        all.RemoveAll(doll => doll == null);
        if (all.Count >= MostMade || (all.Count >= Stocked && Stowed(live.avatar).Count >= Spare)) return;
        stockedFrame = Time.frameCount;
        var fresh = RagdollCorpse.Prepare(live, DollLayer, Solid);
        if (fresh == null) return;
        all.Add(fresh);
        Stowed(live.avatar).Push(fresh);
    }

    /// <summary>A stowed doll for this body's rig, bound to it (made on the spot if none is free). Give it back with GiveDoll.</summary>
    internal static RagdollCorpse TakeDoll(Animator live, Transform[] human)
    {
        if (live == null || live.avatar == null || human == null) return null;
        // A body bigger than the pack's (a brute) gets a doll of its own size, used once and never stowed for others (it
        // stays marked: knocked down by a lost leg first, it needs another such doll when it dies).
        if (grownBodies.Contains(live))
        {
            var own = RagdollCorpse.Prepare(live, DollLayer, Solid);
            if (own != null) oneOff.Add(own);
            return own;
        }
        var stack = Stowed(live.avatar);
        while (stack.Count > 0)
        {
            var doll = stack.Pop();
            if (doll == null) continue;
            doll.Bind(human);
            return doll;
        }
        // None free: a fight with more falling at once than were made ahead. Made for this very body, so it needs no Bind.
        var fresh = RagdollCorpse.Prepare(live, DollLayer, Solid);
        if (fresh != null) Made(live.avatar).Add(fresh);
        return fresh;
    }

    internal static void GiveDoll(RagdollCorpse doll) => Give(doll);

    /// <summary>
    /// This living body stands bigger than the pack's own (a brute, ZombieBody.Brute.cs): its doll will be made for it
    /// when it falls, since the stowed ones fit the pack's size.
    /// </summary>
    internal static void MarkGrown(Animator live)
    {
        if (live == null) return;
        grownBodies.RemoveWhere(body => body == null);
        grownBodies.Add(live);
    }

    private static void Give(RagdollCorpse doll)
    {
        if (doll == null) return;
        if (oneOff.Remove(doll)) { Destroy(doll.gameObject); return; }
        doll.Stow();
        if (doll.Avatar != null) Stowed(doll.Avatar).Push(doll);
    }

    private static Stack<RagdollCorpse> Stowed(Avatar avatar)
    {
        if (!stowed.TryGetValue(avatar, out var stack)) stowed[avatar] = stack = new Stack<RagdollCorpse>();
        return stack;
    }

    private static List<RagdollCorpse> Made(Avatar avatar)
    {
        if (!made.TryGetValue(avatar, out var list)) made[avatar] = list = new List<RagdollCorpse>();
        return list;
    }
}
