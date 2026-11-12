using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Turns a model under Art/Models into a physics prop prefab in Prefabs/&lt;Category&gt;: a root with Rigidbody and
/// PhysicsProp, the model as its child, and a convex MeshCollider on every mesh. A moving rigidbody needs convex
/// colliders; Unity wraps each mesh in a hull of at most 255 triangles, which fits rounded shapes such as fruit.
/// </summary>
internal static class PhysicsPropBuilder
{
    private const string PrefabsFolder = "Assets/_Project/Prefabs";
    private const string MenuPath = "Tools/Models/Create Physics Prop Prefab";
    private const string ContextPath = "Assets/ENFILADE/Create Physics Prop Prefab";
    private const float DefaultMass = 1f;

    [MenuItem(MenuPath, true)]
    [MenuItem(ContextPath, true)]
    private static bool CanCreate() => SelectedModels().Any();

    [MenuItem(MenuPath)]
    [MenuItem(ContextPath, false, 30)]
    private static void CreateSelected()
    {
        var prefabs = SelectedModels().Select(Create).Where(p => p != null).ToArray();
        if (prefabs.Length == 0) return;
        Selection.objects = prefabs;
        EditorGUIUtility.PingObject(prefabs[0]);
    }

    private static IEnumerable<GameObject> SelectedModels() => Selection.GetFiltered<GameObject>(SelectionMode.Assets)
        .Where(g => AssetDatabase.GetAssetPath(g).StartsWith(ModelImportRules.ModelsFolder)
            && AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(g)) is ModelImporter);

    private static GameObject Create(GameObject model)
    {
        string category = AssetDatabase.GetAssetPath(model).Substring(ModelImportRules.ModelsFolder.Length).Split('/')[0];
        string path = $"{EnsureFolder(PrefabsFolder, category)}/{model.name}.prefab";
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (existing != null)
        {
            EditorUtility.DisplayDialog("Physics Prop", path + " 이(가) 이미 있습니다. 그 프리팹을 직접 수정하세요.", "OK");
            return existing;
        }

        var root = new GameObject(model.name);
        try
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(model, root.transform);
            foreach (var filter in instance.GetComponentsInChildren<MeshFilter>(true))
            {
                if (filter.sharedMesh == null) continue;
                var collider = filter.gameObject.AddComponent<MeshCollider>();
                collider.sharedMesh = filter.sharedMesh;
                collider.convex = true;
            }
            var body = root.AddComponent<Rigidbody>();
            body.mass = DefaultMass;
            body.angularDamping = .3f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            root.AddComponent<PhysicsProp>();
            return PrefabUtility.SaveAsPrefabAsset(root, path);
        }
        finally { Object.DestroyImmediate(root); }
    }

    private static string EnsureFolder(string parent, string name)
    {
        string folder = parent + "/" + name;
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(parent, name);
        return folder;
    }
}
