using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;

namespace IronSamiStudio.ProceduralHitReactions.Editor
{
    [CustomEditor(typeof(BodyPartReactor))]
    public class BodyPartReactorMigration : UnityEditor.Editor
    {
        SerializedProperty profileProp;

        void OnEnable()
        {
            profileProp = serializedObject.FindProperty("profile");
        }

        public override void OnInspectorGUI()
        {
            EditorGUILayout.Space(10);
            EditorGUILayout.LabelField("", EditorStyles.boldLabel);
            EditorGUILayout.Space(5);

            serializedObject.Update();

            // Draw all default fields
            DrawPropertiesExcluding(serializedObject, "m_Script");

            EditorGUILayout.Space(4);

            if (GUILayout.Button("Create New Profile"))
            {
                CreateNewProfile((BodyPartReactor)target);
            }

            EditorGUILayout.Space(10);
            if (GUILayout.Button("Migrate Legacy Settings to ReactionProfile"))
            {
                Migrate((BodyPartReactor)target);
            }

            serializedObject.ApplyModifiedProperties();
            EditorGUILayout.Space(15);
            EditorGUILayout.LabelField("Need Help?", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("For news, updates, setup tips, and community support — join our Discord!", MessageType.Info);
            
            if (GUILayout.Button("💬 Join the Discord"))
            {
                Application.OpenURL("https://discord.com/invite/MapKXwNeDz"); // Replace with your invite if it changes
            }
        }

        private void CreateNewProfile(BodyPartReactor reactor)
        {
            string folder = "Assets/";
            if (reactor.profile != null)
            {
                string path = AssetDatabase.GetAssetPath(reactor.profile);
                folder = Path.GetDirectoryName(path);
            }

            string part = reactor.name.Replace(" ", "_");
            int counter = 1;
            string newPath;
            do
            {
                newPath = Path.Combine(folder, $"HitReaction_Profile_{counter:00}_{part}.asset");
                counter++;
            } while (File.Exists(newPath));

            ReactionProfile newProfile = ScriptableObject.CreateInstance<ReactionProfile>();
            newProfile.name = Path.GetFileNameWithoutExtension(newPath);

            AssetDatabase.CreateAsset(newProfile, newPath);
            AssetDatabase.SaveAssets();

            Undo.RecordObject(reactor, "Assign New Profile");
            reactor.profile = newProfile;

            EditorUtility.SetDirty(reactor);
            Debug.Log($"🆕 Created and assigned: {newProfile.name}");
        }

        private void Migrate(BodyPartReactor reactor)
        {
            if (reactor == null || reactor.profile == null)
            {
                Debug.LogWarning("Migration failed: Reactor or Profile is missing.");
                return;
            }

            SerializedObject so = new SerializedObject(reactor);
            ReactionProfile profile = reactor.profile;

            bool useLocalHitDir   = so.FindProperty("useLocalHitDirection")?.boolValue ?? profile.useLocalHitDirection;
            bool autoInvertBack   = so.FindProperty("autoInvertBackHits")?.boolValue ?? profile.autoInvertBackHits;
            bool additive         = so.FindProperty("additive")?.boolValue ?? profile.additive;
            bool overrideOnHit    = so.FindProperty("overrideOnHit")?.boolValue ?? false;
            int  blendResetIndex  = so.FindProperty("blendResetMode")?.enumValueIndex ?? (int)profile.blendResetMode;
            float carryOver       = so.FindProperty("carryOverScale")?.floatValue ?? profile.carryOverScale;
            bool slowBlendEnabled = so.FindProperty("slowBlendMode")?.boolValue ?? false;
            float slowBlendMult   = so.FindProperty("slowBlendMultiplier")?.floatValue ?? 1f;
            bool offsetsFromPose  = so.FindProperty("offsetsFromCurrentPose")?.boolValue ?? profile.offsetsFromCurrentPose;

            bool needsNewProfile =
                profile.useLocalHitDirection != useLocalHitDir ||
                profile.autoInvertBackHits   != autoInvertBack ||
                profile.additive             != additive ||
                profile.offsetsFromCurrentPose != offsetsFromPose ||
                profile.blendResetMode       != (ReactionProfile.BlendResetMode)blendResetIndex ||
                (profile.blendResetMode == ReactionProfile.BlendResetMode.ScaledCarryOver && profile.carryOverScale != carryOver) ||
                (slowBlendEnabled && profile.slowBlendFactor != slowBlendMult) ||
                profile.hitAcceptance != (overrideOnHit ? ReactionProfile.HitAcceptance.RestartTimer : ReactionProfile.HitAcceptance.IgnoreWhileActive);

            if (needsNewProfile)
            {
                string oldPath = AssetDatabase.GetAssetPath(profile);
                string folder  = Path.GetDirectoryName(oldPath);
                string part    = reactor.name.Replace(" ", "_");

                int counter = 1;
                string newPath;
                do
                {
                    newPath = Path.Combine(folder, $"HitReaction_Profile_{counter:00}_{part}.asset");
                    counter++;
                } while (File.Exists(newPath));

                ReactionProfile newProfile = Object.Instantiate(profile);
                newProfile.name = Path.GetFileNameWithoutExtension(newPath);

                newProfile.useLocalHitDirection = useLocalHitDir;
                newProfile.autoInvertBackHits   = autoInvertBack;
                newProfile.additive             = additive;
                newProfile.offsetsFromCurrentPose = offsetsFromPose;
                newProfile.blendResetMode       = (ReactionProfile.BlendResetMode)blendResetIndex;
                newProfile.hitAcceptance        = overrideOnHit ? ReactionProfile.HitAcceptance.RestartTimer : ReactionProfile.HitAcceptance.IgnoreWhileActive;

                if (newProfile.blendResetMode == ReactionProfile.BlendResetMode.ScaledCarryOver)
                    newProfile.carryOverScale = carryOver;

                if (slowBlendEnabled)
                    newProfile.slowBlendFactor = Mathf.Clamp01(slowBlendMult);

                AssetDatabase.CreateAsset(newProfile, newPath);
                AssetDatabase.SaveAssets();

                Undo.RecordObject(reactor, "Assign Migrated ReactionProfile");
                reactor.profile = newProfile;

                Debug.Log($"📦 Created new ReactionProfile: {newProfile.name} and assigned it to '{reactor.name}'");
            }

            var followList = so.FindProperty("followTransforms");
            if (followList != null && followList.isArray && followList.arraySize > 0)
            {
                float rotW = so.FindProperty("followRotWeight")?.floatValue ?? 0.2f;
                float posW = so.FindProperty("followPosWeight")?.floatValue ?? 0.1f;

                var list = new List<FollowObject>();
                for (int i = 0; i < followList.arraySize; i++)
                {
                    var tr = followList.GetArrayElementAtIndex(i).objectReferenceValue as Transform;
                    if (tr == null) continue;

                    list.Add(new FollowObject
                    {
                        transform = tr,
                        rotWeight = rotW,
                        posWeight = posW
                    });
                }

                Undo.RecordObject(reactor, "Migrate Follow Transforms");
                reactor.followObjects = list;
            }

            EditorUtility.SetDirty(reactor);
            Debug.Log($"✅ Migration complete on '{reactor.name}'");
        }
    }
}
