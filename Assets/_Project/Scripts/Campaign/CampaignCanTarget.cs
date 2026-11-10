using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Friend feedback C17: a fuel can (CampaignCans) can be shot. A trigger box on the can, which bullets stop at
/// (AttackTrace lets this trigger through, as it does zombies' bone hitboxes), hands the hit to the run: the shooter's
/// PC tells the host, which sets the can alight (CampaignRun.ShootCan). It never blocks anyone's way.
/// </summary>
[DisallowMultipleComponent]
public sealed class CampaignCanTarget : MonoBehaviour, IDamageReceiver
{
    private static readonly HashSet<Collider> boxes = new HashSet<Collider>();

    /// <summary>A can's box: a trigger that still takes bullets (AttackTrace).</summary>
    public static bool Is(Collider collider) => collider != null && boxes.Contains(collider);

    private BoxCollider box;

    /// <summary>Which can of the run this is.</summary>
    internal int Index { get; set; }

    /// <summary>Shootable while it lies or flies; not carried, burning or gone.</summary>
    internal bool Shootable
    {
        set { if (box != null && box.enabled != value) box.enabled = value; }
    }

    private void Awake()
    {
        box = gameObject.GetComponent<BoxCollider>();
        if (box == null) box = gameObject.AddComponent<BoxCollider>();
        box.isTrigger = true;
        boxes.Add(box);
    }

    private void OnDestroy() => boxes.Remove(box);

    public DamageResult ReceiveHit(DamageHit hit)
    {
        // A player's bullet or blow (enemies don't shoot); its own PC is the only one that hurts with it.
        if (hit.Kind != DamageKind.Attack || hit.Source == null || hit.Source.GetComponentInParent<PlayerMovement>() == null)
            return DamageResult.Ignored;
        CampaignRun.ShootCan(Index, hit.Source.GetComponentInParent<PlayerMovement>());
        return DamageResult.Ignored;
    }
}
