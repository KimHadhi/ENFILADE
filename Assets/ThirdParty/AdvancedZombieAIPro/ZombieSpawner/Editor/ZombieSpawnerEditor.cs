using AdvancedZombieAIPro;
using UnityEditor;
using UnityEngine;

namespace AdvancedZombieAIProEditor
{
    [CustomEditor(typeof(ZombieSpawner))]
    [CanEditMultipleObjects]
    internal sealed class ZombieSpawnerEditor : Editor
    {
        private static readonly Color SpawnerAccentColor = new Color32(0x38, 0xB8, 0xA3, 0xFF);

        private SerializedProperty zombiePrefabs;
        private SerializedProperty spawnMode;
        private SerializedProperty spawnInterval;
        private SerializedProperty zombieCount;
        private SerializedProperty spawnRadius;

        [MenuItem("GameObject/Advanced Zombie AI Pro/Zombie Spawner", false, 11)]
        private static void CreateZombieSpawner(MenuCommand menuCommand)
        {
            GameObject spawnerObject = new GameObject("ZombieSpawner");
            spawnerObject.AddComponent<ZombieSpawner>();
            GameObjectUtility.SetParentAndAlign(spawnerObject, menuCommand.context as GameObject);
            Undo.RegisterCreatedObjectUndo(spawnerObject, "Create Zombie Spawner");
            Selection.activeGameObject = spawnerObject;
        }

        private void OnEnable()
        {
            zombiePrefabs = serializedObject.FindProperty("zombiePrefabs");
            spawnMode = serializedObject.FindProperty("spawnMode");
            spawnInterval = serializedObject.FindProperty("spawnInterval");
            zombieCount = serializedObject.FindProperty("zombieCount");
            spawnRadius = serializedObject.FindProperty("spawnRadius");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            ZombieInspectorGUI.DrawHeader("Zombie Spawner", "Prefab selection, NavMesh spawning and timing", SpawnerAccentColor);
            ZombieInspectorGUI.DrawSection("Spawner", () =>
            {
                ZombieInspectorGUI.DrawProperty(zombiePrefabs);
                ZombieInspectorGUI.DrawProperty(spawnMode);

                if (ShouldShowSpawnInterval())
                {
                    ZombieInspectorGUI.DrawDelayedProperty(spawnInterval);
                }

                ZombieInspectorGUI.DrawDelayedProperty(zombieCount);
                ZombieInspectorGUI.DrawDelayedProperty(spawnRadius);
            }, SpawnerAccentColor);

            serializedObject.ApplyModifiedProperties();
        }

        private bool ShouldShowSpawnInterval()
        {
            return spawnMode.hasMultipleDifferentValues ||
                (ZombieSpawnMode)spawnMode.enumValueIndex == ZombieSpawnMode.OverTime;
        }
    }
}
