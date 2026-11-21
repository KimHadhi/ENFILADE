// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

using System;

namespace Sonity.Internal {

    [Serializable]
    public abstract class SoundPickerBase {

        public SoundPickerInternals internals = new SoundPickerInternals();
    }
}