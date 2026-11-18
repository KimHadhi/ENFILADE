using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// What a build checks about its own map, so nobody has to find it by playing (the user, 2026-11-18, on a list of map
/// bugs each found by hand: props through walls, an objective that rang behind a locked door, walls one walks through,
/// car hitboxes bigger than the car). Each check adds its lines to one file, Logs/MapAudit/&lt;scene&gt;.txt, written as
/// the build ends, and a line to the closing window that names only what needs the layout or a table changed. A later
/// session reads the file ("맵 검사 결과 봐 줘"); the same things can be measured again from the saved scene without
/// Unity (Tools/Maps).
/// The checks: where the solid props stood (GreyboxBuilder.Standing), and whether every Reach step's spot is a place
/// one can get to (below).
/// </summary>
public sealed partial class GreyboxBuilder
{
    private const string AuditFolder = "Logs/MapAudit";
    // A Reach spot whose chest-high point has something of the map within this is buried (m), as CampaignRun finds it.
    private const float ReachProbe = .05f;

    private readonly StringBuilder audit = new StringBuilder();
    private bool auditToRead;

    // Every check's line for the closing window, and the file. Called once, as the build ends.
    private string AuditReport()
    {
        string line = StandReport() + ReachReport();
        if (audit.Length == 0) return line;
        string path = AuditFolder + "/" + sceneName + ".txt";
        try
        {
            Directory.CreateDirectory(AuditFolder);
            File.WriteAllText(path, "# " + sceneName + " 맵 검사 (지을 때마다 다시 씀: GreyboxBuilder.Audit)\n" + audit, new UTF8Encoding(false));
        }
        catch (IOException error)
        {
            Debug.LogWarning("[Greybox] " + sceneName + ": " + path + " could not be written (" + error.Message + ").");
            return line;
        }
        return line + (auditToRead ? " 자세한 것은 " + path + "." : "");
    }

    // The campaign's Reach steps of this map: a step is done when someone is within its radius of its spot with nothing
    // of the building between their chest and the spot's (CampaignRun.Reach). A spot whose own chest-high point is
    // inside a wall or a prop can be seen from nowhere; the game then falls back to the radius alone for it, which is
    // the ball through walls the rule was made to end. So the build names every such spot: move it in the layout.
    private string ReachReport()
    {
        if (!AddsCampaign) return "";
        Physics.SyncTransforms();
        var buried = new List<string>();
        int looked = 0;
        audit.Append("\n## 도착 자리 (Reach 단계: 반경 안이고 그 자리와 사이에 벽·문·바닥이 없어야 도착)\n");
        foreach (var step in CampaignStep.For(sceneName))
        {
            if (step.Kind != CampaignStepKind.Reach) continue;
            var spot = spots.Find(each => each != null && each.name == step.Spot);
            if (spot == null)
            {
                audit.Append("없음 ").Append(step.Id).Append(": 진행 자리 '").Append(step.Spot).Append("'가 이 맵에 없어요(게임은 이 단계를 건너뛰어요)\n");
                continue;
            }
            looked++;
            Vector3 chest = spot.position + Vector3.up * CampaignRun.ReachChest;
            string inside = null;
            foreach (var solid in Physics.OverlapSphere(chest, ReachProbe, ~0, QueryTriggerInteraction.Ignore))
                if (solid.attachedRigidbody == null) { inside = solid.name; break; }
            if (inside == null) continue;
            buried.Add(step.Spot);
            audit.Append("묻힘 ").Append(step.Id).Append(" '").Append(step.Spot).Append("' (").Append(Format(spot.position)).Append("): 가슴 높이가 ")
                .Append(inside).Append(" 속이에요. 자리를 옮겨야 해요\n");
        }
        audit.Append("# 합계: 도착 자리 ").Append(looked).Append("곳, 묻힌 곳 ").Append(buried.Count).Append('\n');
        if (buried.Count == 0) return "";
        auditToRead = true;
        return "\n도착 자리가 벽이나 소품 속에 묻힌 곳 " + buried.Count + "곳: " + string.Join(", ", buried) + " (자리를 옮겨야 해요).";
    }
}
