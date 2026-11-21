// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

#if UNITY_EDITOR


namespace Sonity.Internal {

    public class EditorTextSoundPicker {

        public static readonly string soundPickerTooltip =
            $"{NameOf.SoundPicker} is a serializable class for easily selecting multiple {NameOf.SoundEvent}s and modifiers." + "\n" +
            "\n" +
            $"Add a serialized or public {NameOf.SoundPicker} to a C# script and edit it in the inspector." + "\n" +
            "\n" +
            $"The {NameOf.SoundPicker} is not nestable in an array or custom class because it is built upon CustomPropertyDrawer." + "\n" +
            "\n" +
            $"This is because custom serializable classes do not support inheritance and polymorphism for serialization." + "\n" +
            "\n" +
            $"{NameOf.SoundPicker} are multi-object editable." + EditorTrial.trialTooltip;

        public static readonly string soundEventLabel = $"{NameOf.SoundEvent}";
        public static readonly string soundEventTooltip = $"The {NameOf.SoundEvent} to play." + EditorTrial.trialTooltip;
    }
}
#endif