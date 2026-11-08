using UnityEngine;

/// <summary>
/// The settings a new player starts with and each settings tab's "이 탭 기본값" returns to: one <see cref="GameSettings"/>
/// edited in the inspector at Resources/Settings/DefaultSettings. The player's own choices stay in settings.json and
/// win over it; a setting the player never touched follows the preset. Without the asset the defaults written in
/// GameSettings apply.
/// </summary>
[CreateAssetMenu(menuName = "ENFILADE/Settings Preset", fileName = "DefaultSettings")]
public sealed class GameSettingsPreset : ScriptableObject
{
    public const string ResourcePath = "Settings/DefaultSettings";

    [Tooltip("Defaults for every setting. The profile fields (name, last map, last address, room) are where a fresh " +
        "install starts too.")]
    [SerializeField] private GameSettings settings = new GameSettings();

    private static GameSettingsPreset loaded;
    private static bool looked;

    /// <summary>A copy of the preset's settings, free to change; the GameSettings defaults when there is no preset.</summary>
    public static GameSettings Copy()
    {
        if (!looked)
        {
            looked = true;
            loaded = Resources.Load<GameSettingsPreset>(ResourcePath);
        }
        if (loaded == null || loaded.settings == null) return new GameSettings();
        var copy = JsonUtility.FromJson<GameSettings>(JsonUtility.ToJson(loaded.settings));
        copy.Clamp();
        return copy;
    }

    private void OnValidate() => settings?.Clamp();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        loaded = null;
        looked = false;
    }
}
