using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Stage 5C: the fuel cans a Collect step wants (chapter 3: four into the depot's generator), after L4D2's scavenge.
/// [F] picks one up; carried, it fills both hands, so no weapon is out until it is put down ([F] again) or poured ([F] at
/// the step's spot, PlayerWeaponSlots.HoldHandsFull). A carrier who goes down, dies or leaves drops it where they are.
/// The host, or the PC alone, keeps every can's state and tells the others (a named message on change and every couple
/// of seconds); a guest asks the host to pick up, put down or pour, and sees the answer a round trip later. A wipe puts
/// every can back where it started. Each PC hangs a carried can from its carrier: in front of this PC's own camera, from
/// the right hand on everyone else. CampaignRun owns it: input, the step, the messages' registration.
/// </summary>
internal sealed class CampaignCans
{
    public const string StateMessage = "ENFILADE.Cans", AskMessage = "ENFILADE.CanAsk";
    public enum Act : byte { Pick, Drop, Pour }
    private enum Mode : byte { Lying, Carried, Poured }

    // How near a can must be to pick it up, and a little more for a guest whose copy lags (m).
    public const float Reach = 1.8f;
    private const float ReachSlack = .8f, ShareEvery = 2f, DropAhead = .6f, GroundSearch = 4f;
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
    }

    private readonly Transform[] cans;
    private readonly Can[] state, start;
    private bool changed = true;
    private float nextShare;

    public CampaignCans(Transform[] cans)
    {
        this.cans = cans ?? new Transform[0];
        state = new Can[this.cans.Length];
        start = new Can[this.cans.Length];
        for (int i = 0; i < this.cans.Length; i++)
            start[i] = state[i] = new Can { Mode = (byte)Mode.Lying, Position = this.cans[i] != null ? this.cans[i].position : Vector3.zero };
    }

    public bool Any => cans.Length > 0;

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
    /// fuel goes while the step wants it.</summary>
    public bool Apply(Act act, int index, PlayerMovement who, bool mayPour)
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
        }
        state[index] = can;
        changed = true;
        return true;
    }

    /// <summary>On the host, each frame: a can whose carrier went down, died or left falls where they were.</summary>
    public void Tick()
    {
        for (int i = 0; i < state.Length; i++)
        {
            if (state[i].Mode != (byte)Mode.Carried) continue;
            var carrier = CarrierOf(state[i].Carrier);
            if (carrier != null && !carrier.IsDead && !carrier.DamageReceiver.IsDowned) continue;
            state[i].Mode = (byte)Mode.Lying;
            state[i].Position = carrier != null ? Ground(carrier.transform.position, carrier.gameObject.layer)
                : cans[i] != null ? Ground(cans[i].position, -1) : state[i].Position;
            changed = true;
        }
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
        if (who != null) Apply((Act)ask.Act, ask.Index, who, mayPour(who));
    }

    // ---- Every PC ----

    /// <summary>On a guest: asks the host.</summary>
    public static void Send(NetworkManager network, Act act, int index)
    {
        if (network == null || !network.IsListening || network.CustomMessagingManager == null) return;
        using var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize<Ask>(), Allocator.Temp);
        writer.WriteValueSafe(new Ask { Act = (byte)act, Index = (byte)index });
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

    /// <summary>Late each frame: every can where it is, and this PC's own player's hands full or free.</summary>
    public void Place(PlayerMovement mine, Transform view)
    {
        for (int i = 0; i < cans.Length; i++)
        {
            var can = cans[i];
            if (can == null) continue;
            bool poured = state[i].Mode == (byte)Mode.Poured;
            if (can.gameObject.activeSelf == poured) can.gameObject.SetActive(!poured);
            if (poured) continue;
            if (state[i].Mode == (byte)Mode.Lying) { can.SetPositionAndRotation(state[i].Position, Quaternion.identity); continue; }
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

    // A player's id in the cans' state: their client in a room, 0 alone.
    private static ulong IdOf(PlayerMovement player)
        => player.TryGetComponent(out NetworkObject network) && network.IsSpawned ? network.OwnerClientId : 0UL;

    private static PlayerMovement CarrierOf(ulong id)
    {
        foreach (var someone in PlayerRoster.All)
            if (someone != null && (someone.IsLocal || someone.IsNetworkDriven) && IdOf(someone) == id) return someone;
        return null;
    }

    // Down onto whatever is under it, the carrier's own layer (players are on 31) and enemies aside; -1 leaves none out.
    private static Vector3 Ground(Vector3 at, int playerLayer)
    {
        int mask = Physics.DefaultRaycastLayers & ~LayerMask.GetMask("Enemy");
        if (playerLayer >= 0) mask &= ~(1 << playerLayer);
        return Physics.Raycast(at + Vector3.up * .5f, Vector3.down, out RaycastHit hit, GroundSearch, mask, QueryTriggerInteraction.Ignore)
            ? hit.point : at;
    }
}
