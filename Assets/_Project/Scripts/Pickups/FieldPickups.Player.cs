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
            var row = rows[item.Row];
            if (!Walked(row.Kind)) continue;
            Vector3 off = item.Position - at;
            if (Mathf.Abs(off.y) > TouchHeight) continue;
            off.y = 0f;
            if (off.sqrMagnitude > touchReach * touchReach) continue;
            if (asked.TryGetValue(item.Id, out float when) && now - when < AskAgain) continue;
            // Ammo this player can't use stays for someone who can.
            if (Refuse(mine, row) != null) continue;
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
            if (Walked(rows[item.Row].Kind)) continue;
            if ((item.Position - mine.transform.position).sqrMagnitude > reach * reach) continue;
            float angle = Vector3.Angle(eye.forward, item.Position + Vector3.up * .25f - eye.position);
            if (angle > best) continue;
            best = angle;
            targeted = i;
        }
        if (targeted >= 0) refusal = Refuse(mine, rows[items[targeted].Row]);
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
    private static string Refuse(PlayerMovement player, Row row)
    {
        var receiver = player.DamageReceiver;
        player.TryGetComponent(out PlayerWeaponSlots slots);
        switch (row.Kind)
        {
            case Kind.Gun: return slots == null || player.CombatInputLocked ? Texts.Get("pickup.refuse.now") : null;
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

    // On the taker's own PC: what it got.
    private void Give(int index)
    {
        var player = PlayerRoster.Local;
        if (player == null || index < 0 || index >= rows.Count) return;
        var row = rows[index];
        var receiver = player.DamageReceiver;
        player.TryGetComponent(out PlayerWeaponSlots slots);
        switch (row.Kind)
        {
            case Kind.Gun:
                GiveGun(player, slots, row);
                break;
            case Kind.Ammo:
                int magazines = Mathf.Max(1, Mathf.RoundToInt(row.Amount));
                if (slots != null) AddMagazines(slots, magazines);
                // The class throwable comes with it (Grenades.csv refill), as Horde's ammo boxes give grenades.
                int thrown = PlayerGrenades.FromAmmoPile(player);
                string gives = Texts.Format("pickup.gives.ammo", magazines);
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

    // Into an empty gun slot, else in place of the gun in hand (left on the floor), else the first gun slot. A gun
    // already held fills its ammo instead.
    private void GiveGun(PlayerMovement player, PlayerWeaponSlots slots, Row row)
    {
        if (slots == null || row.Weapon == null) return;
        for (int i = 0; i < PlayerWeaponSlots.SlotCount; i++)
        {
            if (slots.GetWeapon(i) != row.Weapon) continue;
            slots.RefillAmmo();
            Pop(row.Kind, row.Name, Texts.Get("pickup.gives.refill"), row.Weapon);
            return;
        }
        int slot = GunSlotFor(slots);
        var before = slots.GetWeapon(slot);
        if (!slots.TrySetWeapon(slot, row.Weapon)) return;
        if (!slots.HandsFull) slots.SelectSlot(slot);
        Pop(row.Kind, row.Name, Texts.Get("pickup.gives.gun"), row.Weapon);
        if (before != null && before.Category == WeaponCategory.Ranged)
            Leave(RowOf(before), player.transform.position + player.transform.forward * LeaveAhead);
    }

    // Each gun held gets this many magazines more, up to the reserve it started with.
    private static void AddMagazines(PlayerWeaponSlots slots, int magazines)
    {
        for (int i = 0; i < PlayerWeaponSlots.SlotCount; i++)
        {
            var data = slots.GetWeapon(i);
            // A Schießhund's Desperado is loaded by bounties alone.
            if (data == null || data.Category != WeaponCategory.Ranged || slots.KeepsOwnRounds(i)) continue;
            var runtime = slots.GetRuntime(i);
            int room = data.InitialReserveAmmo - runtime.ReserveAmmo;
            if (room > 0) runtime.AddReserve(Mathf.Min(room, data.MagazineSize * magazines));
        }
    }

    private static bool RoomForAmmo(PlayerWeaponSlots slots)
    {
        for (int i = 0; i < PlayerWeaponSlots.SlotCount; i++)
        {
            var data = slots.GetWeapon(i);
            if (data != null && data.Category == WeaponCategory.Ranged && !slots.KeepsOwnRounds(i)
                && slots.GetRuntime(i).ReserveAmmo < data.InitialReserveAmmo) return true;
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
