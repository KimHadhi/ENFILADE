using System;
using System.Collections.Generic;
using System.IO;
using KINEMATION.CharacterAnimationSystem.Integrations.FPSAnimationPack.Scripts;
using UnityEditor;
using UnityEngine;
using PartsPlayer = KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables.SimpleAnimationPlayer;
using Object = UnityEngine.Object;

// TacticalGunSetup: The FPS Animation Pack's guns (MX16A4, M1911). One part of the class; the rest is in TacticalGunSetup.cs and the files beside it.
internal static partial class TacticalGunSetup
{
    // KINEMATION's FPS Animation Pack has no parts of its own for its two guns, so they wear the Tactical Shooter Pack's
    // (user, 2026-11-09: "담 단계"), put on mounts measured off each gun's own mesh: the muzzle's front face, the top of the
    // rail over the receiver, the underside of the handguard where KINEMATION's support hand holds it, and ahead of that. A
    // copy of the gun's CAS prefab carries them (Data/Guns/FPSPackGuns), and Resources/Combat/FPSBody points at it.
    private const string PackGunsFolder = ProjectFolders.Guns + "FPSPackGuns";

    private const string CasGuns = "Assets/ThirdParty/KINEMATION/CharacterAnimationSystem/Integrations/FPSAnimationPack/Prefabs/Weapons";

    private sealed class PackGun
    {
        public string weapon, source;
        public bool pistol;
        public string[] sights, muzzles, grips, underbarrel;
        // Iron sights modelled into the gun's body mesh: its pieces reaching higher than this (m) under the iron sight line
        // are them, cut out at run time so they fold away under an optic (GunSights.ironsInMesh). 0 leaves them on.
        public float ironsBelowSightLine;
        // The optics' mount goes as far ahead of the iron sight's aim point as the optics' own aim points sit behind
        // their mounts, so an optic's eye point meets the iron sight's and the optic sits on the receiver.
        public bool opticsMeetIrons;
    }

    // One of FPS Animation Ultimate's guns in the Tactical Shooter Pack's parts: a long gun the MX16A4's sights, a pistol the
    // M1911's red dot (unless given others), the MCS suppressor, the angled foregrip on a long gun, the LS3 light.
    private static PackGun Ultimate(string weapon, string cas, bool pistol = false, string[] sights = null, bool muzzle = true,
        bool grip = true, bool light = true) => new PackGun
    {
        weapon = weapon, source = CasGuns + "/CAS_FPSPack_" + cas + ".prefab", pistol = pistol,
        sights = sights ?? (pistol ? new[] { "A_MicroRedDot_Picatinny" } : new[] { "A_Holograph", "A_DMCO" }),
        muzzles = muzzle ? new[] { "A_MCS_Suppressor" } : Array.Empty<string>(),
        grips = grip && !pistol ? new[] { "A_AngledForegrip" } : Array.Empty<string>(),
        underbarrel = light ? new[] { "A_LS3_Flashlight" } : Array.Empty<string>(),
    };

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

    // How far either side of a point along the gun its surface is looked at (m), and how near the gun's middle line.
    private const float MountWindow = .03f, MountSide = .02f;

    private static bool FitPackGuns(FPSBodyAssets assets, bool all)
    {
        bool changed = false;
        var bakes = AssetDatabase.LoadAssetAtPath<GunBakes>(ProjectFolders.DataByName + GunBakes.ResourcePath + ".asset");
        foreach (var spec in PackGuns)
        {
            int index = Array.FindIndex(assets.guns, gun => gun.weapon != null && gun.weapon.name == spec.weapon);
            if (index < 0) continue;
            var current = assets.guns[index].prefab;
            if (!all && current != null && current.GetComponent<GunAttachments>() is GunAttachments made && !GripsWithoutHand(made)
                && !IronsNotCut(spec, current) && !PartsNotPlayed(current)) continue;
            try
            {
                var prefab = FitPackGun(spec, assets.guns[index].weapon, bakes);
                if (prefab == null) continue;
                assets.guns[index].prefab = prefab;
                changed = true;
                Debug.Log($"TacticalGunSetup: {spec.weapon} now wears the Tactical Shooter Pack's parts ({PackGunsFolder}).", prefab);
            }
            catch (Exception error)
            {
                Debug.LogWarning($"TacticalGunSetup: {spec.weapon}'s parts could not be fitted: {error.Message}\n{error.StackTrace}");
            }
        }
        return changed;
    }

    private static GameObject FitPackGun(PackGun spec, WeaponData weapon, GunBakes bakes)
    {
        var source = AssetDatabase.LoadAssetAtPath<GameObject>(spec.source);
        if (source == null) return null;
        Directory.CreateDirectory(PackGunsFolder);
        AssetDatabase.Refresh();
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
        try
        {
            PrefabUtility.UnpackPrefabInstance(instance, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            instance.name = spec.weapon;
            var gun = instance.transform;
            gun.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            var cas = instance.GetComponent<CasFPSWeapon>();
            var aim = cas != null && cas.aimPoint != null ? cas.aimPoint : gun;
            PlayPartsThroughCas(instance, source);

            // The gun's shape in its own frame: along the sight line (z), up (y) and across (x), its own parts only. Iron sights
            // modelled into the body are picked out first and left out of it, so the optics' mount finds the receiver.
            Quaternion frame = Quaternion.Inverse(gun.rotation) * Quaternion.LookRotation(aim.forward, aim.up);
            bool[] ironVertex = null;
            int[] ironTriangles = Array.Empty<int>();
            int ironTotal = 0;
            SkinnedMeshRenderer irons = null;
            if (spec.ironsBelowSightLine > 0f)
                irons = IronSightsIn(gun, frame, aim, spec.ironsBelowSightLine, out ironVertex, out ironTriangles, out ironTotal);
            var points = ShapeOf(gun, frame, aim, irons, ironVertex);
            GunMuzzle.Measure(gun, aim, .015f, out Vector3 muzzleAt, out Quaternion muzzleTurn);
            Vector3 muzzle = Quaternion.Inverse(frame) * (muzzleAt - gun.InverseTransformPoint(aim.position));
            // Where KINEMATION's support hand holds it: the bake's grip, kept in KINEMATION's weapon bone frame at the bake's
            // size (the gun model hangs in that frame turned by the gun's rotationOffset, at true size, FPSBody), else a
            // little under half way from the sight to the muzzle. Kept on the handguard.
            float hand = muzzle.z * .45f;
            if (bakes != null && bakes.entries != null)
                foreach (var entry in bakes.entries)
                    if (entry.weapon == weapon && entry.hasGrip && entry.skin != null)
                    {
                        Vector3 left = Quaternion.Inverse(FPSBody.GunFrameOf(cas)) * entry.leftGripPosition * GunClipRetarget.FitScaleOf(entry.skin);
                        hand = (Quaternion.Inverse(frame) * (left - gun.InverseTransformPoint(aim.position))).z;
                        break;
                    }
            hand = Mathf.Clamp(hand, .1f, Mathf.Max(.12f, muzzle.z - .08f));

            Transform Mount(string name, float z, bool top)
            {
                float y = top ? RailTop(points, z) : Underside(points, z);
                var mount = new GameObject(name).transform;
                mount.SetParent(gun, false);
                mount.localPosition = gun.InverseTransformPoint(aim.position) + frame * new Vector3(0f, y, z);
                mount.localRotation = frame;
                return mount;
            }

            var sightMount = Mount("SightMount", spec.pistol ? -.02f : .03f, true);
            var muzzleMount = new GameObject("MuzzleMount").transform;
            muzzleMount.SetParent(gun, false);
            muzzleMount.SetLocalPositionAndRotation(muzzleAt, muzzleTurn);
            var gripMount = Mount("GripMount", hand, false);
            var underMount = Mount("UnderMount", spec.pistol ? muzzle.z - .07f : hand + .1f, false);

            var sights = new List<GunSights.Sight>();
            foreach (string part in spec.sights)
                if (Pick(part) is Component prefab) sights.Add(SightChoice(prefab, Put(prefab, sightMount), aim));
            // The optics' eye points meet the iron sight's: the mount goes as far ahead of the iron aim point as their aim
            // points sit behind it (the MX16A4's holographic 15 cm, its DMCO 13.5), onto the receiver's top there.
            if (spec.opticsMeetIrons)
            {
                float behind = 0f;
                int counted = 0;
                foreach (var sight in sights)
                    if (sight.aimPoint != null && sight.aimPoint != aim)
                    {
                        behind += sightMount.InverseTransformPoint(sight.aimPoint.position).z;
                        counted++;
                    }
                if (counted > 0)
                {
                    float ahead = -behind / counted;
                    sightMount.localPosition = gun.InverseTransformPoint(aim.position) + frame * new Vector3(0f, RailTop(points, ahead), ahead);
                }
            }
            sights.Add(new GunSights.Sight { part = null, aimPoint = aim, ironSight = true });
            var none = new GunAttachments.Choice { kind = GunAttachments.Kind.None };
            var muzzles = new List<GunAttachments.Choice> { none };
            var grips = new List<GunAttachments.Choice> { none };
            var under = new List<GunAttachments.Choice> { none };
            foreach (string part in spec.muzzles) if (Pick(part) is Component prefab) muzzles.Add(ChoiceOf(prefab, Put(prefab, muzzleMount), gun));
            foreach (string part in spec.grips) if (Pick(part) is Component prefab) grips.Add(ChoiceOf(prefab, Put(prefab, gripMount), gun));
            foreach (string part in spec.underbarrel) if (Pick(part) is Component prefab) under.Add(ChoiceOf(prefab, Put(prefab, underMount), gun));

            var parts = instance.AddComponent<GunAttachments>();
            parts.muzzle = muzzles.Count > 1 ? muzzles.ToArray() : Array.Empty<GunAttachments.Choice>();
            parts.grips = grips.Count > 1 ? grips.ToArray() : Array.Empty<GunAttachments.Choice>();
            parts.underbarrel = under.Count > 1 ? under.ToArray() : Array.Empty<GunAttachments.Choice>();
            parts.initialUnderbarrel = Mathf.Max(0, under.FindIndex(choice => choice.kind == GunAttachments.Kind.Flashlight));
            parts.Begin();
            // The gun's own iron sights first, as it came.
            var shown = instance.AddComponent<GunSights>();
            shown.sights = sights.ToArray();
            shown.initial = sights.Count - 1;
            if (irons != null)
            {
                shown.ironsInMesh = irons;
                shown.ironTriangles = ironTriangles;
                shown.ironTriangleTotal = ironTotal;
            }
            shown.Select(shown.initial);

            // The parts' own pack scripts expect the pack's player; their particles are its muzzle flash.
            for (int pass = 0; pass < 4; pass++)
                foreach (var script in instance.GetComponentsInChildren<MonoBehaviour>(true))
                    if (script != null && IsPack(script)) Object.DestroyImmediate(script);

            var saved = PrefabUtility.SaveAsPrefabAsset(instance, $"{PackGunsFolder}/{spec.weapon}.prefab", out bool ok);
            return ok ? saved : null;
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
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

    // The gun's mesh points in the sight line's frame from the aim point (x across, y up, z along), its parts as it stands.
    // The vertices of skipIn marked in skip are left out (iron sights modelled into the body, IronSightsIn).
    private static List<Vector3> ShapeOf(Transform gun, Quaternion frame, Transform aim, Renderer skipIn = null, bool[] skip = null)
    {
        var points = new List<Vector3>();
        var vertices = new List<Vector3>();
        Vector3 origin = gun.InverseTransformPoint(aim.position);
        Quaternion toFrame = Quaternion.Inverse(frame);
        foreach (var renderer in gun.GetComponentsInChildren<Renderer>(true))
        {
            Mesh mesh = null;
            Matrix4x4 toWorld;
            bool baked = false;
            if (renderer is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
            {
                mesh = new Mesh();
                skinned.BakeMesh(mesh, true);
                baked = true;
                toWorld = Matrix4x4.TRS(skinned.transform.position, skinned.transform.rotation, Vector3.one);
            }
            else if (renderer is MeshRenderer && renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
            {
                mesh = filter.sharedMesh;
                toWorld = renderer.transform.localToWorldMatrix;
            }
            else continue;
            mesh.GetVertices(vertices);
            bool skipping = skip != null && skipIn != null && renderer == skipIn && skip.Length == vertices.Count;
            for (int i = 0; i < vertices.Count; i++)
                if (!skipping || !skip[i]) points.Add(toFrame * (gun.InverseTransformPoint(toWorld.MultiplyPoint3x4(vertices[i])) - origin));
            if (baked) Object.DestroyImmediate(mesh);
        }
        return points;
    }

    // Made before the MX16A4's iron sights folded away under optics (2026-11-10): made again once.
    private static bool IronsNotCut(PackGun spec, GameObject prefab)
    {
        if (spec.ironsBelowSightLine <= 0f) return false;
        var sights = prefab.GetComponent<GunSights>();
        return sights == null || sights.ironsInMesh == null;
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

    // The gun's body mesh (its biggest skinned one) and which of its vertices and triangles are iron sights modelled into
    // it: the mesh's pieces (vertices at one place welded, as a hard edge or a seam splits them) whose highest point is
    // less than `below` under the iron sight line (the aim point's height), with the gun posed as it stands. Triangles
    // are numbered through the submeshes in order, as GunSights cuts them. Null when there are none or it can't be read.
    private static SkinnedMeshRenderer IronSightsIn(Transform gun, Quaternion frame, Transform aim, float below,
        out bool[] ironVertex, out int[] ironTriangles, out int total)
    {
        ironVertex = null;
        ironTriangles = Array.Empty<int>();
        total = 0;
        SkinnedMeshRenderer body = null;
        foreach (var skinned in gun.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            if (skinned.sharedMesh != null && (body == null || skinned.sharedMesh.vertexCount > body.sharedMesh.vertexCount))
                body = skinned;
        if (body == null) return null;
        var mesh = body.sharedMesh;
        if (!mesh.isReadable)
        {
            Debug.LogWarning($"TacticalGunSetup: {mesh.name} can't be read, so {gun.name}'s iron sights stay on under optics (switch Read/Write on).");
            return null;
        }

        var raw = new List<Vector3>();
        mesh.GetVertices(raw);
        int count = raw.Count;
        var posedMesh = new Mesh();
        body.BakeMesh(posedMesh, true);
        var posed = new List<Vector3>();
        posedMesh.GetVertices(posed);
        Object.DestroyImmediate(posedMesh);
        if (posed.Count != count) return null;

        var root = new int[count];
        for (int i = 0; i < count; i++) root[i] = i;
        int Find(int a)
        {
            while (root[a] != a) { root[a] = root[root[a]]; a = root[a]; }
            return a;
        }
        void Join(int a, int b)
        {
            a = Find(a);
            b = Find(b);
            if (a != b) root[a] = b;
        }
        // One piece: vertices at one place (to a hundredth of a millimetre), and the corners of each triangle.
        var place = new Dictionary<Vector3Int, int>();
        for (int i = 0; i < count; i++)
        {
            var key = new Vector3Int(Mathf.RoundToInt(raw[i].x * 1e5f), Mathf.RoundToInt(raw[i].y * 1e5f), Mathf.RoundToInt(raw[i].z * 1e5f));
            if (place.TryGetValue(key, out int first)) Join(i, first);
            else place[key] = i;
        }
        var triangles = new List<int>();
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            mesh.GetTriangles(triangles, s);
            for (int t = 0; t + 2 < triangles.Count; t += 3)
            {
                Join(triangles[t], triangles[t + 1]);
                Join(triangles[t], triangles[t + 2]);
            }
        }

        // Each piece's highest point over the iron sight line.
        var toWorld = Matrix4x4.TRS(body.transform.position, body.transform.rotation, Vector3.one);
        Quaternion toFrame = Quaternion.Inverse(frame);
        Vector3 origin = gun.InverseTransformPoint(aim.position);
        var top = new Dictionary<int, float>();
        for (int i = 0; i < count; i++)
        {
            float height = (toFrame * (gun.InverseTransformPoint(toWorld.MultiplyPoint3x4(posed[i])) - origin)).y;
            int piece = Find(i);
            top[piece] = top.TryGetValue(piece, out float highest) ? Mathf.Max(highest, height) : height;
        }
        ironVertex = new bool[count];
        for (int i = 0; i < count; i++) ironVertex[i] = top[Find(i)] > -below;

        var irons = new List<int>();
        int number = 0;
        for (int s = 0; s < mesh.subMeshCount; s++)
        {
            mesh.GetTriangles(triangles, s);
            for (int t = 0; t + 2 < triangles.Count; t += 3, number++)
                if (ironVertex[triangles[t]]) irons.Add(number);
        }
        total = number;
        ironTriangles = irons.ToArray();
        if (irons.Count == 0) return null;
        Debug.Log($"TacticalGunSetup: {gun.name}'s iron sights are {irons.Count} of its {number} triangles; they fold away under optics.");
        return body;
    }

    // The top of the rail at z along the sight line: the median of the highest points in slices about it, so a sight post
    // or a charging handle sticking up in one slice doesn't lift the mount.
    private static float RailTop(List<Vector3> points, float z)
    {
        var tops = new List<float>();
        for (float slice = z - .06f; slice <= z + .06f; slice += .01f)
        {
            float top = float.NegativeInfinity;
            foreach (var point in points)
                if (Mathf.Abs(point.z - slice) <= .005f && Mathf.Abs(point.x) <= MountSide) top = Mathf.Max(top, point.y);
            if (!float.IsNegativeInfinity(top)) tops.Add(top);
        }
        if (tops.Count == 0) return 0f;
        tops.Sort();
        return tops[tops.Count / 2];
    }

    // The underside of the gun at z along the sight line, under its middle line.
    private static float Underside(List<Vector3> points, float z)
    {
        float bottom = float.PositiveInfinity;
        foreach (var point in points)
            if (Mathf.Abs(point.z - z) <= MountWindow && Mathf.Abs(point.x) <= MountSide) bottom = Mathf.Min(bottom, point.y);
        return float.IsPositiveInfinity(bottom) ? -.05f : bottom;
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
