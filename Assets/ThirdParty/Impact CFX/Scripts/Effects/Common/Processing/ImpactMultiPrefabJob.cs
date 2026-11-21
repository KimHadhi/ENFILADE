using ImpactCFX.Pooling;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace ImpactCFX
{
    [BurstCompile]
    public struct ImpactMultiPrefabJob<TResult> : IJobFor where TResult : unmanaged, IMultiPrefabEffectResult, IObjectPoolRequest
    {
        [ReadOnly]
        public NativeList<int> PrefabIDs;

        /// <summary>
        /// Array of results.
        /// </summary>
        public NativeArray<TResult> Results;

        public void Execute(int index)
        {
            TResult result = Results[index];

            if (result.IsEffectValid)
            {
                int prefabID = PrefabIDs[result.PrefabIndex];
                result.TemplateID = prefabID;

                Results[index] = result;
            }
        }
    }
}