using System.Collections.Generic;
using System.Linq;

namespace ImpactCFX
{
    /// <summary>
    /// Base class for effect assets for effects that use object pooling.
    /// </summary>
    public abstract class ImpactMultiPrefabEffectAuthoringBase : ImpactEffectAuthoringBase
    {
        /// <summary>
        /// List of prefabs to use for this effect.
        /// </summary>
        public abstract IEnumerable<PooledEffectObjectBase> Prefabs { get; }

        public override bool Validate()
        {
            IEnumerable<PooledEffectObjectBase> p = Prefabs;
            if (p == null || p.Count() == 0)
                return false;

            //Ensure prefabs list contains at least 1 non-null prefab
            bool hasAnyValidPrefab = false;
            foreach (PooledEffectObjectBase p2 in p)
            {
                if (p2 != null && !p2.Equals(null))
                {
                    hasAnyValidPrefab = true;
                    break;
                }
            }

            return hasAnyValidPrefab;
        }
    }
}