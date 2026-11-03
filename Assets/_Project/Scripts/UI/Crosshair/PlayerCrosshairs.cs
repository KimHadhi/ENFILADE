using System;
using UnityEngine;

public enum CrosshairKind { Gun, Melee }

/// <summary>
/// The crosshairs the player draws in settings, as runtime profiles over the designed ones in Feedback.
/// <see cref="For"/> rewrites the profile from the settings on each call, so callers ask again after GameSettings.Changed.
/// </summary>
public static class PlayerCrosshairs
{
    private static readonly CrosshairProfile[] drawn = new CrosshairProfile[2];

    /// <summary>While set, the HUD shows this kind whatever is in hand: the settings' real-screen button is held.</summary>
    public static CrosshairKind? Showcase { get; private set; }
    public static event Action ShowcaseChanged;

    /// <summary>The designed crosshair: Feedback's gun or melee profile. Null when Feedback has none.</summary>
    public static CrosshairProfile Design(CrosshairKind kind)
    {
        var feedback = CombatFeedbackSettings.InstanceOrDefaults;
        return kind == CrosshairKind.Gun ? feedback.gunCrosshair : feedback.meleeCrosshair;
    }

    public static CrosshairSettings Settings(CrosshairKind kind) =>
        kind == CrosshairKind.Gun ? GameSettings.Current.gunCrosshair : GameSettings.Current.meleeCrosshair;

    /// <summary>The player's own crosshair, or null while they keep the designed one.</summary>
    public static CrosshairProfile For(CrosshairKind kind)
    {
        var settings = Settings(kind);
        if (!settings.custom) return null;
        ref var profile = ref drawn[(int)kind];
        if (profile == null)
        {
            profile = ScriptableObject.CreateInstance<CrosshairProfile>();
            profile.name = kind + " Crosshair (Settings)";
            profile.hideFlags = HideFlags.HideAndDontSave;
        }
        settings.WriteTo(profile, Design(kind));
        return profile;
    }

    /// <summary>Puts a whole drawing in place, such as one read from a share code. Call GameSettings.Apply after.</summary>
    public static void Replace(CrosshairKind kind, CrosshairSettings settings)
    {
        if (kind == CrosshairKind.Gun) GameSettings.Current.gunCrosshair = settings;
        else GameSettings.Current.meleeCrosshair = settings;
    }

    /// <summary>Throws away the player's drawing and starts again from <see cref="CrosshairSettings.Default"/>.</summary>
    public static void ResetDrawing(CrosshairKind kind)
    {
        var fresh = CrosshairSettings.Default(kind);
        fresh.custom = true;
        Replace(kind, fresh);
    }

    public static void SetShowcase(CrosshairKind? kind)
    {
        if (Showcase == kind) return;
        Showcase = kind;
        ShowcaseChanged?.Invoke();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Array.Clear(drawn, 0, drawn.Length);
        Showcase = null;
        ShowcaseChanged = null;
    }
}
