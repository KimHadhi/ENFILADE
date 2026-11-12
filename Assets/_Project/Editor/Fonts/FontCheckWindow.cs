#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

/// <summary>The window ENFILADE → 글꼴 → 글꼴 검사 opens: the report, and buttons to check again, fix roles and copy it.</summary>
public sealed class FontCheckWindow : EditorWindow
{
    [SerializeField] private string report;
    private Vector2 scroll;
    private GUIStyle style;

    public static void Open(string report)
    {
        var window = GetWindow<FontCheckWindow>("글꼴 검사");
        window.report = report;
        window.minSize = new Vector2(520f, 360f);
        window.Show();
    }

    private void OnGUI()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("다시 검사")) report = FontTools.BuildReport();
            if (GUILayout.Button("역할 맞추기")) { FontTools.SyncRoles(); report = FontTools.BuildReport(); }
            if (GUILayout.Button("보고서 복사")) EditorGUIUtility.systemCopyBuffer = report ?? "";
        }
        style ??= new GUIStyle(EditorStyles.label) { wordWrap = true, richText = false };
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.SelectableLabel(report ?? "ENFILADE → 글꼴 → 글꼴 검사를 누르세요.", style,
            GUILayout.ExpandHeight(true), GUILayout.MinHeight(style.CalcHeight(new GUIContent(report ?? ""), position.width - 24f)));
        EditorGUILayout.EndScrollView();
    }
}
#endif
