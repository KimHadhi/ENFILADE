using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// CampaignRun: what players do on the way, the prompt, doors, shops, cans and buttons. One part of the class; the rest is in CampaignRun.cs and the files beside it.
public sealed partial class CampaignRun
{
    private void Prompt()
    {
        if (screen == null) return;
        var now = Current;
        var mine = PlayerRoster.Local;
        string text = null;
        // A field pickup looked at has [F] first (FieldPickups).
        bool able = mine != null && mine.IsInitialized && !mine.IsDead && !mine.DamageReceiver.IsDowned && !FieldPickups.Targeting;
        bool pressed = able && InputFocus.TakesPlayInput() && interact != null && interact.WasPressedThisFrame();
        int door = able ? NearestDoor(mine) : -1;
        // One [F] at a time: a step's button (this one's, or a later one's with nothing but getting there before it, C4),
        // then a can in hand, then a door, then a can to pick up.
        int button = able && !finished && now != null ? ButtonWithin(mine) : -1;
        if (button >= 0)
        {
            var at = steps[button];
            text = string.IsNullOrEmpty(at.Prompt) ? Texts.Format("campaign.prompt.step", at.Title) : at.Prompt;
            if (pressed) Press(button);
        }
        else if (able && fuel.Any && fuel.CarriedBy(mine) >= 0) text = CanPrompt(mine, pressed);
        else if (door >= 0)
        {
            text = doors[door].Closed ? Texts.Get("campaign.prompt.door.open") : Texts.Get("campaign.prompt.door.close");
            if (pressed) ToggleDoor(door);
        }
        else if (able && NearestShop(mine) is Transform shop)
        {
            text = Texts.Get("campaign.prompt.shop");
            if (pressed) ShopMenu.Open(mine, shop.position);
        }
        else if (able && fuel.Any) text = CanPrompt(mine, pressed);
        offering = text != null;
        screen.Prompt(ShopMenu.IsOpen ? null : text);
    }

    private Transform NearestShop(PlayerMovement mine)
    {
        foreach (var shop in shops)
        {
            if (shop == null) continue;
            Vector3 apart = shop.position - mine.transform.position;
            apart.y = 0f;
            if (apart.sqrMagnitude <= ShopReach * ShopReach) return shop;
        }
        return null;
    }

    private int NearestDoor(PlayerMovement mine)
    {
        int nearest = -1;
        float best = DoorReach * DoorReach;
        for (int i = 0; i < doors.Length; i++)
        {
            if (doors[i] == null) continue;
            Vector3 apart = doors[i].Doorway - mine.transform.position;
            apart.y = 0f;
            if (apart.sqrMagnitude <= best) { best = apart.sqrMagnitude; nearest = i; }
        }
        return nearest;
    }

    private void ToggleDoor(int index)
    {
        if (IsHost) { doors[index].Closed = !doors[index].Closed; return; }
        if (network == null || !network.IsListening) return;
        using var writer = new FastBufferWriter(sizeof(byte), Allocator.Temp);
        writer.WriteValueSafe((byte)index);
        network.CustomMessagingManager.SendNamedMessage(DoorMessage, NetworkManager.ServerClientId, writer);
    }

    // A guest worked a door: the host shuts or opens it, and everyone sees it through the status.
    private void ReceiveDoor(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out byte index);
        if (index < doors.Length && doors[index] != null) doors[index].Closed = !doors[index].Closed;
    }

    // Everyone sits in a rest stop with its doors shut: the one being reached now, or the last one reached.
    private bool Resting() => RestingAt(Current) || (checkpoint >= 0 && checkpoint < steps.Count && RestingAt(steps[checkpoint]));

    private bool RestingAt(CampaignStep at)
        => at != null && at.Kind == CampaignStepKind.Checkpoint && HasDoors(at.Spot) && DoorsShut(at.Spot) && EveryoneAt(at);

    private bool HasDoors(string spot)
    {
        foreach (var door in doors) if (door != null && door.Spot == spot) return true;
        return false;
    }

    // A rest stop with no doors counts as shut.
    private bool DoorsShut(string spot)
    {
        foreach (var door in doors) if (door != null && door.Spot == spot && !door.Closed) return false;
        return true;
    }

    private byte DoorBits()
    {
        int bits = 0;
        for (int i = 0; i < doors.Length && i < 8; i++) if (doors[i] != null && doors[i].Closed) bits |= 1 << i;
        return (byte)bits;
    }

    /// <summary>The `director` command: how the director paces the horde now (host or alone).</summary>
    public static string DirectorReport()
    {
        if (active == null) return "캠페인 맵이 아니에요.";
        if (!active.IsHost) return "디렉터는 방장 PC에서 돌아요. 방장이 입력해 보세요.";
        return active.pacer.Report(active.horde.Alive) + (active.Resting() ? " · 휴게소에서 쉬는 중" : "")
            + (active.quiet ? " · 조용한 구간(좀비가 알아채기 전에는 떼가 오지 않음)" : "");
    }

    // Carrying a can: pour it where the step wants it, else put it down. Hands free: pick up the can within reach.
    private string CanPrompt(PlayerMovement mine, bool pressed)
    {
        int carried = fuel.CarriedBy(mine);
        if (carried >= 0)
        {
            bool pour = MayPour(mine);
            if (pressed) CanAct(pour ? CampaignCans.Act.Pour : CampaignCans.Act.Drop, carried, mine);
            return Texts.Format("campaign.prompt.can.carry", pour ? Texts.Get("campaign.prompt.can.pour") : Texts.Get("campaign.prompt.can.drop"));
        }
        int near = fuel.NearestLying(mine.transform.position);
        if (near < 0) return null;
        if (pressed) CanAct(CampaignCans.Act.Pick, near, mine);
        return Texts.Get("campaign.prompt.can.pick");
    }

    private void CanAct(CampaignCans.Act act, int index, PlayerMovement mine, Vector3 aim = default)
    {
        if (IsHost) fuel.Apply(act, index, mine, MayPour(mine), aim);
        else CampaignCans.Send(network, act, index, aim);
    }

    // C17: carried, the left button throws the can where the view looks.
    private void ThrowCan()
    {
        var mine = PlayerRoster.Local;
        if (mine == null || !mine.IsInitialized || mine.IsDead || mine.DamageReceiver.IsDowned || !InputFocus.TakesPlayInput()) return;
        // The fire key (Combat/Fire, the left button unless the key window says otherwise): the hands hold the can, not a gun.
        if (!KeyBindings.Pressed("Combat/Fire")) return;
        int carried = fuel.CarriedBy(mine);
        if (carried < 0) return;
        var view = mine.TryGetComponent(out PlayerBody body) ? body.View : null;
        CanAct(CampaignCans.Act.Throw, carried, mine, view != null ? view.forward : mine.transform.forward);
    }

    /// <summary>
    /// C17: this PC's player shot fuel can <paramref name="index"/> (CampaignCanTarget): the host sets it off; a guest
    /// tells the host.
    /// </summary>
    public static void ShootCan(int index, PlayerMovement shooter)
    {
        if (active == null || shooter == null || !shooter.IsLocal) return;
        if (active.IsHost) { active.fuel.Ignite(index, shooter); return; }
        var network = active.network;
        if (network == null || !network.IsListening || network.CustomMessagingManager == null) return;
        using var writer = new FastBufferWriter(sizeof(byte), Allocator.Temp);
        writer.WriteValueSafe((byte)index);
        network.CustomMessagingManager.SendNamedMessage(CanShotMessage, NetworkManager.ServerClientId, writer);
    }

    // A guest's shot reached a can: the host sets it off as theirs.
    private void ReceiveCanShot(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out byte index);
        PlayerMovement shooter = null;
        foreach (var someone in PlayerRoster.All)
            if (someone != null && someone.TryGetComponent(out NetworkObject owner) && owner.IsSpawned && owner.OwnerClientId == sender) { shooter = someone; break; }
        fuel.Ignite(index, shooter);
    }

    // A Collect's count on the mission HUD as the cans go in, here and on every guest.
    private void ShowPoured()
    {
        var now = Current;
        if (finished || now == null || now.Kind != CampaignStepKind.Collect || tracker == null) { shownPoured = -1; return; }
        int poured = fuel.Poured;
        if (poured == shownPoured) return;
        shownPoured = poured;
        tracker.SetProgress(now.Id, Mathf.Min(poured, now.Count), Mathf.Max(1, now.Count));
    }

    // The button of the current step, or of a later one with only passable steps before it (C4), that this player
    // stands at; -1 for none.
    private int ButtonWithin(PlayerMovement mine)
    {
        for (int i = step; i < steps.Count; i++)
        {
            if (i > step && !Passable(steps[i - 1].Kind)) return -1;
            if (steps[i].Kind == CampaignStepKind.Press && Within(mine, steps[i])) return i;
        }
        return -1;
    }

    private void Press(int button)
    {
        if (IsHost) { AdvanceTo(button + 1); return; }
        if (network == null || !network.IsListening) return;
        using var writer = new FastBufferWriter(sizeof(byte), Allocator.Temp);
        writer.WriteValueSafe((byte)button);
        network.CustomMessagingManager.SendNamedMessage(PressMessage, NetworkManager.ServerClientId, writer);
    }

    // A guest pressed a step's button: it counts if that step is still ahead with nothing but passable steps before it.
    private void ReceivePress(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out byte pressed);
        if (!finished && PassableUpTo(pressed) && steps[pressed].Kind == CampaignStepKind.Press) AdvanceTo(pressed + 1);
    }
}
