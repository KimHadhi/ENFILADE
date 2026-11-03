using System;
using UnityEngine;

/// <summary>
/// One crosshair the player draws in settings, saved inside <see cref="GameSettings"/>. While <see cref="custom"/> is off
/// the designed profile shows as it is. Drawing starts from <see cref="Default"/>: a plain green shape. Whether the lines
/// follow weapon spread is <see cref="GameSettings.steadyCrosshair"/>, for both. Pixel sizes are 1920x1080 canvas units.
/// </summary>
[Serializable]
public sealed class CrosshairSettings
{
    [Tooltip("Off shows the designed crosshair; on shows this one.")]
    public bool custom;
    [Tooltip("Centre dot size; drawn at twice this in pixels. 0 hides the dot.")]
    public int dotSize = 1;
    [Tooltip("Round dot instead of a square one.")]
    public bool roundDot;
    [Tooltip("Lines around the centre. 0 hides them.")]
    public int lineCount = 4;
    [Tooltip("Lines drawn with the designed sprite instead of plain bars.")]
    public bool spriteLines;
    public float lineLength = 8f, lineThickness = 2f;
    [Tooltip("From the centre to the inner end of each line, before spread.")]
    public float gap = 4f;
    [Tooltip("Turns the whole shape, degrees. 45 makes a + into an x.")]
    public float angle;
    public bool spinning;
    [Tooltip("Spin in degrees per second while spinning.")]
    public float spin = 90f;
    public Color color = Color.green;
    public bool rainbow;
    [Tooltip("Rainbow colour cycles per second.")]
    public float rainbowSpeed = .5f;
    [Range(0f, 1f), Tooltip("Rainbow saturation. Lower is paler.")]
    public float rainbowSaturation = .45f;
    [Tooltip("Dark line around the dot and the lines, pixels. 0 turns it off.")]
    public float outline = 1f;
    [Tooltip("How solid the dark line is. The designed crosshairs use .6.")]
    public float outlineOpacity = .6f;

    public const int MaxDotSize = 10, MaxLines = 8;
    public const float MaxLineLength = 60f, MaxLineThickness = 20f, MaxGap = 60f, MaxAngle = 180f, MinSpin = 5f,
        MaxSpin = 360f, MaxOutline = 4f, MinOpacity = .1f, MinRainbowCycle = .5f, MaxRainbowCycle = 10f;

    /// <summary>Where drawing starts: a green cross for guns, a green dot for fists and melee weapons.</summary>
    public static CrosshairSettings Default(CrosshairKind kind) =>
        kind == CrosshairKind.Melee ? new CrosshairSettings { dotSize = 3, lineCount = 0 } : new CrosshairSettings();

    /// <summary>A separate copy. Every field is a value, so a shallow copy is a full one.</summary>
    public CrosshairSettings Clone() => (CrosshairSettings)MemberwiseClone();

    /// <summary>Writes this look into a runtime profile. What the menu does not offer comes from the design.</summary>
    public void WriteTo(CrosshairProfile profile, CrosshairProfile design)
    {
        profile.showDot = dotSize > 0;
        profile.dotSize = dotSize;
        profile.dotSprite = roundDot ? CrosshairShapes.Circle : null;
        profile.showParts = lineCount > 0;
        profile.partCount = lineCount;
        profile.partSprite = spriteLines && design != null ? design.partSprite : null;
        profile.partSize = new Vector2(lineThickness, lineLength);
        profile.baseGap = gap + lineLength * .5f;
        profile.baseAngleOffset = angle;
        profile.isRotating = spinning;
        profile.rotationSpeed = spin;
        profile.color = color;
        profile.isRainbow = rainbow;
        profile.rainbowSpeed = rainbowSpeed;
        profile.rainbowSaturation = rainbowSaturation;
        profile.outlineWidth = outline;
        var outlineColor = design != null ? design.outlineColor : Color.black;
        outlineColor.a = outlineOpacity;
        profile.outlineColor = outlineColor;
        if (design != null) profile.rainbowBrightness = design.rainbowBrightness;
    }

    public void Clamp()
    {
        dotSize = Mathf.Clamp(dotSize, 0, MaxDotSize);
        lineCount = Mathf.Clamp(lineCount, 0, MaxLines);
        lineLength = Mathf.Clamp(lineLength, 1f, MaxLineLength);
        lineThickness = Mathf.Clamp(lineThickness, 1f, MaxLineThickness);
        gap = Mathf.Clamp(gap, 0f, MaxGap);
        angle = Mathf.Clamp(angle, 0f, MaxAngle);
        spin = Mathf.Clamp(spin, MinSpin, MaxSpin);
        color = new Color(Mathf.Clamp01(color.r), Mathf.Clamp01(color.g), Mathf.Clamp01(color.b), Mathf.Clamp(color.a, MinOpacity, 1f));
        rainbowSpeed = Mathf.Clamp(rainbowSpeed, 1f / MaxRainbowCycle, 1f / MinRainbowCycle);
        rainbowSaturation = Mathf.Clamp01(rainbowSaturation);
        outline = Mathf.Clamp(outline, 0f, MaxOutline);
        outlineOpacity = Mathf.Clamp(outlineOpacity, MinOpacity, 1f);
    }
}
