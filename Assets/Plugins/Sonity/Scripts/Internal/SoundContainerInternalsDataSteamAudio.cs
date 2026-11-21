// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

using UnityEngine;
using System;

namespace Sonity.Internal {

    // Sonity Steam Audio

    [Serializable]
    public class SoundContainerInternalsDataSteamAudio {

        public bool steamAudioExpand = false;

        // For this part of the script copied and modified from Steam Audio there is a different license:
        // Copyright 2017-2023 Valve Corporation.
        // Licensed under the Apache License, Version 2.0 (the "License");
        // you may not use this file except in compliance with the License.
        // You may obtain a copy of the License at
        // http://www.apache.org/licenses/LICENSE-2.0
        // Unless required by applicable law or agreed to in writing, software
        // distributed under the License is distributed on an "AS IS" BASIS,
        // WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
        // See the License for the specific language governing permissions and
        // limitations under the License.

        // Copied from SteamAudioSourceInspector
        public bool directBinaural = true;
        public HRTFInterpolation interpolation = HRTFInterpolation.Nearest;
        public bool perspectiveCorrection = false;

        public bool distanceAttenuation = false;
        public DistanceAttenuationInput distanceAttenuationInput = DistanceAttenuationInput.CurveDriven;
        public float distanceAttenuationValue = 1.0f;
        public bool airAbsorption = false;
        public AirAbsorptionInput airAbsorptionInput = AirAbsorptionInput.SimulationDefined;
        [Range(0.0f, 1.0f)]
        public float airAbsorptionLow = 1.0f;
        [Range(0.0f, 1.0f)]
        public float airAbsorptionMid = 1.0f;
        [Range(0.0f, 1.0f)]
        public float airAbsorptionHigh = 1.0f;

        public bool directivity = false;
        public DirectivityInput directivityInput = DirectivityInput.SimulationDefined;
        [Range(0.0f, 1.0f)]
        public float dipoleWeight = 0.0f;
        [Range(0.0f, 4.0f)]
        public float dipolePower = 0.0f;
        [Range(0.0f, 1.0f)]
        public float directivityValue = 1.0f;

        public bool occlusion = false;
        public OcclusionInput occlusionInput = OcclusionInput.SimulationDefined;
        public OcclusionType occlusionType = OcclusionType.Raycast;
        [Range(0.0f, 4.0f)]
        public float occlusionRadius = 1.0f;
        [Range(1, 128)]
        public int occlusionSamples = 16;
        [Range(0.0f, 1.0f)]
        public float occlusionValue = 1.0f;
        public bool transmission = false;
        public TransmissionType transmissionType = TransmissionType.FrequencyIndependent;
        public TransmissionInput transmissionInput = TransmissionInput.SimulationDefined;
        [Range(0.0f, 1.0f)]
        public float transmissionLow = 1.0f;
        [Range(0.0f, 1.0f)]
        public float transmissionMid = 1.0f;
        [Range(0.0f, 1.0f)]
        public float transmissionHigh = 1.0f;
        [Range(1, 8)]
        public int maxTransmissionSurfaces = 1;

        [Range(0.0f, 1.0f)]
        public float directMixLevel = 1.0f;

        public bool reflections = false;
        public ReflectionsType reflectionsType = ReflectionsType.Realtime;
        public bool useDistanceCurveForReflections = false;
#if SONITY_ENABLE_INTEGRATION_STEAM_AUDIO
        public SteamAudio.SteamAudioBakedSource currentBakedSource = null;
#endif
        public IntPtr reflectionsIR = IntPtr.Zero;
        public float reverbTimeLow = 0.0f;
        public float reverbTimeMid = 0.0f;
        public float reverbTimeHigh = 0.0f;
        public float hybridReverbEQLow = 1.0f;
        public float hybridReverbEQMid = 1.0f;
        public float hybridReverbEQHigh = 1.0f;
        public int hybridReverbDelay = 0;
        public bool applyHRTFToReflections = false;
        [Range(0.0f, 10.0f)]
        public float reflectionsMixLevel = 1.0f;

        public bool pathing = false;
#if SONITY_ENABLE_INTEGRATION_STEAM_AUDIO
        public SteamAudio.SteamAudioProbeBatch pathingProbeBatch = null;
#endif
        public bool pathValidation = true;
        public bool findAlternatePaths = true;
        public float[] pathingEQ = new float[3] { 1.0f, 1.0f, 1.0f };
        public float[] pathingSH = new float[16];
        public bool applyHRTFToPathing = false;
        [Range(0.0f, 10.0f)]
        public float pathingMixLevel = 1.0f;
    }

    // From SteamAudio.cs using SteamAudio version 4.6.1 //////////////////////////////////////////

    // ENUMERATIONS

    public enum Bool {
        False,
        True
    }

    public enum Error {
        Success,
        Failure,
        OutOfMemory,
        Initialization
    }

    public enum LogLevel {
        Info,
        Warning,
        Error,
        Debug
    }

    public enum SIMDLevel {
        SSE2,
        SSE4,
        AVX,
        AVX2,
        AVX512,
        NEON = SSE2
    }

    [Flags]
    public enum ContextFlags {
        Validation = 1 << 0,
        Force32Bit = 0x7fffffff
    }

    public enum OpenCLDeviceType {
        Any,
        CPU,
        GPU
    }

    public enum SceneType {
        Default,
        Embree,
        RadeonRays,
#if UNITY_2019_2_OR_NEWER
        [InspectorName("Unity")]
#endif
        Custom
    }

    public enum HRTFType {
        Default,
        SOFA
    }

    public enum HRTFNormType {
        None,
        RMS
    }

    public enum ProbeGenerationType {
        Centroid,
        UniformFloor
    }

    public enum BakedDataVariation {
        Reverb,
        StaticSource,
        StaticListener,
        Dynamic
    }

    public enum BakedDataType {
        Reflections,
        Pathing
    }

    [Flags]
    public enum SimulationFlags {
        Direct = 1 << 0,
        Reflections = 1 << 1,
        Pathing = 1 << 2
    }

    [Flags]
    public enum DirectSimulationFlags {
        DistanceAttenuation = 1 << 0,
        AirAbsorption = 1 << 1,
        Directivity = 1 << 2,
        Occlusion = 1 << 3,
        Transmission = 1 << 4
    }

    public enum HRTFInterpolation {
        Nearest,
        Bilinear
    }

    public enum DistanceAttenuationModelType {
        Default,
        InverseDistance,
        Callback
    }

    public enum AirAbsorptionModelType {
        Default,
        Exponential,
        Callback
    }

    public enum OcclusionType {
        Raycast,
        Volumetric
    }

    [Flags]
    public enum DirectEffectFlags {
        ApplyDistanceAttenuation = 1 << 0,
        ApplyAirAbsorption = 1 << 1,
        ApplyDirectivity = 1 << 2,
        ApplyOcclusion = 1 << 3,
        ApplyTransmission = 1 << 4
    }

    public enum TransmissionType {
        FrequencyIndependent,
        FrequencyDependent
    }

    public enum ReflectionEffectType {
        Convolution,
        Parametric,
        Hybrid,
#if UNITY_2019_2_OR_NEWER
        [InspectorName("TrueAudio Next")]
#endif
        TrueAudioNext
    }

    [Flags]
    public enum ReflectionsBakeFlags {
        BakeConvolution = 1 << 0,
        BakeParametric = 1 << 1
    }

    // From SteamAudioSource.cs using SteamAudio version 4.6.1 ////////////////////////////////////

    public enum DistanceAttenuationInput {
        CurveDriven,
        PhysicsBased
    }

    public enum AirAbsorptionInput {
        SimulationDefined,
        UserDefined
    }

    public enum DirectivityInput {
        SimulationDefined,
        UserDefined
    }

    public enum OcclusionInput {
        SimulationDefined,
        UserDefined
    }

    public enum TransmissionInput {
        SimulationDefined,
        UserDefined
    }

    public enum ReflectionsType {
        Realtime,
        BakedStaticSource,
        BakedStaticListener
    }

    // End of Steam Audio license, resuming original Sonigon copyright
}