using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace AdvancedZombieAIPro
{
    public enum ZombieSpawnMode
    {
        OverTime,
        OnZombieDeath
    }

    [DisallowMultipleComponent]
    [AddComponentMenu("Advanced Zombie AI Pro/Zombie Spawner")]
    public sealed class ZombieSpawner : MonoBehaviour
    {
        private const float MinimumSpawnInterval = 0.01f;
        private const float DeathSourceRefreshInterval = 1f;
        private const float MinimumNavMeshSampleRadius = 2f;
        private const int MaximumSpawnPositionAttempts = 8;
        private const int SpawnRadiusGizmoSegments = 48;
        private static readonly Color SpawnRadiusGizmoColor = new Color32(0x38, 0xB8, 0xA3, 0xFF);

        [Tooltip("Zombie prefabs this spawner can instantiate.")]
        [SerializeField] private List<ZombieAI> zombiePrefabs = new List<ZombieAI>();
        [Tooltip("Controls whether zombies spawn repeatedly over time or in response to a zombie death.")]
        [SerializeField] private ZombieSpawnMode spawnMode = ZombieSpawnMode.OverTime;
        [Tooltip("Seconds between spawn bursts when Spawn Mode is Over Time.")]
        [SerializeField, Min(MinimumSpawnInterval)] private float spawnInterval = 5f;
        [Tooltip("Number of zombies spawned per burst.")]
        [SerializeField, Min(1)] private int zombieCount = 1;
        [Tooltip("Radius around this object in which zombies can spawn.")]
        [SerializeField, Min(0f)] private float spawnRadius = 2f;

        private readonly Dictionary<ZombieHealth, Action<ZombieDamageInfo>> deathSubscriptions =
            new Dictionary<ZombieHealth, Action<ZombieDamageInfo>>();

        private float spawnTimer;
        private float deathSourceRefreshTimer;
        private ZombieSpawnMode activeSpawnMode;
        private bool missingPrefabWarningLogged;
        private bool missingNavMeshWarningLogged;

        private void OnEnable()
        {
            activeSpawnMode = spawnMode;
            spawnTimer = 0f;
            deathSourceRefreshTimer = 0f;

            if (spawnMode == ZombieSpawnMode.OnZombieDeath)
            {
                RefreshDeathSources();
            }
        }

        private void OnDisable()
        {
            UnregisterAllDeathSources();
        }

        private void Update()
        {
            if (spawnMode != activeSpawnMode)
            {
                ApplySpawnModeChange();
            }

            if (spawnMode == ZombieSpawnMode.OverTime)
            {
                TickOverTimeSpawn();
                return;
            }

            TickDeathSourceRefresh();
        }

        private void OnValidate()
        {
            if (zombiePrefabs == null)
            {
                zombiePrefabs = new List<ZombieAI>();
            }

            spawnInterval = Mathf.Max(MinimumSpawnInterval, spawnInterval);
            zombieCount = Mathf.Max(1, zombieCount);
        }

        private void OnDrawGizmosSelected()
        {
            float radius = Mathf.Max(0f, spawnRadius);
            if (radius <= 0f)
            {
                return;
            }

            Color previousColor = Gizmos.color;
            Gizmos.color = SpawnRadiusGizmoColor;

            Vector3 center = transform.position;
            Vector3 previousPoint = center + Vector3.forward * radius;

            for (int i = 1; i <= SpawnRadiusGizmoSegments; i++)
            {
                float angle = i * Mathf.PI * 2f / SpawnRadiusGizmoSegments;
                Vector3 point = center + new Vector3(Mathf.Sin(angle) * radius, 0f, Mathf.Cos(angle) * radius);
                Gizmos.DrawLine(previousPoint, point);
                previousPoint = point;
            }

            Gizmos.color = previousColor;
        }

        private void ApplySpawnModeChange()
        {
            activeSpawnMode = spawnMode;
            spawnTimer = 0f;
            deathSourceRefreshTimer = 0f;

            if (spawnMode == ZombieSpawnMode.OnZombieDeath)
            {
                RefreshDeathSources();
            }
            else
            {
                UnregisterAllDeathSources();
            }
        }

        private void TickOverTimeSpawn()
        {
            float interval = Mathf.Max(MinimumSpawnInterval, spawnInterval);
            spawnTimer += Time.deltaTime;

            if (spawnTimer < interval)
            {
                return;
            }

            spawnTimer -= interval;
            SpawnZombies();
        }

        private void TickDeathSourceRefresh()
        {
            deathSourceRefreshTimer += Time.deltaTime;
            if (deathSourceRefreshTimer < DeathSourceRefreshInterval)
            {
                return;
            }

            deathSourceRefreshTimer = 0f;
            RefreshDeathSources();
        }

        private void RefreshDeathSources()
        {
            RemoveMissingDeathSources();

            // ENFILADE: Unity 6.4's sort-free overload doesn't exist in 6.3; FindObjectsSortMode.None is the same unsorted call.
            ZombieHealth[] healthComponents = UnityEngine.Object.FindObjectsByType<ZombieHealth>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            for (int i = 0; i < healthComponents.Length; i++)
            {
                RegisterDeathSource(healthComponents[i]);
            }
        }

        private void RegisterSpawnedZombie(ZombieAI zombie)
        {
            if (zombie == null || spawnMode != ZombieSpawnMode.OnZombieDeath)
            {
                return;
            }

            ZombieHealth zombieHealth = zombie.Health != null ? zombie.Health : zombie.GetComponent<ZombieHealth>();
            RegisterDeathSource(zombieHealth);
        }

        private void RegisterDeathSource(ZombieHealth zombieHealth)
        {
            if (zombieHealth == null || !zombieHealth.IsAlive || deathSubscriptions.ContainsKey(zombieHealth))
            {
                return;
            }

            Action<ZombieDamageInfo> deathHandler = damageInfo => HandleZombieDeath(zombieHealth);
            zombieHealth.HealthDepleted += deathHandler;
            deathSubscriptions.Add(zombieHealth, deathHandler);
        }

        private void HandleZombieDeath(ZombieHealth zombieHealth)
        {
            UnregisterDeathSource(zombieHealth);

            if (!isActiveAndEnabled || spawnMode != ZombieSpawnMode.OnZombieDeath)
            {
                return;
            }

            SpawnZombies();
        }

        private void UnregisterDeathSource(ZombieHealth zombieHealth)
        {
            if (zombieHealth == null)
            {
                return;
            }

            if (!deathSubscriptions.TryGetValue(zombieHealth, out Action<ZombieDamageInfo> deathHandler))
            {
                return;
            }

            zombieHealth.HealthDepleted -= deathHandler;
            deathSubscriptions.Remove(zombieHealth);
        }

        private void UnregisterAllDeathSources()
        {
            foreach (KeyValuePair<ZombieHealth, Action<ZombieDamageInfo>> subscription in deathSubscriptions)
            {
                if (subscription.Key != null)
                {
                    subscription.Key.HealthDepleted -= subscription.Value;
                }
            }

            deathSubscriptions.Clear();
        }

        private void RemoveMissingDeathSources()
        {
            if (deathSubscriptions.Count == 0)
            {
                return;
            }

            List<ZombieHealth> missingSources = null;
            foreach (ZombieHealth zombieHealth in deathSubscriptions.Keys)
            {
                if (zombieHealth == null || !zombieHealth.IsAlive)
                {
                    if (missingSources == null)
                    {
                        missingSources = new List<ZombieHealth>();
                    }

                    missingSources.Add(zombieHealth);
                }
            }

            if (missingSources == null)
            {
                return;
            }

            for (int i = 0; i < missingSources.Count; i++)
            {
                RemoveDeathSourceSubscription(missingSources[i]);
            }
        }

        private void RemoveDeathSourceSubscription(ZombieHealth zombieHealth)
        {
            if (!deathSubscriptions.TryGetValue(zombieHealth, out Action<ZombieDamageInfo> deathHandler))
            {
                return;
            }

            if (zombieHealth != null)
            {
                zombieHealth.HealthDepleted -= deathHandler;
            }

            deathSubscriptions.Remove(zombieHealth);
        }

        private void SpawnZombies()
        {
            int count = Mathf.Max(1, zombieCount);
            for (int i = 0; i < count; i++)
            {
                if (!TryGetRandomZombiePrefab(out ZombieAI prefab))
                {
                    if (!missingPrefabWarningLogged)
                    {
                        Debug.LogWarning(
                            $"ZombieSpawner '{name}' cannot spawn because it has no valid zombie prefabs assigned.",
                            this);
                        missingPrefabWarningLogged = true;
                    }

                    return;
                }

                missingPrefabWarningLogged = false;

                int areaMask = prefab.Agent != null ? prefab.Agent.areaMask : NavMesh.AllAreas;
                if (!TryGetSpawnPosition(areaMask, out Vector3 spawnPosition))
                {
                    if (!missingNavMeshWarningLogged)
                    {
                        Debug.LogWarning(
                            $"ZombieSpawner '{name}' could not find a valid NavMesh position near {transform.position} " +
                            $"with a spawn radius of {Mathf.Max(0f, spawnRadius):0.##}. Make sure a NavMesh is baked " +
                            "and the spawner is close enough to it.",
                            this);
                        missingNavMeshWarningLogged = true;
                    }

                    continue;
                }

                missingNavMeshWarningLogged = false;
                ZombieAI zombie = Instantiate(prefab, spawnPosition, transform.rotation);
                RegisterSpawnedZombie(zombie);
            }
        }

        private bool TryGetSpawnPosition(int areaMask, out Vector3 spawnPosition)
        {
            float radius = Mathf.Max(0f, spawnRadius);
            float sampleRadius = Mathf.Max(MinimumNavMeshSampleRadius, radius * 0.25f);

            for (int attempt = 0; attempt < MaximumSpawnPositionAttempts; attempt++)
            {
                Vector2 offset = radius > 0f ? UnityEngine.Random.insideUnitCircle * radius : Vector2.zero;
                Vector3 candidate = transform.position + new Vector3(offset.x, 0f, offset.y);

                if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, sampleRadius, areaMask))
                {
                    spawnPosition = hit.position;
                    return true;
                }
            }

            spawnPosition = default;
            return false;
        }

        private bool TryGetRandomZombiePrefab(out ZombieAI prefab)
        {
            prefab = null;
            if (zombiePrefabs == null || zombiePrefabs.Count == 0)
            {
                return false;
            }

            int validPrefabCount = 0;
            for (int i = 0; i < zombiePrefabs.Count; i++)
            {
                if (zombiePrefabs[i] != null)
                {
                    validPrefabCount++;
                }
            }

            if (validPrefabCount == 0)
            {
                return false;
            }

            int selectedIndex = UnityEngine.Random.Range(0, validPrefabCount);
            for (int i = 0; i < zombiePrefabs.Count; i++)
            {
                ZombieAI candidate = zombiePrefabs[i];
                if (candidate == null)
                {
                    continue;
                }

                if (selectedIndex == 0)
                {
                    prefab = candidate;
                    return true;
                }

                selectedIndex--;
            }

            return false;
        }
    }
}
