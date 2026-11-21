// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

#if !SONITY_DISABLE_VOICE_EFFECTS
using System;

namespace Sonity.Internal {

    [Serializable]
    public class SoundManagerVoiceEffectPool {

        [NonSerialized]
        public Voice[] voiceEffects = new Voice[0];

        public bool VoiceEffectShouldPlay(Voice voice) {
            // Resize Array if voice effect limit has changed
            int voiceEffectLimit = SoundManagerBase.Instance.Internals.settings.voiceEffectLimit;
            if (voiceEffects.Length != voiceEffectLimit) {
                if (voiceEffects.Length < voiceEffectLimit) {
                    // If there are too few
                    Array.Resize(ref voiceEffects, voiceEffectLimit);
                } else {
                    // If there are too many
                    for (int i = 0; i < voiceEffectLimit - voiceEffects.Length; i++) {
                        // Disable them before removing them
                        if (voiceEffects[voiceEffectLimit + i] != null) {
                            voiceEffects[voiceEffectLimit + i].cachedVoiceEffect.SetEnabled(false);
                        }
                    }
                    Array.Resize(ref voiceEffects, voiceEffectLimit);
                }
            }

            for (int i = 0; i < voiceEffects.Length; i++) {
                // If already contains itself
                if (voiceEffects[i] == voice) {
                    return true;
                }
            }

            for (int i = 0; i < voiceEffects.Length; i++) {
                // If slot is null
                if (voiceEffects[i] == null) {
                    voiceEffects[i] = voice;
                    return true;
                }
            }
            
            for (int i = 0; i < voiceEffects.Length; i++) {
                // If not enabled
                if (!voiceEffects[i].cachedVoiceEffect.GetEnabled()) {
                    voiceEffects[i] = voice;
                    return true;
                }
            }

            for (int i = 0; i < voiceEffects.Length; i++) {
                // If volume is lower than new voice
                if (!voiceEffects[i].soundContainer.internals.data.neverStealVoiceEffects 
                    && voiceEffects[i].GetVolumeRatioWithoutFadeWithPriority() < voice.GetVolumeRatioWithoutFadeWithPriority()
                    ) {
                    // Steal voice effect
                    voiceEffects[i].cachedVoiceEffect.SetEnabled(false);
                    voiceEffects[i] = voice;
                    return true;
                }
            }
            return false;
        }
    }
}
#endif