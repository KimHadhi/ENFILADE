using AdvancedZombieAIPro;
using UnityEditor;
using UnityEngine;

namespace AdvancedZombieAIProEditor
{
    [CustomEditor(typeof(ZombieProfile))]
    [CanEditMultipleObjects]
    internal sealed class ZombieProfileEditor : Editor
    {
        private SerializedProperty states;
        private SerializedProperty health;
        private SerializedProperty crawling;
        private SerializedProperty death;
        private SerializedProperty detection;
        private SerializedProperty idle;
        private SerializedProperty wander;
        private SerializedProperty alert;
        private SerializedProperty chase;
        private SerializedProperty search;
        private SerializedProperty inspect;
        private SerializedProperty hunger;
        private SerializedProperty stun;
        private SerializedProperty hit;
        private SerializedProperty attack;
        private SerializedProperty animator;

        private GUIStyle sectionHeaderStyle;
        private GUIStyle titleStyle;
        private GUIStyle subtitleStyle;
        private Color headerColor;
        private Color sectionColor;
        private Color accentColor;

        private void OnEnable()
        {
            states = serializedObject.FindProperty("states");
            health = serializedObject.FindProperty("health");
            crawling = serializedObject.FindProperty("crawling");
            death = serializedObject.FindProperty("death");
            detection = serializedObject.FindProperty("detection");
            idle = serializedObject.FindProperty("idle");
            wander = serializedObject.FindProperty("wander");
            alert = serializedObject.FindProperty("alert");
            chase = serializedObject.FindProperty("chase");
            search = serializedObject.FindProperty("search");
            inspect = serializedObject.FindProperty("inspect");
            hunger = serializedObject.FindProperty("hunger");
            stun = serializedObject.FindProperty("stun");
            hit = serializedObject.FindProperty("hit");
            attack = serializedObject.FindProperty("attack");
            animator = serializedObject.FindProperty("animator");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EnsureStyles();

            DrawProfileHeader();

            DrawStatesSection();
            DrawHealthSection();
            DrawHungerSection();
            DrawDetectionSection();
            DrawIdleSection();
            DrawWanderSection();
            DrawInspectSection();
            DrawSearchSection();
            DrawAlertSection();
            DrawChaseSection();
            DrawAttackSection();
            DrawHitSection();
            DrawStunSection();
            DrawCrawlingSection();
            DrawDeathSection();
            DrawAnimatorSection();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawStatesSection()
        {
            DrawSection("States", () =>
            {
                DrawRelative(states, "idleEnabled");
                DrawRelative(states, "wanderEnabled");
                DrawRelative(states, "alertEnabled");
                DrawRelative(states, "chaseEnabled");
                DrawRelative(states, "inspectSearchEnabled");
                DrawRelative(states, "stunEnabled");
                DrawRelative(states, "hitEnabled");
                DrawRelative(states, "attackEnabled");
                DrawRelative(states, "eatEnabled");

                SerializedProperty idleEnabled = states.FindPropertyRelative("idleEnabled");
                SerializedProperty wanderEnabled = states.FindPropertyRelative("wanderEnabled");
                if (!idleEnabled.boolValue && !wanderEnabled.boolValue &&
                    !idleEnabled.hasMultipleDifferentValues && !wanderEnabled.hasMultipleDifferentValues)
                {
                    EditorGUILayout.HelpBox("Idle and Wander are both disabled. The zombie may have no passive fallback state.", MessageType.Warning);
                }
            });
        }

        private void DrawHealthSection()
        {
            DrawSection("Health", () =>
            {
                DrawDelayedRelative(health, "maximumHealth");
                DrawDelayedRelative(health, "startingHealth");
                DrawRelative(health, "crawlingHealth");
                DrawRelative(health, "resetHealthOnAwake");

                WarnIfGreaterThan(
                    health.FindPropertyRelative("startingHealth"),
                    health.FindPropertyRelative("maximumHealth"),
                    "Starting Health is greater than Maximum Health and will be clamped.");
            });
        }

        private void DrawCrawlingSection()
        {
            DrawSection("Crawling", () =>
            {
                DrawRelative(crawling, "startingMode");
                DrawRelative(crawling, "speed");
            });
        }

        private void DrawDeathSection()
        {
            DrawSection("Death", () =>
            {
                DrawRelative(death, "CrawlAfterDeath");
                SerializedProperty deathMode = death.FindPropertyRelative("deathMode");
                DrawProperty(deathMode);

                bool showRagdollFields = deathMode.hasMultipleDifferentValues ||
                    deathMode.enumValueIndex == (int)ZombieDeathMode.Ragdoll;
                if (!showRagdollFields)
                {
                    return;
                }

                SerializedProperty applyRagdollForce = death.FindPropertyRelative("applyRagdollForce");
                DrawProperty(applyRagdollForce);

                bool showForceFields = applyRagdollForce.hasMultipleDifferentValues || applyRagdollForce.boolValue;
                if (showForceFields)
                {
                    DrawRelative(death, "ragdollForce");
                    DrawRelative(death, "ragdollForceMode");
                }
            });
        }

        private void DrawDetectionSection()
        {
            DrawSection("Detection", () =>
            {
                DrawRelative(detection, "eyeOffset");
                DrawRelative(detection, "fieldOfView");
                DrawRelative(detection, "viewDistance");
                DrawRelative(detection, "targetMask");
                DrawRelative(detection, "obstructionMask");
                DrawRelative(detection, "triggerInteraction");
                DrawRelative(detection, "raycastRegions");
                DrawDelayedRelative(detection, "minimumRegionsForChase");
                DrawDelayedRelative(detection, "minimumRegionsForInspect");
                DrawRelative(detection, "chaseConfirmationTime");
                DrawRelative(detection, "inspectConfirmationTime");
                DrawDetectionWarnings();
            });
        }

        private void DrawIdleSection()
        {
            DrawSection("Idle", () =>
            {
                SerializedProperty minimum = idle.FindPropertyRelative("minimumIdleTime");
                SerializedProperty maximum = idle.FindPropertyRelative("maximumIdleTime");
                DrawDelayedProperty(minimum);
                DrawDelayedProperty(maximum);
                WarnIfGreaterThan(minimum, maximum, "Minimum Idle Time is greater than Maximum Idle Time and will be clamped.");
            });
        }

        private void DrawWanderSection()
        {
            DrawSection("Wander", () =>
            {
                DrawRelative(wander, "wanderMode");
                DrawRelative(wander, "speed");
                DrawRelative(wander, "randomRadius");
                DrawRelative(wander, "destinationSampleRadius");
                DrawRelative(wander, "arrivalDistance");
                DrawRelative(wander, "idleChanceAfterDestination");

                SerializedProperty wanderMode = wander.FindPropertyRelative("wanderMode");
                if (!wanderMode.hasMultipleDifferentValues && wanderMode.enumValueIndex == (int)ZombieWanderMode.Waypoint)
                {
                    EditorGUILayout.HelpBox("Zombies using Waypoint mode must have a Zombie Waypoint Path assigned on each ZombieAI component.", MessageType.Info);
                }
            });
        }

        private void DrawAlertSection()
        {
            DrawSection("Alert", () =>
            {
                DrawRelative(alert, "animatorStateTag");
                DrawRelative(alert, "animatorFallbackTime");
                DrawRelative(alert, "faceTargetTurnSpeed");
                DrawRelative(alert, "alertChanceBeforeChase");
                SerializedProperty canAlertNearbyZombies = alert.FindPropertyRelative("canAlertNearbyZombies");
                DrawProperty(canAlertNearbyZombies);
                if (canAlertNearbyZombies.hasMultipleDifferentValues || canAlertNearbyZombies.boolValue)
                {
                    DrawRelative(alert, "nearbyAlertDelay");
                    DrawRelative(alert, "alertRadius");
                }
                DrawRelative(alert, "nearbyAlertResponseChance");
                DrawHeadLookFields(alert);
            });
        }

        private void DrawChaseSection()
        {
            DrawSection("Chase", () =>
            {
                DrawRelative(chase, "speed");
                DrawDelayedRelative(chase, "stoppingDistance");
                DrawRelative(chase, "repathInterval");
                DrawRelative(chase, "playerLoseTime");
                DrawHeadLookFields(chase);
            });
        }

        private void DrawHeadLookFields(SerializedProperty stateSettings)
        {
            SerializedProperty lookAtTarget = stateSettings.FindPropertyRelative("lookAtTarget");
            DrawProperty(lookAtTarget);

            if (lookAtTarget.hasMultipleDifferentValues || lookAtTarget.boolValue)
            {
                DrawRelative(stateSettings, "headLookHorizontalLimit");
                DrawRelative(stateSettings, "headLookVerticalLimit");
            }
        }

        private void DrawSearchSection()
        {
            DrawSection("Search", () =>
            {
                SerializedProperty minimum = search.FindPropertyRelative("minimumSearchTime");
                SerializedProperty maximum = search.FindPropertyRelative("maximumSearchTime");
                DrawDelayedProperty(minimum);
                DrawDelayedProperty(maximum);
                WarnIfGreaterThan(minimum, maximum, "Minimum Search Time is greater than Maximum Search Time and will be clamped.");
            });
        }

        private void DrawInspectSection()
        {
            DrawSection("Inspect", () =>
            {
                DrawRelative(inspect, "speed");
                SerializedProperty maximumRadius = inspect.FindPropertyRelative("maximumSearchRadius");
                SerializedProperty minimumRadius = inspect.FindPropertyRelative("minimumSearchRadius");
                DrawDelayedProperty(maximumRadius);
                DrawDelayedProperty(minimumRadius);
                DrawRelative(inspect, "destinationSampleRadius");
                DrawRelative(inspect, "arrivalDistance");
                DrawRelative(inspect, "searchChanceAfterArrival");
                DrawRelative(inspect, "repathInterval");
                SerializedProperty minimumLocations = inspect.FindPropertyRelative("minimumSearchLocations");
                SerializedProperty maximumLocations = inspect.FindPropertyRelative("maximumSearchLocations");
                DrawDelayedProperty(minimumLocations);
                DrawDelayedProperty(maximumLocations);

                WarnIfGreaterThan(minimumRadius, maximumRadius, "Minimum Search Radius is greater than Maximum Search Radius and will be clamped.");
                WarnIfGreaterThan(minimumLocations, maximumLocations, "Minimum Search Locations is greater than Maximum Search Locations and will be clamped.");
            });
        }

        private void DrawHungerSection()
        {
            DrawSection("Hunger", () =>
            {
                DrawRelative(hunger, "canEatDeadZombies");
                DrawRelative(hunger, "detectTargetWhileEating");
                DrawDelayedRelative(hunger, "maximumHunger");
                DrawDelayedRelative(hunger, "startingHunger");
                DrawDelayedRelative(hunger, "hungerThreshold");
                DrawRelative(hunger, "depletionRate");
                DrawRelative(hunger, "foodDetectionRadius");
                DrawRelative(hunger, "eatingDistance");
                DrawRelative(hunger, "moveSpeed");
                DrawRelative(hunger, "replenishRate");
                DrawRelative(hunger, "depletionPauseAfterEating");
                DrawRelative(hunger, "repathInterval");

                SerializedProperty maximumHunger = hunger.FindPropertyRelative("maximumHunger");
                WarnIfGreaterThan(hunger.FindPropertyRelative("startingHunger"), maximumHunger, "Starting Hunger is greater than Maximum Hunger and will be clamped.");
                WarnIfGreaterThan(hunger.FindPropertyRelative("hungerThreshold"), maximumHunger, "Hunger Threshold is greater than Maximum Hunger and will be clamped.");
            });
        }

        private void DrawStunSection()
        {
            DrawSection("Stun", () =>
            {
                SerializedProperty minimum = stun.FindPropertyRelative("minimumStunTime");
                SerializedProperty maximum = stun.FindPropertyRelative("maximumStunTime");
                DrawDelayedProperty(minimum);
                DrawDelayedProperty(maximum);
                WarnIfGreaterThan(minimum, maximum, "Minimum Stun Time is greater than Maximum Stun Time and will be clamped.");
            });
        }

        private void DrawHitSection()
        {
            DrawSection("Hit", () =>
            {
                DrawRelative(hit, "minimumDamageToReact");
                DrawRelative(hit, "aggroOnDamage");
                SerializedProperty faceHitSource = hit.FindPropertyRelative("faceHitSource");
                DrawProperty(faceHitSource);
                if (faceHitSource.hasMultipleDifferentValues || faceHitSource.boolValue)
                {
                    DrawRelative(hit, "faceHitSourceTurnSpeed");
                }

                DrawRelative(hit, "animatorStateTag");
                DrawRelative(hit, "animatorFallbackTime");
                DrawRelative(hit, "canInterruptIdle");
                DrawRelative(hit, "canInterruptWander");
                DrawRelative(hit, "canInterruptAlert");
                DrawRelative(hit, "canInterruptChase");
                DrawRelative(hit, "canInterruptInspectSearch");
                DrawRelative(hit, "canInterruptAttack");
            });
        }

        private void DrawAttackSection()
        {
            DrawSection("Attack", () =>
            {
                DrawDelayedRelative(attack, "attackDistance");
                DrawAttackDistanceWarning();
                DrawRelative(attack, "attackCooldown");
                DrawRelative(attack, "targetMovingSpeed");
                DrawRelative(attack, "faceTargetTurnSpeed");
                DrawRelative(attack, "invokeAttackEventsOnAttackStart");
                DrawHeadLookFields(attack);
            });
        }

        private void DrawAnimatorSection()
        {
            DrawSection("Animator", () =>
            {
                DrawRelative(animator, "idleBool");
                DrawRelative(animator, "wanderingBool");
                DrawRelative(animator, "alertedBool");
                DrawRelative(animator, "chasingBool");
                DrawRelative(animator, "searchingBool");
                DrawRelative(animator, "inspectingBool");
                DrawRelative(animator, "stunnedBool");
                DrawRelative(animator, "deadBool");
                DrawRelative(animator, "eatingBool");
                DrawRelative(animator, "hitReactionTrigger");
                DrawRelative(animator, "hitReactionTypeFloat");
                DrawRelative(animator, "attackTrigger");
                DrawRelative(animator, "attackTypeFloat");
                DrawRelative(animator, "isInCrawlModeBool");
                DrawRelative(animator, "crawlFallTrigger");
                DrawRelative(animator, "crawlStunnedBool");
                DrawRelative(animator, "crawlDeadBool");
                DrawRelative(animator, "crawlAttackTrigger");
                DrawRelative(animator, "locomotionBlendYFloat");
                DrawRelative(animator, "locomotionBlendXFloat");
                DrawRelative(animator, "locomotionBlendDampTime");
            });
        }

        private void DrawDetectionWarnings()
        {
            SerializedProperty raycastRegions = detection.FindPropertyRelative("raycastRegions");
            SerializedProperty minimumRegionsForChase = detection.FindPropertyRelative("minimumRegionsForChase");
            SerializedProperty minimumRegionsForInspect = detection.FindPropertyRelative("minimumRegionsForInspect");

            if (!raycastRegions.hasMultipleDifferentValues)
            {
                int selectedRegionCount = CountSelectedVisionRegions(raycastRegions.intValue);
                if (selectedRegionCount == 0)
                {
                    EditorGUILayout.HelpBox("No Raycast Regions are selected, so the zombie cannot confirm target visibility.", MessageType.Warning);
                }
                else
                {
                    if (!minimumRegionsForChase.hasMultipleDifferentValues &&
                        minimumRegionsForChase.intValue > selectedRegionCount)
                    {
                        EditorGUILayout.HelpBox("Minimum Regions For Chase is higher than the number of selected Raycast Regions.", MessageType.Warning);
                    }

                    if (!minimumRegionsForInspect.hasMultipleDifferentValues &&
                        minimumRegionsForInspect.intValue > selectedRegionCount)
                    {
                        EditorGUILayout.HelpBox("Minimum Regions For Inspect is higher than the number of selected Raycast Regions.", MessageType.Warning);
                    }
                }
            }

            if (!minimumRegionsForInspect.hasMultipleDifferentValues &&
                !minimumRegionsForChase.hasMultipleDifferentValues &&
                minimumRegionsForInspect.intValue > minimumRegionsForChase.intValue)
            {
                EditorGUILayout.HelpBox("Minimum Regions For Inspect is higher than Minimum Regions For Chase.", MessageType.Warning);
            }
        }

        private void DrawAttackDistanceWarning()
        {
            SerializedProperty attackDistance = attack.FindPropertyRelative("attackDistance");
            SerializedProperty chaseStoppingDistance = chase.FindPropertyRelative("stoppingDistance");

            if (attackDistance.hasMultipleDifferentValues || chaseStoppingDistance.hasMultipleDifferentValues)
            {
                return;
            }

            if (attackDistance.floatValue < chaseStoppingDistance.floatValue)
            {
                EditorGUILayout.HelpBox(
                    "Attack Distance is lower than Chase Stopping Distance. The zombie may stop before it gets close enough to attack.",
                    MessageType.Warning);
            }
        }

        private void DrawSection(string title, System.Action drawContent)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            Rect headerRect = EditorGUILayout.GetControlRect(false, 24f);
            EditorGUI.DrawRect(headerRect, sectionColor);
            EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.y, 4f, headerRect.height), accentColor);
            GUI.Label(new Rect(headerRect.x + 10f, headerRect.y, headerRect.width - 12f, headerRect.height), title, sectionHeaderStyle);

            EditorGUILayout.Space(2f);
            EditorGUI.indentLevel++;
            drawContent();
            EditorGUI.indentLevel--;
            EditorGUILayout.EndVertical();
        }

        private static void DrawRelative(SerializedProperty parent, string relativePropertyPath)
        {
            DrawProperty(parent.FindPropertyRelative(relativePropertyPath));
        }

        private static void DrawDelayedRelative(SerializedProperty parent, string relativePropertyPath)
        {
            DrawDelayedProperty(parent.FindPropertyRelative(relativePropertyPath));
        }

        private static void DrawProperty(SerializedProperty property)
        {
            if (property != null)
            {
                EditorGUILayout.PropertyField(property);
            }
        }

        private static void DrawDelayedProperty(SerializedProperty property)
        {
            ZombieInspectorGUI.DrawDelayedProperty(property);
        }

        private static void WarnIfGreaterThan(SerializedProperty minimum, SerializedProperty maximum, string message)
        {
            if (minimum == null || maximum == null ||
                minimum.hasMultipleDifferentValues || maximum.hasMultipleDifferentValues)
            {
                return;
            }

            if ((minimum.propertyType == SerializedPropertyType.Integer && minimum.intValue > maximum.intValue) ||
                (minimum.propertyType == SerializedPropertyType.Float && minimum.floatValue > maximum.floatValue))
            {
                EditorGUILayout.HelpBox(message, MessageType.Warning);
            }
        }

        private static int CountSelectedVisionRegions(int regions)
        {
            int count = 0;
            if ((regions & (int)ZombieVisionRegion.Top) != 0)
            {
                count++;
            }
            if ((regions & (int)ZombieVisionRegion.Bottom) != 0)
            {
                count++;
            }
            if ((regions & (int)ZombieVisionRegion.Center) != 0)
            {
                count++;
            }
            if ((regions & (int)ZombieVisionRegion.Left) != 0)
            {
                count++;
            }
            if ((regions & (int)ZombieVisionRegion.Right) != 0)
            {
                count++;
            }

            return count;
        }

        private void DrawProfileHeader()
        {
            EditorGUILayout.Space(4f);

            Rect headerRect = EditorGUILayout.GetControlRect(false, 48f);
            EditorGUI.DrawRect(headerRect, headerColor);
            EditorGUI.DrawRect(new Rect(headerRect.x, headerRect.y, 5f, headerRect.height), accentColor);

            Rect titleRect = new Rect(headerRect.x + 14f, headerRect.y + 7f, headerRect.width - 20f, 20f);
            Rect subtitleRect = new Rect(headerRect.x + 14f, headerRect.y + 27f, headerRect.width - 20f, 17f);
            GUI.Label(titleRect, "Zombie Profile", titleStyle);
            GUI.Label(subtitleRect, "AI behavior, detection, combat, and animation tuning", subtitleStyle);
        }

        private void EnsureStyles()
        {
            if (sectionHeaderStyle != null)
            {
                return;
            }

            bool isProSkin = EditorGUIUtility.isProSkin;
            headerColor = isProSkin ? new Color(0.16f, 0.18f, 0.20f) : new Color(0.82f, 0.86f, 0.90f);
            sectionColor = isProSkin ? new Color(0.20f, 0.22f, 0.24f) : new Color(0.89f, 0.91f, 0.94f);
            accentColor = new Color(0.20f, 0.67f, 0.78f);

            sectionHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                normal =
                {
                    textColor = isProSkin ? new Color(0.90f, 0.95f, 0.96f) : new Color(0.12f, 0.18f, 0.20f)
                }
            };

            titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                normal =
                {
                    textColor = isProSkin ? Color.white : new Color(0.08f, 0.12f, 0.14f)
                }
            };

            subtitleStyle = new GUIStyle(EditorStyles.label)
            {
                fontSize = 10,
                normal =
                {
                    textColor = isProSkin ? new Color(0.68f, 0.76f, 0.80f) : new Color(0.30f, 0.38f, 0.42f)
                }
            };
        }
    }
}
