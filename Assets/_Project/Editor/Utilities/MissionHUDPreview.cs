using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Temporarily reveals existing scene UI so its children can be edited normally.</summary>
[InitializeOnLoad]
public static class MissionHUDPreview
{
    private sealed class State
    {
        public RectTransform Rect;
        public CanvasGroup Group;
        public bool Active;
        public float Alpha, X;
        public bool MovesX;
    }
    private static readonly Dictionary<int, State> states = new Dictionary<int, State>();
    public static bool IsPreviewing => states.Count > 0;

    static MissionHUDPreview()
    {
        AssemblyReloadEvents.beforeAssemblyReload += Stop;
        EditorApplication.quitting += Stop;
        EditorApplication.playModeStateChanged += state => { if (state == PlayModeStateChange.ExitingEditMode) Stop(); };
        EditorSceneManager.sceneSaving += BeforeSave;
        EditorSceneManager.sceneClosing += (scene, removing) => Stop();
        Undo.undoRedoPerformed += Stop;
        EditorApplication.update += ProcessRequest;
    }

    private static void ProcessRequest()
    {
        const string request = "Library/MissionHudPreview.request";
        if (!File.Exists(request) || EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode) return;
        File.Delete(request);
        MissionHUDPreviewWindow.Open();
        var hud = FindHUD();
        if (hud != null) { Show(hud, true, true); Selection.activeGameObject = hud.gameObject; }
        File.WriteAllText("Library/MissionHudPreview.result.txt", hud != null ? "PASS: board and alert revealed for editing. Scene not saved." : "FAILED: no MissionHUD in loaded scenes.");
    }

    public static MissionHUD FindHUD()
    {
        if (Selection.activeGameObject != null)
        {
            var selected = Selection.activeGameObject.GetComponentInParent<MissionHUD>(true);
            if (selected != null) return selected;
        }
        foreach (var hud in Object.FindObjectsByType<MissionHUD>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            if (hud.gameObject.scene.IsValid() && hud.gameObject.scene.isLoaded && !EditorSceneManager.IsPreviewScene(hud.gameObject.scene)) return hud;
        return null;
    }

    public static void Show(MissionHUD hud, bool board, bool alert)
    {
        if (hud == null || EditorApplication.isPlayingOrWillChangePlaymode || EditorUtility.IsPersistent(hud)) return;
        var so = new SerializedObject(hud);
        if (board) Reveal(so, "board", "boardGroup", true);
        if (alert) Reveal(so, "alert", "alertGroup", false);
        Canvas.ForceUpdateCanvases();
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
    }

    private static void Reveal(SerializedObject hud, string rectField, string groupField, bool move)
    {
        var rect = hud.FindProperty(rectField).objectReferenceValue as RectTransform;
        var group = hud.FindProperty(groupField).objectReferenceValue as CanvasGroup;
        if (rect == null || group == null) return;
        int id = rect.GetInstanceID();
        if (!states.ContainsKey(id)) states.Add(id, new State
        {
            Rect = rect, Group = group, Active = rect.gameObject.activeSelf,
            Alpha = group.alpha, X = rect.anchoredPosition.x, MovesX = move
        });
        // Only the properties used to hide the UI are temporary. Images, text and child layout stay editable.
        rect.gameObject.SetActive(true);
        group.alpha = 1;
        if (move) rect.anchoredPosition = new Vector2(24, rect.anchoredPosition.y);
    }

    public static void Stop()
    {
        foreach (var state in states.Values)
        {
            if (state.Rect == null) continue;
            if (state.MovesX) state.Rect.anchoredPosition = new Vector2(state.X, state.Rect.anchoredPosition.y);
            if (state.Group != null) state.Group.alpha = state.Alpha;
            state.Rect.gameObject.SetActive(state.Active);
        }
        states.Clear();
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
    }
    private static void BeforeSave(Scene scene, string path) { Stop(); }

    public static void Select(MissionHUD hud, string field)
    {
        if (hud == null) return;
        var rect = new SerializedObject(hud).FindProperty(field).objectReferenceValue as Component;
        if (rect == null) return;
        Selection.activeGameObject = rect.gameObject;
        EditorGUIUtility.PingObject(rect.gameObject);
    }
}

public sealed class MissionHUDPreviewWindow : EditorWindow
{
    [SerializeField] private MissionHUD hud;

    [MenuItem("Tools/HUD/Mission HUD Preview")]
    public static void Open()
    {
        var window = GetWindow<MissionHUDPreviewWindow>("Mission HUD Preview");
        window.minSize = new Vector2(360, 255);
        window.hud = MissionHUDPreview.FindHUD();
        window.Show();
    }
    private void OnGUI()
    {
        EditorGUILayout.LabelField("미션 HUD 편집 미리보기", EditorStyles.boldLabel);
        hud = (MissionHUD)EditorGUILayout.ObjectField("대상 HUD", hud, typeof(MissionHUD), true);
        EditorGUILayout.HelpBox("숨겨진 보드와 알림을 실행 없이 펼쳐서 편집합니다. 하위 오브젝트의 Image·TextMeshPro·RectTransform을 수정하세요. 저장·실행·스크립트 재컴파일 시 임시 표시만 해제되며, 디자인 수정은 유지됩니다.", MessageType.Info);
        DrawControls(hud);
    }

    public static void DrawControls(MissionHUD target)
    {
        using (new EditorGUI.DisabledScope(target == null || EditorApplication.isPlayingOrWillChangePlaymode))
        {
            if (GUILayout.Button("모두 표시", GUILayout.Height(28))) MissionHUDPreview.Show(target, true, true);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("미션 보드 표시")) MissionHUDPreview.Show(target, true, false);
                if (GUILayout.Button("중앙 알림 표시")) MissionHUDPreview.Show(target, false, true);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("미션바 선택")) MissionHUDPreview.Select(target, "missionBar");
                if (GUILayout.Button("보드 선택")) { MissionHUDPreview.Show(target, true, false); MissionHUDPreview.Select(target, "board"); }
                if (GUILayout.Button("알림 선택")) { MissionHUDPreview.Show(target, false, true); MissionHUDPreview.Select(target, "alert"); }
            }
        }
        using (new EditorGUI.DisabledScope(!MissionHUDPreview.IsPreviewing))
            if (GUILayout.Button("미리보기 종료")) MissionHUDPreview.Stop();
    }
}

[CustomEditor(typeof(MissionHUD))]
public sealed class MissionHUDInspector : Editor
{
    public override void OnInspectorGUI()
    {
        EditorGUILayout.LabelField("편집 미리보기", EditorStyles.boldLabel);
        MissionHUDPreviewWindow.DrawControls((MissionHUD)target);
        EditorGUILayout.Space();
        DrawDefaultInspector();
    }
}
