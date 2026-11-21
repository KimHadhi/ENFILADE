using System.Collections.Generic;
using UnityEngine;

namespace ImpactCFX.Decals
{
    [CreateAssetMenu(fileName = "New Impact Decal Effect", menuName = "Impact CFX/Decal Effect", order = 2)]
    public class ImpactDecalEffectAuthoring : ImpactMultiPrefabEffectAuthoringBase
    {
        [Tooltip("The decal prefab to place.")]
        [SerializeField]
        private ImpactDecalBase decalPrefab;
        [Tooltip("A list of decal prefabs to select from.")]
        [SerializeField]
        private List<ImpactDecalBase> decalPrefabs;
        [Tooltip("How decal prefabs are chosen from the prefabs list.")]
        [SerializeField]
        private CollisionSelectionMode decalSelectionMode = CollisionSelectionMode.Random;

        [Tooltip("The minimum velocity required to place a decal.")]
        [SerializeField]
        private float minimumVelocity = 0;
        [Tooltip("The maximum velocity used for calculating collision intensity. Is not used for Random selection mode.")]
        [SerializeField]
        private float maximumVelocity = 10;
        [Tooltip("How much the collision normal should affect the collision intensity.")]
        [SerializeField]
        private float collisionNormalInfluence = 1;

        [Tooltip("Should decals be placed on collision?")]
        [SerializeField]
        private bool createOnCollision = true;
        [Tooltip("Should decals be placed when sliding?")]
        [SerializeField]
        private bool createOnSlide;
        [Tooltip("Should decals be placed when rolling?")]
        [SerializeField]
        private bool createOnRoll;

        [Tooltip("For sliding and rolling, how often a decal should be placed.")]
        [SerializeField]
        private Range creationInterval = new Range(0.5f, 0.5f);
        [Tooltip("For sliding and rolling, the type of interval to use for placement.")]
        [SerializeField]
        private EffectIntervalType creationIntervalType = EffectIntervalType.Distance;

        public override IEnumerable<PooledEffectObjectBase> Prefabs
        {
            get
            {
                //Handling of old assets that use decalPrefab.
                if (decalPrefabs == null || decalPrefabs.Count == 0)
                {
                    return new ImpactDecalBase[] { decalPrefab };
                }
                else
                {
                    return decalPrefabs;
                }
            }
        }

        public DecalEffect GetDecalEffect()
        {
            return new DecalEffect()
            {
                VelocityReferenceRange = new Range(minimumVelocity, maximumVelocity),
                CollisionNormalInfluence = collisionNormalInfluence,
                DecalSelectionMode = decalSelectionMode,
                CreateOnCollision = createOnCollision,
                CreateOnSlide = createOnSlide,
                CreateOnRoll = createOnRoll,
                CreationInterval = creationInterval,
                CreationIntervalType = creationIntervalType
            };
        }
    }
}

