// dev-text: file (the exporter's window and report, read by whoever makes an avatar mod; nothing here is shown to players)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>
/// ENFILADE → 모드 → 아바타를 모드로 내보내기: packs a humanoid character as a mod the game loads with no rebuild (the
/// user, 2026-11-14: "VRChat 유저들이 자기가 쓰던 캐릭터를 내 게임에서도 쓰고 싶어 할 텐데 이거 호환 그대로 잘 되게끔 …
/// 유저를 제약하지 마. 최대한 유동적이고 편하게"). It is made to run in the maker's own project, where the avatar
/// already looks right: a VRChat project (Unity 2022, the built-in pipeline, the VRChat SDK) as well as a Unity 6 URP
/// one, and in the game's own project. This folder and ModAvatarFile.cs are the whole package; they use nothing of the
/// game's.
/// What it does, on a copy (the avatar itself is never changed):
/// - applies Modular Avatar and other NDMF tools as an upload would, where the project has them;
/// - reads VRChat's PhysBones and the avatar descriptor's blink and mouth shapes as they are set (from the live
///   components, or from the prefab's text where the SDK isn't in the project: ModVrcYaml), and writes them down
///   unchanged: the game turns them into its own sway and faces when it loads the avatar;
/// - takes off what only runs in VRChat or would get in the game's way (scripts, colliders, lights, sounds, cameras);
/// - makes the meshes readable (the game measures them: the head for first person, how clothes lie, the feet);
/// - writes every material down by its shader's name with all its values, and puts a plain stand-in on the model, so
///   the game draws it with its own copy of that shader (lilToon) or the nearest it has; in a Unity 6 URP project a
///   material whose shader is neither can go in as it is, shader and all;
/// - builds one bundle (the prefab, meshes, textures) and writes "&lt;id&gt;.avatar.json" beside it, under
///   &lt;output&gt;/&lt;id&gt;/Avatars: a mod folder, to drop into the game's Mods.
/// </summary>
public sealed class ModAvatarExporter : EditorWindow
{
    public const string Version = "1";
    private const string TempRoot = "Assets/_EnfiladeExport";
    private const string OutputKey = "Enfilade.ModSdk.Output";

    [SerializeField] private GameObject avatar;
    [SerializeField] private string id = "", displayName = "", output = "";
    [SerializeField] private bool bake = true, keepShaders = true;
    private GameObject named;
    private Vector2 scroll;
    private string report = "";

    [MenuItem("ENFILADE/모드/아바타를 모드로 내보내기")]
    private static void Open()
    {
        var window = GetWindow<ModAvatarExporter>(true, "ENFILADE 아바타 내보내기");
        window.minSize = new Vector2(520f, 480f);
    }

    private void OnEnable()
    {
        if (string.IsNullOrEmpty(output)) output = EditorPrefs.GetString(OutputKey, DefaultOutput());
        if (avatar == null && Selection.activeGameObject != null && Humanoid(Selection.activeGameObject)) avatar = Selection.activeGameObject;
    }

    // The game's own project has a Mods folder at its root; anywhere else, a folder of the project's own to copy from.
    private static string DefaultOutput()
    {
        string root = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        string mods = Path.Combine(root, "Mods");
        return Directory.Exists(mods) ? mods : Path.Combine(root, "EnfiladeMods");
    }

    private void OnGUI()
    {
        scroll = EditorGUILayout.BeginScrollView(scroll);
        EditorGUILayout.HelpBox("캐릭터를 ENFILADE의 모드로 내보냅니다. 원본은 건드리지 않고 사본으로 작업합니다. 만들어진 폴더를 게임의 Mods 폴더에 넣으면 캐릭터 목록에 나옵니다.", MessageType.Info);
        avatar = (GameObject)EditorGUILayout.ObjectField("아바타 (씬의 것이나 프리팹)", avatar, typeof(GameObject), true);
        if (avatar != named)
        {
            named = avatar;
            if (avatar != null)
            {
                id = Slug(avatar.name);
                displayName = avatar.name;
            }
        }
        id = EditorGUILayout.TextField(new GUIContent("id", "게임이 이 캐릭터를 가리키는 이름. 영문 소문자·숫자·-·_ 만. 한번 정하면 바꾸지 마세요(저장된 설정과 친구 화면이 이 이름으로 찾습니다)."), id);
        displayName = EditorGUILayout.TextField(new GUIContent("이름", "캐릭터 카드에 보이는 이름."), displayName);
        using (new EditorGUILayout.HorizontalScope())
        {
            output = EditorGUILayout.TextField(new GUIContent("내보낼 곳", "여기에 <id>/Avatars 폴더가 생깁니다. 게임의 Mods 폴더를 고르면 바로 들어갑니다."), output);
            if (GUILayout.Button("고르기", GUILayout.Width(60f)))
            {
                string picked = EditorUtility.OpenFolderPanel("모드를 내보낼 폴더 (게임의 Mods 폴더)", output, "");
                if (!string.IsNullOrEmpty(picked)) output = picked;
            }
        }
        bake = EditorGUILayout.ToggleLeft(new GUIContent("Modular Avatar·NDMF 도구를 업로드할 때처럼 적용 (프로젝트에 있을 때)",
            "옷을 Modular Avatar로 입혀 둔 아바타는 켜 두세요. 최적화 도구(AAO 등)가 표정 셰이프키를 지워 게임의 표정이 줄면 끄고 내보내세요."), bake);
        keepShaders = EditorGUILayout.ToggleLeft(new GUIContent("게임에 없는 쉐이더는 쉐이더째 담기 (Unity 6 + URP 프로젝트에서만 됨)",
            "이 프로젝트가 게임과 같은 Unity 6·URP일 때, lilToon이 아닌 재질은 쉐이더까지 번들에 담아 그대로 그립니다. 다른 프로젝트(VRChat용 2022·내장 파이프라인)의 쉐이더는 게임에서 돌릴 수 없어 담지 않습니다."), keepShaders);
        EditorGUILayout.Space();
        string problem = Problem();
        if (problem != null) EditorGUILayout.HelpBox(problem, MessageType.Warning);
        else EditorGUILayout.HelpBox(Preview(), MessageType.None);
        using (new EditorGUI.DisabledScope(problem != null))
            if (GUILayout.Button("내보내기", GUILayout.Height(32f)))
            {
                EditorPrefs.SetString(OutputKey, output);
                try
                {
                    report = Export(avatar, Slug(id), displayName, output, bake, keepShaders);
                }
                catch (Exception error)
                {
                    report = "내보내지 못했습니다: " + error.Message + "\n" + error.StackTrace;
                    Debug.LogException(error);
                }
                GUIUtility.ExitGUI();
            }
        if (!string.IsNullOrEmpty(report)) EditorGUILayout.TextArea(report, EditorStyles.wordWrappedLabel);
        EditorGUILayout.EndScrollView();
    }

    private string Problem()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "플레이를 멈추고 내보내 주세요.";
        if (avatar == null) return "내보낼 아바타를 넣어 주세요(씬에 놓인 것이나 프리팹).";
        if (!Humanoid(avatar)) return "이 오브젝트에는 휴머노이드 Avatar를 가진 Animator가 없습니다. 모델(FBX)의 Rig → Animation Type을 Humanoid로 해야 게임에서 움직입니다.";
        if (Slug(id).Length == 0) return "id를 적어 주세요(영문 소문자·숫자·-·_).";
        if (string.IsNullOrEmpty(output)) return "내보낼 곳을 골라 주세요.";
        return null;
    }

    private string Preview()
    {
        var text = new StringBuilder();
        text.Append("id: ").Append(Slug(id)).Append("  →  ").Append(Path.Combine(output, Slug(id), "Avatars")).Append('\n');
        var shaders = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var renderer in avatar.GetComponentsInChildren<Renderer>(true))
            foreach (var material in renderer.sharedMaterials)
                if (material != null && material.shader != null)
                    shaders[material.shader.name] = shaders.TryGetValue(material.shader.name, out int count) ? count + 1 : 1;
        text.Append("쉐이더: ");
        foreach (var pair in shaders)
            text.Append('\n').Append("  · ").Append(pair.Key).Append(" — ").Append(How(pair.Key, null));
        return text.ToString();
    }

    // What the game will do with a material of this shader (the material given, when it is known whether it can be kept).
    private string How(string shader, Material material)
    {
        if (shader.IndexOf("lilToon", StringComparison.Ordinal) >= 0) return "게임의 lilToon으로 그대로";
        if (keepShaders && CanKeepHere && (material == null || DrawsInUrp(material.shader))) return "쉐이더째 담음";
        return CanKeepHere ? "게임이 lilToon으로 바꿔 그림(비슷하게)" : "게임이 lilToon으로 바꿔 그림(비슷하게. 그대로 쓰려면 Unity 6 + URP 프로젝트에서 내보내기)";
    }

    // ---- The export ----

    /// <summary>Packs <paramref name="source"/> as the mod folder &lt;output&gt;/&lt;id&gt; and returns a report of what was done.</summary>
    public static string Export(GameObject source, string id, string name, string output, bool bake, bool keepShaders)
    {
        if (source == null) throw new ArgumentException("아바타가 없습니다.");
        if (string.IsNullOrEmpty(id)) throw new ArgumentException("id가 비었습니다.");
        var notes = new List<string>();
        string temp = TempRoot + "/" + id;
        if (AssetDatabase.IsValidFolder(temp)) AssetDatabase.DeleteAsset(temp);
        Folder(temp);
        GameObject clone = null;
        try
        {
            EditorUtility.DisplayProgressBar("ENFILADE 아바타 내보내기", "사본을 만드는 중", .1f);
            clone = Instantiate(source);
            clone.name = id;
            clone.transform.SetParent(null, false);
            clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            clone.SetActive(true);
            if (bake) Bake(clone, notes);

            var character = new ModAvatarFile.Character { id = id, name = string.IsNullOrEmpty(name) ? id : name, prefab = temp + "/" + id + ".prefab" };
            var sway = LiveSway(clone);
            string from = "SDK 컴포넌트에서";
            if (sway.Count == 0)
            {
                string prefab = AssetDatabase.GetAssetPath(source);
                if (string.IsNullOrEmpty(prefab)) prefab = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(source);
                if (!string.IsNullOrEmpty(prefab))
                {
                    // Read from the prefab's file: only what still stands at the same place on the copy counts.
                    sway = ModVrcYaml.Read(prefab).Where(bone => string.IsNullOrEmpty(bone.root) || clone.transform.Find(bone.root) != null).ToList();
                    from = "프리팹 파일에서";
                }
            }
            character.sway = sway.ToArray();
            notes.Add(sway.Count > 0 ? $"PhysBone {sway.Count}개를 {from} 읽었습니다(게임이 흔들림으로 바꿉니다)." : "PhysBone이 없습니다(머리카락·치마가 흔들리지 않습니다).");
            character.face = Face(clone, notes);

            EditorUtility.DisplayProgressBar("ENFILADE 아바타 내보내기", "VRChat 전용 부품을 떼는 중", .3f);
            Strip(clone, notes);
            EditorUtility.DisplayProgressBar("ENFILADE 아바타 내보내기", "메시와 재질을 옮기는 중", .45f);
            int meshes = Meshes(clone, temp);
            var textures = new List<string>();
            var file = new ModAvatarFile
            {
                bundle = id + ".bundle",
                exporter = $"ENFILADE Mod SDK {Version} · Unity {Application.unityVersion} · {Pipeline}",
                materials = Materials(clone, temp, keepShaders, textures, notes),
                characters = new[] { character },
            };
            PrefabUtility.SaveAsPrefabAsset(clone, character.prefab);
            DestroyImmediate(clone);
            clone = null;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            EditorUtility.DisplayProgressBar("ENFILADE 아바타 내보내기", "번들을 만드는 중", .7f);
            string folder = Path.Combine(output, id, "Avatars");
            Directory.CreateDirectory(folder);
            var assets = new List<string> { character.prefab };
            foreach (string texture in textures)
                if (!assets.Contains(texture)) assets.Add(texture);
            var build = new AssetBundleBuild { assetBundleName = file.bundle, assetNames = assets.ToArray() };
            var built = BuildPipeline.BuildAssetBundles(folder, new[] { build },
                BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.StandaloneWindows64);
            if (built == null || !File.Exists(Path.Combine(folder, file.bundle))) throw new Exception("번들을 만들지 못했습니다(Console의 오류를 봐 주세요. Windows 빌드 지원이 설치되어 있어야 합니다).");
            // Unity writes a bundle of the folder's own name and text manifests beside every bundle: the game reads none.
            string own = Path.GetFileName(folder);
            foreach (string extra in new[] { Path.Combine(folder, own), Path.Combine(folder, own + ".manifest"), Path.Combine(folder, file.bundle + ".manifest") })
                if (File.Exists(extra)) File.Delete(extra);
            File.WriteAllText(Path.Combine(folder, id + ModAvatarFile.Extension), JsonUtility.ToJson(file, true), new UTF8Encoding(false));
            string note = Path.Combine(output, id, "mod.txt");
            if (!File.Exists(note)) File.WriteAllText(note, character.name + " (ENFILADE 아바타 모드)\n", new UTF8Encoding(false));

            long size = new FileInfo(Path.Combine(folder, file.bundle)).Length;
            var report = new StringBuilder();
            report.Append($"내보냈습니다: {Path.Combine(output, id)}\n");
            report.Append($"번들 {size / 1048576f:F1} MB · 메시 {meshes}개 · 재질 {file.materials.Length}개 · 텍스처 {textures.Distinct().Count()}개\n");
            foreach (string line in notes) report.Append("· ").Append(line).Append('\n');
            report.Append("이 폴더를 게임의 Mods 폴더에 넣고 게임을 켜면(켜져 있으면 콘솔에서 /mods reload) 캐릭터 목록에 나옵니다.");
            Debug.Log("[ENFILADE] " + report);
            return report.ToString();
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            if (clone != null) DestroyImmediate(clone);
            AssetDatabase.DeleteAsset(temp);
            if (AssetDatabase.IsValidFolder(TempRoot) && AssetDatabase.FindAssets("", new[] { TempRoot }).Length == 0) AssetDatabase.DeleteAsset(TempRoot);
            AssetDatabase.Refresh();
        }
    }

    // ---- Modular Avatar and the other NDMF tools ----

    // What VRChat's upload would run over the avatar (clothes merged onto its bones, shapes changed), run on the copy.
    // Found by name, so the package builds without NDMF in the project.
    private static void Bake(GameObject clone, List<string> notes)
    {
        Type processor = null;
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            processor = assembly.GetType("nadena.dev.ndmf.AvatarProcessor");
            if (processor != null) break;
        }
        var run = processor?.GetMethod("ProcessAvatar", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(GameObject) }, null);
        if (run == null) return;
        try
        {
            run.Invoke(null, new object[] { clone });
            notes.Add("Modular Avatar·NDMF 도구를 적용했습니다.");
        }
        catch (Exception error)
        {
            notes.Add("NDMF 도구를 적용하다 멈췄습니다(" + (error.InnerException ?? error).Message + "). 적용 없이 내보냈습니다.");
        }
    }

    // ---- VRChat's settings, as they are ----

    private static List<ModAvatarFile.PhysBone> LiveSway(GameObject clone)
    {
        var found = new List<ModAvatarFile.PhysBone>();
        foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null || behaviour.GetType().Name != "VRCPhysBone" || !behaviour.enabled) continue;
            var read = new SerializedObject(behaviour);
            var on = Reference(read, "rootTransform") as Transform;
            if (on == null) on = behaviour.transform;
            if (!ModVrcYaml.Under(on, clone.transform)) continue;
            var bone = new ModAvatarFile.PhysBone
            {
                root = ModVrcYaml.PathOf(on, clone.transform),
                endpoint = read.FindProperty("endpointPosition") is SerializedProperty end && end.propertyType == SerializedPropertyType.Vector3 ? end.vector3Value : Vector3.zero,
                integration = Whole(read, "integrationType"),
                limitType = Whole(read, "limitType"),
                pull = Number(read, "pull"),
                spring = Number(read, "spring"),
                stiffness = Number(read, "stiffness"),
                gravity = Number(read, "gravity"),
                gravityFalloff = Number(read, "gravityFalloff"),
                immobile = Number(read, "immobile"),
                radius = Number(read, "radius"),
                maxAngleX = Number(read, "maxAngleX"),
                maxAngleZ = Number(read, "maxAngleZ"),
                pullCurve = Curve(read, "pullCurve"),
                springCurve = Curve(read, "springCurve"),
                stiffnessCurve = Curve(read, "stiffnessCurve"),
                gravityCurve = Curve(read, "gravityCurve"),
                immobileCurve = Curve(read, "immobileCurve"),
                radiusCurve = Curve(read, "radiusCurve"),
                maxAngleXCurve = Curve(read, "maxAngleXCurve"),
                maxAngleZCurve = Curve(read, "maxAngleZCurve"),
                collides = Whole(read, "allowCollision") != 0,
            };
            var ignore = new List<string>();
            foreach (var each in References(read, "ignoreTransforms"))
                if (each is Transform ignored && ModVrcYaml.Under(ignored, clone.transform)) ignore.Add(ModVrcYaml.PathOf(ignored, clone.transform));
            var colliders = new List<string>();
            foreach (var each in References(read, "colliders"))
            {
                if (!(each is Component collider)) continue;
                var stands = Reference(new SerializedObject(collider), "rootTransform") as Transform;
                if (stands == null) stands = collider.transform;
                if (ModVrcYaml.Under(stands, clone.transform)) colliders.Add(ModVrcYaml.PathOf(stands, clone.transform));
            }
            bone.ignore = ignore.ToArray();
            bone.colliders = colliders.ToArray();
            found.Add(bone);
        }
        return found;
    }

    // The avatar descriptor's mouth shapes and blink, by shape key name (VRChat keeps the blink as a number into the mesh's keys).
    private static ModAvatarFile.Face Face(GameObject clone, List<string> notes)
    {
        var face = new ModAvatarFile.Face();
        foreach (var behaviour in clone.GetComponentsInChildren<MonoBehaviour>(true))
        {
            if (behaviour == null || behaviour.GetType().Name != "VRCAvatarDescriptor") continue;
            var read = new SerializedObject(behaviour);
            var visemes = read.FindProperty("VisemeBlendShapes");
            if (visemes != null && visemes.isArray && Whole(read, "lipSync") == 3)
            {
                var names = new List<string>();
                for (int i = 0; i < visemes.arraySize; i++) names.Add(visemes.GetArrayElementAtIndex(i).stringValue ?? "");
                face.visemes = names.ToArray();
            }
            var lids = read.FindProperty("customEyeLookSettings.eyelidsBlendshapes");
            var mesh = read.FindProperty("customEyeLookSettings.eyelidsSkinnedMesh")?.objectReferenceValue as SkinnedMeshRenderer;
            if (Whole(read, "customEyeLookSettings.eyelidType") == 2 && lids != null && lids.isArray && lids.arraySize > 0 && mesh != null && mesh.sharedMesh != null)
            {
                int blink = lids.GetArrayElementAtIndex(0).intValue;
                if (blink >= 0 && blink < mesh.sharedMesh.blendShapeCount) face.blink = mesh.sharedMesh.GetBlendShapeName(blink);
            }
            notes.Add("아바타 디스크립터에서 " + (face.visemes.Length > 0 ? "입 모양 " + face.visemes.Length + "개" : "입 모양 없음") + ", "
                + (face.blink.Length > 0 ? "깜박임 '" + face.blink + "'" : "깜박임 없음") + "을 읽었습니다(없는 것은 게임이 흔한 이름으로 찾습니다).");
            break;
        }
        return face;
    }

    private static Object Reference(SerializedObject read, string name)
    {
        var property = read.FindProperty(name);
        return property != null && property.propertyType == SerializedPropertyType.ObjectReference ? property.objectReferenceValue : null;
    }

    private static IEnumerable<Object> References(SerializedObject read, string name)
    {
        var list = read.FindProperty(name);
        if (list == null || !list.isArray) yield break;
        for (int i = 0; i < list.arraySize; i++)
        {
            var each = list.GetArrayElementAtIndex(i);
            if (each.propertyType == SerializedPropertyType.ObjectReference && each.objectReferenceValue != null) yield return each.objectReferenceValue;
        }
    }

    private static float Number(SerializedObject read, string name)
    {
        var property = read.FindProperty(name);
        if (property == null) return 0f;
        float value = property.propertyType == SerializedPropertyType.Float ? property.floatValue
            : property.propertyType == SerializedPropertyType.Integer ? property.intValue : 0f;
        return float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;
    }

    // A whole number, an enum's or a yes/no (VRChat turned some yes/no settings into three-way ones along the way).
    private static int Whole(SerializedObject read, string name)
    {
        var property = read.FindProperty(name);
        if (property == null) return 0;
        switch (property.propertyType)
        {
            case SerializedPropertyType.Integer:
            case SerializedPropertyType.Enum: return property.intValue;
            case SerializedPropertyType.Boolean: return property.boolValue ? 1 : 0;
            case SerializedPropertyType.Float: return Mathf.RoundToInt(property.floatValue);
            default: return 0;
        }
    }

    private static ModAvatarFile.Curve Curve(SerializedObject read, string name)
    {
        var property = read.FindProperty(name);
        return property != null && property.propertyType == SerializedPropertyType.AnimationCurve
            ? new ModAvatarFile.Curve(property.animationCurveValue) : new ModAvatarFile.Curve();
    }

    // ---- Taking off what the game has no use for ----

    // Kept: the bones, the humanoid Animator, meshes and their renderers, and Unity's own constraints. Everything else
    // either runs only in VRChat (its scripts and every add-on's) or would act on the game's world (colliders and
    // bodies in its physics, lights, sounds, cameras, particles drawn with shaders the game can't run).
    private static void Strip(GameObject clone, List<string> notes)
    {
        int objects = 0, parts = 0;
        foreach (var each in clone.GetComponentsInChildren<Transform>(true))
            if (each != null && each != clone.transform && each.CompareTag("EditorOnly"))
            {
                DestroyImmediate(each.gameObject);
                objects++;
            }
        foreach (var each in clone.GetComponentsInChildren<Transform>(true))
            parts += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(each.gameObject);
        // Several passes: a part another part needs can only go once that one has.
        for (int pass = 0; pass < 4; pass++)
        {
            bool any = false;
            foreach (var component in clone.GetComponentsInChildren<Component>(true))
            {
                if (component == null || Kept(component) || !Goes(component, pass)) continue;
                DestroyImmediate(component);
                parts++;
                any = true;
            }
            if (!any && pass > 1) break;
        }
        foreach (var animator in clone.GetComponentsInChildren<Animator>(true))
        {
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
        }
        notes.Add($"VRChat 전용 부품 {parts}개" + (objects > 0 ? $"와 EditorOnly 오브젝트 {objects}개" : "") + "를 사본에서 뗐습니다(스크립트·콜라이더·조명·소리·카메라·파티클).");
    }

    private static bool Kept(Component component) =>
        component is Transform || component is Animator || component is SkinnedMeshRenderer || component is MeshRenderer || component is MeshFilter
        || component is UnityEngine.Animations.IConstraint;

    // Scripts and what leans on other parts first (joints on bodies, drawn parts on their systems), then the rest.
    private static bool Goes(Component component, int pass)
    {
        bool early = component is MonoBehaviour || component is Joint || component is Cloth
            || (component is Renderer && !(component is ParticleSystemRenderer)) || component is AudioReverbFilter
            || component is AudioLowPassFilter || component is AudioHighPassFilter || component is AudioEchoFilter || component is AudioDistortionFilter
            || component is AudioChorusFilter;
        return pass > 0 || early;
    }

    // ---- Meshes the game can read ----

    // The game measures a character from its meshes (which hang from the head, where its soles are, how clothes lie on
    // it), which a build can do only with meshes kept readable. A copy of each goes in the bundle with that set; the
    // maker's model files keep their own import settings.
    private static int Meshes(GameObject clone, string temp)
    {
        var copies = new Dictionary<Mesh, Mesh>();
        Mesh Readable(Mesh mesh)
        {
            if (mesh == null) return null;
            if (copies.TryGetValue(mesh, out var known)) return known;
            var copy = Instantiate(mesh);
            // An asset is named after its file: the mesh's own name goes into the file's, after a number that keeps it apart.
            AssetDatabase.CreateAsset(copy, temp + "/mesh" + copies.Count.ToString("000") + "_" + Safe(mesh.name) + ".asset");
            var settings = new SerializedObject(copy);
            var readable = settings.FindProperty("m_IsReadable");
            if (readable != null)
            {
                readable.boolValue = true;
                settings.ApplyModifiedPropertiesWithoutUndo();
            }
            return copies[mesh] = copy;
        }
        foreach (var skinned in clone.GetComponentsInChildren<SkinnedMeshRenderer>(true)) skinned.sharedMesh = Readable(skinned.sharedMesh);
        foreach (var filter in clone.GetComponentsInChildren<MeshFilter>(true)) filter.sharedMesh = Readable(filter.sharedMesh);
        return copies.Count;
    }

    // ---- Materials ----

    private static ModAvatarFile.MaterialData[] Materials(GameObject clone, string temp, bool keepShaders, List<string> textures, List<string> notes)
    {
        var standIns = new Dictionary<Material, Material>();
        var described = new List<ModAvatarFile.MaterialData>();
        var plain = Shader.Find("Unlit/Color");
        if (plain == null) plain = Shader.Find("Sprites/Default");
        int named = 0, kept = 0, converted = 0;
        var foreign = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
        {
            var shared = renderer.sharedMaterials;
            for (int i = 0; i < shared.Length; i++)
            {
                var material = shared[i];
                if (material == null) continue;
                if (!standIns.TryGetValue(material, out var standIn))
                {
                    var data = Describe(material, "m" + described.Count.ToString("000") + "_" + Safe(material.name), textures);
                    bool lil = data.shader.IndexOf("lilToon", StringComparison.Ordinal) >= 0 || data.original.IndexOf("lilToon", StringComparison.Ordinal) >= 0;
                    data.keep = keepShaders && CanKeepHere && !lil && DrawsInUrp(material.shader);
                    if (lil) named++;
                    else if (data.keep) kept++;
                    else
                    {
                        converted++;
                        foreign.Add(data.original.Length > 0 ? data.original : data.shader);
                    }
                    // Kept: a copy with its shader and everything. Else a plain stand-in the game swaps for the real one.
                    standIn = data.keep ? new Material(material) : new Material(plain);
                    AssetDatabase.CreateAsset(standIn, temp + "/" + data.name + ".mat");
                    standIns[material] = standIn;
                    described.Add(data);
                }
                shared[i] = standIn;
            }
            renderer.sharedMaterials = shared;
        }
        notes.Add($"재질 {described.Count}개: 게임의 lilToon으로 그대로 {named}개, 쉐이더째 담음 {kept}개, 게임이 lilToon으로 바꿔 그림 {converted}개"
            + (foreign.Count > 0 ? " (" + string.Join(", ", foreign) + ")" : "") + ".");
        if (converted > 0)
            notes.Add(CanKeepHere
                ? "바꿔 그리는 재질은 그림·색 정도만 같습니다. 이 프로젝트는 Unity 6·URP이니 '쉐이더째 담기'를 켜면 그대로 갑니다."
                : "바꿔 그리는 재질은 그림·색 정도만 같습니다. 똑같이 보려면 lilToon으로 바꾸거나, Unity 6 + URP 프로젝트에서 그 쉐이더의 URP판으로 내보내세요(VRChat용 프로젝트에서 컴파일된 쉐이더는 게임에서 돌릴 수 없습니다).");
        return described.ToArray();
    }

    // Everything the material says, by the names its shader gives its properties.
    private static ModAvatarFile.MaterialData Describe(Material material, string name, List<string> textures)
    {
        var shader = material.shader;
        var data = new ModAvatarFile.MaterialData
        {
            name = name,
            shader = shader != null ? shader.name : "",
            original = material.GetTag("OriginalShader", false, ""),
            queue = material.renderQueue,
            keywords = material.shaderKeywords ?? Array.Empty<string>(),
            instancing = material.enableInstancing,
            doubleSidedGI = material.doubleSidedGI,
        };
        string type = material.GetTag("RenderType", false, "");
        if (type.Length > 0) data.tags = new[] { new ModAvatarFile.Tag { name = "RenderType", value = type } };
        // Passes the material switches off (a material can't be asked for the list, only about each pass by its LightMode).
        var off = new List<string>();
        var lightMode = new ShaderTagId("LightMode");
        for (int pass = 0; shader != null && pass < shader.passCount; pass++)
        {
            string mode = shader.FindPassTagValue(pass, lightMode).name;
            if (!string.IsNullOrEmpty(mode) && !off.Contains(mode) && !material.GetShaderPassEnabled(mode)) off.Add(mode);
        }
        data.off = off.ToArray();
        var floats = new List<ModAvatarFile.Number>();
        var vectors = new List<ModAvatarFile.Four>();
        var pictures = new List<ModAvatarFile.Picture>();
        int count = shader != null ? shader.GetPropertyCount() : 0;
        for (int i = 0; i < count; i++)
        {
            string property = shader.GetPropertyName(i);
            var flags = shader.GetPropertyFlags(i);
            switch (shader.GetPropertyType(i))
            {
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    floats.Add(new ModAvatarFile.Number { name = property, value = Finite(material.GetFloat(property)) });
                    break;
                case ShaderPropertyType.Int:
                    floats.Add(new ModAvatarFile.Number { name = property, value = material.GetInteger(property), whole = true });
                    break;
                case ShaderPropertyType.Color:
                    var colour = material.GetColor(property);
                    vectors.Add(new ModAvatarFile.Four { name = property, x = Finite(colour.r), y = Finite(colour.g), z = Finite(colour.b), w = Finite(colour.a), color = true });
                    if ((flags & ShaderPropertyFlags.MainColor) != 0) data.mainColor = property;
                    break;
                case ShaderPropertyType.Vector:
                    var vector = material.GetVector(property);
                    vectors.Add(new ModAvatarFile.Four { name = property, x = Finite(vector.x), y = Finite(vector.y), z = Finite(vector.z), w = Finite(vector.w) });
                    break;
                case ShaderPropertyType.Texture:
                    if ((flags & ShaderPropertyFlags.MainTexture) != 0) data.mainTexture = property;
                    var texture = material.GetTexture(property);
                    string path = texture != null ? AssetDatabase.GetAssetPath(texture) : "";
                    // Unity's built-in textures have no file of their own to pack: the game shader's default stands in.
                    if (string.IsNullOrEmpty(path) || !(path.StartsWith("Assets/", StringComparison.Ordinal) || path.StartsWith("Packages/", StringComparison.Ordinal))) break;
                    pictures.Add(new ModAvatarFile.Picture
                    {
                        name = property, asset = path, sub = AssetDatabase.IsMainAsset(texture) ? "" : texture.name,
                        scale = material.GetTextureScale(property), offset = material.GetTextureOffset(property)
                    });
                    textures.Add(path);
                    break;
            }
        }
        data.floats = floats.ToArray();
        data.vectors = vectors.ToArray();
        data.textures = pictures.ToArray();
        return data;
    }

    private static float Finite(float value) => float.IsNaN(value) || float.IsInfinity(value) ? 0f : value;

    // ---- This project ----

    private static string Pipeline
    {
        get
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            return pipeline == null ? "Built-in" : pipeline.GetType().Name;
        }
    }

    // A shader compiled here runs in the game only when this is the game's kind of project: Unity 6 or later with URP.
    private static bool CanKeepHere
    {
        get
        {
            var pipeline = GraphicsSettings.currentRenderPipeline;
            if (pipeline == null || pipeline.GetType().Name.IndexOf("Universal", StringComparison.Ordinal) < 0) return false;
            string version = Application.unityVersion;
            int dot = version.IndexOf('.');
            return dot > 0 && int.TryParse(version.Substring(0, dot), out int major) && major >= 6000;
        }
    }

    // Whether URP draws anything with the shader: one of its own forward passes, or a plain pass with no lighting mode
    // and none of the built-in pipeline's lit passes (the game asks the same of a shader it is given: ModMaterials.Draws).
    private static bool DrawsInUrp(Shader shader)
    {
        if (shader == null || !shader.isSupported) return false;
        var lightMode = new ShaderTagId("LightMode");
        bool builtIn = false, plain = false;
        for (int i = 0; i < shader.passCount; i++)
        {
            string mode = shader.FindPassTagValue(i, lightMode).name;
            if (mode == "UniversalForward" || mode == "UniversalForwardOnly" || mode == "LightweightForward") return true;
            if (mode == "ForwardBase" || mode == "ForwardAdd" || mode == "Deferred" || mode == "PrepassBase" || mode == "PrepassFinal"
                || mode == "Vertex" || mode == "VertexLM" || mode == "VertexLMRGBM") builtIn = true;
            else if (string.IsNullOrEmpty(mode) || mode == "SRPDefaultUnlit") plain = true;
        }
        return plain && !builtIn;
    }

    private static bool Humanoid(GameObject target)
    {
        var animator = target != null ? target.GetComponentInChildren<Animator>(true) : null;
        return animator != null && animator.avatar != null && animator.avatar.isHuman;
    }

    /// <summary>A name as an id: lower-case English letters, digits, - and _; anything else becomes a -.</summary>
    public static string Slug(string name)
    {
        var slug = new StringBuilder();
        foreach (char c in (name ?? "").Trim().ToLowerInvariant())
        {
            bool plain = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9') || c == '_';
            if (plain) slug.Append(c);
            else if (slug.Length > 0 && slug[slug.Length - 1] != '-') slug.Append('-');
        }
        return slug.ToString().Trim('-');
    }

    // A material's name as part of a file name.
    private static string Safe(string name)
    {
        var safe = new StringBuilder();
        foreach (char c in name ?? "")
            safe.Append(char.IsLetterOrDigit(c) || c == '_' || c == '-' ? c : '_');
        return safe.Length > 40 ? safe.ToString(0, 40) : safe.ToString();
    }

    private static void Folder(string path)
    {
        string[] parts = path.Split('/');
        string at = parts[0];
        for (int i = 1; i < parts.Length; i++)
        {
            string next = at + "/" + parts[i];
            if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(at, parts[i]);
            at = next;
        }
    }
}
