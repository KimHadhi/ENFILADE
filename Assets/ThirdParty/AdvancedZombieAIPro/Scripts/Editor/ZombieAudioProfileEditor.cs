using AdvancedZombieAIPro;
using UnityEditor;
using UnityEngine;

namespace AdvancedZombieAIProEditor
{
    [CustomEditor(typeof(ZombieAudioProfile))]
    [CanEditMultipleObjects]
    internal sealed class ZombieAudioProfileEditor : Editor
    {
        private static readonly Color AudioAccentColor = new Color32(0xD9, 0x86, 0x34, 0xFF);

        private SerializedProperty playback;
        private SerializedProperty idle;
        private SerializedProperty wander;
        private SerializedProperty alert;
        private SerializedProperty chase;
        private SerializedProperty search;
        private SerializedProperty inspect;
        private SerializedProperty standAttack;
        private SerializedProperty chaseAttack;
        private SerializedProperty hit;
        private SerializedProperty stun;
        private SerializedProperty eatingLoop;
        private SerializedProperty death;
        private SerializedProperty crawlFall;
        private SerializedProperty crawlIdle;
        private SerializedProperty crawlMovement;

        private void OnEnable()
        {
            playback = serializedObject.FindProperty("playback");
            idle = serializedObject.FindProperty("idle");
            wander = serializedObject.FindProperty("wander");
            alert = serializedObject.FindProperty("alert");
            chase = serializedObject.FindProperty("chase");
            search = serializedObject.FindProperty("search");
            inspect = serializedObject.FindProperty("inspect");
            standAttack = serializedObject.FindProperty("standAttack");
            chaseAttack = serializedObject.FindProperty("chaseAttack");
            hit = serializedObject.FindProperty("hit");
            stun = serializedObject.FindProperty("stun");
            eatingLoop = serializedObject.FindProperty("eatingLoop");
            death = serializedObject.FindProperty("death");
            crawlFall = serializedObject.FindProperty("crawlFall");
            crawlIdle = serializedObject.FindProperty("crawlIdle");
            crawlMovement = serializedObject.FindProperty("crawlMovement");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            ZombieInspectorGUI.DrawHeader(
                "Zombie Audio Profile",
                "State sounds, loops, timed one-shots, and playback settings",
                AudioAccentColor);
            DrawSummarySection();
            DrawPlaybackSection();
            DrawStandingAmbientSection();
            DrawStandingActionSection();
            DrawCrawlingSection();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawSummarySection()
        {
            ZombieInspectorGUI.DrawSection("Summary", () =>
            {
                if (targets.Length != 1)
                {
                    EditorGUILayout.HelpBox("Select one Zombie Audio Profile to inspect clip usage.", MessageType.Info);
                    return;
                }

                ZombieAudioProfile profile = target as ZombieAudioProfile;
                if (profile == null)
                {
                    return;
                }

                int assignedClips = CountAssignedClips(profile);
                ZombieInspectorGUI.DrawStatusLine("Assigned Clips", assignedClips.ToString());
                ZombieInspectorGUI.DrawStatusLine("Playable Groups", CountPlayableGroups(profile).ToString());
                ZombieInspectorGUI.DrawStatusLine("Loop Clips", CountLoopClips(profile).ToString());

                if (assignedClips == 0)
                {
                    EditorGUILayout.HelpBox("No audio clips are assigned yet. The profile is valid, but the controller will not play zombie audio until clips are added.", MessageType.Warning);
                }
            }, AudioAccentColor);
        }

        private void DrawPlaybackSection()
        {
            ZombieInspectorGUI.DrawSection("Playback", () =>
            {
                DrawRelative(playback, "masterVolume");
                DrawRelative(playback, "spatialBlend");
                DrawRelative(playback, "minDistance");
                DrawRelative(playback, "maxDistance");
                DrawRelative(playback, "rolloffMode");
                DrawPlaybackWarnings();
            }, AudioAccentColor);
        }

        private void DrawStandingAmbientSection()
        {
            ZombieInspectorGUI.DrawSection("Standing Ambient", () =>
            {
                ZombieInspectorGUI.DrawProperty(idle);
                ZombieInspectorGUI.DrawProperty(wander);
                ZombieInspectorGUI.DrawProperty(chase);
                ZombieInspectorGUI.DrawProperty(search);
                ZombieInspectorGUI.DrawProperty(inspect);
            }, AudioAccentColor);
        }

        private void DrawStandingActionSection()
        {
            ZombieInspectorGUI.DrawSection("Standing Actions", () =>
            {
                ZombieInspectorGUI.DrawProperty(alert);
                ZombieInspectorGUI.DrawProperty(standAttack);
                ZombieInspectorGUI.DrawProperty(chaseAttack);
                ZombieInspectorGUI.DrawProperty(hit);
                ZombieInspectorGUI.DrawProperty(stun);
                ZombieInspectorGUI.DrawProperty(eatingLoop);
                ZombieInspectorGUI.DrawProperty(death);
            }, AudioAccentColor);
        }

        private void DrawCrawlingSection()
        {
            ZombieInspectorGUI.DrawSection("Crawling", () =>
            {
                ZombieInspectorGUI.DrawProperty(crawlFall);
                ZombieInspectorGUI.DrawProperty(crawlIdle);
                ZombieInspectorGUI.DrawProperty(crawlMovement);
            }, AudioAccentColor);
        }

        private void DrawPlaybackWarnings()
        {
            if (playback == null)
            {
                return;
            }

            SerializedProperty minDistance = playback.FindPropertyRelative("minDistance");
            SerializedProperty maxDistance = playback.FindPropertyRelative("maxDistance");
            if (minDistance == null ||
                maxDistance == null ||
                minDistance.hasMultipleDifferentValues ||
                maxDistance.hasMultipleDifferentValues)
            {
                return;
            }

            if (maxDistance.floatValue < minDistance.floatValue)
            {
                EditorGUILayout.HelpBox("Max Distance is lower than Min Distance and will be clamped by the profile validation.", MessageType.Warning);
            }
        }

        private static void DrawRelative(SerializedProperty parent, string relativePropertyName)
        {
            if (parent == null)
            {
                return;
            }

            ZombieInspectorGUI.DrawProperty(parent.FindPropertyRelative(relativePropertyName));
        }

        private static int CountAssignedClips(ZombieAudioProfile profile)
        {
            int count = 0;
            count += CountClips(profile.idle);
            count += CountLoopAndTimedClips(profile.wander);
            count += CountClips(profile.alert);
            count += CountLoopAndTimedClips(profile.chase);
            count += CountClips(profile.search);
            count += CountLoopAndTimedClips(profile.inspect);
            count += CountClips(profile.standAttack);
            count += CountClips(profile.chaseAttack);
            count += CountClips(profile.hit);
            count += CountClips(profile.stun);
            count += CountClips(profile.eatingLoop);
            count += CountClips(profile.death);
            count += CountClips(profile.crawlFall);
            count += CountClips(profile.crawlIdle);
            count += CountLoopAndTimedClips(profile.crawlMovement);
            return count;
        }

        private static int CountLoopClips(ZombieAudioProfile profile)
        {
            int count = 0;
            count += CountClips(profile.wander != null ? profile.wander.loop : null);
            count += CountClips(profile.chase != null ? profile.chase.loop : null);
            count += CountClips(profile.inspect != null ? profile.inspect.loop : null);
            count += CountClips(profile.stun);
            count += CountClips(profile.eatingLoop);
            count += CountClips(profile.crawlMovement != null ? profile.crawlMovement.loop : null);
            return count;
        }

        private static int CountPlayableGroups(ZombieAudioProfile profile)
        {
            int count = 0;
            AddPlayableGroup(profile.idle, ref count);
            AddPlayableGroups(profile.wander, ref count);
            AddPlayableGroup(profile.alert, ref count);
            AddPlayableGroups(profile.chase, ref count);
            AddPlayableGroup(profile.search, ref count);
            AddPlayableGroups(profile.inspect, ref count);
            AddPlayableGroup(profile.standAttack, ref count);
            AddPlayableGroup(profile.chaseAttack, ref count);
            AddPlayableGroup(profile.hit, ref count);
            AddPlayableGroup(profile.stun, ref count);
            AddPlayableGroup(profile.eatingLoop, ref count);
            AddPlayableGroup(profile.death, ref count);
            AddPlayableGroup(profile.crawlFall, ref count);
            AddPlayableGroup(profile.crawlIdle, ref count);
            AddPlayableGroups(profile.crawlMovement, ref count);
            return count;
        }

        private static int CountLoopAndTimedClips(ZombieLoopAndTimedOneShotAudioSettings settings)
        {
            if (settings == null)
            {
                return 0;
            }

            return CountClips(settings.loop) + CountClips(settings.oneShots);
        }

        private static int CountClips(ZombieAudioClipSettings settings)
        {
            if (settings == null || settings.clips == null)
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < settings.clips.Length; i++)
            {
                if (settings.clips[i] != null)
                {
                    count++;
                }
            }

            return count;
        }

        private static void AddPlayableGroups(ZombieLoopAndTimedOneShotAudioSettings settings, ref int count)
        {
            if (settings == null)
            {
                return;
            }

            AddPlayableGroup(settings.loop, ref count);
            AddPlayableGroup(settings.oneShots, ref count);
        }

        private static void AddPlayableGroup(ZombieAudioClipSettings settings, ref int count)
        {
            if (settings != null && settings.CanPlay)
            {
                count++;
            }
        }
    }
}
