using System.Text;
using UnityEngine;

/// <summary>
/// The record of where the campaign's zombies come from and why (user, 2026-11-18, heading the whole list: "문제를
/// 근본적으로 생각하고 뿌리를 고치며"; whoever changes a spawn rule can't play the game, so the rule has to say for
/// itself what it did). It changes nothing: it listens to what the spawn model says (SpawnGround.Said) and keeps it.
/// <para>In the editor every line goes to Logs/spawns.txt, started afresh by the first of a Play: the route each time
/// it is laid (every step's stretch, its flow, what the walk mesh does not join), every spawn point's place on it with
/// FINDING lines for what is worth a look (a point the walk mesh joins to no part of the route, a step with no point
/// ahead of it), each stretch as it is cleared, a wipe, and a line for every zombie made: the point, the squad's front
/// and rear, the rule, and how many points each reason refused. So a chapter started once has written its own report.</para>
/// <para>The console's /spawns (the host) writes the state as it stands now: the squad, the rule, the stretches and what
/// is cleared, and every point with what a pick would make of it this moment (measured for real, nothing made).
/// /spawns cases runs the rule itself over its cases with no map (SpawnPick.Cases). In a build the same text goes to
/// the Player.log.</para>
/// <para>/spawns here [name] is how a spawn room is put in a map without code (user, 2026-11-18: "방법만 알려 주면 알아
/// 하겠습니다"): it writes the row of Tables/SpawnPoints for a room where this PC's player stands, copies it, and says
/// whether a zombie could stand there. Pasted into the table and saved, the room is in the map at once.</para>
/// </summary>
[DisallowMultipleComponent]
public sealed class SpawnReport : MonoBehaviour
{
    private const string FileName = "spawns.txt";
    private static SpawnReport instance;
    private readonly StringBuilder pending = new StringBuilder();
    private float began, nextWrite;
    private bool fresh = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (instance != null) return;
        instance = new GameObject("Spawn Report").AddComponent<SpawnReport>();
        RuntimeSceneHierarchy.Keep(instance.gameObject);
    }

    private void OnEnable()
    {
        began = Time.realtimeSinceStartup;
#if UNITY_EDITOR
        // Only where there is a file to keep them in: with nobody listening the model writes no line at all.
        SpawnGround.Said += Line;
#endif
    }

    private void OnDisable()
    {
#if UNITY_EDITOR
        SpawnGround.Said -= Line;
#endif
        Flush();
    }

    private void Update()
    {
        if (pending.Length > 0 && Time.realtimeSinceStartup >= nextWrite) Flush();
    }

    private void Line(string text)
    {
        pending.Append((Time.realtimeSinceStartup - began).ToString("0.0").PadLeft(7)).Append(" s  ").Append(text).Append('\n');
    }

    // The lines so far, to Logs/spawns.txt in the editor: started afresh by the first of a Play, a second apart at most.
    private void Flush()
    {
        nextWrite = Time.realtimeSinceStartup + 1f;
        if (pending.Length == 0) return;
#if UNITY_EDITOR
        try
        {
            string path = System.IO.Path.Combine(Application.dataPath, "..", "Logs", FileName);
            if (fresh) System.IO.File.WriteAllText(path, "Play started " + System.DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\n" + pending);
            else System.IO.File.AppendAllText(path, pending.ToString());
            fresh = false;
        }
        catch (System.Exception problem) { Debug.LogWarning("[Spawns] Logs/" + FileName + ": " + problem.Message); }
#endif
        pending.Clear();
    }

    /// <summary>
    /// The console's /spawns: the state as it stands now (the host, in a chapter), or with "cases" the rule over its
    /// cases. The whole text goes to the log (and Logs/spawns.txt in the editor); the answer is its first line.
    /// </summary>
    public static string Command(string args)
    {
        string asked = (args ?? "").Trim();
        if (asked == "here" || asked.StartsWith("here ", System.StringComparison.Ordinal)) return Here(asked.Substring(4).Trim());
        bool cases = asked == "cases";
        string text;
        if (cases) text = SpawnPick.Cases();
        else if (!CampaignRun.TrySpawnsNow(out text)) return text;
        Debug.Log("[Spawns] " + (cases ? "cases" : "now") + "\n" + text);
        if (instance != null)
        {
            foreach (string line in text.Split('\n')) if (line.Length > 0) instance.Line((cases ? "CASES " : "NOW   ") + line);
            instance.Flush();
        }
        int end = text.IndexOf('\n');
        string answer = cases ? text.Substring(text.LastIndexOf("wrong:", System.StringComparison.Ordinal)).Trim() : end > 0 ? text.Substring(0, end) : text;
        return answer + " · Logs/" + FileName; // dev-text
    }

    // /spawns here [open] [name]: the row of Tables/SpawnPoints for a point where this PC's player stands (a room unless
    // "open" is said), copied for pasting, with a word on whether a zombie could stand there.
    private static string Here(string asked)
    {
        var player = PlayerRoster.Local;
        if (player == null) return "플레이어가 없어요."; // dev-text
        var role = SpawnRole.Room;
        if (asked == "open" || asked.StartsWith("open ", System.StringComparison.Ordinal)) { role = SpawnRole.Open; asked = asked.Substring(4).Trim(); }
        string chapter = player.gameObject.scene.name;
        // The name is the point's id in the table: commas would cut the row in two.
        string name = asked.Length > 0 ? asked.Replace(',', ' ') : "방 " + System.DateTime.Now.ToString("HHmmss");
        Vector3 at = player.transform.position;
        bool mesh = UnityEngine.AI.NavMesh.SamplePosition(at, out var hit, 2f, UnityEngine.AI.NavMesh.AllAreas);
        if (mesh) at = hit.position;
        string row = SpawnRules.RowFor(chapter, name, role, at);
        GUIUtility.systemCopyBuffer = row;
        string fit = !mesh ? "여기는 좀비가 걷는 길(걷기 메시) 위가 아니라서 아무도 못 나와요. 바닥에 서서 다시 해 보세요"
            : !SpawnSight.Roomy(at) ? "몸이 설 자리가 없어요(벽·소품에 걸림). 조금 옮겨서 다시 해 보세요" : "좀비가 설 수 있는 자리예요"; // dev-text
        Debug.Log("[Spawns] row for Tables/SpawnPoints.csv (copied):\n" + row);
        if (instance != null) { instance.Line("HERE  " + row); instance.Flush(); }
        return "복사했어요, SpawnPoints.csv 맨 아래에 붙여 넣고 저장하면 바로 생겨요: " + row + " · " + fit; // dev-text
    }
}
