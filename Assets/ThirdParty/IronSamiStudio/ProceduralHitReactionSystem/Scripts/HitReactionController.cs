using UnityEngine;
using System;
using System.Collections.Generic;

namespace IronSamiStudio.ProceduralHitReactions
{
    /// <summary>
    /// Per-character controller that routes a hit to the right BodyPartReactor.
    /// Attach one of these to every character prefab that needs hit reactions.
    /// </summary>
    public class HitReactionController : MonoBehaviour
    {
        [Header("Animator Settings")]
        public Animator animator;          // single Animator for this character
        public int      reactionLayer = 1; // weight driven by BodyPartReactor

        [Header("Body Part Reactors")]
        public List<BodyPartReactor> reactors;

        void Awake()
        {
            // grab the local Animator if the field wasn’t set in the Inspector
            if (animator == null)
                animator = GetComponent<Animator>();
        }

        /// <param name="partName">exact Transform.name of the bone you hit</param>
        /// <param name="hitDirection">world-space incoming velocity</param>
        /// <param name="hitPoint">world-space contact point</param>
        public void React(string partName, Vector3 hitDirection, Vector3 hitPoint)
        {
            foreach (var r in reactors)
            {
                if (r.name.Equals(partName, StringComparison.OrdinalIgnoreCase))
                {
                    r.React(hitDirection, hitPoint);
                    return;
                }
            }
        }
    }
}
