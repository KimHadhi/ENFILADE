using System;
using System.Collections.Generic;
using System.IO;
using KINEMATION.CharacterAnimationSystem.Integrations.FPSAnimationPack.Scripts;
using KINEMATION.FPSAnimationPack.Scripts.Weapon;
using UnityEditor;
using UnityEngine;
using CasAnimation = KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables.AnimationAsset;
using PartsPlayer = KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables.SimpleAnimationPlayer;
using Object = UnityEngine.Object;

// TacticalGunSetup: The gun. One part of the class; the rest is in TacticalGunSetup.cs and the files beside it.
internal static partial class TacticalGunSetup
{
    // The pack's suppressed shots for the gun (its settings' suppressedFireSounds), into our WeaponData, unless it has its own.
    private static void SuppressedSounds(GameObject source, WeaponData weapon)
    {
        var packWeapon = FindPack(source, "TacticalShooterWeapon");
        var packSettings = packWeapon != null ? new SerializedObject(packWeapon).FindProperty("tacWeaponSettings")?.objectReferenceValue : null;
        var sounds = packSettings != null ? new SerializedObject(packSettings).FindProperty("suppressedFireSounds") : null;
        var write = new SerializedObject(weapon);
        var mine = write.FindProperty("suppressedFireSounds");
        if (sounds == null || mine == null || mine.arraySize > 0) return;
        for (int i = 0; i < sounds.arraySize; i++)
        {
            if (!(sounds.GetArrayElementAtIndex(i).objectReferenceValue is AudioClip clip)) continue;
            mine.InsertArrayElementAtIndex(mine.arraySize);
            mine.GetArrayElementAtIndex(mine.arraySize - 1).objectReferenceValue = clip;
        }
        write.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(weapon);
    }

    private static GameObject MakePrefab(GameObject source, string path, FPSWeaponSettings settings, AnimationClip idle, CasFPSWeapon rifle,
        CasAnimation fire, CasAnimation fireOut, CasAnimation reloadTactical, CasAnimation reloadEmpty, string builtInSuppressor)
    {
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
        try
        {
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = Path.GetFileNameWithoutExtension(path);
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            // The gun's own Animator, found before any attachment (which may bring one) goes on.
            var animator = instance.GetComponentInChildren<Animator>(true);

            // What the pack's own gun script knew: its sight, and the bones it keeps hidden.
            Transform aimPoint = null;
            var weapon = FindPack(instance, "TacticalShooterWeapon");
            if (weapon != null)
            {
                var read = new SerializedObject(weapon);
                aimPoint = read.FindProperty("aimPoint")?.objectReferenceValue as Transform;
                var hidden = read.FindProperty("bonesToHide");
                for (int i = 0; hidden != null && i < hidden.arraySize; i++)
                {
                    var bone = hidden.GetArrayElementAtIndex(i);
                    if (bone.FindPropertyRelative("isHidden").boolValue && bone.FindPropertyRelative("bone").objectReferenceValue is Transform shut)
                        shut.localScale = Vector3.zero;
                }
            }
            var sightAim = PutOnAttachments(instance, aimPoint, builtInSuppressor);
            if (sightAim != null) aimPoint = sightAim;

            // The pack's scripts expect its own player; its particle systems are its muzzle flash and brass.
            for (int pass = 0; pass < 4; pass++)
                foreach (var script in instance.GetComponentsInChildren<MonoBehaviour>(true))
                    if (script != null && IsPack(script)) Object.DestroyImmediate(script);
            foreach (var particles in instance.GetComponentsInChildren<ParticleSystem>(true))
            {
                if (particles == null) continue;
                var main = particles.main;
                main.playOnAwake = false;
                // Off altogether, unless it shares its object with a part of the gun itself.
                bool part = particles.gameObject == instance || particles.GetComponentsInChildren<MeshRenderer>(true).Length > 0
                    || particles.GetComponentsInChildren<SkinnedMeshRenderer>(true).Length > 0;
                if (!part) particles.gameObject.SetActive(false);
            }

            // The gun-part clips through CAS's player, the gun at rest on its idle clip.
            if (animator != null)
            {
                animator.runtimeAnimatorController = null;
                var player = animator.gameObject.GetComponent<PartsPlayer>();
                if (player == null) player = animator.gameObject.AddComponent<PartsPlayer>();
                // A graph of its own, as KINEMATION's CAS guns have it: the Animator has no controller to build on.
                var write = new SerializedObject(player);
                var rest = write.FindProperty("defaultClipPose");
                if (rest != null) rest.objectReferenceValue = idle;
                var own = write.FindProperty("useAnimator");
                if (own != null) own.boolValue = false;
                write.ApplyModifiedPropertiesWithoutUndo();
            }

            var cas = instance.GetComponent<CasFPSWeapon>();
            if (cas == null) cas = instance.AddComponent<CasFPSWeapon>();
            cas.weaponSettings = settings;
            cas.aimPoint = aimPoint != null ? aimPoint : instance.transform;
            cas.fire = new FPSAnimationPair { weapon = fire };
            cas.fireOut = new FPSAnimationPair { weapon = fireOut };
            cas.reloadTactical = new FPSAnimationPair { weapon = reloadTactical };
            cas.reloadEmpty = new FPSAnimationPair { weapon = reloadEmpty };
            // The rifle's camera shake (a CAS asset, which FPSGun copies into FPSAF's); semi-automatic.
            var casWrite = new SerializedObject(cas);
            var shake = casWrite.FindProperty("cameraShake");
            var rifleShake = new SerializedObject(rifle).FindProperty("cameraShake");
            if (shake != null && rifleShake != null) shake.objectReferenceValue = rifleShake.objectReferenceValue;
            var mode = casWrite.FindProperty("fireMode");
            if (mode != null) mode.enumValueIndex = 0;
            casWrite.ApplyModifiedPropertiesWithoutUndo();

            var made = PrefabUtility.SaveAsPrefabAsset(instance, path, out bool saved);
            return saved ? made : null;
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    // The pack's attachments, as its TacticalWeaponAttachments puts them on: every grip, muzzle and underbarrel choice on its
    // mount for GunAttachments to switch between (the customize menu, C), nothing on the muzzle or grip to start with, and every sight on the
    // sight mount for GunSights to switch between (the same menu), the pack's own pick shown. A magnified scope gets a GunScope with the pack's numbers, which renders its lens as the pack's
    // ScopeAttachment did. The gun's own iron sights are a choice too when it has them. Returns the shown sight's aim point.
    private static Transform PutOnAttachments(GameObject gun, Transform gunAim, string builtInSuppressor)
    {
        var list = new List<GunSights.Sight>();
        var irons = new List<GameObject>();
        int initial = 0;
        // The muzzle, grip and underbarrel choices (GunAttachments), each starting with nothing there.
        var none = new GunAttachments.Choice { kind = GunAttachments.Kind.None };
        var muzzles = new List<GunAttachments.Choice> { none };
        var grips = new List<GunAttachments.Choice> { none };
        var under = new List<GunAttachments.Choice> { none };
        var attachments = FindPack(gun, "TacticalWeaponAttachments");
        if (attachments != null)
        {
            var read = new SerializedObject(attachments);
            var ironList = read.FindProperty("ironSightObjects");
            for (int i = 0; ironList != null && i < ironList.arraySize; i++)
                if (ironList.GetArrayElementAtIndex(i).objectReferenceValue is GameObject iron) irons.Add(iron);
            foreach (string group in new[] { "scopes", "grips", "muzzle", "underbarrel" })
            {
                var mount = read.FindProperty($"{group}.attachmentPoint")?.objectReferenceValue as Transform;
                var choices = read.FindProperty($"{group}.attachments");
                if (mount == null || choices == null || choices.arraySize == 0) continue;
                int pick = Mathf.Clamp(read.FindProperty($"{group}.initialIndex")?.intValue ?? 0, 0, choices.arraySize - 1);
                if (group != "scopes")
                {
                    // Every choice on the mount for GunAttachments to switch between; the pack's empty entries are its "none".
                    var into = group == "grips" ? grips : group == "muzzle" ? muzzles : under;
                    for (int i = 0; i < choices.arraySize; i++)
                        if (choices.GetArrayElementAtIndex(i).objectReferenceValue is Component other)
                            into.Add(ChoiceOf(other, Put(other, mount), gun.transform));
                    continue;
                }
                // A sight's choice is a selection: its prefab (and secondary scopes, left off here).
                for (int i = 0; i < choices.arraySize; i++)
                {
                    if (!(choices.GetArrayElementAtIndex(i).FindPropertyRelative("prefab")?.objectReferenceValue is Component prefab)) continue;
                    var put = Put(prefab, mount);
                    var entry = SightChoice(prefab, put, gunAim);
                    if (i == pick) initial = list.Count;
                    list.Add(entry);
                }
            }
        }
        // A suppressor that is part of the gun's own model is a muzzle choice too, shown by its size.
        if (!string.IsNullOrEmpty(builtInSuppressor) && Hierarchy.Find(gun.transform, builtInSuppressor) is Transform built)
            muzzles.Add(new GunAttachments.Choice { kind = GunAttachments.Kind.Suppressor, bone = built });
        // Every gun gets one, empty or not, so a gun made before them is made again once (Run).
        var parts = gun.AddComponent<GunAttachments>();
        parts.muzzle = muzzles.Count > 1 ? muzzles.ToArray() : Array.Empty<GunAttachments.Choice>();
        parts.grips = grips.Count > 1 ? grips.ToArray() : Array.Empty<GunAttachments.Choice>();
        parts.underbarrel = under.Count > 1 ? under.ToArray() : Array.Empty<GunAttachments.Choice>();
        // Nothing on the muzzle or under the grip to start with, as a Siege gun comes; a flashlight stays on its rail, off.
        parts.initialUnderbarrel = Mathf.Max(0, under.FindIndex(choice => choice.kind == GunAttachments.Kind.Flashlight));
        parts.Begin();

        // The gun's own iron sights, a choice of their own when it has any, and the only one with no sights to pick from.
        if (gunAim != null && (irons.Count > 0 || list.Count == 0))
            list.Add(new GunSights.Sight { part = null, aimPoint = gunAim, ironSight = true });
        if (list.Count == 0) return null;
        var sights = gun.AddComponent<GunSights>();
        sights.sights = list.ToArray();
        sights.ironSights = irons.ToArray();
        sights.initial = Mathf.Clamp(initial, 0, list.Count - 1);
        sights.Select(sights.initial);
        return sights.AimPoint;
    }

    private static GameObject Put(Component prefab, Transform mount)
    {
        var put = Object.Instantiate(prefab.gameObject, mount, false);
        put.name = prefab.gameObject.name;
        put.transform.localPosition = Vector3.zero;
        put.transform.localRotation = Quaternion.identity;
        put.SetActive(true);
        return put;
    }

    // What the pack's attachment script on the part said it is, read before the pack's scripts come off.
    private static GunAttachments.Choice ChoiceOf(Component prefab, GameObject put, Transform gunRoot)
    {
        var choice = new GunAttachments.Choice { part = put };
        var script = put.GetComponent(prefab.GetType());
        var read = script != null ? new SerializedObject(script) : null;
        string type = prefab.GetType().Name;
        if (type.Contains("Muzzle"))
            choice.kind = read?.FindProperty("isSuppressed")?.boolValue == true ? GunAttachments.Kind.Suppressor : GunAttachments.Kind.None;
        else if (type.Contains("Grip"))
        {
            choice.kind = put.name.IndexOf("Angled", StringComparison.OrdinalIgnoreCase) >= 0
                ? GunAttachments.Kind.AngledGrip : GunAttachments.Kind.VerticalGrip;
            // The pack's LeftHand is where its Operator's left wrist goes, turned as that hand bone; kept as where the
            // palm's middle goes and the ways the fingers point and the palm faces, which any hand can take (ArmIK).
            if (read?.FindProperty("attachmentData.ikTarget")?.objectReferenceValue is Transform wrist
                && OperatorLeftHand(out Vector3 fingers, out Vector3 facing, out Vector3 palm))
            {
                choice.hasHand = true;
                choice.handPalm = gunRoot.InverseTransformPoint(wrist.position + wrist.rotation * palm);
                choice.handFingers = gunRoot.InverseTransformDirection(wrist.rotation * fingers);
                choice.handFacing = gunRoot.InverseTransformDirection(wrist.rotation * facing);
            }
        }
        else if (type.Contains("Flashlight"))
        {
            choice.kind = GunAttachments.Kind.Flashlight;
            choice.light = read?.FindProperty("flashlight")?.objectReferenceValue as Light;
            choice.lens = read?.FindProperty("emissiveRenderer")?.objectReferenceValue as Renderer;
            choice.lensGlow = read?.FindProperty("emissionStrength")?.floatValue ?? 1f;
        }
        return choice;
    }

    // A sight put on its mount, as GunSights keeps it: where the eye lines up through it, whether it is an iron sight, and a
    // magnified scope's lens (GunScope, with the pack's numbers), read before the pack's scripts come off.
    private static GunSights.Sight SightChoice(Component prefab, GameObject put, Transform gunAim)
    {
        var sight = put.GetComponent(prefab.GetType());
        var read = sight != null ? new SerializedObject(sight) : null;
        var aim = read?.FindProperty("sightData.aimPoint")?.objectReferenceValue as Transform;
        var entry = new GunSights.Sight
        {
            part = put,
            aimPoint = aim != null ? aim : gunAim,
            ironSight = read?.FindProperty("isIronSight")?.boolValue ?? false,
        };
        if (read != null && prefab.GetType().Name.Contains("Scope"))
        {
            var scope = put.AddComponent<GunScope>();
            scope.lens = read.FindProperty("lens")?.objectReferenceValue as Renderer;
            scope.capturePoint = read.FindProperty("capturePoint")?.objectReferenceValue as Transform;
            scope.magnification = read.FindProperty("magnification")?.floatValue ?? 1f;
            scope.textureResolution = read.FindProperty("textureResolution")?.intValue ?? 1024;
            scope.captureFov = read.FindProperty("captureFov")?.floatValue ?? 90f;
            entry.scope = scope;
        }
        return entry;
    }
}
