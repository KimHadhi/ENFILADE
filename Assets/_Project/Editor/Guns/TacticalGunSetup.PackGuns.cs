using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using KINEMATION.CharacterAnimationSystem.Integrations.FPSAnimationPack.Scripts;
using UnityEditor;
using UnityEngine;
using PartsPlayer = KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables.SimpleAnimationPlayer;
using Object = UnityEngine.Object;

// TacticalGunSetup: The FPS Animation Pack's guns (MX16A4, M1911 and Ultimate's others). One part of the class; the rest is in TacticalGunSetup.cs and the files beside it.
internal static partial class TacticalGunSetup
{
    // KINEMATION's FPS Animation Pack has no parts of its own for its guns, so they wear the Tactical Shooter Pack's
    // (user, 2026-11-09: "담 단계"). A copy of the gun's CAS prefab carries them (Data/Guns/FPSPackGuns), and
    // Resources/Combat/FPSBody points at it.
    // Which parts a gun carries and where they sit is not this file's to say (GunMounts): each socket's row in
    // Tables/Weapons/GunMounts.csv lists the parts it offers (fits), and the game's own rule seats them (GunSights.Seat,
    // GunAttachments.Seat), here as in play. This file once measured every mount off the mesh by one guess, three
    // centimetres ahead of the gun's aim point, with the MX16A4 alone put right by hand (GunMounts says what came of it).
    private const string PackGunsFolder = ProjectFolders.Guns + "FPSPackGuns";

    private const string CasGuns = "Assets/ThirdParty/KINEMATION/CharacterAnimationSystem/Integrations/FPSAnimationPack/Prefabs/Weapons";

    // One of the pack's guns: our weapon's name and KINEMATION's CAS prefab of it.
    private sealed class PackGun
    {
        public string weapon, source;
    }

    private static PackGun Ultimate(string weapon, string cas) => new PackGun { weapon = weapon, source = CasGuns + "/CAS_FPSPack_" + cas + ".prefab" };

    /// <summary>
    /// The FPS Animation Pack's guns get their parts now (PackGunSetup, once it has put new ones in FPSBody, so they don't
    /// wait for the next time scripts load).
    /// </summary>
    internal static void FitPackGunsNow()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        var assets = AssetDatabase.LoadAssetAtPath<FPSBodyAssets>(AssetsPath);
        if (assets == null || assets.guns == null || !FitPackGuns(assets, false)) return;
        EditorUtility.SetDirty(assets);
        AssetDatabase.SaveAssets();
    }

    private static bool FitPackGuns(FPSBodyAssets assets, bool all)
    {
        bool changed = false;
        foreach (var spec in PackGuns)
        {
            int index = Array.FindIndex(assets.guns, gun => gun.weapon != null && gun.weapon.name == spec.weapon);
            if (index < 0) continue;
            var current = assets.guns[index].prefab;
            // Made already, unless a part its rows offer isn't on it yet (a row's fits gained one).
            if (!all && current != null && current.GetComponent<GunAttachments>() is GunAttachments made && !GripsWithoutHand(made)
                && !PartsNotPlayed(current) && !PartsMissing(spec.weapon, current)) continue;
            try
            {
                var prefab = FitPackGun(spec);
                if (prefab == null) continue;
                assets.guns[index].prefab = prefab;
                changed = true;
                Debug.Log($"TacticalGunSetup: {spec.weapon} now wears the parts its GunMounts rows offer ({PackGunsFolder}).", prefab);
            }
            catch (Exception error)
            {
                Debug.LogWarning($"TacticalGunSetup: {spec.weapon}'s parts could not be fitted: {error.Message}\n{error.StackTrace}");
            }
        }
        return changed;
    }

    // The sockets parts hang on, and the object each one's parts go under on these guns.
    private static readonly (string socket, string mount)[] Mounts =
    {
        (GunMounts.Sight, "SightMount"), (GunMounts.Muzzle, "MuzzleMount"), (GunMounts.Grip, "GripMount"), (GunMounts.Under, "UnderMount"),
    };

    // A part a socket's row offers that the gun's prefab doesn't carry: the gun is made again once to take it on. A part
    // the prefab carries beyond its rows is only left off the menu (GunSights.Offered).
    private static bool PartsMissing(string weapon, GameObject prefab)
    {
        foreach (var (socket, _) in Mounts)
            foreach (string part in GunMounts.Of(weapon, socket).Fits)
                if (part != GunMounts.Own && Hierarchy.Find(prefab.transform, part) == null) return true;
        return false;
    }

    private static GameObject FitPackGun(PackGun spec)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(spec.source);
        if (source == null) return null;
        Directory.CreateDirectory(PackGunsFolder);
        AssetDatabase.Refresh();
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
        try
        {
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            // The gun's id in the tables is its object's name (GunMounts.IdOf).
            instance.name = spec.weapon;
            var gun = instance.transform;
            gun.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var cas = instance.GetComponent<CasFPSWeapon>();
            var aim = cas != null && cas.aimPoint != null ? cas.aimPoint : gun;
            PlayPartsThroughCas(instance, source);
            RowsFor(spec.weapon, gun, aim);

            // Each socket's parts under an object of its own, all at the gun's origin: the game's rule puts the object on
            // the socket and each part along it (below), as it does again whenever the gun wakes.
            var mounts = new Dictionary<string, Transform>();
            var offers = new Dictionary<string, List<Component>>();
            foreach (var (socket, name) in Mounts)
            {
                var found = new List<Component>();
                foreach (string part in GunMounts.Of(spec.weapon, socket).Fits)
                    if (part != GunMounts.Own && Pick(part) is Component prefab) found.Add(prefab);
                offers[socket] = found;
                if (found.Count == 0) continue;
                var mount = new GameObject(name).transform;
                mount.SetParent(gun, false);
                mounts[socket] = mount;
            }

            var sights = new List<GunSights.Sight>();
            foreach (var prefab in offers[GunMounts.Sight]) sights.Add(SightChoice(prefab, Put(prefab, mounts[GunMounts.Sight]), aim));
            // The gun's own sights last, as it came: whether the menu offers them is its row's (the L96X has none).
            sights.Add(new GunSights.Sight { part = null, aimPoint = aim, ironSight = true });
            var none = new GunAttachments.Choice { kind = GunAttachments.Kind.None };
            var muzzles = new List<GunAttachments.Choice> { none };
            var grips = new List<GunAttachments.Choice> { none };
            var under = new List<GunAttachments.Choice> { none };
            foreach (var prefab in offers[GunMounts.Muzzle]) muzzles.Add(ChoiceOf(prefab, Put(prefab, mounts[GunMounts.Muzzle]), gun));
            foreach (var prefab in offers[GunMounts.Grip]) grips.Add(ChoiceOf(prefab, Put(prefab, mounts[GunMounts.Grip]), gun));
            foreach (var prefab in offers[GunMounts.Under]) under.Add(ChoiceOf(prefab, Put(prefab, mounts[GunMounts.Under]), gun));

            var parts = instance.AddComponent<GunAttachments>();
            parts.muzzle = muzzles.Count > 1 ? muzzles.ToArray() : Array.Empty<GunAttachments.Choice>();
            parts.grips = grips.Count > 1 ? grips.ToArray() : Array.Empty<GunAttachments.Choice>();
            parts.underbarrel = under.Count > 1 ? under.ToArray() : Array.Empty<GunAttachments.Choice>();
            // Where a row names no start: a flashlight stays on its rail, off, as a Siege gun comes.
            parts.initialUnderbarrel = Mathf.Max(0, under.FindIndex(choice => choice.kind == GunAttachments.Kind.Flashlight));
            // Seated by the game's rule, so the prefab as saved is the gun as it wakes (a pickup on the floor shows the
            // prefab without waking it).
            parts.Begin();
            var shown = instance.AddComponent<GunSights>();
            shown.sights = sights.ToArray();
            shown.StartWith(sights.Count - 1);
            // Its own iron sights stay in its mesh here: the cut is made of the mesh as each gun wakes, not saved.
            shown.Seat(false);
            shown.Select(shown.initial);

            // The parts' own pack scripts expect the pack's player; their particles are its muzzle flash.
            for (int pass = 0; pass < 4; pass++)
                foreach (var script in instance.GetComponentsInChildren<MonoBehaviour>(true))
                    if (script != null && IsPack(script)) Object.DestroyImmediate(script);

            DropDemoLinks(instance.GetComponent<CasFPSWeapon>());
            var saved = PrefabUtility.SaveAsPrefabAsset(instance, $"{PackGunsFolder}/{spec.weapon}.prefab", out bool ok);
            return ok ? saved : null;
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    /// <summary>
    /// A gun that has no rows in GunMounts.csv yet gets the two that are no judgement: its eye place (KINEMATION's own aim
    /// point) and its muzzle (GunMuzzle's measure of its front face). Where its rails are, which pieces of its mesh are its
    /// iron sights and what each socket offers is measured and looked at outside the editor (Tools/Guns/README.md: the
    /// measure proposes rows and draws the gun with every part seated), then written into the table; until then the gun
    /// has its own sights and nothing else, and says so here.
    /// </summary>
    private static void RowsFor(string weapon, Transform gun, Transform aim)
    {
        if (GunMounts.Of(weapon, GunMounts.Eye).Found) return;
        string path = TablePaths.Of(GunMounts.Table);
        string text = TableFile.Read(path);
        if (string.IsNullOrEmpty(text)) return;
        GunMuzzle.Measure(gun, aim, .015f, out Vector3 muzzle, out Quaternion muzzleTurn);
        Vector3 eye = gun.InverseTransformPoint(aim.position);
        Vector3 eyeTurn = (Quaternion.Inverse(gun.rotation) * aim.rotation).eulerAngles, turn = muzzleTurn.eulerAngles;
        string N(float value) => Mathf.Abs(value) < 5e-5f ? "0" : value.ToString("0.####", CultureInfo.InvariantCulture);
        string A(float angle) => N(Mathf.DeltaAngle(0f, angle));
        string line = text.Contains("\r\n") ? "\r\n" : "\n";
        text = text.TrimEnd('\r', '\n') + line
            + $"{weapon}.{GunMounts.Eye},{N(eye.x)},{N(eye.y)},{N(eye.z)},{A(eyeTurn.x)},{A(eyeTurn.y)},{A(eyeTurn.z)},,,,," + line
            + $"{weapon}.{GunMounts.Muzzle},{N(muzzle.x)},{N(muzzle.y)},{N(muzzle.z)},{A(turn.x)},{A(turn.y)},{A(turn.z)},0,,,," + line;
        TableFile.Write(path, text, $"{weapon}의 눈 자리·총구 줄", "ENFILADE → 총 → FPS Animation Pack 총 만들기를 다시 눌러 주세요.");
        Debug.LogWarning($"TacticalGunSetup: {weapon} had no rows in GunMounts.csv. Its eye place and muzzle are written; its sight rail, the "
            + "parts it offers and its iron sights' boxes are still to be measured and added (Tools/Guns/README.md), so it has only its own sights for now.");
    }

    // One of the Tactical Shooter Pack's attachment prefabs by name, as its attachment script (what the gun setups read).
    private static Component Pick(string name)
    {
        foreach (string guid in AssetDatabase.FindAssets($"{name} t:Prefab", new[] { Pack }))
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));
            if (prefab == null || prefab.name != name) continue;
            foreach (var script in prefab.GetComponents<MonoBehaviour>())
                if (script != null && IsPack(script)) return script;
        }
        Debug.LogWarning($"TacticalGunSetup: the Tactical Shooter Pack's {name} wasn't found.");
        return null;
    }

    // Made without the gun-part player its CAS prefab carries (the MX16A4 since 0.8.1): made again once.
    private static bool PartsNotPlayed(GameObject prefab)
        => prefab.GetComponentInChildren<Animator>(true) != null && prefab.GetComponentInChildren<PartsPlayer>(true) == null;

    /// <summary>
    /// The gun-part clips (a reload's magazine out and in, the bolt) play through CAS's player on the gun's own Animator,
    /// as on KINEMATION's CAS guns and our Tactical ones. KINEMATION's CAS_FPSPack_MX16A4 adds its player, and takes the
    /// Animator's controller away, as overrides on an object of the model prefab it holds; unpacked, the MX16A4 came out
    /// with neither, so its reload clip never moved the magazine and it stayed in the gun through every reload while the
    /// hand went to the pouch (user, 2026-11-12: "M4는 왜 재장전할 때 탄창을 안 빼냐"). The rest pose is the source's
    /// player's where it has one.
    /// </summary>
    private static void PlayPartsThroughCas(GameObject instance, GameObject source)
    {
        var animator = instance.GetComponentInChildren<Animator>(true);
        if (animator == null) return;
        animator.runtimeAnimatorController = null;
        var player = animator.GetComponent<PartsPlayer>();
        if (player == null) player = animator.gameObject.AddComponent<PartsPlayer>();
        var write = new SerializedObject(player);
        var own = write.FindProperty("useAnimator");
        if (own != null) own.boolValue = false;
        var rest = write.FindProperty("defaultClipPose");
        var theirs = source != null ? source.GetComponentInChildren<PartsPlayer>(true) : null;
        if (rest != null && rest.objectReferenceValue == null && theirs != null)
            rest.objectReferenceValue = new SerializedObject(theirs).FindProperty("defaultClipPose")?.objectReferenceValue;
        write.ApplyModifiedPropertiesWithoutUndo();
    }

    // KINEMATION's Operator's left hand (its hand_l, which a grip's LeftHand stands for), in that bone's own turn: the way
    // the fingers point (wrist to the middle knuckle), the way the palm faces (as ArmIK takes it for a left hand), and the
    // palm's middle from the wrist (a little over half way to the knuckles, as HandGrip finds ours), in metres.
    private static bool operatorMeasured, operatorFound;

    private static Vector3 operatorFingers, operatorFacing, operatorPalm;

    private static bool OperatorLeftHand(out Vector3 fingers, out Vector3 facing, out Vector3 palm)
    {
        if (!operatorMeasured)
        {
            operatorMeasured = true;
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(Operator);
            if (model != null)
            {
                var copy = Object.Instantiate(model);
                try
                {
                    copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    copy.transform.localScale = Vector3.one;
                    var hand = Hierarchy.Find(copy.transform, "hand_l");
                    var index = Hierarchy.Find(copy.transform, "index_01_l");
                    var middle = Hierarchy.Find(copy.transform, "middle_01_l");
                    var ring = Hierarchy.Find(copy.transform, "ring_01_l");
                    var pinky = Hierarchy.Find(copy.transform, "pinky_01_l");
                    if (hand != null && index != null && middle != null && ring != null && pinky != null)
                    {
                        Quaternion toHand = Quaternion.Inverse(hand.rotation);
                        Vector3 along = middle.position - hand.position, thumbSide = index.position - pinky.position;
                        Vector3 face = -Vector3.Cross(along, thumbSide);
                        Vector3 knuckles = (index.position + middle.position + ring.position + pinky.position) * .25f;
                        operatorFingers = toHand * along.normalized;
                        operatorFacing = toHand * face.normalized;
                        operatorPalm = toHand * ((knuckles - hand.position) * .55f);
                        operatorFound = along.sqrMagnitude > 1e-8f && face.sqrMagnitude > 1e-10f;
                    }
                }
                finally
                {
                    Object.DestroyImmediate(copy);
                }
            }
            if (!operatorFound) Debug.LogWarning("TacticalGunSetup: the Operator's left hand bones weren't found; grips won't take the support hand.");
        }
        fingers = operatorFingers;
        facing = operatorFacing;
        palm = operatorPalm;
        return operatorFound;
    }

    private static bool IsPack(MonoBehaviour script)
    {
        string space = script.GetType().Namespace ?? string.Empty;
        return space.StartsWith("KINEMATION.TacticalShooterPack", StringComparison.Ordinal)
            || space.StartsWith("KINEMATION.KShooterCore", StringComparison.Ordinal);
    }

    private static MonoBehaviour FindPack(GameObject root, string typeName)
    {
        foreach (var script in root.GetComponentsInChildren<MonoBehaviour>(true))
            if (script != null && IsPack(script))
                for (var type = script.GetType(); type != null; type = type.BaseType)
                    if (type.Name == typeName) return script;
        return null;
    }
}
