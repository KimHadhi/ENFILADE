// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

using UnityEngine;

namespace Sonity.Internal {

    public static class ApplicationQuitting {

        // Should be read and writeable
        private static bool applicationQuitting = false;

        // Needed for disabling domain reloading
#if UNITY_2019_2_OR_NEWER
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#else
        // Older versions than 2019.2 doesn't have the SubsystemRegistration load type
        [RuntimeInitializeOnLoadMethod]
#endif
        static void ResetStatics() {
            applicationQuitting = false;
            Application.quitting -= SetQuitting;
            Application.quitting += SetQuitting;
        }

        public static bool GetApplicationIsQuitting() {
            return applicationQuitting;
        }

        static void SetQuitting() {
            applicationQuitting = true;
        }
    }
}