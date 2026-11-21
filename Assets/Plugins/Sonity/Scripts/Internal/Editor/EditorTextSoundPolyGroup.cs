// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

#if UNITY_EDITOR

namespace Sonity.Internal {

    public class EditorTextSoundPolyGroup {

        public static readonly string soundPolyGroupTooltip =
            $"{NameOf.SoundPolyGroup} objects are used to create a polyphony limit shared by multiple different {NameOf.SoundEvent}s." + "\n" +
            "\n" +
            $"You can assign them in the {NameOf.SoundEvent} settings." + "\n" +
            "\n" +
            $"The priority for voice allocation is calculated by multiplying the priority set in the {NameOf.SoundEvent} by the volume of the instance." + "\n" +
            "\n" +
            $"A perfect use case would be to have a {NameOf.SoundPolyGroup} for all bullet impacts of all the different materials so that when combined, they don’t use too many voices." + "\n" +
            "\n" +
            $"If you want simple individual polyphony control, use the polyphony modifier on the {NameOf.SoundEvent}." + "\n" +
            "\n" +
            $"All {NameOf.SoundPolyGroup} objects are multi-object editable." + EditorTrial.trialTooltip;

        public static readonly string polyphonyLimitLabel = "Polyphony Limit";
        public static readonly string polyphonyLimitTooltip = $"The maximum number of {NameOf.SoundEvent}s which can be played at the same time in this {NameOf.SoundPolyGroup}." + EditorTrial.trialTooltip;
    }
}
#endif