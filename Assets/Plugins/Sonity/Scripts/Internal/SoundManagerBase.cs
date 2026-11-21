// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

using System;
using System.Collections;
using UnityEngine;

namespace Sonity.Internal {

    // ExecuteInEditMode so that e.g. SoundContainer & SoundTrigger distanceScale, guiWarnings, GetIsInSolo can be checked in editor
    [ExecuteInEditMode]
    public abstract class SoundManagerBase : MonoBehaviour {

        /// <summary>
        /// The static instance of the <see cref="SoundManagerBase">SoundManager</see>
        /// </summary>
        public static SoundManagerBase Instance { get; private set; }

        [SerializeField]
        private SoundManagerInternals internals = new SoundManagerInternals();

        /// <summary>
        /// The internal data of the <see cref="SoundManagerBase">SoundManager</see>
        /// </summary>
        public SoundManagerInternals Internals { get { return internals; } private set { internals = value; } }

#if SONITY_ENABLE_ADDRESSABLE_AUDIOMIXER
        private bool addressableAudioMixerIsLoaded = false;
        public event Action<bool> ActionAddressableAudioMixerLoaded = null;
#endif

        // Needed for disabling domain reloading
#if UNITY_2019_2_OR_NEWER
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#else
        // Older versions than 2019.2 doesn't have the SubsystemRegistration load type
        [RuntimeInitializeOnLoadMethod]
#endif
        static void ResetStatics() {
            if (Instance != null) {
                Instance.ScriptingDefineSymbolsWarnings();
                // AwakeSetup is run before async to set correct time for fades etc
                Instance.Awake();
                if (Instance.internals.settings.asyncStartup) {
                    IEnumerator asyncLoader = Instance.Start();
                    while (asyncLoader.MoveNext()) ;
                }
#if SONITY_ENABLE_ADDRESSABLE_AUDIOMIXER
                Instance.addressableAudioMixerIsLoaded = false;
                Instance.ActionAddressableAudioMixerLoaded = null;
#endif
            }
        }

#if SONITY_ENABLE_ADDRESSABLE_AUDIOMIXER
        public bool GetAddressableAudioMixerHasLoaded() {
            return addressableAudioMixerIsLoaded;
        }

        public void InternalAddressableAudioMixerInvoke(bool success) {
            addressableAudioMixerIsLoaded = true;
            ActionAddressableAudioMixerLoaded?.Invoke(success);
        }
#endif

        private void InstanceCheck() {
            if (Instance == null) {
                Instance = this;
                // Needed for disabling domain reloading
                internals.isGoingToDelete = false;
                internals.cachedSoundManagerTransform = GetComponent<Transform>();
            } else if (Instance != this) {
                if (ShouldDebug.Warnings()) {
                    Debug.LogWarning($"There can only be one Sonity.{NameOf.SoundManager} instance per scene.", this);
                }
                // So that it does not run the rest of the Awake and Update code
                internals.isGoingToDelete = true;
                if (Application.isPlaying) {
                    Destroy(gameObject);
                }
            }
        }

        void ScriptingDefineSymbolsWarnings() {
#if SONITY_ENABLE_VOLUME_INCREASE
            if (!internals.settings.volumeIncreaseEnable) {
                Debug.LogWarning($"Sonity.{NameOf.SoundManager}: \"Enable Volume Increase\" is disabled but the Script Define Symbol \"SONITY_ENABLE_VOLUME_INCREASE\" exists.", Instance);
            }
#else
            if (internals.settings.volumeIncreaseEnable) {
                Debug.LogWarning($"Sonity.{NameOf.SoundManager}: \"Enable Volume Increase\" is enabled but the Script Define Symbol \"SONITY_ENABLE_VOLUME_INCREASE\" is not added.", Instance);
            }
#endif
#if SONITY_ENABLE_ADDRESSABLE_AUDIOMIXER
            if (!internals.settings.addressableAudioMixerUse) {
                Debug.LogWarning($"Sonity.{NameOf.SoundManager}: \"Addressable AudioMixer\" is disabled but the Script Define Symbol \"SONITY_ENABLE_ADDRESSABLE_AUDIOMIXER\" exists.", Instance);
            }
#else
            if (internals.settings.addressableAudioMixerUse) {
                Debug.LogWarning($"Sonity.{NameOf.SoundManager}: \"Addressable AudioMixer\" is enabled but the Script Define Symbol \"SONITY_ENABLE_ADDRESSABLE_AUDIOMIXER\" is not added.", Instance);
            }
#endif
#if SONITY_ENABLE_INTEGRATION_STEAM_AUDIO
            if (!Instance.internals.settings.steamAudioIntegrationEnable) {
                Debug.LogWarning($"Sonity.{NameOf.SoundManager}: \"Steam Audio Integration\" is disabled but the Script Define Symbol \"SONITY_ENABLE_INTEGRATION_STEAM_AUDIO\" exists.", Instance);
            }
#else
            if (Instance.internals.settings.steamAudioIntegrationEnable) {
                Debug.LogWarning($"Sonity.{NameOf.SoundManager}: \"Steam Audio Integration\" is enabled but the Script Define Symbol \"SONITY_ENABLE_INTEGRATION_STEAM_AUDIO\" is not added.", Instance);
            }
#endif
#if SONITY_ENABLE_INTEGRATION_PLAYMAKER
            if (!Instance.internals.settings.playmakerIntegrationEnable) {
                Debug.LogWarning($"Sonity.{NameOf.SoundManager}: \"PlayMaker Integration\" is disabled but the Script Define Symbol \"SONITY_ENABLE_INTEGRATION_PLAYMAKER\" exists.", Instance);
            }
#else
            if (Instance.internals.settings.playmakerIntegrationEnable) {
                Debug.LogWarning($"Sonity.{NameOf.SoundManager}: \"PlayMaker Integration\" is enabled but the Script Define Symbol \"SONITY_ENABLE_INTEGRATION_PLAYMAKER\" is not added.", Instance);
            }
#endif
#if SONITY_DISABLE_VOICE_EFFECTS
            if (!Instance.internals.settings.voiceEffectDisable) {
                Debug.LogWarning($"Sonity.{NameOf.SoundManager}: \"Disable Voice Effects\" is disabled but the Script Define Symbol \"SONITY_DISABLE_VOICE_EFFECTS\" exists.", Instance);
            }
#else
            if (Instance.internals.settings.voiceEffectDisable) {
                Debug.LogWarning($"Sonity.{NameOf.SoundManager}: \"Disable Voice Effects\" is enabled but the Script Define Symbol \"SONITY_DISABLE_VOICE_EFFECTS\" is not added.", Instance);
            }
#endif
#if SONITY_ENABLE_SOUNDMANAGER_MANUAL_UPDATE
            if (!Instance.internals.settings.soundManagerManualUpdate) {
                Debug.LogWarning($"Sonity.{NameOf.SoundManager}: \"SoundManager Manual Update\" is disabled but the Script Define Symbol \"SONITY_ENABLE_SOUNDMANAGER_MANUAL_UPDATE\" exists.", Instance);
            }
#else
            if (Instance.internals.settings.soundManagerManualUpdate) {
                Debug.LogWarning($"Sonity.{NameOf.SoundManager}: \"SoundManager Manual Update\" is enabled but the Script Define Symbol \"SONITY_ENABLE_SOUNDMANAGER_MANUAL_UPDATE\" is not added.", Instance);
            }
#endif
            //#if SONITY_ENABLE_INTEGRATION_ENTITY_SOUND
            //            if (!Instance.internals.settings.entitySoundIntegrationEnable) {
            //                Debug.LogWarning($"Sonity.{NameOf.SoundManager}: \"Entity Integration\" is disabled but the Script Define Symbol \"SONITY_ENABLE_INTEGRATION_ENTITY_SOUND\" exists.", Instance);
            //            }
            //#else
            //            if (Instance.internals.settings.entitySoundIntegrationEnable) {
            //                Debug.LogWarning($"Sonity.{NameOf.SoundManager}: \"Entity Integration\" is enabled but the Script Define Symbol \"SONITY_ENABLE_INTEGRATION_ENTITY_SOUND\" is not added.", Instance);
            //            }
            //#endif

        }

        // Normal startup is run before async startup
        private void Awake() {
            InstanceCheck();
            if (!internals.isGoingToDelete && Application.isPlaying) {
                internals.AwakeCheck();
            }
        }

        // Async startup
        // This needs to be Start(), otherwise it wont work
        private IEnumerator Start() {
            if (internals.settings.asyncStartup) {
                InstanceCheck();
                if (!internals.isGoingToDelete && Application.isPlaying) {
                    // Override so if the scripting define symbol is added, it should run even if it is disabled in the SoundManager
                    yield return internals.AwakeCheckAsync();
                }
            }
            yield return null;
        }

        /// <summary>
        // To use ManualUpdate(), you need to enable "Manual Update" in the SoundManager,
        // and add the script define symbol "SONITY_ENABLE_SOUNDMANAGER_MANUAL_UPDATE".
        // If enabled then the SoundManagers MonoBehaviour Update() will be replaced with ManualUpdate().
        // This way Sonity won't automatically update every frame.
        // You need to call SoundManager.Instance.ManualUpdate() manually.
        // Giving you full control how often and when in the frame the SoundManager should update.
        /// </summary>
#if SONITY_ENABLE_SOUNDMANAGER_MANUAL_UPDATE
        public void ManualUpdate() {
#else
        private void Update() {
#endif
            if (!internals.isGoingToDelete) {
#if UNITY_EDITOR
                if (Application.isPlaying) {
                    internals.ManagedUpdate();
                } else {
                    InstanceCheck();
                }
#else
                // Don't need to instance check during Update() in builds
                internals.ManagedUpdate();
#endif
            }
        }

        private void OnDestroy() {
            // So that if the SoundManager is destroyed during eg. scene switching it will stop all playing sounds
            if (Application.isPlaying) {
                internals.Destroy();
            }
            // Needed for disabling domain reloading. If the current SoundManager is removed, then find any other one.
            if (Instance == this) {
                Instance = null;
#if UNITY_6000_4_OR_NEWER || SONITY_DLL_RUNTIME
                // Unity 6000.4 removes FindFirstObjectByType
                SoundManagerBase tempSoundManagerBase = UnityEngine.Object.FindAnyObjectByType<SoundManagerBase>();
#elif UNITY_2022_3_OR_NEWER
                // FindFirstObjectByType is slower than FindAnyObjectByType but is more consistent
                SoundManagerBase tempSoundManagerBase = UnityEngine.Object.FindFirstObjectByType<SoundManagerBase>();
#else
                SoundManagerBase tempSoundManagerBase = UnityEngine.Object.FindObjectOfType<SoundManagerBase>();
#endif
                if (tempSoundManagerBase != null) {
                    tempSoundManagerBase.InstanceCheck();
                }
            }
        }

#if UNITY_EDITOR
        private void OnGUI() {
            // Double check for DLL
            if (Application.isEditor) {
                // For live SoundEvent debugging
                EditorDebugDrawInGameView.UpdateDraw();
            }
        }
#endif

        void OnApplicationPause(bool pauseStatus) {
            internals.onApplicationIsPaused = pauseStatus;
        }

        private void OnApplicationQuit() {
            internals.applicationIsQuitting = true;
        }
    }
}