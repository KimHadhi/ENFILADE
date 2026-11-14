using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A round a calm zombie walks on a campaign map (the map gimmick card "아파트를 암살 놀이터로": a corridor's walker): its
/// children are the stops, walked in order and round again (ZombieMind.Patrol). The host sets count walkers on it as the
/// stretch its step belongs to begins quiet, each from its own stop away from the players (CampaignHorde.PlaceWalkers).
/// A round switched off this run by its GimmickVariant group has nobody on it.
/// </summary>
[DisallowMultipleComponent]
public sealed class PatrolRoute : MonoBehaviour
{
    private static readonly List<PatrolRoute> all = new List<PatrolRoute>();

    [SerializeField, Tooltip("The campaign step whose stretch it belongs to (CampaignSteps id).")]
    private string step = "";
    [SerializeField, Min(1), Tooltip("How many walk it, spread round its stops.")]
    private int count = 1;

    internal string Step => step;
    internal int Count => count;

    /// <summary>Its stops in order (its children's places).</summary>
    internal Vector3[] Stops
    {
        get
        {
            var stops = new Vector3[transform.childCount];
            for (int i = 0; i < stops.Length; i++) stops[i] = transform.GetChild(i).position;
            return stops;
        }
    }

    /// <summary>Every round in the open scenes that is switched on this run.</summary>
    internal static IReadOnlyList<PatrolRoute> All => all;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Clear() => all.Clear();

    private void OnEnable() => all.Add(this);

    private void OnDisable() => all.Remove(this);
}
