using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Creates a separate practice scene without editing the user's game scene.</summary>
public static class MovementPlaygroundBuilder
{
    [MenuItem("Tools/Movement/Create Ability Playground")]
    public static void Create()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Exit Play Mode first.");
        Scene previous = SceneManager.GetActiveScene();
        if (Application.isBatchMode && string.IsNullOrEmpty(previous.path)) EditorSceneManager.SaveScene(previous, ProjectAssetPaths.DevelopmentScene("MovementCheckHost.unity"));
        Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        try
        {
            var light = new GameObject("Sun").AddComponent<Light>(); light.type = LightType.Directional; light.intensity = 1.2f;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            Block("Floor", new Vector3(0f, -0.2f, 10f), new Vector3(40f, 0.4f, 40f));
            Block("Step 0.15", new Vector3(-8f, 0.075f, 3f), new Vector3(3f, 0.15f, 1f));
            Block("Step 0.30", new Vector3(-8f, 0.15f, 4f), new Vector3(3f, 0.3f, 1f));
            Block("Step 0.45", new Vector3(-8f, 0.225f, 5f), new Vector3(3f, 0.45f, 1f));
            Block("Vault rail", new Vector3(0f, 0.45f, 5f), new Vector3(4f, 0.9f, 0.45f));
            Block("Mantle ledge", new Vector3(0f, 0.8f, 10f), new Vector3(5f, 1.6f, 3f));
            Block("Wall climb lane", new Vector3(7f, 2.5f, 10f), new Vector3(0.5f, 5f, 15f));
            Block("Wall climb finish", new Vector3(-7f, 1.5f, 13f), new Vector3(4f, 3f, 2f));
            Block("Swing launch platform", new Vector3(0f, 1f, 15f), new Vector3(4f, 2f, 3f));
            for (int i = 0; i < 3; i++)
            {
                var bar = new GameObject("Swing Bar " + (i + 1)); bar.transform.position = new Vector3(0f, 4.6f, 18f + i * 3.6f);
                bar.AddComponent<SwingBar>();
                var rail = Block("Rail", bar.transform.position, new Vector3(2.5f, 0.12f, 0.12f)); rail.transform.SetParent(bar.transform, true);
            }
            Block("Roll drop platform", new Vector3(-12f, 2.5f, 10f), new Vector3(4f, 5f, 4f));
            var target = Block("Shove and tackle dummy", new Vector3(4f, 0.75f, 3f), new Vector3(0.7f, 1.5f, 0.7f));
            target.AddComponent<Rigidbody>().mass = 1.5f; target.AddComponent<MovementTrainingTarget>();
            var turret = Block("Dodge trainer - yellow means incoming attack", new Vector3(-4f, 1f, 0f), Vector3.one * 0.6f);
            turret.AddComponent<MovementTrainingAttacker>();
            var player = new GameObject("Player"); player.layer = 31; player.tag = "Player";
            player.transform.position = new Vector3(0f, 0.05f, 0f);
            var cc = player.AddComponent<CharacterController>(); cc.height = 1.75f; cc.radius = 0.3f; cc.skinWidth = 0.03f;
            Transform crouch = Child(player.transform, "CrouchPivot", new Vector3(0f, 1.6f, 0f));
            Transform bob = Child(crouch, "BobPivot", Vector3.zero);
            Transform look = Child(bob, "LookPivot", Vector3.zero);
            Transform camera = Child(look, "Camera", Vector3.zero);
            camera.gameObject.AddComponent<Camera>().nearClipPlane = 0.03f; camera.gameObject.tag = "MainCamera";
            camera.gameObject.AddComponent<AudioListener>();
            player.AddComponent<PlayerMovement>(); player.AddComponent<PlayerLook>(); player.AddComponent<PlayerCameraEffects>(); player.AddComponent<MovementDebugHUD>();
            ProjectAssetPaths.EnsureFolder("Assets/_Project/Scenes/Development");
            string path = AssetDatabase.GenerateUniqueAssetPath("Assets/_Project/Scenes/Development/MovementAbilityPlayground.unity");
            EditorSceneManager.SaveScene(scene, path);
            Debug.Log("MOVEMENT_PLAYGROUND_CREATED: " + path);
        }
        finally { if (previous.IsValid()) SceneManager.SetActiveScene(previous); EditorSceneManager.CloseScene(scene, true); }
    }
    private static GameObject Block(string name, Vector3 position, Vector3 size)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube); go.name = name; go.transform.position = position; go.transform.localScale = size;
        var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Art/Materials/MovementPlayground.mat");
        if (material != null) go.GetComponent<Renderer>().sharedMaterial = material;
        return go;
    }
    private static Transform Child(Transform parent, string name, Vector3 position)
    { var go = new GameObject(name); go.transform.SetParent(parent, false); go.transform.localPosition = position; return go.transform; }
    public static void ValidateAndBuild() { MovementAbilityChecks.RunAll(); Create(); }
}
