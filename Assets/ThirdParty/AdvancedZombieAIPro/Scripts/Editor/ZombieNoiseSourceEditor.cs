using AdvancedZombieAIPro;
using UnityEditor;
using UnityEngine;

namespace AdvancedZombieAIProEditor
{
    [CustomEditor(typeof(ZombieNoiseSource))]
    [CanEditMultipleObjects]
    internal sealed class ZombieNoiseSourceEditor : Editor
    {
        private static readonly Color NoiseAccentColor = new Color32(0xF2, 0x8C, 0x28, 0xFF);

        private SerializedProperty defaultNoiseIntensity;

        private void OnEnable()
        {
            defaultNoiseIntensity = serializedObject.FindProperty("defaultNoiseIntensity");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            ZombieInspectorGUI.DrawHeader(
                "Zombie Noise Source",
                "Noise intensity used to alert nearby zombies",
                NoiseAccentColor);
            ZombieInspectorGUI.DrawSection("Noise", () =>
            {
                ZombieInspectorGUI.DrawDelayedProperty(defaultNoiseIntensity);
            }, NoiseAccentColor);

            serializedObject.ApplyModifiedProperties();
        }
    }
}
