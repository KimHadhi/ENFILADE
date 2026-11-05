using UnityEngine;

namespace AdvancedZombieAIPro
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Advanced Zombie AI Pro/Zombie Audio Controller")]
    public sealed class ZombieAudioController : MonoBehaviour
    {
        private const string CrawlingTransitionStateName = "CrawlingTransition";
        private const string LoopSourceObjectName = "LoopSource";
        private const string OneShotSourceObjectName = "OneShotSource";

        [SerializeField] private ZombieAudioProfile audioProfile = null;
        [SerializeField] private ZombieAI zombieAI = null;
        [SerializeField] private ZombieHealth health = null;
        [SerializeField] private AudioSource loopSource = null;
        [SerializeField] private AudioSource oneShotSource = null;
        [SerializeField] private bool respectGamePause = true;
        [SerializeField] private bool stopAudioOnDisable = true;

        private ZombieHealth subscribedHealth;
        private ZombieAI subscribedZombieAI;
        private ZombieAudioState currentAudioState = ZombieAudioState.None;
        private ZombieAudioProfile cachedAudioProfile;
        private ZombieTimedOneShotAudioSettings activeTimedOneShots;
        private float oneShotTimer;
        private bool pausedByController;
        private bool deathAudioPlayed;
        private int lastHitAudioFrame = -1;
        private AudioClip currentOneShotClip;
        private float currentOneShotEndTime;

        public ZombieAudioProfile AudioProfile => audioProfile;
        public ZombieAI ZombieAI => zombieAI;
        public ZombieHealth Health => health;
        public AudioSource LoopSource => loopSource;
        public AudioSource OneShotSource => oneShotSource;
        public AudioClip CurrentLoopClip => loopSource != null && loopSource.isPlaying ? loopSource.clip : null;
        public AudioClip CurrentOneShotClip => HasActiveOneShotClip() ? currentOneShotClip : null;

        private void Reset()
        {
            zombieAI = GetComponent<ZombieAI>();
            health = GetComponent<ZombieHealth>();
            loopSource = FindChildAudioSource(LoopSourceObjectName);
            if (loopSource == null)
            {
                loopSource = GetComponent<AudioSource>();
            }

            oneShotSource = FindChildAudioSource(OneShotSourceObjectName);
        }

        private void Awake()
        {
            RefreshReferences();
            EnsureAudioSources();
            ApplyPlaybackSettings();
        }

        private void OnEnable()
        {
            RefreshReferences();
            EnsureAudioSources();
            ApplyPlaybackSettings();
            SubscribeHealth();
            SubscribeZombieAI();
            ForceStateRefresh();
        }

        private void OnDisable()
        {
            UnsubscribeHealth();
            UnsubscribeZombieAI();

            if (stopAudioOnDisable)
            {
                StopAllAudio();
            }
        }

        private void OnValidate()
        {
            if (zombieAI == null)
            {
                zombieAI = GetComponent<ZombieAI>();
            }

            if (health == null)
            {
                health = GetComponent<ZombieHealth>();
            }
        }

        private void Update()
        {
            RefreshRuntimeReferences();

            bool paused = IsPlaybackPaused();
            ApplyPauseState(paused);
            if (paused)
            {
                return;
            }

            if (audioProfile != cachedAudioProfile)
            {
                cachedAudioProfile = audioProfile;
                ForceStateRefresh();
            }

            UpdateAudioState();
            UpdateTimedOneShots(Time.deltaTime);

            if (health != null && health.IsAlive && currentAudioState != ZombieAudioState.Death)
            {
                deathAudioPlayed = false;
            }
        }

        private void RefreshReferences()
        {
            if (zombieAI == null)
            {
                zombieAI = GetComponent<ZombieAI>();
            }

            if (health == null)
            {
                health = GetComponent<ZombieHealth>();
            }
        }

        private void RefreshRuntimeReferences()
        {
            if (zombieAI == null)
            {
                zombieAI = GetComponent<ZombieAI>();
            }

            if (health == null)
            {
                health = zombieAI != null ? zombieAI.Health : GetComponent<ZombieHealth>();
            }

            if (subscribedHealth != health)
            {
                SubscribeHealth();
            }

            if (subscribedZombieAI != zombieAI)
            {
                SubscribeZombieAI();
            }

            if (loopSource == null || oneShotSource == null)
            {
                EnsureAudioSources();
            }
        }

        private void EnsureAudioSources()
        {
            if (loopSource == null)
            {
                loopSource = FindChildAudioSource(LoopSourceObjectName);
                if (loopSource == null)
                {
                    loopSource = GetComponent<AudioSource>();
                }

                if (loopSource == null)
                {
                    loopSource = gameObject.AddComponent<AudioSource>();
                }
            }

            if (oneShotSource == null)
            {
                oneShotSource = FindChildAudioSource(OneShotSourceObjectName);
                if (oneShotSource == null)
                {
                    oneShotSource = gameObject.AddComponent<AudioSource>();
                }
            }

            ConfigureSource(loopSource, true);
            ConfigureSource(oneShotSource, false);
        }

        private static void ConfigureSource(AudioSource source, bool loop)
        {
            if (source == null)
            {
                return;
            }

            source.playOnAwake = false;
            source.loop = loop;
            source.ignoreListenerPause = false;
        }

        private AudioSource FindChildAudioSource(string childName)
        {
            Transform child = transform.Find(childName);
            return child != null ? child.GetComponent<AudioSource>() : null;
        }

        private void SubscribeHealth()
        {
            UnsubscribeHealth();

            if (health == null)
            {
                return;
            }

            subscribedHealth = health;
            subscribedHealth.Damaged += HandleDamaged;
            subscribedHealth.HealthDepleted += HandleHealthDepleted;
        }

        private void UnsubscribeHealth()
        {
            if (subscribedHealth == null)
            {
                return;
            }

            subscribedHealth.Damaged -= HandleDamaged;
            subscribedHealth.HealthDepleted -= HandleHealthDepleted;
            subscribedHealth = null;
        }

        private void SubscribeZombieAI()
        {
            UnsubscribeZombieAI();

            if (zombieAI == null)
            {
                return;
            }

            subscribedZombieAI = zombieAI;
            subscribedZombieAI.AttackAnimationHit += HandleAttackAnimationHit;
        }

        private void UnsubscribeZombieAI()
        {
            if (subscribedZombieAI == null)
            {
                return;
            }

            subscribedZombieAI.AttackAnimationHit -= HandleAttackAnimationHit;
            subscribedZombieAI = null;
        }

        private void HandleDamaged(ZombieDamageInfo damageInfo)
        {
            if (IsPlaybackPaused() || audioProfile == null || damageInfo.appliedDamage <= 0f)
            {
                return;
            }

            ZombieProfile profile = zombieAI != null ? zombieAI.Profile : null;
            if (profile != null && damageInfo.appliedDamage < profile.hit.minimumDamageToReact)
            {
                return;
            }

            if (TryPlayOneShot(audioProfile.hit))
            {
                lastHitAudioFrame = Time.frameCount;
            }
        }

        private void HandleHealthDepleted(ZombieDamageInfo damageInfo)
        {
            TryPlayDeathAudio();
        }

        private void HandleAttackAnimationHit(
            ZombieAI attackingZombie,
            ZombieTarget attackTarget,
            ZombieAttackKind attackKind)
        {
            if (IsPlaybackPaused() || audioProfile == null)
            {
                return;
            }

            ZombieOneShotAudioSettings settings = attackKind == ZombieAttackKind.ChaseAttack
                ? audioProfile.chaseAttack
                : audioProfile.standAttack;
            TryPlayOneShot(settings);
        }

        private void UpdateAudioState()
        {
            ZombieAudioState nextState = ResolveAudioState();
            if (nextState == currentAudioState)
            {
                return;
            }

            ExitAudioState();
            currentAudioState = nextState;
            EnterAudioState(nextState);
        }

        private ZombieAudioState ResolveAudioState()
        {
            if (zombieAI == null)
            {
                return ZombieAudioState.None;
            }

            if (zombieAI.CurrentStateName == CrawlingTransitionStateName)
            {
                return ZombieAudioState.CrawlFall;
            }

            if (zombieAI.IsDead)
            {
                return ZombieAudioState.Death;
            }

            ZombieStateType? stateType = zombieAI.CurrentStateType;
            if (!stateType.HasValue)
            {
                return ZombieAudioState.None;
            }

            bool isCrawling = zombieAI.IsCrawling;
            switch (stateType.Value)
            {
                case ZombieStateType.Idle:
                    return isCrawling ? ZombieAudioState.CrawlIdle : ZombieAudioState.Idle;
                case ZombieStateType.Wander:
                    return isCrawling ? ZombieAudioState.CrawlMovement : ZombieAudioState.Wander;
                case ZombieStateType.Alert:
                    return ZombieAudioState.Alert;
                case ZombieStateType.Chase:
                    return isCrawling ? ZombieAudioState.CrawlMovement : ZombieAudioState.Chase;
                case ZombieStateType.Search:
                    return isCrawling ? ZombieAudioState.CrawlIdle : ZombieAudioState.Search;
                case ZombieStateType.Inspect:
                    return isCrawling ? ZombieAudioState.CrawlMovement : ZombieAudioState.Inspect;
                case ZombieStateType.Stun:
                    return ZombieAudioState.Stun;
                case ZombieStateType.Hit:
                    return ZombieAudioState.Hit;
                case ZombieStateType.Eat:
                    return ZombieAudioState.Eating;
                case ZombieStateType.Dead:
                    return ZombieAudioState.Death;
                default:
                    return ZombieAudioState.None;
            }
        }

        private void EnterAudioState(ZombieAudioState audioState)
        {
            if (audioProfile == null)
            {
                return;
            }

            ApplyPlaybackSettings();

            switch (audioState)
            {
                case ZombieAudioState.Idle:
                    StartTimedOneShots(audioProfile.idle);
                    break;
                case ZombieAudioState.Wander:
                    StartLoopAndTimedOneShots(audioProfile.wander);
                    break;
                case ZombieAudioState.Alert:
                    TryPlayOneShot(audioProfile.alert);
                    break;
                case ZombieAudioState.Chase:
                    StartLoopAndTimedOneShots(audioProfile.chase);
                    break;
                case ZombieAudioState.Search:
                    StartTimedOneShots(audioProfile.search);
                    break;
                case ZombieAudioState.Inspect:
                    StartLoopAndTimedOneShots(audioProfile.inspect);
                    break;
                case ZombieAudioState.Hit:
                    TryPlayHitStateAudio();
                    break;
                case ZombieAudioState.Stun:
                    StartLoop(audioProfile.stun);
                    break;
                case ZombieAudioState.Eating:
                    StartLoop(audioProfile.eatingLoop);
                    break;
                case ZombieAudioState.CrawlFall:
                    TryPlayOneShot(audioProfile.crawlFall);
                    break;
                case ZombieAudioState.CrawlIdle:
                    StartTimedOneShots(audioProfile.crawlIdle);
                    break;
                case ZombieAudioState.CrawlMovement:
                    StartLoopAndTimedOneShots(audioProfile.crawlMovement);
                    break;
                case ZombieAudioState.Death:
                    TryPlayDeathAudio();
                    break;
            }
        }

        private void ExitAudioState()
        {
            StopLoop();
            activeTimedOneShots = null;
            oneShotTimer = 0f;
        }

        private void StartLoopAndTimedOneShots(ZombieLoopAndTimedOneShotAudioSettings settings)
        {
            if (settings == null)
            {
                return;
            }

            StartLoop(settings.loop);
            StartTimedOneShots(settings.oneShots);
        }

        private void StartLoop(ZombieLoopAudioSettings settings)
        {
            StopLoop();

            if (settings == null || !settings.CanPlay || loopSource == null)
            {
                return;
            }

            AudioClip clip = settings.GetRandomClip();
            if (clip == null)
            {
                return;
            }

            ApplyPlaybackSettings();
            loopSource.clip = clip;
            loopSource.loop = true;
            loopSource.volume = GetScaledVolume(settings.volume);
            loopSource.Play();
        }

        private void StopLoop()
        {
            if (loopSource == null)
            {
                return;
            }

            loopSource.Stop();
            loopSource.clip = null;
        }

        private void StartTimedOneShots(ZombieTimedOneShotAudioSettings settings)
        {
            activeTimedOneShots = settings != null && settings.CanPlay ? settings : null;
            oneShotTimer = activeTimedOneShots != null ? activeTimedOneShots.GetRandomInterval() : 0f;
        }

        private void UpdateTimedOneShots(float deltaTime)
        {
            if (activeTimedOneShots == null)
            {
                return;
            }

            oneShotTimer -= deltaTime;
            if (oneShotTimer > 0f)
            {
                return;
            }

            TryPlayOneShot(activeTimedOneShots);
            oneShotTimer = activeTimedOneShots.GetRandomInterval();
        }

        private void TryPlayHitStateAudio()
        {
            if (lastHitAudioFrame == Time.frameCount)
            {
                return;
            }

            if (TryPlayOneShot(audioProfile.hit))
            {
                lastHitAudioFrame = Time.frameCount;
            }
        }

        private void TryPlayDeathAudio()
        {
            if (deathAudioPlayed || audioProfile == null)
            {
                return;
            }

            if (IsPlaybackPaused())
            {
                return;
            }

            StopLoop();
            deathAudioPlayed = true;
            TryPlayOneShot(audioProfile.death);
        }

        private bool TryPlayOneShot(ZombieAudioClipSettings settings)
        {
            if (settings == null || !settings.CanPlay || oneShotSource == null || IsPlaybackPaused())
            {
                return false;
            }

            AudioClip clip = settings.GetRandomClip();
            if (clip == null)
            {
                return false;
            }

            ApplyPlaybackSettings();
            currentOneShotClip = clip;
            currentOneShotEndTime = Time.unscaledTime + Mathf.Max(0f, clip.length);
            oneShotSource.PlayOneShot(clip, GetScaledVolume(settings.volume));
            return true;
        }

        private float GetScaledVolume(float categoryVolume)
        {
            return audioProfile != null
                ? Mathf.Clamp01(audioProfile.playback.masterVolume) * Mathf.Clamp01(categoryVolume)
                : Mathf.Clamp01(categoryVolume);
        }

        private void ApplyPlaybackSettings()
        {
            if (audioProfile == null)
            {
                return;
            }

            audioProfile.playback.ApplyTo(loopSource);
            audioProfile.playback.ApplyTo(oneShotSource);
        }

        private bool IsPlaybackPaused()
        {
            return respectGamePause && (Time.timeScale <= 0f || AudioListener.pause);
        }

        private void ApplyPauseState(bool paused)
        {
            if (pausedByController == paused)
            {
                return;
            }

            pausedByController = paused;
            if (paused)
            {
                if (loopSource != null)
                {
                    loopSource.Pause();
                }

                if (oneShotSource != null)
                {
                    oneShotSource.Pause();
                }

                return;
            }

            if (loopSource != null)
            {
                loopSource.UnPause();
            }

            if (oneShotSource != null)
            {
                oneShotSource.UnPause();
            }
        }

        private void StopAllAudio()
        {
            if (loopSource != null)
            {
                loopSource.Stop();
                loopSource.clip = null;
            }

            if (oneShotSource != null)
            {
                oneShotSource.Stop();
            }

            activeTimedOneShots = null;
            pausedByController = false;
            currentOneShotClip = null;
            currentOneShotEndTime = 0f;
        }

        private void ForceStateRefresh()
        {
            ExitAudioState();
            currentAudioState = ZombieAudioState.None;
            cachedAudioProfile = audioProfile;
        }

        private bool HasActiveOneShotClip()
        {
            if (currentOneShotClip == null)
            {
                return false;
            }

            if (oneShotSource != null && oneShotSource.isPlaying)
            {
                return true;
            }

            return Time.unscaledTime < currentOneShotEndTime;
        }

        private enum ZombieAudioState
        {
            None,
            Idle,
            Wander,
            Alert,
            Chase,
            Search,
            Inspect,
            Hit,
            Stun,
            Eating,
            CrawlFall,
            CrawlIdle,
            CrawlMovement,
            Death
        }
    }
}
