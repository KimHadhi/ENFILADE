using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class WeaponFoundationBuilder
{
    [MenuItem("Tools/Combat/Create Foundation Test Scene")]
    public static void Create()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Exit Play Mode first.");
        EnsureFolder("Assets/Combat"); EnsureFolder("Assets/Combat/Weapons"); EnsureFolder("Assets/Scenes");
        var automatic = Data("Assets/Combat/Weapons/FoundationRifle.asset", "Foundation Rifle", WeaponFireMode.Automatic, 20f, 600f, 30, 1.5f);
        Data("Assets/Combat/Weapons/FoundationPistol.asset", "Foundation Pistol", WeaponFireMode.SemiAutomatic, 35f, 300f, 12, 1.2f);
        Scene previous = SceneManager.GetActiveScene();
        if (Application.isBatchMode && string.IsNullOrEmpty(previous.path)) EditorSceneManager.SaveScene(previous, "Assets/CombatCheckHost.unity");
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            var sun = new GameObject("Sun").AddComponent<Light>(); sun.type = LightType.Directional;
            sun.transform.rotation = Quaternion.Euler(45f, -25f, 0f);
            Box("Floor", new Vector3(0f, -0.2f, 10f), new Vector3(30f, 0.4f, 40f));
            Target("Target 5m", new Vector3(0f, 1f, 5f));
            Target("Target 10m", new Vector3(4f, 1f, 10f));
            Target("Blocked target", new Vector3(-4f, 1f, 10f));
            Box("Cover - bullets must stop here", new Vector3(-4f, 1.4f, 7f), new Vector3(2f, 2.8f, 0.4f));
            var player = new GameObject("Player - invisible weapon"); player.tag = "Player"; player.layer = 31;
            player.transform.position = Vector3.up * 0.05f;
            var cc = player.AddComponent<CharacterController>(); cc.height = 1.75f; cc.radius = 0.3f; cc.skinWidth = 0.03f;
            Transform crouch = Child(player.transform, "CrouchPivot", Vector3.up * 1.6f);
            Transform bob = Child(crouch, "BobPivot", Vector3.zero);
            Transform look = Child(bob, "LookPivot", Vector3.zero);
            var camera = Child(look, "Camera", Vector3.zero).gameObject;
            camera.tag = "MainCamera"; camera.AddComponent<Camera>().nearClipPlane = 0.03f; camera.AddComponent<AudioListener>();
            player.AddComponent<PlayerMovement>(); player.AddComponent<PlayerLook>(); player.AddComponent<PlayerCameraEffects>();
            Install(player, automatic); player.AddComponent<WeaponDebugHUD>();
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/Scenes/CombatFoundation.unity");
            EditorSceneManager.SaveScene(scene, path); AssetDatabase.SaveAssets();
            Debug.Log("COMBAT_FOUNDATION_CREATED: " + path);
        }
        finally { if (previous.IsValid()) SceneManager.SetActiveScene(previous); EditorSceneManager.CloseScene(scene, true); }
    }

    [MenuItem("Tools/Combat/Add Foundation Weapon To Selected Player")]
    public static void InstallSelected()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Exit Play Mode first.");
        var player = Selection.activeGameObject;
        if (player == null || player.GetComponent<PlayerMovement>() == null)
            throw new System.InvalidOperationException("Select the PlayerMovement object first.");
        EnsureFolder("Assets/Combat"); EnsureFolder("Assets/Combat/Weapons");
        var data = Data("Assets/Combat/Weapons/FoundationRifle.asset", "Foundation Rifle", WeaponFireMode.Automatic, 20, 600, 30, 1.5f);
        Undo.RecordObject(player, "Add foundation weapon"); Install(player, data);
        EditorSceneManager.MarkSceneDirty(player.scene);
    }

    private static void Install(GameObject player, WeaponData data)
    {
        if (player.GetComponent<PlayerWeaponInput>() == null) Undo.AddComponent<PlayerWeaponInput>(player);
        var controller = player.GetComponent<WeaponController>();
        if (controller == null) controller = Undo.AddComponent<WeaponController>(player);
        var serialized = new SerializedObject(controller);
        if (serialized.FindProperty("weaponData").objectReferenceValue == null)
        { serialized.FindProperty("weaponData").objectReferenceValue = data; serialized.ApplyModifiedProperties(); }
    }

    private static WeaponData Data(string path, string label, WeaponFireMode mode, float damage, float rpm, int magazine, float reload)
    {
        var data = AssetDatabase.LoadAssetAtPath<WeaponData>(path); if (data != null) return data;
        data = ScriptableObject.CreateInstance<WeaponData>();
        var so = new SerializedObject(data);
        so.FindProperty("displayName").stringValue = label; so.FindProperty("fireMode").enumValueIndex = (int)mode;
        so.FindProperty("damage").floatValue = damage; so.FindProperty("roundsPerMinute").floatValue = rpm;
        so.FindProperty("magazineSize").intValue = magazine; so.FindProperty("reloadDuration").floatValue = reload;
        so.ApplyModifiedPropertiesWithoutUndo(); AssetDatabase.CreateAsset(data, path); return data;
    }
    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        int slash = path.LastIndexOf('/'); AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
    }
    private static GameObject Box(string name, Vector3 position, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.position = position; go.transform.localScale = size;
        var mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/MovementPlayground.mat");
        if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
        return go;
    }
    private static void Target(string name, Vector3 position) => Box(name, position, new Vector3(0.8f, 2f, 0.5f)).AddComponent<WeaponTrainingTarget>();
    private static Transform Child(Transform parent, string name, Vector3 position)
    { var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = position; return go.transform; }
    public static void ValidateAndBuild() { MovementAbilityChecks.RunAll(); WeaponFoundationChecks.Run(); Create(); }
}
