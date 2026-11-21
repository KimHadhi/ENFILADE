// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

using UnityEngine;

namespace Sonity.Internal {

    public abstract class SoundPolyGroupBase : ScriptableObject {

        public SoundPolyGroupInternals internals = new SoundPolyGroupInternals();
    }
}