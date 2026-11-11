using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The settings a new player starts with and each settings tab's "이 탭 기본값" returns to. Three layers, each over the
/// last: the defaults written in <see cref="GameSettings"/>, this asset at Resources/Settings/DefaultSettings when it has
/// been edited in the inspector, and the table Resources/Tables/SettingsDefaults.csv, one column per setting, which is the
/// place to change them (user, 2026-11-11: "설정 기본값이나 텍스트들 내 입맛대로 수정하기", edited in Excel like every other
/// table). An empty cell keeps the layer below. The columns gunCrosshairCode and meleeCrosshairCode take a crosshair share
/// code (ENF-…) for the drawn crosshair a new player starts with. The player's own choices stay in settings.json and win
/// over all of it.
/// </summary>
[CreateAssetMenu(menuName = "ENFILADE/Settings Preset", fileName = "DefaultSettings")]
public sealed class GameSettingsPreset : ScriptableObject
{
    public const string ResourcePath = "Settings/DefaultSettings";
    public const string TableName = "SettingsDefaults", TableRow = "Default";
    private const string GunCrosshairColumn = "gunCrosshairCode", MeleeCrosshairColumn = "meleeCrosshairCode";

    [Tooltip("Defaults for every setting. The profile fields (name, last map, last address, room) are where a fresh " +
        "install starts too. Tables/SettingsDefaults.csv goes on top of these.")]
    [SerializeField] private GameSettings settings = new GameSettings();

    private static GameSettingsPreset loaded;
    private static bool looked;
    private static readonly HashSet<string> reported = new HashSet<string>();

    /// <summary>A copy of the defaults, free to change: the code's, then the asset's, then the table's.</summary>
    public static GameSettings Copy()
    {
        if (!looked)
        {
            looked = true;
            loaded = Resources.Load<GameSettingsPreset>(ResourcePath);
        }
        var copy = loaded != null && loaded.settings != null
            ? JsonUtility.FromJson<GameSettings>(JsonUtility.ToJson(loaded.settings))
            : new GameSettings();
        FromTable(copy);
        copy.Clamp();
        return copy;
    }

    // Each filled cell of the table's one row over the copy; a cell that names no setting or will not read is reported once.
    private static void FromTable(GameSettings settings)
    {
        if (!GameTables.TryGetRow(TableName, TableRow, out var row)) return;
        foreach (var cell in row)
        {
            if (cell.Key == GunCrosshairColumn || cell.Key == MeleeCrosshairColumn)
            {
                if (!CrosshairCode.TryDecode(cell.Value, out var drawing)) Report(cell.Key, cell.Value, "읽을 수 없는 크로스헤어 공유 코드입니다");
                else if (cell.Key == GunCrosshairColumn) settings.gunCrosshair = drawing;
                else settings.meleeCrosshair = drawing;
                continue;
            }
            var result = TableFieldWriter.TryWrite(settings, cell.Key, cell.Value, out string error);
            if (result != TableWrite.Written) Report(cell.Key, cell.Value, error ?? "GameSettings에 이 이름의 설정이 없습니다");
        }
    }

    private static void Report(string column, string value, string why)
    {
        string message = $"[Tables] {TableName}.csv [{TableRow}] '{column}' = '{value}': {why}.";
        if (reported.Add(message)) Debug.LogWarning(message);
    }

    private void OnValidate() => settings?.Clamp();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        loaded = null;
        looked = false;
        reported.Clear();
    }
}
