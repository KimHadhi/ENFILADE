using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Puts a rigid piece made for anyone on the face of whoever wears it, in the slots that fit to the face (OutfitSlot.fit
/// "face": the mask slot). The welding mask from the Heist pack (the user, 2026-11-13: "얼굴에 맞게 알잘딱으로") is one
/// rigid shell sized for a person, and nothing in it says where this character's eyes are; anime faces and modders'
/// avatars all differ. So the face is measured from the character's own meshes as made, each time it is dressed:
/// - the eyes: the middle of the eye bones (else a little above the head bone);
/// - how far the face stands out in front of them: the front-most point hanging from the head (any vertex whose
///   strongest bone is the head or one under it: skin, and hair or ears over the face) below the eyes at the middle
///   (the nose) and at eye height (the brow, or bangs hanging in front of the eyes).
/// The piece's glass (its see-through part: a transparent material, or one named glass, lens or visor) then goes at eye
/// height, turned the way the character faces, its inside `clearance` in front of that. Where the shell's back-most edge
/// would stand in the body below the head (a short neck, a big chest), it tips forward at the bottom just enough to
/// clear it, as a welder's helmet does. The piece keeps its maker's size, and follows the head bone.
/// Measured on the FBX files (2026-11-13): eyes to nose 1.9-5.5 cm, to the brow or bangs 2.2-7.3 cm, so the glass stands
/// 3.5 (Hiro) to 8.6 cm (Mayo, for her bangs) in front of the eyes; no face then comes through the shell, and of the hair
/// only a few vertices stand out through it (Mayo 9, Kazusa 9, Ichigo 44, Hiro 81), which OutfitDresser presses in under
/// it. Ichigo's chest came up to 33 mm through the shell's lower edge upright, and nothing at a 6° tilt.
/// </summary>
internal static class FaceFit
{
    public const string TableName = "FaceFit";

    private struct Values
    {
        public float Clearance, EyeLift, NoseWidth, NoseBelow, BrowWidth, BrowHeight, Front, ChestClear, MostTilt, Hide;
    }

    // The body below the head that the shell's lower part may meet (metres from the eyes): under the chin, within reach
    // of a mask's width, not far behind the eyes.
    private const float ChestTop = .04f, ChestBottom = .35f, ChestSide = .15f, ChestBack = .05f;
    // The shell is read in columns this wide (x, y) for its back-most point in each.
    private const float Column = .02f;

    private static Values? values;
    private static bool listening;
    private static readonly HashSet<string> warned = new HashSet<string>();

    /// <summary>A face as measured, in the model's space (x right, y up, z forward).</summary>
    internal sealed class Face
    {
        public Vector3 Eyes;
        /// <summary>How far in front of the eyes the face stands (nose, brow or bangs), metres.</summary>
        public float Front;
        /// <summary>Points of the body below the head in front of the chest, for the shell's lower edge to clear.</summary>
        public readonly List<Vector3> Chest = new List<Vector3>();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        values = null;
        warned.Clear();
    }

    /// <summary>How much of a head piece a face piece must cover for the piece to go out of sight (FaceFit.csv hide).</summary>
    internal static float HideShare => Settings().Hide;

    /// <summary>
    /// The face of a fresh, unposed model, from its own meshes (<paramref name="own"/>) as they show now with their keys;
    /// null without a head bone.
    /// </summary>
    internal static Face Measure(Transform root, Animator animator, Renderer[] own)
    {
        var head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        if (root == null || head == null || own == null) return null;
        var settings = Settings();
        var toModel = root.worldToLocalMatrix;
        var face = new Face();
        var leftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye);
        var rightEye = animator.GetBoneTransform(HumanBodyBones.RightEye);
        face.Eyes = leftEye != null && rightEye != null
            ? toModel.MultiplyPoint3x4((leftEye.position + rightEye.position) * .5f)
            : toModel.MultiplyPoint3x4(head.position) + Vector3.up * settings.EyeLift;
        float nose = float.MinValue, brow = float.MinValue;
        foreach (var renderer in own)
        {
            if (renderer is not SkinnedMeshRenderer skinned || !skinned.enabled || !skinned.gameObject.activeInHierarchy) continue;
            var mesh = skinned.sharedMesh;
            if (mesh == null || !mesh.isReadable) continue;
            var bones = skinned.bones;
            var underHead = new bool[bones.Length];
            for (int i = 0; i < bones.Length; i++) underHead[i] = bones[i] != null && (bones[i] == head || bones[i].IsChildOf(head));
            var perVertex = mesh.GetBonesPerVertex();
            var weights = mesh.GetAllBoneWeights();
            if (perVertex.Length != mesh.vertexCount) continue;
            var at = UnderwearFit.Posed(skinned, mesh, toModel * skinned.transform.localToWorldMatrix);
            int next = 0;
            for (int v = 0; v < at.Length; v++)
            {
                int count = perVertex[v];
                // A vertex's weights come strongest first.
                int strongest = count > 0 && next < weights.Length ? weights[next].boneIndex : -1;
                next += count;
                var p = at[v] - face.Eyes;
                bool onTheHead = strongest >= 0 && strongest < underHead.Length && underHead[strongest];
                if (onTheHead)
                {
                    if (Mathf.Abs(p.x) < settings.NoseWidth && p.y < -.005f && p.y > -settings.NoseBelow) nose = Mathf.Max(nose, p.z);
                    if (Mathf.Abs(p.x) < settings.BrowWidth && Mathf.Abs(p.y) < settings.BrowHeight) brow = Mathf.Max(brow, p.z);
                }
                else if (p.y < -ChestTop && p.y > -ChestBottom && Mathf.Abs(p.x) < ChestSide && p.z > -ChestBack) face.Chest.Add(at[v]);
            }
        }
        face.Front = Mathf.Max(nose, brow);
        if (face.Front == float.MinValue) face.Front = settings.Front;
        return face;
    }

    /// <summary>
    /// Turns and moves a freshly placed piece (<paramref name="instance"/>, standing at the character's origin) onto the
    /// measured face, then gives it to <paramref name="anchor"/> (the head bone) to follow. False when it has no mesh to
    /// read.
    /// </summary>
    internal static bool Put(Transform instance, Transform root, Face face, Transform anchor)
    {
        if (instance == null || root == null || face == null) return false;
        var settings = Settings();
        var toModel = root.worldToLocalMatrix;
        // The piece as it stands, and its glass, in the model's space.
        var shell = new List<Vector3>();
        var glass = new List<Vector3>();
        var triangles = new List<int>();
        foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
        {
            var mesh = renderer is SkinnedMeshRenderer skinned ? skinned.sharedMesh
                : renderer.TryGetComponent(out MeshFilter filter) ? filter.sharedMesh : null;
            if (mesh == null || !mesh.isReadable) continue;
            var matrix = toModel * renderer.transform.localToWorldMatrix;
            int first = shell.Count;
            foreach (var vertex in mesh.vertices) shell.Add(matrix.MultiplyPoint3x4(vertex));
            var materials = renderer.sharedMaterials;
            for (int sub = 0; sub < mesh.subMeshCount && sub < materials.Length; sub++)
            {
                if (!IsGlass(materials[sub]) || mesh.GetTopology(sub) != MeshTopology.Triangles) continue;
                mesh.GetTriangles(triangles, sub);
                foreach (int index in triangles) glass.Add(shell[first + index]);
            }
        }
        if (shell.Count == 0)
        {
            Warn($"{instance.name}: 메시를 읽을 수 없어(Read/Write 꺼짐) 얼굴에 맞추지 못합니다.");
            return false;
        }
        Vector3 middle = Middle(shell), eyeOf, ahead;
        float thickness = 0f;
        if (glass.Count >= 3)
        {
            // The glass is a thin plate: it faces along the way it spreads least, out from the shell's middle.
            eyeOf = Middle(glass);
            ahead = Thinnest(glass, eyeOf);
            if (Vector3.Dot(ahead, eyeOf - middle) < 0f) ahead = -ahead;
            float near = float.MaxValue, far = float.MinValue;
            foreach (var point in glass)
            {
                float along = Vector3.Dot(point - eyeOf, ahead);
                near = Mathf.Min(near, along);
                far = Mathf.Max(far, along);
            }
            thickness = far - near;
        }
        else
        {
            // No glass: the piece's own front (+Z as made) faces out, its middle at the eyes.
            eyeOf = middle;
            ahead = toModel.MultiplyVector(instance.forward).normalized;
            Warn($"{instance.name}: 유리(투명 재질이나 이름에 glass·lens·visor)가 없어 모델의 앞(+Z)과 가운데로 얼굴에 맞춥니다.");
        }
        var up = toModel.MultiplyVector(instance.up);
        up -= ahead * Vector3.Dot(up, ahead);
        if (up.sqrMagnitude < 1e-8f) up = Vector3.up;
        // Upright first: the glass turned to face forward, at eye height, its inside clear of the face.
        var turn = Quaternion.Inverse(Quaternion.LookRotation(ahead, up.normalized));
        var to = face.Eyes + Vector3.forward * (face.Front + settings.Clearance + thickness * .5f);
        float tilt = Tilt(shell, eyeOf, turn, to, face, settings);
        turn = Quaternion.AngleAxis(-tilt, Vector3.right) * turn;
        var worldTurn = root.rotation * turn * Quaternion.Inverse(root.rotation);
        var eyeWorld = root.TransformPoint(eyeOf);
        var toWorld = root.TransformPoint(to);
        instance.SetPositionAndRotation(toWorld + worldTurn * (instance.position - eyeWorld), worldTurn * instance.rotation);
        if (anchor != null) instance.SetParent(anchor, true);
        return true;
    }

    // How far (degrees) to tip the shell's bottom forward, about its glass, so its back-most edge clears the body below
    // the head: per column of the shell, how far the body there stands in front of the shell's back-most point, over how
    // far below the glass it is. 0 when nothing meets it.
    private static float Tilt(List<Vector3> shell, Vector3 eyeOf, Quaternion turn, Vector3 to, Face face, Values settings)
    {
        if (face.Chest.Count == 0 || settings.MostTilt <= 0f) return 0f;
        var back = new Dictionary<Vector2Int, float>();
        foreach (var point in shell)
        {
            var placed = to + turn * (point - eyeOf);
            var key = new Vector2Int(Mathf.FloorToInt(placed.x / Column), Mathf.FloorToInt(placed.y / Column));
            if (!back.TryGetValue(key, out float z) || placed.z < z) back[key] = placed.z;
        }
        float tilt = 0f;
        foreach (var point in face.Chest)
        {
            float drop = to.y - point.y;
            if (drop <= 0f) continue;
            var key = new Vector2Int(Mathf.FloorToInt(point.x / Column), Mathf.FloorToInt(point.y / Column));
            if (!back.TryGetValue(key, out float z)) continue;
            float need = point.z + settings.ChestClear - z;
            if (need <= 0f) continue;
            tilt = Mathf.Max(tilt, Mathf.Asin(Mathf.Clamp01(need / drop)) * Mathf.Rad2Deg);
        }
        return Mathf.Min(tilt, settings.MostTilt);
    }

    // See-through: a transparent material (past the opaque queues) or one named as glass.
    private static bool IsGlass(Material material)
    {
        if (material == null) return false;
        if (material.renderQueue > 2500) return true;
        string name = material.name;
        return name.IndexOf("glass", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("lens", StringComparison.OrdinalIgnoreCase) >= 0
            || name.IndexOf("visor", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static Vector3 Middle(List<Vector3> points)
    {
        var sum = Vector3.zero;
        foreach (var point in points) sum += point;
        return sum / points.Count;
    }

    // The direction a cloud of points spreads least along: the smallest axis of its covariance (Jacobi turns).
    private static Vector3 Thinnest(List<Vector3> points, Vector3 middle)
    {
        var a = new double[3, 3];
        foreach (var point in points)
        {
            var d = point - middle;
            a[0, 0] += d.x * d.x; a[0, 1] += d.x * d.y; a[0, 2] += d.x * d.z;
            a[1, 1] += d.y * d.y; a[1, 2] += d.y * d.z; a[2, 2] += d.z * d.z;
        }
        a[1, 0] = a[0, 1]; a[2, 0] = a[0, 2]; a[2, 1] = a[1, 2];
        var v = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
        for (int sweep = 0; sweep < 32; sweep++)
        {
            int p = 0, q = 1;
            double largest = Math.Abs(a[0, 1]);
            if (Math.Abs(a[0, 2]) > largest) { p = 0; q = 2; largest = Math.Abs(a[0, 2]); }
            if (Math.Abs(a[1, 2]) > largest) { p = 1; q = 2; largest = Math.Abs(a[1, 2]); }
            if (largest < 1e-15) break;
            double angle = .5 * Math.Atan2(2 * a[p, q], a[q, q] - a[p, p]), c = Math.Cos(angle), s = Math.Sin(angle);
            for (int k = 0; k < 3; k++)
            {
                double kp = a[k, p], kq = a[k, q];
                a[k, p] = c * kp - s * kq;
                a[k, q] = s * kp + c * kq;
            }
            for (int k = 0; k < 3; k++)
            {
                double pk = a[p, k], qk = a[q, k];
                a[p, k] = c * pk - s * qk;
                a[q, k] = s * pk + c * qk;
            }
            for (int k = 0; k < 3; k++)
            {
                double kp = v[k, p], kq = v[k, q];
                v[k, p] = c * kp - s * kq;
                v[k, q] = s * kp + c * kq;
            }
        }
        int least = 0;
        if (a[1, 1] < a[least, least]) least = 1;
        if (a[2, 2] < a[least, least]) least = 2;
        return new Vector3((float)v[0, least], (float)v[1, least], (float)v[2, least]).normalized;
    }

    private static Values Settings()
    {
        if (!listening)
        {
            GameTables.Reloaded += () => values = null;
            listening = true;
        }
        if (values.HasValue) return values.Value;
        var read = new Values
        {
            Clearance = .01f, EyeLift = .035f, NoseWidth = .02f, NoseBelow = .06f, BrowWidth = .03f, BrowHeight = .015f,
            Front = .05f, ChestClear = .01f, MostTilt = 15f, Hide = .9f,
        };
        if (GameTables.TryGetRow(TableName, "Default", out var cells))
        {
            read.Clearance = Mathf.Max(0f, cells.Number("clearance", read.Clearance));
            read.EyeLift = cells.Number("eyeLift", read.EyeLift);
            read.NoseWidth = Mathf.Max(.001f, cells.Number("noseWidth", read.NoseWidth));
            read.NoseBelow = Mathf.Max(.006f, cells.Number("noseBelow", read.NoseBelow));
            read.BrowWidth = Mathf.Max(.001f, cells.Number("browWidth", read.BrowWidth));
            read.BrowHeight = Mathf.Max(.001f, cells.Number("browHeight", read.BrowHeight));
            read.Front = Mathf.Max(0f, cells.Number("front", read.Front));
            read.ChestClear = Mathf.Max(0f, cells.Number("chestClear", read.ChestClear));
            read.MostTilt = Mathf.Clamp(cells.Number("mostTilt", read.MostTilt), 0f, 60f);
            read.Hide = Mathf.Clamp01(cells.Number("hide", read.Hide));
        }
        values = read;
        return read;
    }

    private static void Warn(string message)
    {
        if (warned.Add(message)) Debug.LogWarning("[FaceFit] " + message);
    }
}
