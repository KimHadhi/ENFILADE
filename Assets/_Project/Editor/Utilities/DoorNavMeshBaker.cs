using Unity.AI.Navigation;
using Unity.AI.Navigation.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// Tools > World > 문 길찾기 다시 굽기. Gives every BreakableDoor in the open scenes its carving NavMeshObstacle (the
/// surfaces leave objects with one out of the bake) and bakes each NavMeshSurface that already has a walk mesh again, so
/// the mesh runs through the doorways: enemies go round a door that stands and through one that broke. A surface with
/// no walk mesh yet is left alone, as it may be a spare copy. Save the scene afterwards.
/// </summary>
public static class DoorNavMeshBaker
{
    private const string Title = "문 길찾기";

    [MenuItem("Tools/World/문 길찾기 다시 굽기")]
    private static void Bake()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) { Tell("Play를 멈춘 뒤에 실행해 주세요."); return; }
        var doors = Object.FindObjectsByType<BreakableDoor>(FindObjectsInactive.Include, FindObjectsSortMode.None);
        int added = 0;
        foreach (var door in doors)
        {
            if (!door.TryGetComponent(out NavMeshObstacle obstacle))
            {
                obstacle = Undo.AddComponent<NavMeshObstacle>(door.gameObject);
                added++;
            }
            else Undo.RecordObject(obstacle, Title);
            door.ShapeObstacle(obstacle);
            EditorUtility.SetDirty(obstacle);
        }
        var surfaces = System.Array.FindAll(Object.FindObjectsByType<NavMeshSurface>(FindObjectsSortMode.None),
            surface => surface.navMeshData != null);
        if (surfaces.Length > 0) NavMeshAssetManager.instance.StartBakingSurfaces(surfaces);
        EditorSceneManager.MarkAllScenesDirty();
        Tell($"문 {doors.Length}개(장애물을 새로 단 문 {added}개), 길찾기 면 {surfaces.Length}개를 다시 굽습니다. 끝나면 씬을 저장해 주세요.");
    }

    private static void Tell(string message) => EditorUtility.DisplayDialog(Title, message, "확인");
}
