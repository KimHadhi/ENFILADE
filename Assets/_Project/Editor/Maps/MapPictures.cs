using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// ENFILADE/맵/맵 사진 찍기: opens every map in Tables/Maps that is a scene in the project, takes a 16:9 picture of it
/// and saves it as Resources/UI/Maps/&lt;id&gt;.png for the title screen's map card (MainMenuMapCard). The camera stands
/// at an object named "MapShot" if the scene has one (place and aim it by hand for the best view); otherwise behind and
/// above the player start, looking the way it faces; otherwise from a corner over everything. Scenes open one after
/// another, and the scenes that were open come back afterwards. Any image can replace a picture by hand.
/// </summary>
public static class MapPictures
{
    private const int Width = 1280, Height = 720;
    private const string Folder = "Assets/_Project/Resources/UI/Maps", ShotName = "MapShot";

    [MenuItem("ENFILADE/맵/맵 사진 찍기")]
    private static void TakeAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogWarning("[맵 사진] 플레이 중에는 찍을 수 없어요.");
            return;
        }
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        var setup = EditorSceneManager.GetSceneManagerSetup();
        Directory.CreateDirectory(Folder);
        int taken = 0;
        try
        {
            var ids = MapTable.All();
            for (int i = 0; i < ids.Length; i++)
            {
                string id = ids[i];
                EditorUtility.DisplayProgressBar("맵 사진 찍기", id, (float)i / Mathf.Max(1, ids.Length));
                string path = ScenePath(id);
                if (path == null)
                {
                    Debug.LogWarning($"[맵 사진] '{id}' 씬을 찾지 못했어요.");
                    continue;
                }
                EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                if (Shoot(id)) taken++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            if (CanRestore(setup)) EditorSceneManager.RestoreSceneManagerSetup(setup);
            AssetDatabase.Refresh();
        }
        Debug.Log($"[맵 사진] {taken}장 찍어서 {Folder} 에 저장했어요.");
    }

    // An untitled scene that was never saved can't be opened again.
    private static bool CanRestore(SceneSetup[] setup)
    {
        if (setup == null || setup.Length == 0) return false;
        foreach (var scene in setup) if (string.IsNullOrEmpty(scene.path)) return false;
        return true;
    }

    private static string ScenePath(string id)
    {
        foreach (var scene in EditorBuildSettings.scenes)
            if (Path.GetFileNameWithoutExtension(scene.path) == id) return scene.path;
        foreach (string guid in AssetDatabase.FindAssets("t:Scene " + id))
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileNameWithoutExtension(path) == id) return path;
        }
        return null;
    }

    private static bool Shoot(string id)
    {
        var holder = new GameObject("Map Picture Camera") { hideFlags = HideFlags.HideAndDontSave };
        var eye = holder.AddComponent<Camera>();
        RenderTexture target = null;
        Texture2D picture = null;
        var previous = RenderTexture.active;
        try
        {
            Place(eye.transform);
            eye.fieldOfView = 55f;
            eye.nearClipPlane = .1f;
            eye.farClipPlane = 1500f;
            // Everything in the frame, whatever the scene's occlusion bake says: from the ENFILADE tower's shot on its
            // square the bake hid every static thing and the picture was sky with the floor numbers floating in it (11-12).
            // A picture taken once costs nothing to draw in full.
            eye.useOcclusionCulling = false;
            // The scene's own sky if it has one, else the menus' night.
            eye.clearFlags = RenderSettings.skybox != null ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
            eye.backgroundColor = KitStyle.Night;
            target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
            eye.targetTexture = target;
            eye.Render();
            RenderTexture.active = target;
            picture = new Texture2D(Width, Height, TextureFormat.RGB24, false);
            picture.ReadPixels(new Rect(0f, 0f, Width, Height), 0, 0);
            picture.Apply();
            File.WriteAllBytes(Path.Combine(Folder, id + ".png"), picture.EncodeToPNG());
            return true;
        }
        catch (System.Exception problem)
        {
            Debug.LogWarning($"[맵 사진] '{id}' 사진을 찍지 못했어요: {problem.Message}");
            return false;
        }
        finally
        {
            RenderTexture.active = previous;
            eye.targetTexture = null;
            if (target != null) Object.DestroyImmediate(target);
            if (picture != null) Object.DestroyImmediate(picture);
            Object.DestroyImmediate(holder);
        }
    }

    // At the scene's MapShot if it has one; else 7 m behind and 5 m above the player start, looking 25 m ahead of it;
    // else from a high corner down across everything.
    private static void Place(Transform eye)
    {
        var shot = GameObject.Find(ShotName);
        if (shot != null)
        {
            eye.SetPositionAndRotation(shot.transform.position, shot.transform.rotation);
            return;
        }
        var start = Object.FindAnyObjectByType<PlayerSpawnPoint>();
        if (start != null)
        {
            var at = start.transform;
            var ahead = Vector3.ProjectOnPlane(at.forward, Vector3.up);
            ahead = ahead.sqrMagnitude > .001f ? ahead.normalized : Vector3.forward;
            eye.position = at.position - ahead * 7f + Vector3.up * 5f;
            eye.rotation = Quaternion.LookRotation(at.position + ahead * 25f - eye.position);
            return;
        }
        bool any = false;
        var bounds = new Bounds();
        foreach (var renderer in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
        {
            if (!any) { bounds = renderer.bounds; any = true; }
            else bounds.Encapsulate(renderer.bounds);
        }
        if (!any)
        {
            eye.SetPositionAndRotation(new Vector3(0f, 10f, -10f), Quaternion.Euler(35f, 0f, 0f));
            return;
        }
        eye.position = bounds.center + new Vector3(-bounds.extents.x, bounds.extents.y + 10f, -bounds.extents.z);
        eye.rotation = Quaternion.LookRotation(bounds.center - eye.position);
    }
}
