// dev-text: file (the exporter's report, read by whoever makes a mod; nothing here is shown to players)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// ModAvatarExporter: packing a character or an outfit as a mod folder. One part of the class; the rest is in
// ModAvatarExporter.cs and the files beside it.
public sealed partial class ModAvatarExporter
{
    // ---- A character ----

    /// <summary>Packs <paramref name="source"/> as the mod folder &lt;output&gt;/&lt;id&gt; and returns a report of what was done.</summary>
    /// <param name="slots">
    /// Slots chosen by hand for its meshes, by mesh name (<see cref="Tidy"/>): a slot's id, or <see cref="Leave"/> to
    /// keep a mesh on the body. A mesh not named is sorted by the rules (ModPieces); null sorts them all so.
    /// </param>
    public static string Export(GameObject source, string id, string name, string output, bool bake, bool keepShaders,
        IReadOnlyDictionary<string, string> slots = null)
    {
        if (source == null) throw new ArgumentException("아바타가 없습니다.");
        if (string.IsNullOrEmpty(id)) throw new ArgumentException("id가 비었습니다.");
        var notes = new List<string>();
        string temp = Begin(id);
        GameObject clone = null;
        try
        {
            Progress("사본을 만드는 중", .1f);
            clone = Instantiate(source);
            clone.name = id;
            clone.transform.SetParent(null, false);
            clone.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            clone.SetActive(true);
            if (bake) Bake(clone, notes);
            TidyNames(clone);

            var character = new ModAvatarFile.Character { id = id, name = string.IsNullOrEmpty(name) ? id : name, prefab = temp + "/" + id + ".prefab" };
            character.sway = Sway(clone, source, null, notes);
            character.face = Face(clone, notes);

            Progress("VRChat 전용 부품을 떼는 중", .3f);
            Strip(clone, notes);
            Progress("입고 온 옷을 옷장 칸에 나누는 중", .4f);
            var body = ModPieces.Body.Of(clone);
            var pieces = ModPieces.Measure(clone, body, null, Prefix(id, name, source.name), true, true);
            var worn = Arrange(pieces, slots, body, ModPieces.Frame.Of(clone, clone), true, out _).Where(piece => piece.Slot != null).ToList();
            character.parts = Parts(worn);
            notes.Add(character.parts.Length > 0
                ? $"입고 온 옷을 옷장 칸에 나눴습니다: {Count(worn.Select(piece => piece.Slot))}. 게임에서 칸마다 벗기거나 이 캐릭터용 옷으로 갈아입힐 수 있습니다. 나머지 메시 {pieces.Count - worn.Count}개는 몸으로 둡니다."
                : "이름으로 옷이라고 알아본 메시가 없어 통째로 입은 캐릭터로 내보냈습니다(창의 '옷 칸' 목록에서 메시마다 칸을 고를 수 있습니다).");

            Progress("메시와 재질을 옮기는 중", .45f);
            int meshes = Meshes(clone, temp);
            var textures = new List<string>();
            var file = new ModAvatarFile
            {
                bundle = id + ".bundle",
                exporter = Exporter,
                materials = Materials(clone, temp, keepShaders, textures, notes),
                characters = new[] { character },
            };
            long size = Pack(ref clone, character.prefab, file, textures, Path.Combine(output, id, "Avatars"), id);
            Note(Path.Combine(output, id), character.name + " (ENFILADE 아바타 모드)");
            return Report(Path.Combine(output, id), size, meshes, file, textures, notes,
                "이 폴더를 게임의 Mods 폴더에 넣고 게임을 켜면(켜져 있으면 콘솔에서 /mods reload) 캐릭터 목록에 나옵니다.");
        }
        finally
        {
            End(temp, clone);
        }
    }

    // ---- An outfit ----

    /// <summary>
    /// Packs an outfit (clothes on a copy of an avatar's bones, or a rigid accessory) as the mod folder
    /// &lt;output&gt;/&lt;id&gt;: its meshes go into the wardrobe's slots as things to wear on the character
    /// <paramref name="skin"/> (that character's id in the game, or ModAvatarFile.Anyone). Returns a report.
    /// </summary>
    /// <param name="wearer">
    /// The avatar it is made for, in this project: where its pieces sit on the body is measured against it, a copy of its
    /// body carried in the outfit is left out, and where the outfit stood under it is kept. May be null: then the
    /// meshes are sorted by their names alone.
    /// </param>
    /// <param name="slots">Slots chosen by hand, by mesh name: a slot's id, or <see cref="Leave"/> to leave a mesh out of the mod.</param>
    public static string ExportOutfit(GameObject source, GameObject wearer, string id, string name, string skin, string output, bool keepShaders,
        IReadOnlyDictionary<string, string> slots = null)
    {
        if (source == null) throw new ArgumentException("옷이 없습니다.");
        if (string.IsNullOrEmpty(id)) throw new ArgumentException("옷 id가 비었습니다.");
        if (string.IsNullOrEmpty(skin)) throw new ArgumentException("입을 캐릭터의 id가 비었습니다.");
        bool anyone = skin == ModAvatarFile.Anyone;
        if (anyone) wearer = null;
        string title = string.IsNullOrWhiteSpace(name) ? ModPieces.Title(id) : name.Trim();
        // One outfit made for two characters is two files of one id, side by side in the mod's folder.
        string stem = id + "." + (anyone ? "any" : skin);
        var notes = new List<string>();
        string temp = Begin(stem);
        GameObject clone = null;
        try
        {
            Progress("사본을 만드는 중", .1f);
            clone = Instantiate(source);
            clone.name = id;
            clone.transform.SetParent(null, false);
            var outfit = new ModAvatarFile.Outfit { id = id, name = title, skin = skin, prefab = temp + "/" + id + ".prefab" };
            Stand(clone, source, wearer, outfit);
            clone.SetActive(true);
            TidyNames(clone);
            PlainBones(clone, notes);
            outfit.sway = Sway(clone, source, wearer, notes);
            outfit.holds = Holds(clone, source, wearer, notes);

            Progress("VRChat 전용 부품을 떼는 중", .3f);
            Strip(clone, notes);
            Progress("옷을 옷장 칸에 나누는 중", .4f);
            var prefix = Prefix(id, name, source.name, anyone ? "" : skin);
            // The outfit as it stands under its avatar in the scene is no part of that avatar's body.
            var body = ModPieces.Body.Of(wearer, source.transform);
            var pieces = ModPieces.Measure(clone, body, null, prefix, true);
            var active = Arrange(pieces, slots, body, ModPieces.Frame.Of(wearer, clone), false, out int unknown);
            outfit.items = Items(active, id, title, prefix);
            if (outfit.items.Length == 0)
                throw new Exception("입을 수 있는 메시가 하나도 없습니다. 창의 '옷 칸' 목록에서 메시마다 칸을 골라 주세요.");
            // What isn't worn stays out of the bundle. What its maker put in a slot though it was switched off goes on.
            var worn = active.Where(piece => piece.Slot != null).ToList();
            // Clothes already joined to their avatar hang from the avatar's own bones, which the copy hasn't got.
            foreach (var piece in worn)
                if (piece.Renderer is SkinnedMeshRenderer skinned && skinned.bones.Any(bone => bone != null && !ModVrcYaml.Under(bone, clone.transform)))
                    throw new Exception($"'{piece.Name}'은 옷 안의 뼈가 아니라 아바타의 뼈에 바로 묶여 있습니다(아바타에 이미 합쳐진 옷). "
                        + "이런 옷은 '캐릭터' 탭으로 아바타째 내보내면 칸마다 나뉘어 들어갑니다. 여기에는 자기 뼈대를 가진 옷 프리팹(아바타에 입히기 전의 것)을 넣어 주세요.");
            var kept = new HashSet<Renderer>(worn.Select(piece => piece.Renderer));
            int off = 0, copies = 0;
            foreach (var piece in pieces)
            {
                if (kept.Contains(piece.Renderer))
                {
                    if (piece.Given && piece.Off) SwitchOn(piece.Renderer, clone.transform);
                    continue;
                }
                if (piece.Off) off++;
                else if (piece.Copy) copies++;
                Drop(piece.Renderer);
            }
            int left = pieces.Count - worn.Count;
            notes.Add($"옷 {outfit.items.Length}개로 나눴습니다: {Count(worn.Select(piece => piece.Slot))}."
                + (left > 0 ? $" 넣지 않은 메시 {left}개" + (off + copies + unknown > 0 ? "(" + string.Join(", ", new[]
                    { off > 0 ? $"꺼져 있던 것 {off}" : null, copies > 0 ? $"캐릭터 몸의 사본 {copies}" : null, unknown > 0 ? $"칸을 모르는 것 {unknown}" : null }
                    .Where(each => each != null)) + ")" : "") + "." : ""));
            if (unknown > 0) notes.Add("칸을 모르는 메시는 창의 '옷 칸' 목록에서 칸을 고르면 들어갑니다" + (wearer == null && !anyone ? "('입힐 몸'에 아바타를 넣으면 이름이 없는 메시도 몸의 어디에 있는지로 알아봅니다)." : "."));

            Progress("메시와 재질을 옮기는 중", .45f);
            int meshes = Meshes(clone, temp);
            var textures = new List<string>();
            var file = new ModAvatarFile
            {
                bundle = stem + ".bundle",
                exporter = Exporter,
                materials = Materials(clone, temp, keepShaders, textures, notes),
                outfits = new[] { outfit },
            };
            long size = Pack(ref clone, outfit.prefab, file, textures, Path.Combine(output, id, "Avatars"), stem);
            Note(Path.Combine(output, id), title + " (ENFILADE 옷 모드)");
            return Report(Path.Combine(output, id), size, meshes, file, textures, notes, anyone
                ? "이 폴더를 게임의 Mods 폴더에 넣고 게임을 켜면(켜져 있으면 콘솔에서 /mods reload) 옷장의 그 칸에 나옵니다. 누구나 쓰는 물건은 얼굴에 맞추는 칸(마스크)이나 캐릭터마다 자리를 잰 칸(귀걸이)에서만 나옵니다."
                : $"이 폴더를 게임의 Mods 폴더에 넣고 게임을 켜면(켜져 있으면 콘솔에서 /mods reload) 캐릭터 '{skin}'의 옷장 칸에 나옵니다. 캐릭터 id는 게임 콘솔의 /mods ids로 볼 수 있습니다.");
        }
        finally
        {
            End(temp, clone);
        }
    }

    // Where the outfit stood in its avatar's root is kept two ways: written into the file (the game puts the outfit
    // there, whatever size either root is), and given to the copy's own root on top of the avatar root's size, so the
    // two files stand at one origin as an outfit's and a character's exported from one avatar do (the game compares
    // their bones' places when it works out the outfit's sway). With no avatar under which it stands, the prefab is
    // taken as it is.
    private static void Stand(GameObject clone, GameObject source, GameObject wearer, ModAvatarFile.Outfit outfit)
    {
        var root = clone.transform;
        if (wearer != null && source != wearer && source.transform.IsChildOf(wearer.transform))
        {
            var stood = wearer.transform.worldToLocalMatrix * source.transform.localToWorldMatrix;
            outfit.position = stood.GetColumn(3);
            outfit.rotation = stood.rotation;
            outfit.scale = stood.lossyScale;
            var beside = Matrix4x4.TRS(Vector3.zero, Quaternion.identity, wearer.transform.localScale) * stood;
            root.localPosition = beside.GetColumn(3);
            root.localRotation = beside.rotation;
            root.localScale = beside.lossyScale;
            return;
        }
        outfit.position = root.localPosition;
        outfit.rotation = root.localRotation;
        outfit.scale = root.localScale;
    }

    /// <summary>The avatar an outfit placed in a scene is worn by: the nearest object above it with a humanoid Animator; null when there is none.</summary>
    public static GameObject WearerOf(GameObject outfit)
    {
        for (var at = outfit != null ? outfit.transform.parent : null; at != null; at = at.parent)
            if (at.TryGetComponent(out Animator animator) && animator.avatar != null && animator.avatar.isHuman) return at.gameObject;
        return null;
    }

    private static void SwitchOn(Renderer renderer, Transform top)
    {
        for (var at = renderer.transform; at != null && at != top; at = at.parent)
            if (!at.gameObject.activeSelf) at.gameObject.SetActive(true);
        renderer.enabled = true;
    }

    // A mesh left out of the mod: its renderer goes from the copy, so neither it nor its materials are packed. Its object
    // stays, for whatever hangs under it.
    private static void Drop(Renderer renderer)
    {
        if (renderer == null) return;
        var filter = renderer.GetComponent<MeshFilter>();
        DestroyImmediate(renderer);
        if (filter != null) DestroyImmediate(filter);
    }

    // ---- What both do ----

    // A folder of the project's own to make the bundle's assets in, emptied first; End takes it away again.
    private static string Begin(string stem)
    {
        string temp = TempRoot + "/" + stem;
        if (AssetDatabase.IsValidFolder(temp)) AssetDatabase.DeleteAsset(temp);
        Folder(temp);
        return temp;
    }

    private static void End(string temp, GameObject clone)
    {
        EditorUtility.ClearProgressBar();
        if (clone != null) DestroyImmediate(clone);
        AssetDatabase.DeleteAsset(temp);
        if (AssetDatabase.IsValidFolder(TempRoot) && AssetDatabase.FindAssets("", new[] { TempRoot }).Length == 0) AssetDatabase.DeleteAsset(TempRoot);
        AssetDatabase.Refresh();
    }

    private static void Progress(string doing, float done) => EditorUtility.DisplayProgressBar("ENFILADE 모드 내보내기", doing, done);

    private static string Exporter => $"ENFILADE Mod SDK {Version} · Unity {Application.unityVersion} · {Pipeline}";

    // The PhysBones as set: from the live components, or, where VRChat's SDK isn't in the project, from the text of
    // the prefab the thing came from (only what still stands at the same place on the copy counts).
    private static ModAvatarFile.PhysBone[] Sway(GameObject clone, GameObject source, GameObject wearer, List<string> notes)
    {
        var sway = LiveSway(clone, wearer);
        string from = "SDK 컴포넌트에서";
        if (sway.Count == 0)
        {
            string prefab = AssetDatabase.GetAssetPath(source);
            if (string.IsNullOrEmpty(prefab)) prefab = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(source);
            if (!string.IsNullOrEmpty(prefab))
            {
                sway = ModVrcYaml.Read(prefab).Where(bone => string.IsNullOrEmpty(bone.root) || clone.transform.Find(bone.root) != null).ToList();
                from = "프리팹 파일에서";
            }
        }
        notes.Add(sway.Count > 0 ? $"PhysBone {sway.Count}개를 {from} 읽었습니다(게임이 흔들림으로 바꿉니다)." : "PhysBone이 없습니다(흔들리는 부분이 없습니다).");
        return sway.ToArray();
    }

    // Saves the copy as the bundle's prefab, builds the bundle and writes the file beside it. Returns the bundle's size.
    private static long Pack(ref GameObject clone, string prefab, ModAvatarFile file, List<string> textures, string folder, string stem)
    {
        PrefabUtility.SaveAsPrefabAsset(clone, prefab);
        DestroyImmediate(clone);
        clone = null;
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Progress("번들을 만드는 중", .7f);
        Directory.CreateDirectory(folder);
        var assets = new List<string> { prefab };
        foreach (string texture in textures)
            if (!assets.Contains(texture)) assets.Add(texture);
        var build = new AssetBundleBuild { assetBundleName = file.bundle, assetNames = assets.ToArray() };
        var built = BuildPipeline.BuildAssetBundles(folder, new[] { build },
            BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.ForceRebuildAssetBundle, BuildTarget.StandaloneWindows64);
        string bundle = Path.Combine(folder, file.bundle);
        if (built == null || !File.Exists(bundle)) throw new Exception("번들을 만들지 못했습니다(Console의 오류를 봐 주세요. Windows 빌드 지원이 설치되어 있어야 합니다).");
        // Unity writes a bundle of the folder's own name and text manifests beside every bundle: the game reads none.
        string own = Path.GetFileName(folder);
        foreach (string extra in new[] { Path.Combine(folder, own), Path.Combine(folder, own + ".manifest"), bundle + ".manifest" })
            if (File.Exists(extra)) File.Delete(extra);
        File.WriteAllText(Path.Combine(folder, stem + ModAvatarFile.Extension), JsonUtility.ToJson(file, true), new UTF8Encoding(false));
        return new FileInfo(bundle).Length;
    }

    // The mod folder's one line about itself, where it has none yet.
    private static void Note(string modFolder, string line)
    {
        string note = Path.Combine(modFolder, "mod.txt");
        if (!File.Exists(note)) File.WriteAllText(note, line + "\n", new UTF8Encoding(false));
    }

    private static string Report(string modFolder, long size, int meshes, ModAvatarFile file, List<string> textures, List<string> notes, string next)
    {
        var report = new StringBuilder();
        report.Append($"내보냈습니다: {modFolder}\n");
        report.Append($"번들 {size / 1048576f:F1} MB · 메시 {meshes}개 · 재질 {file.materials.Length}개 · 텍스처 {textures.Distinct().Count()}개\n");
        foreach (string line in notes) report.Append("· ").Append(line).Append('\n');
        report.Append(next);
        Debug.Log("[ENFILADE] " + report);
        return report.ToString();
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

    // ---- Taking off what the game has no use for ----

    // What stays on the copy is what the game lets a worn thing keep (WornParts: the bones, the humanoid Animator,
    // meshes and their renderers, a LOD group, Unity's own constraints): the one list the game itself strips by when it
    // dresses a body, so what is packed here is what the game would have left on it anyway. Everything else either runs
    // only in VRChat (its scripts and every add-on's) or would act on the game's world (colliders and bodies in its
    // physics, lights, sounds, cameras, particles drawn with shaders the game can't run).
    private static void Strip(GameObject clone, List<string> notes)
    {
        int objects = 0, missing = 0;
        foreach (var each in clone.GetComponentsInChildren<Transform>(true))
            if (each != null && each != clone.transform && each.CompareTag("EditorOnly"))
            {
                DestroyImmediate(each.gameObject);
                objects++;
            }
        foreach (var each in clone.GetComponentsInChildren<Transform>(true))
            missing += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(each.gameObject);
        string went = WornParts.Strip(clone);
        foreach (var animator in clone.GetComponentsInChildren<Animator>(true))
        {
            animator.runtimeAnimatorController = null;
            animator.applyRootMotion = false;
        }
        var said = new List<string>();
        if (went.Length > 0) said.Add(went);
        if (missing > 0) said.Add($"빠진 스크립트 {missing}");
        if (objects > 0) said.Add($"EditorOnly 오브젝트 {objects}");
        notes.Add(said.Count > 0
            ? "게임에서 쓰지 않는 부품을 사본에서 뗐습니다(스크립트·콜라이더·조명·소리·카메라·파티클): " + string.Join(", ", said) + "."
            : "뗄 부품이 없습니다(뼈·메시·컨스트레인트만 있는 깨끗한 모델).");
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
