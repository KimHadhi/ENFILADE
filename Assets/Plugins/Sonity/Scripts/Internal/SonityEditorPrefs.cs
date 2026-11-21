// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

#if UNITY_EDITOR

using UnityEditor;

namespace Sonity.Internal {

    public static class SonityEditorPrefs {

        // EditorPrefs settings will be same in all Sonity projects on the same computer

        private static readonly string sonitySoundManagerDebugEnableKey = "SonitySoundManagerDebugEnable";
        public static bool SonitySoundManagerDebugEnable {
            get => EditorPrefs.GetBool(sonitySoundManagerDebugEnableKey, false);
            set => EditorPrefs.SetBool(sonitySoundManagerDebugEnableKey, value);
        }

        private static readonly string sonityLegacyMissingSonityGuidsKey = "SonityLegacyMissingSonityGuids";
        public static bool SonityLegacyMissingSonityGuids {
            get => EditorPrefs.GetBool(sonityLegacyMissingSonityGuidsKey, false);
            set => EditorPrefs.SetBool(sonityLegacyMissingSonityGuidsKey, value);
        }
    }
}
#endif