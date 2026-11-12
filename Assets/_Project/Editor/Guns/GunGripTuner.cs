using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Grip Tuner (Unity menu ENFILADE → 총 → Grip Tuner), 2026-11-11: shape a gun's hands while looking at them, for the user's
/// gun-hand fixes (g-seven grip shapes, g-fingers thumbs). In Play mode, with a gun in hand, each finger's three joints and
/// the thumb's turn under the palm have a slider per hand, and the hand changes on the spot (GunGrips.SetLive) on whatever
/// character is worn, in the real first-person view with the game's own IK and fitting. Save writes the gun's rows into
/// Tables/GunGrips.csv, where they hold for any humanoid (they are HandGrip's own angles, measured on each hand); Reset
/// drops them and the hands go back to the shared shapes. Chosen over a Blender tool for grips: Blender can't show what
/// the game's runtime does to the hands, the game view can.
/// </summary>
internal sealed class GunGripTuner : EditorWindow
{
    private static string TablePath => TablePaths.Of("GunGrips");
    private static readonly string[] FingerNames = { "검지", "중지", "약지", "새끼", "엄지" };
    private static readonly string[] JointNames = { "뿌리", "가운데", "끝" };

    private string gun;
    private bool pistol, leftHand;
    private Vector2 scroll;
    private readonly GunGrips.Grip[] editing = new GunGrips.Grip[2];
    private readonly bool[] touched = new bool[2];

    [MenuItem("ENFILADE/총/Grip Tuner")]
    private static void Open() => GetWindow<GunGripTuner>("총 손 모양");

    // Play mode moves on underneath: show the gun the player holds now.
    private void OnInspectorUpdate()
    {
        if (Application.isPlaying) Repaint();
    }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox("Play 중에 총을 든 채로 슬라이더를 움직이면 손이 바로 바뀝니다. 저장하면 Tables/GunGrips.csv에 남고, " +
                                "어느 캐릭터든 같은 모양으로 잡습니다. 왼손은 손잡이 부착물이 없을 때만 씁니다.", MessageType.Info);
        if (!Application.isPlaying)
        {
            EditorGUILayout.LabelField("Play를 누르고 총을 드세요 (채팅 /gun 이름).");
            ListSaved();
            return;
        }
        if (!GunGrips.Holding(out string held, out bool heldPistol))
        {
            EditorGUILayout.LabelField("총을 든 플레이어가 없습니다. 채팅에 /gun 이름 으로 총을 드세요.");
            return;
        }
        if (held != gun) Begin(held, heldPistol);

        EditorGUILayout.LabelField("총", gun + (pistol ? "  (권총)" : ""), EditorStyles.boldLabel);
        int side = GUILayout.Toolbar(leftHand ? 0 : 1, new[] { "왼손 (받치는 손)", "오른손 (방아쇠 손)" }) == 0 ? 0 : 1;
        leftHand = side == 0;
        var grip = editing[side];

        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUI.BeginChangeCheck();
        for (int f = 0; f < FingerNames.Length; f++)
        {
            EditorGUILayout.Space(2);
            EditorGUILayout.LabelField(FingerNames[f], EditorStyles.miniBoldLabel);
            Vector3 bend = grip.Bends[f];
            for (int j = 0; j < 3; j++)
                bend[j] = EditorGUILayout.Slider("  " + JointNames[j] + " 굽힘", bend[j], -30f, 120f);
            grip.Bends[f] = bend;
        }
        EditorGUILayout.Space(2);
        grip.ThumbUnder = EditorGUILayout.Slider("엄지 손바닥 쪽으로", grip.ThumbUnder, -60f, 90f);
        if (EditorGUI.EndChangeCheck())
        {
            touched[side] = true;
            GunGrips.SetLive(gun, leftHand, grip);
        }
        EditorGUILayout.EndScrollView();

        EditorGUILayout.Space(4);
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("이 손을 처음 모양으로"))
            {
                editing[side] = GunGrips.Default(leftHand, pistol);
                touched[side] = true;
                GunGrips.SetLive(gun, leftHand, editing[side]);
            }
            if (GUILayout.Button("표 값으로 되돌리기"))
            {
                GunGrips.ClearLive(gun);
                Begin(gun, pistol);
            }
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            GUI.enabled = touched[0] || touched[1];
            if (GUILayout.Button("이 총에 저장", GUILayout.Height(28)))
            {
                Save(gun, touched[0] ? editing[0] : null, touched[1] ? editing[1] : null, false);
                touched[0] = touched[1] = false;
            }
            GUI.enabled = true;
            if (GUILayout.Button("이 총 줄 지우기 (기본 모양)", GUILayout.Height(28))
                && EditorUtility.DisplayDialog("총 손 모양", gun + "의 저장된 손 모양을 지우고 기본 모양으로 돌아갈까요?", "지우기", "취소"))
            {
                Save(gun, null, null, true);
                GunGrips.ClearLive(gun);
                Begin(gun, pistol);
            }
        }
        EditorGUILayout.HelpBox("굽힘은 손바닥 쪽으로 접히는 각도(도)입니다. 엄지는 먼저 '손바닥 쪽으로'만큼 손바닥 밑으로 돌고 나서 굽습니다. " +
                                "다른 캐릭터로 바꿔서 같은 총을 들어 보면 같은 값으로 맞는지 볼 수 있습니다.", MessageType.None);
    }

    // The gun now held: start each hand from its saved row, else from the shared shape it wears today.
    private void Begin(string held, bool heldPistol)
    {
        gun = held;
        pistol = heldPistol;
        for (int side = 0; side < 2; side++)
        {
            bool left = side == 0;
            editing[side] = GunGrips.TryGet(gun, left, out var current) ? current.Copy() : GunGrips.Default(left, pistol);
            touched[side] = false;
        }
    }

    private static void ListSaved()
    {
        var ids = GameTables.RowIds(GunGrips.TableName).ToList();
        EditorGUILayout.Space(6);
        EditorGUILayout.LabelField("저장된 손 모양", ids.Count == 0 ? "없음 (모든 총이 기본 모양)" : string.Join(", ", ids));
    }

    // Writes (or with remove, drops) the gun's rows; the file keeps its header, its description row, its other rows and
    // its own line ending, UTF-8 with BOM as every table. Through TableFile, which writes over the file in place, since
    // Windows refuses File.WriteAllText's truncating write while any program has the file mapped (Win32 IO 1224, met on
    // Cloth.csv 2026-11-12), and makes a save the file is held against wait and land by itself.
    private static void Save(string gun, GunGrips.Grip left, GunGrips.Grip right, bool remove)
    {
        // Built on what TableFile will put there: a save still waiting, else the file without padding.
        string text;
        try
        {
            text = TableFile.Read(TablePath);
        }
        catch (System.Exception error) when (error is IOException || error is System.UnauthorizedAccessException)
        {
            Debug.LogWarning($"[GunGripTuner] {TablePath}를 읽지 못해 저장하지 않았습니다({error.Message}). 그 파일을 연 프로그램을 닫고 다시 저장해 주세요.");
            return;
        }
        string newline = text.Contains("\r\n") ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n").Split('\n').Where(l => l.Length > 0).ToList();
        if (lines.Count < 2) lines = new List<string> { Header(), "\"#설명\"" };
        var rows = new SortedDictionary<string, string>(System.StringComparer.Ordinal);
        foreach (string line in lines.Skip(2))
        {
            int comma = line.IndexOf(',');
            rows[comma < 0 ? line : line.Substring(0, comma)] = line;
        }
        string leftId = gun + "/L", rightId = gun + "/R";
        if (remove)
        {
            rows.Remove(leftId);
            rows.Remove(rightId);
        }
        if (left != null) rows[leftId] = Row(leftId, left);
        if (right != null) rows[rightId] = Row(rightId, right);
        var output = new StringBuilder();
        output.Append(lines[0]).Append(newline).Append(lines[1]).Append(newline);
        foreach (string row in rows.Values) output.Append(row).Append(newline);
        // TableFile imports the table and reloads GameTables once it lands.
        if (TableFile.Write(TablePath, output.ToString(), gun + " 손 모양", "Play 중에 ENFILADE → 총 → Grip Tuner에서 다시 저장해 주세요."))
            Debug.Log($"[GunGripTuner] {(remove ? "removed" : "saved")} {gun}'s grip in {TablePath}.");
        else if (TableFile.Waiting(TablePath))
            Debug.Log($"[GunGripTuner] 다른 프로그램이 {TablePath}를 잡고 있어서, {gun} 손 모양은 파일이 풀리는 대로 저절로 저장됩니다.");
    }

    private static string Header()
    {
        var header = new StringBuilder("id");
        for (int f = 0; f < GunGrips.Fingers.Length; f++)
            for (int j = 0; j < GunGrips.Joints.Length; j++)
                header.Append(',').Append(GunGrips.Column(f, j));
        return header.Append(",thumbUnder").ToString();
    }

    private static string Row(string id, GunGrips.Grip grip)
    {
        var row = new StringBuilder(id);
        for (int f = 0; f < GunGrips.Fingers.Length; f++)
            for (int j = 0; j < GunGrips.Joints.Length; j++)
                row.Append(',').Append(Round(grip.Bends[f][j]));
        return row.Append(',').Append(Round(grip.ThumbUnder)).ToString();
    }

    private static string Round(float value) => System.Math.Round(value, 1).ToString("0.#", CultureInfo.InvariantCulture);
}
