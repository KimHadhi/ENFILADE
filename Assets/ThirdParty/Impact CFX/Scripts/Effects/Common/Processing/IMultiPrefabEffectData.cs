using ImpactCFX.Pooling;

namespace ImpactCFX
{
    /// <summary>
    /// Interface for impact effect data that supports multiple pooled prefabs.
    /// </summary>
    /// <typeparam name="TResult">The type of result struct that this effect returns.</typeparam>
    public interface IMultiPrefabEffectData<TResult> : IEffectData<TResult> where TResult : struct, IMultiPrefabEffectResult, IObjectPoolRequest
    {
        /// <summary>
        /// Section of the prefab array that stores prefab IDs for this effect.
        /// </summary>
        ArrayChunk PrefabArrayChunk { get; set; }
    }
}