using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Stage 5C: the fuel cans a Collect step wants (chapter 3: four into the depot's generator), after L4D2's scavenge.
/// [F] picks one up; carried, it fills both hands, so no weapon is out until it is put down ([F] again) or poured ([F] at
/// the step's spot, PlayerWeaponSlots.HoldHandsFull). A carrier who goes down, dies or leaves drops it where they are.
/// Friend feedback C17: carried, the left button throws it (an arc under gravity, landing where it hits); shot where it
/// lies or flies, it blows up (zombies near it are hurt and knocked flat, the brute only hurt) and burns a while,
/// hurting zombies that stand in the fire, then turns up again where it first lay, so the step can still be done.
/// The blast and the fire are drawn by CanBlastEffect, in the game's own flat style.
/// The host, or the PC alone, keeps every can's state and tells the others (a named message on change, every frame while
/// one flies, and every couple of seconds); a guest asks the host to pick up, put down, pour or throw, and a shot from a
/// guest goes to the host (CampaignRun). A wipe puts every can back where it started. Each PC hangs a carried can from its
/// carrier: in front of this PC's own camera, from the right hand on everyone else. CampaignRun owns it: input, the step,
/// the messages' registration. Numbers: the constants below.
/// </summary>
internal sealed class CampaignCans
{
    public const string StateMessage = "ENFILADE.Cans", AskMessage = "ENFILADE.CanAsk";
    public enum Act : byte { Pick, Drop, Pour, Throw }
    private enum Mode : byte { Lying, Carried, Poured, Flying, Burning, Gone }

    // How near a can must be to pick it up, and a little more for a guest whose copy lags (m).
    public const float Reach = 1.8f;
    private const float ReachSlack = .8f, ShareEvery = 2f, DropAhead = .6f, GroundSearch = 4f;
    // A throw: how fast it leaves the hands along the aim and up (m/s), the game's gravity, the longest flight (s).
    private const float ThrowSpeed = 11f, ThrowLift = 2.5f, Gravity = 20f, LongestFlight = 4f, CanRadius = .18f;
    // What a flying can lands on: a face leaning no more than this from level (its normal's height). Anything steeper it
    // knocks against, keeping this share of its speed.
    private const float FloorNormal = .5f, Bounce = .3f;
    // The blast: how far it reaches (m), its damage at the middle and at the edge, how long it knocks zombies down (s).
    private const float BlastRadius = 5f, BlastDamage = 180f, BlastEdgeDamage = 60f, BlastKnockdown = 2.2f;
    // The fire after it: how wide (m), damage a second to a zombie in it, how long it burns, how long the can is gone (s).
    private const float FireRadius = 3f, FireDamage = 40f, FireSeconds = 8f, FireEvery = .5f, GoneSeconds = 12f;
    // A view within this of the blast shakes, harder nearer (m).
    private const float ShakeReach = 18f;
    // Where a carried can sits in this PC's own view, from the camera (m: right, up, ahead).
    private static readonly Vector3 InView = new Vector3(.28f, -.42f, .62f);

    private struct Can : INetworkSerializeByMemcpy
    {
        public byte Mode;
        public ulong Carrier;
        public Vector3 Position;
    }

    private struct Ask : INetworkSerializeByMemcpy
    {
        public byte Act, Index;
        public Vector3 Aim;
    }

    private readonly Transform[] cans;
    private readonly CampaignCanTarget[] targets;
    private readonly Can[] state, start;
    // The host's own bookkeeping: a flying can's velocity, the time left burning or gone, and who set it off.
    private readonly Vector3[] velocity;
    private readonly float[] timeLeft, flown, nextBurn;
    private readonly ulong[] setOffBy;
    // What each PC last showed of each can, so a can catching fire shows its blast once.
    private readonly byte[] shown;
    private readonly CanBlastEffect[] fires;
    private readonly HashSet<EnemyHealth> struck = new HashSet<EnemyHealth>();
    private AudioClip blastSound;
    private bool changed = true;
    private float nextShare;

    public CampaignCans(Transform[] cans)
    {
        this.cans = cans ?? new Transform[0];
        int count = this.cans.Length;
        state = new Can[count];
        start = new Can[count];
        targets = new CampaignCanTarget[count];
        velocity = new Vector3[count];
        timeLeft = new float[count];
        flown = new float[count];
        nextBurn = new float[count];
        setOffBy = new ulong[count];
        shown = new byte[count];
        fires = new CanBlastEffect[count];
        for (int i = 0; i < count; i++)
        {
            start[i] = state[i] = new Can { Mode = (byte)Mode.Lying, Position = this.cans[i] != null ? this.cans[i].position : Vector3.zero };
            if (this.cans[i] == null) continue;
            targets[i] = this.cans[i].gameObject.AddComponent<CampaignCanTarget>();
            targets[i].Index = i;
        }
    }

    public bool Any => cans.Length > 0;

    /// <summary>What a blast sounds like (CampaignRun's, set by the builder); none is fine.</summary>
    public void Dress(AudioClip sound) => blastSound = sound;

    public int Poured
    {
        get
        {
            int poured = 0;
            foreach (var can in state) if (can.Mode == (byte)Mode.Poured) poured++;
            return poured;
        }
    }

    /// <summary>The can this player carries, or -1.</summary>
    public int CarriedBy(PlayerMovement player)
    {
        if (player == null) return -1;
        ulong id = IdOf(player);
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
                can.Carrier = IdOf(who);
                break;
            case Act.Drop:
                if (can.Mode != (byte)Mode.Carried || can.Carrier != IdOf(who)) return false;
                can.Mode = (byte)Mode.Lying;
                can.Position = Ground(who.transform.position + who.transform.forward * DropAhead, who.gameObject.layer);
                break;
            case Act.Pour:
                if (can.Mode != (byte)Mode.Carried || can.Carrier != IdOf(who) || !mayPour) return false;
                can.Mode = (byte)Mode.Poured;
                break;
            case Act.Throw:
                if (can.Mode != (byte)Mode.Carried || can.Carrier != IdOf(who)) return false;
                if (aim.sqrMagnitude < 1e-4f) aim = who.transform.forward;
                aim.Normalize();
                can.Mode = (byte)Mode.Flying;
                can.Position = who.transform.position + Vector3.up * 1.4f + aim * .6f;
                velocity[index] = aim * ThrowSpeed + Vector3.up * ThrowLift;
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
        if (can.Mode == (byte)Mode.Flying) can.Position = Ground(can.Position, -1);
        can.Mode = (byte)Mode.Burning;
        state[index] = can;
        timeLeft[index] = FireSeconds;
        nextBurn[index] = FireEvery;
        setOffBy[index] = shooter != null ? IdOf(shooter) : 0UL;
        Blast(can.Position, shooter);
        changed = true;
    }

    /// <summary>
    /// On the host, each frame: a can whose carrier went down, died or left falls where they were; thrown cans fly; fires
    /// burn and go out; a can gone that long is back where it first lay.
    /// </summary>
    public void Tick(float dt)
    {
        for (int i = 0; i < state.Length; i++)
        {
            switch ((Mode)state[i].Mode)
            {
                case Mode.Carried:
                    var carrier = CarrierOf(state[i].Carrier);
                    if (carrier != null && !carrier.IsDead && !carrier.DamageReceiver.IsDowned) break;
                    state[i].Mode = (byte)Mode.Lying;
                    state[i].Position = carrier != null ? Ground(carrier.transform.position, carrier.gameObject.layer)
                        : cans[i] != null ? Ground(cans[i].position, -1) : state[i].Position;
                    changed = true;
                    break;
                case Mode.Flying:
                    Fly(i, dt);
                    break;
                case Mode.Burning:
                    if ((nextBurn[i] -= dt) <= 0f) { nextBurn[i] += FireEvery; Burn(state[i].Position, CarrierOf(setOffBy[i])); }
                    if ((timeLeft[i] -= dt) > 0f) break;
                    state[i].Mode = (byte)Mode.Gone;
                    timeLeft[i] = GoneSeconds;
                    changed = true;
                    break;
                case Mode.Gone:
                    if ((timeLeft[i] -= dt) > 0f) break;
                    state[i] = start[i];
                    changed = true;
                    break;
            }
        }
    }

    // Along its arc (players and zombies aside). It lands on a floor; a wall or a ceiling it only knocks against, loses
    // most of its speed and drops from. It used to lie wherever it first touched anything, so a can thrown at a ceiling
    // stayed stuck up there (user, 2026-11-10). After LongestFlight it comes down wherever it is.
    private void Fly(int index, float dt)
    {
        var can = state[index];
        Vector3 from = can.Position;
        velocity[index] += Vector3.down * Gravity * dt;
        Vector3 step = velocity[index] * dt;
        flown[index] += dt;
        RaycastHit hit = default;
        bool met = step.sqrMagnitude > 1e-8f
            && Physics.SphereCast(from, CanRadius, step.normalized, out hit, step.magnitude, SolidMask(-1), QueryTriggerInteraction.Ignore);
        if (from.y < -100f) { can.Mode = (byte)Mode.Lying; can.Position = start[index].Position; }
        else if (met && hit.normal.y >= FloorNormal) { can.Mode = (byte)Mode.Lying; can.Position = Ground(hit.point + hit.normal * .05f, -1); }
        else if (flown[index] >= LongestFlight) { can.Mode = (byte)Mode.Lying; can.Position = Settle(from); }
        else if (met)
        {
            // Up to where it touches and a little clear of it, then off it: nothing of its speed carries it further in,
            // and off a ceiling nothing carries it up.
            can.Position = from + step.normalized * hit.distance + hit.normal * .02f;
            Vector3 off = Vector3.Reflect(velocity[index], hit.normal) * Bounce;
            if (hit.normal.y < 0f && off.y > 0f) off.y = 0f;
            velocity[index] = off;
        }
        else can.Position = from + step;
        state[index] = can;
        changed = true;
    }

    // Where a can still in the air at the end of its flight comes down: the first floor under it, however far below.
    private static Vector3 Settle(Vector3 at)
        => Physics.Raycast(at, Vector3.down, out RaycastHit floor, 200f, SolidMask(-1), QueryTriggerInteraction.Ignore) ? floor.point : at;

    // The blast: zombies within reach are hurt by how near they are, and knocked flat (a brute shrugs that off).
    private void Blast(Vector3 at, PlayerMovement by)
    {
        foreach (var enemy in EnemiesNear(at, BlastRadius))
        {
            Vector3 away = Vector3.ProjectOnPlane(enemy.transform.position - at, Vector3.up);
            float near = 1f - Mathf.Clamp01(away.magnitude / BlastRadius);
            away = away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.forward;
            Hurt(enemy, Mathf.Lerp(BlastEdgeDamage, BlastDamage, near), at, away, by);
            if (enemy != null && enemy.IsAlive && enemy.TryGetComponent(out MeleeCombatTarget melee)) melee.KnockDown(away, BlastKnockdown);
        }
    }

    // The fire: every zombie standing in it burns.
    private void Burn(Vector3 at, PlayerMovement by)
    {
        foreach (var enemy in EnemiesNear(at, FireRadius))
            Hurt(enemy, FireDamage * FireEvery, at, Vector3.ProjectOnPlane(enemy.transform.position - at, Vector3.up), by);
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

    // Credited to whoever set it off, as their own kill.
    private static void Hurt(EnemyHealth enemy, float amount, Vector3 at, Vector3 away, PlayerMovement by)
    {
        if (enemy == null || !enemy.IsAlive || amount <= 0f) return;
        enemy.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), amount, DamageKind.Attack, enemy.transform.position + Vector3.up,
            away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.forward, by != null ? by.gameObject : null, false, 0f, true, origin: at));
    }

    /// <summary>On the host: every can back where it started (a wipe).</summary>
    public void Reset()
    {
        System.Array.Copy(start, state, state.Length);
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
        var who = CarrierOf(sender);
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

    /// <summary>Late each frame: every can where it is, its blast and fire shown once, and this PC's own player's hands full or free.</summary>
    public void Place(PlayerMovement mine, Transform view)
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
            if (mode == Mode.Lying || mode == Mode.Flying)
            {
                // Flying, it tumbles end over end.
                var turn = mode == Mode.Flying ? Quaternion.Euler(Time.time * 540f, Time.time * 130f, 0f) : Quaternion.identity;
                can.SetPositionAndRotation(state[i].Position, turn);
                continue;
            }
            var carrier = CarrierOf(state[i].Carrier);
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

    // A player's id in the cans' state: their client in a room, 0 alone.
    private static ulong IdOf(PlayerMovement player)
        => player.TryGetComponent(out NetworkObject network) && network.IsSpawned ? network.OwnerClientId : 0UL;

    private static PlayerMovement CarrierOf(ulong id)
    {
        foreach (var someone in PlayerRoster.All)
            if (someone != null && (someone.IsLocal || someone.IsNetworkDriven) && IdOf(someone) == id) return someone;
        return null;
    }

    // What a can lands on and comes down onto: everything but enemies and, unless -1, the players' own layer (31).
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
