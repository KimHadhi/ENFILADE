using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The bone hitboxes, fitted to each look's own body (the user's playtest of 2026-11-11: "히트 박스 좀 아쉬운 듯"). The
/// pack's fifty zombies range from thin to heavy, and one set of fixed thicknesses missed the heavy ones' bellies and
/// arms and padded the air round the thin ones; the neck was a gap between the head sphere and the chest box, and hands
/// and feet had nothing. Now, the first time a look is dressed, its skin is posed as it stands (BakeMesh) and every
/// vertex goes to the human bone that moves it most (a twist, finger or toe bone counts for the human bone above it):
/// - the head: a sphere round most of the head's own vertices (a hat brim or a ponytail can't blow it up), and down the
///   neck a capsule that counts as the head too, as generous as the gun rules want (gun-damage-rules);
/// - the torso: a box per spine bone, as wide and deep as most of that bone's vertices reach;
/// - arms and legs: a capsule per bone, as thick as most of its vertices stand off the bone;
/// - hands and feet: a box round their vertices in the bone's own frame.
/// The fit is kept per look (its mesh) in each bone's own space, in metres at the pack's size, so later zombies of that
/// look only lay the shapes out, a brute's grown with it. A mesh that can't be read keeps the fixed sizes (BuildHitboxes).
/// The console's /hitbox draws them all (head red, body yellow) to judge the fit.
/// </summary>
public sealed partial class ZombieBody
{
    // Share of a part's vertices its shape takes in; the rest are its farthest strays (a strap, a hem, a cuff).
    private const float FitShare = .9f, LimbShare = .85f;
    // Bounds on the fitted sizes (m at the pack's size), so an odd mesh can't make a part vanish or swallow a neighbour.
    private const float LeastLimb = .035f, MostLimb = .2f, LeastHead = .09f, MostHead = .2f, LeastNeck = .04f, MostNeck = .11f;
    private const float LeastTorso = .14f, MostTorso = .62f, LeastEnd = .04f, MostEnd = .32f;

    private enum FitShape : byte { Sphere, Capsule, Box }

    private struct FitPart
    {
        public HumanBodyBones Bone;
        public FitShape Shape;
        public WeaponHitRegion.Region Region;
        // In the bone's own space, which a uniformly scaled model leaves alone, so a brute of the same look fits too.
        public Vector3 Center;
        public Quaternion Turn;
        // Metres at the pack's size: a sphere's radius in x; a capsule's radius in x and length (along its z) in y; a box's size.
        public Vector3 Size;
    }

    private static readonly Dictionary<Mesh, FitPart[]> fitted = new Dictionary<Mesh, FitPart[]>();
    private static bool showingHitboxes;
    private static Material headView, bodyView;
    private readonly List<GameObject> hitboxViews = new List<GameObject>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ClearFits()
    {
        fitted.Clear();
        showingHitboxes = false;
    }

    /// <summary>Console /hitbox: shows or hides every zombie's hitboxes. Returns what it says.</summary>
    public static string ToggleHitboxes()
    {
        showingHitboxes = !showingHitboxes;
        // Found when asked rather than kept in a list, which would hold every zombie ever dressed (the 2026-11-11 review).
        foreach (var body in FindObjectsByType<ZombieBody>(FindObjectsSortMode.None)) body.ViewHitboxes(showingHitboxes);
        return showingHitboxes ? "좀비 히트박스 보이기 (머리 빨강 · 몸 노랑). 다시 입력하면 숨긴다" : "좀비 히트박스 숨김";
    }

    /// <summary>Lays the hitboxes out on the dressed body. Returns the head's top in the enemy's own y, for the capsule.</summary>
    private float FitHitboxes(float feet)
    {
        var parts = FitFor();
        float top;
        if (parts == null) top = BuildHitboxes(feet);
        else
        {
            top = feet + 1.75f * grown;
            foreach (var part in parts)
            {
                var bone = Bone(part.Bone);
                if (bone == null) continue;
                Vector3 center = bone.TransformPoint(part.Center);
                var made = Hitbox(bone, center, bone.rotation * part.Turn);
                Vector3 size = part.Size * grown;
                switch (part.Shape)
                {
                    case FitShape.Sphere:
                        var sphere = made.AddComponent<SphereCollider>();
                        sphere.radius = size.x;
                        Own(sphere, part.Region);
                        if (part.Region == WeaponHitRegion.Region.Head)
                            top = transform.InverseTransformPoint(center + Model.transform.up * size.x).y;
                        break;
                    case FitShape.Capsule:
                        var capsule = made.AddComponent<CapsuleCollider>();
                        capsule.direction = 2;
                        capsule.radius = size.x;
                        capsule.height = Mathf.Max(size.y, size.x * 2f);
                        Own(capsule, part.Region);
                        break;
                    default:
                        var box = made.AddComponent<BoxCollider>();
                        box.size = size;
                        Own(box, part.Region);
                        break;
                }
            }
        }
        if (showingHitboxes) ViewHitboxes(true);
        return top;
    }

    // This look's fit, measured the first time it is dressed; null (the fixed sizes) when its skin can't be read.
    private FitPart[] FitFor()
    {
        var skins = Model.GetComponentsInChildren<SkinnedMeshRenderer>();
        if (skins.Length == 0 || skins[0].sharedMesh == null) return null;
        var key = skins[0].sharedMesh;
        if (fitted.TryGetValue(key, out var known)) return known;
        FitPart[] made;
        try { made = MeasureFit(skins); }
        catch (System.Exception problem)
        {
            Debug.LogWarning($"[ZombieBody] Couldn't fit {Model.name}'s hitboxes to its body ({problem.Message}); it keeps the fixed sizes.", this);
            made = null;
        }
        fitted[key] = made;
        return made;
    }

    private FitPart[] MeasureFit(SkinnedMeshRenderer[] skins)
    {
        // Which human bone each bone counts for: itself, or the nearest human bone above it. The jaw and eyes count for
        // the head, fingers for the hand, toes for the foot, the shoulders for the top of the chest.
        var owner = new Dictionary<Transform, int>();
        for (int b = 0; b < human.Length; b++) if (human[b] != null) owner[human[b]] = Fold((HumanBodyBones)b);
        var points = new Dictionary<int, List<Vector3>>();
        var baked = new Mesh();
        var weights = new List<BoneWeight>();
        var vertices = new List<Vector3>();
        try
        {
            foreach (var skin in skins)
            {
                var mesh = skin.sharedMesh;
                if (mesh == null || !skin.enabled || !skin.gameObject.activeInHierarchy) continue;
                if (!mesh.isReadable)
                {
                    Debug.LogWarning($"[ZombieBody] {mesh.name} can't be read (Read/Write off), so {Model.name} keeps the fixed hitbox sizes.", this);
                    return null;
                }
                var bones = skin.bones;
                skin.BakeMesh(baked, true);
                baked.GetVertices(vertices);
                mesh.GetBoneWeights(weights);
                if (weights.Count != vertices.Count) continue;
                // Which part each of this skin's bones counts for, worked out once per bone rather than per vertex.
                var partOf = new int[bones.Length];
                for (int b = 0; b < bones.Length; b++) partOf[b] = bones[b] != null ? OwnerOf(bones[b], owner) : -1;
                // BakeMesh(…, true) gives the mesh in the renderer's own unscaled space, so the renderer's whole matrix takes
                // it to the world. With its scale left out, a brute (grown) dressed first fitted its look too small for
                // every later zombie of that look (the 2026-11-11 review).
                var toWorld = skin.transform.localToWorldMatrix;
                for (int v = 0; v < vertices.Count; v++)
                {
                    int index = MeshTools.Strongest(weights[v]);
                    if (index < 0 || index >= bones.Length) continue;
                    int part = partOf[index];
                    if (part < 0) continue;
                    if (!points.TryGetValue(part, out var list)) points[part] = list = new List<Vector3>();
                    list.Add(toWorld.MultiplyPoint3x4(vertices[v]));
                }
            }
        }
        finally { Destroy(baked); }

        var parts = new List<FitPart>();
        Vector3 up = Model.transform.up, forward = Model.transform.forward;
        FitHead(points, parts, up, forward);
        FitTorso(points, parts, up, forward);
        FitLimb(points, parts, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm);
        FitLimb(points, parts, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
        FitLimb(points, parts, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm);
        FitLimb(points, parts, HumanBodyBones.RightLowerArm, HumanBodyBones.RightHand);
        FitLimb(points, parts, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg);
        FitLimb(points, parts, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot);
        FitLimb(points, parts, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg);
        FitLimb(points, parts, HumanBodyBones.RightLowerLeg, HumanBodyBones.RightFoot);
        FitEnd(points, parts, HumanBodyBones.LeftHand);
        FitEnd(points, parts, HumanBodyBones.RightHand);
        FitEnd(points, parts, HumanBodyBones.LeftFoot);
        FitEnd(points, parts, HumanBodyBones.RightFoot);
        // Without a head or a torso the fit isn't to be trusted: the fixed sizes then.
        bool head = false, torso = false;
        foreach (var part in parts)
        {
            if (part.Region == WeaponHitRegion.Region.Head) head = true;
            else if (part.Bone == HumanBodyBones.Hips || part.Bone == HumanBodyBones.Spine || part.Bone == HumanBodyBones.Chest
                     || part.Bone == HumanBodyBones.UpperChest) torso = true;
        }
        return head && torso ? parts.ToArray() : null;
    }

    // The human bone a vertex on this bone counts for.
    private static int Fold(HumanBodyBones bone)
    {
        switch (bone)
        {
            case HumanBodyBones.Jaw: case HumanBodyBones.LeftEye: case HumanBodyBones.RightEye: return (int)HumanBodyBones.Head;
            case HumanBodyBones.LeftShoulder: case HumanBodyBones.RightShoulder: return (int)HumanBodyBones.UpperChest;
            case HumanBodyBones.LeftToes: return (int)HumanBodyBones.LeftFoot;
            case HumanBodyBones.RightToes: return (int)HumanBodyBones.RightFoot;
        }
        if (bone >= HumanBodyBones.LeftThumbProximal && bone <= HumanBodyBones.LeftLittleDistal) return (int)HumanBodyBones.LeftHand;
        if (bone >= HumanBodyBones.RightThumbProximal && bone <= HumanBodyBones.RightLittleDistal) return (int)HumanBodyBones.RightHand;
        return (int)bone;
    }

    private int OwnerOf(Transform bone, Dictionary<Transform, int> owner)
    {
        for (var at = bone; at != null && at != Model.transform.parent; at = at.parent)
            if (owner.TryGetValue(at, out int part))
            {
                // A rig without an upper chest: its shoulders count for the chest.
                if (part == (int)HumanBodyBones.UpperChest && Bone(HumanBodyBones.UpperChest) == null) return (int)HumanBodyBones.Chest;
                return part;
            }
        return -1;
    }

    // The head: a sphere round the middle of the head's vertices, taking in FitShare of them; then down the neck a capsule
    // to the bottom of that sphere, which counts as the head as well.
    private void FitHead(Dictionary<int, List<Vector3>> points, List<FitPart> parts, Vector3 up, Vector3 forward)
    {
        var head = Bone(HumanBodyBones.Head);
        if (head == null || !points.TryGetValue((int)HumanBodyBones.Head, out var list) || list.Count < 8) return;
        Vector3 center = SpreadCenter(list, Quaternion.LookRotation(forward, up));
        float radius = Mathf.Clamp(Share(Distances(list, center), FitShare), LeastHead * grown, MostHead * grown);
        parts.Add(Part(HumanBodyBones.Head, FitShape.Sphere, WeaponHitRegion.Region.Head, head, center,
            Quaternion.LookRotation(forward, up), new Vector3(radius, 0f, 0f)));

        var neck = Bone(HumanBodyBones.Neck);
        if (neck == null) return;
        Vector3 from = neck.position, to = center - up * (radius * .6f);
        Vector3 axis = to - from;
        float length = axis.magnitude;
        if (length < .02f) return;
        float thick = points.TryGetValue((int)HumanBodyBones.Neck, out var necks) && necks.Count >= 4
            ? Share(Offsets(necks, from, to), LimbShare) : .06f * grown;
        thick = Mathf.Clamp(thick, LeastNeck * grown, MostNeck * grown);
        // Hung on the head bone, not the neck's: gore reads the part a shot met from the bone its hitbox hangs on
        // (ZombieGore.PartOf), so a killing shot through the neck bursts the head, and a burst head takes this with it.
        parts.Add(Part(HumanBodyBones.Head, FitShape.Capsule, WeaponHitRegion.Region.Head, head, (from + to) * .5f,
            Quaternion.LookRotation(axis, forward), new Vector3(thick, length + thick, 0f)));
    }

    // A box per spine bone up to the neck, along the bone to the next one, as wide (side to side) and deep (front to back)
    // as most of its vertices; the hips' box also reaches down over the seat.
    private void FitTorso(Dictionary<int, List<Vector3>> points, List<FitPart> parts, Vector3 up, Vector3 forward)
    {
        var chain = new List<HumanBodyBones>();
        foreach (var bone in new[] { HumanBodyBones.Hips, HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest, HumanBodyBones.Neck })
            if (Bone(bone) != null && !chain.Exists(found => Bone(found) == Bone(bone))) chain.Add(bone);
        for (int i = 0; i + 1 < chain.Count; i++)
        {
            var bone = Bone(chain[i]);
            Vector3 from = bone.position, to = Bone(chain[i + 1]).position;
            Vector3 axis = to - from;
            float length = axis.magnitude;
            if (length < .02f) continue;
            Vector3 along = axis / length;
            Vector3 facing = Vector3.ProjectOnPlane(forward, along);
            if (facing.sqrMagnitude < 1e-4f) facing = Vector3.ProjectOnPlane(Model.transform.right, along);
            var turn = Quaternion.LookRotation(facing.normalized, along);
            Vector3 side = turn * Vector3.right, front = turn * Vector3.forward;
            float low = 0f, high = length, left = -.18f * grown, right = .18f * grown, back = -.12f * grown, ahead = .12f * grown;
            if (points.TryGetValue((int)chain[i], out var list) && list.Count >= 8)
            {
                Range(list, from, side, out left, out right);
                Range(list, from, front, out back, out ahead);
                // Along the bone it covers the stretch to the next one, and on the hips whatever hangs below them.
                Range(list, from, along, out float under, out _);
                if (i == 0) low = Mathf.Min(0f, under);
            }
            float width = Mathf.Clamp(right - left, LeastTorso * grown, MostTorso * grown);
            float depth = Mathf.Clamp(ahead - back, LeastTorso * grown, MostTorso * grown);
            float tall = high - low + .04f * grown;
            Vector3 center = from + side * ((left + right) * .5f) + front * ((back + ahead) * .5f) + along * ((low + high) * .5f);
            parts.Add(Part(chain[i], FitShape.Box, WeaponHitRegion.Region.Body, bone, center, turn, new Vector3(width, tall, depth)));
        }
    }

    // An arm or leg bone: a capsule from it to the next, as thick as most of its vertices stand off that line.
    private void FitLimb(Dictionary<int, List<Vector3>> points, List<FitPart> parts, HumanBodyBones start, HumanBodyBones end)
    {
        var from = Bone(start);
        var to = Bone(end);
        if (from == null || to == null) return;
        Vector3 axis = to.position - from.position;
        float length = axis.magnitude;
        if (length < .02f) return;
        float radius = points.TryGetValue((int)start, out var list) && list.Count >= 6
            ? Share(Offsets(list, from.position, to.position), LimbShare) : .07f * grown;
        radius = Mathf.Clamp(radius, LeastLimb * grown, MostLimb * grown);
        parts.Add(Part(start, FitShape.Capsule, WeaponHitRegion.Region.Body, from, (from.position + to.position) * .5f,
            Quaternion.LookRotation(axis), new Vector3(radius, length + radius, 0f)));
    }

    // A hand or a foot: a box round most of its vertices, squared to the bone.
    private void FitEnd(Dictionary<int, List<Vector3>> points, List<FitPart> parts, HumanBodyBones end)
    {
        var bone = Bone(end);
        if (bone == null || !points.TryGetValue((int)end, out var list) || list.Count < 6) return;
        var turn = bone.rotation;
        Vector3 x = turn * Vector3.right, y = turn * Vector3.up, z = turn * Vector3.forward, from = bone.position;
        Range(list, from, x, out float x0, out float x1);
        Range(list, from, y, out float y0, out float y1);
        Range(list, from, z, out float z0, out float z1);
        var size = new Vector3(Mathf.Clamp(x1 - x0, LeastEnd * grown, MostEnd * grown),
            Mathf.Clamp(y1 - y0, LeastEnd * grown, MostEnd * grown), Mathf.Clamp(z1 - z0, LeastEnd * grown, MostEnd * grown));
        Vector3 center = from + x * ((x0 + x1) * .5f) + y * ((y0 + y1) * .5f) + z * ((z0 + z1) * .5f);
        parts.Add(Part(end, FitShape.Box, WeaponHitRegion.Region.Body, bone, center, turn, size));
    }

    // A part in its bone's own space, its size in metres at the pack's size.
    private FitPart Part(HumanBodyBones bone, FitShape shape, WeaponHitRegion.Region region, Transform on, Vector3 center,
        Quaternion turn, Vector3 size) => new FitPart
    {
        Bone = bone, Shape = shape, Region = region, Center = on.InverseTransformPoint(center),
        Turn = Quaternion.Inverse(on.rotation) * turn, Size = size / Mathf.Max(.01f, grown)
    };

    // How far along one way from a point most of the vertices reach, both ends, leaving the strays out.
    private static void Range(List<Vector3> list, Vector3 from, Vector3 way, out float low, out float high)
    {
        var along = new List<float>(list.Count);
        foreach (var point in list) along.Add(Vector3.Dot(point - from, way));
        along.Sort();
        float leave = (1f - FitShare) * .5f;
        low = along[Mathf.Clamp(Mathf.FloorToInt(leave * (along.Count - 1)), 0, along.Count - 1)];
        high = along[Mathf.Clamp(Mathf.CeilToInt((1f - leave) * (along.Count - 1)), 0, along.Count - 1)];
    }

    // The middle of the vertices' spread in a frame: the middle of where most of them reach on each of its axes.
    private static Vector3 SpreadCenter(List<Vector3> list, Quaternion frame)
    {
        Vector3 origin = list[0], center = origin;
        for (int axis = 0; axis < 3; axis++)
        {
            Vector3 way = frame * (axis == 0 ? Vector3.right : axis == 1 ? Vector3.up : Vector3.forward);
            Range(list, origin, way, out float low, out float high);
            center += way * ((low + high) * .5f);
        }
        return center;
    }

    private static List<float> Distances(List<Vector3> list, Vector3 center)
    {
        var distances = new List<float>(list.Count);
        foreach (var point in list) distances.Add(Vector3.Distance(point, center));
        return distances;
    }

    // Each vertex's distance from the segment a-b.
    private static List<float> Offsets(List<Vector3> list, Vector3 a, Vector3 b)
    {
        var offsets = new List<float>(list.Count);
        Vector3 ab = b - a;
        float lengthSquared = Mathf.Max(1e-6f, ab.sqrMagnitude);
        foreach (var point in list)
        {
            float t = Mathf.Clamp01(Vector3.Dot(point - a, ab) / lengthSquared);
            offsets.Add(Vector3.Distance(point, a + ab * t));
        }
        return offsets;
    }

    // The value below which this share of the list lies.
    private static float Share(List<float> values, float share)
    {
        values.Sort();
        return values[Mathf.Clamp(Mathf.RoundToInt(share * (values.Count - 1)), 0, values.Count - 1)];
    }

    // ---- /hitbox -------------------------------------------------------------------------------------------------

    // A see-through shape on each hitbox, drawn with the shot effects' unlit material: the head red, the body yellow.
    private void ViewHitboxes(bool on)
    {
        foreach (var view in hitboxViews) if (view != null) Destroy(view);
        hitboxViews.Clear();
        if (!on) return;
        if (headView == null)
        {
            // Over the body (GuideBeacon is the shot effects' material drawn whatever stands in front): the shapes sit mostly
            // inside the skin, and with the depth test only their corners poked out (the 2026-11-11 review).
            var through = Resources.Load<Shader>("Combat/Shaders/GuideBeacon");
            headView = through != null ? new Material(through) : new Material(FeelAssets.Particle(Texture2D.whiteTexture, true));
            headView.hideFlags = HideFlags.HideAndDontSave;
            if (through != null)
            {
                headView.SetTexture("_MainTex", Texture2D.whiteTexture);
                headView.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                headView.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
            }
            headView.SetColor("_Color", new Color(1f, .15f, .15f, .45f));
            bodyView = new Material(headView) { hideFlags = HideFlags.HideAndDontSave };
            bodyView.SetColor("_Color", new Color(1f, .85f, .1f, .3f));
        }
        foreach (var collider in owned)
        {
            if (collider == null) continue;
            bool head = collider.TryGetComponent(out WeaponHitRegion region) && region.Kind == WeaponHitRegion.Region.Head;
            GameObject view;
            switch (collider)
            {
                case SphereCollider sphere:
                    view = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                    view.transform.SetParent(sphere.transform, false);
                    view.transform.localPosition = sphere.center;
                    view.transform.localScale = Vector3.one * sphere.radius * 2f;
                    break;
                case CapsuleCollider capsule:
                    view = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    view.transform.SetParent(capsule.transform, false);
                    view.transform.localPosition = capsule.center;
                    // The primitive stands along y; these lie along z.
                    view.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                    view.transform.localScale = new Vector3(capsule.radius * 2f, capsule.height * .5f, capsule.radius * 2f);
                    break;
                case BoxCollider box:
                    view = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    view.transform.SetParent(box.transform, false);
                    view.transform.localPosition = box.center;
                    view.transform.localScale = box.size;
                    break;
                default: continue;
            }
            view.name = "Hitbox View";
            // The shape only: its own collider would take the bullets.
            DestroyImmediate(view.GetComponent<Collider>());
            var renderer = view.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = head ? headView : bodyView;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            hitboxViews.Add(view);
        }
    }
}
