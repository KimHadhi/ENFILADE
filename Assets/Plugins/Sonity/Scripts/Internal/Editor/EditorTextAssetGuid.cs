// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

#if UNITY_EDITOR

namespace Sonity.Internal {

    public static class EditorTextAssetGuid {

        public static readonly string sonityGuidEditorPropertyName = $"sonityGuidEditor";

        public static readonly string sonityGuidLabel = $"Sonity GUID";
        public static readonly string sonityGuidTooltip =
        $"Sonity uses cached GUIDs for object comparison." + "\n" +
        "\n" +
        $"This solves problems where object equality isn't always the same when using e.g. addressables." + "\n" +
        "\n" +
        $"The Sonity AssetPostprocessor automatically saves the GUIDs for objects." + "\n" +
        "\n" +
        $"If you have any missing GUIDs (when upgrading) you can run:" + "\n" +
        "\n" +
        $"\"Tools/Sonity 🔊/Tools/Reserialize - All Sonity Assets\"." + EditorTrial.trialTooltip;
    }
}
#endif