using UnityEngine;

// CampaignRun: what "someone got there" means for a Reach step. One part of the class; the rest is in CampaignRun.cs
// and the files beside it.
public sealed partial class CampaignRun
{
    /// <summary>How high over a spot and over a player's feet the line between them is looked along (m): a standing
    /// chest, over tables, sofas and railings and under door heads. The builder checks every Reach spot at this height.</summary>
    public const float ReachChest = 1.2f;
    // A spot whose own point lies inside something this big round it is buried (m).
    private const float ReachBuried = .05f;
    private const string EnemyLayer = "Enemy";
    private static readonly RaycastHit[] reachHits = new RaycastHit[24];

    /// <summary>
    /// Someone has got to a Reach step's place: they are within its radius of the spot AND nothing of the building
    /// stands between them and it. A place is where one has walked to, not a ball through walls, doors and floors.
    /// It was the ball alone (the user, 2026-11-18: "마스터키 힌트 불일치"): chapter 1's "1층 도착" rang in the stairwell
    /// behind the ground floor's chained door, 2.5 m from the lobby's spot through the wall, and the next objective then
    /// sent the party to a cellar they were still locked away from; "지하 도착" rang on the floor over the cellar.
    /// One rule for every Reach step of every map, a mod's too: no spot is moved or resized to make a step come out.
    /// What stands between: the map's own solids (walls, slabs, a shut door or gate, a chained door until it is opened
    /// or broken). Not people, zombies, corpses or loose props (anything with a body of its own), and not triggers.
    /// A spot that is itself inside something (a layout mistake, or a blocker of this run standing on it) can be seen
    /// from nowhere, so for it the ball alone decides, as before: a step can always be finished. The build names such
    /// spots (GreyboxBuilder, Logs/MapAudit).
    /// </summary>
    private bool Arrived(CampaignStep at)
    {
        var spot = SpotOf(at.Spot);
        if (spot == null) return true;
        foreach (var someone in players)
            if (Within(someone, at) && InTheOpen(someone, spot.position)) return true;
        return false;
    }

    private static bool InTheOpen(PlayerMovement someone, Vector3 spot)
    {
        Vector3 from = someone.transform.position + Vector3.up * ReachChest, to = spot + Vector3.up * ReachChest;
        // What stops a walking player, less the zombies: the building.
        int building = someone.groundLayers & ~LayerMask.GetMask(EnemyLayer);
        Vector3 way = to - from;
        float reach = way.magnitude;
        if (reach < .01f) return true;
        if (Buried(to, building)) return true;
        int count = Physics.RaycastNonAlloc(from, way / reach, reachHits, reach, building, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (IsBuilding(reachHits[i].collider)) return false;
        return true;
    }

    private static bool Buried(Vector3 point, int building)
    {
        int count = Physics.OverlapSphereNonAlloc(point, ReachBuried, reachOverlaps, building, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (IsBuilding(reachOverlaps[i])) return true;
        return false;
    }

    private static readonly Collider[] reachOverlaps = new Collider[8];

    // The map's own solids: no body of its own (a player's parts, a ragdoll, a loose prop) and nobody's character.
    private static bool IsBuilding(Collider collider)
        => collider != null && collider.attachedRigidbody == null && collider.GetComponentInParent<PlayerMovement>() == null;
}
