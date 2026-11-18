using UnityEngine;

// FieldPickups: This PC's player. One part of the class; the rest is in FieldPickups.cs and the files beside it.
public sealed partial class FieldPickups
{
    private void Update()
    {
        if (IsHost)
        {
            Expire();
            Share();
        }
        Touch();
        Aim();
        ShowPrompt();
        if (targeted < 0 || refusal != null || interact == null || !interact.WasPressedThisFrame()) return;
        var item = items[targeted];
        pressedAt = Time.unscaledTime;
        if (IsHost) Take(item.Id, Mine, PlayerRoster.Local);
        else SendAsk(item.Id);
    }

    // Ammo and money under this PC's player: ask for them (the host just takes them), again now and then until answered.
    private void Touch()
    {
        var mine = PlayerRoster.Local;
        if (mine == null || !mine.IsInitialized || mine.IsDead || mine.IsDowned) return;
        Vector3 at = mine.transform.position;
        float now = Time.unscaledTime;
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (!Walked(RowOf(item).Kind)) continue;
            Vector3 off = item.Position - at;
            if (Mathf.Abs(off.y) > TouchHeight) continue;
            off.y = 0f;
            if (off.sqrMagnitude > touchReach * touchReach) continue;
            if (asked.TryGetValue(item.Id, out float when) && now - when < AskAgain) continue;
            // Ammo this player can't use stays for someone who can.
            if (Refuse(mine, item) != null) continue;
            asked[item.Id] = now;
            if (!IsHost) { SendAsk(item.Id); continue; }
            // The host takes it at once, which changes the list: the rest wait for the next frame.
            if (Take(item.Id, Mine, mine)) return;
        }
    }

    // The [F] pickup within reach nearest the middle of the view, unless something else has [F] or the hands.
    private void Aim()
    {
        targeted = -1;
        refusal = null;
        eye = null;
        var mine = PlayerRoster.Local;
        if (mine == null || !mine.TryGetComponent(out PlayerBody body) || body.View == null) return;
        eye = body.View;
        if (!Free(mine)) return;
        float best = lookAngle;
        for (int i = 0; i < items.Count; i++)
        {
            var item = items[i];
            if (Walked(RowOf(item).Kind)) continue;
            if ((item.Position - mine.transform.position).sqrMagnitude > reach * reach) continue;
            float angle = Vector3.Angle(eye.forward, item.Position + Vector3.up * .25f - eye.position);
            if (angle > best) continue;
            best = angle;
            targeted = i;
        }
        if (targeted >= 0) refusal = Refuse(mine, items[targeted]);
    }

    private static bool Free(PlayerMovement mine)
    {
        if (!mine.IsInitialized || mine.IsDead || mine.IsDowned || Cursor.lockState != CursorLockMode.Locked) return false;
        if (ShopMenu.TakesKeyboard || PerkCardScreen.TakesKeyboard || GunCustomizeMenu.TakesKeyboard || DevConsole.TakesKeyboard) return false;
        if (mine.Execution != null && mine.Execution.Candidate != null) return false;
        if (mine.TryGetComponent(out PlayerRevive revive) && revive.Candidate != null) return false;
        return !(mine.TryGetComponent(out PlayerWeaponSlots slots) && slots.HandsFull);
    }

    // What stops this player using it now, or null.
    private string Refuse(PlayerMovement player, in Item item)
    {
        var row = RowOf(item);
        var receiver = player.DamageReceiver;
        player.TryGetComponent(out PlayerWeaponSlots slots);
        switch (row.Kind)
        {
            case Kind.Gun:
                if (slots == null || player.CombatInputLocked) return Texts.Get("pickup.refuse.now");
                // A gun of a kind already held is taken for its rounds: none in it, or no room for them, and it stays.
                int held = slots.SlotOf(row.Weapon);
                if (held < 0) return null;
                var gun = StateOf(item);
                if (gun.Spare + (gun.MagazineOut ? 0 : gun.Rounds) <= 0) return Texts.Get("pickup.refuse.emptygun");
                return slots.RoomFor(held, gun) > 0 ? null : Texts.Get("pickup.refuse.ammofull");
            case Kind.Ammo:
                // Room for the class throwable alone is reason enough to take it (PlayerGrenades).
                if (PlayerGrenades.HasRoom(player)) return null;
                if (slots == null || !PlayerWeaponSlots.HoldsAnyGun(slots)) return Texts.Get("pickup.refuse.nogun");
                return RoomForAmmo(slots) ? null : Texts.Get("pickup.refuse.ammofull");
            case Kind.Heal: return receiver.Health >= receiver.MaximumHealth - .5f ? Texts.Get("pickup.refuse.healthfull") : null;
            case Kind.Shield: return receiver.Shield >= row.Amount - .5f ? Texts.Get("pickup.refuse.shield") : null;
        }
        return null;
    }

    // On the taker's own PC: what it got, as it lay.
    private void Give(in Item item)
    {
        var player = PlayerRoster.Local;
        var row = RowOf(item);
        if (player == null || row == null) return;
        var receiver = player.DamageReceiver;
        player.TryGetComponent(out PlayerWeaponSlots slots);
        switch (row.Kind)
        {
            case Kind.Gun:
                GiveGun(player, slots, row, StateOf(item), item.Position);
                break;
            case Kind.Ammo:
                int magazines = Mathf.Max(1, Mathf.RoundToInt(row.Amount));
                if (slots != null) AddMagazines(slots, magazines);
                // The class throwable comes with it (Grenades.csv refill), as Horde's ammo boxes give grenades.
                int thrown = PlayerGrenades.FromAmmoPile(player);
                string gives = Texts.Format("pickup.gives.ammo", magazines);
                // A scavenger's share more says so on the line, as Heriot's does on a kill's points.
                if (PlayerPerks.Has(PlayerPerks.Scavenger))
                    gives += "  " + Texts.Format("perk.scavenger.gives", Mathf.RoundToInt(PlayerPerks.Total(PlayerPerks.Scavenger)));
                if (thrown > 0) gives += "  " + Texts.Format("pickup.gives.grenade", thrown);
                Pop(row.Kind, row.Name, gives);
                break;
            case Kind.Heal:
                receiver.Heal(row.Amount);
                Pop(row.Kind, row.Name, Texts.Format("pickup.gives.heal", Mathf.RoundToInt(row.Amount)));
                break;
            case Kind.Shield:
                receiver.SetShield(Mathf.Max(receiver.Shield, row.Amount));
                Pop(row.Kind, row.Name, Texts.Format("pickup.gives.shield", Mathf.RoundToInt(row.Amount)));
                break;
            case Kind.Money:
                Wallet.Receive((long)row.Amount);
                Pop(row.Kind, row.Name, "+" + Wallet.Format((long)row.Amount));
                break;
        }
    }

    // This particular gun, as it lay (the user, 2026-11-18, "필드 무기 교체 및 탄약 시스템 개편"). Into an empty gun slot,
    // else in place of the gun in hand, else the first gun slot: it comes with the rounds that were in it, the rounds
    // lying with it join this player's reserve for its kind as far as that is filled to (the rest stays with the gun),
    // and the gun it took the place of goes on the ground with what is in that one, its reserve staying with the player
    // (PlayerWeaponSlots.Put). A gun of a kind already held
    // isn't taken: its rounds go into the reserve as far as there is room, and it stays where it lay with the rest
    // (TakeRounds). Nothing else is filled either way: before, the gun taken was a new full one, and one of a kind
    // already held refilled every gun the player had.
    private void GiveGun(PlayerMovement player, PlayerWeaponSlots slots, Row row, GunState gun, Vector3 lay)
    {
        if (slots == null || gun.Weapon == null) return;
        int held = slots.SlotOf(gun.Weapon);
        if (held >= 0)
        {
            int taken = slots.TakeRounds(held, gun, out GunState left);
            if (taken > 0) Pop(Kind.Ammo, row.Name, Texts.Format("pickup.gives.rounds", taken));
            // It was taken off the ground to be handed here; it goes back where it lay, with what is left in it.
            Leave(left, lay);
            return;
        }
        int slot = GunSlotFor(slots);
        if (!slots.Put(slot, gun, out GunState? before))
        {
            // The slot wouldn't take it after all (the hands were locked meanwhile): it is not lost, it lies there again.
            Leave(gun, lay);
            return;
        }
        // Not with a can in the hands, nor with the guns on an x-ray belt (GimmickTray): the new gun waits in its slot.
        if (!slots.HandsFull && !slots.GunsAway) slots.SelectSlot(slot);
        // The line says what was got: the rounds in it, and of those lying with it what the reserve had room for.
        var mine = slots.GetRuntime(slot);
        Pop(row.Kind, row.Name, Holds(gun.Holding(gun.Rounds, mine != null ? gun.Spare - mine.Spare : gun.Spare)), gun.Weapon);
        if (before is GunState old) Leave(old, player.transform.position + player.transform.forward * LeaveAhead);
    }

    // Each gun held gets this many magazines more (a scavenger's share more: PlayerPerks.ScavengeFactor), up to the reserve
    // it is filled to (PlayerWeaponSlots.ReserveCap, which an ammo pouch raises).
    private static void AddMagazines(PlayerWeaponSlots slots, int magazines)
    {
        float share = PlayerPerks.ScavengeFactor;
        for (int i = 0; i < PlayerWeaponSlots.SlotCount; i++)
        {
            var data = slots.GetWeapon(i);
            // A Schießhund's Desperado is loaded by bounties alone.
            if (data == null || data.Category != WeaponCategory.Ranged || slots.KeepsOwnRounds(i)) continue;
            var runtime = slots.GetRuntime(i);
            int room = slots.ReserveCap(i) - runtime.ReserveAmmo;
            if (room > 0) runtime.AddReserve(Mathf.Min(room, Mathf.RoundToInt(data.SupplyRounds * magazines * share)));
        }
    }

    private static bool RoomForAmmo(PlayerWeaponSlots slots)
    {
        for (int i = 0; i < PlayerWeaponSlots.SlotCount; i++)
        {
            var data = slots.GetWeapon(i);
            if (data != null && data.Category == WeaponCategory.Ranged && !slots.KeepsOwnRounds(i)
                && slots.GetRuntime(i).ReserveAmmo < slots.ReserveCap(i)) return true;
        }
        return false;
    }

    private static int GunSlotFor(PlayerWeaponSlots slots)
    {
        for (int i = 0; i < PlayerWeaponSlots.SlotCount; i++)
            if (PlayerWeaponSlots.GetSlotCategory(i) == WeaponCategory.Ranged && slots.GetWeapon(i) == null) return i;
        int held = slots.SelectedSlot;
        if (held >= 0 && PlayerWeaponSlots.GetSlotCategory(held) == WeaponCategory.Ranged) return held;
        for (int i = 0; i < PlayerWeaponSlots.SlotCount; i++)
            if (PlayerWeaponSlots.GetSlotCategory(i) == WeaponCategory.Ranged) return i;
        return 0;
    }
}
