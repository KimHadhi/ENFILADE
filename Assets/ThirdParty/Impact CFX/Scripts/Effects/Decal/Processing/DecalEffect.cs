using Unity.Mathematics;

namespace ImpactCFX.Decals
{
    public struct DecalEffect : IMultiPrefabEffectData<DecalEffectResult>
    {
        public int EffectID { get; set; }
        public ArrayChunk PrefabArrayChunk { get; set; }

        public ImpactTagMaskFilter IncludeTags { get; set; }
        public ImpactTagMaskFilter ExcludeTags { get; set; }

        public Range VelocityReferenceRange;
        public float CollisionNormalInfluence;
        public CollisionSelectionMode DecalSelectionMode;

        public bool CreateOnCollision;
        public bool CreateOnSlide;
        public bool CreateOnRoll;

        public Range CreationInterval;
        public EffectIntervalType CreationIntervalType;

        public DecalEffectResult GetResult(CollisionInputData collisionData, MaterialCompositionData materialCompositionData, ImpactVelocityData velocityData, ref Random random)
        {
            DecalEffectResult result = new DecalEffectResult();
            if (PrefabArrayChunk.Length == 0)
            {
#if IMPACTCFX_DEBUG
                ImpactCFXLogger.LogEffectInvalid(this, EffectID, $"Decal effect has no prefabs!");
#endif          
                return result;
            }

            float intensity = EffectUtility.GetCollisionIntensity(velocityData.ImpactVelocity, collisionData.Normal, CollisionNormalInfluence, collisionData.CollisionType) * materialCompositionData.Composition;

            if (intensity < VelocityReferenceRange.Min)
            {
#if IMPACTCFX_DEBUG
                ImpactCFXLogger.LogEffectInvalid(this, EffectID, $"Intensity ({intensity}) is less than Minimum Velocity ({VelocityReferenceRange.Min})");
#endif          
                return result;
            }

            if (shouldPlace(collisionData.CollisionType))
            {
                float normalizedIntensity = VelocityReferenceRange.Normalize(intensity);

                result.PrefabIndex = EffectUtility.GetArrayIndexForCollision(PrefabArrayChunk, DecalSelectionMode, normalizedIntensity, ref random);
                result.Priority = collisionData.Priority;
                result.ContactPointID = ContactPointIDGenerator.CalculateContactPointID(collisionData.TriggerObjectID, collisionData.HitObjectID, collisionData.CollisionType, materialCompositionData.MaterialData.MaterialTags.Value, EffectID);
                result.CheckContactPointID = collisionData.CollisionType.RequiresContactPointID();

                result.CreationInterval = CreationInterval;
                result.CreationIntervalType = CreationIntervalType;

                result.IsEffectValid = result.IsObjectPoolRequestValid = true;
            }
            else
            {
#if IMPACTCFX_DEBUG
                ImpactCFXLogger.LogEffectInvalid(this, EffectID, $"Decals not placed for {collisionData.CollisionType}");
#endif    
            }

            return result;
        }

        private bool shouldPlace(CollisionType collisionType)
        {
            return (collisionType == CollisionType.Collision && CreateOnCollision) ||
                (collisionType == CollisionType.Slide && CreateOnSlide) ||
                (collisionType == CollisionType.Roll && CreateOnRoll);
        }
    }
}