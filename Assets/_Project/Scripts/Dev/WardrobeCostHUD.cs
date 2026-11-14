// dev-text: file (the console's /wardrobe readout and its answer, for whoever tunes the wardrobe; nothing here is shown to players)
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

/// <summary>
/// The console's /wardrobe: what standing a dressed character up costs this PC, measured until it is switched off,
/// which reports the whole measurement and copies it, as /zai does for the zombies. The user asked for the wardrobe's
/// preview to be looked at for its lag (2026-11-14: "그 미리 보기 할 때 렉 걸리는 거 최적화 탐색 좀"), and nothing can be
/// run or profiled from where the code is written, so the user reads it off here while trying clothes on. For each
/// character stood up it shows what that took and how long the frame it fell in was, and inside it (WardrobeCost): the
/// model, dressing (outfits joined to the bones, pieces taking the body's shape keys, what covers what, layers fitted
/// under one another, and how much of that was measuring a pair of meshes for the first time), the soles, the face;
/// then the cloth, hung a moment later in a frame of its own; and how often a look came back off the shelf for nothing.
/// In the editor the answer and every event are also written to Logs/wardrobe-cost.txt.
/// </summary>
[DisallowMultipleComponent]
public sealed class WardrobeCostHUD : MonoBehaviour
{
    private const float Width = 760f, LineHeight = 22f, Margin = 20f;
    private const int MostEvents = 500;

    private static WardrobeCostHUD running;

    private readonly List<string> lines = new List<string>(), events = new List<string>();
    private readonly double[] milliseconds = new double[WardrobeCost.Parts], total = new double[WardrobeCost.Parts];
    private readonly int[] ran = new int[WardrobeCost.Parts], counted = new int[WardrobeCost.Tallies];
    private readonly long[] ranAll = new long[WardrobeCost.Parts], countedAll = new long[WardrobeCost.Tallies];
    private readonly StringBuilder text = new StringBuilder(512);
    // What the frame before did, waiting for that frame's length, which only the next frame knows.
    private string waitingStand, waitingCloth, waitingDress;
    private string lastStand = "", lastCloth = "", lastDress = "";
    private double worstStand, worstCloth;
    private float elapsed, worstFrame;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => running = null;

    /// <summary>/wardrobe: starts a measurement, or ends the one running and answers with its result.</summary>
    public static string Command(string _)
    {
        if (running != null)
        {
            string result = running.Result();
            running.Keep(result);
            Destroy(running.gameObject);
            running = null;
            GUIUtility.systemCopyBuffer = result;
            return result + " (복사됨" + (Application.isEditor ? ", Logs/wardrobe-cost.txt에도 적음" : "") + ")";
        }
        var holder = new GameObject(nameof(WardrobeCostHUD));
        DontDestroyOnLoad(holder);
        running = holder.AddComponent<WardrobeCostHUD>();
        return "옷장 미리 보기 비용 측정을 시작했습니다. 옷을 이것저것 입혀 본 뒤 /wardrobe를 다시 입력하면 결과가 나옵니다.";
    }

    private void OnEnable()
    {
        WardrobeCost.Switch(true);
        lines.Clear();
        lines.Add("옷장 비용 /wardrobe · 옷을 입혀 보면 여기에 뜹니다");
    }

    private void OnDisable() => WardrobeCost.Switch(false);

    // After everything that stands a character up this frame (the wardrobe's Update, a pointer's click, the cloth's rest).
    private void LateUpdate()
    {
        float frame = Time.unscaledDeltaTime * 1000f;
        elapsed += Time.unscaledDeltaTime;
        bool changed = false;
        if (waitingStand != null || waitingCloth != null || waitingDress != null)
        {
            worstFrame = Mathf.Max(worstFrame, frame);
            string tail = $" · 그 프레임 {frame:F0} ms";
            if (waitingStand != null) Log(lastStand = waitingStand + tail);
            if (waitingCloth != null) Log(lastCloth = waitingCloth + tail);
            if (waitingDress != null) Log(lastDress = waitingDress + tail);
            waitingStand = waitingCloth = waitingDress = null;
            changed = true;
        }
        WardrobeCost.Take(milliseconds, ran, counted);
        bool any = false;
        for (int i = 0; i < WardrobeCost.Parts; i++)
        {
            total[i] += milliseconds[i];
            ranAll[i] += ran[i];
            any |= ran[i] > 0;
        }
        for (int i = 0; i < WardrobeCost.Tallies; i++)
        {
            countedAll[i] += counted[i];
            any |= counted[i] > 0;
        }
        if (any)
        {
            double Of(WardrobeCost.Part part) => milliseconds[(int)part];
            int Count(WardrobeCost.Tally tally) => counted[(int)tally];
            string dressing = $"입히기 {Of(WardrobeCost.Part.Dress):F1} [붙이기 {Of(WardrobeCost.Part.Place):F1} · 체형 키 {Of(WardrobeCost.Part.Follow):F1} · 가림 "
                + $"{Of(WardrobeCost.Part.Cover):F1} · 겹침 맞춤 {Of(WardrobeCost.Part.Fit):F1} | 그중 처음 잰 쌍 {Count(WardrobeCost.Tally.Measured)}개 "
                + $"{Of(WardrobeCost.Part.Measure):F1} ms, 기억해 둔 쌍 {Count(WardrobeCost.Tally.Remembered)}개, 만든 메시 {Count(WardrobeCost.Tally.Meshes)}, 만든 키 "
                + $"{Count(WardrobeCost.Tally.Keys)}]";
            if (ran[(int)WardrobeCost.Part.Stand] > 0)
            {
                double stand = Of(WardrobeCost.Part.Stand);
                double rest = stand - Of(WardrobeCost.Part.Model) - Of(WardrobeCost.Part.Dress) - Of(WardrobeCost.Part.Sole) - Of(WardrobeCost.Part.Face);
                worstStand = Math.Max(worstStand, stand);
                waitingStand = $"세움 {stand:F1} ms = 모델 {Of(WardrobeCost.Part.Model):F1} · {dressing} · 발바닥 {Of(WardrobeCost.Part.Sole):F1} · 얼굴 "
                    + $"{Of(WardrobeCost.Part.Face):F1} · 그 밖 {rest:F1}";
            }
            // Dressed with nobody stood up: a player's own body (the hub's mirror, a spawn), not a stand-in.
            else if (ran[(int)WardrobeCost.Part.Dress] > 0) waitingDress = "몸에 " + dressing;
            if (ran[(int)WardrobeCost.Part.Cloth] > 0)
            {
                double cloth = Of(WardrobeCost.Part.Cloth);
                worstCloth = Math.Max(worstCloth, cloth);
                waitingCloth = $"천 걸기 {cloth:F1} ms ({Count(WardrobeCost.Tally.ClothParts)}조각)";
            }
            if (Count(WardrobeCost.Tally.Shelf) > 0 && waitingStand == null) Log($"선반에서 꺼냄 ×{Count(WardrobeCost.Tally.Shelf)}");
            changed = true;
        }
        if (changed) Show();
    }

    private void Log(string what)
    {
        if (events.Count < MostEvents) events.Add($"{elapsed,7:F1}s  {what}");
    }

    private void Show()
    {
        long stands = countedAll[(int)WardrobeCost.Tally.Stands], cloths = ranAll[(int)WardrobeCost.Part.Cloth];
        lines.Clear();
        lines.Add($"옷장 비용 /wardrobe · 세움 {stands} · 선반에서 꺼냄 {countedAll[(int)WardrobeCost.Tally.Shelf]} · 천 걸기 {cloths}");
        if (lastStand.Length > 0) lines.Add("방금 " + lastStand);
        if (lastCloth.Length > 0) lines.Add("방금 " + lastCloth);
        if (lastDress.Length > 0) lines.Add("방금 " + lastDress);
        if (stands > 0)
            lines.Add($"세움 평균 {total[(int)WardrobeCost.Part.Stand] / stands:F1} ms · 가장 오래 {worstStand:F1} ms"
                + (cloths > 0 ? $" · 천 평균 {total[(int)WardrobeCost.Part.Cloth] / cloths:F1} ms, 가장 오래 {worstCloth:F1} ms" : "")
                + $" · 가장 느린 프레임 {worstFrame:F0} ms");
    }

    private string Result()
    {
        long stands = countedAll[(int)WardrobeCost.Tally.Stands], cloths = ranAll[(int)WardrobeCost.Part.Cloth];
        if (stands <= 0 && cloths <= 0 && ranAll[(int)WardrobeCost.Part.Dress] <= 0) return "측정한 것이 없습니다(옷을 입혀 본 적이 없음).";
        double Each(WardrobeCost.Part part) => total[(int)part] / Math.Max(1L, stands);
        text.Clear();
        text.Append($"옷장 측정 {elapsed:F0}초: 세움 {stands}번(선반에서 꺼냄 {countedAll[(int)WardrobeCost.Tally.Shelf]}번), 가장 느린 프레임 {worstFrame:F0} ms / ");
        if (stands > 0)
        {
            double rest = Each(WardrobeCost.Part.Stand) - Each(WardrobeCost.Part.Model) - Each(WardrobeCost.Part.Dress) - Each(WardrobeCost.Part.Sole)
                - Each(WardrobeCost.Part.Face);
            text.Append($"세움 평균 {Each(WardrobeCost.Part.Stand):F1} ms(가장 오래 {worstStand:F1}) = 모델 {Each(WardrobeCost.Part.Model):F1} + 입히기 "
                + $"{Each(WardrobeCost.Part.Dress):F1}(붙이기 {Each(WardrobeCost.Part.Place):F1}, 체형 키 {Each(WardrobeCost.Part.Follow):F1}, 가림 "
                + $"{Each(WardrobeCost.Part.Cover):F1}, 겹침 맞춤 {Each(WardrobeCost.Part.Fit):F1}) + 발바닥 {Each(WardrobeCost.Part.Sole):F1} + 얼굴 "
                + $"{Each(WardrobeCost.Part.Face):F1} + 그 밖 {rest:F1} / ");
        }
        text.Append($"메시 쌍: 처음 잼 {countedAll[(int)WardrobeCost.Tally.Measured]}개에 모두 {total[(int)WardrobeCost.Part.Measure]:F0} ms, 기억해 둔 것 "
            + $"{countedAll[(int)WardrobeCost.Tally.Remembered]}개, 만든 메시 {countedAll[(int)WardrobeCost.Tally.Meshes]}, 만든 키 {countedAll[(int)WardrobeCost.Tally.Keys]}");
        if (cloths > 0)
            text.Append($" / 천 걸기 {cloths}번 평균 {total[(int)WardrobeCost.Part.Cloth] / cloths:F1} ms(가장 오래 {worstCloth:F1}), 한 번에 "
                + $"{countedAll[(int)WardrobeCost.Tally.ClothParts] / (double)cloths:F0}조각");
        return text.ToString();
    }

    // The editor keeps the answer and every event beside the other logs, to be read later without a console.
    private void Keep(string result)
    {
        if (!Application.isEditor) return;
        try
        {
            string folder = Path.Combine(Application.dataPath, "..", "Logs");
            Directory.CreateDirectory(folder);
            var all = new StringBuilder();
            all.AppendLine(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + Application.version);
            all.AppendLine(result);
            all.AppendLine();
            foreach (string line in events) all.AppendLine(line);
            File.WriteAllText(Path.Combine(folder, "wardrobe-cost.txt"), all.ToString(), new UTF8Encoding(false));
        }
        catch (Exception error)
        {
            Debug.LogWarning("[WardrobeCost] Logs/wardrobe-cost.txt: " + error.Message);
        }
    }

    private void OnGUI()
    {
        if (lines.Count == 0) return;
        GUILayout.BeginArea(new Rect(Margin, Screen.height * .32f, Width, LineHeight * (lines.Count * 2 + 1)), GUI.skin.box);
        foreach (string line in lines) GUILayout.Label(line);
        GUILayout.EndArea();
    }
}
