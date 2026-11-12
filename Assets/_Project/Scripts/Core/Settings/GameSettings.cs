using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>How damage numbers show: none, one running total per target, or one number per hit.</summary>
public enum DamageNumberMode { Off, Stacked, Each }

/// <summary>How the hit marker looks (HitMarkerHUD): thin lines that pop and fade, or the earlier thick ticks that slide in.</summary>
public enum HitMarkerStyle { Clean, Bold }

/// <summary>How the weapon slots look (WeaponSlotsHUD): a column of leaning plates at the HUD's right (A), or a row of cards
/// over the ammo (C).</summary>
public enum WeaponSlotLook { Plates, Cards }

/// <summary>How dying looks (DeathScreen): all of it until spectating or coming back, a shorter one, or none at all.</summary>
public enum DeathScreenMode { Full, Short, Off }

/// <summary>How the game window covers the monitor (DisplaySettings).</summary>
public enum WindowMode { Fullscreen, Borderless, Windowed }

/// <summary>The settings menu's tabs, each reset to the defaults on its own.</summary>
public enum SettingsGroup { Sound, Controls, Screen, Camera, Hud, Crosshair, Character }

/// <summary>
/// Player preferences: volume, controls, screen, camera, HUD, frame rate and crosshairs. Saved per user in settings.json under
/// Application.persistentDataPath and changed in the Esc menu. A fresh player starts from the defaults
/// (<see cref="GameSettingsPreset"/>: the values written below, then Resources/Settings/DefaultSettings if it was edited,
/// then the table Tables/SettingsDefaults.csv), which are also what each tab's reset returns to. Balance numbers stay in
/// Data/Resources/Tables.
/// </summary>
[Serializable]
public sealed class GameSettings
{
    public float masterVolume = 1f, combatVolume = 1f, movementVolume = 1f, voiceVolume = 1f, uiVolume = 1f;
    [Tooltip("The campaign's songs (CampaignMusic) and each map's background sound (MapAmbience).")]
    public float musicVolume = .8f, ambienceVolume = 1f;
    [Tooltip("Multiplies PlayerLook's base sensitivity.")]
    public float mouseSensitivity = 1f;
    public bool invertY;
    [Tooltip("Crouch and lean keys latch on one press instead of acting while held.")]
    public bool crouchToggle, leanToggle;
    [Tooltip("An empty magazine reloads by itself once the gun could fire again, and a reload cut short before its bolt finishes " +
        "itself (WeaponController.AutoReloadDue). Off: only R reloads.")]
    public bool autoReload = true;
    [Tooltip("Callouts (PlayerCallouts): letting go of the callout key says the line the pointer is on. Off: a left click " +
        "says it, and letting go closes the wheel unsaid.")]
    public bool calloutOnRelease = true;
    [Tooltip("Teammates' callouts also show as their words over their heads (CalloutBubbles). Off leaves their voice and pins.")]
    public bool calloutBubbles = true;
    [Tooltip("The player shouts some callouts by themself: help when a zombie gets hold of them or they go down, the elite " +
        "they first come near, and the CHARGE (the Assault's, and friends near it answering).")]
    public bool calloutAuto = true;
    [Tooltip("A callout brings up the caller's face, live from a camera in front of them, in a small comms window on the HUD " +
        "(CalloutFeedHUD), this player's own included. Off: no window and no extra camera.")]
    public bool calloutFeed = true;
    [Tooltip("Friends pointing with a held F can poke this player's character (cheeks, belly, arms, shoulders, the top of " +
        "the head): the skin gives under their fingertip on every PC (SkinPoke). Off: their fingertip stops short of this " +
        "character everywhere, the others' screens included (PlayerPose carries it).")]
    public bool letOthersPoke = true;
    /// <summary>
    /// The keys the player picked in the key window (KeyBindings: the Input System's overrides as JSON, by binding id);
    /// empty for every default. The default keys themselves are Input/Player.inputactions.
    /// </summary>
    public string keyBindings = "";
    [Tooltip("Scales head bob, landing, impacts and movement tilt. Lean and weapon recoil are not affected.")]
    public float screenShake = 1f;
    [Tooltip("How much of the body's own sway the first-person view follows (1 rides the eyes as they move, 0 follows an "
        + "eased path through them). The head bends to wherever the view is (HeadReach), so the two stay together.")]
    public float bodySway = 1f;
    [Tooltip("How much the HUD trails the look when turning (HudCamera). 0 keeps it still.")]
    public float hudSway = .5f;
    [Tooltip("Scales each HUD element's own lean: the rows of HudTilt.csv, the teammate cards and the score popup. " +
        "0 lays them flat on their side of the HUD, 2 doubles them. HudTilt slides leaning pieces apart so none covers another.")]
    public float hudTilt = 1f;
    [Tooltip("How far the HUD's left and right sides turn away at their inner edge (°), like the inside of a curved " +
        "screen (HudPlane). 0 is a flat HUD.")]
    public float hudAngle = 20f;
    [Tooltip("While leaning (Q/E) the HUD's sides tilt on the screen the way the head does: Q to the left, E to the right (HudPlane, Hud.csv Lean).")]
    public bool hudFollowsLean = true;
    [Tooltip("Show the glass boards with their grid that the HUD's two sides stand on (HudBoard). Off by default.")]
    public bool hudBoards;
    [Tooltip("Damage numbers: none, one running total per target (default), or one number per hit (DamagePopupManager).")]
    public DamageNumberMode damageNumbers = DamageNumberMode.Stacked;
    [Tooltip("Hit marker look: Clean (four thin lines that pop out and fade, after PAYDAY 3, numbers in HitMarkers.csv) or " +
        "Bold (the earlier thick outlined ticks that hold wide and slide in, numbers in GunFeel). The headshot's star, sparks " +
        "and flare and the posture break's burst show with either.")]
    public HitMarkerStyle hitMarkerStyle = HitMarkerStyle.Clean;
    [Tooltip("Weapon slots: Plates (a column of dark plates with cut corners at the HUD's right; the one in hand kicks out, " +
        "grows and shows its name; the default) or Cards (a row of small cards over the ammo; the one in hand turns into a " +
        "wide light card). Numbers in WeaponSlots.csv.")]
    public WeaponSlotLook weaponSlots = WeaponSlotLook.Plates;
    [Tooltip("Death screen (DeathScreen): Full drains, blurs and muffles the world with the sting and the card of what killed " +
        "the player, how long they lasted and what comes next, until spectating or coming back; Short is the same over a " +
        "shorter time and lighter; Off shows only the plain lines (PlayerLifeHUD). Numbers in DeathScreen.csv.")]
    public DeathScreenMode deathScreen = DeathScreenMode.Full;
    [Tooltip("Horizontal field of view in degrees, measured at 16:9. Wider screens see more at the sides.")]
    public float fieldOfView = 90f;
    [Tooltip("Show the player's own hair and head in first person, cut only where the camera sits inside them.")]
    public bool showOwnHead = true;
    [Tooltip("Rookie mode (애송이 모드): through the parkour moves ticked below the camera leaves the eyes for behind the head, " +
        "so the whole body shows, and comes back after (ThirdPersonCamera). Off by default: the game is played through the eyes.")]
    public bool rookieMode;
    [Tooltip("Rookie mode's moves, each on its own: vaults and climbs, ladders, landing rolls, dodges, ziplines, slides and " +
        "swing bars. All on, so switching rookie mode on starts with every one.")]
    public bool rookieVault = true, rookieLadder = true, rookieLandingRoll = true, rookieDodge = true, rookieZipline = true,
        rookieSlide = true, rookieBars = true;
    [Tooltip("Executions and stealth kills are shown from outside the body at changing angles, then the view comes back to " +
        "the eyes (ThirdPersonCamera). This PC's screen only.")]
    public bool executionCinematic = true;
    [Tooltip("Frames per second cap, one of FrameLimits. 0 is unlimited. Ignored while vSync is on.")]
    public int frameLimit;
    [Tooltip("Wait for the monitor's refresh before showing a frame. Overrides frameLimit.")]
    public bool vSync;
    [Tooltip("Monitor the game runs on, by the name Windows gives it (DisplaySettings). Empty keeps the one the game opens on.")]
    public string monitorName = "";
    [Tooltip("That monitor's place in Windows' list, telling two monitors of the same name apart. -1 when unset.")]
    public int monitorIndex = -1;
    [Tooltip("Rendering resolution in pixels. 0 uses the monitor's own resolution.")]
    public int resolutionWidth, resolutionHeight;
    [Tooltip("Exclusive fullscreen, a borderless window over the whole monitor, or a window.")]
    public WindowMode windowMode = WindowMode.Borderless;
    [Tooltip("The crosshair keeps one shape: weapon spread (moving, jumping, each gun's own) and shot kicks never open it. " +
        "Applies to the designed and the player's crosshair alike.")]
    public bool steadyCrosshair = true;
    [Tooltip("The crosshair for guns, and for fists and melee weapons. Each keeps the designed one until the player draws their own.")]
    public CrosshairSettings gunCrosshair = CrosshairSettings.Default(CrosshairKind.Gun),
        meleeCrosshair = CrosshairSettings.Default(CrosshairKind.Melee);
    [Tooltip("Player skin ID from the PlayerBody profile. Empty uses the profile's default character.")]
    public string characterSkin = "";
    [Tooltip("Colour set ID of characterSkin (SkinVariant). Empty or unknown wears the skin's first colour set.")]
    public string characterVariant = "";
    [Tooltip("What each character wears in its outfit slots, one entry per character: \"skin@slot=item#colour;slot=item\" " +
        "(OutfitLook). A character without an entry wears its own clothes.")]
    public List<string> outfits = new List<string>();
    [Tooltip("The name other players see. Empty uses this PC's user name.")]
    public string nickname = "";
    [Tooltip("Scene name of the map last chosen on the main menu (Data/Resources/Tables/Maps/Maps.csv).")]
    public string lastMap = "";
    [Tooltip("Address last typed on the main menu to join a room.")]
    public string lastAddress = GameSession.DefaultAddress;
    [Tooltip("Room title, size and reach last used when making a room.")]
    public string roomTitle = "";
    public int maxPlayers = 4;
    public bool roomOnInternet = true;

    public const float MinSensitivity = .2f, MaxSensitivity = 3f, MinFieldOfView = 70f, MaxFieldOfView = 120f;
    public const float MaxHudTilt = 2f, MaxHudAngle = 40f;
    /// <summary>Frame caps offered in the menu; 0 is unlimited.</summary>
    public static readonly int[] FrameLimits = { 0, 30, 60, 120, 144, 165, 240 };
    /// <summary>The least and most a cap typed into the frame limit's box can be (fps).</summary>
    public const int MinFrameLimit = 20, MaxFrameLimit = 1000;

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

    /// <summary>A separate copy of the defaults, from the preset asset when there is one.</summary>
    public static GameSettings Defaults() => GameSettingsPreset.Copy();

    /// <summary>Clamps the values and tells listeners. Call after editing <see cref="Current"/>.</summary>
    public static void Apply()
    {
        Current.Clamp();
        Changed?.Invoke();
    }

    /// <summary>Puts one tab's settings back to the defaults. The profile (name, last map, room) is never reset.</summary>
    public static void ResetToDefaults(SettingsGroup group)
    {
        var s = Current;
        var d = Defaults();
        switch (group)
        {
            case SettingsGroup.Sound:
                s.masterVolume = d.masterVolume; s.combatVolume = d.combatVolume; s.movementVolume = d.movementVolume;
                s.voiceVolume = d.voiceVolume; s.uiVolume = d.uiVolume;
                s.musicVolume = d.musicVolume; s.ambienceVolume = d.ambienceVolume;
                break;
            case SettingsGroup.Controls:
                s.mouseSensitivity = d.mouseSensitivity; s.invertY = d.invertY;
                s.crouchToggle = d.crouchToggle; s.leanToggle = d.leanToggle;
                s.autoReload = d.autoReload;
                s.calloutOnRelease = d.calloutOnRelease; s.calloutBubbles = d.calloutBubbles; s.calloutAuto = d.calloutAuto;
                s.calloutFeed = d.calloutFeed;
                s.letOthersPoke = d.letOthersPoke;
                // The keys are left alone: the key window has its own reset per line and for all (KeyBindingsView), so
                // putting the mouse back to default doesn't throw away every key the player set.
                break;
            case SettingsGroup.Screen:
                s.monitorName = d.monitorName; s.monitorIndex = d.monitorIndex;
                s.resolutionWidth = d.resolutionWidth; s.resolutionHeight = d.resolutionHeight; s.windowMode = d.windowMode;
                s.vSync = d.vSync; s.frameLimit = d.frameLimit;
                s.fieldOfView = d.fieldOfView; s.screenShake = d.screenShake; s.bodySway = d.bodySway; s.showOwnHead = d.showOwnHead;
                break;
            case SettingsGroup.Camera:
                s.rookieMode = d.rookieMode; s.rookieVault = d.rookieVault; s.rookieLadder = d.rookieLadder;
                s.rookieLandingRoll = d.rookieLandingRoll; s.rookieDodge = d.rookieDodge; s.rookieZipline = d.rookieZipline;
                s.rookieSlide = d.rookieSlide; s.rookieBars = d.rookieBars; s.executionCinematic = d.executionCinematic;
                break;
            case SettingsGroup.Hud:
                s.hudSway = d.hudSway; s.hudTilt = d.hudTilt; s.hudAngle = d.hudAngle; s.hudFollowsLean = d.hudFollowsLean;
                s.hudBoards = d.hudBoards; s.damageNumbers = d.damageNumbers; s.hitMarkerStyle = d.hitMarkerStyle;
                s.weaponSlots = d.weaponSlots;
                s.deathScreen = d.deathScreen;
                break;
            case SettingsGroup.Crosshair:
                s.steadyCrosshair = d.steadyCrosshair;
                s.gunCrosshair = d.gunCrosshair; s.meleeCrosshair = d.meleeCrosshair; // d is a fresh copy, nothing shares them
                break;
            case SettingsGroup.Character:
                s.characterSkin = d.characterSkin; s.characterVariant = d.characterVariant;
                s.outfits = new List<string>(d.outfits ?? new List<string>());
                break;
        }
        Apply();
    }

    public static void Save()
    {
        try { File.WriteAllText(FilePath, JsonUtility.ToJson(Current, true)); }
        catch (Exception e) { Debug.LogWarning($"[Settings] Could not save {FilePath}: {e.Message}"); }
    }

    // Saved values over the preset, so a setting added later starts at its preset default.
    private static GameSettings Load()
    {
        var settings = Defaults();
        try
        {
            if (File.Exists(FilePath)) JsonUtility.FromJsonOverwrite(File.ReadAllText(FilePath), settings);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[Settings] {FilePath} could not be read; using defaults. {e.Message}");
            settings = Defaults();
        }
        settings.Clamp();
        return settings;
    }

    internal void Clamp()
    {
        masterVolume = Mathf.Clamp01(masterVolume);
        combatVolume = Mathf.Clamp01(combatVolume);
        movementVolume = Mathf.Clamp01(movementVolume);
        voiceVolume = Mathf.Clamp01(voiceVolume);
        uiVolume = Mathf.Clamp01(uiVolume);
        musicVolume = Mathf.Clamp01(musicVolume);
        ambienceVolume = Mathf.Clamp01(ambienceVolume);
        mouseSensitivity = Mathf.Clamp(mouseSensitivity, MinSensitivity, MaxSensitivity);
        screenShake = Mathf.Clamp01(screenShake);
        bodySway = Mathf.Clamp01(bodySway);
        hudSway = Mathf.Clamp01(hudSway);
        hudTilt = Mathf.Clamp(hudTilt, 0f, MaxHudTilt);
        hudAngle = Mathf.Clamp(hudAngle, 0f, MaxHudAngle);
        if (damageNumbers < DamageNumberMode.Off || damageNumbers > DamageNumberMode.Each) damageNumbers = DamageNumberMode.Stacked;
        if (hitMarkerStyle < HitMarkerStyle.Clean || hitMarkerStyle > HitMarkerStyle.Bold) hitMarkerStyle = HitMarkerStyle.Clean;
        if (weaponSlots < WeaponSlotLook.Plates || weaponSlots > WeaponSlotLook.Cards) weaponSlots = WeaponSlotLook.Plates;
        if (deathScreen < DeathScreenMode.Full || deathScreen > DeathScreenMode.Off) deathScreen = DeathScreenMode.Full;
        fieldOfView = Mathf.Clamp(fieldOfView, MinFieldOfView, MaxFieldOfView);
        frameLimit = frameLimit <= 0 ? 0 : Mathf.Clamp(frameLimit, MinFrameLimit, MaxFrameLimit);
        monitorName ??= "";
        keyBindings ??= "";
        outfits ??= new List<string>();
        monitorIndex = Mathf.Max(-1, monitorIndex);
        if (resolutionWidth <= 0 || resolutionHeight <= 0) resolutionWidth = resolutionHeight = 0;
        if (windowMode < WindowMode.Fullscreen || windowMode > WindowMode.Windowed) windowMode = WindowMode.Borderless;
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
