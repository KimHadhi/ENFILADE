// dev-text: file (the console's /zai readout and its answer, for whoever tunes the zombies; nothing here is shown to players)
using System.Collections.Generic;
using System.Text;
using Unity.Profiling;
using UnityEngine;

/// <summary>
/// The console's /zai: what the zombies cost this PC, measured until it is switched off, which reports the whole
/// measurement and copies it (chat text can't be selected), as /net does for the network. It works alone too.
/// The user asked for the zombies' thinking to be made cheap (2026-11-14), and nothing can be run or profiled from where
/// the code is written, so the user reads it off here in a horde: on the host, which runs every zombie's brain, and on a
/// guest, which only shows copies. Each second it shows:
/// - how many zombies there are, how many this PC runs, awake, calm and lying;
/// - each timed part of the zombies' scripts in ms a frame (ZombieCost): thinking, sight and its rays a second, the
///   chase, picking a quarry, asking for paths (how many a second, and how many turns kept their path), places in the
///   crowd, stepping back out of players, attacks, posture and stuns, the body's numbers, the host's network writes;
/// - the engine's own share where the build can tell (the editor and development builds): the walk mesh and its agents,
///   animation, skinning, Netcode's tick. Those are not the scripts' to shorten, but they say where the frame goes.
/// </summary>
[DisallowMultipleComponent]
public sealed class ZombieCostHUD : MonoBehaviour
{
    private const float SampleTime = 1f, Width = 470f, LineHeight = 22f, Margin = 20f;

    // The engine's own markers worth reading beside the scripts', by the names its profiler gives them. One the build
    // doesn't have is left out (a release build has few).
    private static readonly (string marker, string label)[] Engine =
    {
        ("NavMeshManager", "NavMesh"), ("Animators.Update", "Animator"), ("MeshSkinning.Update", "스키닝"),
        ("Physics.Simulate", "물리"), ("NetworkTickSystem.Tick", "Netcode 틱"),
        ("NetworkBehaviour.NetworkBehaviourUpdate", "Netcode 변수")
    };

    private static ZombieCostHUD running;

    private readonly List<string> lines = new List<string>();
    private readonly double[] milliseconds = new double[ZombieCost.Parts], total = new double[ZombieCost.Parts];
    private readonly int[] ran = new int[ZombieCost.Parts], counted = new int[ZombieCost.Tallies];
    private readonly long[] countedAll = new long[ZombieCost.Tallies];
    private readonly ProfilerRecorder[] recorders = new ProfilerRecorder[Engine.Length];
    private readonly double[] engineSample = new double[Engine.Length], engineAll = new double[Engine.Length];
    private readonly StringBuilder text = new StringBuilder(256);
    private float sampleLeft = SampleTime, elapsed, worstInSample, worstFrame, worstScripts;
    private int framesInSample, frames, zombiesMost;
    private double scriptsAll;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => running = null;

    /// <summary>/zai: starts a measurement, or ends the one running and answers with its result.</summary>
    public static string Command(string _)
    {
        if (running != null)
        {
            string result = running.Result();
            Destroy(running.gameObject);
            running = null;
            GUIUtility.systemCopyBuffer = result;
            return result + " (복사됨)";
        }
        var holder = new GameObject(nameof(ZombieCostHUD));
        DontDestroyOnLoad(holder);
        running = holder.AddComponent<ZombieCostHUD>();
        return "좀비 AI 비용 측정을 시작했습니다. 좀비 떼와 싸운 뒤 /zai를 다시 입력하면 결과가 나옵니다(방장 창과 참가자 창에서 따로).";
    }

    private void OnEnable()
    {
        ZombieCost.Switch(true);
        for (int i = 0; i < Engine.Length; i++)
        {
            recorders[i] = new ProfilerRecorder(Engine[i].marker, 1, ProfilerRecorderOptions.Default);
            if (recorders[i].Valid) recorders[i].Start();
        }
    }

    private void OnDisable()
    {
        ZombieCost.Switch(false);
        for (int i = 0; i < recorders.Length; i++) recorders[i].Dispose();
    }

    private void Update()
    {
        float dt = Time.unscaledDeltaTime;
        elapsed += dt;
        frames++;
        framesInSample++;
        worstInSample = Mathf.Max(worstInSample, dt);
        // The engine's markers say what the frame before took; added up frame by frame.
        for (int i = 0; i < recorders.Length; i++)
            if (recorders[i].Valid) engineSample[i] += recorders[i].LastValue * 1e-6;
        sampleLeft -= dt;
        if (sampleLeft > 0f) return;
        Sample(SampleTime - sampleLeft);
        sampleLeft = SampleTime;
    }

    private void Sample(float time)
    {
        ZombieCost.Take(milliseconds, ran, counted);
        int count = Mathf.Max(1, framesInSample);
        // Per frame. Parts timed inside another are taken out of it, so the parts add up to the scripts' whole.
        double sight = milliseconds[(int)ZombieCost.Part.Sight], target = milliseconds[(int)ZombieCost.Part.Target];
        double path = milliseconds[(int)ZombieCost.Part.Path], spot = milliseconds[(int)ZombieCost.Part.Spot];
        double mind = milliseconds[(int)ZombieCost.Part.Mind] - sight;
        // A quarry is picked inside the chase and inside the attack; its time is shown once, apart from both.
        double chase = milliseconds[(int)ZombieCost.Part.Chase] - path - spot;
        double combat = milliseconds[(int)ZombieCost.Part.Combat];
        double shared = chase + combat;
        if (shared > target && shared > 0d) { chase -= target * (chase / shared); combat -= target * (combat / shared); }
        double yield = milliseconds[(int)ZombieCost.Part.Yield], stance = milliseconds[(int)ZombieCost.Part.Stance];
        double body = milliseconds[(int)ZombieCost.Part.Body], network = milliseconds[(int)ZombieCost.Part.Network];
        double scripts = mind + sight + chase + target + path + spot + yield + combat + stance + body + network;
        for (int i = 0; i < ZombieCost.Parts; i++) total[i] += milliseconds[i];
        for (int i = 0; i < ZombieCost.Tallies; i++) countedAll[i] += counted[i];
        scriptsAll += scripts;
        worstScripts = Mathf.Max(worstScripts, (float)(scripts / count));
        worstFrame = Mathf.Max(worstFrame, worstInSample);

        CountZombies(out int zombies, out int mine, out int awake, out int calm, out int lying);
        zombiesMost = Mathf.Max(zombiesMost, zombies);

        lines.Clear();
        string role = !GameSession.IsOnline ? "혼자" : GameSession.IsGuest ? "참가자 (사본만 보여 줌)" : "방장 (두뇌를 굴림)";
        lines.Add($"좀비 AI 비용 /zai · {role}");
        lines.Add($"좀비 {zombies} (이 PC가 굴림 {mine}) · 깸 {awake} · 조용 {calm} · 누움 {lying}");
        lines.Add($"프레임 {1000f * time / count:F1} ms ({count / time:F0} fps), 가장 느림 {worstInSample * 1000f:F0} ms");
        lines.Add($"좀비 스크립트 합계 {scripts / count:F2} ms/프레임");
        lines.Add($"  생각 {mind / count:F2} · 시야 {sight / count:F2} (광선 {counted[(int)ZombieCost.Tally.Rays] / time:F0}/초, 살핌 "
            + $"{counted[(int)ZombieCost.Tally.Looks] / time:F0}/초 · 미룸 {counted[(int)ZombieCost.Tally.LooksPutOff] / time:F0}/초)");
        lines.Add($"  추적 {chase / count:F2} · 노릴 사람 {target / count:F2} · 길 요청 {path / count:F2} "
            + $"({counted[(int)ZombieCost.Tally.Paths] / time:F0}/초, 그대로 둠 {counted[(int)ZombieCost.Tally.PathsKept] / time:F0}/초)");
        lines.Add($"  무리 자리 {spot / count:F2} · 비켜서기 {yield / count:F2} · 공격 {combat / count:F2} · 자세·기절 {stance / count:F2}");
        lines.Add($"  몸(애니메이션 값) {body / count:F2} · 네트워크 쓰기 {network / count:F2} (위치 "
            + $"{counted[(int)ZombieCost.Tally.Sends] / time:F0}/초)");
        text.Clear();
        for (int i = 0; i < Engine.Length; i++)
        {
            if (!recorders[i].Valid) continue;
            engineAll[i] += engineSample[i];
            if (text.Length > 0) text.Append(" · ");
            text.Append(Engine[i].label).Append(' ').Append((engineSample[i] / count).ToString("F2"));
            engineSample[i] = 0d;
        }
        lines.Add(text.Length > 0 ? "엔진(ms/프레임): " + text : "엔진 쪽 수치는 에디터·개발 빌드에서만 보입니다");
        framesInSample = 0;
        worstInSample = 0f;
    }

    private static void CountZombies(out int zombies, out int mine, out int awake, out int calm, out int lying)
    {
        zombies = mine = awake = calm = lying = 0;
        foreach (var enemy in EnemyHealth.Living)
        {
            if (enemy == null || !enemy.IsAlive) continue;
            zombies++;
            if (!enemy.TryGetComponent(out EnemyNetwork network) || !network.IsCopy) mine++;
            if (enemy.TryGetComponent(out MeleeCombatTarget target) && target.IsLying) lying++;
            if (!enemy.TryGetComponent(out ZombieMind mind)) continue;
            if (mind.IsAwake) awake++;
            else calm++;
        }
    }

    private string Result()
    {
        if (elapsed <= 0f || frames <= 0) return "측정한 시간이 없습니다.";
        // What the last, unfinished sample gathered counts too.
        ZombieCost.Take(milliseconds, ran, counted);
        for (int i = 0; i < ZombieCost.Parts; i++) total[i] += milliseconds[i];
        for (int i = 0; i < ZombieCost.Tallies; i++) countedAll[i] += counted[i];
        double Each(ZombieCost.Part part) => total[(int)part] / frames;
        double sight = Each(ZombieCost.Part.Sight), path = Each(ZombieCost.Part.Path), spot = Each(ZombieCost.Part.Spot);
        double target = Each(ZombieCost.Part.Target);
        // The whole of the scripts: the outer parts, each with what is timed inside it.
        double scripts = Each(ZombieCost.Part.Mind) + Each(ZombieCost.Part.Chase) + Each(ZombieCost.Part.Yield)
            + Each(ZombieCost.Part.Combat) + Each(ZombieCost.Part.Stance) + Each(ZombieCost.Part.Body) + Each(ZombieCost.Part.Network);
        string role = !GameSession.IsOnline ? "혼자" : GameSession.IsGuest ? "참가자" : "방장";
        text.Clear();
        text.Append($"좀비 AI 측정 {elapsed:F0}초 ({role}, 좀비 최대 {zombiesMost}): 평균 {frames / elapsed:F0} fps, 가장 느린 프레임 "
            + $"{worstFrame * 1000f:F0} ms / 좀비 스크립트 평균 {scripts:F2} ms/프레임(1초 평균 중 최고 {worstScripts:F2}) = 생각 "
            + $"{Each(ZombieCost.Part.Mind) - sight:F2} + 시야 {sight:F2} + 추적 {Each(ZombieCost.Part.Chase) - path - spot:F2} + 길 요청 {path:F2} + 무리 자리 "
            + $"{spot:F2} + 비켜서기 {Each(ZombieCost.Part.Yield):F2} + 공격 {Each(ZombieCost.Part.Combat):F2} + 자세·기절 "
            + $"{Each(ZombieCost.Part.Stance):F2} + 몸 {Each(ZombieCost.Part.Body):F2} + 네트워크 {Each(ZombieCost.Part.Network):F2} (노릴 사람 {target:F2}은 추적·공격에 포함) / "
            + $"초당 광선 {countedAll[(int)ZombieCost.Tally.Rays] / elapsed:F0}, 길 요청 {countedAll[(int)ZombieCost.Tally.Paths] / elapsed:F0}"
            + $"(그대로 둠 {countedAll[(int)ZombieCost.Tally.PathsKept] / elapsed:F0}), 위치 전송 {countedAll[(int)ZombieCost.Tally.Sends] / elapsed:F0}");
        bool any = false;
        for (int i = 0; i < Engine.Length; i++)
        {
            if (!recorders[i].Valid) continue;
            text.Append(any ? ", " : " / 엔진 ms/프레임: ").Append(Engine[i].label).Append(' ').Append(((engineAll[i] + engineSample[i]) / frames).ToString("F2"));
            any = true;
        }
        return text.ToString();
    }

    private void OnGUI()
    {
        if (lines.Count == 0) return;
        GUILayout.BeginArea(new Rect(Margin, Screen.height * .32f, Width, LineHeight * (lines.Count + 1)), GUI.skin.box);
        foreach (string line in lines) GUILayout.Label(line);
        GUILayout.EndArea();
    }
}
