// dev-text: file (the exporter's window and report, read by whoever makes a mod; nothing here is shown to players)
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// ENFILADE → 모드 → 아바타를 모드로 내보내기 / 옷을 모드로 내보내기: packs a humanoid character, or an outfit to wear
/// on one, as a mod the game loads with no rebuild (the user, 2026-11-14: "VRChat 유저들이 자기가 쓰던 캐릭터를 내
/// 게임에서도 쓰고 싶어 할 텐데 이거 호환 그대로 잘 되게끔 … 유저를 제약하지 마. 최대한 유동적이고 편하게", and of
/// clothes as mods of their own: "좀 더 유저에게 자유를 주고 유동적이고 개방적인 방향이네. 좋아"). It is made to run
/// in the maker's own project, where the avatar already looks right: a VRChat project (Unity 2022, the built-in
/// pipeline, the VRChat SDK) as well as a Unity 6 URP one, and in the game's own project. This folder and
/// ModAvatarFile.cs are the whole package; they use nothing of the game's.
/// What it does, on a copy (the avatar or outfit itself is never changed):
/// - a character: applies Modular Avatar and other NDMF tools as an upload would, where the project has them;
/// - reads VRChat's PhysBones and the avatar descriptor's blink and mouth shapes as they are set (from the live
///   components, or from the prefab's text where the SDK isn't in the project: ModVrcYaml), and writes them down
///   unchanged: the game turns them into its own sway and faces when it loads the model;
/// - sorts the meshes into the wardrobe's slots (ModPieces: by their names, the piece each lies on, and for an outfit
///   where it sits on the body) and shows the result in the window, each mesh with a menu to put it elsewhere: a
///   character's clothes can then be taken off slot by slot in the game, and an outfit's pieces are each a thing to
///   wear in its own slot. An outfit also keeps Modular Avatar's Bone Proxies (a hat that follows the head);
/// - takes off what only runs in VRChat or would get in the game's way (scripts, colliders, lights, sounds, cameras);
/// - makes the meshes readable (the game measures them: the head for first person, how clothes lie, the feet);
/// - writes every material down by its shader's name with all its values, and puts a plain stand-in on the model, so
///   the game draws it with its own copy of that shader (lilToon) or the nearest it has; in a Unity 6 URP project a
///   material whose shader is neither can go in as it is, shader and all;
/// - builds one bundle (the prefab, meshes, textures) and writes "&lt;name&gt;.avatar.json" beside it, under
///   &lt;output&gt;/&lt;id&gt;/Avatars: a mod folder, to drop into the game's Mods.
/// </summary>
public sealed partial class ModAvatarExporter : EditorWindow
{
    public const string Version = "2";
    /// <summary>A mesh's choice that leaves it as it is: on the body for good (a character's), out of the mod (an outfit's).</summary>
    public const string Leave = "-";
    private const string TempRoot = "Assets/_EnfiladeExport";
    private const string OutputKey = "Enfilade.ModSdk.Output";
    private static readonly string[] Modes = { "캐릭터", "옷" };

    [SerializeField] private int mode;
    [SerializeField] private GameObject avatar;
    [SerializeField] private string id = "", displayName = "", output = "";
    [SerializeField] private bool bake = true, keepShaders = true;
    // An outfit, the avatar in this project it is made for, and that character's id in the game.
    [SerializeField] private GameObject outfit, wearer;
    [SerializeField] private string outfitId = "", outfitName = "", wearerId = "";
    [SerializeField] private Choices avatarChoices = new Choices(), outfitChoices = new Choices();
    // What the ids and names below were last taken from: kept through a script reload, so typing isn't lost to one.
    [SerializeField] private GameObject named, namedOutfit, namedWearer;
    private Vector2 scroll;
    private string report = "";

    [MenuItem("ENFILADE/모드/아바타를 모드로 내보내기")]
    private static void Open() => OpenTab(0);

    [MenuItem("ENFILADE/모드/옷을 모드로 내보내기")]
    private static void OpenOutfit() => OpenTab(1);

    private static void OpenTab(int mode)
    {
        var window = GetWindow<ModAvatarExporter>(true, "ENFILADE 모드 내보내기");
        window.minSize = new Vector2(560f, 520f);
        window.mode = mode;
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
        mode = GUILayout.Toolbar(Mathf.Clamp(mode, 0, Modes.Length - 1), Modes, GUILayout.Height(24f));
        EditorGUILayout.Space();
        if (mode == 0) CharacterGUI();
        else OutfitGUI();
        if (!string.IsNullOrEmpty(report)) EditorGUILayout.TextArea(report, EditorStyles.wordWrappedLabel);
        EditorGUILayout.EndScrollView();
    }

    // ---- A character ----

    private void CharacterGUI()
    {
        EditorGUILayout.HelpBox("캐릭터를 ENFILADE의 모드로 내보냅니다. 원본은 건드리지 않고 사본으로 작업합니다. 만들어진 폴더를 게임의 Mods 폴더에 넣으면 캐릭터 목록에 나옵니다.", MessageType.Info);
        avatar = (GameObject)EditorGUILayout.ObjectField("아바타 (씬의 것이나 프리팹)", avatar, typeof(GameObject), true);
        if (avatar != named)
        {
            named = avatar;
            avatarChoices.Clear();
            if (avatar != null)
            {
                id = Slug(avatar.name);
                displayName = avatar.name;
            }
        }
        id = EditorGUILayout.TextField(new GUIContent("id", "게임이 이 캐릭터를 가리키는 이름. 영문 소문자·숫자·-·_ 만. 한번 정하면 바꾸지 마세요(저장된 설정과 친구 화면이 이 이름으로 찾습니다)."), id);
        displayName = EditorGUILayout.TextField(new GUIContent("이름", "캐릭터 카드에 보이는 이름."), displayName);
        OutputGUI();
        bake = EditorGUILayout.ToggleLeft(new GUIContent("Modular Avatar·NDMF 도구를 업로드할 때처럼 적용 (프로젝트에 있을 때)",
            "옷을 Modular Avatar로 입혀 둔 아바타는 켜 두세요. 최적화 도구(AAO 등)가 표정 셰이프키를 지우거나 메시를 합쳐 게임의 표정·옷 칸이 줄면 끄고 내보내세요."), bake);
        ShadersGUI();
        EditorGUILayout.Space();
        string problem = Problem();
        if (problem != null) EditorGUILayout.HelpBox(problem, MessageType.Warning);
        else
        {
            EditorGUILayout.HelpBox(Preview(avatar, Slug(id)), MessageType.None);
            PiecesGUI(true);
        }
        using (new EditorGUI.DisabledScope(problem != null))
            if (GUILayout.Button("캐릭터 내보내기", GUILayout.Height(32f)))
                Run(() => Export(avatar, Slug(id), displayName, output, bake, keepShaders, avatarChoices.Map()));
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

    // ---- An outfit ----

    private void OutfitGUI()
    {
        EditorGUILayout.HelpBox("옷을 ENFILADE의 모드로 내보냅니다. 게임의 옷장에서 재킷은 겉옷 칸, 치마는 하의 칸처럼 조각마다 제 칸에 나와 다른 옷과 섞어 입을 수 있습니다. "
            + "아바타용으로 만든 옷(그 아바타의 뼈 이름을 가진 옷)은 그 캐릭터에게만 맞습니다. 원본은 건드리지 않습니다.", MessageType.Info);
        outfit = (GameObject)EditorGUILayout.ObjectField(new GUIContent("옷 (씬의 것이나 프리팹)", "아바타 밑에 넣어 둔 옷 오브젝트, 또는 옷 프리팹. Modular Avatar로 입혀 둔 그대로면 됩니다."),
            outfit, typeof(GameObject), true);
        if (outfit != namedOutfit)
        {
            namedOutfit = outfit;
            outfitChoices.Clear();
            if (outfit != null)
            {
                outfitId = Slug(outfit.name);
                outfitName = outfit.name;
                // An outfit standing under its avatar in the scene says whom it is for.
                var above = WearerOf(outfit);
                if (above != null) wearer = above;
            }
        }
        wearer = (GameObject)EditorGUILayout.ObjectField(new GUIContent("입힐 몸 (이 옷이 맞는 아바타)",
            "이 프로젝트에 있는 그 아바타. 넣어 두면 이름만으로 알 수 없는 조각도 몸의 어디에 있는지로 칸을 찾고, 옷 파일에 딸려 온 몸의 사본을 빼고, 흔들림이 몸에 부딪히는 설정을 옮깁니다. 없으면 비워 두세요."),
            wearer, typeof(GameObject), true);
        if (wearer != namedWearer)
        {
            namedWearer = wearer;
            if (wearer != null && wearerId != ModAvatarFile.Anyone) wearerId = Slug(wearer.name);
        }
        wearerId = EditorGUILayout.TextField(new GUIContent("입을 캐릭터 id",
            "게임에서 이 옷을 입을 캐릭터의 id. 게임에 든 캐릭터는 콘솔의 /mods ids가 보여 주고(mayo, ichigo …), 모드 캐릭터는 그 캐릭터를 내보낼 때 정한 id입니다. 뼈 없이 통짜로 된, 누구나 쓰는 물건은 * 를 적습니다."),
            wearerId).Trim();
        outfitId = EditorGUILayout.TextField(new GUIContent("옷 id", "게임이 이 옷을 가리키는 이름. 영문 소문자·숫자·-·_ 만. 한번 정하면 바꾸지 마세요(저장된 옷차림이 이 이름으로 찾습니다). 같은 옷을 다른 캐릭터용으로 또 내보낼 때는 같은 id를 씁니다."), outfitId);
        outfitName = EditorGUILayout.TextField(new GUIContent("이름", "옷장에 보이는 이름의 앞부분. 조각 이름이 뒤에 붙습니다(메이드복 Skirt)."), outfitName);
        OutputGUI();
        ShadersGUI();
        EditorGUILayout.Space();
        string problem = OutfitProblem();
        if (problem != null) EditorGUILayout.HelpBox(problem, MessageType.Warning);
        else
        {
            EditorGUILayout.HelpBox(Preview(outfit, Slug(outfitId)), MessageType.None);
            PiecesGUI(false);
        }
        using (new EditorGUI.DisabledScope(problem != null))
            if (GUILayout.Button("옷 내보내기", GUILayout.Height(32f)))
                Run(() => ExportOutfit(outfit, wearer, Slug(outfitId), outfitName, Skin(wearerId), output, keepShaders, outfitChoices.Map()));
    }

    // The character an outfit is for, as the game's id for it: * for anyone, else the words as an id.
    private static string Skin(string typed) => (typed ?? "").Trim() == ModAvatarFile.Anyone ? ModAvatarFile.Anyone : Slug(typed);

    private string OutfitProblem()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return "플레이를 멈추고 내보내 주세요.";
        if (outfit == null) return "내보낼 옷을 넣어 주세요(아바타 밑에 넣어 둔 옷 오브젝트나 옷 프리팹).";
        if (outfit == wearer) return "옷 자리에 아바타 전체가 들어 있습니다. 아바타가 입고 있는 옷은 '캐릭터' 탭으로 내보내면 칸마다 나뉘어 들어갑니다. 여기에는 옷만 든 오브젝트를 넣어 주세요.";
        if (outfit.GetComponentsInChildren<Renderer>(true).Length == 0) return "이 오브젝트에는 메시가 없습니다.";
        if (wearer != null && !Humanoid(wearer)) return "'입힐 몸'에는 휴머노이드 Avatar를 가진 아바타를 넣어 주세요(없으면 비워 둡니다).";
        if (Skin(wearerId).Length == 0) return "입을 캐릭터의 id를 적어 주세요(게임 콘솔의 /mods ids가 보여 줍니다. 누구나 쓰는 물건은 *).";
        if (Slug(outfitId).Length == 0) return "옷 id를 적어 주세요(영문 소문자·숫자·-·_).";
        if (string.IsNullOrEmpty(output)) return "내보낼 곳을 골라 주세요.";
        return null;
    }

    // ---- What both tabs show ----

    private void OutputGUI()
    {
        using (new EditorGUILayout.HorizontalScope())
        {
            output = EditorGUILayout.TextField(new GUIContent("내보낼 곳", "여기에 <id>/Avatars 폴더가 생깁니다. 게임의 Mods 폴더를 고르면 바로 들어갑니다."), output);
            if (GUILayout.Button("고르기", GUILayout.Width(60f)))
            {
                string picked = EditorUtility.OpenFolderPanel("모드를 내보낼 폴더 (게임의 Mods 폴더)", output, "");
                if (!string.IsNullOrEmpty(picked)) output = picked;
            }
        }
    }

    private void ShadersGUI()
    {
        keepShaders = EditorGUILayout.ToggleLeft(new GUIContent("게임에 없는 쉐이더는 쉐이더째 담기 (Unity 6 + URP 프로젝트에서만 됨)",
            "이 프로젝트가 게임과 같은 Unity 6·URP일 때, lilToon이 아닌 재질은 쉐이더까지 번들에 담아 그대로 그립니다. 다른 프로젝트(VRChat용 2022·내장 파이프라인)의 쉐이더는 게임에서 돌릴 수 없어 담지 않습니다."), keepShaders);
    }

    private void Run(Func<string> export)
    {
        EditorPrefs.SetString(OutputKey, output);
        try
        {
            report = export();
        }
        catch (Exception error)
        {
            report = "내보내지 못했습니다: " + error.Message + "\n" + error.StackTrace;
            Debug.LogException(error);
        }
        // The export moved assets about: what the window had measured is measured again.
        measured = null;
        listed = null;
        GUIUtility.ExitGUI();
    }

    private string Preview(GameObject source, string slug)
    {
        var text = new StringBuilder();
        text.Append("id: ").Append(slug).Append("  →  ").Append(Path.Combine(output, slug, "Avatars")).Append('\n');
        var shaders = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var renderer in source.GetComponentsInChildren<Renderer>(true))
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
}
