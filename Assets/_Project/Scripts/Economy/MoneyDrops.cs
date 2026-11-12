using System.Collections.Generic;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Stage 6A: dropping money for friends, after GMod Horde's drop money. In a room, G drops a bundle of dropAmount
/// (Tables/Money) on the floor in front of this PC's player, out of the Wallet at once; whoever walks over it first takes
/// it (the dropper too, after a moment, so a drop isn't taken straight back). The host keeps the bundles: a drop goes to
/// the host, which places it and tells everyone; a touch asks the host, which gives it to whoever asked first and tells
/// everyone, and the taker's PC puts it in their wallet. The kill feed says who dropped and who took how much. Bundles go
/// with the map. Alone there is nobody to give to. Lives on this PC's own player.
/// </summary>
[DisallowMultipleComponent]
public sealed class MoneyDrops : MonoBehaviour
{
    private const string DropMessage = "ENFILADE.MoneyDrop", BundleMessage = "ENFILADE.MoneyBundle", ClaimMessage = "ENFILADE.MoneyClaim";
    // How far ahead it lands (m), how long its dropper waits to take it back, how often an unanswered touch asks again (s).
    private const float DropAhead = .9f, OwnPickupDelay = 2f, AskAgain = .5f, SpinSpeed = 120f, Bob = .05f;
    private const long MostAtOnce = 10_000_000_000L;

    private struct Drop : INetworkSerializeByMemcpy
    {
        public long Amount;
        public Vector3 At;
    }

    private struct Bundle : INetworkSerializeByMemcpy
    {
        public ushort Id;
        public byte Taken;
        public long Amount;
        public Vector3 At;
        public ulong Who;
    }

    private struct Claim : INetworkSerializeByMemcpy
    {
        public ushort Id;
    }

    private sealed class Lying
    {
        public long Amount;
        public ulong Dropper;
        public Vector3 At;
        public float Since, Asked = float.NegativeInfinity;
        public Transform Look;
    }

    private readonly Dictionary<ushort, Lying> lying = new Dictionary<ushort, Lying>();
    private readonly List<ushort> touched = new List<ushort>();
    private PlayerMovement player;
    private NetworkManager network;
    private ushort nextId;
    private bool listening;

    private bool IsHost => network != null && network.IsServer;

    private void Awake() => player = GetComponent<PlayerMovement>();

    private void Start()
    {
        network = GameSession.Network;
        if (network == null || network.CustomMessagingManager == null) return;
        listening = true;
        var messages = network.CustomMessagingManager;
        if (network.IsServer)
        {
            messages.RegisterNamedMessageHandler(DropMessage, ReceiveDrop);
            messages.RegisterNamedMessageHandler(ClaimMessage, ReceiveClaim);
        }
        else messages.RegisterNamedMessageHandler(BundleMessage, ReceiveBundle);
    }

    private void OnDestroy()
    {
        foreach (var bundle in lying.Values) if (bundle.Look != null) Destroy(bundle.Look.gameObject);
        if (!listening || network == null || network.CustomMessagingManager == null) return;
        var messages = network.CustomMessagingManager;
        if (network.IsServer)
        {
            messages.UnregisterNamedMessageHandler(DropMessage);
            messages.UnregisterNamedMessageHandler(ClaimMessage);
        }
        else messages.UnregisterNamedMessageHandler(BundleMessage);
    }

    private void Update()
    {
        if (!listening || !network.IsListening || player == null || !player.IsInitialized) return;
        bool able = !player.IsDead && !player.DamageReceiver.IsDowned;
        // Player/DropMoney (G unless the key window says otherwise).
        if (able && TakesInput() && KeyBindings.Pressed("Player/DropMoney")) DropSome();
        if (able) TouchBundles();
        Animate();
    }

    private void DropSome()
    {
        long amount = Wallet.DropAmount;
        if (!Wallet.TryGive(amount))
        {
            CombatFeedHUD.Notice(player, Texts.Format("money.drop.short", Wallet.Format(amount)), false);
            return;
        }
        Vector3 at = Ground(transform.position + transform.forward * DropAhead);
        if (IsHost) { Place(amount, at, network.LocalClientId); return; }
        using var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize<Drop>(), Allocator.Temp);
        writer.WriteValueSafe(new Drop { Amount = amount, At = at });
        network.CustomMessagingManager.SendNamedMessage(DropMessage, NetworkManager.ServerClientId, writer);
    }

    // This PC's player walked over a bundle: ask for it (the host just takes it), again now and then until answered.
    private void TouchBundles()
    {
        float reach = Wallet.PickupReach, now = Time.time;
        touched.Clear();
        foreach (var pair in lying)
        {
            var bundle = pair.Value;
            if ((bundle.At - transform.position).sqrMagnitude > reach * reach || now - bundle.Asked < AskAgain) continue;
            if (bundle.Dropper == network.LocalClientId && now - bundle.Since < OwnPickupDelay) continue;
            bundle.Asked = now;
            touched.Add(pair.Key);
        }
        foreach (ushort id in touched)
        {
            if (IsHost) { Take(id, network.LocalClientId); continue; }
            using var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize<Claim>(), Allocator.Temp);
            writer.WriteValueSafe(new Claim { Id = id });
            network.CustomMessagingManager.SendNamedMessage(ClaimMessage, NetworkManager.ServerClientId, writer);
        }
    }

    // ---- The host's side ----

    private void ReceiveDrop(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out Drop drop);
        if (drop.Amount > 0 && drop.Amount <= MostAtOnce) Place(drop.Amount, drop.At, sender);
    }

    private void ReceiveClaim(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out Claim claim);
        Take(claim.Id, sender);
    }

    private void Place(long amount, Vector3 at, ulong dropper)
    {
        ushort id = nextId++;
        Show(id, amount, at, dropper);
        Tell(new Bundle { Id = id, Amount = amount, At = at, Who = dropper });
    }

    // First come, first served: a bundle already gone stays gone.
    private void Take(ushort id, ulong taker)
    {
        if (!lying.TryGetValue(id, out var bundle)) return;
        Gone(id, bundle.Amount, taker);
        Tell(new Bundle { Id = id, Taken = 1, Amount = bundle.Amount, At = bundle.At, Who = taker });
    }

    private void Tell(in Bundle bundle)
    {
        using var writer = new FastBufferWriter(FastBufferWriter.GetWriteSize<Bundle>(), Allocator.Temp);
        writer.WriteValueSafe(bundle);
        network.CustomMessagingManager.SendNamedMessageToAll(BundleMessage, writer);
    }

    // ---- Every PC ----

    private void ReceiveBundle(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out Bundle bundle);
        if (sender != NetworkManager.ServerClientId) return;
        if (bundle.Taken == 0) Show(bundle.Id, bundle.Amount, bundle.At, bundle.Who);
        else Gone(bundle.Id, bundle.Amount, bundle.Who);
    }

    private void Show(ushort id, long amount, Vector3 at, ulong dropper)
    {
        if (lying.ContainsKey(id)) return;
        // The same stacked notes a money pickup wears (PickupLibrary.money, the Heist pack), resting on the drop's floor.
        var stack = new GameObject("돈 뭉치 " + Wallet.Format(amount)).transform;
        stack.position = at + Vector3.up * .08f;
        PickupProps.Make(FieldPickups.Kind.Money, null, stack);
        lying[id] = new Lying { Amount = amount, Dropper = dropper, At = at, Since = Time.time, Look = stack };
        var who = PlayerOf(dropper);
        if (who != null) CombatFeedHUD.Notice(who, Texts.Format("money.drop.feed", Wallet.Format(amount)), false);
    }

    private void Gone(ushort id, long amount, ulong taker)
    {
        if (lying.TryGetValue(id, out var bundle))
        {
            if (bundle.Look != null) Destroy(bundle.Look.gameObject);
            lying.Remove(id);
        }
        if (taker == network.LocalClientId) Wallet.Receive(amount);
        var who = PlayerOf(taker);
        if (who != null) CombatFeedHUD.Notice(who, Texts.Format("money.pickup.feed", Wallet.Format(amount)), false);
    }

    private void Animate()
    {
        float bob = Mathf.Sin(Time.time * 3f) * Bob;
        foreach (var bundle in lying.Values)
        {
            if (bundle.Look == null) continue;
            bundle.Look.position = bundle.At + Vector3.up * (.12f + bob);
            bundle.Look.rotation = Quaternion.Euler(0f, Time.time * SpinSpeed, 0f);
        }
    }

    private static PlayerMovement PlayerOf(ulong client)
    {
        foreach (var someone in PlayerRoster.All)
            if (someone != null && someone.TryGetComponent(out NetworkObject owner) && owner.IsSpawned && owner.OwnerClientId == client)
                return someone;
        return null;
    }

    // Down onto the floor, players (layer 31) and enemies aside.
    private static Vector3 Ground(Vector3 at)
    {
        int mask = Physics.DefaultRaycastLayers & ~(1 << 31) & ~LayerMask.GetMask("Enemy");
        return Physics.Raycast(at + Vector3.up * .5f, Vector3.down, out RaycastHit hit, 4f, mask, QueryTriggerInteraction.Ignore) ? hit.point : at;
    }

    // As the mouse look: not while the cursor is free (chat, menus), the window is in the background or the console is open.
    private static bool TakesInput() => Cursor.lockState == CursorLockMode.Locked && Application.isFocused && !DevConsole.TakesKeyboard;
}
