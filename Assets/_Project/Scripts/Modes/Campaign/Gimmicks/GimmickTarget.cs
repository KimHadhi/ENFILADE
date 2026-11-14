using System.Collections.Generic;
using UnityEngine;

/// <summary>A map gimmick that something on it can be shot (GimmickTarget hands it the hit).</summary>
public interface IGimmickShot
{
    /// <summary>This PC's own player hit it.</summary>
    void Shot(PlayerMovement by, in DamageHit hit);
}

/// <summary>
/// Something thin on a map gimmick that bullets and blows stop at: a crane's cable (GimmickDrop). A trigger box, so it
/// blocks nobody's way, that AttackTrace lets through to as it does a fuel can's (CampaignCanTarget); the hit goes to the
/// gimmick above it, on the shooter's PC only (the actor decides).
/// </summary>
[DisallowMultipleComponent]
public sealed class GimmickTarget : MonoBehaviour, IDamageReceiver
{
    private static readonly HashSet<Collider> boxes = new HashSet<Collider>();

    /// <summary>A gimmick's target box: a trigger that still takes bullets (AttackTrace).</summary>
    public static bool Is(Collider collider) => collider != null && boxes.Contains(collider);

    private BoxCollider box;
    private IGimmickShot owner;

    private void Awake()
    {
        box = GetComponent<BoxCollider>();
        if (box == null) box = gameObject.AddComponent<BoxCollider>();
        box.isTrigger = true;
        boxes.Add(box);
        owner = GetComponentInParent<IGimmickShot>();
    }

    private void OnDestroy() => boxes.Remove(box);

    public DamageResult ReceiveHit(DamageHit hit)
    {
        var by = hit.Source != null ? hit.Source.GetComponentInParent<PlayerMovement>() : null;
        if (hit.Kind == DamageKind.Attack && by != null && by.IsLocal && owner != null) owner.Shot(by, hit);
        return DamageResult.Ignored;
    }
}
