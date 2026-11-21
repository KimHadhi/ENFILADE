// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

using UnityEngine;

namespace Sonity.Internal {

    public static class PitchScale {

        private static readonly float pitchSemitonesToRatioScale = Mathf.Pow(2f, 1f / 12f);
        private static readonly float pitchRatioToSemitonesScale = Mathf.Log(2f, 2f);

        public static float SemitonesToRatio(float pitchSemitones) {
            return Mathf.Pow(pitchSemitonesToRatioScale, pitchSemitones);
        }

        public static float RatioToSemitones(float pitchRatio) {
            return 12f * Mathf.Log(pitchRatio, 2f) / pitchRatioToSemitonesScale;
        }
    }
}