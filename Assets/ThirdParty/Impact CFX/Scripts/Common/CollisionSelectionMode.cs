using UnityEngine;

namespace ImpactCFX
{
    /// <summary>
    /// Enum for configuring how items from an array are chosen for effects.
    /// </summary>
    public enum CollisionSelectionMode
    {
        /// <summary>
        /// Items will be selected based on the collision velocity, with the first element being the lowest velocity and the last element being the highest velocity.
        /// </summary>
        [Tooltip("Items will be selected based on the collision velocity, with the first element being the lowest velocity and the last element being the highest velocity.")]
        Velocity = 0,

        /// <summary>
        /// Items will be selected at random.
        /// </summary>
        [Tooltip("Items will be selected at random.")]
        Random = 1,
    }
}
