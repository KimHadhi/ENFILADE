using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>One magnification's mouse sensitivity as the player set it (GameSettings.scopeSensitivities).</summary>
[Serializable]
public struct ScopeSensitivity
{
    [Tooltip("The sight's magnification (GunParts.csv magnification).")]
    public float magnification;
    [Tooltip("1 = a target crosses the screen as fast as it does unaimed.")]
    public float sensitivity;
}

/// <summary>
/// The mouse through a sight that magnifies the whole view (GunParts.csv view = zoom): a setting of the player's own for
/// each magnification the game's sights have, as shooters with scopes of several powers give one.
/// A setting of 1 turns the view by 1 / magnification of the mouse's turn, so a target crosses the screen as fast as it
/// does unaimed; the player takes it up or down from there for each power (user, 2026-11-18, asked whether the mouse
/// should slow by the magnification: "그것은 배율 별로 감도 설정을 따로 두면 해결될 문제라고 생각합니다"). Before that one
/// table number (Scopes.csv Look.zoomTurn) set a single rule for every scope and every player.
/// The magnifications are read from the parts table, not listed here: a mod's sight of a new power gets its row in the
/// settings by being in GunParts.csv. A power with no saved setting has the table's default (Scopes.csv Look.sensitivity).
/// Sights looked through as they are (iron sights, dots, holographs) and the picture-in-picture kind never slow the mouse.
/// </summary>
public static class ScopeLook
{
    /// <summary>The setting's range: a tenth of the matched speed up to four times it (a 4x scope turned as fast as
    /// the hip's view).</summary>
    public const float Least = .1f, Most = 4f;

    private static float[] magnifications;
    private static int readFor = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        magnifications = null;
        readFor = -1;
    }

    /// <summary>The sensitivity of a magnification nobody has set (Scopes.csv Look.sensitivity).</summary>
    public static float Default => Mathf.Clamp(GameTables.Number(GunScope.TableName, "Look", "sensitivity", 1f), Least, Most);

    /// <summary>
    /// The magnifications the game's sights have, ascending: every sight of GunParts.csv that magnifies the whole view.
    /// Read again after the tables are (a mod laid over them).
    /// </summary>
    public static IReadOnlyList<float> Magnifications
    {
        get
        {
            if (magnifications != null && readFor == GunMounts.Version) return magnifications;
            var found = new List<float>();
            foreach (string id in GameTables.RowIds(GunMounts.PartsTable))
            {
                var part = GunMounts.PartOf(id);
                if (!part.Zooms) continue;
                float power = Key(part.Magnification);
                if (!found.Contains(power)) found.Add(power);
            }
            found.Sort();
            magnifications = found.ToArray();
            // After the rows were read: reading one is what starts GunMounts counting the tables' reloads.
            readFor = GunMounts.Version;
            return magnifications;
        }
    }

    /// <summary>The player's sensitivity for <paramref name="magnification"/>; the default where none was set.</summary>
    public static float Of(GameSettings settings, float magnification)
    {
        float power = Key(magnification);
        var saved = settings != null ? settings.scopeSensitivities : null;
        if (saved != null)
            foreach (var one in saved)
                if (Mathf.Abs(one.magnification - power) < .005f) return Mathf.Clamp(one.sensitivity, Least, Most);
        return Default;
    }

    /// <summary>Sets the player's sensitivity for <paramref name="magnification"/> (the settings menu's row).</summary>
    public static void Set(GameSettings settings, float magnification, float sensitivity)
    {
        if (settings == null) return;
        settings.scopeSensitivities ??= new List<ScopeSensitivity>();
        float power = Key(magnification);
        var entry = new ScopeSensitivity { magnification = power, sensitivity = Mathf.Clamp(sensitivity, Least, Most) };
        for (int i = 0; i < settings.scopeSensitivities.Count; i++)
        {
            if (Mathf.Abs(settings.scopeSensitivities[i].magnification - power) >= .005f) continue;
            settings.scopeSensitivities[i] = entry;
            return;
        }
        settings.scopeSensitivities.Add(entry);
    }

    /// <summary>
    /// How much of the mouse's turn the view takes fully aimed through <paramref name="sight"/>: 1 for a sight that does
    /// not magnify the whole view, else the player's setting for its power over that power.
    /// </summary>
    public static float Turn(in GunMounts.Part sight, GameSettings settings)
        => sight.Zooms ? Of(settings, sight.Magnification) / Mathf.Max(1f, sight.Magnification) : 1f;

    // A power as it is kept and compared: to a hundredth.
    private static float Key(float magnification) => Mathf.Round(Mathf.Max(1f, magnification) * 100f) / 100f;
}
