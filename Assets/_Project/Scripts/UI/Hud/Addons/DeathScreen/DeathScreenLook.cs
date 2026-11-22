using System;
using UnityEngine;

namespace HudAddons
{
    /// <summary>
    /// The MW3 Death Screen addon's constants (lua/autorun/mw3_deathscreen.lua), from Tables/Hud/AddonDeathScreen.csv rows
    /// Screen (its timetable) and Grade (its DrawColorModify numbers). The initialisers are the addon's own numbers, so a
    /// missing table changes nothing. The addon's five convars are the player's options instead (DeathScreenOptions).
    /// </summary>
    [Serializable]
    public sealed class DeathScreenLook
    {
        public const string TableName = "AddonDeathScreen", ScreenRow = "Screen", GradeRow = "Grade";

        [Tooltip("T_COLOR[1]: seconds after death before the colour starts to change.")]
        public float colourDelay;
        [Tooltip("T_COLOR[2]: seconds for the colour change to come in all the way (ease out).")]
        public float colourSeconds = 1.2f;
        [Tooltip("T_VIGNETTE[1]: seconds after death before the vignette picture starts to show.")]
        public float vignetteDelay;
        [Tooltip("T_VIGNETTE[2]: seconds for the vignette to come in all the way (ease out).")]
        public float vignetteSeconds = 1.6f;
        [Tooltip("T_BLUR[1]: seconds after death before the blur starts.")]
        public float blurDelay = .15f;
        [Tooltip("T_BLUR[2]: seconds for the blur to come in all the way (ease out).")]
        public float blurSeconds = 2.4f;
        [Tooltip("passes: the blur drawn this many times, each a 3x3 box (i / passes) x strength pixels wide.")]
        public int blurPasses = 4;
        [Tooltip("T_BLOOD_FADE: seconds for the blood picture to come in all the way (smoothstep); it starts at the blood delay option.")]
        public float bloodSeconds = 1.6f;
        [Tooltip("FADE_OUT_SCREEN: seconds the screen takes to clear, from wherever it had got, on coming back.")]
        public float clearSeconds = .3f;
        [Tooltip("FADE_OUT_SOUND: seconds the whistle takes to cut out on coming back (a tiny ramp, so it doesn't click).")]
        public float whistleCutSeconds = .06f;

        [Tooltip("$pp_colour_addr at the full change (screen value 0-1, grown by the colour level).")]
        public float addRed = .05f;
        [Tooltip("$pp_colour_addg at the full change.")]
        public float addGreen;
        [Tooltip("$pp_colour_addb at the full change.")]
        public float addBlue = -.015f;
        [Tooltip("$pp_colour_brightness at the full change: added to every channel.")]
        public float brightness = -.03f;
        [Tooltip("$pp_colour_contrast = 1 + this x the colour level: every channel multiplied by it.")]
        public float contrast = .08f;
        [Tooltip("$pp_colour_colour = 1 + this x the colour level: 0 is the three channels' mean grey, 1 leaves the colour.")]
        public float colour = -.42f;
        [Tooltip("$pp_colour_mulr: adds this x (how far red stands over green and blue)^2 to red. The addon's is 0.")]
        public float mulRed;
        [Tooltip("$pp_colour_mulg, as mulRed for green.")]
        public float mulGreen;
        [Tooltip("$pp_colour_mulb, as mulRed for blue.")]
        public float mulBlue;
    }
}
