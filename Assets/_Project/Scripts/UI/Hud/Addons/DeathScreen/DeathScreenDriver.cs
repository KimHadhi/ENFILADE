using UnityEngine;

namespace HudAddons
{
    /// <summary>
    /// The MW3 Death Screen addon's state (lua/autorun/mw3_deathscreen.lua): how far each part of the screen has come since
    /// death (its levels()), and its whistle. Its Think hook watched ply:Alive(); here the game's death flow
    /// (global::DeathScreen, which also runs our death card) says when through DeathScreenModule, so the two never
    /// disagree about a death. DeathScreenPass draws the levels into the world camera's picture.
    /// <para>Real time throughout (Smooth.Now, the addon's RealTime()): a pause menu over a death does not stop it.</para>
    /// <para>One per run, kept across scenes, made on first use.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DeathScreenDriver : MonoBehaviour
    {
        /// <summary>The whistle in the HudAddons sound bank: Art/Audio/Addons/DeathScreen/Whistle/whistle.mp3, the
        /// addon's sound/deathscreen/whistle.mp3 as it is.</summary>
        public const string WhistleKey = "DeathScreen/Whistle";

        // The addon skips a part at or under this level (its "<= 0.001" checks).
        private const float Visible = .001f;
        // Both whistle voices are started on one audio sample this far ahead (s), so the second only adds level.
        private const double StartLead = .05;
        // The most blur passes a table may ask for (the addon draws 4).
        private const int MaxBlurPasses = 16;

        /// <summary>What DeathScreenPass draws this frame.</summary>
        internal struct Frame
        {
            /// <summary>GMod's colour modify at the colour level: Add = ($pp_colour_addr, addg, addb); Modify =
            /// (brightness, contrast, colour); Mul = (mulr, mulg, mulb). At level 0 they leave the picture as it is.</summary>
            public Vector4 Add, Modify, Mul;
            /// <summary>The last blur pass's sample spacing in 1080p pixels (0: no blur), and how many passes.</summary>
            public float BlurPixels;
            public int BlurPasses;
            /// <summary>The vignette's and the blood's opacity, 0-1 in whole 1/255 steps (the addon's alphaByte).</summary>
            public float Vignette, Blood;
        }

        private static DeathScreenDriver instance;
        private static readonly DeathScreenOptions Fallback = new DeathScreenOptions();
        private static bool warnedWhistle;

        private readonly DeathScreenLook look = new DeathScreenLook();
        private AudioSource main, boost;
        private SoundBank bank;
        private bool bankLooked;
        // The addon's dead / previewUntil / deathStart / reviveAt / snapshot.
        private bool dead, previewing;
        private float diedAt, revivedAt = float.NegativeInfinity;
        private float fromColour, fromVignette, fromBlur, fromBlood;
        // The addon's soundFadeAt (fading while true), and the Sounds.csv row's volume taken at the start.
        private bool fading;
        private float fadeAt, mixVolume = 1f;
        // This frame's numbers, worked out once however many cameras ask.
        private int framed = -1;
        private bool hasFrame;
        private Frame frame;

        /// <summary>The addon's constants (Tables/Hud/AddonDeathScreen.csv).</summary>
        internal DeathScreenLook Look => look;

        /// <summary>The driver if one was made, else null (DeathScreenPass asks every camera; it never makes one).</summary>
        internal static DeathScreenDriver Existing => instance;

        private static DeathScreenOptions Options => GameSettings.Current.addonDeathScreen ?? Fallback;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            instance = null;
            warnedWhistle = false;
        }

        /// <summary>The driver, made (and kept across scenes) on first use. Null outside Play.</summary>
        internal static DeathScreenDriver Get()
        {
            if (instance != null || !Application.isPlaying) return instance;
            var host = new GameObject("Death Screen (Addon)");
            RuntimeSceneHierarchy.Keep(host, "UI");
            host.AddComponent<DeathScreenDriver>();
            return instance;
        }

        private void Awake()
        {
            if (instance != null && instance != this) { Destroy(gameObject); return; }
            instance = this;
            GameTables.BindNested(DeathScreenLook.TableName, DeathScreenLook.ScreenRow, this, look);
            GameTables.BindNested(DeathScreenLook.TableName, DeathScreenLook.GradeRow, this, look);
        }

        private void OnDisable() => KillWhistle();

        private void OnDestroy()
        {
            if (instance == this) instance = null;
        }

        /// <summary>Reads the whistle ahead of the first death, so it never waits on the file.</summary>
        internal void Prepare() => WhistleClip();

        /// <summary>
        /// onDeath: every part starts growing in from now by its own clock, and the whistle plays once. Again while
        /// showing, it starts over (the addon does that when a preview turns into a real death).
        /// </summary>
        internal void Die(bool preview)
        {
            dead = true;
            previewing = preview;
            diedAt = Smooth.Now;
            framed = -1;
            PlayWhistle();
        }

        /// <summary>
        /// onRespawn: the screen clears from wherever it had got over clearSeconds; with <paramref name="cutWhistle"/> the
        /// whistle is cut over whistleCutSeconds (the addon cuts it on coming back), without it the whistle plays out.
        /// </summary>
        internal void Respawn(bool cutWhistle)
        {
            if (!dead) return;
            float now = Smooth.Now;
            Levels(now, out fromColour, out fromVignette, out fromBlur, out fromBlood);
            dead = previewing = false;
            revivedAt = now;
            framed = -1;
            if (cutWhistle) StopWhistle(now);
        }

        /// <summary>Everything off at once, the whistle too (the addon's ShutDown hook and killWhistleNow).</summary>
        internal void Stop()
        {
            dead = previewing = false;
            fromColour = fromVignette = fromBlur = fromBlood = 0f;
            revivedAt = float.NegativeInfinity;
            framed = -1;
            KillWhistle();
        }

        /// <summary>This frame's numbers for the pass; false when there is nothing to draw (most frames: no work).</summary>
        internal bool TryFrame(out Frame result)
        {
            int now = Time.frameCount;
            if (now != framed)
            {
                framed = now;
                hasFrame = Compute(Smooth.Now, out frame);
            }
            result = frame;
            return hasFrame;
        }

        // The addon's Think hook, less the alive watch (DeathScreenModule's callers say when).
        private void Update()
        {
            // Switched off while dead, the screen clears as on coming back; a preview runs its time anyway.
            if (dead && !previewing && !Options.enabled) Respawn(true);
            if (!fading) return;
            if (!main.isPlaying && !boost.isPlaying)
            {
                fading = false;
                return;
            }
            float f = 1f - Ramp(Smooth.Now - fadeAt, look.whistleCutSeconds);
            if (f <= 0f) KillWhistle();
            // The fade reads the volume option as it is now, as the addon's Think reads cv_volume.
            else Gain(Options.volume * f);
        }

        // The addon's two hooks in one: RenderScreenspaceEffects (the colour) and HUDPaintBackground (blur, vignette,
        // blood), each with its own early-out, as numbers for the one pass that draws them all.
        private bool Compute(float now, out Frame result)
        {
            result = default;
            Levels(now, out float c, out float v, out float b, out float k);
            bool colour = c > Visible;
            result.Add = colour ? new Vector4(look.addRed * c, look.addGreen * c, look.addBlue * c, 0f) : Vector4.zero;
            result.Modify = colour ? new Vector4(look.brightness * c, 1f + look.contrast * c, 1f + look.colour * c, 0f) : new Vector4(0f, 1f, 1f, 0f);
            // The addon writes its mul numbers as plain 0; grown by the level like the rest, so a table that sets one
            // still starts from an untouched picture.
            result.Mul = colour ? new Vector4(look.mulRed * c, look.mulGreen * c, look.mulBlue * c, 0f) : Vector4.zero;
            if (Mathf.Max(v, Mathf.Max(b, k)) > Visible)
            {
                var o = Options;
                if (b > Visible && o.blur > 0f)
                {
                    result.BlurPixels = o.blur * b;
                    // Each pass is a full-screen draw: a mistyped table number must not turn into hundreds of them.
                    result.BlurPasses = Mathf.Clamp(look.blurPasses, 1, MaxBlurPasses);
                }
                if (v > Visible) result.Vignette = AlphaByte(v) / 255f;
                float bloodAmount = k * o.blood;
                if (bloodAmount > Visible) result.Blood = AlphaByte(bloodAmount) / 255f;
            }
            return colour || result.BlurPixels > 0f || result.Vignette > 0f || result.Blood > 0f;
        }

        // The addon's levels(): colour, vignette, blur, blood (0-1).
        private void Levels(float now, out float colour, out float vignette, out float blur, out float blood)
        {
            if (dead)
            {
                float t = now - diedAt;
                colour = EaseOut(t - look.colourDelay, look.colourSeconds);
                vignette = EaseOut(t - look.vignetteDelay, look.vignetteSeconds);
                blur = EaseOut(t - look.blurDelay, look.blurSeconds);
                blood = Smoothstep(t - Options.bloodDelay, look.bloodSeconds);
                return;
            }
            // Clearing: every part from where it had got, straight down together.
            float f = 1f - Ramp(now - revivedAt, look.clearSeconds);
            colour = fromColour * f;
            vignette = fromVignette * f;
            blur = fromBlur * f;
            blood = fromBlood * f;
        }

        // t / seconds held to 0-1; a time of 0 or less is a step at t = 0 (the addon's numbers are never 0).
        private static float Ramp(float t, float seconds) => seconds > 0f ? Mathf.Clamp01(t / seconds) : t >= 0f ? 1f : 0f;

        // The addon's easeOut: quick at first, settling at the end.
        private static float EaseOut(float t, float seconds)
        {
            float x = Ramp(t, seconds);
            return 1f - (1f - x) * (1f - x);
        }

        // The addon's smooth (smoothstep), for the blood only.
        private static float Smoothstep(float t, float seconds)
        {
            float x = Ramp(t, seconds);
            return x * x * (3f - 2f * x);
        }

        // The addon's alphaByte: a 0-1 level as the draw colour's whole 0-255 alpha, rounded.
        private static int AlphaByte(float x) => Mathf.FloorToInt(Mathf.Clamp01(x) * 255f + .5f);

        // ---- the whistle ----------------------------------------------------------------------------------------------

        // playWhistle: once, at the volume option (0 plays nothing). The addon's channel took volumes up to 2 (BASS
        // amplifies past 1) and an AudioSource stops at 1, so a second voice playing the same clip from the same sample
        // carries the part over 1: two in-phase copies add up to exactly that gain.
        private void PlayWhistle()
        {
            KillWhistle();
            float volume = Options.volume;
            if (volume <= 0f) return;
            var clip = WhistleClip();
            if (clip == null) return;
            if (main == null)
            {
                main = Voice();
                boost = Voice();
            }
            var mix = bank.SettingsOf(WhistleKey);
            mixVolume = mix.Volume;
            float pitch = mix.RandomPitch();
            double at = AudioSettings.dspTime + StartLead;
            Begin(main, clip, pitch, at);
            if (volume * mixVolume > 1f) Begin(boost, clip, pitch, at);
            Gain(volume);
        }

        // A 2D voice on the UI channel: it follows the UI volume, plays under the pause menu and stays clear of the
        // listener's filters, as GMod's sound.PlayFile channel stays clear of the game's DSP.
        private AudioSource Voice()
        {
            var voice = gameObject.AddComponent<AudioSource>();
            voice.playOnAwake = false;
            voice.loop = false;
            voice.spatialBlend = 0f;
            GameAudio.Bind(voice, AudioChannel.UI);
            return voice;
        }

        private static void Begin(AudioSource voice, AudioClip clip, float pitch, double at)
        {
            voice.clip = clip;
            voice.pitch = pitch;
            voice.PlayScheduled(at);
        }

        // The addon's channel volume (0-2) split over the two voices, under the Sounds.csv row's volume.
        private void Gain(float level)
        {
            float total = Mathf.Max(0f, level) * mixVolume;
            GameAudio.SetGain(main, Mathf.Min(total, 1f));
            GameAudio.SetGain(boost, Mathf.Clamp01(total - 1f));
        }

        // stopWhistle: Update ramps it down over whistleCutSeconds and stops it.
        private void StopWhistle(float now)
        {
            if (main == null || !main.isPlaying && !boost.isPlaying) return;
            fading = true;
            fadeAt = now;
        }

        // killWhistleNow.
        private void KillWhistle()
        {
            fading = false;
            if (main == null) return;
            main.Stop();
            boost.Stop();
        }

        private AudioClip WhistleClip()
        {
            if (!bankLooked)
            {
                bankLooked = true;
                bank = SoundBank.Load(Sound.BankId);
            }
            var clip = bank != null ? bank.Pick(WhistleKey) : null;
            if (clip == null)
            {
                if (!warnedWhistle) Debug.LogWarning($"[HudAddons] DeathScreen: no '{WhistleKey}' in the {Sound.BankId} sound bank (Art/Audio/Addons/DeathScreen/Whistle).");
                warnedWhistle = true;
                return null;
            }
            // Read ahead, so the first death never waits on the file.
            if (clip.loadState == AudioDataLoadState.Unloaded) clip.LoadAudioData();
            return clip;
        }
    }
}
