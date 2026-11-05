using AdvancedZombieAIPro;
using UnityEngine;
using UnityEngine.AI;
using UnityEditor;
using UnityEditorInternal;

namespace AdvancedZombieAIProEditor
{
    public static class ZombieSetupMenu
    {
        private const string LoopSourceObjectName = "LoopSource";
        private const string OneShotSourceObjectName = "OneShotSource";

        [MenuItem("Tools/Advanced Zombie AI Pro/Set up Zombie")]
        private static void SetUpZombie()
        {
            GameObject selected = Selection.activeGameObject;

            if (selected == null)
            {
                EditorUtility.DisplayDialog(
                    "Set up Zombie",
                    "Please select a GameObject first.",
                    "OK"
                );
                return;
            }

            Undo.SetCurrentGroupName("Set up Zombie");
            int undoGroup = Undo.GetCurrentGroup();

            AddIfMissing<Animator>(selected);
            AddIfMissing<NavMeshAgent>(selected);
            AddIfMissing<ZombieAI>(selected);
            AddIfMissing<ZombieHealth>(selected);
            AddIfMissing<ZombieRagdollController>(selected);
            AddIfMissing<ZombieFood>(selected);
            ZombieAudioController audioController = AddIfMissing<ZombieAudioController>(selected);
            SetUpAudioSources(selected, audioController);

            ArrangeComponentOrder(selected);
            RefreshReferences(selected);

            Undo.CollapseUndoOperations(undoGroup);

            EditorUtility.SetDirty(selected);

            Debug.Log($"Zombie setup completed on: {selected.name}", selected);
        }

        [MenuItem("Tools/Advanced Zombie AI Pro/Set up Zombie", true)]
        private static bool ValidateSetUpZombie()
        {
            return Selection.activeGameObject != null;
        }

        private static T AddIfMissing<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            if (component != null)
                return component;

            return Undo.AddComponent<T>(target);
        }

        private static void ArrangeComponentOrder(GameObject target)
        {
            MoveComponentNearTop<ZombieFood>(target);
            MoveComponentNearTop<ZombieRagdollController>(target);
            MoveComponentNearTop<ZombieAudioController>(target);
            MoveComponentNearTop<ZombieHealth>(target);
            MoveComponentNearTop<ZombieAI>(target);
            MoveComponentNearTop<NavMeshAgent>(target);
            MoveComponentNearTop<Animator>(target);
        }

        private static void SetUpAudioSources(GameObject target, ZombieAudioController audioController)
        {
            if (target == null || audioController == null)
            {
                return;
            }

            AudioSource loopSource = EnsureChildAudioSource(target, LoopSourceObjectName, true);
            AudioSource oneShotSource = EnsureChildAudioSource(target, OneShotSourceObjectName, false);
            AssignAudioSources(audioController, loopSource, oneShotSource);
        }

        private static AudioSource EnsureChildAudioSource(GameObject target, string childName, bool loop)
        {
            Transform childTransform = target.transform.Find(childName);
            GameObject childObject;
            bool createdChild = false;

            if (childTransform == null)
            {
                childObject = new GameObject(childName);
                Undo.RegisterCreatedObjectUndo(childObject, $"Create {childName}");
                Undo.SetTransformParent(childObject.transform, target.transform, $"Parent {childName}");
                childObject.transform.localPosition = Vector3.zero;
                childObject.transform.localRotation = Quaternion.identity;
                childObject.transform.localScale = Vector3.one;
                createdChild = true;
            }
            else
            {
                childObject = childTransform.gameObject;
            }

            AudioSource source = childObject.GetComponent<AudioSource>();
            if (source == null)
            {
                source = Undo.AddComponent<AudioSource>(childObject);
            }
            else if (!createdChild)
            {
                Undo.RecordObject(source, $"Configure {childName}");
            }

            ConfigureAudioSource(source, loop);
            EditorUtility.SetDirty(source);
            return source;
        }

        private static void ConfigureAudioSource(AudioSource source, bool loop)
        {
            if (source == null)
            {
                return;
            }

            source.playOnAwake = false;
            source.loop = loop;
            source.ignoreListenerPause = false;
        }

        private static void AssignAudioSources(
            ZombieAudioController audioController,
            AudioSource loopSource,
            AudioSource oneShotSource)
        {
            SerializedObject controllerObject = new SerializedObject(audioController);
            SerializedProperty loopSourceProperty = controllerObject.FindProperty("loopSource");
            SerializedProperty oneShotSourceProperty = controllerObject.FindProperty("oneShotSource");
            bool assignLoopSource = ShouldAssignReference(loopSourceProperty, loopSource);
            bool assignOneShotSource = ShouldAssignReference(oneShotSourceProperty, oneShotSource);

            if (!assignLoopSource && !assignOneShotSource)
            {
                return;
            }

            Undo.RecordObject(audioController, "Assign Zombie Audio Sources");

            if (assignLoopSource)
            {
                loopSourceProperty.objectReferenceValue = loopSource;
            }

            if (assignOneShotSource)
            {
                oneShotSourceProperty.objectReferenceValue = oneShotSource;
            }

            controllerObject.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(audioController);
            PrefabUtility.RecordPrefabInstancePropertyModifications(audioController);
        }

        private static bool ShouldAssignReference(SerializedProperty property, Object reference)
        {
            return property != null && property.objectReferenceValue != reference;
        }

        private static void RefreshReferences(GameObject target)
        {
            ZombieAI zombieAI = target.GetComponent<ZombieAI>();
            if (zombieAI != null)
            {
                ZombieAIEditor.RefreshReferences(zombieAI);
            }

            ZombieHealth zombieHealth = target.GetComponent<ZombieHealth>();
            if (zombieHealth != null)
            {
                ZombieHealthEditor.RefreshReferences(zombieHealth);
            }

            ZombieFood zombieFood = target.GetComponent<ZombieFood>();
            if (zombieFood != null)
            {
                ZombieFoodEditor.RefreshReferences(zombieFood);
            }

            ZombieAudioController audioController = target.GetComponent<ZombieAudioController>();
            if (audioController != null)
            {
                ZombieAudioControllerEditor.RefreshReferences(audioController);
            }
        }

        private static void MoveComponentNearTop<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();

            if (component == null)
                return;

            for (int i = 0; i < 50; i++)
            {
                bool moved = ComponentUtility.MoveComponentUp(component);

                if (!moved)
                    break;
            }
        }
    }
}
