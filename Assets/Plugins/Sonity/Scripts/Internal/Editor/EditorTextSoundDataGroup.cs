// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

#if UNITY_EDITOR

namespace Sonity.Internal {

    public class EditorTextSoundDataGroup {

        public static readonly string soundDataGroupTooltip =
            $"{NameOf.SoundDataGroup} objects are used to easily load and unload the audio data of the {NameOf.SoundEvent}s." + "\n" +
            "\n" +
            $"All {NameOf.SoundDataGroup} objects are multi-object editable." + EditorTrial.trialTooltip;

        public static readonly string childSoundDataGroupsLabel = $"Child {NameOf.SoundDataGroup}s";
        public static readonly string childSoundDataGroupsTooltip = $"Nesting {NameOf.SoundDataGroup}s makes it easy to load/unload all audio data or just parts of it." + EditorTrial.trialTooltip;

        public static readonly string soundEventsLabel = $"{NameOf.SoundEvent}s";
        public static readonly string soundEventsTooltip = $"The {NameOf.SoundEvent} whoms audio data will be loaded or unloaded." + EditorTrial.trialTooltip;
    }
}
#endif