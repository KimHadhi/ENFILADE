using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Where a crowd lies asleep on a campaign map (the map gimmick card "잠든 무리", after GTFO's sleepers, Guts &amp;
/// Blackpowder's cells under Kaub and PAYDAY 2's Murky Station yard at night): count zombies on the floor round it,
/// within radius, asleep (ZombieMind.Sleep: eyes shut, a flashlight on a face or a sprint, a shot or a door smashed in
/// near enough wakes them, crouching past doesn't, and any side will do to take one). The host lays them as the stretch
/// its step belongs to begins quiet, along with the calm ones on the way (CampaignRun, CampaignHorde.PlaceSleepers), and
/// again after a wipe. A nest switched off this run by its GimmickVariant group lies empty.
/// </summary>
[DisallowMultipleComponent]
public sealed class SleeperNest : MonoBehaviour
{
    private static readonly List<SleeperNest> all = new List<SleeperNest>();

    [SerializeField, Tooltip("The campaign step whose stretch it belongs to (CampaignSteps id): laid as that stretch begins.")]
    private string step = "";
    [SerializeField, Min(1), Tooltip("How many lie here.")]
    private int count = 3;
    [SerializeField, Min(0f), Tooltip("How far round its middle they lie (m).")]
    private float radius = 2f;

    internal string Step => step;
    internal int Count => count;
    internal float Radius => radius;

    /// <summary>Every nest in the open scenes that is switched on this run.</summary>
    internal static IReadOnlyList<SleeperNest> All => all;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Clear() => all.Clear();

    private void OnEnable() => all.Add(this);

    private void OnDisable() => all.Remove(this);
}
