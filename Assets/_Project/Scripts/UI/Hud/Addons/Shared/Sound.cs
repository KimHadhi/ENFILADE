using UnityEngine;

namespace HudAddons
{
    /// <summary>
    /// The addons' sounds (surface.PlaySound, sound.Add / EmitSound, sound.PlayFile) as 2D sounds on the confirm sounds'
    /// voices (FeelAudio.PlayClip). They come from one sound bank, HudAddons, built from Art/Audio/Addons: a folder
    /// Art/Audio/Addons/&lt;Module&gt;/&lt;Key&gt;/ holding the clip(s) is the key "&lt;Module&gt;/&lt;Key&gt;" (several clips: one is
    /// picked at random, never the same twice running, like the addons' sound tables). So each module (PlayerHud,
    /// HitFeedback, EnemyBars, Grenades, DeathScreen) adds its own folders and nothing shared changes; the editor rebuilds
    /// the bank on import (SoundBankBuilder).
    /// The addon's own numbers (its sound script's pitch range and volume) are passed in as they are; a Sounds.csv row
    /// "HudAddons/&lt;Module&gt;/&lt;Key&gt;" can then fit a sound into the game's mix (its volume and pitch multiply the addon's;
    /// no row is 1 and 1-1).
    /// </summary>
    public static class Sound
    {
        /// <summary>The bank's id: Resources/Audio/HudAddons, rows "HudAddons/..." of Sounds.csv.</summary>
        public const string BankId = "HudAddons";

        /// <summary>The folder the bank is built from.</summary>
        public const string Folder = "Assets/_Project/Art/Audio/Addons";

        private static SoundBank bank;
        private static bool looked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            bank = null;
            looked = false;
        }

        /// <summary>Loads the bank now. A bank loads every clip it holds the first time it is asked, so this runs when the
        /// HUD comes up (Boot.Install) rather than on the first hit marker of a fight.</summary>
        public static void Prepare()
        {
            if (looked || !Application.isPlaying) return;
            bank = SoundBank.Load(BankId);
            looked = true;
        }

        /// <summary>
        /// Plays the key once. Pitch is a fraction (Garry's Mod's pitch 100 = 1; sound.Add's {80, 110} = 0.8, 1.1), drawn
        /// evenly between the two; volume is the addon's (1 = full). False when the bank has no such key yet.
        /// </summary>
        public static bool Play(string key, float pitchMin = 1f, float pitchMax = 1f, float volume = 1f)
        {
            if (!Application.isPlaying || volume <= 0f || string.IsNullOrEmpty(key)) return false;
            Prepare();
            var clip = bank != null ? bank.Pick(key) : null;
            if (clip == null) return false;
            var mix = bank.SettingsOf(key);
            float pitch = pitchMax > pitchMin ? Random.Range(pitchMin, pitchMax) : pitchMin;
            FeelAudio.PlayClip(clip, volume * mix.Volume, pitch * mix.RandomPitch());
            return true;
        }
    }
}
