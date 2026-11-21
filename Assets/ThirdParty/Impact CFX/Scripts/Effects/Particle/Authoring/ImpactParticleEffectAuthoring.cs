using System.Collections.Generic;
using UnityEngine;

namespace ImpactCFX.Particles
{
    [CreateAssetMenu(fileName = "New Impact Particle Effect", menuName = "Impact CFX/Particle Effect", order = 2)]
    public class ImpactParticleEffectAuthoring : ImpactMultiPrefabEffectAuthoringBase
    {
        [Tooltip("The template particle prefab.")]
        [SerializeField]
        private ImpactParticlesBase particlePrefab;
        [Tooltip("The template particle prefab.")]
        [SerializeField]
        private List<ImpactParticlesBase> particlePrefabs;
        [Tooltip("Whether the particles are one-shot or looping.")]
        [SerializeField]
        private ParticleEffectType particleEffectType;
        [Tooltip("How particle prefabs are chosen from the prefabs list.")]
        [SerializeField]
        private CollisionSelectionMode particleSelectionMode = CollisionSelectionMode.Random;

        [Tooltip("The minimum velocity required to emit particles.")]
        [SerializeField]
        private float minimumVelocity = 0;
        [Tooltip("The maximum velocity used for calculating collision intensity. Is not used for Random selection mode.")]
        [SerializeField]
        private float maximumVelocity = 10;
        [Tooltip("How much the collision normal should affect the collision intensity.")]
        [SerializeField]
        private float collisionNormalInfluence = 1;

        [Tooltip("Should particles be emitted when sliding? Only available for Looping effects.")]
        [SerializeField]
        private bool emitOnSlide;
        [Tooltip("Should particles be emitted when rolling? Only available for Looping effects.")]
        [SerializeField]
        private bool emitOnRoll;

        public override IEnumerable<PooledEffectObjectBase> Prefabs
        {
            get
            {
                //Looped particle effects still use particlePrefab
                //Also handles old assets that use particlePrefab
                if (particleEffectType == ParticleEffectType.Looped || particlePrefabs == null || particlePrefabs.Count == 0)
                {
                    return new ImpactParticlesBase[] { particlePrefab };
                }
                else
                {
                    return particlePrefabs;
                }
            }
        }

        public ParticleEffect GetParticleEffect()
        {
            return new ParticleEffect()
            {
                VelocityReferenceRange = new Range(minimumVelocity, maximumVelocity),
                CollisionNormalInfluence = collisionNormalInfluence,
                ParticleSelectionMode = particleSelectionMode,
                ParticleEffectType = particleEffectType,
                EmitOnSlide = particleEffectType == ParticleEffectType.Looped && this.emitOnSlide,
                EmitOnRoll = particleEffectType == ParticleEffectType.Looped && this.emitOnRoll
            };
        }
    }
}