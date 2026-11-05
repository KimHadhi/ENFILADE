using System.Collections.Generic;
using AdvancedZombieAIPro;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace AdvancedZombieAIProEditor
{
    [CustomEditor(typeof(ZombieWaypointPath))]
    [CanEditMultipleObjects]
    internal sealed class ZombieWaypointPathEditor : Editor
    {
        private static readonly Color WaypointPathAccentColor = new Color32(0x9B, 0x5D, 0xE5, 0xFF);

        private SerializedProperty waypoints;
        private ReorderableList waypointList;

        [MenuItem("GameObject/Advanced Zombie AI Pro/Zombie Waypoint Path", false, 10)]
        private static void CreateZombieWaypointPath(MenuCommand menuCommand)
        {
            GameObject pathObject = new GameObject("ZombieWaypointPath");
            pathObject.AddComponent<ZombieWaypointPath>();
            GameObjectUtility.SetParentAndAlign(pathObject, menuCommand.context as GameObject);
            Undo.RegisterCreatedObjectUndo(pathObject, "Create Zombie Waypoint Path");
            Selection.activeGameObject = pathObject;
        }

        private void OnEnable()
        {
            waypoints = serializedObject.FindProperty("waypoints");
            waypointList = new ReorderableList(serializedObject, waypoints, true, true, true, true);
            waypointList.drawHeaderCallback = rect => EditorGUI.LabelField(rect, "Ordered Waypoints");
            waypointList.elementHeightCallback = index => EditorGUIUtility.singleLineHeight + 4f;
            waypointList.drawElementCallback = DrawWaypointElement;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            ZombieInspectorGUI.DrawHeader(
                "Zombie Waypoint Path",
                "Ordered waypoint route and Scene view creation",
                WaypointPathAccentColor);
            ZombieInspectorGUI.DrawSection("Waypoint Route", () =>
            {
                EditorGUILayout.HelpBox(
                    "Select this path and Shift + left-click on a surface in the Scene view to create a waypoint.",
                    MessageType.Info);

                waypointList.DoLayoutList();
                DrawValidationWarnings();
            }, WaypointPathAccentColor);

            serializedObject.ApplyModifiedProperties();
        }

        private void OnSceneGUI()
        {
            ZombieWaypointPath path = target as ZombieWaypointPath;
            if (path == null || Selection.activeGameObject != path.gameObject)
            {
                return;
            }

            Event currentEvent = Event.current;
            if (currentEvent == null ||
                currentEvent.type != EventType.MouseDown ||
                currentEvent.button != 0 ||
                !currentEvent.shift)
            {
                return;
            }

            Ray ray = HandleUtility.GUIPointToWorldRay(currentEvent.mousePosition);
            if (!Physics.Raycast(ray, out RaycastHit hit, float.PositiveInfinity, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                return;
            }

            CreateWaypoint(path, hit.point);
            currentEvent.Use();
        }

        private void DrawWaypointElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            SerializedProperty element = waypoints.GetArrayElementAtIndex(index);
            rect.y += 2f;
            rect.height = EditorGUIUtility.singleLineHeight;
            EditorGUI.PropertyField(rect, element, new GUIContent($"Waypoint {index}"));
        }

        private void DrawValidationWarnings()
        {
            int nullCount = 0;
            int validCount = 0;
            bool hasDuplicate = false;

            for (int targetIndex = 0; targetIndex < targets.Length; targetIndex++)
            {
                ZombieWaypointPath path = targets[targetIndex] as ZombieWaypointPath;
                if (path == null)
                {
                    continue;
                }

                HashSet<ZombieWaypoint> seenWaypoints = new HashSet<ZombieWaypoint>();
                IReadOnlyList<ZombieWaypoint> pathWaypoints = path.Waypoints;
                if (pathWaypoints.Count == 0)
                {
                    continue;
                }

                for (int i = 0; i < pathWaypoints.Count; i++)
                {
                    ZombieWaypoint waypoint = pathWaypoints[i];
                    if (waypoint == null)
                    {
                        nullCount++;
                        continue;
                    }

                    validCount++;
                    if (!seenWaypoints.Add(waypoint))
                    {
                        hasDuplicate = true;
                    }
                }
            }

            if (HasAnyEmptyPath())
            {
                EditorGUILayout.HelpBox("No waypoints assigned.", MessageType.Warning);
            }

            if (nullCount > 0)
            {
                EditorGUILayout.HelpBox("This path contains null waypoint entries.", MessageType.Warning);
            }

            if (hasDuplicate)
            {
                EditorGUILayout.HelpBox("This path contains duplicate waypoint references.", MessageType.Warning);
            }

            if (validCount < ZombieWaypointPath.MinimumUsableWaypointCount)
            {
                EditorGUILayout.HelpBox(
                    $"Waypoint wandering requires at least {ZombieWaypointPath.MinimumUsableWaypointCount} valid waypoint.",
                    MessageType.Warning);
            }
        }

        private bool HasAnyEmptyPath()
        {
            for (int i = 0; i < targets.Length; i++)
            {
                ZombieWaypointPath path = targets[i] as ZombieWaypointPath;
                if (path != null && path.WaypointCount == 0)
                {
                    return true;
                }
            }

            return false;
        }

        private void CreateWaypoint(ZombieWaypointPath path, Vector3 position)
        {
            GameObject waypointObject = new GameObject(GetNextWaypointName(path));
            waypointObject.transform.position = position;

            Undo.RegisterCreatedObjectUndo(waypointObject, "Create Zombie Waypoint");
            Undo.SetTransformParent(waypointObject.transform, path.transform, "Parent Zombie Waypoint");
            ZombieWaypoint waypoint = Undo.AddComponent<ZombieWaypoint>(waypointObject);

            Undo.RecordObject(path, "Add Zombie Waypoint To Path");
            path.AddWaypoint(waypoint);
            EditorUtility.SetDirty(path);
        }

        private static string GetNextWaypointName(ZombieWaypointPath path)
        {
            int index = path != null ? path.WaypointCount : 0;
            string waypointName = $"Waypoint {index}";
            while (path != null && path.transform.Find(waypointName) != null)
            {
                index++;
                waypointName = $"Waypoint {index}";
            }

            return waypointName;
        }
    }
}
