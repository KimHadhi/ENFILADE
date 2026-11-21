using ImpactCFX.EditorScripts;
using UnityEditor;
using UnityEngine;

namespace ImpactCFX.Particles.EditorScripts
{
    [CustomEditor(typeof(ImpactParticleEffectAuthoring))]
    public class ImpactParticleEffectAuthoringEditor : ImpactEffectAuthoringBaseEditor
    {
        private SerializedProperty minimumVelocityProperty;
        private SerializedProperty maximumVelocityProperty;
        private SerializedProperty collisionNormalInfluenceProperty;

        private SerializedProperty particlePrefabProperty;
        private SerializedProperty particlePrefabsProperty;
        private SerializedProperty particleEffectTypeProperty;
        private SerializedProperty particleSelectionModeProperty;

        private SerializedProperty emitOnSlideProperty;
        private SerializedProperty emitOnRollProperty;

        protected override void OnEnable()
        {
            base.OnEnable();

            minimumVelocityProperty = serializedObject.FindProperty("minimumVelocity");
            maximumVelocityProperty = serializedObject.FindProperty("maximumVelocity");
            collisionNormalInfluenceProperty = serializedObject.FindProperty("collisionNormalInfluence");

            particlePrefabProperty = serializedObject.FindProperty("particlePrefab");
            particlePrefabsProperty = serializedObject.FindProperty("particlePrefabs");
            particleSelectionModeProperty = serializedObject.FindProperty("particleSelectionMode");
            particleEffectTypeProperty = serializedObject.FindProperty("particleEffectType");

            emitOnSlideProperty = serializedObject.FindProperty("emitOnSlide");
            emitOnRollProperty = serializedObject.FindProperty("emitOnRoll");
        }

        protected override void drawEffectProperties()
        {
            ParticleEffectType particleEffectType = (ParticleEffectType)particleEffectTypeProperty.enumValueIndex;

            if (particleEffectType == ParticleEffectType.OneShot && particlePrefabProperty.objectReferenceValue != null)
            {
                EditorGUILayout.HelpBox("Particle Prefab is now obsolete for one-shot particles, and the Particle Prefabs array can be used instead. You can delete the prefab from the field below to hide this message.", MessageType.Warning);
                EditorGUILayout.PropertyField(particlePrefabProperty);

                EditorGUILayout.Separator();
            }

            EditorGUILayout.PropertyField(particleEffectTypeProperty);

            if (particleEffectType == ParticleEffectType.Looped)
            {
                EditorGUILayout.PropertyField(particlePrefabProperty);
            }
            else
            {
                EditorGUILayout.PropertyField(particlePrefabsProperty);
                EditorGUILayout.PropertyField(particleSelectionModeProperty);
            }

            GUI.enabled = particleEffectType == ParticleEffectType.Looped;

            EditorGUI.indentLevel++;

            EditorGUILayout.PropertyField(emitOnSlideProperty);
            EditorGUILayout.PropertyField(emitOnRollProperty);

            EditorGUI.indentLevel--;

            GUI.enabled = true;

            ImpactEditorUtilities.Separator();

            CollisionSelectionMode particleSelectionMode = (CollisionSelectionMode)particleSelectionModeProperty.enumValueIndex;
            Rect r = EditorGUILayout.GetControlRect();
            bool enableMax = particleSelectionMode == CollisionSelectionMode.Velocity && particleEffectType == ParticleEffectType.OneShot;
            ImpactEditorUtilities.DrawRangeProperty(r, new GUIContent("Velocity Reference Range"), minimumVelocityProperty, maximumVelocityProperty, enableMax);

            EditorGUILayout.Slider(collisionNormalInfluenceProperty, 0, 1);
        }
    }
}