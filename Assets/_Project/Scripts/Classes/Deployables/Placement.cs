using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

// dev-text: file (the record's lines are for whoever reads Logs/placement.txt; nothing here is shown in the game)

/// <summary>
/// Something a player set down that stays where it was put: a Support's planks, ammo crate or searchlight (Barricade),
/// its claymore (ClaymoreMine), and whatever a later class or a mod sets down. To the placement rule every one of them
/// is a structure: it takes room, and nothing else stands on it (it can come down under what stood there). And every
/// one of them is counted in one place (Deployables): how many its owner has standing, how many they may keep, and
/// which comes down when another goes up.
/// </summary>
public interface IPlaced
{
    /// <summary>Who set it down.</summary>
    PlayerMovement Owner { get; }
    /// <summary>Its kind: a row of Tables/Deployables (its name, who holds it, how many one player keeps).</summary>
    string KindId { get; }
    /// <summary>Still standing: not broken, not gone off, not already being taken back.</summary>
    bool Stands { get; }
    /// <summary>Taken back for a newer one of its owner's, past their most: it comes down and harms nobody.</summary>
    void Retire();
}

/// <summary>
/// The one rule of what stands in the way of something a player sets down, and of what it may stand on. Every
/// deployable asks here (a Support's structures through Barricade.Room and SupportKit.Spot, its claymore through
/// PlayerGrenades.Spot), and whatever comes later or from a mod asks the same two questions (Ray, Box), so no deployable
/// has a rule of its own to get wrong.
/// <para>
/// The user, 2026-11-18: "시체 콜라이더로 인해 서포트 구조물 … 설치가 막히는 판정 수정". Until then Barricade.Room tested
/// every layer and let through only what it named (a zombie, a player, a barricade), so everything it did not name was
/// a wall or a floor: a zombie's corpse lying on the spot, a severed arm, a dropped magazine, a stone, a physics crate,
/// even the planks still flying off the Support's own last barricade. The claymore had a second rule (a layer mask),
/// which let it be stuck on a barricade or a rolling crate and left hanging in the air when that went.
/// </para>
/// <para>
/// The rule says what a collider IS to something being set down, by what the thing is, not by where it was filed:
/// <list type="bullet">
/// <item>World: the built map, what stays where it is. The only thing a deployable stands on.</item>
/// <item>Body: a living player or zombie. It takes room (nothing goes up inside a friend) and is no ground.</item>
/// <item>Structure: another deployable (IPlaced). It takes room and is no ground.</item>
/// <item>Loose: what lies about and gives way. The dead (a doll, RagdollCorpse, whether a zombie's or a player's, and
/// what a dead one left standing), gore, what was thrown or dropped, anything physics carries (it is pushed aside by
/// what goes up, and would carry off what stood on it), anything walked through (a trigger: barbed wire, a zone), and
/// whatever else lies on the layer the players pass (WorldLayers.Passed). It never takes room and is never ground.</item>
/// </list>
/// The builder's own body is never in its own way.
/// </para>
/// Checked without playing: every placement let go, refused or not, leaves a line in Logs/placement.txt (PlayRecord)
/// with the collider that stood in the way and what the rule took it for, and with what it passed there.
/// </summary>
public static class Placement
{
    /// <summary>What a collider is to something being set down.</summary>
    [Flags]
    public enum Thing : byte
    {
        None = 0,
        World = 1,
        Body = 2,
        Structure = 4,
        Loose = 8,
    }

    /// <summary>What takes room and can be met at all: everything but what gives way.</summary>
    public const Thing Solid = Thing.World | Thing.Body | Thing.Structure;
    /// <summary>What a view or a line of sight ends on: the built world and what players built in it. Bodies are looked past.</summary>
    public const Thing Built = Thing.World | Thing.Structure;

    public const string RecordName = "placement";
    private const string RecordHeading = "what each placement let go met (Placement): REFUSED/OK kind fit at spot | by = the collider in the way "
        + "[what the rule took it for: why, layer] | passed = what lay there and gave way";

    private static readonly RaycastHit[] rays = new RaycastHit[64];
    private static readonly Collider[] found = new Collider[64];

    /// <summary>What this collider is to something being set down.</summary>
    public static Thing Of(Collider collider) => Of(collider, out _);

    /// <summary>What this collider is to something being set down, and in a word why (the record's).</summary>
    public static Thing Of(Collider collider, out string why)
    {
        why = "";
        if (collider == null) return Thing.None;
        if (collider.isTrigger) { why = "trigger"; return Thing.Loose; }
        // What it is comes first: a knocked-down zombie is carried by physics too, and it is alive.
        var enemy = collider.GetComponentInParent<EnemyHealth>();
        if (enemy != null)
        {
            why = enemy.IsAlive ? "zombie" : "dead zombie";
            return enemy.IsAlive ? Thing.Body : Thing.Loose;
        }
        var player = collider.GetComponentInParent<PlayerMovement>();
        if (player != null)
        {
            why = player.IsDead ? "dead player" : "player";
            return player.IsDead ? Thing.Loose : Thing.Body;
        }
        if (collider.GetComponentInParent<IPlaced>() != null) { why = "placed"; return Thing.Structure; }
        // The dead lie in their dolls, held still or not.
        if (collider.GetComponentInParent<RagdollCorpse>() != null) { why = "corpse"; return Thing.Loose; }
        if (GoreGib.IsPiece(collider)) { why = "gore"; return Thing.Loose; }
        if (DroppedMagazine.IsMagazine(collider)) { why = "magazine"; return Thing.Loose; }
        if (ThrownStone.IsStone(collider)) { why = "thrown"; return Thing.Loose; }
        var carried = collider.attachedRigidbody;
        if (carried != null && !carried.isKinematic) { why = "physics"; return Thing.Loose; }
        if (collider.gameObject.layer == WorldLayers.Passed) { why = "passed layer"; return Thing.Loose; }
        why = "world";
        return Thing.World;
    }

    /// <summary>
    /// The nearest thing along a ray among those in <paramref name="meets"/>, the <paramref name="builder"/>'s own body
    /// aside: what it is (None when there is nothing within <paramref name="reach"/>) and where it was met.
    /// </summary>
    public static Thing Ray(Vector3 from, Vector3 way, float reach, Transform builder, Thing meets, out RaycastHit hit)
    {
        hit = default;
        if (reach <= 0f || way.sqrMagnitude < 1e-8f) return Thing.None;
        way.Normalize();
        int count = Physics.RaycastNonAlloc(from, way, rays, reach, ~0, QueryTriggerInteraction.Ignore);
        var hits = rays;
        // A full buffer may have left the nearest one out (a pile of corpses is a hundred shapes): ask for them all.
        if (count == rays.Length)
        {
            hits = Physics.RaycastAll(from, way, reach, ~0, QueryTriggerInteraction.Ignore);
            count = hits.Length;
        }
        var what = Thing.None;
        float nearest = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            var candidate = hits[i];
            if (candidate.collider == null || candidate.distance >= nearest) continue;
            if (builder != null && candidate.collider.transform.IsChildOf(builder)) continue;
            var kind = Of(candidate.collider);
            if ((kind & meets) == 0) continue;
            nearest = candidate.distance;
            hit = candidate;
            what = kind;
        }
        return what;
    }

    /// <summary>
    /// Whether a box holds any of the things in <paramref name="meets"/>, the <paramref name="builder"/>'s own body
    /// aside: what the first one found is (None when the box is free of them), and which collider.
    /// </summary>
    public static Thing Box(Vector3 middle, Vector3 half, Quaternion turn, Transform builder, Thing meets, out Collider by)
    {
        by = null;
        int count = Physics.OverlapBoxNonAlloc(middle, half, found, turn, ~0, QueryTriggerInteraction.Ignore);
        var inside = found;
        if (count == found.Length)
        {
            inside = Physics.OverlapBox(middle, half, turn, ~0, QueryTriggerInteraction.Ignore);
            count = inside.Length;
        }
        for (int i = 0; i < count; i++)
        {
            var candidate = inside[i];
            if (candidate == null || (builder != null && candidate.transform.IsChildOf(builder))) continue;
            var kind = Of(candidate);
            if ((kind & meets) == 0) continue;
            by = candidate;
            return kind;
        }
        return Thing.None;
    }

    // ---- The record (Logs/placement.txt) ----

    /// <summary>A collider as the record names it: where it hangs in the scene, what the rule takes it for and why.</summary>
    public static string Describe(Collider collider)
    {
        if (collider == null) return "nothing";
        var what = Of(collider, out string why);
        return $"\"{PlayRecord.PathOf(collider.transform)}\" [{what}: {why}, layer {collider.gameObject.layer}]";
    }

    /// <summary>
    /// What lies in a box and gives way to something set down there, counted by what it is ("corpse ×14, gore ×2"):
    /// the record's proof that the dead were there and were passed. Asked only when a line is written.
    /// </summary>
    public static string Passed(Vector3 middle, Vector3 half, Quaternion turn, Transform builder)
    {
        var inside = Physics.OverlapBox(middle, half, turn, ~0, QueryTriggerInteraction.Collide);
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var candidate in inside)
        {
            if (candidate == null || (builder != null && candidate.transform.IsChildOf(builder))) continue;
            if (Of(candidate, out string why) != Thing.Loose) continue;
            counts[why] = counts.TryGetValue(why, out int n) ? n + 1 : 1;
        }
        if (counts.Count == 0) return "nothing";
        var text = new StringBuilder();
        foreach (var pair in counts)
        {
            if (text.Length > 0) text.Append(", ");
            text.Append(pair.Key).Append(" ×").Append(pair.Value);
        }
        return text.ToString();
    }

    /// <summary>
    /// A placement let go: what was set down (or not), where, and what the rule met there. <paramref name="fit"/> is the
    /// deployable's own word for the outcome ("Ok", "Blocked", "Steep"); <paramref name="by"/> the collider in the way.
    /// </summary>
    public static void Record(string kind, string fit, bool placed, Vector3 at, float yaw, Vector3 size, Collider by, Transform builder)
    {
        if (!PlayRecord.On) return;
        Vector3 middle = at + Vector3.up * (size.y * .5f + .1f);
        var half = new Vector3(size.x * .5f, Mathf.Max(.05f, size.y * .5f - .05f), size.z * .5f);
        string passed = Passed(middle, half, Quaternion.Euler(0f, yaw, 0f), builder);
        PlayRecord.Line(RecordName, RecordHeading, $"{(placed ? "OK     " : "REFUSED")} {kind} {fit} at {PlayRecord.At(at)} yaw {yaw:0}"
            + $" | by {(by != null ? Describe(by) : "nothing")} | passed {passed}");
    }
}
