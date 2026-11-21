namespace ImpactCFX
{
    /// <summary>
    /// Interface for results of effects that support multiple pooled prefabs.
    /// </summary>
    public interface IMultiPrefabEffectResult : IEffectResult
    {
        /// <summary>
        /// The index of the prefab to use for the final effect.
        /// </summary>
        int PrefabIndex { get; set; }
    }
}