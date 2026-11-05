using System;
using UnityEngine;
using UnityEngine.Events;

namespace AdvancedZombieAIPro
{
    [DisallowMultipleComponent]
    public sealed class ZombieHealth : MonoBehaviour
    {
        private const float DefaultMaximumHealth = 100f;

        [SerializeField] private ZombieAI zombieAI = null;
        private float currentHealth = 100f;
        private bool isAlive = true;

        [SerializeField] private ZombieDamagedEvent onDamaged = new ZombieDamagedEvent();
        [SerializeField] private ZombieHealthEvent onHealthChanged = new ZombieHealthEvent();
        [SerializeField] private ZombieDamageInfoEvent onHealthDepleted = new ZombieDamageInfoEvent();

        public event Action<ZombieDamageInfo> Damaged;
        public event Action<ZombieDamageInfo> HealthDepleted;

        public ZombieAI ZombieAI => zombieAI;
        public ZombieProfile Profile => zombieAI != null ? zombieAI.Profile : null;
        public float CurrentHealth => currentHealth;
        public float MaximumHealth => zombieAI != null ? zombieAI.CurrentMaximumHealth : DefaultMaximumHealth;
        public bool IsAlive => isAlive;
        public ZombieDamagedEvent OnDamaged => onDamaged;
        public ZombieHealthEvent OnHealthChanged => onHealthChanged;
        public ZombieDamageInfoEvent OnHealthDepleted => onHealthDepleted;

        private void Reset()
        {
            zombieAI = GetComponent<ZombieAI>();
            ResetHealth();
        }

        private void Awake()
        {
            if (zombieAI == null)
            {
                zombieAI = GetComponent<ZombieAI>();
            }

            if (Profile != null && Profile.health.resetHealthOnAwake)
            {
                ResetHealth();
            }
            else
            {
                ClampCurrentHealth();
            }
        }

        private void OnValidate()
        {
            if (zombieAI == null)
            {
                zombieAI = GetComponent<ZombieAI>();
            }

            ClampCurrentHealth();
        }

        public void ResetHealth()
        {
            ZombieProfile zombieProfile = Profile;
            if (zombieProfile != null)
            {
                float maximumHealth = zombieAI != null ? zombieAI.ConfiguredStartingMaximumHealth : zombieProfile.health.maximumHealth;
                float startingHealth = zombieAI != null ? zombieAI.ConfiguredStartingHealth : zombieProfile.health.startingHealth;
                currentHealth = Mathf.Clamp(startingHealth, 0f, maximumHealth);
            }
            else if (currentHealth <= 0f)
            {
                currentHealth = DefaultMaximumHealth;
            }

            isAlive = currentHealth > 0f;
            onHealthChanged.Invoke(this);
        }

        public void SetCurrentHealth(float health)
        {
            float previousHealth = currentHealth;
            currentHealth = Mathf.Clamp(health, 0f, Mathf.Max(0.01f, MaximumHealth));
            isAlive = currentHealth > 0f;

            if (!Mathf.Approximately(previousHealth, currentHealth))
            {
                onHealthChanged.Invoke(this);
            }
        }

        public void ApplyDamage(float damage)
        {
            ApplyDamage(damage, null, transform.position, false, Vector3.zero, false);
        }

        public void ApplyDamage(float damage, GameObject attacker)
        {
            Vector3 hitPoint = attacker != null ? attacker.transform.position : transform.position;
            ApplyDamage(damage, attacker, hitPoint, attacker != null, Vector3.zero, false);
        }

        public void ApplyDamage(float damage, GameObject attacker, Vector3 hitPoint)
        {
            ApplyDamage(damage, attacker, hitPoint, true, Vector3.zero, false);
        }

        public void ApplyDamage(float damage, GameObject attacker, Vector3 hitPoint, Vector3 hitDirection)
        {
            ApplyDamage(damage, attacker, hitPoint, true, hitDirection, hitDirection.sqrMagnitude > 0.0001f);
        }

        public void ApplyDamage(float damage, Component attacker)
        {
            ApplyDamage(damage, attacker != null ? attacker.gameObject : null);
        }

        public void ApplyDamage(float damage, Component attacker, Vector3 hitPoint)
        {
            ApplyDamage(damage, attacker != null ? attacker.gameObject : null, hitPoint);
        }

        public void ApplyDamage(float damage, Component attacker, Vector3 hitPoint, Vector3 hitDirection)
        {
            ApplyDamage(damage, attacker != null ? attacker.gameObject : null, hitPoint, hitDirection);
        }

        private void ApplyDamage(float damage, GameObject attacker, Vector3 hitPoint, bool hasHitPoint, Vector3 hitDirection, bool hasHitDirection)
        {
            if (damage <= 0f || !isAlive)
            {
                return;
            }

            ClampCurrentHealth();

            float previousHealth = currentHealth;
            currentHealth = Mathf.Max(0f, currentHealth - damage);
            isAlive = currentHealth > 0f;

            ZombieDamageInfo damageInfo = new ZombieDamageInfo
            {
                requestedDamage = damage,
                appliedDamage = Mathf.Max(0f, previousHealth - currentHealth),
                previousHealth = previousHealth,
                currentHealth = currentHealth,
                attacker = attacker,
                hitPoint = hitPoint,
                hasHitPoint = hasHitPoint,
                hitDirection = hasHitDirection ? hitDirection.normalized : Vector3.zero,
                hasHitDirection = hasHitDirection,
                time = Time.time
            };

            Damaged?.Invoke(damageInfo);
            onDamaged.Invoke(this, damageInfo.appliedDamage, attacker, hitPoint);
            onHealthChanged.Invoke(this);

            if (isAlive)
            {
                if (zombieAI != null)
                {
                    zombieAI.HandleDamage(damageInfo);
                }

                return;
            }

            if (zombieAI != null)
            {
                zombieAI.HandleDeath(damageInfo);
            }

            if (!isAlive)
            {
                HealthDepleted?.Invoke(damageInfo);
                onHealthDepleted.Invoke(damageInfo);
            }
        }

        private void ClampCurrentHealth()
        {
            float maxHealth = Mathf.Max(0.01f, MaximumHealth);
            currentHealth = Mathf.Clamp(currentHealth, 0f, maxHealth);
            isAlive = currentHealth > 0f;
        }
    }

    [Serializable]
    public struct ZombieDamageInfo
    {
        public float requestedDamage;
        public float appliedDamage;
        public float previousHealth;
        public float currentHealth;
        public GameObject attacker;
        public Vector3 hitPoint;
        public bool hasHitPoint;
        public Vector3 hitDirection;
        public bool hasHitDirection;
        public float time;
    }

    [Serializable]
    public sealed class ZombieDamagedEvent : UnityEvent<ZombieHealth, float, GameObject, Vector3>
    {
    }

    [Serializable]
    public sealed class ZombieHealthEvent : UnityEvent<ZombieHealth>
    {
    }

    [Serializable]
    public sealed class ZombieDamageInfoEvent : UnityEvent<ZombieDamageInfo>
    {
    }
}
