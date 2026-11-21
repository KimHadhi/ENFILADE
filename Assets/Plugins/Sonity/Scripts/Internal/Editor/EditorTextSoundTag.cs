// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

#if UNITY_EDITOR

namespace Sonity.Internal {

    public static class EditorTextSoundTag {

        public static readonly string soundTagTooltip =
        $"{NameOf.SoundTag} objects are passed to modify how a {NameOf.SoundEvent} should be played." + "\n" +
        "\n" +
        $"You can assign them in the {NameOf.SoundEvent}s {NameOf.SoundTag} section." + "\n" +
        "\n" +
        $"You can either pass them when playing a {NameOf.SoundEvent} for setting the local {NameOf.SoundTag}." + "\n" +
        "\n" +
        $"Or you can set the global {NameOf.SoundTag} in the {NameOf.SoundManager}." + "\n" +
        "\n" +
        $"This is useful for e.g; weapon reverb zones." + "\n" +
        "\n" +
        $"Because you can set the {NameOf.SoundTag} corresponding to the acoustic space which the listener is in." + "\n" +
        "\n" +
        $"And when you play the {NameOf.SoundEvent}, your gun reflection layers can correspond to the acoustic space." + EditorTrial.trialTooltip;
    }
}
#endif