using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Where a campaign map sets a crowd in one place. Only its caged kind holds anyone: count zombies shut in behind bars
/// round it, within radius, standing (ZombieMind.Cage; after Guts &amp; Blackpowder's cells under Kaub). The host sets them
/// as the stretch its step belongs to begins quiet, along with the calm ones on the way (CampaignRun.PlaceMapCrowds,
/// CampaignHorde.PlaceCaged), and again after a wipe. A nest switched off this run by its GimmickVariant group stays empty.
/// One that isn't caged was a crowd laid face down on the floor (the map gimmick card "잠든 무리": asleep until
/// 2026-11-15, lying awake after). It holds nobody since 2026-11-18, when the user had every such crowd removed ("[버그]
/// 비활성(자는) 좀비 잔존 문제: 제거 지시된 자는 좀비 오브젝트 완전 삭제"; asked how far, "엎드린 무리까지"). The builders'
/// calls that still make those empty spots, and this component's name and its caged switch, go when the maps are next
/// rebuilt (Docs/Features/MapGimmicks.md, '엎드린 무리').
/// </summary>
[DisallowMultipleComponent]
public sealed class SleeperNest : MonoBehaviour
{
    private static readonly List<SleeperNest> all = new List<SleeperNest>();

    [SerializeField, Tooltip("The campaign step whose stretch it belongs to (CampaignSteps id): laid as that stretch begins.")]
    private string step = "";
    [SerializeField, Min(1), Tooltip("How many stand here.")]
    private int count = 3;
    [SerializeField, Min(0f), Tooltip("How far round its middle they stand (m).")]
    private float radius = 2f;
    [SerializeField, Tooltip("Shut in behind bars, standing (a cage's crowd: ZombieMind.Cage). Off: the spot of a crowd that lay on the floor, which holds nobody now.")]
    private bool caged;

    internal string Step => step;
    internal int Count => count;
    internal float Radius => radius;
    internal bool Caged => caged;

    /// <summary>Every nest in the open scenes that is switched on this run.</summary>
    internal static IReadOnlyList<SleeperNest> All => all;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Clear() => all.Clear();

    private void OnEnable() => all.Add(this);

    private void OnDisable() => all.Remove(this);
}
