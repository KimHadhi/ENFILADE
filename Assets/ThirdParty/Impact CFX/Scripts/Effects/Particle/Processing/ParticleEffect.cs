using Unity.Mathematics;

namespace ImpactCFX.Particles
{
    public struct ParticleEffect : IMultiPrefabEffectData<ParticleEffectResult>
    {
        public int EffectID { get; set; }
        public ArrayChunk PrefabArrayChunk { get; set; }

        public ImpactTagMaskFilter IncludeTags { get; set; }
        public ImpactTagMaskFilter ExcludeTags { get; set; }

        public Range VelocityReferenceRange;
        public float CollisionNormalInfluence;
        public CollisionSelectionMode ParticleSelectionMode;

        public ParticleEffectType ParticleEffectType;

        public bool EmitOnSlide;
        public bool EmitOnRoll;

        public ParticleEffectResult GetResult(CollisionInputData collisionData, MaterialCompositionData materialCompositionData, ImpactVelocityData velocityData, ref Random random)
        {
            ParticleEffectResult result = new ParticleEffectResult();
            if (PrefabArrayChunk.Length == 0)
            {
#if IMPACTCFX_DEBUG
                ImpactCFXLogger.LogEffectInvalid(this, EffectID, $"Particle effect has no prefabs!");
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

            if (shouldEmit(collisionData.CollisionType))
            {
                if (ParticleEffectType == ParticleEffectType.Looped)
                {
                    result.PrefabIndex = PrefabArrayChunk.Offset;
                }
                else
                {
                    float normalizedIntensity = VelocityReferenceRange.Normalize(intensity);
                    result.PrefabIndex = EffectUtility.GetArrayIndexForCollision(PrefabArrayChunk, ParticleSelectionMode, normalizedIntensity, ref random);
                }

                result.Priority = collisionData.Priority;
                result.ContactPointID = ContactPointIDGenerator.CalculateContactPointID(collisionData.TriggerObjectID, collisionData.HitObjectID, collisionData.CollisionType, materialCompositionData.MaterialData.MaterialTags.Value, EffectID);
                result.CheckContactPointID = collisionData.CollisionType.RequiresContactPointID();

                result.IsEffectValid = result.IsObjectPoolRequestValid = true;
            }
            else
            {
#if IMPACTCFX_DEBUG
                ImpactCFXLogger.LogEffectInvalid(this, EffectID, $"Particles not emitted for {collisionData.CollisionType}");
#endif    
            }
            return result;
        }

        private bool shouldEmit(CollisionType collisionType)
        {
            return (collisionType == CollisionType.Collision && ParticleEffectType == ParticleEffectType.OneShot) ||
                (collisionType == CollisionType.Slide && ParticleEffectType == ParticleEffectType.Looped && EmitOnSlide) ||
                (collisionType == CollisionType.Roll && ParticleEffectType == ParticleEffectType.Looped && EmitOnRoll);
        }
    }
}