using System;
using UnityEngine;

namespace AdvancedZombieAIPro
{
    [CreateAssetMenu(fileName = "ZombieProfile", menuName = "Advanced Zombie AI Pro/Zombie Profile")]
    public sealed class ZombieProfile : ScriptableObject
    {
        [Header("States")]
        [Tooltip("Controls which AI states this profile allows the zombie to enter.")]
        public ZombieStateSettings states = new ZombieStateSettings();

        [Header("Health")]
        [Tooltip("Health values used by the zombie in standing and crawling modes.")]
        public ZombieHealthSettings health = new ZombieHealthSettings();

        [Header("Crawling")]
        [Tooltip("Crawling mode setup, including starting mode and crawl movement speed.")]
        public ZombieCrawlingSettings crawling = new ZombieCrawlingSettings();

        [Header("Death")]
        [Tooltip("Death behavior, including animation/ragdoll handling and crawl-after-death behavior.")]
        public ZombieDeathSettings death = new ZombieDeathSettings();

        [Header("Detection")]
        [Tooltip("Vision, target masks, raycast regions, and detection confirmation settings.")]
        public ZombieVisionSettings detection = new ZombieVisionSettings();

        [Header("Idle")]
        [Tooltip("Idle state duration settings.")]
        public ZombieIdleSettings idle = new ZombieIdleSettings();

        [Header("Wander")]
        [Tooltip("Wandering mode, speed, sampling, arrival, and idle-after-destination settings.")]
        public ZombieWanderSettings wander = new ZombieWanderSettings();

        [Header("Alert")]
        [Tooltip("Alert state animation, timing, facing, and chance settings.")]
        public ZombieAlertSettings alert = new ZombieAlertSettings();

        [Header("Chase")]
        [Tooltip("Chase movement, stopping, repath, and target-loss settings.")]
        public ZombieChaseSettings chase = new ZombieChaseSettings();

        [Header("Search")]
        [Tooltip("Search-in-place timing settings.")]
        public ZombieSearchSettings search = new ZombieSearchSettings();

        [Header("Inspect")]
        [Tooltip("Inspect/search movement, radius, arrival, and search-cycle settings.")]
        public ZombieInspectSettings inspect = new ZombieInspectSettings();

        [Header("Hunger")]
        [Tooltip("Hunger, food detection, eating, and hunger recovery settings.")]
        public ZombieHungerSettings hunger = new ZombieHungerSettings();

        [Header("Stun")]
        [Tooltip("Stun duration settings.")]
        public ZombieStunSettings stun = new ZombieStunSettings();

        [Header("Hit")]
        [Tooltip("Damage reaction, hit aggro, hit facing, and hit interrupt settings.")]
        public ZombieHitSettings hit = new ZombieHitSettings();

        [Header("Attack")]
        [Tooltip("Attack distance, cooldown, facing, and attack event timing settings.")]
        public ZombieAttackSettings attack = new ZombieAttackSettings();

        [Header("Animator")]
        [Tooltip("Animator parameter names used by the zombie controller.")]
        public ZombieAnimatorSettings animator = new ZombieAnimatorSettings();

        public bool IsStateEnabled(ZombieStateType stateType)
        {
            switch (stateType)
            {
                case ZombieStateType.Idle:
                    return states.idleEnabled;
                case ZombieStateType.Wander:
                    return states.wanderEnabled;
                case ZombieStateType.Alert:
                    return states.alertEnabled;
                case ZombieStateType.Chase:
                    return states.chaseEnabled;
                case ZombieStateType.Search:
                    return states.inspectSearchEnabled;
                case ZombieStateType.Inspect:
                    return states.inspectSearchEnabled;
                case ZombieStateType.Stun:
                    return states.stunEnabled;
                case ZombieStateType.Hit:
                    return states.hitEnabled;
                case ZombieStateType.Attack:
                    return states.attackEnabled;
                case ZombieStateType.Eat:
                    return states.eatEnabled;
                case ZombieStateType.Dead:
                    return true;
                default:
                    return false;
            }
        }

        private void OnValidate()
        {
            detection.viewDistance = Mathf.Max(0.1f, detection.viewDistance);
            detection.fieldOfView = Mathf.Clamp(detection.fieldOfView, 1f, 360f);
            detection.minimumRegionsForChase = Mathf.Clamp(detection.minimumRegionsForChase, 1, 5);
            detection.minimumRegionsForInspect = Mathf.Clamp(detection.minimumRegionsForInspect, 1, detection.minimumRegionsForChase);
            detection.chaseConfirmationTime = Mathf.Max(0f, detection.chaseConfirmationTime);
            detection.inspectConfirmationTime = Mathf.Max(0f, detection.inspectConfirmationTime);

            health.maximumHealth = Mathf.Max(0.01f, health.maximumHealth);
            health.startingHealth = Mathf.Clamp(health.startingHealth, 0f, health.maximumHealth);
            health.crawlingHealth = Mathf.Max(0.01f, health.crawlingHealth);

            crawling.speed = Mathf.Max(0f, crawling.speed);

            death.ragdollForce = Mathf.Max(0f, death.ragdollForce);

            idle.minimumIdleTime = Mathf.Max(0f, idle.minimumIdleTime);
            idle.maximumIdleTime = Mathf.Max(idle.minimumIdleTime, idle.maximumIdleTime);

            wander.speed = Mathf.Max(0f, wander.speed);
            wander.randomRadius = Mathf.Max(0.1f, wander.randomRadius);
            wander.destinationSampleRadius = Mathf.Max(0.1f, wander.destinationSampleRadius);
            wander.arrivalDistance = Mathf.Max(0.05f, wander.arrivalDistance);
            wander.idleChanceAfterDestination = Mathf.Clamp01(wander.idleChanceAfterDestination);

            if (string.IsNullOrWhiteSpace(alert.animatorStateTag))
            {
                alert.animatorStateTag = "Alert";
            }
            alert.animatorFallbackTime = Mathf.Max(0f, alert.animatorFallbackTime);
            alert.faceTargetTurnSpeed = Mathf.Max(0f, alert.faceTargetTurnSpeed);
            alert.alertChanceBeforeChase = Mathf.Clamp01(alert.alertChanceBeforeChase);
            alert.nearbyAlertDelay = Mathf.Max(0f, alert.nearbyAlertDelay);
            alert.alertRadius = Mathf.Max(0f, alert.alertRadius);
            alert.nearbyAlertResponseChance = Mathf.Clamp01(alert.nearbyAlertResponseChance);
            alert.headLookHorizontalLimit = Mathf.Clamp(alert.headLookHorizontalLimit, 0f, 180f);
            alert.headLookVerticalLimit = Mathf.Clamp(alert.headLookVerticalLimit, 0f, 89f);

            chase.speed = Mathf.Max(0f, chase.speed);
            chase.stoppingDistance = Mathf.Max(0f, chase.stoppingDistance);
            chase.repathInterval = Mathf.Max(0.02f, chase.repathInterval);
            chase.playerLoseTime = Mathf.Max(0f, chase.playerLoseTime);
            chase.headLookHorizontalLimit = Mathf.Clamp(chase.headLookHorizontalLimit, 0f, 180f);
            chase.headLookVerticalLimit = Mathf.Clamp(chase.headLookVerticalLimit, 0f, 89f);

            search.minimumSearchTime = Mathf.Max(0.1f, search.minimumSearchTime);
            search.maximumSearchTime = Mathf.Max(search.minimumSearchTime, search.maximumSearchTime);

            inspect.speed = Mathf.Max(0f, inspect.speed);
            inspect.maximumSearchRadius = Mathf.Max(0.1f, inspect.maximumSearchRadius);
            inspect.minimumSearchRadius = Mathf.Clamp(inspect.minimumSearchRadius, 0f, inspect.maximumSearchRadius);
            inspect.destinationSampleRadius = Mathf.Max(0.1f, inspect.destinationSampleRadius);
            inspect.arrivalDistance = Mathf.Max(0.05f, inspect.arrivalDistance);
            inspect.searchChanceAfterArrival = Mathf.Clamp01(inspect.searchChanceAfterArrival);
            inspect.repathInterval = Mathf.Max(0.02f, inspect.repathInterval);
            inspect.minimumSearchLocations = Mathf.Max(1, inspect.minimumSearchLocations);
            inspect.maximumSearchLocations = Mathf.Max(inspect.minimumSearchLocations, inspect.maximumSearchLocations);

            hunger.maximumHunger = Mathf.Max(0.01f, hunger.maximumHunger);
            hunger.startingHunger = Mathf.Clamp(hunger.startingHunger, 0f, hunger.maximumHunger);
            hunger.hungerThreshold = Mathf.Clamp(hunger.hungerThreshold, 0f, hunger.maximumHunger);
            hunger.depletionRate = Mathf.Max(0f, hunger.depletionRate);
            hunger.foodDetectionRadius = Mathf.Max(0.1f, hunger.foodDetectionRadius);
            hunger.eatingDistance = Mathf.Max(0.05f, hunger.eatingDistance);
            hunger.moveSpeed = Mathf.Max(0f, hunger.moveSpeed);
            hunger.replenishRate = Mathf.Max(0.01f, hunger.replenishRate);
            hunger.depletionPauseAfterEating = Mathf.Max(0f, hunger.depletionPauseAfterEating);
            hunger.repathInterval = Mathf.Max(0.02f, hunger.repathInterval);

            stun.minimumStunTime = Mathf.Max(0.05f, stun.minimumStunTime);
            stun.maximumStunTime = Mathf.Max(stun.minimumStunTime, stun.maximumStunTime);

            hit.minimumDamageToReact = Mathf.Max(0f, hit.minimumDamageToReact);
            if (string.IsNullOrWhiteSpace(hit.animatorStateTag))
            {
                hit.animatorStateTag = "Hit";
            }
            hit.animatorFallbackTime = Mathf.Max(0f, hit.animatorFallbackTime);
            hit.faceHitSourceTurnSpeed = Mathf.Max(0f, hit.faceHitSourceTurnSpeed);

            attack.attackDistance = Mathf.Max(0.05f, attack.attackDistance);
            attack.attackCooldown = Mathf.Max(0.05f, attack.attackCooldown);
            attack.targetMovingSpeed = Mathf.Max(0f, attack.targetMovingSpeed);
            attack.faceTargetTurnSpeed = Mathf.Max(0f, attack.faceTargetTurnSpeed);
            attack.headLookHorizontalLimit = Mathf.Clamp(attack.headLookHorizontalLimit, 0f, 180f);
            attack.headLookVerticalLimit = Mathf.Clamp(attack.headLookVerticalLimit, 0f, 89f);
        }
    }

    [Serializable]
    public sealed class ZombieStateSettings
    {
        [Tooltip("Allows the zombie to enter the Idle state.")]
        public bool idleEnabled = true;
        [Tooltip("Allows the zombie to enter the Wander state.")]
        public bool wanderEnabled = true;
        [Tooltip("Allows the zombie to enter the Alert state.")]
        public bool alertEnabled = true;
        [Tooltip("Allows the zombie to enter the Chase state.")]
        public bool chaseEnabled = true;
        [InspectorName("Inspect/Search Enabled")]
        [Tooltip("Allows the zombie to enter both the Inspect and Search states.")]
        public bool inspectSearchEnabled = true;
        [Tooltip("Allows the zombie to enter the Stun state.")]
        public bool stunEnabled = true;
        [Tooltip("Allows the zombie to enter the Hit reaction state.")]
        public bool hitEnabled = true;
        [Tooltip("Allows the zombie to enter the Attack state.")]
        public bool attackEnabled = true;
        [Tooltip("Allows the zombie to enter the Eat state when hunger and food logic are active.")]
        public bool eatEnabled = true;
    }

    [Serializable]
    public sealed class ZombieHealthSettings
    {
        [Tooltip("Maximum health used while the zombie is standing.")]
        [Min(0.01f)] public float maximumHealth = 100f;
        [Tooltip("Health assigned when the zombie starts or resets in standing mode.")]
        [Min(0f)] public float startingHealth = 100f;
        [Tooltip("Health assigned when the zombie enters or starts in crawling mode.")]
        [Min(0.01f)] public float crawlingHealth = 50f;
        [Tooltip("Resets the zombie health from this profile when the health component awakens.")]
        public bool resetHealthOnAwake = true;
    }

    [Serializable]
    public sealed class ZombieCrawlingSettings
    {
        [Tooltip("Initial movement mode for this zombie when it starts.")]
        public ZombieMode startingMode = ZombieMode.Standing;
        [Tooltip("Movement speed used while crawling.")]
        [Min(0f)] public float speed = 0.8f;
    }

    [Serializable]
    public sealed class ZombieDeathSettings
    {
        [Tooltip("When enabled, a standing zombie enters crawling mode instead of fully dying when standing health reaches zero.")]
        public bool CrawlAfterDeath = false;
        [Tooltip("Determines whether final death uses an animation state or ragdoll physics.")]
        public ZombieDeathMode deathMode = ZombieDeathMode.Animation;
        [Tooltip("Applies an impulse to the ragdoll when ragdoll death is used.")]
        public bool applyRagdollForce = true;
        [Tooltip("Strength of the force applied to the ragdoll on death.")]
        [Min(0f)] public float ragdollForce = 7f;
        [Tooltip("Unity force mode used when applying ragdoll death force.")]
        public ForceMode ragdollForceMode = ForceMode.Impulse;
    }

    [Serializable]
    public sealed class ZombieVisionSettings
    {
        [Tooltip("Local offset from the zombie transform used as the origin for vision rays.")]
        public Vector3 eyeOffset = new Vector3(0f, 1.7f, 0f);
        [Tooltip("Horizontal field of view angle used by zombie vision.")]
        [Range(1f, 360f)] public float fieldOfView = 100f;
        [Tooltip("Maximum distance at which the zombie can detect targets.")]
        [Min(0.1f)] public float viewDistance = 18f;
        [Tooltip("Layers that can be detected as valid targets.")]
        public LayerMask targetMask = ~0;
        [Tooltip("Layers that block vision rays between the zombie and target.")]
        public LayerMask obstructionMask = ~0;
        [Tooltip("Controls whether trigger colliders are considered by vision checks.")]
        public QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.Ignore;
        [Tooltip("Target body regions used by the multi-ray vision test.")]
        public ZombieVisionRegion raycastRegions = ZombieVisionRegion.Top |
            ZombieVisionRegion.Bottom |
            ZombieVisionRegion.Center |
            ZombieVisionRegion.Left |
            ZombieVisionRegion.Right;
        [Tooltip("Number of visible raycast regions required before the zombie can chase.")]
        [Range(1, 5)] public int minimumRegionsForChase = 3;
        [Tooltip("Number of visible raycast regions required before the zombie can inspect.")]
        [Range(1, 5)] public int minimumRegionsForInspect = 1;
        [Tooltip("Time the target must remain chase-detectable before chase is confirmed.")]
        [Min(0f)] public float chaseConfirmationTime = 1.5f;
        [Tooltip("Time the target must remain inspect-detectable before inspect is confirmed.")]
        [Min(0f)] public float inspectConfirmationTime = 0.5f;
    }

    [Serializable]
    public sealed class ZombieIdleSettings
    {
        [Tooltip("Shortest time the zombie can stay idle.")]
        [Min(0f)] public float minimumIdleTime = 1.5f;
        [Tooltip("Longest time the zombie can stay idle.")]
        [Min(0f)] public float maximumIdleTime = 4f;
    }

    [Serializable]
    public sealed class ZombieWanderSettings
    {
        [Tooltip("Chooses whether the zombie wanders randomly or follows a waypoint path.")]
        public ZombieWanderMode wanderMode = ZombieWanderMode.Random;
        [Tooltip("Movement speed used while wandering.")]
        [Min(0f)] public float speed = 1.4f;
        [Tooltip("Radius around the zombie used when choosing random wander destinations.")]
        [Min(0.1f)] public float randomRadius = 10f;
        [Tooltip("Radius used when sampling candidate wander or waypoint positions on the NavMesh.")]
        [Min(0.1f)] public float destinationSampleRadius = 2f;
        [Tooltip("Distance from the destination at which the zombie counts the wander destination as reached.")]
        [Min(0.05f)] public float arrivalDistance = 0.5f;
        [Tooltip("Chance that the zombie enters Idle after reaching a wander destination.")]
        [Range(0f, 1f)] public float idleChanceAfterDestination = 0.45f;
    }

    [Serializable]
    public sealed class ZombieAlertSettings
    {
        [Tooltip("Animator state tag used to identify the one-shot Alert animation state.")]
        public string animatorStateTag = "Alert";
        [Tooltip("Used only if the Animator never enters a state with the Alert tag, so the AI cannot get stuck forever.")]
        [Min(0f)] public float animatorFallbackTime = 2f;
        [Tooltip("Turn speed used while the alert state faces the target or last known target position.")]
        [Min(0f)] public float faceTargetTurnSpeed = 12f;
        [Tooltip("Chance that the zombie plays Alert before Chase when it newly detects or reacquires a target. 0 means never alert first, 1 means always alert first.")]
        [Range(0f, 1f)] public float alertChanceBeforeChase = 0.65f;
        [Tooltip("Allows this zombie to alert nearby zombies when it enters Alert.")]
        public bool canAlertNearbyZombies = false;
        [Tooltip("Seconds to wait after entering Alert before broadcasting the alert to nearby zombies.")]
        [Min(0f)] public float nearbyAlertDelay = 0.75f;
        [Tooltip("Radius used by this zombie when broadcasting Alert to nearby zombies.")]
        [Min(0f)] public float alertRadius = 8f;
        [Tooltip("Chance this zombie responds when another zombie alerts it from within that zombie's Alert Radius.")]
        [Range(0f, 1f)] public float nearbyAlertResponseChance = 0.65f;
        [Tooltip("Allows the zombie head to look toward the target while in Alert.")]
        public bool lookAtTarget = true;
        [Tooltip("Maximum horizontal head look angle, in degrees, while Alert look-at is enabled.")]
        [Range(0f, 180f)] public float headLookHorizontalLimit = 70f;
        [Tooltip("Maximum vertical head look angle, in degrees, while Alert look-at is enabled.")]
        [Range(0f, 89f)] public float headLookVerticalLimit = 35f;
    }

    [Serializable]
    public sealed class ZombieChaseSettings
    {
        [Tooltip("Movement speed used while chasing a target.")]
        [Min(0f)] public float speed = 3.6f;
        [Tooltip("NavMeshAgent stopping distance used while chasing.")]
        [Min(0f)] public float stoppingDistance = 1.2f;
        [Tooltip("How often the zombie refreshes its chase destination.")]
        [Min(0.02f)] public float repathInterval = 0.15f;
        [Tooltip("Seconds the zombie keeps chasing after chase-level vision is broken before it truly loses the player.")]
        [Min(0f)] public float playerLoseTime = 1.25f;
        [Tooltip("Allows the zombie head to look toward the target while in Chase.")]
        public bool lookAtTarget = true;
        [Tooltip("Maximum horizontal head look angle, in degrees, while Chase look-at is enabled.")]
        [Range(0f, 180f)] public float headLookHorizontalLimit = 70f;
        [Tooltip("Maximum vertical head look angle, in degrees, while Chase look-at is enabled.")]
        [Range(0f, 89f)] public float headLookVerticalLimit = 35f;
    }

    [Serializable]
    public sealed class ZombieSearchSettings
    {
        [Tooltip("Shortest time the zombie searches in place before moving to another search step.")]
        [Min(0.1f)] public float minimumSearchTime = 0.8f;
        [Tooltip("Longest time the zombie searches in place before moving to another search step.")]
        [Min(0.1f)] public float maximumSearchTime = 2.5f;
    }

    [Serializable]
    public sealed class ZombieInspectSettings
    {
        [Tooltip("Movement speed used while inspecting suspicious positions and search steps.")]
        [Min(0f)] public float speed = 1.8f;
        [Tooltip("Maximum radius from the search origin used when choosing search step destinations.")]
        [Min(0.1f)] public float maximumSearchRadius = 3f;
        [Tooltip("Minimum radius from the zombie used when choosing search step destinations.")]
        [Min(0f)] public float minimumSearchRadius = 1f;
        [Tooltip("Radius used when sampling inspect/search destinations on the NavMesh.")]
        [Min(0.1f)] public float destinationSampleRadius = 1.5f;
        [Tooltip("Distance from the inspect destination at which the zombie counts it as reached.")]
        [Min(0.05f)] public float arrivalDistance = 0.6f;
        [Tooltip("Chance that the zombie starts a full search cycle after reaching a suspicious inspect position.")]
        [Range(0f, 1f)] public float searchChanceAfterArrival = 0.65f;
        [Tooltip("How often the zombie refreshes its inspect destination while tracking partial target visibility.")]
        [Min(0.02f)] public float repathInterval = 0.25f;
        [Tooltip("Minimum number of search locations visited in a search cycle.")]
        [Min(1)] public int minimumSearchLocations = 3;
        [Tooltip("Maximum number of search locations visited in a search cycle.")]
        [Min(1)] public int maximumSearchLocations = 5;
    }

    [Serializable]
    public sealed class ZombieHungerSettings
    {
        [Tooltip("Allows this zombie to treat dead zombies as food sources.")]
        public bool canEatDeadZombies = true;
        [Tooltip("Allows target detection to interrupt eating.")]
        public bool detectTargetWhileEating = true;
        [Tooltip("Maximum hunger value.")]
        [Min(0.01f)] public float maximumHunger = 100f;
        [Tooltip("Hunger value assigned when hunger initializes.")]
        [Min(0f)] public float startingHunger = 100f;
        [Tooltip("Hunger value at or below which the zombie starts looking for food.")]
        [Min(0f)] public float hungerThreshold = 35f;
        [Tooltip("Hunger lost per second while hunger depletion is active.")]
        [Min(0f)] public float depletionRate = 1f;
        [Tooltip("Radius used to search for available food.")]
        [Min(0.1f)] public float foodDetectionRadius = 8f;
        [Tooltip("Distance from food at which the zombie can start eating.")]
        [Min(0.05f)] public float eatingDistance = 1.2f;
        [Tooltip("Movement speed used while moving toward food.")]
        [Min(0f)] public float moveSpeed = 1.6f;
        [Tooltip("Hunger restored per second while eating.")]
        [Min(0.01f)] public float replenishRate = 15f;
        [Tooltip("Delay before hunger starts depleting again after eating.")]
        [Min(0f)] public float depletionPauseAfterEating = 3f;
        [Tooltip("How often the zombie refreshes its path to the current food target.")]
        [Min(0.02f)] public float repathInterval = 0.25f;
    }

    [Serializable]
    public sealed class ZombieAttackSettings
    {
        [Tooltip("Distance at which the zombie can attack the target.")]
        [Min(0.05f)] public float attackDistance = 1.6f;
        [Tooltip("Minimum time between attacks.")]
        [Min(0.05f)] public float attackCooldown = 1.25f;
        [Tooltip("Target speed threshold used to choose moving-target attack behavior.")]
        [Min(0f)] public float targetMovingSpeed = 0.15f;
        [Tooltip("Turn speed used while facing the target during attack behavior.")]
        [Min(0f)] public float faceTargetTurnSpeed = 12f;
        [Tooltip("Invokes attack UnityEvents immediately when an attack starts instead of waiting for animation events.")]
        public bool invokeAttackEventsOnAttackStart = true;
        [Tooltip("Allows the zombie head to look toward the target while attacking.")]
        public bool lookAtTarget = true;
        [Tooltip("Maximum horizontal head look angle, in degrees, while Attack look-at is enabled.")]
        [Range(0f, 180f)] public float headLookHorizontalLimit = 70f;
        [Tooltip("Maximum vertical head look angle, in degrees, while Attack look-at is enabled.")]
        [Range(0f, 89f)] public float headLookVerticalLimit = 35f;
    }

    [Serializable]
    public sealed class ZombieStunSettings
    {
        [Tooltip("Shortest random stun duration.")]
        [Min(0.05f)] public float minimumStunTime = 2f;
        [Tooltip("Longest random stun duration.")]
        [Min(0.05f)] public float maximumStunTime = 4f;
    }

    [Serializable]
    public sealed class ZombieHitSettings
    {
        [Tooltip("Small damage below this value will reduce health but will not play the Hit reaction.")]
        [Min(0f)] public float minimumDamageToReact = 0f;
        [Tooltip("When enabled, damage can make the zombie remember or target the attacker.")]
        public bool aggroOnDamage = true;
        [Tooltip("When enabled, the standing hit reaction turns toward the hit source.")]
        public bool faceHitSource = true;
        [Tooltip("Turn speed used while facing the hit source during a hit reaction.")]
        [Min(0f)] public float faceHitSourceTurnSpeed = 12f;
        [Tooltip("Animator state tag used to identify the one-shot Hit reaction animation state.")]
        public string animatorStateTag = "Hit";
        [Tooltip("Used only if the Animator never enters a state with the Hit tag, so the AI cannot get stuck forever.")]
        [Min(0f)] public float animatorFallbackTime = 1f;
        [Tooltip("Allows damage to interrupt Idle with a hit reaction.")]
        public bool canInterruptIdle = true;
        [Tooltip("Allows damage to interrupt Wander with a hit reaction.")]
        public bool canInterruptWander = true;
        [Tooltip("Allows damage to interrupt Alert with a hit reaction.")]
        public bool canInterruptAlert = true;
        [Tooltip("Allows damage to interrupt Chase with a hit reaction.")]
        public bool canInterruptChase = true;
        [InspectorName("Can Interrupt Inspect/Search")]
        [Tooltip("Allows damage to interrupt Inspect or Search with a hit reaction.")]
        public bool canInterruptInspectSearch = true;
        [Tooltip("Allows damage to interrupt Attack with a hit reaction.")]
        public bool canInterruptAttack = false;
    }

    [Serializable]
    public sealed class ZombieAnimatorSettings
    {
        [Tooltip("Animator bool set while the zombie is in Idle.")]
        public string idleBool = "IsIdle";
        [Tooltip("Animator bool set while the zombie is in Wander.")]
        public string wanderingBool = "IsWandering";
        [Tooltip("Animator bool set while the zombie is in Alert.")]
        public string alertedBool = "IsAlerted";
        [Tooltip("Animator bool set while the zombie is in Chase.")]
        public string chasingBool = "IsChasing";
        [Tooltip("Animator bool set while the zombie is in Search.")]
        public string searchingBool = "IsSearching";
        [Tooltip("Animator bool set while the zombie is in Inspect.")]
        public string inspectingBool = "IsInspecting";
        [Tooltip("Animator bool set while the zombie is in Stun.")]
        public string stunnedBool = "IsStunned";
        [Tooltip("Animator bool set while the zombie is dead.")]
        public string deadBool = "IsDead";
        [Tooltip("Animator bool set while the zombie is eating.")]
        public string eatingBool = "IsEating";
        [Tooltip("Animator trigger fired when the zombie plays a hit reaction.")]
        public string hitReactionTrigger = "HitReaction";
        [Tooltip("Animator float used to choose the hit reaction type.")]
        public string hitReactionTypeFloat = "HitReactionType";
        [Tooltip("Animator trigger fired when the zombie attacks.")]
        public string attackTrigger = "Attack";
        [Tooltip("Animator float used to choose the attack type.")]
        public string attackTypeFloat = "AttackType";
        [Tooltip("Animator bool set when the zombie is in crawling mode.")]
        public string isInCrawlModeBool = "IsInCrawlMode";
        [Tooltip("Animator trigger fired when standing health depletes and the zombie falls into crawling mode.")]
        public string crawlFallTrigger = "CrawlFall";
        [Tooltip("Animator bool set while a crawling zombie is stunned.")]
        public string crawlStunnedBool = "IsCrawlStunned";
        [Tooltip("Animator bool set while a crawling zombie is dead.")]
        public string crawlDeadBool = "IsCrawlDead";
        [Tooltip("Animator trigger fired when a crawling zombie attacks.")]
        public string crawlAttackTrigger = "AttackCrawl";
        [Tooltip("2D locomotion Y parameter: 0 stopped, 1 full configured movement speed.")]
        public string locomotionBlendYFloat = "LocomotionBlendY";
        [Tooltip("2D locomotion X parameter: -1 wander/inspect movement, 0 idle/stopped, 1 chase movement.")]
        public string locomotionBlendXFloat = "LocomotionBlendX";
        [Tooltip("Damp time used when updating 2D locomotion blend parameters.")]
        [Min(0f)] public float locomotionBlendDampTime = 0.1f;
    }
}
