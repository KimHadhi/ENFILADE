using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.Events;

namespace AdvancedZombieAIPro
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class ZombieAI : MonoBehaviour
    {
        private static readonly List<ZombieAI> ActiveZombies = new List<ZombieAI>();
        private static readonly ZombieVisionRegion[] VisionRegionOrder =
        {
            ZombieVisionRegion.Top,
            ZombieVisionRegion.Bottom,
            ZombieVisionRegion.Center,
            ZombieVisionRegion.Left,
            ZombieVisionRegion.Right
        };
        private static readonly Color DebugFovColor = new Color(1f, 0.92f, 0.15f, 0.85f);
        private static readonly Color DebugVisibleRayColor = new Color(0.15f, 1f, 0.25f, 0.95f);
        private static readonly Color DebugBlockedRayColor = new Color(1f, 0.15f, 0.1f, 0.95f);
        private static readonly Color DebugInactiveRayColor = new Color(1f, 0.55f, 0.05f, 0.75f);
        private static readonly Color DebugRayRemainderColor = new Color(0.55f, 0.55f, 0.55f, 0.45f);
        private const int DebugFovArcSegments = 32;
        private const float WanderBlendX = -1f;
        private const float IdleBlendX = 0f;
        private const float ChaseBlendX = 1f;
        private const float StandReactionType = 0f;
        private const float ChaseReactionType = 1f;
        private const int CrawlingAnimatorLayerIndex = 1;
        private const int UpperBodyAnimatorLayerIndex = 2;
        private const string FallingAnimatorTag = "Falling";
        private const float CrawlingFallAnimatorFallbackTime = 5f;
        private const float HeadLookFadeSpeed = 6f;
        private const float HeadLookBodyWeight = 0.08f;
        private const float HeadLookWeight = 0.9f;
        private const float HeadLookEyesWeight = 0f;
        private const float HeadLookClampWeight = 0.45f;

        [SerializeField] private ZombieProfile profile = null;
        [SerializeField] private ZombieTarget target = null;
        [SerializeField] private NavMeshAgent agent = null;
        [SerializeField] private Animator animator = null;
        [SerializeField] private ZombieHealth health = null;
        [SerializeField] private ZombieRagdollController ragdollController = null;
        [SerializeField] private bool debugMode;
        private string currentStateName = "None";
        private ZombieMode currentMode = ZombieMode.Standing;

        private float currentHunger = 100f;

        [SerializeField] private ZombieWaypointPath waypointPath = null;

        [SerializeField] private ZombieAttackEvent onStandAttack = new ZombieAttackEvent();
        [SerializeField] private ZombieAttackEvent onChaseAttack = new ZombieAttackEvent();

        private readonly Dictionary<ZombieStateType, ZombieState> states = new Dictionary<ZombieStateType, ZombieState>();
        private readonly Dictionary<int, AnimatorControllerParameterType> animatorParameterTypes = new Dictionary<int, AnimatorControllerParameterType>();

        private ZombieState currentState;
        private Collider[] rootColliders = Array.Empty<Collider>();
        private RuntimeAnimatorController cachedAnimatorController;
        private Vector3 lastKnownTargetPosition;
        private Vector3 suspiciousPosition;
        private bool hasLastKnownTargetPosition;
        private bool hasSuspiciousPosition;
        private int visibleRegionCount;
        private float chaseDetectionTimer;
        private float inspectDetectionTimer;
        private bool hasFullDetection;
        private bool hasPartialDetection;
        private int waypointIndex = -1;
        private bool hasQueuedIdleDuration;
        private float queuedIdleDuration;
        private ZombieAttackKind pendingAttackKind;
        private bool hasPendingAttackEvent;
        private bool searchCycleActive;
        private Vector3 searchOrigin;
        private int searchLocationTargetCount;
        private int searchedLocationCount;
        private ZombieDamageInfo lastDamageInfo;
        private Vector3 hitSourcePosition;
        private Vector3 localDamageSearchPosition;
        private bool hasLastDamageInfo;
        private bool hasHitSourcePosition;
        private bool hasDamageAggro;
        private bool hasLocalDamageSearch;
        private ZombieDamageInfo deathDamageInfo;
        private bool hasDeathDamageInfo;
        private bool isDead;
        private bool deathApplied;
        private bool modeInitialized;
        private bool crawlingFallInProgress;
        private bool crawlingFallAnimationStarted;
        private float crawlingFallElapsedTime;
        private ZombieFood currentFood;
        private bool hungerInitialized;
        private bool isEatingFood;
        private float hungerDepletionPauseRemaining;
        private float requestedStunDuration = -1f;
        private float currentHeadLookWeight;
        private int lastHeadLookIKFrame = -1;
        private bool suppressNextAlertPropagation;
        private ZombieAttackKind lastAttackKind = ZombieAttackKind.StandAttack;

        public ZombieProfile Profile => profile;
        public ZombieTarget Target => target;
        public NavMeshAgent Agent => agent;
        public Animator Animator => animator;
        public ZombieHealth Health => health;
        public ZombieRagdollController RagdollController => ragdollController;
        public bool DebugMode => debugMode;
        public bool IsDead => isDead;
        public ZombieMode CurrentMode => currentMode;
        public bool IsCrawling => modeInitialized
            ? currentMode == ZombieMode.Crawling
            : profile != null && profile.crawling.startingMode == ZombieMode.Crawling;
        public float CurrentMaximumHealth => profile != null && IsCrawling
            ? profile.health.crawlingHealth
            : profile != null ? profile.health.maximumHealth : 100f;
        public float ConfiguredStartingHealth => profile != null && profile.crawling.startingMode == ZombieMode.Crawling
            ? profile.health.crawlingHealth
            : profile != null ? profile.health.startingHealth : 100f;
        public float ConfiguredStartingMaximumHealth => profile != null && profile.crawling.startingMode == ZombieMode.Crawling
            ? profile.health.crawlingHealth
            : profile != null ? profile.health.maximumHealth : 100f;
        public ZombieFood CurrentFood => currentFood;
        public ZombieWaypointPath WaypointPath => waypointPath;
        public float CurrentHunger => currentHunger;
        public bool IsHungerEnabled => IsStateEnabled(ZombieStateType.Eat);
        public ZombieStateType? CurrentStateType => currentState != null ? currentState.StateType : (ZombieStateType?)null;
        public string CurrentStateName => currentStateName;
        public int VisibleRegionCount => visibleRegionCount;
        public bool HasFullDetection => hasFullDetection;
        public bool HasChaseVisibility => profile != null && visibleRegionCount >= profile.detection.minimumRegionsForChase;
        public bool HasPartialDetection => hasPartialDetection;
        public bool HasAnyDetection => visibleRegionCount > 0;
        public bool HasDamageAggro => profile != null && profile.hit.aggroOnDamage && hasDamageAggro;
        public Vector3 LastKnownTargetPosition => lastKnownTargetPosition;
        public Vector3 SuspiciousPosition => suspiciousPosition;
        public bool HasLastKnownTargetPosition => hasLastKnownTargetPosition;
        public bool HasSuspiciousPosition => hasSuspiciousPosition;
        public bool IsSearchCycleActive => searchCycleActive;
        public bool IsSearchCycleComplete => searchCycleActive && searchedLocationCount >= searchLocationTargetCount;
        public Vector3 SearchOrigin => searchOrigin;
        public ZombieDamageInfo LastDamageInfo => lastDamageInfo;
        public Vector3 HitSourcePosition => hitSourcePosition;
        public bool HasLastDamageInfo => hasLastDamageInfo;
        public bool HasHitSourcePosition => hasHitSourcePosition;
        public ZombieAttackEvent OnStandAttack => onStandAttack;
        public ZombieAttackEvent OnChaseAttack => onChaseAttack;
        public ZombieAttackKind LastAttackKind => lastAttackKind;
        public event Action<ZombieAI, ZombieTarget, ZombieAttackKind> AttackAnimationHit;

        private void Reset()
        {
            agent = GetComponent<NavMeshAgent>();
            animator = GetComponentInChildren<Animator>();
            health = GetComponent<ZombieHealth>();
            ragdollController = GetComponent<ZombieRagdollController>();
            CacheRootColliders();
        }

        private void Awake()
        {
            if (agent == null)
            {
                agent = GetComponent<NavMeshAgent>();
            }

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            if (health == null)
            {
                health = GetComponent<ZombieHealth>();
            }

            if (ragdollController == null)
            {
                ragdollController = GetComponent<ZombieRagdollController>();
            }

            if (target == null)
            {
                target = FindAnyObjectByType<ZombieTarget>();
                if (target == null)
                {
                    Debug.LogWarning("There is no Zombie Target in the scene.");
                }
            }

            CacheRootColliders();
            BuildStates();
            CacheAnimatorParameters();
            InitializeModeFromProfile();
        }

        private void OnEnable()
        {
            if (!ActiveZombies.Contains(this))
            {
                ActiveZombies.Add(this);
            }
        }

        private void Start()
        {
            InitializeModeFromProfile();
            ApplyStartingModeHealth();
            EnsureHungerInitialized();

            if (health != null && !health.IsAlive)
            {
                HandleDeath(CreateCurrentHealthDeathInfo());
                return;
            }

            if (profile != null && currentState == null)
            {
                ChangeState(ChoosePassiveState());
            }
        }

        private void Update()
        {
            if (isDead)
            {
                currentState?.Tick(Time.deltaTime);
                return;
            }

            if (profile == null)
            {
                return;
            }

            if (states.Count == 0)
            {
                BuildStates();
            }

            RefreshAnimatorParameterCacheIfNeeded();
            if (crawlingFallInProgress)
            {
                TickCrawlingFallTransition(Time.deltaTime);
                UpdateLocomotionAnimator(Time.deltaTime);
                return;
            }

            EnsureHungerInitialized();
            UpdateHunger(Time.deltaTime);
            if (IsBehaviorLocked())
            {
                ResetDetection();
            }
            else
            {
                EvaluateVision(Time.deltaTime);
            }

            if (currentState == null)
            {
                ChangeState(ChoosePassiveState());
            }

            currentState?.Tick(Time.deltaTime);
            TryStartEatingIfHungry();
            UpdateLocomotionAnimator(Time.deltaTime);
        }

        private void OnDisable()
        {
            ActiveZombies.Remove(this);
            currentState?.Exit();
            currentState = null;
            currentStateName = "None";
        }

        private void OnValidate()
        {
            if (agent == null)
            {
                agent = GetComponent<NavMeshAgent>();
            }

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            if (health == null)
            {
                health = GetComponent<ZombieHealth>();
            }

            if (ragdollController == null)
            {
                ragdollController = GetComponent<ZombieRagdollController>();
            }

            CacheRootColliders();
        }

        private void OnDrawGizmos()
        {
            if (!debugMode || profile == null)
            {
                return;
            }

            DrawDebugVisionGizmos();
        }

        private void OnAnimatorIK(int layerIndex)
        {
            ApplyHeadLookIK();
        }

        public void SetTarget(ZombieTarget newTarget)
        {
            target = newTarget;
            hasDamageAggro = false;
            ResetDetection();
        }

        public void ClearTarget()
        {
            target = null;
            hasDamageAggro = false;
            ResetDetection();
        }

        public void SetDebugMode(bool enabled)
        {
            debugMode = enabled;
        }

        public bool Stun()
        {
            return ApplyStun(-1f);
        }

        public bool Stun(float duration)
        {
            return ApplyStun(duration);
        }

        private bool ApplyStun(float duration)
        {
            if (isDead)
            {
                return false;
            }

            if (crawlingFallInProgress)
            {
                return false;
            }

            if (profile == null)
            {
                return false;
            }

            if (!IsStateEnabled(ZombieStateType.Stun))
            {
                Log("Stun was requested, but Stun state is disabled.");
                return false;
            }

            requestedStunDuration = duration > 0f ? duration : -1f;

            if (currentState is ZombieStunState stunState)
            {
                stunState.Restart();
                return true;
            }

            return ChangeState(ZombieStateType.Stun);
        }

        public void ReceiveHit(float damage)
        {
            ReceiveHit(damage, null, transform.position, false);
        }

        public void ReceiveHit(float damage, GameObject attacker)
        {
            Vector3 hitPoint = attacker != null ? attacker.transform.position : transform.position;
            ReceiveHit(damage, attacker, hitPoint, attacker != null);
        }

        public void ReceiveHit(float damage, GameObject attacker, Vector3 hitPoint)
        {
            ReceiveHit(damage, attacker, hitPoint, true);
        }

        public void ReceiveHit(float damage, GameObject attacker, Vector3 hitPoint, Vector3 hitDirection)
        {
            ReceiveHit(damage, attacker, hitPoint, true, hitDirection, hitDirection.sqrMagnitude > 0.0001f);
        }

        public void ReceiveHit(float damage, Component attacker)
        {
            ReceiveHit(damage, attacker != null ? attacker.gameObject : null);
        }

        public void ReceiveHit(float damage, Component attacker, Vector3 hitPoint)
        {
            ReceiveHit(damage, attacker != null ? attacker.gameObject : null, hitPoint);
        }

        public void ReceiveHit(float damage, Component attacker, Vector3 hitPoint, Vector3 hitDirection)
        {
            ReceiveHit(damage, attacker != null ? attacker.gameObject : null, hitPoint, hitDirection);
        }

        private void ReceiveHit(float damage, GameObject attacker, Vector3 hitPoint, bool hasHitPoint)
        {
            ReceiveHit(damage, attacker, hitPoint, hasHitPoint, Vector3.zero, false);
        }

        private void ReceiveHit(float damage, GameObject attacker, Vector3 hitPoint, bool hasHitPoint, Vector3 hitDirection, bool hasHitDirection)
        {
            if (isDead)
            {
                return;
            }

            if (health == null)
            {
                health = GetComponent<ZombieHealth>();
            }

            if (health != null)
            {
                if (hasHitPoint && hasHitDirection)
                {
                    health.ApplyDamage(damage, attacker, hitPoint, hitDirection);
                }
                else if (hasHitPoint)
                {
                    health.ApplyDamage(damage, attacker, hitPoint);
                }
                else
                {
                    health.ApplyDamage(damage, attacker);
                }

                return;
            }

            ZombieDamageInfo damageInfo = new ZombieDamageInfo
            {
                requestedDamage = damage,
                appliedDamage = Mathf.Max(0f, damage),
                previousHealth = 0f,
                currentHealth = 0f,
                attacker = attacker,
                hitPoint = hitPoint,
                hasHitPoint = hasHitPoint,
                hitDirection = hasHitDirection ? hitDirection.normalized : Vector3.zero,
                hasHitDirection = hasHitDirection,
                time = Time.time
            };
            HandleDamage(damageInfo);
        }

        public void HearNoise(Vector3 noisePosition, float noiseIntensity)
        {
            if (isDead)
            {
                return;
            }

            if (crawlingFallInProgress)
            {
                return;
            }

            if (profile == null)
            {
                return;
            }

            if (currentState != null && currentState.StateType == ZombieStateType.Eat && !profile.hunger.detectTargetWhileEating)
            {
                Log($"Heard noise at {noisePosition}, but ignored it while eating.");
                return;
            }

            if (currentState != null && (currentState.StateType == ZombieStateType.Alert ||
                currentState.StateType == ZombieStateType.Chase ||
                currentState.StateType == ZombieStateType.Stun ||
                currentState.StateType == ZombieStateType.Hit ||
                currentState.StateType == ZombieStateType.Attack ||
                currentState.StateType == ZombieStateType.Dead))
            {
                Log($"Heard noise at {noisePosition}, but kept current target priority.");
                return;
            }

            SetSuspiciousPosition(noisePosition);
            EndSearchCycle();
            Log($"Heard noise with intensity {noiseIntensity:0.##}. Inspecting noise position.");
            ChangeState(ZombieStateType.Inspect);
        }

        public void AnimationEvent_AttackHit()
        {
            if (isDead)
            {
                return;
            }

            AttackAnimationHit?.Invoke(this, target, lastAttackKind);
            InvokePendingAttackEvent();
        }

        public void SetStateEnabled(ZombieStateType stateType, bool enabled)
        {
            if (profile == null)
            {
                return;
            }

            switch (stateType)
            {
                case ZombieStateType.Idle:
                    profile.states.idleEnabled = enabled;
                    break;
                case ZombieStateType.Wander:
                    profile.states.wanderEnabled = enabled;
                    break;
                case ZombieStateType.Alert:
                    profile.states.alertEnabled = enabled;
                    break;
                case ZombieStateType.Chase:
                    profile.states.chaseEnabled = enabled;
                    break;
                case ZombieStateType.Search:
                    profile.states.inspectSearchEnabled = enabled;
                    break;
                case ZombieStateType.Inspect:
                    profile.states.inspectSearchEnabled = enabled;
                    break;
                case ZombieStateType.Stun:
                    profile.states.stunEnabled = enabled;
                    break;
                case ZombieStateType.Hit:
                    profile.states.hitEnabled = enabled;
                    break;
                case ZombieStateType.Attack:
                    profile.states.attackEnabled = enabled;
                    break;
                case ZombieStateType.Eat:
                    profile.states.eatEnabled = enabled;
                    if (!enabled)
                    {
                        ClearCurrentFood();
                    }
                    break;
                case ZombieStateType.Dead:
                    break;
            }

            if (currentState != null && !IsStateEnabled(currentState.StateType))
            {
                ChangeState(currentState.StateType);
            }
        }

        public static void NotifyNoise(Vector3 noisePosition, float noiseIntensity)
        {
            if (noiseIntensity <= 0f)
            {
                return;
            }

            for (int i = ActiveZombies.Count - 1; i >= 0; i--)
            {
                ZombieAI zombie = ActiveZombies[i];
                if (zombie == null || !zombie.isActiveAndEnabled)
                {
                    ActiveZombies.RemoveAt(i);
                    continue;
                }

                if (zombie.IsDead)
                {
                    continue;
                }

                float sqrDistance = (zombie.transform.position - noisePosition).sqrMagnitude;
                if (sqrDistance <= noiseIntensity * noiseIntensity)
                {
                    zombie.HearNoise(noisePosition, noiseIntensity);
                }
            }
        }

        internal void TryAlertNearbyZombies()
        {
            if (suppressNextAlertPropagation)
            {
                suppressNextAlertPropagation = false;
                return;
            }

            if (profile == null || !profile.alert.canAlertNearbyZombies)
            {
                return;
            }

            if (target == null || !target.isActiveAndEnabled)
            {
                return;
            }

            float alertRadius = profile.alert.alertRadius;
            if (alertRadius <= 0f)
            {
                return;
            }

            float sqrAlertRadius = alertRadius * alertRadius;
            Vector3 targetPosition = target.transform.position;

            for (int i = ActiveZombies.Count - 1; i >= 0; i--)
            {
                ZombieAI zombie = ActiveZombies[i];
                if (zombie == null || !zombie.isActiveAndEnabled)
                {
                    ActiveZombies.RemoveAt(i);
                    continue;
                }

                if (zombie == this)
                {
                    continue;
                }

                float sqrDistance = (zombie.transform.position - transform.position).sqrMagnitude;
                if (sqrDistance > sqrAlertRadius)
                {
                    continue;
                }

                zombie.TryRespondToNearbyAlert(target, targetPosition);
            }
        }

        internal bool ConsumeNextAlertPropagationSuppression()
        {
            if (!suppressNextAlertPropagation)
            {
                return false;
            }

            suppressNextAlertPropagation = false;
            return true;
        }

        private bool TryRespondToNearbyAlert(ZombieTarget alertTarget, Vector3 targetPosition)
        {
            if (!CanRespondToNearbyAlert(alertTarget))
            {
                return false;
            }

            if (UnityEngine.Random.value > profile.alert.nearbyAlertResponseChance)
            {
                Log("Ignored nearby zombie alert.");
                return false;
            }

            SetTarget(alertTarget);
            SetLastKnownTargetPosition(targetPosition);
            SetSuspiciousPosition(targetPosition);
            EndSearchCycle();

            if (ShouldUseAlertBeforeChase())
            {
                suppressNextAlertPropagation = true;
                if (ChangeState(ZombieStateType.Alert))
                {
                    Log("Responded to nearby zombie alert with Alert.");
                    return true;
                }

                suppressNextAlertPropagation = false;
            }

            bool changedToChase = ChangeState(ZombieStateType.Chase);
            if (changedToChase)
            {
                Log("Responded to nearby zombie alert with Chase.");
            }

            return changedToChase;
        }

        private bool CanRespondToNearbyAlert(ZombieTarget alertTarget)
        {
            if (alertTarget == null || !alertTarget.isActiveAndEnabled)
            {
                return false;
            }

            if (isDead || crawlingFallInProgress || profile == null || !isActiveAndEnabled)
            {
                return false;
            }

            if (!IsStateEnabled(ZombieStateType.Chase))
            {
                return false;
            }

            if (currentState == null)
            {
                return true;
            }

            switch (currentState.StateType)
            {
                case ZombieStateType.Alert:
                case ZombieStateType.Chase:
                case ZombieStateType.Attack:
                case ZombieStateType.Stun:
                case ZombieStateType.Hit:
                case ZombieStateType.Dead:
                    return false;
                case ZombieStateType.Eat:
                    return profile.hunger.detectTargetWhileEating;
                default:
                    return true;
            }
        }

        internal bool ChangeState(ZombieStateType requestedState)
        {
            if (isDead && requestedState != ZombieStateType.Dead)
            {
                return false;
            }

            if (crawlingFallInProgress && requestedState != ZombieStateType.Dead)
            {
                return false;
            }

            ZombieStateType? resolvedState = ResolveEnabledState(requestedState);
            if (!resolvedState.HasValue)
            {
                StopAgent(true);
                SetAnimatorState(null);
                currentStateName = "None";
                Log($"Could not enter {requestedState}; no enabled fallback state exists.");
                return false;
            }

            if (currentState != null && currentState.StateType == resolvedState.Value)
            {
                return false;
            }

            bool preserveCurrentFood = currentFood != null &&
                (resolvedState.Value == ZombieStateType.Wander || resolvedState.Value == ZombieStateType.Eat);

            currentState?.Exit();
            if (!preserveCurrentFood)
            {
                ClearCurrentFood();
            }

            if (resolvedState.Value != ZombieStateType.Inspect && resolvedState.Value != ZombieStateType.Search)
            {
                EndSearchCycle();
            }

            currentState = states[resolvedState.Value];
            currentStateName = resolvedState.Value.ToString();
            SetAnimatorState(resolvedState.Value);
            Log($"Entered {resolvedState.Value} state.");
            currentState.Enter();
            return true;
        }

        internal bool TryHandleTargetReaction(bool allowInspect)
        {
            if (isDead)
            {
                return false;
            }

            if (target == null)
            {
                return false;
            }

            bool shouldUseAlertBeforeChase = ShouldUseAlertBeforeChase();

            if (CanAttackTarget())
            {
                if (IsStateEnabled(ZombieStateType.Attack))
                {
                    if (HasFullDetection && shouldUseAlertBeforeChase)
                    {
                        return ChangeState(ZombieStateType.Alert);
                    }

                    return ChangeState(ZombieStateType.Attack);
                }

                if (HasFullDetection && IsStateEnabled(ZombieStateType.Chase))
                {
                    return ChangeState(shouldUseAlertBeforeChase ? ZombieStateType.Alert : ZombieStateType.Chase);
                }
            }

            if (HasFullDetection)
            {
                SetLastKnownTargetPosition(target.transform.position);
                return ChangeState(shouldUseAlertBeforeChase ? ZombieStateType.Alert : ZombieStateType.Chase);
            }

            if (allowInspect && HasPartialDetection)
            {
                SetSuspiciousPosition(target.transform.position);
                EndSearchCycle();
                Log($"Partially detected target with {visibleRegionCount} visible region(s).");
                return ChangeState(ZombieStateType.Inspect);
            }

            return false;
        }

        internal bool ShouldUseAlertBeforeChase()
        {
            if (isDead)
            {
                return false;
            }

            if (IsCrawling)
            {
                return false;
            }

            if (!IsStateEnabled(ZombieStateType.Alert))
            {
                return false;
            }

            if (currentState == null)
            {
                return UnityEngine.Random.value <= profile.alert.alertChanceBeforeChase;
            }

            bool canUseAlert = currentState.StateType != ZombieStateType.Attack &&
                currentState.StateType != ZombieStateType.Chase &&
                currentState.StateType != ZombieStateType.Alert;
            return canUseAlert && UnityEngine.Random.value <= profile.alert.alertChanceBeforeChase;
        }

        internal ZombieStateType ChoosePassiveState()
        {
            bool idleEnabled = IsStateEnabled(ZombieStateType.Idle);
            bool wanderEnabled = IsStateEnabled(ZombieStateType.Wander);

            if (idleEnabled && wanderEnabled)
            {
                return UnityEngine.Random.value < 0.5f ? ZombieStateType.Idle : ZombieStateType.Wander;
            }

            if (idleEnabled)
            {
                return ZombieStateType.Idle;
            }

            return ZombieStateType.Wander;
        }

        internal bool IsStateEnabled(ZombieStateType stateType)
        {
            if (profile == null)
            {
                return false;
            }

            if (IsCrawling && (stateType == ZombieStateType.Alert ||
                stateType == ZombieStateType.Hit ||
                stateType == ZombieStateType.Eat))
            {
                return false;
            }

            return profile.IsStateEnabled(stateType);
        }

        private void EnsureHungerInitialized()
        {
            if (hungerInitialized || profile == null)
            {
                return;
            }

            if (IsCrawling)
            {
                currentHunger = profile.hunger.maximumHunger;
                hungerDepletionPauseRemaining = 0f;
                hungerInitialized = true;
                return;
            }

            currentHunger = Mathf.Clamp(profile.hunger.startingHunger, 0f, profile.hunger.maximumHunger);
            hungerDepletionPauseRemaining = 0f;
            hungerInitialized = true;
        }

        private void UpdateHunger(float deltaTime)
        {
            if (IsCrawling || !IsHungerEnabled || isDead)
            {
                return;
            }

            if (isEatingFood)
            {
                return;
            }

            if (hungerDepletionPauseRemaining > 0f)
            {
                hungerDepletionPauseRemaining = Mathf.Max(0f, hungerDepletionPauseRemaining - deltaTime);
                return;
            }

            currentHunger = Mathf.Max(0f, currentHunger - profile.hunger.depletionRate * deltaTime);
        }

        private void TryStartEatingIfHungry()
        {
            if (IsCrawling)
            {
                return;
            }

            if (!CanStartEatingFromCurrentState())
            {
                return;
            }

            if (currentHunger > profile.hunger.hungerThreshold)
            {
                return;
            }

            if (!ZombieFood.TryFindClosest(transform.position, profile.hunger.foodDetectionRadius, profile.hunger.canEatDeadZombies, transform, out ZombieFood food))
            {
                return;
            }

            currentFood = food;
            Log($"Found food {food.name}. Wandering to food.");
            ChangeState(ZombieStateType.Wander);
        }

        private bool CanStartEatingFromCurrentState()
        {
            if (IsCrawling || !IsHungerEnabled)
            {
                currentFood = null;
                return false;
            }

            if (currentFood != null)
            {
                if (currentFood.IsAvailable)
                {
                    return false;
                }

                currentFood = null;
            }

            if (currentState == null)
            {
                return true;
            }

            return currentState.StateType == ZombieStateType.Idle ||
                currentState.StateType == ZombieStateType.Wander;
        }

        internal bool CanAttackTarget()
        {
            if (isDead || crawlingFallInProgress || profile == null || target == null)
            {
                return false;
            }

            float sqrAttackDistance = profile.attack.attackDistance * profile.attack.attackDistance;
            bool closeEnough = (target.transform.position - transform.position).sqrMagnitude <= sqrAttackDistance;
            return closeEnough && HasAnyDetection;
        }

        internal bool ShouldUseChaseAttack()
        {
            return !isDead && !IsCrawling && target != null && target.IsMoving(profile.attack.targetMovingSpeed);
        }

        internal bool ShouldDetectTargetWhileEating()
        {
            return !IsCrawling && profile != null && profile.hunger.detectTargetWhileEating;
        }

        internal bool HasCurrentFood()
        {
            return !IsCrawling && currentFood != null && currentFood.IsAvailable;
        }

        internal Vector3 CurrentFoodPosition => currentFood != null ? currentFood.EatPosition : transform.position;

        internal bool IsCurrentFoodWithinEatingDistance()
        {
            if (!HasCurrentFood() || profile == null)
            {
                return false;
            }

            Vector3 offset = CurrentFoodPosition - transform.position;
            offset.y = 0f;
            float eatingDistance = profile.hunger.eatingDistance;
            return offset.sqrMagnitude <= eatingDistance * eatingDistance;
        }

        internal void ClearCurrentFood()
        {
            currentFood = null;
        }

        internal float EatCurrentFood(float deltaTime)
        {
            if (!HasCurrentFood() || profile == null)
            {
                return 0f;
            }

            float hungerSpace = Mathf.Max(0f, profile.hunger.maximumHunger - currentHunger);
            if (hungerSpace <= 0f)
            {
                return 0f;
            }

            float requestedEnergy = Mathf.Min(profile.hunger.replenishRate * deltaTime, hungerSpace);
            float consumedEnergy = currentFood.Consume(requestedEnergy);
            currentHunger = Mathf.Clamp(currentHunger + consumedEnergy, 0f, profile.hunger.maximumHunger);
            return consumedEnergy;
        }

        internal void BeginHungerDepletionPause()
        {
            if (profile != null)
            {
                hungerDepletionPauseRemaining = profile.hunger.depletionPauseAfterEating;
            }
        }

        internal void TriggerAttack(ZombieAttackKind attackKind)
        {
            if (isDead)
            {
                return;
            }

            if (IsCrawling)
            {
                attackKind = ZombieAttackKind.StandAttack;
            }

            lastAttackKind = attackKind;
            pendingAttackKind = attackKind;
            hasPendingAttackEvent = true;

            if (IsCrawling)
            {
                SetAnimatorTrigger(profile.animator.crawlAttackTrigger);
            }
            else
            {
                SetAnimatorFloatImmediate(profile.animator.attackTypeFloat, GetAttackTypeValue(attackKind));
                SetAnimatorTrigger(profile.animator.attackTrigger);
            }

            if (attackKind == ZombieAttackKind.StandAttack)
            {
                Log("Attacked target with StandAttack.");
            }
            else
            {
                Log("Attacked target while chasing with ChaseAttack.");
            }

            if (profile.attack.invokeAttackEventsOnAttackStart)
            {
                InvokePendingAttackEvent();
            }
        }

        internal void TriggerHitReaction(ZombieHitReactionKind hitReactionKind)
        {
            if (isDead)
            {
                return;
            }

            SetAnimatorFloatImmediate(profile.animator.hitReactionTypeFloat, GetHitReactionTypeValue(hitReactionKind));
            SetAnimatorTrigger(profile.animator.hitReactionTrigger);

            if (hitReactionKind == ZombieHitReactionKind.ChaseHit)
            {
                Log("Played Chase Hit reaction without stopping chase.");
            }
            else
            {
                Log("Played Stand Hit reaction.");
            }
        }

        internal void HandleDamage(ZombieDamageInfo damageInfo)
        {
            if (isDead || profile == null || damageInfo.appliedDamage <= 0f)
            {
                return;
            }

            lastDamageInfo = damageInfo;
            hasLastDamageInfo = true;
            hasDamageAggro = false;
            hasHitSourcePosition = TryResolveHitReactionPosition(damageInfo, out hitSourcePosition);

            if (!profile.hit.aggroOnDamage)
            {
                if (ShouldQueueLocalDamageSearch())
                {
                    SetLocalDamageSearchPosition(transform.position);
                }
            }
            else
            {
                ZombieTarget attackerTarget = ResolveAttackerTarget(damageInfo.attacker);
                if (attackerTarget != null)
                {
                    target = attackerTarget;
                    SetLastKnownTargetPosition(attackerTarget.transform.position);
                    SetSuspiciousPosition(attackerTarget.transform.position);
                    hasDamageAggro = true;
                }
                else if (TryResolveAggroPosition(damageInfo, out Vector3 aggroPosition))
                {
                    SetLastKnownTargetPosition(aggroPosition);
                    SetSuspiciousPosition(aggroPosition);
                    hasDamageAggro = true;
                }
            }

            if (health != null)
            {
                Log($"Took {damageInfo.appliedDamage:0.##} damage. Health is now {damageInfo.currentHealth:0.##}.");
            }
            else
            {
                Log($"Received hit for {damageInfo.appliedDamage:0.##} damage. Add ZombieHealth to track health.");
            }

            if (crawlingFallInProgress)
            {
                return;
            }

            if (!ShouldEnterHitState(damageInfo))
            {
                TryReactAfterDamageWithoutHit();
                return;
            }

            if (ShouldUseChaseHitReaction())
            {
                TriggerHitReaction(ZombieHitReactionKind.ChaseHit);
                return;
            }

            ChangeState(ZombieStateType.Hit);
        }

        internal void HandleDeath(ZombieDamageInfo damageInfo)
        {
            if (isDead)
            {
                return;
            }

            if (ShouldEnterCrawlingAfterDeath())
            {
                BeginCrawlingAfterDeath(damageInfo);
                return;
            }

            deathDamageInfo = damageInfo;
            hasDeathDamageInfo = true;
            isDead = true;
            hasDamageAggro = false;
            hasLocalDamageSearch = false;
            hasPendingAttackEvent = false;
            ResetDetection();
            EndSearchCycle();

            if (profile == null)
            {
                currentState?.Exit();
                currentState = null;
                currentStateName = ZombieStateType.Dead.ToString();
                StopAndDisableAgent();
                DisableRootColliders();
                Log("Died.");
                return;
            }

            ChangeState(ZombieStateType.Dead);
        }

        internal void EnterDeadState()
        {
            isDead = true;
            ResetDetection();
            EndSearchCycle();
            hasDamageAggro = false;
            hasLocalDamageSearch = false;
            hasPendingAttackEvent = false;
            SetAnimatorFloatImmediate(profile.animator.locomotionBlendYFloat, 0f);
            SetAnimatorFloatImmediate(profile.animator.locomotionBlendXFloat, IdleBlendX);
            SetAnimatorBool(profile.animator.isInCrawlModeBool, IsCrawling);
            SetAnimatorBool(profile.animator.crawlStunnedBool, false);
            SetAnimatorBool(profile.animator.crawlDeadBool, IsCrawling);
            SetCrawlingAnimatorLayerWeight(ShouldKeepCrawlingAnimatorLayerActive() ? 1f : 0f);
            StopAndDisableAgent();
            DisableRootColliders();

            if (deathApplied)
            {
                return;
            }

            deathApplied = true;

            if (profile.death.deathMode == ZombieDeathMode.Ragdoll)
            {
                ApplyRagdollDeath();
            }
            else
            {
                Log("Died with animation death.");
            }
        }

        internal void MaintainDeadState()
        {
            ResetDetection();
            StopAgent(true);
        }

        private ZombieDamageInfo CreateCurrentHealthDeathInfo()
        {
            float currentHealth = health != null ? health.CurrentHealth : 0f;
            return new ZombieDamageInfo
            {
                requestedDamage = 0f,
                appliedDamage = 0f,
                previousHealth = currentHealth,
                currentHealth = currentHealth,
                attacker = null,
                hitPoint = transform.position,
                hasHitPoint = false,
                hitDirection = Vector3.zero,
                hasHitDirection = false,
                time = Time.time
            };
        }

        private void ApplyRagdollDeath()
        {
            if (ragdollController == null)
            {
                ragdollController = GetComponent<ZombieRagdollController>();
            }

            if (ragdollController == null)
            {
                Log("Ragdoll death was requested, but no ZombieRagdollController was found. Using animation death.");
                return;
            }

            Vector3 force = Vector3.zero;
            Vector3 forcePoint = transform.position;
            bool hasForcePoint = false;
            if (TryGetDeathRagdollForce(out Vector3 resolvedForce, out Vector3 resolvedForcePoint, out bool resolvedHasForcePoint))
            {
                force = resolvedForce;
                forcePoint = resolvedForcePoint;
                hasForcePoint = resolvedHasForcePoint;
            }

            ragdollController.EnableRagdoll(force, forcePoint, hasForcePoint, profile.death.ragdollForceMode);

            if (animator != null)
            {
                animator.enabled = false;
            }

            Log("Died with ragdoll death.");
        }

        private void InitializeModeFromProfile()
        {
            if (profile == null)
            {
                ApplyCrawlingAnimatorMode();
                return;
            }

            if (!modeInitialized)
            {
                currentMode = profile.crawling.startingMode;
                crawlingFallInProgress = false;
                crawlingFallAnimationStarted = false;
                crawlingFallElapsedTime = 0f;
                modeInitialized = true;
            }

            ApplyCrawlingAnimatorMode();

            if (IsCrawling)
            {
                CancelHungerAndEating();
            }
        }

        private void ApplyStartingModeHealth()
        {
            if (profile == null || health == null)
            {
                return;
            }

            if (profile.crawling.startingMode == ZombieMode.Crawling && IsCrawling)
            {
                health.SetCurrentHealth(profile.health.crawlingHealth);
            }
        }

        private bool ShouldEnterCrawlingAfterDeath()
        {
            return profile != null &&
                profile.death.CrawlAfterDeath &&
                !IsCrawling &&
                !crawlingFallInProgress;
        }

        private void BeginCrawlingAfterDeath(ZombieDamageInfo damageInfo)
        {
            currentMode = ZombieMode.Crawling;
            modeInitialized = true;
            crawlingFallInProgress = true;
            crawlingFallAnimationStarted = false;
            crawlingFallElapsedTime = 0f;
            deathApplied = false;
            hasDeathDamageInfo = false;
            hasPendingAttackEvent = false;
            hasLocalDamageSearch = false;

            if (TryResolveAggroPosition(damageInfo, out Vector3 aggroPosition))
            {
                SetLastKnownTargetPosition(aggroPosition);
                SetSuspiciousPosition(aggroPosition);
            }

            ZombieTarget attackerTarget = ResolveAttackerTarget(damageInfo.attacker);
            if (attackerTarget != null)
            {
                target = attackerTarget;
                SetLastKnownTargetPosition(attackerTarget.transform.position);
                SetSuspiciousPosition(attackerTarget.transform.position);
            }

            currentState?.Exit();
            currentState = null;
            currentStateName = "CrawlingTransition";

            CancelHungerAndEating();
            EndSearchCycle();
            ResetDetection();
            StopAgent(true);
            SetAnimatorFloatImmediate(profile.animator.locomotionBlendYFloat, 0f);
            SetAnimatorFloatImmediate(profile.animator.locomotionBlendXFloat, IdleBlendX);
            SetAnimatorState(null);
            SetCrawlingAnimatorLayerWeight(1f);
            SetAnimatorTrigger(profile.animator.crawlFallTrigger);

            if (health != null)
            {
                health.SetCurrentHealth(profile.health.crawlingHealth);
            }

            Log("Standing health depleted. Entering crawling mode.");
        }

        private void TickCrawlingFallTransition(float deltaTime)
        {
            crawlingFallElapsedTime += deltaTime;
            StopAgent(true);
            ResetDetection();
            SetAnimatorFloatImmediate(profile.animator.locomotionBlendYFloat, 0f);
            SetAnimatorFloatImmediate(profile.animator.locomotionBlendXFloat, IdleBlendX);
            ApplyCrawlingAnimatorMode();

            if (HasCrawlingFallFinished())
            {
                FinishCrawlingFallTransition();
            }
        }

        private bool HasCrawlingFallFinished()
        {
            if (animator == null || animator.runtimeAnimatorController == null || animator.layerCount <= CrawlingAnimatorLayerIndex)
            {
                return true;
            }

            bool isInTransition = animator.IsInTransition(CrawlingAnimatorLayerIndex);
            AnimatorStateInfo stateInfo = animator.GetCurrentAnimatorStateInfo(CrawlingAnimatorLayerIndex);

            if (stateInfo.IsTag(FallingAnimatorTag))
            {
                crawlingFallAnimationStarted = true;
                return !isInTransition && stateInfo.normalizedTime >= 1f;
            }

            if (crawlingFallAnimationStarted && !isInTransition)
            {
                return true;
            }

            if (crawlingFallElapsedTime >= CrawlingFallAnimatorFallbackTime)
            {
                Log($"Crawling Falling Animator tag '{FallingAnimatorTag}' was not detected. Continuing after fallback time.");
                return true;
            }

            return false;
        }

        private void FinishCrawlingFallTransition()
        {
            crawlingFallInProgress = false;
            crawlingFallAnimationStarted = false;
            crawlingFallElapsedTime = 0f;
            RefreshVisionSnapshot();

            ZombieStateType resumeState = ResolveCrawlingResumeState();
            Log($"Crawling fall finished. Resuming {resumeState}.");
            ChangeState(resumeState);
        }

        private ZombieStateType ResolveCrawlingResumeState()
        {
            if (CanAttackTarget() && IsStateEnabled(ZombieStateType.Attack))
            {
                return ZombieStateType.Attack;
            }

            if (target != null && (HasFullDetection || HasChaseVisibility) && IsStateEnabled(ZombieStateType.Chase))
            {
                return ZombieStateType.Chase;
            }

            if (target != null && HasPartialDetection && IsStateEnabled(ZombieStateType.Inspect))
            {
                return ZombieStateType.Inspect;
            }

            if (hasLastKnownTargetPosition && IsStateEnabled(ZombieStateType.Search))
            {
                return ZombieStateType.Search;
            }

            if (hasSuspiciousPosition && IsStateEnabled(ZombieStateType.Inspect))
            {
                return ZombieStateType.Inspect;
            }

            return ChoosePassiveState();
        }

        private void ApplyCrawlingAnimatorMode()
        {
            if (profile == null)
            {
                SetCrawlingAnimatorLayerWeight(0f);
                return;
            }

            SetCrawlingAnimatorLayerWeight(ShouldKeepCrawlingAnimatorLayerActive() ? 1f : 0f);
            SetAnimatorBool(profile.animator.isInCrawlModeBool, IsCrawling);
            SetAnimatorBool(profile.animator.crawlStunnedBool, IsCrawling && currentState != null && currentState.StateType == ZombieStateType.Stun);
            SetAnimatorBool(profile.animator.crawlDeadBool, IsCrawling && isDead);
        }

        private bool ShouldKeepCrawlingAnimatorLayerActive()
        {
            if (!IsCrawling)
            {
                return false;
            }

            if (!isDead)
            {
                return true;
            }

            return profile != null && profile.death.deathMode == ZombieDeathMode.Animation;
        }

        private void SetCrawlingAnimatorLayerWeight(float weight)
        {
            if (animator == null || animator.runtimeAnimatorController == null)
            {
                return;
            }

            if (animator.layerCount > CrawlingAnimatorLayerIndex)
            {
                animator.SetLayerWeight(CrawlingAnimatorLayerIndex, Mathf.Clamp01(weight));
            }

            if (IsCrawling && animator.layerCount > UpperBodyAnimatorLayerIndex)
            {
                animator.SetLayerWeight(UpperBodyAnimatorLayerIndex, 0f);
            }
        }

        private void CancelHungerAndEating()
        {
            currentFood = null;
            isEatingFood = false;
            hungerDepletionPauseRemaining = 0f;

            if (profile != null)
            {
                currentHunger = profile.hunger.maximumHunger;
                SetAnimatorBool(profile.animator.eatingBool, false);
            }
        }

        private bool TryGetDeathRagdollForce(out Vector3 force, out Vector3 forcePoint, out bool hasForcePoint)
        {
            force = Vector3.zero;
            forcePoint = transform.position;
            hasForcePoint = false;

            if (profile == null || !profile.death.applyRagdollForce || profile.death.ragdollForce <= 0f || !hasDeathDamageInfo)
            {
                return false;
            }

            Vector3 direction = Vector3.zero;
            if (deathDamageInfo.hasHitDirection && deathDamageInfo.hitDirection.sqrMagnitude > 0.0001f)
            {
                direction = deathDamageInfo.hitDirection.normalized;
            }
            else if (deathDamageInfo.attacker != null)
            {
                direction = transform.position - deathDamageInfo.attacker.transform.position;
            }
            else if (deathDamageInfo.hasHitPoint)
            {
                direction = transform.position - deathDamageInfo.hitPoint;
            }

            if (direction.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            force = direction.normalized * profile.death.ragdollForce;
            if (deathDamageInfo.hasHitPoint)
            {
                forcePoint = deathDamageInfo.hitPoint;
                hasForcePoint = true;
            }

            return true;
        }

        internal bool ShouldChaseKnownTargetAfterHit()
        {
            return profile != null &&
                target != null &&
                (HasDamageAggro || HasFullDetection || HasChaseVisibility);
        }

        internal float ConsumeStunDuration()
        {
            float duration = requestedStunDuration > 0f
                ? requestedStunDuration
                : UnityEngine.Random.Range(profile.stun.minimumStunTime, profile.stun.maximumStunTime);
            requestedStunDuration = -1f;
            return Mathf.Max(0.01f, duration);
        }

        internal void ResumeAfterStun()
        {
            RefreshVisionSnapshot();

            if (CanAttackTarget() && IsStateEnabled(ZombieStateType.Attack))
            {
                Log("Stun ended. Target is in attack range.");
                ChangeState(ZombieStateType.Attack);
                return;
            }

            if (target != null && (HasFullDetection || HasChaseVisibility))
            {
                SetLastKnownTargetPosition(target.transform.position);
                SetSuspiciousPosition(target.transform.position);
                Log("Stun ended. Target reacquired.");

                bool useAlert = ShouldUseAlertBeforeChase();
                ChangeState(useAlert ? ZombieStateType.Alert : ZombieStateType.Chase);
                return;
            }

            if (target != null && visibleRegionCount >= profile.detection.minimumRegionsForInspect)
            {
                SetSuspiciousPosition(target.transform.position);
                Log("Stun ended. Partial target visibility found.");
                ChangeState(ZombieStateType.Inspect);
                return;
            }

            if (TryStartLocalDamageSearch())
            {
                return;
            }

            if (hasLastKnownTargetPosition)
            {
                Log("Stun ended. Searching last known target position.");
                ChangeState(ZombieStateType.Search);
                return;
            }

            if (hasSuspiciousPosition)
            {
                Log("Stun ended. Inspecting remembered suspicious position.");
                ChangeState(ZombieStateType.Inspect);
                return;
            }

            if (IsStateEnabled(ZombieStateType.Search))
            {
                Vector3 searchPosition = transform.position;
                SetSuspiciousPosition(searchPosition);
                BeginSearchCycle(searchPosition);
                Log("Stun ended. No target visible, searching around current position.");
                ChangeState(ZombieStateType.Search);
                return;
            }

            Log("Stun ended. Inspect/Search disabled, returning to passive behavior.");
            ChangeState(ChoosePassiveState());
        }

        internal void RefreshVisionSnapshot()
        {
            ResetDetection();
            EvaluateVision(0f);
        }

        private bool ShouldUseChaseHitReaction()
        {
            return currentState != null && currentState.StateType == ZombieStateType.Chase && target != null;
        }

        private bool ShouldEnterHitState(ZombieDamageInfo damageInfo)
        {
            if (!IsStateEnabled(ZombieStateType.Hit))
            {
                return false;
            }

            if (damageInfo.appliedDamage < profile.hit.minimumDamageToReact)
            {
                Log($"Damage was below Hit reaction threshold ({profile.hit.minimumDamageToReact:0.##}).");
                return false;
            }

            return CanHitInterruptCurrentState();
        }

        private void TryReactAfterDamageWithoutHit()
        {
            if (currentState != null && currentState.StateType == ZombieStateType.Eat && !profile.hunger.detectTargetWhileEating)
            {
                return;
            }

            if (!profile.hit.aggroOnDamage)
            {
                TryStartLocalDamageSearch();
                return;
            }

            if (currentState == null)
            {
                return;
            }

            if (currentState.StateType == ZombieStateType.Attack ||
                currentState.StateType == ZombieStateType.Chase ||
                currentState.StateType == ZombieStateType.Alert ||
                currentState.StateType == ZombieStateType.Stun ||
                currentState.StateType == ZombieStateType.Hit)
            {
                return;
            }

            if (target != null)
            {
                ChangeState(ShouldUseAlertBeforeChase() ? ZombieStateType.Alert : ZombieStateType.Chase);
                return;
            }

            if (hasSuspiciousPosition || hasLastKnownTargetPosition)
            {
                ChangeState(ZombieStateType.Inspect);
            }
        }

        private bool ShouldQueueLocalDamageSearch()
        {
            if (currentState == null)
            {
                return true;
            }

            return currentState.StateType != ZombieStateType.Attack &&
                currentState.StateType != ZombieStateType.Chase &&
                currentState.StateType != ZombieStateType.Alert;
        }

        internal bool TryStartLocalDamageSearch()
        {
            if (!hasLocalDamageSearch)
            {
                return false;
            }

            if (currentState != null && currentState.StateType == ZombieStateType.Stun)
            {
                return false;
            }

            Vector3 searchPosition = localDamageSearchPosition;
            hasLocalDamageSearch = false;
            EndSearchCycle();
            SetSuspiciousPosition(searchPosition);
            BeginSearchCycle(searchPosition);
            Log("Aggro On Damage is disabled. Searching around current hit position.");
            return ChangeState(ZombieStateType.Search);
        }

        private bool CanHitInterruptCurrentState()
        {
            if (currentState == null || currentState.StateType == ZombieStateType.Hit)
            {
                return true;
            }

            switch (currentState.StateType)
            {
                case ZombieStateType.Idle:
                    return profile.hit.canInterruptIdle;
                case ZombieStateType.Wander:
                    return profile.hit.canInterruptWander;
                case ZombieStateType.Alert:
                    return profile.hit.canInterruptAlert;
                case ZombieStateType.Chase:
                    return profile.hit.canInterruptChase;
                case ZombieStateType.Search:
                case ZombieStateType.Inspect:
                    return profile.hit.canInterruptInspectSearch;
                case ZombieStateType.Stun:
                    return false;
                case ZombieStateType.Attack:
                    return profile.hit.canInterruptAttack;
                case ZombieStateType.Eat:
                    return true;
                default:
                    return true;
            }
        }

        private bool TryResolveHitReactionPosition(ZombieDamageInfo damageInfo, out Vector3 sourcePosition)
        {
            if (damageInfo.attacker != null)
            {
                sourcePosition = damageInfo.attacker.transform.position;
                return true;
            }

            if (damageInfo.hasHitDirection && damageInfo.hitDirection.sqrMagnitude > 0.0001f)
            {
                Vector3 hitReference = damageInfo.hasHitPoint ? damageInfo.hitPoint : transform.position;
                sourcePosition = hitReference - damageInfo.hitDirection.normalized;
                return true;
            }

            if (damageInfo.hasHitPoint)
            {
                sourcePosition = damageInfo.hitPoint;
                return true;
            }

            sourcePosition = transform.position;
            return false;
        }

        private static bool TryResolveAggroPosition(ZombieDamageInfo damageInfo, out Vector3 aggroPosition)
        {
            if (damageInfo.attacker != null)
            {
                aggroPosition = damageInfo.attacker.transform.position;
                return true;
            }

            if (damageInfo.hasHitPoint)
            {
                aggroPosition = damageInfo.hitPoint;
                return true;
            }

            aggroPosition = Vector3.zero;
            return false;
        }

        private static ZombieTarget ResolveAttackerTarget(GameObject attacker)
        {
            if (attacker == null)
            {
                return null;
            }

            ZombieTarget attackerTarget = attacker.GetComponent<ZombieTarget>();
            if (attackerTarget != null)
            {
                return attackerTarget;
            }

            attackerTarget = attacker.GetComponentInParent<ZombieTarget>();
            if (attackerTarget != null)
            {
                return attackerTarget;
            }

            return attacker.GetComponentInChildren<ZombieTarget>();
        }

        internal void FacePosition(Vector3 position, float deltaTime)
        {
            float turnSpeed = profile != null ? profile.attack.faceTargetTurnSpeed : 12f;
            FacePosition(position, deltaTime, turnSpeed);
        }

        internal void FacePosition(Vector3 position, float deltaTime, float turnSpeed)
        {
            Vector3 direction = position - transform.position;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, turnSpeed * deltaTime);
        }

        private void ApplyHeadLookIK()
        {
            if (animator == null ||
                animator.runtimeAnimatorController == null ||
                animator.avatar == null ||
                !animator.avatar.isHuman)
            {
                currentHeadLookWeight = 0f;
                return;
            }

            bool hasTarget = TryGetHeadLookTarget(out Vector3 lookPosition, out float horizontalLimit, out float verticalLimit);
            float targetWeight = hasTarget ? 1f : 0f;
            if (lastHeadLookIKFrame != Time.frameCount)
            {
                currentHeadLookWeight = Mathf.MoveTowards(
                    currentHeadLookWeight,
                    targetWeight,
                    HeadLookFadeSpeed * Time.deltaTime);
                lastHeadLookIKFrame = Time.frameCount;
            }

            animator.SetLookAtWeight(
                currentHeadLookWeight,
                HeadLookBodyWeight,
                HeadLookWeight,
                HeadLookEyesWeight,
                HeadLookClampWeight);

            if (currentHeadLookWeight <= 0f)
            {
                return;
            }

            animator.SetLookAtPosition(hasTarget
                ? GetLimitedHeadLookPosition(lookPosition, horizontalLimit, verticalLimit)
                : GetEyePosition() + transform.forward * 2f);
        }

        private bool TryGetHeadLookTarget(out Vector3 lookPosition, out float horizontalLimit, out float verticalLimit)
        {
            lookPosition = Vector3.zero;
            horizontalLimit = 0f;
            verticalLimit = 0f;

            if (profile == null ||
                target == null ||
                !target.isActiveAndEnabled ||
                isDead ||
                crawlingFallInProgress)
            {
                return false;
            }

            ZombieStateType? stateType = CurrentStateType;
            if (stateType == ZombieStateType.Alert)
            {
                if (!profile.alert.lookAtTarget)
                {
                    return false;
                }

                horizontalLimit = profile.alert.headLookHorizontalLimit;
                verticalLimit = profile.alert.headLookVerticalLimit;
            }
            else if (stateType == ZombieStateType.Chase)
            {
                if (!profile.chase.lookAtTarget)
                {
                    return false;
                }

                horizontalLimit = profile.chase.headLookHorizontalLimit;
                verticalLimit = profile.chase.headLookVerticalLimit;
            }
            else if (stateType == ZombieStateType.Attack)
            {
                if (!profile.attack.lookAtTarget)
                {
                    return false;
                }

                horizontalLimit = profile.attack.headLookHorizontalLimit;
                verticalLimit = profile.attack.headLookVerticalLimit;
            }
            else
            {
                return false;
            }

            lookPosition = target.GetRegionPosition(ZombieVisionRegion.Top);
            return true;
        }

        private Vector3 GetLimitedHeadLookPosition(Vector3 targetPosition, float horizontalLimit, float verticalLimit)
        {
            Transform headTransform = animator.GetBoneTransform(HumanBodyBones.Head);
            Vector3 origin = headTransform != null ? headTransform.position : GetEyePosition();
            Vector3 direction = targetPosition - origin;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return targetPosition;
            }

            Vector3 localDirection = transform.InverseTransformDirection(direction);
            float distance = localDirection.magnitude;
            float yaw = Mathf.Atan2(localDirection.x, localDirection.z) * Mathf.Rad2Deg;
            float planarDistance = new Vector2(localDirection.x, localDirection.z).magnitude;
            float pitch = Mathf.Atan2(localDirection.y, Mathf.Max(0.0001f, planarDistance)) * Mathf.Rad2Deg;

            yaw = Mathf.Clamp(yaw, -horizontalLimit, horizontalLimit);
            pitch = Mathf.Clamp(pitch, -verticalLimit, verticalLimit);

            float yawRadians = yaw * Mathf.Deg2Rad;
            float pitchRadians = pitch * Mathf.Deg2Rad;
            float pitchCos = Mathf.Cos(pitchRadians);
            Vector3 limitedLocalDirection = new Vector3(
                Mathf.Sin(yawRadians) * pitchCos,
                Mathf.Sin(pitchRadians),
                Mathf.Cos(yawRadians) * pitchCos) * distance;

            return origin + transform.TransformDirection(limitedLocalDirection);
        }

        internal bool TrySetDestination(Vector3 destination)
        {
            if (!CanUseAgent())
            {
                return false;
            }

            agent.isStopped = false;
            return agent.SetDestination(destination);
        }

        internal void StopAgent(bool clearPath)
        {
            if (!CanUseAgent())
            {
                return;
            }

            agent.isStopped = true;
            if (clearPath)
            {
                agent.ResetPath();
            }
        }

        private void StopAndDisableAgent()
        {
            if (agent == null)
            {
                return;
            }

            if (agent.enabled && agent.isOnNavMesh)
            {
                agent.isStopped = true;
                agent.ResetPath();
            }

            agent.enabled = false;
        }

        private void CacheRootColliders()
        {
            rootColliders = GetComponents<Collider>();
        }

        private void DisableRootColliders()
        {
            if (rootColliders == null || rootColliders.Length == 0)
            {
                CacheRootColliders();
            }

            if (rootColliders == null)
            {
                return;
            }

            for (int i = 0; i < rootColliders.Length; i++)
            {
                Collider rootCollider = rootColliders[i];
                if (rootCollider != null)
                {
                    rootCollider.enabled = false;
                }
            }
        }

        internal void ConfigureAgent(float speed, float stoppingDistance)
        {
            if (agent == null)
            {
                return;
            }

            agent.speed = IsCrawling && profile != null ? profile.crawling.speed : speed;
            agent.stoppingDistance = stoppingDistance;
        }

        internal void QueueNextIdleDuration(float duration)
        {
            queuedIdleDuration = Mathf.Max(0f, duration);
            hasQueuedIdleDuration = true;
        }

        internal bool TryConsumeQueuedIdleDuration(out float duration)
        {
            duration = queuedIdleDuration;
            if (!hasQueuedIdleDuration)
            {
                return false;
            }

            hasQueuedIdleDuration = false;
            queuedIdleDuration = 0f;
            return true;
        }

        internal void ClearQueuedIdleDuration()
        {
            hasQueuedIdleDuration = false;
            queuedIdleDuration = 0f;
        }

        internal bool HasReachedDestination(float arrivalDistance)
        {
            if (!CanUseAgent())
            {
                return true;
            }

            if (agent.pathPending)
            {
                return false;
            }

            if (!agent.hasPath)
            {
                return true;
            }

            return agent.remainingDistance <= arrivalDistance;
        }

        internal bool TryGetRandomWanderDestination(out Vector3 destination)
        {
            destination = transform.position;
            if (profile == null)
            {
                return false;
            }

            for (int i = 0; i < 12; i++)
            {
                Vector2 randomCircle = UnityEngine.Random.insideUnitCircle * profile.wander.randomRadius;
                Vector3 candidate = transform.position + new Vector3(randomCircle.x, 0f, randomCircle.y);
                if (TrySampleNavMeshPosition(candidate, profile.wander.destinationSampleRadius, out destination))
                {
                    return true;
                }
            }

            return false;
        }

        internal bool TryGetNextWaypointDestination(out Vector3 destination)
        {
            return TryGetNextWaypointDestination(out destination, out _);
        }

        internal bool TryGetNextWaypointDestination(out Vector3 destination, out ZombieWaypoint waypointData)
        {
            destination = transform.position;
            waypointData = null;
            if (waypointPath == null || waypointPath.WaypointCount == 0)
            {
                return false;
            }

            int waypointCount = waypointPath.WaypointCount;
            for (int i = 0; i < waypointCount; i++)
            {
                waypointIndex = (waypointIndex + 1) % waypointCount;
                if (!waypointPath.TryGetWaypoint(waypointIndex, out ZombieWaypoint waypoint))
                {
                    continue;
                }

                if (TrySampleNavMeshPosition(waypoint.transform.position, profile.wander.destinationSampleRadius, out destination))
                {
                    waypointData = waypoint;
                    return true;
                }
            }

            return false;
        }

        internal bool TryGetSearchDestination(Vector3 origin, out Vector3 destination)
        {
            destination = transform.position;
            if (profile == null)
            {
                return false;
            }

            float minimumRadius = Mathf.Clamp(profile.inspect.minimumSearchRadius, 0f, profile.inspect.maximumSearchRadius);
            float minimumSqrDistanceFromZombie = minimumRadius * minimumRadius;

            for (int i = 0; i < 12; i++)
            {
                float angle = UnityEngine.Random.Range(0f, Mathf.PI * 2f);
                float radius = UnityEngine.Random.Range(minimumRadius, profile.inspect.maximumSearchRadius);
                Vector3 candidate = origin + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                if (TrySampleNavMeshPosition(candidate, profile.inspect.destinationSampleRadius, out destination) &&
                    IsSearchDestinationFarEnough(destination, minimumSqrDistanceFromZombie))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsSearchDestinationFarEnough(Vector3 destination, float minimumSqrDistance)
        {
            if (minimumSqrDistance <= 0f)
            {
                return true;
            }

            Vector3 offset = destination - transform.position;
            offset.y = 0f;
            return offset.sqrMagnitude >= minimumSqrDistance;
        }

        internal Vector3 GetSearchStartPosition()
        {
            if (hasLastKnownTargetPosition)
            {
                return lastKnownTargetPosition;
            }

            if (hasSuspiciousPosition)
            {
                return suspiciousPosition;
            }

            return transform.position;
        }

        internal void BeginSearchCycle(Vector3 origin)
        {
            searchOrigin = origin;
            searchLocationTargetCount = UnityEngine.Random.Range(profile.inspect.minimumSearchLocations, profile.inspect.maximumSearchLocations + 1);
            searchedLocationCount = 0;
            searchCycleActive = true;
            Log($"Started search cycle around {searchOrigin} for {searchLocationTargetCount} location(s).");
        }

        internal void CompleteCurrentSearchLocation()
        {
            if (searchCycleActive)
            {
                searchedLocationCount = Mathf.Min(searchedLocationCount + 1, searchLocationTargetCount);
                Log($"Completed search location {searchedLocationCount}/{searchLocationTargetCount}.");
            }
        }

        internal void EndSearchCycle()
        {
            searchCycleActive = false;
            searchedLocationCount = 0;
            searchLocationTargetCount = 0;
        }

        internal bool TrySelectNextSearchStep()
        {
            if (!searchCycleActive)
            {
                BeginSearchCycle(GetSearchStartPosition());
            }

            if (TryGetSearchDestination(searchOrigin, out Vector3 destination))
            {
                SetSuspiciousPosition(destination);
                return true;
            }

            return false;
        }

        internal bool TrySampleNavMeshPosition(Vector3 candidate, float sampleRadius, out Vector3 position)
        {
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, sampleRadius, NavMesh.AllAreas))
            {
                position = hit.position;
                return true;
            }

            position = candidate;
            return false;
        }

        internal void SetLastKnownTargetPosition(Vector3 position)
        {
            lastKnownTargetPosition = position;
            hasLastKnownTargetPosition = true;
        }

        internal void SetSuspiciousPosition(Vector3 position)
        {
            suspiciousPosition = position;
            hasSuspiciousPosition = true;
        }

        private void SetLocalDamageSearchPosition(Vector3 position)
        {
            localDamageSearchPosition = position;
            hasLocalDamageSearch = true;
        }

        internal void Log(string message)
        {
            if (debugMode)
            {
                Debug.Log($"[Advanced Zombie AI Pro] {name}: {message}", this);
            }
        }

        private void BuildStates()
        {
            states.Clear();
            states.Add(ZombieStateType.Idle, new ZombieIdleState(this));
            states.Add(ZombieStateType.Wander, new ZombieWanderState(this));
            states.Add(ZombieStateType.Alert, new ZombieAlertState(this));
            states.Add(ZombieStateType.Chase, new ZombieChaseState(this));
            states.Add(ZombieStateType.Search, new ZombieSearchState(this));
            states.Add(ZombieStateType.Inspect, new ZombieInspectState(this));
            states.Add(ZombieStateType.Stun, new ZombieStunState(this));
            states.Add(ZombieStateType.Hit, new ZombieHitState(this));
            states.Add(ZombieStateType.Attack, new ZombieAttackState(this));
            states.Add(ZombieStateType.Eat, new ZombieEatState(this));
            states.Add(ZombieStateType.Dead, new ZombieDeadState(this));
        }

        private ZombieStateType? ResolveEnabledState(ZombieStateType requestedState)
        {
            if (IsStateEnabled(requestedState))
            {
                return requestedState;
            }

            switch (requestedState)
            {
                case ZombieStateType.Stun:
                    if (target != null && IsStateEnabled(ZombieStateType.Chase))
                    {
                        return ZombieStateType.Chase;
                    }
                    if ((hasSuspiciousPosition || hasLastKnownTargetPosition) && IsStateEnabled(ZombieStateType.Search))
                    {
                        return ZombieStateType.Search;
                    }
                    if ((hasSuspiciousPosition || hasLastKnownTargetPosition) && IsStateEnabled(ZombieStateType.Inspect))
                    {
                        return ZombieStateType.Inspect;
                    }
                    return ResolvePassiveState();

                case ZombieStateType.Hit:
                    if (target != null && IsStateEnabled(ZombieStateType.Chase))
                    {
                        return ZombieStateType.Chase;
                    }
                    if ((hasSuspiciousPosition || hasLastKnownTargetPosition) && IsStateEnabled(ZombieStateType.Inspect))
                    {
                        return ZombieStateType.Inspect;
                    }
                    if ((hasSuspiciousPosition || hasLastKnownTargetPosition) && IsStateEnabled(ZombieStateType.Search))
                    {
                        return ZombieStateType.Search;
                    }
                    return ResolvePassiveState();

                case ZombieStateType.Attack:
                    if (IsStateEnabled(ZombieStateType.Chase))
                    {
                        return ZombieStateType.Chase;
                    }
                    if (IsStateEnabled(ZombieStateType.Search))
                    {
                        return ZombieStateType.Search;
                    }
                    return ResolvePassiveState();

                case ZombieStateType.Eat:
                    return ResolvePassiveState();

                case ZombieStateType.Alert:
                    if (IsStateEnabled(ZombieStateType.Chase))
                    {
                        return ZombieStateType.Chase;
                    }
                    if (IsStateEnabled(ZombieStateType.Inspect))
                    {
                        return ZombieStateType.Inspect;
                    }
                    if (IsStateEnabled(ZombieStateType.Search))
                    {
                        return ZombieStateType.Search;
                    }
                    return ResolvePassiveState();

                case ZombieStateType.Inspect:
                    if (IsStateEnabled(ZombieStateType.Search))
                    {
                        return ZombieStateType.Search;
                    }
                    return ResolvePassiveState();

                case ZombieStateType.Search:
                    return ResolvePassiveState();

                case ZombieStateType.Chase:
                    if (IsStateEnabled(ZombieStateType.Inspect))
                    {
                        return ZombieStateType.Inspect;
                    }
                    if (IsStateEnabled(ZombieStateType.Search))
                    {
                        return ZombieStateType.Search;
                    }
                    return ResolvePassiveState();

                case ZombieStateType.Idle:
                    return IsStateEnabled(ZombieStateType.Wander) ? ZombieStateType.Wander : (ZombieStateType?)null;

                case ZombieStateType.Wander:
                    return IsStateEnabled(ZombieStateType.Idle) ? ZombieStateType.Idle : (ZombieStateType?)null;

                default:
                    return ResolvePassiveState();
            }
        }

        private ZombieStateType? ResolvePassiveState()
        {
            if (IsStateEnabled(ZombieStateType.Idle) && IsStateEnabled(ZombieStateType.Wander))
            {
                return UnityEngine.Random.value < 0.5f ? ZombieStateType.Idle : ZombieStateType.Wander;
            }

            if (IsStateEnabled(ZombieStateType.Idle))
            {
                return ZombieStateType.Idle;
            }

            if (IsStateEnabled(ZombieStateType.Wander))
            {
                return ZombieStateType.Wander;
            }

            return null;
        }

        private void EvaluateVision(float deltaTime)
        {
            visibleRegionCount = 0;

            if (isDead)
            {
                ResetDetection();
                return;
            }

            if (target == null || !target.isActiveAndEnabled)
            {
                ResetDetection();
                return;
            }

            if (!IsTargetLayerAllowed())
            {
                ResetDetection();
                return;
            }

            for (int i = 0; i < VisionRegionOrder.Length; i++)
            {
                ZombieVisionRegion region = VisionRegionOrder[i];
                if ((profile.detection.raycastRegions & region) == 0)
                {
                    continue;
                }

                if (CanSeeRegion(region))
                {
                    visibleRegionCount++;
                }
            }

            bool fullCandidate = visibleRegionCount >= profile.detection.minimumRegionsForChase;
            bool partialCandidate = visibleRegionCount >= profile.detection.minimumRegionsForInspect;

            if (fullCandidate)
            {
                chaseDetectionTimer += deltaTime;
                inspectDetectionTimer = 0f;
                SetLastKnownTargetPosition(target.transform.position);
                SetSuspiciousPosition(target.transform.position);
            }
            else if (partialCandidate)
            {
                chaseDetectionTimer = 0f;
                inspectDetectionTimer += deltaTime;
                SetSuspiciousPosition(target.transform.position);
            }
            else
            {
                chaseDetectionTimer = 0f;
                inspectDetectionTimer = 0f;
            }

            hasFullDetection = fullCandidate && chaseDetectionTimer >= profile.detection.chaseConfirmationTime;
            hasPartialDetection = !hasFullDetection && partialCandidate && inspectDetectionTimer >= profile.detection.inspectConfirmationTime;
        }

        private bool CanSeeRegion(ZombieVisionRegion region)
        {
            return TryEvaluateRegionVisibility(region, out VisionDebugRay ray) && ray.isVisible;
        }

        private bool IsTargetLayerAllowed()
        {
            int mask = profile.detection.targetMask.value;
            return (mask & (1 << target.gameObject.layer)) != 0;
        }

        private bool TryEvaluateRegionVisibility(ZombieVisionRegion region, out VisionDebugRay ray)
        {
            ray = new VisionDebugRay
            {
                region = region,
                eyePosition = GetEyePosition()
            };

            if (profile == null || target == null || !target.isActiveAndEnabled || !IsTargetLayerAllowed())
            {
                return false;
            }

            ray.regionPosition = target.GetRegionPosition(region);
            Vector3 toRegion = ray.regionPosition - ray.eyePosition;
            ray.distance = toRegion.magnitude;

            if (ray.distance <= 0.001f)
            {
                return true;
            }

            ray.direction = toRegion / ray.distance;
            ray.withinViewDistance = ray.distance <= profile.detection.viewDistance;
            ray.withinFieldOfView = Vector3.Angle(transform.forward, ray.direction) <= profile.detection.fieldOfView * 0.5f;

            if (!ray.withinViewDistance || !ray.withinFieldOfView)
            {
                return true;
            }

            int obstructionMask = profile.detection.obstructionMask.value;
            if (obstructionMask == 0)
            {
                ray.isVisible = true;
                return true;
            }

            if (Physics.Raycast(ray.eyePosition, ray.direction, out RaycastHit hit, ray.distance, obstructionMask, profile.detection.triggerInteraction))
            {
                ray.hasHit = true;
                ray.hitPosition = hit.point;
                ray.hitCollider = hit.collider;
                ray.isVisible = target.ContainsCollider(hit.collider);
                return true;
            }

            ray.isVisible = true;
            return true;
        }

        private Vector3 GetEyePosition()
        {
            return transform.TransformPoint(profile.detection.eyeOffset);
        }

        private void DrawDebugVisionGizmos()
        {
            Vector3 eyePosition = GetEyePosition();
            DrawDebugViewDistanceAndFieldOfView(eyePosition);
            DrawDebugRegionRays();
        }

        private void DrawDebugViewDistanceAndFieldOfView(Vector3 eyePosition)
        {
            float viewDistance = Mathf.Max(0.1f, profile.detection.viewDistance);
            float fieldOfView = Mathf.Clamp(profile.detection.fieldOfView, 1f, 360f);
            Vector3 forward = transform.forward.sqrMagnitude > 0.0001f ? transform.forward.normalized : Vector3.forward;
            Vector3 upAxis = transform.up.sqrMagnitude > 0.0001f ? transform.up.normalized : Vector3.up;

            Gizmos.color = DebugFovColor;
            DrawDebugArc(eyePosition, forward, upAxis, fieldOfView, viewDistance);

            float halfFov = fieldOfView * 0.5f;
            DrawDebugFovBoundary(eyePosition, forward, upAxis, -halfFov, viewDistance);
            DrawDebugFovBoundary(eyePosition, forward, upAxis, halfFov, viewDistance);
        }

        private static void DrawDebugArc(Vector3 center, Vector3 forward, Vector3 axis, float angle, float radius)
        {
            int segments = Mathf.Max(4, DebugFovArcSegments);
            Vector3 previousPoint = center + Quaternion.AngleAxis(-angle * 0.5f, axis) * forward * radius;

            for (int i = 1; i <= segments; i++)
            {
                float t = i / (float)segments;
                float currentAngle = Mathf.Lerp(-angle * 0.5f, angle * 0.5f, t);
                Vector3 currentPoint = center + Quaternion.AngleAxis(currentAngle, axis) * forward * radius;
                Gizmos.DrawLine(previousPoint, currentPoint);
                previousPoint = currentPoint;
            }
        }

        private static void DrawDebugFovBoundary(Vector3 eyePosition, Vector3 forward, Vector3 axis, float angle, float distance)
        {
            Vector3 direction = Quaternion.AngleAxis(angle, axis) * forward;
            Gizmos.DrawLine(eyePosition, eyePosition + direction.normalized * distance);
        }

        private void DrawDebugRegionRays()
        {
            if (target == null || !target.isActiveAndEnabled)
            {
                return;
            }

            float markerRadius = 0.1f;
            for (int i = 0; i < VisionRegionOrder.Length; i++)
            {
                ZombieVisionRegion region = VisionRegionOrder[i];
                if ((profile.detection.raycastRegions & region) == 0)
                {
                    continue;
                }

                if (!TryEvaluateRegionVisibility(region, out VisionDebugRay ray))
                {
                    Vector3 regionPosition = target.GetRegionPosition(region);
                    Gizmos.color = DebugInactiveRayColor;
                    Gizmos.DrawLine(GetEyePosition(), regionPosition);
                    Gizmos.DrawWireSphere(regionPosition, markerRadius);
                    continue;
                }

                if (ray.isVisible)
                {
                    Gizmos.color = DebugVisibleRayColor;
                    Gizmos.DrawLine(ray.eyePosition, ray.regionPosition);
                    Gizmos.DrawSphere(ray.regionPosition, markerRadius);
                    continue;
                }

                Gizmos.color = ray.withinViewDistance && ray.withinFieldOfView ? DebugBlockedRayColor : DebugInactiveRayColor;
                Vector3 rayEnd = ray.hasHit ? ray.hitPosition : ray.regionPosition;
                Gizmos.DrawLine(ray.eyePosition, rayEnd);
                Gizmos.DrawWireSphere(ray.regionPosition, markerRadius);

                if (ray.hasHit)
                {
                    Gizmos.DrawCube(ray.hitPosition, Vector3.one * markerRadius);
                    Gizmos.color = DebugRayRemainderColor;
                    Gizmos.DrawLine(ray.hitPosition, ray.regionPosition);
                }
            }
        }

        internal void ResetDetection()
        {
            visibleRegionCount = 0;
            chaseDetectionTimer = 0f;
            inspectDetectionTimer = 0f;
            hasFullDetection = false;
            hasPartialDetection = false;
        }

        private bool CanUseAgent()
        {
            return agent != null && agent.enabled && agent.isOnNavMesh;
        }

        private bool IsBehaviorLocked()
        {
            return currentState != null && (currentState.StateType == ZombieStateType.Stun ||
                (currentState.StateType == ZombieStateType.Eat && !profile.hunger.detectTargetWhileEating) ||
                currentState.StateType == ZombieStateType.Dead);
        }

        private void CacheAnimatorParameters()
        {
            animatorParameterTypes.Clear();
            cachedAnimatorController = animator != null ? animator.runtimeAnimatorController : null;

            if (animator == null || cachedAnimatorController == null)
            {
                return;
            }

            AnimatorControllerParameter[] parameters = animator.parameters;
            for (int i = 0; i < parameters.Length; i++)
            {
                AnimatorControllerParameter parameter = parameters[i];
                animatorParameterTypes[parameter.nameHash] = parameter.type;
            }
        }

        private void RefreshAnimatorParameterCacheIfNeeded()
        {
            RuntimeAnimatorController controller = animator != null ? animator.runtimeAnimatorController : null;
            if (controller != cachedAnimatorController)
            {
                CacheAnimatorParameters();
                ApplyCrawlingAnimatorMode();
            }
        }

        private void SetAnimatorState(ZombieStateType? stateType)
        {
            SetAnimatorBool(profile.animator.idleBool, stateType == ZombieStateType.Idle);
            SetAnimatorBool(profile.animator.wanderingBool, stateType == ZombieStateType.Wander);
            SetAnimatorBool(profile.animator.alertedBool, stateType == ZombieStateType.Alert);
            SetAnimatorBool(profile.animator.chasingBool, stateType == ZombieStateType.Chase);
            SetAnimatorBool(profile.animator.searchingBool, stateType == ZombieStateType.Search);
            SetAnimatorBool(profile.animator.inspectingBool, stateType == ZombieStateType.Inspect);
            SetAnimatorBool(profile.animator.stunnedBool, stateType == ZombieStateType.Stun);
            SetAnimatorBool(profile.animator.deadBool, stateType == ZombieStateType.Dead);
            SetAnimatorBool(profile.animator.eatingBool, false);
            SetAnimatorBool(profile.animator.isInCrawlModeBool, IsCrawling);
            SetAnimatorBool(profile.animator.crawlStunnedBool, IsCrawling && stateType == ZombieStateType.Stun);
            SetAnimatorBool(profile.animator.crawlDeadBool, IsCrawling && stateType == ZombieStateType.Dead);
            SetCrawlingAnimatorLayerWeight(ShouldKeepCrawlingAnimatorLayerActive() ? 1f : 0f);
        }

        internal void SetEatingAnimationActive(bool active)
        {
            isEatingFood = active;

            if (profile != null)
            {
                SetAnimatorBool(profile.animator.eatingBool, active);
            }
        }

        private void UpdateLocomotionAnimator(float deltaTime)
        {
            if (profile == null)
            {
                return;
            }

            float locomotionBlendY = GetLocomotionBlendY();
            float locomotionBlendX = GetLocomotionBlendX();

            SetAnimatorFloat(profile.animator.locomotionBlendYFloat, locomotionBlendY, profile.animator.locomotionBlendDampTime, deltaTime);
            SetAnimatorFloat(profile.animator.locomotionBlendXFloat, locomotionBlendX, profile.animator.locomotionBlendDampTime, deltaTime);
        }

        private float GetLocomotionBlendY()
        {
            if (!CanUseAgent())
            {
                return 0f;
            }

            float actualSpeed = agent.velocity.magnitude;
            if (actualSpeed <= 0.01f)
            {
                return 0f;
            }

            ZombieStateType? stateType = CurrentStateType;
            if (!stateType.HasValue)
            {
                return 0f;
            }

            if (IsCrawling)
            {
                switch (stateType.Value)
                {
                    case ZombieStateType.Wander:
                    case ZombieStateType.Inspect:
                    case ZombieStateType.Chase:
                    case ZombieStateType.Attack:
                        return ScaleSpeedToUnit(actualSpeed, profile.crawling.speed);
                }
            }

            switch (stateType.Value)
            {
                case ZombieStateType.Wander:
                    if (HasCurrentFood())
                    {
                        return ScaleSpeedToUnit(actualSpeed, profile.hunger.moveSpeed);
                    }

                    return ScaleSpeedToUnit(actualSpeed, profile.wander.speed);

                case ZombieStateType.Inspect:
                    return ScaleSpeedToUnit(actualSpeed, profile.inspect.speed);

                case ZombieStateType.Eat:
                    return ScaleSpeedToUnit(actualSpeed, profile.hunger.moveSpeed);

                case ZombieStateType.Chase:
                    return ScaleSpeedToUnit(actualSpeed, profile.chase.speed);

                case ZombieStateType.Attack:
                    return agent.isStopped ? 0f : ScaleSpeedToUnit(actualSpeed, profile.chase.speed);

                default:
                    return 0f;
            }
        }

        private float GetLocomotionBlendX()
        {
            ZombieStateType? stateType = CurrentStateType;
            if (!stateType.HasValue)
            {
                return IdleBlendX;
            }

            switch (stateType.Value)
            {
                case ZombieStateType.Wander:
                case ZombieStateType.Inspect:
                case ZombieStateType.Eat:
                    return WanderBlendX;

                case ZombieStateType.Chase:
                    return ChaseBlendX;

                case ZombieStateType.Attack:
                    return CanUseAgent() && !agent.isStopped ? ChaseBlendX : IdleBlendX;

                default:
                    return IdleBlendX;
            }
        }

        private static float ScaleSpeedToUnit(float actualSpeed, float configuredSpeed)
        {
            if (configuredSpeed <= 0.01f)
            {
                return 1f;
            }

            return Mathf.Clamp01(actualSpeed / configuredSpeed);
        }

        private static float GetAttackTypeValue(ZombieAttackKind attackKind)
        {
            return attackKind == ZombieAttackKind.ChaseAttack ? ChaseReactionType : StandReactionType;
        }

        private static float GetHitReactionTypeValue(ZombieHitReactionKind hitReactionKind)
        {
            return hitReactionKind == ZombieHitReactionKind.ChaseHit ? ChaseReactionType : StandReactionType;
        }

        private void SetAnimatorBool(string parameterName, bool value)
        {
            if (HasAnimatorParameter(parameterName, AnimatorControllerParameterType.Bool))
            {
                animator.SetBool(parameterName, value);
            }
        }

        private void SetAnimatorFloat(string parameterName, float value, float dampTime, float deltaTime)
        {
            if (HasAnimatorParameter(parameterName, AnimatorControllerParameterType.Float))
            {
                animator.SetFloat(parameterName, value, dampTime, deltaTime);
            }
        }

        private void SetAnimatorFloatImmediate(string parameterName, float value)
        {
            if (HasAnimatorParameter(parameterName, AnimatorControllerParameterType.Float))
            {
                animator.SetFloat(parameterName, value);
            }
        }

        private void SetAnimatorTrigger(string parameterName)
        {
            if (HasAnimatorParameter(parameterName, AnimatorControllerParameterType.Trigger))
            {
                animator.SetTrigger(parameterName);
            }
        }

        private bool HasAnimatorParameter(string parameterName, AnimatorControllerParameterType expectedType)
        {
            if (animator == null || string.IsNullOrWhiteSpace(parameterName))
            {
                return false;
            }

            int parameterHash = Animator.StringToHash(parameterName);
            return animatorParameterTypes.TryGetValue(parameterHash, out AnimatorControllerParameterType actualType) && actualType == expectedType;
        }

        private void InvokePendingAttackEvent()
        {
            if (!hasPendingAttackEvent)
            {
                return;
            }

            hasPendingAttackEvent = false;
            if (pendingAttackKind == ZombieAttackKind.StandAttack)
            {
                onStandAttack.Invoke(this, target);
            }
            else
            {
                onChaseAttack.Invoke(this, target);
            }
        }
    }

    [Serializable]
    public sealed class ZombieAttackEvent : UnityEvent<ZombieAI, ZombieTarget>
    {
    }

    internal struct VisionDebugRay
    {
        public ZombieVisionRegion region;
        public Vector3 eyePosition;
        public Vector3 regionPosition;
        public Vector3 direction;
        public Vector3 hitPosition;
        public Collider hitCollider;
        public float distance;
        public bool withinViewDistance;
        public bool withinFieldOfView;
        public bool hasHit;
        public bool isVisible;
    }
}
