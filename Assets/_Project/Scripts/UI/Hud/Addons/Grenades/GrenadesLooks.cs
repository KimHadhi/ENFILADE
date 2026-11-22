using System;
using UnityEngine;

namespace HudAddons
{
    /// <summary>
    /// Where the Grenades module's numbers are: Tables/Hud/AddonGrenades.csv, rows Row.field, every one read off the two
    /// addons (cl_quicknade.lua, sh_quicknade.lua and the quick grenade's viewmodel c_eq_frag.mdl; cl_grenade_indicator.lua
    /// and ugi/holo.vmt). The file is not called Grenades.csv: the game's throwables already own that table name
    /// (Tables/Grenades/Grenades.csv), and tables are found by file name alone.
    /// </summary>
    internal static class GrenadesTable
    {
        public const string Name = "AddonGrenades";
        public const string RingRow = "Ring", FragRow = "Frag", ArcticRow = "Arctic", HoloRow = "Holo", SoundRow = "Sound";

        /// <summary>One Source unit in metres (an inch). The addons' distances stay written in units, as in their Lua.</summary>
        public const float Unit = .0254f;

        /// <summary>A Garry's Mod colour written 0-255 (the table's "r g b") as a Unity colour.</summary>
        public static Color Rgb(Vector3 rgb) => new Color(rgb.x / 255f, rgb.y / 255f, rgb.z / 255f, 1f);
    }

    /// <summary>
    /// MW Quick Grenade's fuse ring round the crosshair, Indicator:DrawTimer (cl_quicknade.lua): its circle is DrawArc(x, y,
    /// radius - radius·share, thickness, 0, 360, segments) in vgui/white, share = the time held over the fuse.
    /// </summary>
    [Serializable]
    internal sealed class GrenadeRingLook
    {
        /// <summary>The arc's inner radius at the pin and how far it closes as the fuse burns (px), its width outward, its sides.</summary>
        public float radius = 40f, thickness = 2f;
        public int segments = 30;
        /// <summary>Shown once held this long (s); turns red this long before the fuse ends (s).</summary>
        public float showAfter = .5f, redLead = 1.5f;
        /// <summary>The Lerp(FrameTime() / t) times it comes up, goes and changes colour by (s), and the strength it snaps off under.</summary>
        public float fadeIn = .3f, fadeOut = .1f, colorTime = .3f, gone = .014f;
        public Vector3 color = new Vector3(255f, 255f, 255f), lateColor = new Vector3(255f, 0f, 0f);
    }

    /// <summary>MW Quick Grenade's incoming frag indicator, Indicator:DrawFrag (cl_quicknade.lua).</summary>
    [Serializable]
    internal sealed class GrenadeFragLook
    {
        /// <summary>vgui/frag's side (ScreenScale) and vgui/frag_arrow's as a multiple of it; the circle they sit on (ScreenScale).</summary>
        public float size = 15f, arrow = 1.5f, radius = 72f;
        /// <summary>Shown within this many units of the feet (ply:GetPos()), red within near.</summary>
        public float reach = 300f, near = 250f;
        /// <summary>The Lerp(FrameTime() / t) time place, strength and colour follow by (s); a burst one's record goes under gone.</summary>
        public float time = .1f, gone = .1f;
        public Vector3 color = new Vector3(255f, 255f, 255f), nearColor = new Vector3(255f, 0f, 0f);
    }

    /// <summary>Arctic's Grenade Indicators, the triangle (cl_grenade_indicator.lua, cam.Start2D part) and the range both parts look in.</summary>
    [Serializable]
    internal sealed class GrenadeArcticLook
    {
        /// <summary>ents.FindInSphere(EyePos(), range) (units).</summary>
        public float range = 512f;
        /// <summary>ugi/triangle.png's side and the circle it sits on (ScreenScale).</summary>
        public float size = 10f, radius = 64f;
        public Vector3 color = new Vector3(255f, 0f, 0f);
        /// <summary>
        /// Its alpha is 255·(pulse + floor). The original's pulse was clamp(sin(CurTime()·15), 0, 1): on for half of every
        /// 0.42 s and off for the other half, a 2.4 Hz blink. The flicker rule (no strobe, smooth pulses of at most 1.6 Hz)
        /// makes it a smooth rise and fall at pulseHz between the same ends.
        /// </summary>
        public float floor = .1f, pulseHz = 1.6f;
    }

    /// <summary>Arctic's Grenade Indicators, the hologram (cl_grenade_indicator.lua, cam.Start3D part; ugi/holo.vmt).</summary>
    [Serializable]
    internal sealed class GrenadeHoloLook
    {
        /// <summary>cam.Start3D(nil, nil, fov, 0, 0, nil, nil, near, far): Source's fov (the width of a 4:3 screen, °), near and far (units).</summary>
        public float fov = 100f, near = 2f, far = 1024f;
        /// <summary>EyePos() + forward·ahead + up·y·up·d + right·x·side·d (units; dist_y and dist_x in the Lua).</summary>
        public float ahead = 90f, up = 48f, side = 64f;
        /// <summary>d = (clamp(distance, 0, range) / range + distAdd) · distScale.</summary>
        public float distAdd = .25f, distScale = .75f;
        /// <summary>SetAngles(Angle(pitch, CurTime()·spin, 0)): its pitch (°) and its turn (° a second).</summary>
        public float pitch = 30f, spin = 40f;
        /// <summary>render.SetColorModulation (0-1) and render.SetBlend.</summary>
        public Vector3 color = new Vector3(1f, .65f, 0f);
        public float blend = .5f;
        /// <summary>
        /// The frames of ugi/holo's static (5) and how many it moves through a second. The original's AnimatedTexture proxy
        /// cut to the next frame 20 times a second; under the flicker rule each frame fades into the next instead, at most
        /// 1.6 a second.
        /// </summary>
        public int frames = 5;
        public float frameRate = 1.6f;
    }

    /// <summary>
    /// When the quick grenade's viewmodel plays its sounds: its two sequences' sound events (c_eq_frag.mdl, event 5004
    /// AE_CL_PLAYSOUND), as frames of the sequence at its frame rate and playback rate (weapon_quicknade.lua Deploy plays
    /// ACT_VM_PULLPIN at 1.25; sv_quicknade.lua ThrowGrenade plays "throw" at 1 and lets the grenade go 0.3 s in).
    /// </summary>
    [Serializable]
    internal sealed class QuickNadeSoundTimes
    {
        /// <summary>quick_pullout: 27 frames at 30 fps, played at 1.25. Viewmodel.Small at frame 5, the pin (QuickNade.PinPull
        /// and QuickNade.PinPull.Cloth) at 10, Viewmodel.Small at 12 and 21.</summary>
        public float pullFps = 30f, pullRate = 1.25f, pullRattle = 5f, pullPin = 10f, pullRattle2 = 12f, pullRattle3 = 21f;
        /// <summary>throw: 21 frames at 30 fps, played at 1. Viewmodel.Medium at frame 0, QuickNade.Throw at 6, Viewmodel.Medium
        /// at 10; the grenade leaves the hand releaseAt seconds in.</summary>
        public float throwFps = 30f, throwRate = 1f, throwRattle = 0f, throwWhoosh = 6f, throwRattle2 = 10f, releaseAt = .3f;
    }
}
