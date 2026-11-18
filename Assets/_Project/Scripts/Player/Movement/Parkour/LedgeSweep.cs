#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

// dev-text: file (a report for whoever checks a map's ledges; nothing here is shown in the game)

/// <summary>
/// The editor's sweep of the open scene's ledges, without playing: along the top edge of every box in the scene a body
/// of the player's size is stood before the face, at the heights a jump takes its feet through, and the game's own
/// finder (LedgeFinder.Find, with Player.csv's numbers) is asked what is there. Logs/ledge-sweep.txt then says how many
/// places are ledges, every top it refused for having no room for a hand (the seam under a slab that the player used to
/// get stuck in: the user, 2026-11-18), and every ledge that can be hung from but not climbed onto. A map's builder reads
/// it to see what a wall really offers; whoever changes the finder reads it to see nothing became a ledge that should
/// not be.
/// It runs from ENFILADE → 이동 → 렛지 훑기, or when the file Temp/enfilade-ledgesweep.request is made (a tool outside
/// Unity asking, as RefreshOnRequest is asked). Never by itself: a big map takes a few seconds.
/// </summary>
[InitializeOnLoad]
internal static class LedgeSweep
{
    private const string Request = "Temp/enfilade-ledgesweep.request", ReportPath = "Logs/ledge-sweep.txt";
    // A place every so far along an edge (m), the top this far over the feet at each try (m), and the most lines a list takes.
    private const float Step = 1f, LeastWidth = .4f;
    private static readonly float[] TopOverFeet = { 1.3f, 1.7f, 2.1f };
    private const int MostLines = 250;
    private static double next;

    static LedgeSweep() => EditorApplication.update += Tick;

    private static void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        if (now < next) return;
        next = now + .5;
        if (!File.Exists(Request)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        try
        {
            File.Delete(Request);
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            return;
        }
        Run();
    }

    [MenuItem("ENFILADE/이동/렛지 훑기 (열린 씬)")]
    private static void Run()
    {
        var text = new StringBuilder();
        try
        {
            Sweep(text);
        }
        catch (Exception problem)
        {
            text.AppendLine("FAIL the sweep itself stopped: " + problem);
        }
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
            File.WriteAllText(ReportPath, text.ToString(), new UTF8Encoding(false));
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            Debug.LogWarning("LedgeSweep: " + ReportPath + " could not be written: " + error.Message);
        }
        Debug.Log("LedgeSweep: " + ReportPath);
    }

    private static void Sweep(StringBuilder text)
    {
        var body = Body();
        var rules = Rules();
        Physics.SyncTransforms();
        var tally = new Dictionary<LedgeFinder.Why, int>();
        var seams = new Dictionary<Collider, (int count, Vector3 at, string under)>();
        var hangOnly = new List<string>();
        var elsewhere = new Dictionary<string, int>();
        int boxes = 0, places = 0, asked = 0, stand = 0, crouch = 0;
        foreach (var box in UnityEngine.Object.FindObjectsByType<BoxCollider>(FindObjectsSortMode.None))
        {
            if (!box.enabled || box.isTrigger || !WallDetector.CanUseSurface(box)) continue;
            Transform frame = box.transform;
            // Only boxes that stand upright have a top edge to hang from.
            if (Vector3.Dot(frame.up, Vector3.up) < .98f) continue;
            Vector3 scale = frame.lossyScale;
            var size = new Vector3(Mathf.Abs(box.size.x * scale.x), Mathf.Abs(box.size.y * scale.y), Mathf.Abs(box.size.z * scale.z));
            Vector3 centre = frame.TransformPoint(box.center);
            float topY = centre.y + size.y * .5f;
            boxes++;
            for (int side = 0; side < 4; side++)
            {
                Vector3 axis = side < 2 ? frame.right : frame.forward;
                if (side % 2 == 1) axis = -axis;
                float half = (side < 2 ? size.x : size.z) * .5f, width = side < 2 ? size.z : size.x;
                Vector3 outward = Vector3.ProjectOnPlane(axis, Vector3.up).normalized, along = Vector3.Cross(Vector3.up, outward);
                if (width < LeastWidth) continue;
                int count = Mathf.Max(1, Mathf.FloorToInt(width / Step));
                for (int k = 0; k < count; k++)
                {
                    Vector3 edge = centre + outward * half + along * ((k + .5f) / count * width - width * .5f);
                    edge.y = topY;
                    places++;
                    // One answer a place: from the first height the body has room to be at.
                    foreach (float over in TopOverFeet)
                    {
                        Vector3 feet = edge + outward * (body.Radius + .35f) - Vector3.up * over;
                        if (!LedgeFinder.ClearCapsule(body, feet, body.Height, body.Layers)) continue;
                        asked++;
                        bool found = LedgeFinder.Find(body, rules, feet, -outward, out LedgeTarget ledge, out LedgeFinder.Probe probe);
                        var why = found ? LedgeFinder.Why.Found : probe.Result;
                        tally[why] = tally.TryGetValue(why, out int so) ? so + 1 : 1;
                        if (found)
                        {
                            // The ledge taken may be another top than this box's (a slab resting on it): said by name.
                            if (probe.On != box)
                            {
                                string pair = PlayRecord.PathOf(box.transform) + " -> " + PlayRecord.PathOf(probe.On != null ? probe.On.transform : null);
                                elsewhere[pair] = elsewhere.TryGetValue(pair, out int seen) ? seen + 1 : 1;
                            }
                            switch (StandRoom(body, probe.Point, -outward))
                            {
                                case LedgeClimb.Stand: stand++; break;
                                case LedgeClimb.Crouch: crouch++; break;
                                default:
                                    if (hangOnly.Count < MostLines) hangOnly.Add($"  {PlayRecord.At(probe.Point)} on {PlayRecord.PathOf(probe.On != null ? probe.On.transform : null)}");
                                    break;
                            }
                        }
                        else if (why == LedgeFinder.Why.NoHandRoom && probe.On != null)
                        {
                            seams.TryGetValue(probe.On, out var seam);
                            seams[probe.On] = (seam.count + 1, probe.Point, seam.under ?? Over(body, probe.Point));
                        }
                        break;
                    }
                }
            }
        }
        var scene = SceneManager.GetActiveScene();
        text.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} ledge sweep of {scene.name}: {boxes} upright boxes, {places} places along their top edges, {asked} with room for a body before them");
        text.AppendLine($"body radius {body.Radius:0.00} height {body.Height:0.00} crouch {body.CrouchHeight:0.00}; a top {rules.Hands(body.Height) - rules.BelowHands:0.00} to {rules.Hands(body.Height) + rules.AboveHands:0.00} m over the feet is in reach, hand room {rules.HandRoom:0.00} m");
        foreach (LedgeFinder.Why why in Enum.GetValues(typeof(LedgeFinder.Why)))
            text.AppendLine($"  {why,-12} {(tally.TryGetValue(why, out int n) ? n : 0)}");
        text.AppendLine($"of the ledges found: {stand} with room to stand on top, {crouch} to crouch, {hangOnly.Count} to hang from only");
        text.AppendLine();
        text.AppendLine($"== tops refused for having no room for a hand ({seams.Count} colliders): the seam under what rests on them. The old rule hung the player here.");
        int lines = 0;
        foreach (var seam in seams)
        {
            if (lines++ >= MostLines) { text.AppendLine("  ..."); break; }
            text.AppendLine($"  {PlayRecord.PathOf(seam.Key.transform)}: {seam.Value.count} place(s), e.g. {PlayRecord.At(seam.Value.at)}, under {seam.Value.under}");
        }
        text.AppendLine();
        text.AppendLine($"== ledges taken on another collider than the wall under them ({elsewhere.Count} pairs): the old rule refused these (a top had to be the wall's own)");
        lines = 0;
        foreach (var pair in elsewhere)
        {
            if (lines++ >= MostLines) { text.AppendLine("  ..."); break; }
            text.AppendLine($"  {pair.Key}: {pair.Value} place(s)");
        }
        text.AppendLine();
        text.AppendLine($"== ledges to hang from only, no room on top ({hangOnly.Count}{(hangOnly.Count >= MostLines ? "+" : "")})");
        foreach (string line in hangOnly) text.AppendLine(line);
    }

    // What stands on a refused top: the first thing in the hand's space over it.
    private static string Over(in LedgeFinder.Body body, Vector3 top)
    {
        foreach (var collider in Physics.OverlapSphere(top + Vector3.up * .06f, .05f, body.Layers, QueryTriggerInteraction.Ignore))
            return PlayRecord.PathOf(collider.transform);
        return "(nothing found over it)";
    }

    // Whether a body fits on top behind this edge, standing or crouched: the question the climb asks, without the
    // player's own moves (which need a player).
    private static LedgeClimb StandRoom(in LedgeFinder.Body body, Vector3 edge, Vector3 inward)
    {
        var best = LedgeClimb.None;
        for (float depth = .1f; depth <= body.Radius + .5f; depth += .1f)
        {
            if (!LedgeFinder.Tread(body, edge + inward * depth + Vector3.up * .2f, .35f, out RaycastHit tread)) continue;
            Vector3 feet = tread.point + Vector3.up * .02f;
            if (LedgeFinder.ClearCapsule(body, feet, body.Height, body.Layers)) return LedgeClimb.Stand;
            if (LedgeFinder.ClearCapsule(body, feet, body.CrouchHeight, body.Layers)) best = LedgeClimb.Crouch;
        }
        return best;
    }

    // The player's capsule as its prefab has it, and the ledge's numbers as the table has them.
    private static LedgeFinder.Body Body()
    {
        var prefab = Resources.Load<GameObject>("Characters/Player");
        var capsule = prefab != null ? prefab.GetComponent<CharacterController>() : null;
        return new LedgeFinder.Body
        {
            Radius = capsule != null ? capsule.radius : .3f, Height = capsule != null ? capsule.height : 1.75f,
            SlopeLimit = capsule != null ? capsule.slopeLimit : 45f,
            CrouchHeight = GameTables.Number(PlayerMovement.TableName, "Posture", "crouchColliderHeight", .9f),
            Layers = prefab != null ? ~(1 << prefab.layer) : ~0, Self = null
        };
    }

    private static LedgeFinder.Rules Rules()
    {
        float Of(string field, float fallback) => GameTables.Number(PlayerMovement.TableName, "Ability", field, fallback);
        return new LedgeFinder.Rules
        {
            Reach = Of("ledgeReach", 1f), EdgeBelowHead = Of("ledgeEdgeBelowHead", .15f), WallBelowHands = Of("ledgeWallBelowHands", .45f),
            BelowHands = Of("ledgeReachBelowHands", .4f), AboveHands = Of("ledgeReachAboveHands", .6f), Inset = Of("ledgeProbeInset", .08f),
            HandRoom = Of("ledgeHandRoom", .08f)
        };
    }
}
#endif
