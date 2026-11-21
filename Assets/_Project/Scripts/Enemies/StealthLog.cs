using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// A record of how the zombies' alarm really goes off, for the talk about redesigning it (user, 2026-11-18: "과도하게
/// 잦고 불합리하게 발동하는 "놈들이 소리쳤다" 알람 … 추후 논의를 통해 … 재설계 예정"). It changes nothing: it only listens
/// where the zombies' minds run (the host, or alone) to every zombie waking (ZombieMind.Woke) and every call
/// (ZombieMind.Called).
/// What the code says should make it fire often is counted here so it can be seen: each zombie that wakes by itself
/// calls on its own, the same zombie may call again after losing its quarry, and a lull turns the whole horde into
/// zombies that call. In the editor every line goes to Logs/stealth.txt; the console's /alarm prints the counts.
/// </summary>
[DisallowMultipleComponent]
public sealed class StealthLog : MonoBehaviour
{
    private static StealthLog instance;
    private readonly Queue<float> callTimes = new Queue<float>();
    private readonly Dictionary<int, int> callsBy = new Dictionary<int, int>();
    private readonly StringBuilder pending = new StringBuilder();
    private int wokeAlone, wokeByCall, calls, callsAgain, callsOnceHorde;
    private float began, nextWrite;
    private bool fresh = true;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Create()
    {
        if (instance != null) return;
        instance = new GameObject("Stealth Log").AddComponent<StealthLog>();
        RuntimeSceneHierarchy.Keep(instance.gameObject);
    }

    private void OnEnable()
    {
        began = Time.realtimeSinceStartup;
        ZombieMind.Woke += OnWoke;
        ZombieMind.Called += OnCalled;
    }

    private void OnDisable()
    {
        ZombieMind.Woke -= OnWoke;
        ZombieMind.Called -= OnCalled;
        Flush();
    }

    private void OnWoke(ZombieMind mind, PlayerMovement quarry)
    {
        if (mind == null) return;
        bool alone = mind.WokeByItself;
        if (alone) wokeAlone++;
        else wokeByCall++;
        Line($"woke {Name(mind)} {(alone ? "by itself (will call)" : "by a call or without one")}{(mind.OnceHorde ? ", once of the horde" : "")}, quarry {Far(mind, quarry)}");
    }

    private void OnCalled(ZombieMind mind, PlayerMovement quarry)
    {
        if (mind == null) return;
        calls++;
        callTimes.Enqueue(Time.realtimeSinceStartup);
        int id = mind.GetInstanceID();
        callsBy.TryGetValue(id, out int before);
        callsBy[id] = before + 1;
        if (before > 0) callsAgain++;
        if (mind.OnceHorde) callsOnceHorde++;
        Line($"CALL {Name(mind)}{(before > 0 ? ", its call number " + (before + 1) : "")}{(mind.OnceHorde ? ", once of the horde" : "")}, quarry {Far(mind, quarry)}");
    }

    private void Update()
    {
        if (pending.Length > 0 && Time.realtimeSinceStartup >= nextWrite) Flush();
    }

    private static string Name(ZombieMind mind)
        => (mind.TryGetComponent(out ZombieType type) ? type.Kind.ToString() : "?") + "#" + (mind.GetInstanceID() & 0xFFFF).ToString("X4");

    private static string Far(ZombieMind mind, PlayerMovement quarry)
        => quarry == null ? "nobody" : Vector3.Distance(mind.transform.position, quarry.transform.position).ToString("0.0") + " m off";

    private void Line(string text)
    {
        pending.Append((Time.realtimeSinceStartup - began).ToString("0.0").PadLeft(7)).Append(" s  ")
            .Append(text).Append('\n');
    }

    // The lines so far, to Logs/stealth.txt in the editor: started afresh by the first of a Play, a second apart at most.
    private void Flush()
    {
        nextWrite = Time.realtimeSinceStartup + 1f;
        if (pending.Length == 0) return;
#if UNITY_EDITOR
        try
        {
            string path = System.IO.Path.Combine(Application.dataPath, "..", "Logs", "stealth.txt");
            if (fresh) System.IO.File.WriteAllText(path, "Play started " + System.DateTime.Now.ToString("HH:mm:ss") + "\n" + pending);
            else System.IO.File.AppendAllText(path, pending.ToString());
            fresh = false;
        }
        catch (System.Exception problem) { Debug.LogWarning("[StealthLog] Logs/stealth.txt: " + problem.Message); }
#endif
        pending.Clear();
    }

    /// <summary>The console's /alarm: the counts since the game started, and the calls of the last minute.</summary>
    public static string Command(string args)
    {
        if (instance == null) return "기록이 아직 없다"; // dev-text
        return instance.Counts();
    }

    private string Counts()
    {
        float now = Time.realtimeSinceStartup;
        while (callTimes.Count > 0 && now - callTimes.Peek() > 60f) callTimes.Dequeue();
        int callers = callsBy.Count;
        string text = $"외침 {calls}번(지난 1분 {callTimes.Count}번) · 외친 좀비 {callers}마리 · 같은 좀비가 또 외친 것 {callsAgain}번 · 떼였다가 외친 것 {callsOnceHorde}번 · "
            + $"스스로 깬 좀비 {wokeAlone} · 외침에 깬 좀비 {wokeByCall} · Logs/stealth.txt"; // dev-text
        Line("COUNTS " + text);
        Flush();
        Debug.Log("[StealthLog] " + text);
        return text;
    }
}
