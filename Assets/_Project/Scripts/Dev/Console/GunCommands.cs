/// <summary>
/// The console commands of the guns side (2026-11-18). Each is added beside its feature and registered here, so the
/// chat's console channel and the developer console get the same list (GameCommands).
/// </summary>
public static class GunCommands
{
    public static void Register(ChatCommandRouter router)
    {
        router.Register("drop", "[1 · 2] — 든 총(또는 그 칸의 총)을 발 앞에 내려놓는다. 안에 든 탄은 총과 함께 가고 예비 탄약은 내게 남는다. 다시 주우면 그대로다. Tab 메뉴의 드랍이 생기기 전의 시험용 (FieldPickups.Drop). 기록은 Logs/gun-ammo.txt", Drop); // dev-text
    }

    // The gun in hand, or the one in the slot named, goes on the ground as it is.
    private static string Drop(string args)
    {
        var mine = PlayerRoster.Local;
        if (mine == null || !mine.TryGetComponent(out PlayerWeaponSlots slots)) return "플레이어가 없습니다."; // dev-text
        int slot = slots.SelectedSlot;
        string text = (args ?? string.Empty).Trim();
        if (text.Length > 0)
        {
            if (!TableCells.TryWhole(text, out int asked) || asked < 1 || asked > 2) return "사용법: /drop [1 또는 2] — 비우면 든 총"; // dev-text
            slot = asked - 1;
        }
        var gun = slot >= 0 ? slots.GetWeapon(slot) : null;
        if (gun == null || !FieldPickups.Drop(mine, slot)) return "내려놓을 총이 없습니다(그 칸이 비었거나, 내려놓을 수 없는 총이거나, 줍기가 없는 맵)."; // dev-text
        return $"{gun.DisplayName}을(를) 내려놓았습니다. 안에 든 탄은 총에, 예비 탄약은 내게 남습니다."; // dev-text
    }
}
