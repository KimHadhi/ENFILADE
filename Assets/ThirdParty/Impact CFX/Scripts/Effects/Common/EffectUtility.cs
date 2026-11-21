using Unity.Mathematics;

namespace ImpactCFX
{
    /// <summary>
    /// Utilities for effects.
    /// </summary>
    public static class EffectUtility
    {
        /// <summary>
        /// Gets an intensity value for the collision, taking into account the collision normal.
        /// </summary>
        /// <param name="velocity">The collision velocity.</param>
        /// <param name="normal">The collision normal.</param>
        /// <param name="collisionNormalInfluence">How much influence the collision normal has on the resulting intensity.</param>
        /// <param name="collisionType">The type of collision.</param>
        public static float GetCollisionIntensity(float3 velocity, float3 normal, float collisionNormalInfluence, CollisionType collisionType)
        {
            float dotProduct;
            float velocityMagnitude = math.length(velocity);

            if (math.lengthsq(normal) == 0)
                dotProduct = 1;
            else
            {
                float3 normalizedVelocity = velocityMagnitude == 0 ? float3.zero : velocity / velocityMagnitude;

                if (collisionType == CollisionType.Collision)
                    dotProduct = math.abs(math.dot(normalizedVelocity, normal));
                else
                    dotProduct = 1 - math.abs(math.dot(normalizedVelocity, normal));
            }

            float intensity = (dotProduct + (1 - dotProduct) * (1 - collisionNormalInfluence)) * velocityMagnitude;

            return intensity;
        }

        /// <summary>
        /// Gets the appropriate array index for a collision where an array of effects is used.
        /// </summary>
        /// <param name="arrayChunk">The array chunk corresponding to the effect items.</param>
        /// <param name="collisionSelectionMode">How an item from the array should be selected.</param>
        /// <param name="normalizedValue">Normalized value for getting an item from the array.</param>
        /// <param name="random">Random number generator.</param>
        /// <returns>An index based on the given array chunk and other parameters.</returns>
        public static int GetArrayIndexForCollision(ArrayChunk arrayChunk, CollisionSelectionMode collisionSelectionMode, float normalizedValue, ref Random random)
        {
            if (arrayChunk.Length == 0)
                return -1;

            if (collisionSelectionMode == CollisionSelectionMode.Velocity)
            {
                int relativeIndex = (int)(math.clamp(normalizedValue, 0, 1) * (arrayChunk.Length - 1));
                return arrayChunk.Offset + relativeIndex;
            }
            else
            {
                return arrayChunk.Offset + random.NextInt(0, arrayChunk.Length);
            }
        }
    }
}
