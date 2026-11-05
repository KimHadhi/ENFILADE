using System.Collections.Generic;
using UnityEngine;

namespace AdvancedZombieAIPro
{
    [DisallowMultipleComponent]
    [AddComponentMenu("Advanced Zombie AI Pro/Zombie Food")]
    public sealed class ZombieFood : MonoBehaviour
    {
        private static readonly List<ZombieFood> ActiveFoods = new List<ZombieFood>();

        [SerializeField, Min(0.01f)] private float energy = 35f;
        [SerializeField] private Transform eatPoint = null;
        [SerializeField] private bool destroyWhenDepleted = true;

        [SerializeField] private ZombieHealth sourceZombieHealth = null;

        private float remainingEnergy = 35f;

        public float Energy => energy;
        public float RemainingEnergy => remainingEnergy;
        public Vector3 EatPosition => eatPoint != null ? eatPoint.position : transform.position;
        public bool IsDeadZombieFood => sourceZombieHealth != null;
        public bool IsDepleted => remainingEnergy <= 0f;

        public bool IsAvailable
        {
            get
            {
                if (!isActiveAndEnabled || IsDepleted)
                {
                    return false;
                }

                if (sourceZombieHealth == null)
                {
                    return true;
                }

                return !sourceZombieHealth.IsAlive;
            }
        }

        private void Reset()
        {
            eatPoint = transform;
            sourceZombieHealth = GetComponentInParent<ZombieHealth>();
            remainingEnergy = energy;
        }

        private void Awake()
        {
            if (eatPoint == null)
            {
                eatPoint = transform;
            }

            if (sourceZombieHealth == null)
            {
                sourceZombieHealth = GetComponentInParent<ZombieHealth>();
            }

            if (remainingEnergy <= 0f || remainingEnergy > energy)
            {
                remainingEnergy = energy;
            }
        }

        private void OnEnable()
        {
            if (!ActiveFoods.Contains(this))
            {
                ActiveFoods.Add(this);
            }
        }

        private void OnDisable()
        {
            ActiveFoods.Remove(this);
        }

        private void OnValidate()
        {
            energy = Mathf.Max(0.01f, energy);
            if (!Application.isPlaying)
            {
                remainingEnergy = energy;
            }
        }

        public float Consume(float requestedEnergy)
        {
            if (requestedEnergy <= 0f || !IsAvailable)
            {
                return 0f;
            }

            float consumedEnergy = Mathf.Min(requestedEnergy, remainingEnergy);
            remainingEnergy = Mathf.Max(0f, remainingEnergy - consumedEnergy);

            if (remainingEnergy <= 0f)
            {
                if (destroyWhenDepleted)
                {
                    Destroy(gameObject);
                }
                else
                {
                    enabled = false;
                }
            }

            return consumedEnergy;
        }

        public static bool TryFindClosest(Vector3 origin, float radius, bool includeDeadZombieFood, Transform seeker, out ZombieFood closestFood)
        {
            closestFood = null;
            if (radius <= 0f)
            {
                return false;
            }

            float closestSqrDistance = radius * radius;
            for (int i = ActiveFoods.Count - 1; i >= 0; i--)
            {
                ZombieFood food = ActiveFoods[i];
                if (food == null)
                {
                    ActiveFoods.RemoveAt(i);
                    continue;
                }

                if (!food.IsAvailable)
                {
                    continue;
                }

                if (!includeDeadZombieFood && food.IsDeadZombieFood)
                {
                    continue;
                }

                if (seeker != null && food.transform.root == seeker.root)
                {
                    continue;
                }

                float sqrDistance = (food.EatPosition - origin).sqrMagnitude;
                if (sqrDistance > closestSqrDistance)
                {
                    continue;
                }

                closestSqrDistance = sqrDistance;
                closestFood = food;
            }

            return closestFood != null;
        }
    }
}
