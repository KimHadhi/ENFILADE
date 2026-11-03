using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Player preferences: volume, controls, screen, frame rate and crosshairs. Saved per user in settings.json under
/// Application.persistentDataPath and changed in the Esc menu. Balance numbers stay in Resources/Tables.
/// </summary>
[Serializable]
public sealed class GameSettings
{
    public float masterVolume = 1f, combatVolume = 1f, movementVolume = 1f, voiceVolume = 1f, uiVolume = 1f;
    [Tooltip("Multiplies PlayerLook's base sensitivity.")]
    public float mouseSensitivity = 1f;
    public bool invertY;
    [Tooltip("Crouch and lean keys latch on one press instead of acting while held.")]
    public bool crouchToggle, leanToggle;
    [Tooltip("Scales head bob, landing, impacts and movement tilt. Lean and weapon recoil are not affected.")]
    public float screenShake = 1f;
    [Tooltip("How much the HUD trails the look when turning (HudCamera). 0 keeps it still.")]
    public float hudSway = .5f;
    [Tooltip("Show the glass boards with their grid that the HUD's two sides stand on (HudBoard). Off by default.")]
    public bool hudBoards;
    [Tooltip("Horizontal field of view in degrees, measured at 16:9. Wider screens see more at the sides.")]
    public float fieldOfView = 90f;
    [Tooltip("Show the player's own hair and head in first person, cut only where the camera sits inside them.")]
    public bool showOwnHead = true;
    [Tooltip("Frames per second cap, one of FrameLimits. 0 is unlimited. Ignored while vSync is on.")]
    public int frameLimit;
    [Tooltip("Wait for the monitor's refresh before showing a frame. Overrides frameLimit.")]
    public bool vSync;
    [Tooltip("The crosshair keeps one shape: weapon spread (moving, jumping, each gun's own) and shot kicks never open it. " +
        "Applies to the designed and the player's crosshair alike.")]
    public bool steadyCrosshair = true;
    [Tooltip("The crosshair for guns, and for fists and melee weapons. Each keeps the designed one until the player draws their own.")]
    public CrosshairSettings gunCrosshair = CrosshairSettings.Default(CrosshairKind.Gun),
        meleeCrosshair = CrosshairSettings.Default(CrosshairKind.Melee);
    [Tooltip("Player skin ID from the PlayerBody profile. Empty uses the profile's default character.")]
    public string characterSkin = "";
    [Tooltip("The name other players see. Empty uses this PC's user name.")]
    public string nickname = "";
    [Tooltip("Scene name of the map last chosen on the main menu (Resources/Tables/Maps.csv).")]
    public string lastMap = "";
    [Tooltip("Address last typed on the main menu to join a room.")]
    public string lastAddress = GameSession.DefaultAddress;
    [Tooltip("Room title, size and reach last used when making a room.")]
    public string roomTitle = "";
    public int maxPlayers = 4;
    public bool roomOnInternet = true;

    public const float MinSensitivity = .2f, MaxSensitivity = 3f, MinFieldOfView = 70f, MaxFieldOfView = 120f;
    /// <summary>Frame caps offered in the menu; 0 is unlimited.</summary>
    public static readonly int[] FrameLimits = { 0, 30, 60, 120, 144, 165, 240 };

    /// <summary>The name this player goes by.</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(nickname) ? Environment.UserName : nickname.Trim();
    /// <summary>The camera's vertical field of view for <see cref="fieldOfView"/>.</summary>
    public float VerticalFieldOfView => Camera.HorizontalToVerticalFieldOfView(fieldOfView, 16f / 9f);
    private const string FileName = "settings.json";
    private static GameSettings current;

    /// <summary>Raised after a change is applied, before it is saved.</summary>
    public static event Action Changed;
    public static GameSettings Current => current ??= Load();
    private static string FilePath => Path.Combine(Application.persistentDataPath, FileName);

    /// <summary>Clamps the values and tells listeners. Call after editing <see cref="Current"/>.</summary>
    public static void Apply()
    {
        Current.Clamp();
        Changed?.Invoke();
    }

    public static void ResetToDefaults()
    {
        current = new GameSettings();
        Apply();
    }

    public static void Save()
    {
        try { File.WriteAllText(FilePath, JsonUtility.ToJson(Current, true)); }
        catch (Exception e) { Debug.LogWarning($"[Settings] Could not save {FilePath}: {e.Message}"); }
    }

    private static GameSettings Load()
    {
        var settings = new GameSettings();
        try
        {
            if (File.Exists(FilePath)) JsonUtility.FromJsonOverwrite(File.ReadAllText(FilePath), settings);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Settings] {FilePath} could not be read; using defaults. {e.Message}");
            settings = new GameSettings();
        }
        settings.Clamp();
        return settings;
    }

    private void Clamp()
    {
        masterVolume = Mathf.Clamp01(masterVolume);
        combatVolume = Mathf.Clamp01(combatVolume);
        movementVolume = Mathf.Clamp01(movementVolume);
        voiceVolume = Mathf.Clamp01(voiceVolume);
        uiVolume = Mathf.Clamp01(uiVolume);
        mouseSensitivity = Mathf.Clamp(mouseSensitivity, MinSensitivity, MaxSensitivity);
        screenShake = Mathf.Clamp01(screenShake);
        hudSway = Mathf.Clamp01(hudSway);
        fieldOfView = Mathf.Clamp(fieldOfView, MinFieldOfView, MaxFieldOfView);
        frameLimit = Mathf.Max(0, frameLimit);
        (gunCrosshair ??= CrosshairSettings.Default(CrosshairKind.Gun)).Clamp();
        (meleeCrosshair ??= CrosshairSettings.Default(CrosshairKind.Melee)).Clamp();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        current = null;
        Changed = null;
    }
}
