using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace AdvancedZombieAIPro
{
    [CreateAssetMenu(fileName = "ZombieAudioProfile", menuName = "Advanced Zombie AI Pro/Zombie Audio Profile")]
    public sealed class ZombieAudioProfile : ScriptableObject
    {
        [Tooltip("Shared AudioSource playback settings used by this audio profile.")]
        public ZombieAudioPlaybackSettings playback = new ZombieAudioPlaybackSettings();

        [Tooltip("Random one-shot sounds played while the zombie is idle.")]
        public ZombieTimedOneShotAudioSettings idle = ZombieTimedOneShotAudioSettings.Create(4f, 9f);
        [Tooltip("Loop and random one-shot sounds played while the zombie is wandering.")]
        public ZombieLoopAndTimedOneShotAudioSettings wander = ZombieLoopAndTimedOneShotAudioSettings.Create(3f, 7f);
        [Tooltip("One-shot sounds played once when the zombie enters Alert.")]
        public ZombieOneShotAudioSettings alert = new ZombieOneShotAudioSettings();
        [Tooltip("Loop and random one-shot sounds played while the zombie is chasing.")]
        public ZombieLoopAndTimedOneShotAudioSettings chase = ZombieLoopAndTimedOneShotAudioSettings.Create(2f, 5f);
        [Tooltip("Random one-shot sounds played while the zombie searches in place.")]
        public ZombieTimedOneShotAudioSettings search = ZombieTimedOneShotAudioSettings.Create(2f, 5f);
        [Tooltip("Loop and random one-shot sounds played while the zombie inspects suspicious positions.")]
        public ZombieLoopAndTimedOneShotAudioSettings inspect = ZombieLoopAndTimedOneShotAudioSettings.Create(2f, 5f);
        [Tooltip("One-shot sounds played from the stand attack animation event.")]
        public ZombieOneShotAudioSettings standAttack = new ZombieOneShotAudioSettings();
        [Tooltip("One-shot sounds played from the chase attack animation event.")]
        public ZombieOneShotAudioSettings chaseAttack = new ZombieOneShotAudioSettings();
        [Tooltip("One-shot sounds played when the zombie receives a hit reaction.")]
        public ZombieOneShotAudioSettings hit = new ZombieOneShotAudioSettings();
        [Tooltip("Loop sounds played while the zombie is stunned.")]
        public ZombieLoopAudioSettings stun = new ZombieLoopAudioSettings();
        [Tooltip("Loop sounds played while the zombie is eating.")]
        public ZombieLoopAudioSettings eatingLoop = new ZombieLoopAudioSettings();
        [Tooltip("One-shot sounds played once when the zombie dies.")]
        public ZombieOneShotAudioSettings death = new ZombieOneShotAudioSettings();

        [Tooltip("One-shot sounds played once when the zombie falls into crawling mode.")]
        public ZombieOneShotAudioSettings crawlFall = new ZombieOneShotAudioSettings();
        [Tooltip("Random one-shot sounds played while the zombie is crawling but not moving.")]
        public ZombieTimedOneShotAudioSettings crawlIdle = ZombieTimedOneShotAudioSettings.Create(3f, 8f);
        [Tooltip("Loop and random one-shot sounds played while the zombie crawls through movement states.")]
        public ZombieLoopAndTimedOneShotAudioSettings crawlMovement = ZombieLoopAndTimedOneShotAudioSettings.Create(2f, 5f);

        private void OnValidate()
        {
            EnsureSettings();
            playback.Validate();
            idle.Validate();
            wander.Validate();
            alert.Validate();
            chase.Validate();
            search.Validate();
            inspect.Validate();
            standAttack.Validate();
            chaseAttack.Validate();
            hit.Validate();
            stun.Validate();
            eatingLoop.Validate();
            death.Validate();
            crawlFall.Validate();
            crawlIdle.Validate();
            crawlMovement.Validate();
        }

        private void EnsureSettings()
        {
            if (playback == null)
            {
                playback = new ZombieAudioPlaybackSettings();
            }

            if (idle == null)
            {
                idle = ZombieTimedOneShotAudioSettings.Create(4f, 9f);
            }

            if (wander == null)
            {
                wander = ZombieLoopAndTimedOneShotAudioSettings.Create(3f, 7f);
            }

            if (alert == null)
            {
                alert = new ZombieOneShotAudioSettings();
            }

            if (chase == null)
            {
                chase = ZombieLoopAndTimedOneShotAudioSettings.Create(2f, 5f);
            }

            if (search == null)
            {
                search = ZombieTimedOneShotAudioSettings.Create(2f, 5f);
            }

            if (inspect == null)
            {
                inspect = ZombieLoopAndTimedOneShotAudioSettings.Create(2f, 5f);
            }

            if (standAttack == null)
            {
                standAttack = new ZombieOneShotAudioSettings();
            }

            if (chaseAttack == null)
            {
                chaseAttack = new ZombieOneShotAudioSettings();
            }

            if (hit == null)
            {
                hit = new ZombieOneShotAudioSettings();
            }

            if (stun == null)
            {
                stun = new ZombieLoopAudioSettings();
            }

            if (eatingLoop == null)
            {
                eatingLoop = new ZombieLoopAudioSettings();
            }

            if (death == null)
            {
                death = new ZombieOneShotAudioSettings();
            }

            if (crawlFall == null)
            {
                crawlFall = new ZombieOneShotAudioSettings();
            }

            if (crawlIdle == null)
            {
                crawlIdle = ZombieTimedOneShotAudioSettings.Create(3f, 8f);
            }

            if (crawlMovement == null)
            {
                crawlMovement = ZombieLoopAndTimedOneShotAudioSettings.Create(2f, 5f);
            }
        }
    }

    [Serializable]
    public sealed class ZombieAudioPlaybackSettings
    {
        [Range(0f, 1f)] public float masterVolume = 1f;
        [Range(0f, 1f)] public float spatialBlend = 1f;
        [Min(0f)] public float minDistance = 1f;
        [Min(0.01f)] public float maxDistance = 25f;
        public AudioRolloffMode rolloffMode = AudioRolloffMode.Logarithmic;

        public void Validate()
        {
            masterVolume = Mathf.Clamp01(masterVolume);
            spatialBlend = Mathf.Clamp01(spatialBlend);
            minDistance = Mathf.Max(0f, minDistance);
            maxDistance = Mathf.Max(0.01f, maxDistance);
            if (maxDistance < minDistance)
            {
                maxDistance = minDistance;
            }
        }

        public void ApplyTo(AudioSource source)
        {
            if (source == null)
            {
                return;
            }

            source.playOnAwake = false;
            source.ignoreListenerPause = false;
            source.spatialBlend = spatialBlend;
            source.minDistance = minDistance;
            source.maxDistance = maxDistance;
            source.rolloffMode = rolloffMode;
        }
    }

    [Serializable]
    public class ZombieAudioClipSettings
    {
        private const float MinimumVolume = 0f;

        [Tooltip("Enables this audio group.")]
        public bool enabled = true;
        [Tooltip("Clips this group can choose from.")]
        public AudioClip[] clips = new AudioClip[0];
        [Tooltip("Volume multiplier for this group.")]
        [Range(0f, 1f)] public float volume = 1f;

        public bool CanPlay
        {
            get
            {
                if (!enabled || clips == null)
                {
                    return false;
                }

                for (int i = 0; i < clips.Length; i++)
                {
                    if (clips[i] != null)
                    {
                        return true;
                    }
                }

                return false;
            }
        }

        public AudioClip GetRandomClip()
        {
            if (!CanPlay)
            {
                return null;
            }

            int startIndex = UnityEngine.Random.Range(0, clips.Length);
            for (int i = 0; i < clips.Length; i++)
            {
                AudioClip clip = clips[(startIndex + i) % clips.Length];
                if (clip != null)
                {
                    return clip;
                }
            }

            return null;
        }

        public virtual void Validate()
        {
            if (clips == null)
            {
                clips = new AudioClip[0];
            }

            volume = Mathf.Max(MinimumVolume, Mathf.Clamp01(volume));
        }
    }

    [Serializable]
    public sealed class ZombieOneShotAudioSettings : ZombieAudioClipSettings
    {
    }

    [Serializable]
    public sealed class ZombieLoopAudioSettings : ZombieAudioClipSettings
    {
    }

    [Serializable]
    public sealed class ZombieTimedOneShotAudioSettings : ZombieAudioClipSettings
    {
        private const float MinimumInterval = 0.05f;

        [Tooltip("Shortest time between random one-shot sounds.")]
        [Min(MinimumInterval)] public float minimumInterval = 3f;
        [Tooltip("Longest time between random one-shot sounds.")]
        [Min(MinimumInterval)] public float maximumInterval = 7f;

        public static ZombieTimedOneShotAudioSettings Create(float minimumInterval, float maximumInterval)
        {
            ZombieTimedOneShotAudioSettings settings = new ZombieTimedOneShotAudioSettings
            {
                minimumInterval = minimumInterval,
                maximumInterval = maximumInterval
            };
            settings.Validate();
            return settings;
        }

        public float GetRandomInterval()
        {
            if (maximumInterval <= minimumInterval)
            {
                return minimumInterval;
            }

            return UnityEngine.Random.Range(minimumInterval, maximumInterval);
        }

        public override void Validate()
        {
            base.Validate();
            minimumInterval = Mathf.Max(MinimumInterval, minimumInterval);
            maximumInterval = Mathf.Max(minimumInterval, maximumInterval);
        }
    }

    [Serializable]
    public sealed class ZombieLoopAndTimedOneShotAudioSettings
    {
        [Tooltip("Loop clips played while the state is active.")]
        public ZombieLoopAudioSettings loop = new ZombieLoopAudioSettings();
        [Tooltip("Random one-shot clips played while the state is active.")]
        public ZombieTimedOneShotAudioSettings oneShots = ZombieTimedOneShotAudioSettings.Create(3f, 7f);

        public static ZombieLoopAndTimedOneShotAudioSettings Create(float minimumInterval, float maximumInterval)
        {
            return new ZombieLoopAndTimedOneShotAudioSettings
            {
                oneShots = ZombieTimedOneShotAudioSettings.Create(minimumInterval, maximumInterval)
            };
        }

        public void Validate()
        {
            if (loop == null)
            {
                loop = new ZombieLoopAudioSettings();
            }

            if (oneShots == null)
            {
                oneShots = ZombieTimedOneShotAudioSettings.Create(3f, 7f);
            }

            loop.Validate();
            oneShots.Validate();
        }
    }
}
