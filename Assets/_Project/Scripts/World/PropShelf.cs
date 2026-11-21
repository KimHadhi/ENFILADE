using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The physics objects the game makes at run time, by name: the fuel can every Collect step uses (CampaignCans: Impact
/// CFX's red jerry can, the user's pick of 2026-11-21: "3챕에 기름통도 저 impact cfx에 있는 기름통으로") and every item of
/// Impact CFX's demo, to try props out anywhere with /prop (the user: "허브에 테스팅용으로 넣어 줘. 아님 콘솔로 생성할 수
/// 있게"). Each is the pack's own prefab, with its Rigidbody, colliders and Impact CFX components as the pack made them.
/// One asset: Resources/World/PropShelf.
/// </summary>
[CreateAssetMenu(menuName = "ENFILADE/Prop Shelf")]
public sealed class PropShelf : ScriptableObject
{
    public const string ResourcePath = "World/PropShelf";

    [SerializeField, Tooltip("The fuel can a Collect step's cans are made as (CampaignCans).")]
    private GameObject fuelCan;
    [SerializeField, Tooltip("What /prop can make, in the order it lists them.")]
    private List<GameObject> items = new List<GameObject>();

    private static PropShelf loaded;
    private static bool tried;

    /// <summary>The shelf, or null when the asset is missing (warned once).</summary>
    public static PropShelf Get()
    {
        if (loaded != null || tried) return loaded;
        tried = true;
        loaded = Resources.Load<PropShelf>(ResourcePath);
        if (loaded == null) Debug.LogWarning("[PropShelf] Resources/" + ResourcePath + " is missing: fuel cans stay plain boxes and /prop has nothing to make.");
        return loaded;
    }

    public GameObject FuelCan => fuelCan;
    public IReadOnlyList<GameObject> Items => items;

    /// <summary>An item by its name (any case, a part of it is enough when only one matches) or its number in the list (from 1).</summary>
    public GameObject Find(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;
        name = name.Trim();
        if (int.TryParse(name, out int number)) return number >= 1 && number <= items.Count ? items[number - 1] : null;
        GameObject part = null;
        int parts = 0;
        foreach (var item in items)
        {
            if (item == null) continue;
            if (string.Equals(item.name, name, StringComparison.OrdinalIgnoreCase)) return item;
            if (item.name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0) { part = item; parts++; }
        }
        return parts == 1 ? part : null;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() { loaded = null; tried = false; }
}
