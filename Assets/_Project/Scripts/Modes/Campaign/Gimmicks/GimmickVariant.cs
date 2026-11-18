using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What changes from run to run on a campaign map (the map gimmick card "판마다 다른 길", after PAYDAY 2's Murky Station
/// and Hotline Miami day 2, whose designers change only what changes how it plays): of this group's children, keep picks
/// of them and switch the rest off, by the run's seed (MapGimmicks.Seed), so every PC lays the map out alike. The builder
/// fills a group with what may be there: boards across a flat's door, the plug in a hole through a wall, the rubble on
/// one flight of stairs or another. A child that blocks a way carves the walk mesh while it stands (the bake leaves
/// it out), so zombies go round it and through the gap once it is gone. Until the seed is known (a guest, a moment)
/// everything stands as built.
/// </summary>
[DisallowMultipleComponent]
public sealed class GimmickVariant : MonoBehaviour
{
    private static readonly List<GimmickVariant> all = new List<GimmickVariant>();
    private static readonly List<int> order = new List<int>();

    [SerializeField, Min(0), Tooltip("How many of its children are kept each run; the rest are switched off.")]
    private int keep = 1;
    [SerializeField, Tooltip("Which roll of the run's seed it takes: two groups with the same salt keep the same children.")]
    private int salt;

    private ushort laidOut;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Clear() => all.Clear();

    private void Awake() => all.Add(this);

    private void OnDestroy() => all.Remove(this);

    /// <summary>Every group in the open scenes lays itself out by the seed (MapGimmicks, once it is known).</summary>
    internal static void ApplyAll(ushort seed)
    {
        foreach (var group in all) if (group != null) group.Apply(seed);
    }

    private void Apply(ushort seed)
    {
        if (seed == 0 || seed == laidOut) return;
        laidOut = seed;
        int count = transform.childCount;
        order.Clear();
        for (int i = 0; i < count; i++) order.Add(i);
        // A shuffle by the seed: the first `keep` stay.
        for (int i = count - 1; i > 0; i--)
        {
            int j = Mathf.Min(i, Mathf.FloorToInt(MapGimmicks.Mix(seed, (uint)(salt * 131 + i)) * (i + 1)));
            (order[i], order[j]) = (order[j], order[i]);
        }
        for (int i = 0; i < count; i++) transform.GetChild(order[i]).gameObject.SetActive(i < keep);
        order.Clear();
    }
}
