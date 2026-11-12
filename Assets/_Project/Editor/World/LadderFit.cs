using System.Globalization;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Fits the ladders a map builder lays (CampaignMaps through GreyboxBuilder, HubBuilder) to the floor each one leads to,
/// so their rails end flush with it (the user, 2026-11-12: "사다리가 좀 필요 이상으로 높게 만드는 것 같다 네가. 옥상에
/// 딱 맞추면 될 것 같은데"). The layouts gave every ladder a height by hand, a metre or more past the roof it reached,
/// and the rails stood up out of the roof; a layout now gives only a ladder's foot and the way it faces.
/// Measured once the whole layout stands, on its colliders, where the climb off the top steps onto
/// (MovementFixtures.TryLeaveLadderTop): straight in from the rungs by Ability.ladderExitReach less ladderStandOff
/// (Player.csv), the first walkable top over the foot, which must leave a standing player room.
/// The climb needs nothing past that floor: the feet stop Ability.ladderTopGap under the rails' top, as on the
/// playground's ladder the climb off was made on (2026-11-08, rails 0.3 m over its platform), the hands push on the
/// rails' tops, and the climb off carries the body the rest of the way up onto the floor
/// (ObstacleTraversalState.StartLadderWarp). Taken from the top (Interact), the body goes out over the rails' tops
/// instead of through rails standing up past the edge.
/// </summary>
public static class LadderFit
{
    // How far over a ladder's foot its floor is looked for from (m), and the least climb a ladder makes (m): a top within
    // that of the foot is the floor the ladder stands on, or a kerb by it.
    private const float Search = 100f, LeastClimb = .5f;
    // A standing player (the standing capsule and the controller's radius), with a little gap under it, and the
    // steepest top that is a floor (about the controller's 45° slope limit).
    private const float StandingHeight = 1.8f, Radius = .3f, Gap = .05f, LeastUp = .7f;

    /// <summary>
    /// Fits every ladder under <paramref name="root"/>. Returns a line for the builder's closing window ("" when there are
    /// no ladders): how many were fitted with their heights, and any that found no floor, which keep the height they had
    /// and are also named in the console as a warning.
    /// </summary>
    public static string FitAll(Transform root)
    {
        var ladders = root != null ? root.GetComponentsInChildren<Ladder>(true) : new Ladder[0];
        if (ladders.Length == 0) return "";
        // The layout was laid just now in an edit-mode scene: physics sees where its boxes are once synced.
        Physics.SyncTransforms();
        float inset = Inset();
        var fitted = new StringBuilder();
        var lost = new StringBuilder();
        int done = 0;
        foreach (var ladder in ladders)
        {
            Vector3 foot = ladder.transform.position;
            if (!FloorOver(foot, ladder.Outward, inset, out float floor, out string why))
            {
                lost.Append(lost.Length > 0 ? ", " : "").Append(ladder.name);
                Debug.LogWarning("[LadderFit] " + ladder.name + ": " + why + " Its height is left as it was; move its foot "
                    + "so that its floor is " + inset.ToString("0.##", CultureInfo.InvariantCulture) + " m in from its rungs.", ladder);
                continue;
            }
            float height = floor - foot.y;
            var so = new SerializedObject(ladder);
            so.FindProperty("height").floatValue = height;
            so.ApplyModifiedPropertiesWithoutUndo();
            fitted.Append(fitted.Length > 0 ? ", " : "").Append(ladder.name).Append(' ')
                .Append(height.ToString("0.##", CultureInfo.InvariantCulture)).Append(" m");
            done++;
        }
        // The heights go to the closing window only: a fitted ladder is nothing to act on, so the console stays quiet
        // (a rebuild of every chapter printed a line of them per chapter, 11-12). A ladder with no floor still warns.
        return "사다리 " + done + "개를 오르는 바닥 높이에 맞췄어요" + (fitted.Length > 0 ? "(" + fitted + ")" : "") + "."
            + (lost.Length > 0 ? " 바닥을 못 찾은 사다리: " + lost + " (높이 그대로, 콘솔을 보세요)" : "");
    }

    // The first walkable top over the foot, in from the rungs, with room to stand on it. A top under something too low
    // to stand under is no floor, and the ladder doesn't go on past it to whatever is over that.
    private static bool FloorOver(Vector3 foot, Vector3 outward, float inset, out float floor, out string why)
    {
        floor = 0f;
        why = null;
        Vector3 column = foot - outward * inset;
        var hits = Physics.RaycastAll(column + Vector3.up * Search, Vector3.down, Search, Physics.DefaultRaycastLayers,
            QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.point.y.CompareTo(b.point.y));
        foreach (var hit in hits)
        {
            if (hit.point.y < foot.y + LeastClimb) continue;
            if (hit.normal.y < LeastUp) { why = "the first top over its foot (" + hit.collider.name + ") is too steep to stand on."; return false; }
            Vector3 low = new Vector3(column.x, hit.point.y + Gap + Radius, column.z);
            Vector3 high = new Vector3(column.x, hit.point.y + StandingHeight - Radius, column.z);
            if (Physics.CheckCapsule(low, high, Radius, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                why = "there is no room to stand on the first top over its foot (" + hit.collider.name + ").";
                return false;
            }
            floor = hit.point.y;
            return true;
        }
        why = "nothing over its foot to climb to.";
        return false;
    }

    // Where the climb off the top looks for its floor, in from the rungs (m): Ability.ladderExitReach in from the body,
    // which hangs Ability.ladderStandOff out from them (Player.csv; the settings' own values without the table).
    private static float Inset()
    {
        var defaults = new MovementAbilitySettings();
        float reach = Read("ladderExitReach", defaults.ladderExitReach), standOff = Read("ladderStandOff", defaults.ladderStandOff);
        return Mathf.Max(.1f, reach - standOff);
    }

    private static float Read(string field, float fallback)
        => GameTables.TryGetRow(PlayerMovement.TableName, "Ability", out var row) && row.TryGetValue(field, out string cell)
            && float.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;
}
