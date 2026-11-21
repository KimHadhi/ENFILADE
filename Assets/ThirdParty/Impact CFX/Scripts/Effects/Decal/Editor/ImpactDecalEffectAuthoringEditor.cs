using ImpactCFX.EditorScripts;
using UnityEditor;
using UnityEngine;

namespace ImpactCFX.Decals.EditorScripts
{
    [CustomEditor(typeof(ImpactDecalEffectAuthoring))]
    public class ImpactDecalEffectAuthoringEditor : ImpactEffectAuthoringBaseEditor
    {
        private SerializedProperty minimumVelocityProperty;
        private SerializedProperty maximumVelocityProperty;
        private SerializedProperty collisionNormalInfluenceProperty;

        private SerializedProperty decalPrefabProperty;
        private SerializedProperty decalPrefabsProperty;
        private SerializedProperty decalSelectionModeProperty;

        private SerializedProperty createOnCollisionProperty;
        private SerializedProperty createOnSlideProperty;
        private SerializedProperty createOnRollProperty;

        private SerializedProperty creationIntervalProperty;
        private SerializedProperty creationIntervalTypeProperty;

        protected override void OnEnable()
        {
            base.OnEnable();

            minimumVelocityProperty = serializedObject.FindProperty("minimumVelocity");
            maximumVelocityProperty = serializedObject.FindProperty("maximumVelocity");

            collisionNormalInfluenceProperty = serializedObject.FindProperty("collisionNormalInfluence");

            decalPrefabsProperty = serializedObject.FindProperty("decalPrefabs");
            decalPrefabProperty = serializedObject.FindProperty("decalPrefab");
            decalSelectionModeProperty = serializedObject.FindProperty("decalSelectionMode");

            createOnCollisionProperty = serializedObject.FindProperty("createOnCollision");
            createOnSlideProperty = serializedObject.FindProperty("createOnSlide");
            createOnRollProperty = serializedObject.FindProperty("createOnRoll");

            creationIntervalProperty = serializedObject.FindProperty("creationInterval");
            creationIntervalTypeProperty = serializedObject.FindProperty("creationIntervalType");
        }

        protected override void drawEffectProperties()
        {
            if (decalPrefabProperty.objectReferenceValue != null)
            {
                EditorGUILayout.HelpBox("Decal Prefab is now obsolete, and the Decal Prefabs array can be used instead. You can delete the prefab from the field below to hide this message.", MessageType.Warning);
                EditorGUILayout.PropertyField(decalPrefabProperty);

                EditorGUILayout.Separator();
            }

            EditorGUILayout.PropertyField(decalPrefabsProperty);
            EditorGUILayout.PropertyField(decalSelectionModeProperty);

            EditorGUILayout.Separator();

            EditorGUILayout.PropertyField(createOnCollisionProperty);
            EditorGUILayout.PropertyField(createOnSlideProperty);
            EditorGUILayout.PropertyField(createOnRollProperty);

            EditorGUILayout.Separator();

            bool slideOrRoll = createOnSlideProperty.boolValue || createOnRollProperty.boolValue;

            GUI.enabled = slideOrRoll;

            EditorGUILayout.PropertyField(creationIntervalProperty);
            EditorGUILayout.PropertyField(creationIntervalTypeProperty);

            GUI.enabled = true;

            ImpactEditorUtilities.Separator();

            CollisionSelectionMode decalSelectionMode = (CollisionSelectionMode)decalSelectionModeProperty.enumValueIndex;
            Rect r = EditorGUILayout.GetControlRect();
            ImpactEditorUtilities.DrawRangeProperty(r, new GUIContent("Velocity Reference Range"), minimumVelocityProperty, maximumVelocityProperty, decalSelectionMode == CollisionSelectionMode.Velocity);

            EditorGUILayout.Slider(collisionNormalInfluenceProperty, 0, 1);
        }

    }
}