using System;
using System.Collections.Generic;
using System.IO;
using KINEMATION.CharacterAnimationSystem.Integrations.FPSAnimationPack.Scripts;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// The Schießhund's Desperado as its own gun: KINEMATION's R08 revolver as the body already holds it (its rig, clips,
/// hands, bake and the R08's Weapons.csv feel), wearing the Fatebringer, a Destiny hand cannon from GMod workshop
/// 3735980722 (user, 2026-11-12: "fatebringer는 시순트 데스페라도용이고"). Tools/Blender/addon_weapon_to_fbx.py made
/// Art/Models/Addons/Weapons/Fatebringer/Fatebringer.fbx from the addon in the R08 gun's own frame
/// (Tools/Blender/Addons/Fatebringer.json says how: its cylinder on the R08 cylinder's axis, its grip on the R08 grip, its
/// iron sights level). This copies TacticalGuns/R08.prefab, takes the R08's own mesh, red dot and flashlight off, hangs
/// the Fatebringer's parts from the R08 bones that move them (frame: Body, crane: CylinderHolder, cylinder: Cylinder,
/// rounds: Ammo_01, which carries them out and back in a reload), turns its six-chamber cylinder a sixth a shot
/// (ChamberTurn), sights it through its own iron sights at its IronAim marker, gives the gun data the Fatebringer's sounds,
/// and adds it to Resources/Combat/FPSBody, whose clips GunClipRetarget then bakes. ClassKit hands slot 4 the class's
/// first gun (Classes.csv: Desperado). Runs by itself once the model, textures and sounds are in, and from
/// ENFILADE → 총 → 데스페라도 만들기; a higher Made makes it again.
/// </summary>
[InitializeOnLoad]
internal static class DesperadoSetup
{
    private const string Weapon = "Desperado", Template = "R08";
    private const string WeaponPath = "Assets/_Project/Data/Combat/Weapons/" + Weapon + ".asset";
    private const string PrefabPath = "Assets/_Project/Data/Combat/TacticalGuns/" + Weapon + ".prefab";
    private const string ModelFolder = "Assets/_Project/Art/Models/Addons/Weapons/Fatebringer";
    private const string ModelPath = ModelFolder + "/Fatebringer.fbx";
    private const string TexturesFolder = ModelFolder + "/Textures";
    private const string MaterialsFolder = ModelFolder + "/Materials";
    private const string SoundsFolder = "Assets/_Project/Art/Audio/Weapons/Fatebringer";
    private const string AssetsPath = "Assets/_Project/Resources/" + FPSBodyAssets.ResourcePath + ".asset";
    // Kept in the prefab's import data: a gun made by an older setup is made again.
    private const string Made = "DesperadoSetup 2";
    // The R08 rig turns its cylinder a chamber of eight a shot; the Fatebringer's has six (ChamberTurn).
    private const float ChamberRatio = 8f / 6f;
    // A hand cannon's shot throws a bigger flame than the R08's 1.1 (MuzzleFlash).
    private const float FlashScale = 1.35f;
    // The look of the Fatebringer's materials, from its VMTs (Source phong and env map): dark bronze metal whose base
    // map's alpha is its reflection mask ($basemapalphaenvmapmask), small parts that glow ($selfillummask, emission map),
    // and matte dark sights. Made once; tune them on the materials afterwards.
    private const float BodyMetallic = .55f, BodySmoothness = .8f, Glow = 2.5f, SightMetallic = .3f, SightSmoothness = .3f;

    static DesperadoSetup() => EditorApplication.delayCall += () => Run(false);

    [MenuItem("ENFILADE/총/데스페라도 만들기")]
    private static void RunAll() => Run(true);

    // The model, its textures or the sounds arriving (a pull, an import) make the gun once the import is over.
    private sealed class Watcher : AssetPostprocessor
    {
        private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
        {
            foreach (string path in imported)
                if (path.StartsWith(ModelFolder, StringComparison.Ordinal) || path.StartsWith(SoundsFolder, StringComparison.Ordinal))
                {
                    EditorApplication.delayCall += () => Run(false);
                    return;
                }
        }
    }

    private static void Run(bool force)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        var assets = AssetDatabase.LoadAssetAtPath<FPSBodyAssets>(AssetsPath);
        if (assets == null || assets.guns == null || AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath) == null) return;
        int template = Array.FindIndex(assets.guns, gun => gun.weapon != null && gun.weapon.name == Template);
        if (template < 0 || assets.guns[template].prefab == null) return;
        var existing = AssetDatabase.LoadAssetAtPath<WeaponData>(WeaponPath);
        int index = existing != null ? Array.FindIndex(assets.guns, gun => gun.weapon == existing) : -1;
        var importer = AssetImporter.GetAtPath(PrefabPath);
        if (!force && index >= 0 && assets.guns[index].prefab != null && importer != null && importer.userData == Made) return;
        try
        {
            // The meshes are read for the muzzle and the surface the hands touch (GunMuzzle, GunSurface).
            if (AssetImporter.GetAtPath(ModelPath) is ModelImporter model && !model.isReadable)
            {
                model.isReadable = true;
                model.SaveAndReimport();
            }
            var body = MakeMaterial("Fatebringer_Body", "FatebringerBody", BodyMetallic, BodySmoothness, true);
            var sight = MakeMaterial("Fatebringer_Sight", "FatebringerSight", SightMetallic, SightSmoothness, false);
            if (body == null || sight == null) return;
            var weapon = WeaponAsset(assets.guns[template].weapon);
            if (weapon == null) return;
            var prefab = MakePrefab(assets.guns[template].prefab, AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath), body, sight);
            if (prefab == null) return;

            // The gun list: the R08's entry (clips, profile, bake sources) with the Desperado's data and model, last, so
            // every other gun keeps its place.
            var entry = assets.guns[template];
            entry.weapon = weapon;
            entry.prefab = prefab;
            var guns = new List<FPSBodyAssets.Gun>(assets.guns);
            index = guns.FindIndex(gun => gun.weapon == weapon);
            if (index >= 0) guns[index] = entry;
            else guns.Add(entry);
            assets.guns = guns.ToArray();
            EditorUtility.SetDirty(assets);
            AssetDatabase.SaveAssets();

            importer = AssetImporter.GetAtPath(PrefabPath);
            if (importer != null)
            {
                importer.userData = Made;
                importer.SaveAndReimport();
            }
            Debug.Log("DesperadoSetup: the Desperado wears the Fatebringer (Data/Combat/TacticalGuns/Desperado.prefab); GunClipRetarget bakes its clips next.");
        }
        catch (Exception error)
        {
            Debug.LogWarning($"DesperadoSetup: the Desperado could not be made: {error.Message}\n{error.StackTrace}");
        }
    }

    // The gun data: the R08's, made once (its numbers are Weapons.csv's Desperado row), with the Fatebringer's sounds and flame.
    private static WeaponData WeaponAsset(WeaponData template)
    {
        var weapon = AssetDatabase.LoadAssetAtPath<WeaponData>(WeaponPath);
        if (weapon == null)
        {
            if (template == null) return null;
            weapon = Object.Instantiate(template);
            weapon.name = Weapon;
            AssetDatabase.CreateAsset(weapon, WeaponPath);
        }
        var write = new SerializedObject(weapon);
        write.FindProperty("displayName").stringValue = Weapon;
        write.FindProperty("muzzleFlashScale").floatValue = FlashScale;
        // Its shot, and the reload's moments as the R08's clip moves the parts (ReloadMoments): the crane swung out, the
        // spent rounds out, the new ones in, the crane shut.
        if (!Clips(write, "fireSounds", "Fatebringer_Fire_01", "Fatebringer_Fire_02", "Fatebringer_Fire_03")
            || !Clips(write, "drawSounds", "Fatebringer_Draw")
            || !Clips(write, "boltBackSounds", "Fatebringer_CraneOpen")
            || !Clips(write, "magOutSounds", "Fatebringer_Eject")
            || !Clips(write, "magInSounds", "Fatebringer_Load")
            || !Clips(write, "boltForwardSounds", "Fatebringer_CraneClose"))
            return null;
        write.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(weapon);
        return weapon;
    }

    // Sets a sound list from Art/Audio/Weapons/Fatebringer; false while a clip isn't imported yet.
    private static bool Clips(SerializedObject write, string field, params string[] names)
    {
        var list = write.FindProperty(field);
        if (list == null) return false;
        var clips = new List<AudioClip>();
        foreach (string name in names)
        {
            var clip = AssetDatabase.LoadAssetAtPath<AudioClip>($"{SoundsFolder}/{name}.wav");
            if (clip == null) return false;
            clips.Add(clip);
        }
        list.arraySize = clips.Count;
        for (int i = 0; i < clips.Count; i++) list.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
        return true;
    }

    private static GameObject MakePrefab(GameObject r08, GameObject model, Material body, Material sight)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(r08);
        GameObject look = null;
        try
        {
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = Weapon;
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            Transform root = instance.transform;

            // The R08's own look goes: its skinned mesh (the rig's bones stay for the clips), its red dot and flashlight.
            foreach (var skin in instance.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                Object.DestroyImmediate(skin.transform.childCount == 0 ? skin.gameObject : (Object)skin);
            var sights = instance.GetComponent<GunSights>();
            if (sights != null)
                foreach (var s in sights.sights)
                    if (s.part != null) Object.DestroyImmediate(s.part);
            if (instance.TryGetComponent(out GunAttachments parts))
            {
                foreach (var list in new[] { parts.muzzle, parts.grips, parts.underbarrel })
                    foreach (var choice in list)
                        if (choice.part != null) Object.DestroyImmediate(choice.part);
                Object.DestroyImmediate(parts);
            }

            // The Fatebringer in the gun's frame, each part then hung from the bone that moves it, where it is. The converter
            // writes the gun's tip on FBX +Z, but Art/Models imports bake the axis conversion (ModelImportRules), and the baked
            // conversion flips Z where Unity's own flips X: the model comes in a half turn about Y from the gun's frame (the
            // first build's parts sat at +90° about X instead of -90°, its iron sight 0.205 m in front of the grip instead of
            // behind it). A half turn back puts every part, the crane's side too, where the recipe measured it on the R08.
            look = (GameObject)PrefabUtility.InstantiatePrefab(model);
            PrefabUtility.UnpackPrefabInstance(look, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            look.transform.SetParent(root, false);
            look.transform.localPosition = Vector3.zero;
            look.transform.localRotation = Quaternion.Euler(0f, 180f, 0f);
            look.transform.localScale = Vector3.one;
            Transform frame = Find(look.transform, "Fatebringer_Frame"), crane = Find(look.transform, "Fatebringer_Crane"),
                cylinder = Find(look.transform, "Fatebringer_Cylinder"), rounds = Find(look.transform, "Fatebringer_Rounds"),
                ironAim = Find(look.transform, "IronAim");
            Transform bodyBone = FindBone(root, look.transform, "Body"), craneBone = FindBone(root, look.transform, "CylinderHolder"),
                cylinderBone = FindBone(root, look.transform, "Cylinder"), roundBone = FindBone(root, look.transform, "Ammo_01");
            if (frame == null || crane == null || cylinder == null || rounds == null || ironAim == null
                || bodyBone == null || craneBone == null || cylinderBone == null || roundBone == null)
            {
                Debug.LogWarning("DesperadoSetup: the Fatebringer model or the R08 rig is missing a part or a bone.");
                return null;
            }
            foreach (var part in new[] { frame, crane, cylinder, rounds })
                foreach (var renderer in part.GetComponentsInChildren<MeshRenderer>(true))
                {
                    var materials = renderer.sharedMaterials;
                    for (int i = 0; i < materials.Length; i++)
                        materials[i] = materials[i] != null && materials[i].name.IndexOf("Sight", StringComparison.OrdinalIgnoreCase) >= 0 ? sight : body;
                    renderer.sharedMaterials = materials;
                }
            frame.SetParent(bodyBone, true);
            crane.SetParent(craneBone, true);
            cylinder.SetParent(cylinderBone, true);
            rounds.SetParent(roundBone, true);
            Vector3 axis = cylinderBone.InverseTransformDirection(root.forward);
            cylinder.gameObject.AddComponent<ChamberTurn>().Set(cylinderBone, axis, ChamberRatio);
            rounds.gameObject.AddComponent<ChamberTurn>().Set(cylinderBone, axis, ChamberRatio);

            // Its own iron sights: the R08's aim point moves to the eye on the Fatebringer's sight line, turned as the gun.
            var aim = FindBone(root, look.transform, "AimPoint");
            if (aim == null || aim.parent != root)
            {
                aim = new GameObject("AimPoint").transform;
                aim.SetParent(root, false);
            }
            aim.SetPositionAndRotation(ironAim.position, root.rotation);
            if (sights != null)
            {
                sights.sights = new[] { new GunSights.Sight { part = null, aimPoint = aim, ironSight = true, scope = null } };
                sights.ironSights = Array.Empty<GameObject>();
                sights.ironsInMesh = null;
                sights.ironTriangles = Array.Empty<int>();
                sights.ironTriangleTotal = 0;
                sights.initial = 0;
                sights.Select(0);
            }
            var cas = instance.GetComponent<CasFPSWeapon>();
            if (cas != null) cas.aimPoint = aim;
            Object.DestroyImmediate(look);
            look = null;

            var made = PrefabUtility.SaveAsPrefabAsset(instance, PrefabPath, out bool saved);
            return saved ? made : null;
        }
        finally
        {
            if (look != null) Object.DestroyImmediate(look);
            Object.DestroyImmediate(instance);
        }
    }

    // A URP Lit material for the Fatebringer, made once from its textures; null while they aren't imported yet.
    private static Material MakeMaterial(string name, string texture, float metallic, float smoothness, bool reflectionMask)
    {
        string path = $"{MaterialsFolder}/{name}.mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        var baseMap = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexturesFolder}/{texture}.png");
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexturesFolder}/{texture}_normal.png");
        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (baseMap == null || normal == null || shader == null) return null;
        material = new Material(shader) { name = name };
        material.SetTexture("_BaseMap", baseMap);
        material.SetTexture("_MainTex", baseMap);
        material.SetTexture("_BumpMap", normal);
        material.EnableKeyword("_NORMALMAP");
        material.SetFloat("_Metallic", metallic);
        material.SetFloat("_Smoothness", smoothness);
        if (reflectionMask)
        {
            // Smoothness from the base map's alpha, the gun's reflection mask.
            material.SetFloat("_SmoothnessTextureChannel", 1f);
            material.EnableKeyword("_SMOOTHNESS_TEXTURE_ALBEDO_CHANNEL_A");
        }
        var glow = AssetDatabase.LoadAssetAtPath<Texture2D>($"{TexturesFolder}/{texture}_emission.png");
        if (glow != null)
        {
            material.SetTexture("_EmissionMap", glow);
            material.SetColor("_EmissionColor", Color.white * Glow);
            material.EnableKeyword("_EMISSION");
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
        }
        Directory.CreateDirectory(MaterialsFolder);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    private static Transform Find(Transform root, string name)
    {
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
            if (Find(root.GetChild(i), name) is Transform found) return found;
        return null;
    }

    // A transform of the R08's by name, outside the Fatebringer model (whose parts share no names with the rig's).
    private static Transform FindBone(Transform root, Transform skip, string name)
    {
        if (root == skip) return null;
        if (root.name == name) return root;
        for (int i = 0; i < root.childCount; i++)
            if (FindBone(root.GetChild(i), skip, name) is Transform found) return found;
        return null;
    }
}
