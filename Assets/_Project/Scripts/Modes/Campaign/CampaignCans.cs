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
/// A can is Impact CFX's red jerry can (PropShelf.FuelCan; the user, 2026-11-21: "3챕에 기름통도 저 impact cfx에 있는
/// 기름통으로 바꿔 줘 물리랑 해서"), a real physics body of CanMass: thrown it tumbles, bounces and rolls, clatters as the
/// pack made it, is knocked about by kicks, shoves and shots, and a can that strikes a zombie hard is the thrower's blow
/// (PhysicsProp, PropStrike). The map's marker for each can (a plain box) only says where it first stands. A kill by the
/// can's fire is KillMarks.Fire (the flame under the crosshair).
/// Two owners, each of one thing (0.9.256; the user: "what I see is what others see"): this keeps the run's word on each
/// can, the host's, as every PC's mode of it (lying, carried by whom, poured, burning, gone) shared by a named message on
/// change and every couple of seconds; a guest asks the host to pick up, put down, pour or throw, and a guest's shot goes
/// to the host (CampaignRun). Where a can is and how it moves is every loose prop's (PropNet): the PC of whoever touches it
/// last runs it (the thrower's, the kicker's, the host's for one at rest or set back), so a guest's throw and kick move it
/// for everyone, its strike is judged on the thrower's PC, and it clatters on every PC from its own collisions. The host
/// hands a picked can to its carrier by PropNet's word, and the carrier's own PC lets it go (CanAct → LetGo). What the host
/// judges for a guest (their can's blast or fire) carries KillMarks.JudgedByHost, so their PC still shows the kill. A wipe
/// puts every can back where it started. Each PC hangs a carried can as every carried thing hangs (CarryHold), and a throw
/// leaves the hands as every thrown thing does. CampaignRun owns it: input, the step, the messages' registration.
/// Numbers: the constants below.
/// </summary>
internal sealed class CampaignCans
{
    public const string StateMessage = "ENFILADE.Cans", AskMessage = "ENFILADE.CanAsk";
    public enum Act : byte { Pick, Drop, Pour, Throw }
    private enum Mode : byte { Lying, Carried, Poured, Flying, Burning, Gone }

    // How near a can must be to pick it up, and a little more for a guest whose copy lags (m).
    public const float Reach = 1.8f;
    private const float ReachSlack = .8f, ShareEvery = 2f, DropAhead = .6f, GroundSearch = 4f;
    // The longest a thrown can counts as flying, whatever it does (s). How it leaves the hands is every carried thing's
    // (CarryHold: the arms' push over its mass, about 11 m/s for a can).
    private const float LongestFlight = 6f;
    // A thrown can lies again (to be picked up) once slower than this (m/s), and not before this long after the throw (s).
    private const float RestSpeed = .6f, RestAfter = .25f;
    // A 20 L jerry can about half full (kg): heavy enough to floor the zombie it is thrown at (PropStrike), too heavy to fly
    // off at a bullet's push. The pack's demo can weighs 1 kg, an empty one.
    private const float CanMass = 8f;
    // After this PC's own player lets go of a can (CanAct), the run's word may still say they carry it for this long (s):
    // their PC does not take it back into their hands meanwhile.
    private const float LetGoGrace = 1.5f;
    // The blast: how far it reaches (m), its damage at the middle and at the edge, how long it knocks zombies down (s).
    private const float BlastRadius = 5f, BlastDamage = 180f, BlastEdgeDamage = 60f, BlastKnockdown = 2.2f;
    // The fire after it: how wide (m), damage a second to a zombie in it, how long it burns, how long the can is gone (s).
    private const float FireRadius = 3f, FireDamage = 40f, FireSeconds = 8f, FireEvery = .5f, GoneSeconds = 12f;
    // A can poured on a burn hold's pile is back at the station this long after (s).
    private const float PouredBack = 12f;
    // A view within this of the blast shakes, harder nearer (m).
    private const float ShakeReach = 18f;

    private struct Can : INetworkSerializeByMemcpy
    {
        public byte Mode;
        public ulong Carrier;
        // Burning: the fire's place on the floor. Otherwise where the run last knew its body (PropNet has it exactly).
        public Vector3 Position;
        public Quaternion Rotation;
    }

    private struct Ask : INetworkSerializeByMemcpy
    {
        public byte Act, Index;
        public Vector3 Aim;
    }

    private readonly Transform[] cans;
    private readonly PhysicsProp[] props;
    // Each body's own solid shape, set aside while a can is set down so the floor is found under it, not itself.
    private readonly List<Collider>[] solids;
    // From each body's pivot down to the bottom of its shape (m), so it is set down standing on the floor.
    private readonly float[] lift;
    private readonly CampaignCanTarget[] targets;
    private readonly Can[] state, start;
    // The host's own bookkeeping: how long a can has been flying, the time left burning or gone, and who set it off.
    private readonly float[] timeLeft, flown, nextBurn;
    private readonly ulong[] setOffBy;
    // When this PC's own player last let go of each can (LetGo).
    private readonly float[] letGo;
    // What each PC last showed of each can, so a can catching fire shows its blast once.
    private readonly byte[] shown;
    private readonly CanBlastEffect[] fires;
    private readonly HashSet<EnemyHealth> struck = new HashSet<EnemyHealth>();
    private readonly RaycastHit[] under = new RaycastHit[8];
    private AudioClip blastSound;
    private bool changed = true;
    private float nextShare;

    public CampaignCans(Transform[] marks)
    {
        marks ??= new Transform[0];
        int count = marks.Length;
        cans = new Transform[count];
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
        letGo = new float[count];
        shown = new byte[count];
        fires = new CanBlastEffect[count];
        var shelf = count > 0 ? PropShelf.Get() : null;
        var fuelCan = shelf != null ? shelf.FuelCan : null;
        if (count > 0) CollisionEffects.Ensure();
        for (int i = 0; i < count; i++)
        {
            letGo[i] = -99f;
            var mark = marks[i];
            if (mark == null) continue;
            cans[i] = Dress(mark, fuelCan, i);
            var turn = Quaternion.Euler(0f, mark.eulerAngles.y, 0f);
            start[i] = state[i] = new Can { Mode = (byte)Mode.Lying, Position = RestOn(i, mark.position, -1), Rotation = turn };
            props[i].Teleport(state[i].Position, turn, Vector3.zero, Vector3.zero);
            targets[i] = cans[i].gameObject.AddComponent<CampaignCanTarget>();
            targets[i].Index = i;
        }
    }

    // A can's body: the shelf's fuel can where the map's marker stands (the marker, a plain box, is hidden), or the marker
    // itself made solid when there is none. Either way a loose prop of CanMass, named the same on every PC (PropNet) and
    // carried only the run's way (PhysicsProp.OwnCarry).
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
        var own = new List<Collider>();
        foreach (var collider in body.GetComponentsInChildren<Collider>(true)) if (!collider.isTrigger) own.Add(collider);
        solids[index] = own;
        if (!body.TryGetComponent(out PhysicsProp prop)) prop = body.gameObject.AddComponent<PhysicsProp>();
        prop.OwnCarry = true;
        PropNet.Name(prop, "fuelcan/" + mark.gameObject.scene.name + "/" + index);
        props[index] = prop;
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
        int count = Physics.RaycastNonAlloc(at + Vector3.up * .5f, Vector3.down, under, GroundSearch, SolidMask(playerLayer), QueryTriggerInteraction.Ignore);
        float nearest = float.MaxValue;
        Vector3 floor = at;
        for (int i = 0; i < count; i++)
        {
            if (under[i].distance >= nearest || own != null && own.Contains(under[i].collider)) continue;
            nearest = under[i].distance;
            floor = under[i].point;
        }
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
            SetDown(index, start[index].Position, start[index].Rotation, Vector3.zero, Vector3.zero);
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

    /// <summary>The nearest can lying within reach of <paramref name="at"/>, or -1: where its body is on this PC.</summary>
    public int NearestLying(Vector3 at, float reach = Reach)
    {
        int nearest = -1;
        float best = reach * reach;
        for (int i = 0; i < state.Length; i++)
        {
            if (state[i].Mode != (byte)Mode.Lying || cans[i] == null) continue;
            float distance = (cans[i].position - at).sqrMagnitude;
            if (distance <= best) { best = distance; nearest = i; }
        }
        return nearest;
    }

    // ---- The acting player's own PC ----

    /// <summary>
    /// This PC's own player puts down or throws the can they carry (CampaignRun.CanAct, before the run hears of it): their
    /// PC sets it loose now and runs its flight for everyone (PropNet), so the throw leaves the hands without waiting on
    /// the host, and its strike is theirs to judge. The run's word follows (Apply).
    /// </summary>
    public void LetGo(Act act, int index, PlayerMovement who, Vector3 aim)
    {
        if (who == null || index < 0 || index >= cans.Length || props[index] == null || CarriedBy(who) != index) return;
        letGo[index] = Time.time;
        if (act == Act.Throw)
        {
            if (aim.sqrMagnitude < 1e-4f) aim = who.transform.forward;
            aim.Normalize();
            Vector3 level = Vector3.ProjectOnPlane(aim, Vector3.up);
            var turn = Quaternion.LookRotation(level.sqrMagnitude > 1e-4f ? level : who.transform.forward);
            PropNet.Release(props[index], CarryHold.ThrowFrom(who, aim), turn, CarryHold.ThrowVelocity(CanMass, aim),
                Random.onUnitSphere * CarryHold.ThrowSpin);
            props[index].SetAttacker(who.gameObject);
            return;
        }
        SetDown(index, RestOn(index, who.transform.position + who.transform.forward * DropAhead, who.gameObject.layer),
            Quaternion.Euler(0f, who.transform.eulerAngles.y, 0f), Vector3.zero, Vector3.zero);
    }

    // Its body put loose somewhere by this PC, which runs it from there (PropNet).
    private void SetDown(int index, Vector3 at, Quaternion turn, Vector3 velocity, Vector3 spin)
    {
        if (props[index] != null) PropNet.Release(props[index], at, turn, velocity, spin);
    }

    // ---- The host's side ----

    /// <summary>On the host: does it, if it may. <paramref name="mayPour"/> says whether this player stands where the
    /// fuel goes while the step wants it. A put-down or a throw is already in the carrier's hands' doing (LetGo): this is
    /// the run's word on it.</summary>
    public bool Apply(Act act, int index, PlayerMovement who, bool mayPour, Vector3 aim = default)
    {
        if (index < 0 || index >= state.Length || who == null || who.IsDead || who.DamageReceiver.IsDowned) return false;
        var can = state[index];
        switch (act)
        {
            case Act.Pick:
                if (can.Mode != (byte)Mode.Lying || CarriedBy(who) >= 0 || cans[index] == null
                    || (cans[index].position - who.transform.position).sqrMagnitude > (Reach + ReachSlack) * (Reach + ReachSlack))
                    return false;
                can.Mode = (byte)Mode.Carried;
                can.Carrier = PlayerRoster.IdOf(who);
                // In their hands on every PC, by the word on its motion.
                if (props[index] != null) PropNet.Hold(props[index], who);
                break;
            case Act.Drop:
                if (can.Mode != (byte)Mode.Carried || can.Carrier != PlayerRoster.IdOf(who)) return false;
                can.Mode = (byte)Mode.Lying;
                break;
            case Act.Pour:
                if (can.Mode != (byte)Mode.Carried || can.Carrier != PlayerRoster.IdOf(who) || !mayPour) return false;
                can.Mode = (byte)Mode.Poured;
                PourCount++;
                timeLeft[index] = PouredBack;
                break;
            case Act.Throw:
                if (can.Mode != (byte)Mode.Carried || can.Carrier != PlayerRoster.IdOf(who)) return false;
                can.Mode = (byte)Mode.Flying;
                flown[index] = 0f;
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
    /// On the host, each frame: a can whose carrier went down, died or left falls where they were; a thrown can lies again
    /// once it has all but stopped; fires burn and go out; a can gone that long is back where it first lay.
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
                    SetDown(i, state[i].Position, state[i].Rotation, Vector3.zero, Vector3.zero);
                    changed = true;
                    break;
                case Mode.Flying:
                    Land(i, dt);
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

    // Back where it first lay, at rest: the host sets it there for everyone.
    private void Restart(int index)
    {
        state[index] = start[index];
        SetDown(index, start[index].Position, start[index].Rotation, Vector3.zero, Vector3.zero);
        changed = true;
    }

    // A thrown can lies again (to be picked up) once it has all but stopped, wherever its body is (PropNet has every PC's
    // copy where the thrower's PC runs it); one that falls out of the world is back where it first lay.
    private void Land(int index, float dt)
    {
        var body = cans[index];
        if (body == null || !body.gameObject.activeInHierarchy) return;
        if (body.position.y < -100f) { Restart(index); return; }
        flown[index] += dt;
        var rb = props[index] != null ? props[index].Body : null;
        float speed = rb != null && !rb.isKinematic ? rb.linearVelocity.magnitude : 0f;
        if (flown[index] < LongestFlight && (flown[index] < RestAfter || speed >= RestSpeed)) return;
        state[index].Mode = (byte)Mode.Lying;
        state[index].Position = body.position;
        state[index].Rotation = body.rotation;
        changed = true;
    }

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
    /// Late each frame: a can shown or hidden as the run's word says, its blast and fire shown once, the one in this PC's
    /// own player's hands before their camera, and their hands full or free. Where a loose can is, and a carried one on
    /// the others' screens, is PropNet's.
    /// </summary>
    public void Place(PlayerMovement mine, Transform view)
    {
        for (int i = 0; i < cans.Length; i++)
        {
            var can = cans[i];
            if (can == null) continue;
            var mode = (Mode)state[i].Mode;
            ShowFire(i, mode, mine);
            bool hidden = mode == Mode.Poured || mode == Mode.Burning || mode == Mode.Gone;
            if (can.gameObject.activeSelf == hidden)
            {
                // Hidden, nobody runs its motion; set back, the host's word places it again (Restart).
                if (hidden && props[i] != null) { props[i].Driving = false; props[i].Following = false; }
                can.gameObject.SetActive(!hidden);
            }
            if (targets[i] != null) targets[i].Shootable = mode == Mode.Lying || mode == Mode.Flying;
            if (hidden || mode != Mode.Carried) continue;
            // In this PC's own player's hands (by PropNet's word), and not just let go of: before their camera.
            if (mine != null && props[i] != null && props[i].Holder == mine && Time.time - letGo[i] > LetGoGrace)
                CarryHold.Place(can, mine, mine, view);
        }
        if (mine != null && mine.TryGetComponent(out PlayerWeaponSlots slots))
        {
            int carried = CarriedBy(mine);
            slots.HoldHandsFull(this, carried >= 0 && Time.time - letGo[carried] > LetGoGrace);
        }
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
}
