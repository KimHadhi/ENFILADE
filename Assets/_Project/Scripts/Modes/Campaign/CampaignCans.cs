using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Stage 5C: the fuel cans a Collect step wants (chapter 3: four into the depot's generator), after L4D2's scavenge.
/// [F] picks one up; carried, it fills both hands, so no weapon is out until it is put down ([F] again) or poured ([F] at
/// the step's spot, PlayerWeaponSlots.HoldHandsFull). A carrier who goes down, dies or leaves drops it where they are.
/// Friend feedback C17: carried, the left button throws it; shot where it lies or flies, it blows up (zombies near it are
/// hurt and knocked flat, the brute only hurt) and burns a while, hurting zombies that stand in the fire, then turns up
/// again where it first lay, so the step can still be done. The blast and the fire are drawn by CanBlastEffect, in the
/// game's own flat style.
/// Since 0.9.245 a can is Impact CFX's red jerry can (PropShelf.FuelCan; the user, 2026-11-21: "3챕에 기름통도 저 impact
/// cfx에 있는 기름통으로 바꿔 줘 물리랑 해서"), a real physics body: thrown it tumbles, bounces and rolls, clatters as the
/// pack made it (CollisionEffects), is knocked about by kicks, shoves and blasts, and a can that strikes a zombie hard hurts
/// it as the thrower's blow (PhysicsProp, PropStrike: a fuel can floors a normal zombie). The map's marker for each can (a
/// plain box) only says where it first stands. A kill by the can's fire is KillMarks.Fire (the flame under the crosshair).
/// The host, or the PC alone, keeps every can's state and runs its body; it tells the others (a named message on change,
/// every frame while one moves, and every couple of seconds), whose copies follow it and touch nothing. A guest asks the
/// host to pick up, put down, pour or throw, and a shot from a guest goes to the host (CampaignRun). What the host judges
/// for a guest (their can's blast, fire or strike) carries KillMarks.JudgedByHost, so their PC still shows the kill. A wipe
/// puts every can back where it started. Each PC hangs a carried can from its carrier: in front of this PC's own camera,
/// from the right hand on everyone else. CampaignRun owns it: input, the step, the messages' registration. Numbers: the
/// constants below.
/// </summary>
internal sealed class CampaignCans
{
    public const string StateMessage = "ENFILADE.Cans", AskMessage = "ENFILADE.CanAsk";
    public enum Act : byte { Pick, Drop, Pour, Throw }
    private enum Mode : byte { Lying, Carried, Poured, Flying, Burning, Gone }

    // How near a can must be to pick it up, and a little more for a guest whose copy lags (m).
    public const float Reach = 1.8f;
    private const float ReachSlack = .8f, ShareEvery = 2f, DropAhead = .6f, GroundSearch = 4f;
    // A throw: how fast it leaves the hands along the aim and up (m/s), how fast it tumbles (rad/s), and the longest it
    // counts as flying, whatever it does (s).
    private const float ThrowSpeed = 11f, ThrowLift = 2.5f, ThrowSpin = 8f, LongestFlight = 6f;
    // A thrown can lies again (to be picked up) once slower than this (m/s), and not before this long after the throw (s).
    private const float RestSpeed = .6f, RestAfter = .25f;
    // A 20 L jerry can about half full (kg): heavy enough to floor the zombie it is thrown at (PropStrike), too heavy to fly
    // off at a bullet's push. The pack's demo can weighs 1 kg, an empty one.
    private const float CanMass = 8f;
    // A guest's copy closes this share of the gap to the host's word every 1/60 s, and jumps across anything farther (m).
    private const float FollowRate = .35f, FollowJump = 3f;
    // The blast: how far it reaches (m), its damage at the middle and at the edge, how long it knocks zombies down (s).
    private const float BlastRadius = 5f, BlastDamage = 180f, BlastEdgeDamage = 60f, BlastKnockdown = 2.2f;
    // The fire after it: how wide (m), damage a second to a zombie in it, how long it burns, how long the can is gone (s).
    private const float FireRadius = 3f, FireDamage = 40f, FireSeconds = 8f, FireEvery = .5f, GoneSeconds = 12f;
    // A can poured on a burn hold's pile is back at the station this long after (s).
    private const float PouredBack = 12f;
    // A view within this of the blast shakes, harder nearer (m).
    private const float ShakeReach = 18f;
    // Where a carried can sits in this PC's own view, from the camera (m: right, up, ahead).
    private static readonly Vector3 InView = new Vector3(.28f, -.42f, .62f);

    private struct Can : INetworkSerializeByMemcpy
    {
        public byte Mode;
        public ulong Carrier;
        // Lying, carried or flying: the body's place and turn; burning: the fire's place on the floor.
        public Vector3 Position;
        public Quaternion Rotation;
    }

    private struct Ask : INetworkSerializeByMemcpy
    {
        public byte Act, Index;
        public Vector3 Aim;
    }

    private readonly Transform[] cans;
    private readonly Rigidbody[] bodies;
    private readonly PhysicsProp[] props;
    // Each body's own solid shape, off whenever its body is moved by hand (carried; every copy on a guest).
    private readonly List<Collider>[] solids;
    // From each body's pivot down to the bottom of its shape (m), so it is set down standing on the floor.
    private readonly float[] lift;
    private readonly CampaignCanTarget[] targets;
    private readonly Can[] state, start;
    // The host's own bookkeeping: how long a can has been flying, the time left burning or gone, and who set it off.
    private readonly float[] timeLeft, flown, nextBurn;
    private readonly ulong[] setOffBy;
    // What each PC last showed of each can, so a can catching fire shows its blast once.
    private readonly byte[] shown;
    private readonly CanBlastEffect[] fires;
    private readonly HashSet<EnemyHealth> struck = new HashSet<EnemyHealth>();
    private AudioClip blastSound;
    private bool changed = true;
    private float nextShare;

    public CampaignCans(Transform[] marks)
    {
        marks ??= new Transform[0];
        int count = marks.Length;
        cans = new Transform[count];
        bodies = new Rigidbody[count];
        props = new PhysicsProp[count];
        solids = new List<Collider>[count];
        lift = new float[count];
        state = new Can[count];
        start = new Can[count];
        targets = new CampaignCanTarget[count];
        timeLeft = new float[count];
        flown = new float[count];
        nextBurn = new float[count];
        setOffBy = new ulong[count];
        shown = new byte[count];
        fires = new CanBlastEffect[count];
        var shelf = count > 0 ? PropShelf.Get() : null;
        var fuelCan = shelf != null ? shelf.FuelCan : null;
        if (count > 0) CollisionEffects.Ensure();
        for (int i = 0; i < count; i++)
        {
            var mark = marks[i];
            if (mark == null) continue;
            cans[i] = Dress(mark, fuelCan, i);
            // Still until Place sets it loose on the PC that runs it (the host).
            Simulate(i, false);
            var turn = Quaternion.Euler(0f, mark.eulerAngles.y, 0f);
            start[i] = state[i] = new Can { Mode = (byte)Mode.Lying, Position = RestOn(i, mark.position, -1), Rotation = turn };
            cans[i].SetPositionAndRotation(state[i].Position, turn);
            targets[i] = cans[i].gameObject.AddComponent<CampaignCanTarget>();
            targets[i].Index = i;
        }
    }

    // A can's body: the shelf's fuel can where the map's marker stands (the marker, a plain box, is hidden), or the marker
    // itself made solid when there is none. Either way a physics body of CanMass, whose strikes the host judges.
    private Transform Dress(Transform mark, GameObject prefab, int index)
    {
        Transform body;
        if (prefab != null)
        {
            var made = Object.Instantiate(prefab, mark.position, Quaternion.Euler(0f, mark.eulerAngles.y, 0f), mark.parent);
            made.name = mark.name;
            mark.gameObject.SetActive(false);
            body = made.transform;
        }
        else
        {
            body = mark;
            if (!body.TryGetComponent(out Collider _)) body.gameObject.AddComponent<BoxCollider>();
        }
        if (!body.TryGetComponent(out Rigidbody rb)) rb = body.gameObject.AddComponent<Rigidbody>();
        rb.mass = CanMass;
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        // Thrown fast at a zombie, it must not pass through its body between two physics steps.
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        bodies[index] = rb;
        if (!body.TryGetComponent(out PhysicsProp prop)) prop = body.gameObject.AddComponent<PhysicsProp>();
        prop.HostJudged = true;
        props[index] = prop;
        var own = new List<Collider>();
        foreach (var collider in body.GetComponentsInChildren<Collider>(true)) if (!collider.isTrigger) own.Add(collider);
        solids[index] = own;
        lift[index] = Lift(body);
        return body;
    }

    // From the body's pivot down to the bottom of what it shows.
    private static float Lift(Transform body)
    {
        bool any = false;
        var bounds = default(Bounds);
        foreach (var renderer in body.GetComponentsInChildren<Renderer>())
        {
            if (!any) { bounds = renderer.bounds; any = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        return any ? Mathf.Max(0f, body.position.y - bounds.min.y) : 0f;
    }

    // Where can <index>'s body stands on whatever is under <at> (the players' layer aside unless -1).
    private Vector3 RestOn(int index, Vector3 at, int playerLayer) => Floor(index, at + Vector3.up * .3f, playerLayer) + Vector3.up * lift[index];

    // The floor under <at>, can <index>'s own shape aside, so a can set down where it already lies finds the floor, not itself.
    private Vector3 Floor(int index, Vector3 at, int playerLayer)
    {
        var own = solids[index];
        if (own == null) return Ground(at, playerLayer);
        var was = new bool[own.Count];
        for (int i = 0; i < own.Count; i++) if (own[i] != null) { was[i] = own[i].enabled; own[i].enabled = false; }
        var floor = Ground(at, playerLayer);
        for (int i = 0; i < own.Count; i++) if (own[i] != null) own[i].enabled = was[i];
        return floor;
    }

    public bool Any => cans.Length > 0;

    /// <summary>What a blast sounds like (CampaignRun's, set by the builder); none is fine.</summary>
    public void Dress(AudioClip sound) => blastSound = sound;

    /// <summary>
    /// How many times a can has been poured since the start or the last wipe, whatever became of it after: a burn hold
    /// counts its fuel by it (CampaignRun), as its poured cans go back to the station.
    /// </summary>
    public int PourCount { get; private set; }

    /// <summary>
    /// While a burn hold runs (CampaignRun): a can poured goes back where it first lay after PouredBack seconds, so the
    /// fire can always be fed again.
    /// </summary>
    public bool RespawnPoured { get; set; }

    public int Poured
    {
        get
        {
            int poured = 0;
            foreach (var can in state) if (can.Mode == (byte)Mode.Poured) poured++;
            return poured;
        }
    }

    /// <summary>
    /// On the host, before anyone has touched it: can <paramref name="index"/> first lies at <paramref name="position"/> this
    /// run (GimmickCanSpots: where a run's seed puts the yard's cans), and a wipe brings it back there.
    /// </summary>
    public void Relocate(int index, Vector3 position)
    {
        if (index < 0 || index >= state.Length) return;
        start[index].Position = RestOn(index, position, -1);
        if (state[index].Mode == (byte)Mode.Lying)
        {
            state[index].Position = start[index].Position;
            state[index].Rotation = start[index].Rotation;
            Teleport(index, start[index].Position, start[index].Rotation);
        }
        changed = true;
    }

    /// <summary>
    /// Can <paramref name="index"/>'s own body in the scene, which Place keeps wherever the can is on this PC (lying, in
    /// its carrier's hands, in the air). A hint hung on it goes where the can goes; null when the run has no such can.
    /// </summary>
    public Transform Body(int index) => index >= 0 && index < cans.Length ? cans[index] : null;

    /// <summary>
    /// Whether can <paramref name="index"/> is one to go and get now, on this PC's word of it: lying, carried or in the
    /// air. Not once it is poured, nor while it burns or is burnt away after a shot (it waits again when it is back where
    /// it first lay).
    /// </summary>
    public bool Waits(int index)
    {
        if (index < 0 || index >= state.Length) return false;
        var mode = (Mode)state[index].Mode;
        return mode == Mode.Lying || mode == Mode.Carried || mode == Mode.Flying;
    }

    /// <summary>The can this player carries, or -1.</summary>
    public int CarriedBy(PlayerMovement player)
    {
        if (player == null) return -1;
        ulong id = PlayerRoster.IdOf(player);
        for (int i = 0; i < state.Length; i++) if (state[i].Mode == (byte)Mode.Carried && state[i].Carrier == id) return i;
        return -1;
    }

    /// <summary>The nearest can lying within reach of <paramref name="at"/>, or -1.</summary>
    public int NearestLying(Vector3 at, float reach = Reach)
    {
        int nearest = -1;
        float best = reach * reach;
        for (int i = 0; i < state.Length; i++)
        {
            if (state[i].Mode != (byte)Mode.Lying) continue;
            float distance = (state[i].Position - at).sqrMagnitude;
            if (distance <= best) { best = distance; nearest = i; }
        }
        return nearest;
    }

    // ---- The host's side ----

    /// <summary>On the host: does it, if it may. <paramref name="mayPour"/> says whether this player stands where the
    /// fuel goes while the step wants it; <paramref name="aim"/> is where a throw goes.</summary>
    public bool Apply(Act act, int index, PlayerMovement who, bool mayPour, Vector3 aim = default)
    {
        if (index < 0 || index >= state.Length || who == null || who.IsDead || who.DamageReceiver.IsDowned) return false;
        var can = state[index];
        switch (act)
        {
            case Act.Pick:
                if (can.Mode != (byte)Mode.Lying || CarriedBy(who) >= 0
                    || (can.Position - who.transform.position).sqrMagnitude > (Reach + ReachSlack) * (Reach + ReachSlack))
                    return false;
                can.Mode = (byte)Mode.Carried;
                can.Carrier = PlayerRoster.IdOf(who);
                Simulate(index, false);
                break;
            case Act.Drop:
                if (can.Mode != (byte)Mode.Carried || can.Carrier != PlayerRoster.IdOf(who)) return false;
                can.Mode = (byte)Mode.Lying;
                can.Position = RestOn(index, who.transform.position + who.transform.forward * DropAhead, who.gameObject.layer);
                can.Rotation = Quaternion.Euler(0f, who.transform.eulerAngles.y, 0f);
                Teleport(index, can.Position, can.Rotation);
                break;
            case Act.Pour:
                if (can.Mode != (byte)Mode.Carried || can.Carrier != PlayerRoster.IdOf(who) || !mayPour) return false;
                can.Mode = (byte)Mode.Poured;
                PourCount++;
                timeLeft[index] = PouredBack;
                break;
            case Act.Throw:
                if (can.Mode != (byte)Mode.Carried || can.Carrier != PlayerRoster.IdOf(who)) return false;
                if (aim.sqrMagnitude < 1e-4f) aim = who.transform.forward;
                aim.Normalize();
                can.Mode = (byte)Mode.Flying;
                can.Position = who.transform.position + Vector3.up * 1.4f + aim * .6f;
                can.Rotation = Quaternion.LookRotation(Vector3.ProjectOnPlane(aim, Vector3.up).sqrMagnitude > 1e-4f ? Vector3.ProjectOnPlane(aim, Vector3.up) : who.transform.forward);
                flown[index] = 0f;
                Throw(index, can.Position, can.Rotation, aim * ThrowSpeed + Vector3.up * ThrowLift, who);
                break;
        }
        state[index] = can;
        changed = true;
        return true;
    }

    /// <summary>
    /// On the host: a player shot this can. Lying or in the air it blows up where it is and starts burning; carried,
    /// poured, burning or gone, nothing happens.
    /// </summary>
    public void Ignite(int index, PlayerMovement shooter)
    {
        if (index < 0 || index >= state.Length) return;
        var can = state[index];
        if (can.Mode != (byte)Mode.Lying && can.Mode != (byte)Mode.Flying) return;
        // The fire burns on the floor under wherever its body is now.
        can.Position = Floor(index, (cans[index] != null ? cans[index].position : can.Position) + Vector3.up * .3f, -1);
        can.Mode = (byte)Mode.Burning;
        state[index] = can;
        timeLeft[index] = FireSeconds;
        nextBurn[index] = FireEvery;
        setOffBy[index] = shooter != null ? PlayerRoster.IdOf(shooter) : 0UL;
        Blast(can.Position, shooter);
        changed = true;
    }

    /// <summary>
    /// On the host, each frame: a can whose carrier went down, died or left falls where they were; loose cans are where
    /// their bodies are; fires burn and go out; a can gone that long is back where it first lay.
    /// </summary>
    public void Tick(float dt)
    {
        for (int i = 0; i < state.Length; i++)
        {
            switch ((Mode)state[i].Mode)
            {
                case Mode.Carried:
                    var carrier = PlayerRoster.ById(state[i].Carrier);
                    if (carrier != null && !carrier.IsDead && !carrier.DamageReceiver.IsDowned) break;
                    state[i].Mode = (byte)Mode.Lying;
                    state[i].Position = carrier != null ? RestOn(i, carrier.transform.position, carrier.gameObject.layer)
                        : cans[i] != null ? RestOn(i, cans[i].position, -1) : state[i].Position;
                    Teleport(i, state[i].Position, state[i].Rotation);
                    changed = true;
                    break;
                case Mode.Lying:
                case Mode.Flying:
                    Track(i, dt);
                    break;
                case Mode.Burning:
                    if ((nextBurn[i] -= dt) <= 0f) { nextBurn[i] += FireEvery; Burn(state[i].Position, PlayerRoster.ById(setOffBy[i])); }
                    if ((timeLeft[i] -= dt) > 0f) break;
                    state[i].Mode = (byte)Mode.Gone;
                    timeLeft[i] = GoneSeconds;
                    changed = true;
                    break;
                case Mode.Gone:
                    if ((timeLeft[i] -= dt) > 0f) break;
                    Restart(i);
                    break;
                case Mode.Poured:
                    if (!RespawnPoured || (timeLeft[i] -= dt) > 0f) break;
                    Restart(i);
                    break;
            }
        }
    }

    // Back where it first lay, at rest.
    private void Restart(int index)
    {
        state[index] = start[index];
        Teleport(index, start[index].Position, start[index].Rotation);
        changed = true;
    }

    // The host's word on a loose can is where its body is: physics moves it (a throw, a kick, a shove, a blast), and the
    // others hear of it every frame it moves. A thrown can lies again once it has all but stopped.
    private void Track(int index, float dt)
    {
        var body = cans[index];
        if (body == null || !body.gameObject.activeInHierarchy) return;
        if (body.position.y < -100f) { Restart(index); return; }
        var can = state[index];
        if ((body.position - can.Position).sqrMagnitude > 1e-6f || Quaternion.Angle(body.rotation, Turn(can.Rotation)) > .1f)
        {
            can.Position = body.position;
            can.Rotation = body.rotation;
            changed = true;
        }
        if (can.Mode == (byte)Mode.Flying)
        {
            flown[index] += dt;
            var rb = bodies[index];
            float speed = rb != null && !rb.isKinematic ? rb.linearVelocity.magnitude : 0f;
            if (flown[index] >= LongestFlight || flown[index] >= RestAfter && speed < RestSpeed)
            {
                can.Mode = (byte)Mode.Lying;
                changed = true;
            }
        }
        state[index] = can;
    }

    // Out of the hands and into the air: its body on physics, leaving along the aim and tumbling, the thrower's blow if it
    // strikes a zombie (PropStrike).
    private void Throw(int index, Vector3 from, Quaternion turn, Vector3 velocity, PlayerMovement by)
    {
        Teleport(index, from, turn);
        Simulate(index, true);
        var rb = bodies[index];
        if (rb != null && !rb.isKinematic)
        {
            rb.linearVelocity = velocity;
            rb.angularVelocity = Random.onUnitSphere * ThrowSpin;
        }
        if (props[index] != null && by != null) props[index].SetAttacker(by.gameObject);
    }

    // Its body put somewhere at once, standing still.
    private void Teleport(int index, Vector3 position, Quaternion rotation)
    {
        var body = cans[index];
        if (body == null) return;
        rotation = Turn(rotation);
        var rb = bodies[index];
        if (rb != null && body.gameObject.activeInHierarchy)
        {
            if (!rb.isKinematic) { rb.linearVelocity = Vector3.zero; rb.angularVelocity = Vector3.zero; }
            rb.position = position;
            rb.rotation = rotation;
        }
        body.SetPositionAndRotation(position, rotation);
    }

    // The host's body runs on physics while the can lies or flies. Carried, or on a guest's PC at all, it is moved by hand
    // and touches nothing: its solid shape is off, so it never shoves its carrier or a guest's player, and only the host's
    // body strikes anyone.
    private void Simulate(int index, bool on)
    {
        var rb = bodies[index];
        if (rb == null || rb.isKinematic != on) return;
        rb.isKinematic = !on;
        if (solids[index] != null) foreach (var collider in solids[index]) if (collider != null) collider.enabled = on;
        if (on) rb.WakeUp();
    }

    // A turn from a message: an unset one (all zero) is upright.
    private static Quaternion Turn(Quaternion turn)
        => turn.x == 0f && turn.y == 0f && turn.z == 0f && turn.w == 0f ? Quaternion.identity : turn;

    // The blast: zombies within reach are hurt by how near they are, and knocked flat (a brute shrugs that off).
    private void Blast(Vector3 at, PlayerMovement by)
    {
        foreach (var enemy in EnemiesNear(at, BlastRadius))
        {
            Vector3 away = Vector3.ProjectOnPlane(enemy.transform.position - at, Vector3.up);
            float near = 1f - Mathf.Clamp01(away.magnitude / BlastRadius);
            away = away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.forward;
            Hurt(enemy, Mathf.Lerp(BlastEdgeDamage, BlastDamage, near), at, away, by, false);
            if (enemy != null && enemy.IsAlive && enemy.TryGetComponent(out MeleeCombatTarget melee)) melee.KnockDown(away, BlastKnockdown);
        }
    }

    // The fire: every zombie standing in it burns.
    private void Burn(Vector3 at, PlayerMovement by)
    {
        foreach (var enemy in EnemiesNear(at, FireRadius))
            Hurt(enemy, FireDamage * FireEvery, at, Vector3.ProjectOnPlane(enemy.transform.position - at, Vector3.up), by, true);
    }

    private IEnumerable<EnemyHealth> EnemiesNear(Vector3 at, float radius)
    {
        struck.Clear();
        foreach (var collider in Physics.OverlapSphere(at, radius, ~0, QueryTriggerInteraction.Collide))
        {
            var enemy = collider.GetComponentInParent<EnemyHealth>();
            if (enemy != null && enemy.IsAlive) struck.Add(enemy);
        }
        return new List<EnemyHealth>(struck);
    }

    // Credited to whoever set it off, as their own kill: a fire's burn is KillMarks.Fire, and what the host judges for a
    // player on another PC is JudgedByHost, so their PC shows the kill when it hears of it.
    private static void Hurt(EnemyHealth enemy, float amount, Vector3 at, Vector3 away, PlayerMovement by, bool fire)
    {
        if (enemy == null || !enemy.IsAlive || amount <= 0f) return;
        var marks = (fire ? KillMarks.Fire : KillMarks.None) | (by != null && !by.IsLocal ? KillMarks.JudgedByHost : KillMarks.None);
        enemy.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), amount, DamageKind.Attack, enemy.transform.position + Vector3.up,
            away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.forward, by != null ? by.gameObject : null, false, 0f, true, origin: at,
            marks: marks));
    }

    /// <summary>On the host: every can back where it started (a wipe).</summary>
    public void Reset()
    {
        for (int i = 0; i < state.Length; i++) Restart(i);
        changed = true;
    }

    /// <summary>On the host in a room: the cans' state to everyone, when it changed and every couple of seconds.</summary>
    public void Share(NetworkManager network)
    {
        if (state.Length == 0 || network == null || !network.IsListening || network.CustomMessagingManager == null) return;
        if (!changed && Time.unscaledTime < nextShare) return;
        changed = false;
        nextShare = Time.unscaledTime + ShareEvery;
        using var writer = new FastBufferWriter(sizeof(byte) + state.Length * FastBufferWriter.GetWriteSize<Can>(), Allocator.Temp);
        writer.WriteValueSafe((byte)state.Length);
        foreach (var can in state) writer.WriteValueSafe(can);
        network.CustomMessagingManager.SendNamedMessageToAll(StateMessage, writer);
    }

    /// <summary>On the host: a guest's ask, done if it may be.</summary>
    public void ReceiveAsk(ulong sender, FastBufferReader reader, System.Func<PlayerMovement, bool> mayPour)
    {
        reader.ReadValueSafe(out Ask ask);
        var who = PlayerRoster.ById(sender);
        if (who != null) Apply((Act)ask.Act, ask.Index, who, mayPour(who), ask.Aim);
    }

    // ---- Every PC ----

    /// <summary>On a guest: asks the host.</summary>
    public static void Send(NetworkManager network, Act act, int index, Vector3 aim = default)
    {
        if (network == null || !network.IsListening || network.CustomMessagingManager == null) return;
        using var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize<Ask>(), Allocator.Temp);
        writer.WriteValueSafe(new Ask { Act = (byte)act, Index = (byte)index, Aim = aim });
        network.CustomMessagingManager.SendNamedMessage(AskMessage, NetworkManager.ServerClientId, writer);
    }

    /// <summary>On a guest: the host's word on every can.</summary>
    public void ReceiveState(FastBufferReader reader)
    {
        reader.ReadValueSafe(out byte count);
        for (int i = 0; i < count; i++)
        {
            reader.ReadValueSafe(out Can can);
            if (i < state.Length) state[i] = can;
        }
    }

    /// <summary>
    /// Late each frame: every can where it is, its blast and fire shown once, and this PC's own player's hands full or
    /// free. <paramref name="simulating"/>: this PC runs the cans' bodies (the host, or the PC alone); a guest's copies
    /// follow the host's word.
    /// </summary>
    public void Place(PlayerMovement mine, Transform view, bool simulating = true)
    {
        for (int i = 0; i < cans.Length; i++)
        {
            var can = cans[i];
            if (can == null) continue;
            var mode = (Mode)state[i].Mode;
            ShowFire(i, mode, mine);
            bool hidden = mode == Mode.Poured || mode == Mode.Burning || mode == Mode.Gone;
            if (can.gameObject.activeSelf == hidden) can.gameObject.SetActive(!hidden);
            if (targets[i] != null) targets[i].Shootable = mode == Mode.Lying || mode == Mode.Flying;
            if (hidden) continue;
            bool loose = mode == Mode.Lying || mode == Mode.Flying;
            Simulate(i, simulating && loose);
            if (loose)
            {
                // The host's physics has it where it is; a guest's copy follows the host's word.
                if (!simulating) Follow(i);
                continue;
            }
            var carrier = PlayerRoster.ById(state[i].Carrier);
            if (carrier == null) continue;
            if (carrier == mine && view != null)
            {
                can.SetPositionAndRotation(view.TransformPoint(InView), Quaternion.Euler(0f, view.eulerAngles.y, 0f));
                continue;
            }
            var hand = carrier.TryGetComponent(out PlayerBody body) && body.BodyAnimator != null && body.BodyAnimator.isHuman
                ? body.BodyAnimator.GetBoneTransform(HumanBodyBones.RightHand) : null;
            Vector3 at = hand != null ? hand.position + Vector3.down * .22f : carrier.transform.position + Vector3.up * .8f + carrier.transform.right * .25f;
            can.SetPositionAndRotation(at, Quaternion.Euler(0f, carrier.transform.eulerAngles.y, 0f));
        }
        if (mine != null && mine.TryGetComponent(out PlayerWeaponSlots slots)) slots.HoldHandsFull(CarriedBy(mine) >= 0);
    }

    // A guest's copy of a loose can eases toward the host's word, and jumps there when it is far (a wipe, a restart).
    private void Follow(int index)
    {
        var body = cans[index];
        Vector3 to = state[index].Position;
        var turn = Turn(state[index].Rotation);
        if ((body.position - to).sqrMagnitude > FollowJump * FollowJump) { body.SetPositionAndRotation(to, turn); return; }
        float share = 1f - Mathf.Pow(1f - FollowRate, Time.deltaTime * 60f);
        body.SetPositionAndRotation(Vector3.Lerp(body.position, to, share), Quaternion.Slerp(body.rotation, turn, share));
    }

    // A can that just caught fire blows up here (seen and heard on every PC), and its fire burns until it goes out.
    private void ShowFire(int index, Mode mode, PlayerMovement mine)
    {
        var was = (Mode)shown[index];
        shown[index] = (byte)mode;
        Vector3 at = state[index].Position;
        if (mode == Mode.Burning && was != Mode.Burning)
        {
            if (fires[index] != null) fires[index].PutOut();
            fires[index] = CanBlastEffect.Play(at, BlastRadius, FireRadius);
            if (blastSound != null) GameAudio.PlayAt(blastSound, at + Vector3.up, AudioChannel.Combat, 1f, 4f, 90f);
            if (mine != null)
            {
                float near = 1f - Mathf.Clamp01(Vector3.Distance(mine.transform.position, at) / ShakeReach);
                var shake = near > 0f ? mine.GetComponentInChildren<PlayerCameraEffects>() : null;
                if (shake != null) shake.ApplyCameraImpulse(new Vector3(-9f * near, 0f, (Random.value < .5f ? -5f : 5f) * near), 5f * near);
            }
        }
        if (mode != Mode.Burning && fires[index] != null) { fires[index].PutOut(); fires[index] = null; }
    }

    // What a can is set down on: everything but enemies and, unless -1, the players' own layer (31).
    private static int SolidMask(int playerLayer)
    {
        int mask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Enemy") & ~(1 << 31);
        if (playerLayer >= 0) mask &= ~(1 << playerLayer);
        return mask;
    }

    // Down onto whatever is under it, the carrier's own layer (players are on 31) and enemies aside.
    private static Vector3 Ground(Vector3 at, int playerLayer)
        => Physics.Raycast(at + Vector3.up * .5f, Vector3.down, out RaycastHit hit, GroundSearch, SolidMask(playerLayer), QueryTriggerInteraction.Ignore)
            ? hit.point : at;
}
