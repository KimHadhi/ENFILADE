using System;
using UnityEngine;

namespace HudAddons
{
    /// <summary>What the HUD settings offer, one a page (HudSettingsView; in its order).</summary>
    public enum HudPart
    {
        Whole, Colour, Fx, Motion,
        Portrait, Health, Armor, Stims, ClassUi, Hazards,
        Ammo, Grenade, Slots, History,
        Marker, Score, Skulls, EnemyBar, Numbers, Incoming,
        Sense, Flare, Death,
        Alert,
    }

    /// <summary>
    /// The HUD settings' stage (the user, 2026-11-22: "hud 설정하는 창 글만 보고선 이해할 수가 없는데 직관적이고 바로 볼 수
    /// 있게"): while it is up the HUD itself draws, whole and in its place, with sample numbers instead of the player's, so
    /// a change shows at once on the thing it changes, in a game or on the title screen. The part being set moves as it
    /// does in play (health falls and comes back, rounds are fired, a hit comes in), the fight's parts play one short
    /// fight over and over, the rest stands still. Every part says where it drew itself (<see cref="Mark"/>), which is
    /// where the settings let it be pressed and dragged.
    /// <para>Only what is drawn is sampled: nothing of the game is read or written, nothing is counted, nothing sounds.
    /// HudSettingsView opens it, steps it once a frame before the HUD draws, and closes it.</para>
    /// </summary>
    public static class HudStage
    {
        /// <summary>The short fight's length (s): hits, then kills; the kill chain ends with it.</summary>
        public const float FightSeconds = 3.6f;
        // Over the title screen's canvases, under the settings' (1000).
        private const int Lift = 900;
        /// <summary>Where the picture behind the stage goes on the title screen: under the raised overlays, over the title.</summary>
        public const int BackdropOrder = Lift - 1;

        private static readonly Rect[] rects = new Rect[Enum.GetValues(typeof(HudPart)).Length];
        private static readonly int[] markedAt = new int[rects.Length];
        private static float since, before;
        private static HudPart chosen;

        public static bool On { get; private set; }

        /// <summary>The part being set; choosing one starts its sample over.</summary>
        public static HudPart Chosen
        {
            get => chosen;
            set
            {
                if (chosen == value) return;
                chosen = value;
                since = before = 0f;
            }
        }

        /// <summary>The screen's width in the HUD's units (as Overlay works it out).</summary>
        public static float Width => Screen.width / Mathf.Max(1e-4f, Mathf.Sqrt(Screen.width / 1920f * (Screen.height / 1080f)));

        /// <summary>The sample numbers: shares of full, counts.</summary>
        public static float Health { get; private set; }
        public static float Armor { get; private set; }
        public static float Charge { get; private set; }
        public static int Clip { get; private set; }
        public static int Reserve { get; private set; }
        public static int Grenades { get; private set; }
        public static int Doses { get; private set; }
        public const int Magazine = 30;

        /// <summary>Until when the HUD sways as if the view turned (its sway was just changed), and when hiding was turned
        /// on (the bars then fade as they would, and come back).</summary>
        public static float SwayUntil { get; set; }
        public static float HideShownAt { get; set; } = float.NegativeInfinity;

        /// <summary>The fight's parts are being set: the short fight plays.</summary>
        public static bool Fight => On && (chosen == HudPart.Marker || chosen == HudPart.Score || chosen == HudPart.Skulls || chosen == HudPart.EnemyBar);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            On = false;
            chosen = HudPart.Whole;
            since = before = 0f;
            SwayUntil = 0f;
            HideShownAt = float.NegativeInfinity;
            Array.Clear(markedAt, 0, markedAt.Length);
        }

        /// <summary>Brings the stage up. On the title screen, where no HUD is, the modules come up for it and draw over the
        /// title's canvases; in a game they stay where they are.</summary>
        public static void Open(HudPart part)
        {
            if (!Application.isPlaying || On) return;
            On = true;
            chosen = part;
            since = before = 0f;
            Rest();
            PlayerHudModule.Install();
            HitFeedbackModule.Install();
            EnemyBarsModule.Install();
            GrenadesModule.Install();
            if (PlayerRoster.Local == null) Overlay.Raise(Lift);
        }

        public static void Close()
        {
            if (!On) return;
            On = false;
            Overlay.Raise(0);
        }

        /// <summary>One frame of the samples, before the HUD draws.</summary>
        public static void Step()
        {
            if (!On) return;
            before = since;
            since += Smooth.Dt;
            Rest();
            switch (chosen)
            {
                case HudPart.Health:
                    // Three hits down to the red, then a heal, then back to rest.
                    float h = Mathf.Repeat(since, 6f);
                    Health = h < .6f ? .78f : h < 1.3f ? .56f : h < 2.2f ? .38f : h < 3.8f ? .2f : h < 4.8f ? 1f : .78f;
                    break;
                case HudPart.Armor:
                    float a = Mathf.Repeat(since, 5f);
                    Armor = a < .6f ? .62f : a < 1.4f ? .35f : a < 2.6f ? .08f : a < 4f ? 1f : .62f;
                    break;
                case HudPart.Stims:
                    Doses = 3 - Mathf.FloorToInt(Mathf.Repeat(since, 4f));
                    break;
                case HudPart.Grenade:
                    Grenades = 2 - Mathf.FloorToInt(Mathf.Repeat(since, 3f));
                    break;
                case HudPart.Ammo:
                    // A burst, a pause, the rest of the magazine, then a fresh one out of the reserve.
                    float s = Mathf.Repeat(since, 5f);
                    int fired = Mathf.Min(8, Mathf.FloorToInt(s / .1f)) + Mathf.Clamp(Mathf.FloorToInt((s - 1.6f) / .1f), 0, 16);
                    Clip = s > 4.2f ? Magazine : Mathf.Max(0, Magazine - fired);
                    Reserve = s > 4.2f ? 96 : 120;
                    break;
                case HudPart.Portrait:
                    // The magazine fills past one use and is spent.
                    float c = Mathf.Repeat(since, 7f);
                    Charge = c < 4.6f ? Mathf.Lerp(.3f, 1.4f, c / 4.6f) : .4f;
                    break;
            }
        }

        // What stands still.
        private static void Rest()
        {
            Health = .78f;
            Armor = .62f;
            Charge = .7f;
            Clip = 22;
            Reserve = 120;
            Grenades = 2;
            Doses = 3;
        }

        /// <summary>The moment <paramref name="at"/> of every <paramref name="period"/> seconds passed this frame (in the time
        /// since the part was chosen).</summary>
        public static bool At(float period, float at)
        {
            if (!On || since <= before) return false;
            return Mathf.FloorToInt((since - at) / period) != Mathf.FloorToInt((before - at) / period) && since >= at;
        }

        /// <summary>How far into the short fight it is (s).</summary>
        public static float FightTime => Mathf.Repeat(since, FightSeconds);

        /// <summary>A part drew itself here this frame (the overlay's units).</summary>
        public static void Mark(HudPart part, float x, float y, float w, float h)
        {
            if (!On) return;
            rects[(int)part] = new Rect(x, y, w, h);
            markedAt[(int)part] = Time.frameCount;
        }

        /// <summary>Where the part drew itself this frame or the last; false while it isn't drawn.</summary>
        public static bool TryRect(HudPart part, out Rect rect)
        {
            rect = rects[(int)part];
            return On && markedAt[(int)part] >= Time.frameCount - 1 && markedAt[(int)part] > 0;
        }
    }
}
