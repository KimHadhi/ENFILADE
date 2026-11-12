using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// ENFILADE > HUD. One prefab, GameHUD, is the HUD of every play scene; everything else on screen (kill feed, score popup,
/// team panel, Tab screen, settings) builds itself in any scene. "현재 씬에 HUD 넣기·갱신" first lays the prefab's mission
/// bar out from Hud.csv, so the editor shows what Play will, and gives any part saved without a CanvasRenderer its
/// renderer (such a part vanishes in Play), then makes the open scene carry exactly that prefab: adds
/// it when missing, swaps a copy that lost its prefab link for the prefab, and clears the layout leftovers a scene
/// piles on its instance while keeping the scene's own references and names.
/// </summary>
public static class HudInstaller
{
    private const string PrefabPath = "Assets/_Project/Prefabs/UI/HUD/GameHUD.prefab";
    private const string InstanceName = "★HUD(Canvas)";
    private const string Title = "HUD";

    [MenuItem("ENFILADE/HUD/현재 씬에 HUD 넣기·갱신")]
    private static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Tell("Play를 멈춘 뒤에 실행해 주세요."); return; }
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null) { Tell(PrefabPath + " 프리팹이 없습니다."); return; }
        var stage = PrefabStageUtility.GetCurrentPrefabStage();
        if (stage != null && stage.assetPath == PrefabPath && stage.scene.isDirty)
        {
            Tell("GameHUD 프리팹 편집 화면에 저장하지 않은 변경이 있습니다. 저장하거나 닫은 뒤에 실행해 주세요.");
            return;
        }

        var scene = SceneManager.GetActiveScene();
        var instances = new List<GameObject>();
        var strays = new List<GameObject>();
        foreach (var hud in Object.FindObjectsByType<MissionHUD>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            if (hud.gameObject.scene != scene) continue;
            var root = PrefabUtility.GetOutermostPrefabInstanceRoot(hud);
            var list = root != null && PrefabUtility.GetCorrespondingObjectFromOriginalSource(root) == prefab ? instances : strays;
            var top = list == instances ? root : TopCanvas(hud);
            if (!list.Contains(top)) list.Add(top);
        }
        if (instances.Count == 0 && strays.Count == 0
            && !EditorUtility.DisplayDialog(Title, $"'{scene.name}' 씬에 HUD가 없습니다. 넣을까요?", "넣기", "취소"))
            return;
        if (strays.Count > 0 && !EditorUtility.DisplayDialog(Title,
                $"프리팹과 연결이 끊긴 HUD가 {strays.Count}개 있습니다 ({string.Join(", ", strays.Select(s => s.name))}).\n"
                + "프리팹으로 바꿀까요? 그 HUD에 직접 고친 내용은 사라집니다.", "바꾸기", "그대로 두기"))
            strays.Clear();

        var report = new List<string>();
        report.Add(BakeMissionBar(out int repaired) ? "미션 바 모양을 Hud.csv 값으로 프리팹에 반영했습니다." : "프리팹에서 미션 바를 찾지 못했습니다.");
        if (repaired > 0) report.Add($"Play에서 안 보이던 UI 부품 {repaired}개에 CanvasRenderer를 붙였습니다.");
        foreach (var stray in strays)
            instances.Add(Place(prefab, scene, stray.transform.parent, stray.transform.GetSiblingIndex(), stray));
        if (strays.Count > 0) report.Add($"연결이 끊긴 HUD {strays.Count}개를 프리팹으로 바꿨습니다.");
        if (instances.Count == 0)
        {
            instances.Add(Place(prefab, scene, null, -1, null));
            report.Add("HUD를 새로 넣었습니다.");
        }
        int cleared = instances.Sum(ClearLeftovers);
        report.Add(cleared > 0 ? $"씬에 남아 있던 배치 찌꺼기 {cleared}개를 지워 프리팹과 똑같이 맞췄습니다." : "씬의 HUD는 이미 프리팹과 똑같습니다.");
        if (instances.Count > 1) report.Add($"이 씬에 HUD가 {instances.Count}개 있습니다. 하나만 남겨 주세요.");

        EditorSceneManager.MarkSceneDirty(scene);
        Selection.activeGameObject = instances[0];
        EditorGUIUtility.PingObject(instances[0]);
        report.Add("씬을 저장해야(Ctrl+S) 남습니다.");
        Tell(string.Join("\n", report));
    }

    [MenuItem("ENFILADE/HUD/HUD 프리팹 열기")]
    private static void OpenPrefab()
    {
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab != null) AssetDatabase.OpenAsset(prefab);
        else Tell(PrefabPath + " 프리팹이 없습니다.");
    }

    // The mission bar is shaped by code from Hud.csv; laying it out here keeps the prefab looking the way Play will.
    // A Graphic saved without its CanvasRenderer still shows in the editor, which adds one on the fly, but vanished in
    // Play once HudCamera drew the canvas (the mission bar's orange ! tab), so any such part gets one for good.
    private static bool BakeMissionBar(out int repaired)
    {
        var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
        try
        {
            repaired = 0;
            foreach (var graphic in contents.GetComponentsInChildren<Graphic>(true))
                if (!graphic.TryGetComponent(out CanvasRenderer _)) { graphic.gameObject.AddComponent<CanvasRenderer>(); repaired++; }
            var huds = contents.GetComponentsInChildren<MissionHUD>(true);
            foreach (var hud in huds) hud.LayOutForEditor();
            if (huds.Length > 0 || repaired > 0) PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
            return huds.Length > 0;
        }
        finally { PrefabUtility.UnloadPrefabContents(contents); }
    }

    private static GameObject Place(GameObject prefab, Scene scene, Transform parent, int sibling, GameObject replaces)
    {
        var hud = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
        hud.name = InstanceName;
        Undo.RegisterCreatedObjectUndo(hud, "HUD 넣기");
        if (parent != null) Undo.SetTransformParent(hud.transform, parent, "HUD 넣기");
        if (sibling >= 0) hud.transform.SetSiblingIndex(sibling);
        if (replaces != null) Undo.DestroyObjectImmediate(replaces);
        return hud;
    }

    // Placement and size overrides on the HUD's own parts are leftovers from layout passes, not design: the prefab
    // decides those. Links to the scene's objects, names and the root placement Unity always keeps stay.
    private static int ClearLeftovers(GameObject root)
    {
        var mods = PrefabUtility.GetPropertyModifications(root);
        if (mods == null) return 0;
        var keep = mods.Where(mod => !(mod.target is Transform) || PrefabUtility.IsDefaultOverride(mod)).ToArray();
        if (keep.Length == mods.Length) return 0;
        Undo.RegisterCompleteObjectUndo(PrefabUtility.GetPrefabInstanceHandle(root), "HUD 갱신");
        PrefabUtility.SetPropertyModifications(root, keep);
        return mods.Length - keep.Length;
    }

    // A HUD copy that lost its prefab link: the outermost canvas holding it.
    private static GameObject TopCanvas(Component part)
    {
        GameObject top = part.gameObject;
        for (var t = part.transform; t != null; t = t.parent)
            if (t.GetComponent<Canvas>() != null) top = t.gameObject;
        return top;
    }

    private static void Tell(string message) => EditorUtility.DisplayDialog(Title, message, "확인");
}
