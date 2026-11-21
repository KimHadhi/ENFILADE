// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

using UnityEngine;

namespace Sonity.Internal {

    public static class SoundTimeScale {

        // Should be read and writeable
        private static float sonityTime = 0f;
        private static float sonityDeltaTime = 0f;

        // Needed for disabling domain reloading
#if UNITY_2019_2_OR_NEWER
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#else
        // Older versions than 2019.2 doesn't have the SubsystemRegistration load type
        [RuntimeInitializeOnLoadMethod]
#endif
        static void ResetStatics() {
            // SoundTimeScale needs to reset before Awake() for fadeins etc, so don't use RuntimeInitializeLoadType.AfterSceneLoad here.
            sonityTime = 0f;
            sonityDeltaTime = 0f;
        }

        public static float GetTimeEditor() {
            // The other time scales doesnt work in the editor
            return Time.realtimeSinceStartup;
        }

        public static float GetTimeRuntime() {
            return sonityTime;
        }

        public static float GetDeltaTimeRuntime() {
            return sonityDeltaTime;
        }

        public static void UpdateTime(SoundTimeScaleMode soundTimeScale) {
            switch (soundTimeScale) {
                case SoundTimeScaleMode.UnscaledTime:
                    sonityTime = Time.unscaledTime;
                    sonityDeltaTime = Time.unscaledDeltaTime;
                    break;
                case SoundTimeScaleMode.Time:
                    sonityTime = Time.time;
                    sonityDeltaTime = Time.deltaTime;
                    break;
                case SoundTimeScaleMode.FixedUnscaledTime:
                    sonityTime = Time.fixedUnscaledTime;
                    sonityDeltaTime = Time.fixedUnscaledDeltaTime;
                    break;
                case SoundTimeScaleMode.FixedTime:
                    sonityTime = Time.fixedTime;
                    sonityDeltaTime = Time.fixedDeltaTime;
                    break;
                case SoundTimeScaleMode.RealtimeSinceStartup:
                    // Doesnt have an equivalent counterpart
                    sonityDeltaTime = Time.realtimeSinceStartup - sonityTime;
                    sonityTime = Time.realtimeSinceStartup;
                    break;
            }
        }
    }
}