// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

using UnityEngine;
using System;

namespace Sonity.Internal {

    [Serializable]
    public abstract class SoundTagBase : ScriptableObject {

        public SoundTagInternals internals = new SoundTagInternals();
    }
}