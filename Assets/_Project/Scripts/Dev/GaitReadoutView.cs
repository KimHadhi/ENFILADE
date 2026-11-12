using System.Text;
using UnityEngine;

/// <summary>
/// Console /gait: a box with how this PC's player runs right now (PlayerBody.GaitReadout: the speed, how fast the playing
/// cycles' feet carry the body at their own pace, the pace and the stride's stretch against theirs, steps a minute, any
/// slide left and the cycles playing), and, when it is turned off again, the averages by speed, copied for pasting back.
/// Made for the user's test of the run (2026-11-12): a slide of more than a few tenths of a m/s, or steps a minute far from
/// a person's (about 180 at 5 m/s, 220 sprinting), says which clip or table number to change.
/// </summary>
internal sealed class GaitReadoutView : MonoBehaviour
{
    // Speed bands (m/s) for the averages: standing to walking, jogging, running, sprinting, flat out.
    private static readonly float[] Bands = { 2f, 4f, 6f, 8f, float.PositiveInfinity };
    private const float Width = 820f, Margin = 12f, LineHeight = 20f;

    private PlayerBody body;
    private string text = "";
    private readonly float[] seconds = new float[Bands.Length], steps = new float[Bands.Length],
        stretch = new float[Bands.Length], slide = new float[Bands.Length];

    /// <summary>Shows the box for <paramref name="player"/>, or, if it is up, takes it down and returns the averages.</summary>
    internal static string Toggle(PlayerMovement player)
    {
        if (player == null) return "플레이어가 없습니다.";
        if (player.TryGetComponent(out GaitReadoutView shown))
        {
            string result = shown.Result();
            Destroy(shown);
            GUIUtility.systemCopyBuffer = result;
            return result + " (복사됨)";
        }
        if (!player.TryGetComponent(out PlayerBody body)) return "몸이 없습니다.";
        player.gameObject.AddComponent<GaitReadoutView>().body = body;
        return "걸음 수치를 켰습니다. 걷고 뛴 뒤 /gait를 다시 입력하면 속도별 평균이 나옵니다.";
    }

    // After every Update, so this frame's run (PlayerBody's, in its Update) is the one read.
    private void LateUpdate()
    {
        if (body == null) { Destroy(this); return; }
        text = body.GaitReadout();
        if (!body.TryGaitNow(out float speed, out float stepsNow, out float stretchNow, out float slideNow)) return;
        int band = 0;
        while (band < Bands.Length - 1 && speed >= Bands[band]) band++;
        float dt = Time.deltaTime;
        seconds[band] += dt;
        steps[band] += stepsNow * dt;
        stretch[band] += stretchNow * dt;
        slide[band] += slideNow * dt;
    }

    private string Result()
    {
        var result = new StringBuilder("걸음 평균");
        float from = 0f;
        for (int i = 0; i < Bands.Length; i++)
        {
            float time = seconds[i];
            string range = float.IsPositiveInfinity(Bands[i]) ? $"{from:0}+" : $"{from:0}-{Bands[i]:0}";
            from = Bands[i];
            if (time < .2f) continue;
            result.Append($" / {range} m/s {time:0.0}초: 분당 {steps[i] / time:0}걸음, 보폭 ×{stretch[i] / time:0.00}, 미끄러짐 {slide[i] / time:0.00} m/s");
        }
        return result.ToString();
    }

    private void OnGUI()
    {
        if (string.IsNullOrEmpty(text)) return;
        int lines = 1;
        foreach (char c in text) if (c == '\n') lines++;
        GUILayout.BeginArea(new Rect(Margin, Screen.height * .35f, Width, LineHeight * (lines + 1)), GUI.skin.box);
        foreach (string line in text.Split('\n')) GUILayout.Label(line);
        GUILayout.EndArea();
    }
}
