using System;
using System.IO;
using UnityEngine;

/// <summary>
/// Player preferences: volume, controls and screen shake. Saved per user in settings.json under
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
    [Tooltip("Horizontal field of view in degrees, measured at 16:9. Wider screens see more at the sides.")]
    public float fieldOfView = 90f;

    public const float MinSensitivity = .2f, MaxSensitivity = 3f, MinFieldOfView = 70f, MaxFieldOfView = 120f;

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
        fieldOfView = Mathf.Clamp(fieldOfView, MinFieldOfView, MaxFieldOfView);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        current = null;
        Changed = null;
    }
}
