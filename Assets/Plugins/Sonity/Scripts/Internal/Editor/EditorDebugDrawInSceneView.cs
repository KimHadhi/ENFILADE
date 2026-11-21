// Created by Victor Engström
// Copyright 2026 Sonigon AB
// http://www.sonity.org/

// Code for older because SceneView.duringSceneGui doesnt exist
#if UNITY_EDITOR && UNITY_2019_1_OR_NEWER

using UnityEngine;
using UnityEditor;

namespace Sonity.Internal {

    public static class EditorDebugDrawInSceneView {

        // Should be read and writeable
        private static GUIStyle guiStyle = new GUIStyle();
        private static GUIContent guiContent = new GUIContent();

        // Needed for disabling domain reloading
#if UNITY_2019_2_OR_NEWER
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#else
        // Older versions than 2019.2 doesn't have the SubsystemRegistration load type
        [RuntimeInitializeOnLoadMethod]
#endif
        static void ResetStatics() {
            SceneView.duringSceneGui -= UpdateDraw;
            SceneView.duringSceneGui += UpdateDraw;
        }

        private static void UpdateDraw(SceneView sceneview) {
            if (SoundManagerBase.Instance != null && SoundManagerBase.Instance.Internals.debug.GetDrawEnabledSceneView()) {
                if (SoundManagerBase.Instance.Internals.voicePool.voices == null) {
                    return;
                }
                for (int i = 0; i < SoundManagerBase.Instance.Internals.voicePool.voices.Length; i++) {
                    Voice voice = SoundManagerBase.Instance.Internals.voicePool.voices[i];
                    if (voice != null
                        && voice.isAssigned
                        && voice.soundEvent != null
                        && voice.cachedGameObject != null
                        && voice.GetState() == VoiceState.Play
                        && voice.soundEvent.internals.data.debugDrawShow == DebugSoundEventDrawShow.Show
                        ) {
                        Draw(
                            voice.soundEventName,
                            voice.position,
                            GetColorStart(voice.soundEvent),
                            GetColorEnd(voice.soundEvent),
                            GetColorOutline(voice.soundEvent),
                            voice.GetTimePlayed(),
                            SoundManagerBase.Instance.Internals.debug.drawSoundEventsVolumeToOpacity,
                            SoundManagerBase.Instance.Internals.debug.drawSoundEventsLifetimeToOpacity,
                            SoundManagerBase.Instance.Internals.debug.drawSoundEventsLifetimeFadeLength,
                            voice.GetVolumeRatioWithFade(),
                            GetFontSize(voice.soundEvent),
                            GetOpacityMultiplier(voice.soundEvent)
                        );
                    }
                }
            }
        }

        private static int GetFontSize(SoundEventBase soundEvent) {
            if (soundEvent.internals.data.debugDrawSoundEventStyleOverride) {
                return Mathf.RoundToInt(SoundManagerBase.Instance.Internals.debug.drawSoundEventsFontSize * soundEvent.internals.data.debugDrawSoundEventFontSizeMultiplier);
            } else {
                return SoundManagerBase.Instance.Internals.debug.drawSoundEventsFontSize;
            }
        }

        private static float GetOpacityMultiplier(SoundEventBase soundEvent) {
            if (soundEvent.internals.data.debugDrawSoundEventStyleOverride) {
                return soundEvent.internals.data.debugDrawSoundEventOpacityMultiplier;
            } else {
                return 1f;
            }
        }

        private static Color GetColorStart(SoundEventBase soundEvent) {
            if (soundEvent.internals.data.debugDrawSoundEventStyleOverride) {
                return soundEvent.internals.data.debugDrawSoundEventColorStart;
            } else {
                return SoundManagerBase.Instance.Internals.debug.drawSoundEventsColorStart;
            }
        }

        private static Color GetColorEnd(SoundEventBase soundEvent) {
            if (soundEvent.internals.data.debugDrawSoundEventStyleOverride) {
                return soundEvent.internals.data.debugDrawSoundEventColorEnd;
            } else {
                return SoundManagerBase.Instance.Internals.debug.drawSoundEventsColorEnd;
            }
        }

        private static Color GetColorOutline(SoundEventBase soundEvent) {
            if (soundEvent.internals.data.debugDrawSoundEventStyleOverride) {
                return soundEvent.internals.data.debugDrawSoundEventColorOutline;
            } else {
                return SoundManagerBase.Instance.Internals.debug.drawSoundEventsColorOutline;
            }
        }

        static private void Draw(
            string text, Vector3 worldPos, Color lifetimeStartColor, Color lifetimeFadeColor, Color outlineColor,
            float timePlayed, float volumeToAlpha, float lifetimeToAlpha, float lifetimeFadeLength, 
            float voiceVolume, int fontSize, float opacityMultiplier, bool outline = true, float offsetX = 0f, float offsetY = 0f
            ) {

            if (lifetimeFadeLength == 0f) {
                lifetimeFadeLength = 0.00001f;
            }
            float timeAlpha = 1f - Mathf.Clamp(timePlayed / lifetimeFadeLength, 0f, 1f);
            float textAlpha = (timeAlpha - 1f) * lifetimeToAlpha + 1f;
            textAlpha *= (LogLinExp.Get(voiceVolume, -2f) - 1f) * volumeToAlpha + 1f;
            textAlpha *= opacityMultiplier;

            SceneView view = SceneView.currentDrawingSceneView;
            Vector3 screenPos = view.camera.WorldToScreenPoint(worldPos);

            guiStyle.fontSize = fontSize;
            guiStyle.alignment = TextAnchor.UpperLeft;

            guiContent.text = text;
            Vector2 guiContentSize = guiStyle.CalcSize(guiContent);

            // If outside of screen
            if (screenPos.y < 0 || screenPos.y > Screen.height || screenPos.x + guiContentSize.x < 0 || screenPos.x - guiContentSize.x > Screen.width || screenPos.z < 0) {
                return;
            }

            float textOffsetX = guiContentSize.x * 0.74f;
            float textOffsetY = -guiContentSize.y * 0.9f;

            if (outline) {
                // Alpha divided by 4
                outlineColor.a *= textAlpha * 0.25f;
                guiStyle.normal.textColor = outlineColor;
                float shadowOffset = 1f;
                // Below
                Handles.Label(MoveByPixel(worldPos, offsetX - textOffsetX, offsetY - shadowOffset - textOffsetY), guiContent, guiStyle);
                // Above
                Handles.Label(MoveByPixel(worldPos, offsetX - textOffsetX, offsetY + shadowOffset - textOffsetY), guiContent, guiStyle);
                // Left
                Handles.Label(MoveByPixel(worldPos, offsetX - textOffsetX - shadowOffset, offsetY - textOffsetY), guiContent, guiStyle);
                // Right
                Handles.Label(MoveByPixel(worldPos, offsetX - textOffsetX + shadowOffset, offsetY - textOffsetY), guiContent, guiStyle);
            }

            lifetimeStartColor = Color.Lerp(lifetimeStartColor, lifetimeFadeColor, Mathf.Clamp(timePlayed / lifetimeFadeLength, 0f, 1f));
            lifetimeStartColor.a *= textAlpha;
            guiStyle.normal.textColor = lifetimeStartColor;
            Handles.Label(MoveByPixel(worldPos, offsetX - textOffsetX, offsetY - textOffsetY), guiContent, guiStyle);
        }

        private static Vector3 MoveByPixel(Vector3 position, float x, float y) {
            Camera camera = SceneView.currentDrawingSceneView.camera;
            if (camera) {
                return camera.ScreenToWorldPoint(camera.WorldToScreenPoint(position) + new Vector3(x, y));
            } else {
                return position;
            }
        }
    }
}
#endif