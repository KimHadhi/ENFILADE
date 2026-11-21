using System;
using UnityEngine;

namespace ImpactCFX
{
    /// <summary>
    /// Defines how triggers handle assignment of the "Trigger Object" and the "Hit Object".
    /// </summary>
    [Flags]
    public enum TriggerBehavior
    {
        [Tooltip("The default behavior, where the object with the trigger component will play effects.")]
        /// <summary>
        /// The default behavior, where the object with the trigger component will play effects.
        /// </summary>
        Default = 1 << 0,
        [Tooltip("Inverted behavior, where the object that was hit will play effects.")]
        /// <summary>
        /// Inverted behavior, where the object that was hit will play effects.
        /// </summary>
        Inverted = 1 << 1,
    }
}
