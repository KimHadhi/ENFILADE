using System;
using System.Collections.Generic;
using MagicaCloth2;
using UnityEngine;

/// <summary>
/// Skirts, coats and hair that hang free. Cloth.csv names the bones of each skin. The PC that plays a character simulates
/// them with Magica Cloth; every other PC's copy runs no simulation and follows what that PC reports (PlayerNetworkCloth),
/// so everyone sees the same cloth. Then every PC has the arms it shows press the skirts in (SkirtPress). Made again
/// whenever the body is, so a skin swap brings its own. A corpse is this same body, so its cloth goes on hanging as it
/// falls. Numbers: Tables/Cloth, Tables/ClothBody.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.Cloth)]
public sealed class PlayerBodyCloth : MonoBehaviour
{
    public const string TableName = "ClothBody", TableRow = "Default";

    private sealed class Worn
    {
        public ClothPart Part;
        public MagicaCloth Cloth;
        public ClothBones Bones;
        /// <summary>What the arms do to a ring they touch. Null for anything else.</summary>
        public SkirtPress Press;
        /// <summary>How far a point is from what the part rests against. Null where nothing needs to know.</summary>
        public Func<Vector3, float> Room;
    }

    [SerializeField, Min(.01f), Tooltip("Seconds between the cloth poses sent to the other PCs.")]
    private float sendInterval = .066f;
    [SerializeField, Min(1f), Tooltip("How quickly a copy's cloth closes on the last pose it got (per second).")]
    private float followRate = 20f;
    [SerializeField, Range(0f, 1f), Tooltip("How far an arm pressing a skirt sinks into its outer layers: 0 presses the skirt's outside back to the arm, 1 only its bones.")]
    private float handSink = .3f;
    [SerializeField, Min(0f), Tooltip("How far past an arm's girth its press on a skirt reaches, as a share of the distance between the skirt's neighbouring chains of bones.")]
    private float pressSpread = 1f;
    [SerializeField, Range(0f, 1f), Tooltip("How flat an arm can press a skirt against the body: 0 stops it when its inside meets the skin, 1 when its outside does.")]
    private float squash = .8f;
    [SerializeField, Range(0f, .2f), Tooltip("How far an arm may swing out from the shoulder to rest on a skirt it would still sink into once the skirt is pressed as far as it goes, as a share of the arm's length. 0 leaves the arm where the clip has it.")]
    private float armClearance = .04f;

    // How quickly an arm swings out of a skirt and back (per second).
    private const float ArmSwingRate = 15f;

    private readonly Dictionary<string, ClothPart> parts = new Dictionary<string, ClothPart>(StringComparer.OrdinalIgnoreCase);
    private readonly List<Worn> worn = new List<Worn>();
    private PlayerMovement player;
    private PlayerBody body;
    private PlayerBodyHands hands;
    private Animator builtFor;
    private PlayerBodyClothColliders.ArmBone[] arms;
    private SkirtPress.Contact[] contacts;
    // Per arm, left then right: its upper arm, how long the arm is, how far it is swung out now (degrees) and about which
    // axis, kept in the upper arm's parent's space so it turns with the body.
    private readonly Transform[] uppers = new Transform[2];
    private readonly float[] armLengths = new float[2], swings = new float[2];
    private readonly Vector3[] swingAxes = new Vector3[2];

    internal float SendInterval => sendInterval;

    /// <summary>
    /// Raised on the PC that simulates, right after the simulation and before the arms press the skirts: the cloth as it
    /// moved itself, which is what the other PCs are sent, since they press with their own arms.
    /// </summary>
    internal event Action Simulated;

    private void Awake()
    {
        player = GetComponent<PlayerMovement>();
        body = GetComponent<PlayerBody>();
        GameTables.Reloaded += OnTablesReloaded;
        MagicaManager.OnPostSimulation += AfterSimulation;
    }

    private void OnDestroy()
    {
        GameTables.Reloaded -= OnTablesReloaded;
        MagicaManager.OnPostSimulation -= AfterSimulation;
    }

    private void LateUpdate()
    {
        var animator = body != null ? body.BodyAnimator : null;
        if (animator != builtFor) Build(animator);
        foreach (var item in worn)
        {
            if (item.Cloth != null) item.Cloth.SetTimeScale(player.Pace);
            else item.Bones.Follow(followRate, Time.deltaTime);
        }
        // A copy's bones are where they stay this frame now; the simulating PC's only once Magica has run.
        if (player.IsNetworkDriven) Press();
    }

    // Magica has written this frame's bones. Every body hears it, so a copy, which simulates nothing, lets it pass.
    private void AfterSimulation()
    {
        if (player.IsNetworkDriven || worn.Count == 0) return;
        Simulated?.Invoke();
        Press();
    }

    private void Build(Animator animator)
    {
        builtFor = animator;
        worn.Clear();
        arms = null;
        contacts = null;
        if (animator == null || body.Skin == null) return;
        // This skin's parts first, so that measuring the body can leave everything it wears out.
        var cloth = new HashSet<Transform>();
        var hanging = new HashSet<Transform>();
        var springs = new HashSet<Transform>();
        var found = Rows(animator, body.Skin.Id, body.Wears, Part, cloth, hanging, springs);
        var colliders = GetComponent<PlayerBodyClothColliders>();
        bool simulates = !player.IsNetworkDriven;
        SkinSample skin = null;
        BodySurface surface = null;
        DressedBody measured = null;
        HashSet<Transform> armBones = null;
        foreach (var (id, part, roots) in found)
        {
            // Read before the simulation first moves them.
            var item = new Worn { Part = part, Bones = new ClothBones(roots, part.rootTurn) };
            string shapes = Touches(part, roots, animator, ref armBones);
            List<ColliderComponent> touching = null;
            if (simulates)
            {
                if (part.vrc) measured ??= colliders.Measure(animator, null, () => skin ??= SkinSample.Read(animator), hanging, springs);
                touching = Touching(colliders, animator, part, shapes, item.Bones, measured);
                item.Bones.MeasureRoom(point => colliders.Room(point, touching));
                item.Cloth = part.Wear(animator.transform, id, roots, touching, item.Bones);
            }
            if (part.ring && PlayerBodyClothColliders.PressedByArms(part.shapes))
            {
                skin ??= SkinSample.Read(animator);
                surface ??= new BodySurface(animator, skin, cloth);
                item.Press = new SkirtPress(roots, skin);
                if (surface.Measured) item.Room = surface.Distance;
                else
                {
                    // Without the skin, the legs the cloth collides with are the next best thing. A copy makes them only
                    // to know where they are.
                    touching ??= colliders.For(animator, shapes);
                    item.Room = point => colliders.Room(point, touching);
                }
                arms ??= colliders.ArmBones(animator);
            }
            worn.Add(item);
        }
        contacts = arms != null ? new SkirtPress.Contact[arms.Length] : null;
        for (int side = 0; side < 2; side++)
        {
            bool left = side == 0;
            Transform upper = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
            Transform lower = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
            Transform hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            bool whole = upper != null && lower != null && hand != null;
            uppers[side] = whole ? upper : null;
            armLengths[side] = whole ? Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, hand.position) : 0f;
            swings[side] = 0f;
            swingAxes[side] = Vector3.zero;
        }
    }

    /// <summary>
    /// The rows of Cloth.csv a body wears, their roots found on it and tips added, in the table's order: this skin's own
    /// first, then an outfit's (a row names a skin, or an outfit item or model, Outfits.csv: an outfit's skirt or coat
    /// tails swing once it is on), each only while a mesh it moves shows. <paramref name="cloth"/> gets every bone they
    /// move, <paramref name="hanging"/> those that hang free (not a spring's soft lump), for measuring the body without them,
    /// and <paramref name="springs"/> the bones each soft lump hangs from, which the body is measured with as lumps of
    /// their own (DressedBody).
    /// </summary>
    internal static List<(string Id, ClothPart Part, List<Transform> Roots)> Rows(Animator animator, string skin,
        Func<string, bool> wears, Func<string, ClothPart> part, HashSet<Transform> cloth, HashSet<Transform> hanging,
        HashSet<Transform> springs)
    {
        var bones = new Dictionary<string, Transform>();
        foreach (var bone in animator.GetComponentsInChildren<Transform>(true)) bones.TryAdd(bone.name, bone);
        var found = new List<(string Id, ClothPart Part, List<Transform> Roots)>();
        foreach (string id in GameTables.RowIds(ClothPart.TableName))
        {
            var row = part(id);
            if (!string.Equals(row.skin, skin, StringComparison.OrdinalIgnoreCase) && !wears(row.skin)) continue;
            if (!Shows(row.parts, animator)) continue;
            var roots = row.FindRoots(bones, animator.name);
            if (roots.Count == 0) continue;
            row.AddTips(roots, -animator.transform.up);
            found.Add((id, row, roots));
            foreach (var root in roots)
            {
                // An outfit's bones hung inside this row are the outfit's rows' (ClothBones.Collect).
                var under = new List<Transform>();
                ClothBones.Collect(root, under, null);
                cloth.UnionWith(under);
                if (!row.spring) hanging.UnionWith(under);
                else springs.Add(root);
            }
        }
        return found;
    }

    /// <summary>
    /// The shapes a row's cloth rests against. A row written by hand meets the shapes made from the bones it was tuned
    /// with; one BoothImport made meets the body as measured from its meshes (DressedBody), each shape fitted to the
    /// cloth as its maker drew it (PlayerBodyClothColliders.Fit). A chain of those that hangs gets its particles bone by
    /// bone (ClothBones.FitRadii): never thicker than the room its bone has as drawn, and over the chest's soft lumps as
    /// thick as its own cloth reaches toward the body; and, where its maker lets gravity pull it, it is laid down onto the
    /// lumps it hovers over (ClothBones.Settle). Run before the cloth is built: this turns its bones.
    /// </summary>
    internal static List<ColliderComponent> Touching(PlayerBodyClothColliders colliders, Animator animator, ClothPart part,
        string shapes, ClothBones bones, DressedBody measured)
    {
        if (!part.vrc) return colliders.For(animator, shapes);
        var touching = colliders.For(animator, shapes, measured);
        colliders.Fit(touching, bones, part.Thinnest(bones), part.ring);
        if (!part.Hangs || measured == null) return touching;
        bones.FitRadii(part.Thickest(bones), part.ThickestAlong, bone => measured.Reach(bone, out float reach) ? reach : -1f,
            point => colliders.Room(point, touching), point => colliders.LumpRounding(point, touching), colliders.CurveDip);
        if (part.gravity > 0f && colliders.SettleShare > 0f)
            bones.Settle(point => colliders.Room(point, touching), point => colliders.RoomOnLumps(point, touching),
                -animator.transform.up, depth => part.LimitAt(depth) * colliders.SettleShare,
                colliders.SettleReach * Mathf.Abs(animator.transform.lossyScale.y));
        return touching;
    }

    // The body a row's cloth rests against. A row written by hand, a ring and a spring: their shapes, as they say (a
    // spring, a chest's or a bottom's own jiggle, stays inside the body it is part of). A chain BoothImport made from a
    // PhysBone: all of the body, the head and chest and the hips and legs, besides whatever its maker listed. VRChat keeps
    // such a chain off the body with few colliders or none (Mayo's long side locks list only her arms and the floor) and
    // with limit cones turned so that the chain rests on their edge, free to swing out but not in; Magica's cones can't
    // turn, so here the body itself has to stop it. A chain with no colliders met nothing and passed through the body
    // (the user, 2026-11-12: "옷이나 천이 바디와 상호작용을 안 함. 그냥 뚫고 들어감"); one that listed only its head's met
    // nothing below the chest, and Mayo's side locks sank into her ("어쩐지 내려온 옆머리가 가슴 쪽을 뚫더라"). Both, since a
    // chain hung from the chest reaches the hips and long hair the back of the thighs; the shapes are then fitted to it
    // (PlayerBodyClothColliders.Fit), so it keeps the shape its maker drew. A chain hung from an arm keeps only what its
    // maker listed, as the arm would drag it through the body wherever it goes.
    internal static string Touches(ClothPart part, List<Transform> roots, Animator animator, ref HashSet<Transform> armBones)
    {
        string own = part.shapes ?? "";
        if (!part.vrc || part.spring || part.ring || roots.Count == 0) return own;
        armBones ??= ArmTransforms(animator);
        for (var at = roots[0].parent; at != null && at != animator.transform; at = at.parent)
            if (armBones.Contains(at)) return own;
        var all = new List<string>(WholeBody);
        foreach (string shape in own.Split('|'))
            if (shape.Trim().Length > 0 && !all.Exists(name => string.Equals(name, shape.Trim(), StringComparison.OrdinalIgnoreCase)))
                all.Add(shape.Trim());
        return string.Join("|", all);
    }

    // What a chain BoothImport made meets wherever it hangs (Touches).
    private static readonly string[] WholeBody = { "head", "legs" };

    // Every humanoid bone of both arms, upper arms down to the fingers.
    private static HashSet<Transform> ArmTransforms(Animator animator)
    {
        var found = new HashSet<Transform>();
        for (var id = HumanBodyBones.LeftUpperArm; id <= HumanBodyBones.RightHand; id++) Add(id);
        for (var id = HumanBodyBones.LeftThumbProximal; id <= HumanBodyBones.RightLittleDistal; id++) Add(id);
        return found;

        void Add(HumanBodyBones id)
        {
            var bone = animator.GetBoneTransform(id);
            if (bone != null) found.Add(bone);
        }
    }

    // Whether one of the renderers a row moves shows on the body; a row that names none always swings.
    private static bool Shows(string parts, Animator animator)
    {
        if (string.IsNullOrEmpty(parts)) return true;
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in parts.Split('|')) names.Add(name.Trim());
        foreach (var renderer in animator.GetComponentsInChildren<Renderer>(true))
            if (renderer.enabled && renderer.gameObject.activeInHierarchy && names.Contains(renderer.name)) return true;
        return false;
    }

    // One object per row, because a table's binding keeps the object it was first given.
    private ClothPart Part(string id)
    {
        if (parts.TryGetValue(id, out var part)) return part;
        part = new ClothPart();
        GameTables.BindNested(ClothPart.TableName, id, this, part);
        return parts[id] = part;
    }

    // The bones and shapes stay as they were built: those columns take effect on the next body.
    private void OnTablesReloaded()
    {
        foreach (var item in worn)
        {
            if (item.Cloth == null) continue;
            item.Part.Apply(item.Cloth.SerializeData, item.Bones);
            item.Cloth.SetParameterChange();
        }
    }

    // The arms press the skirts, the bones being where the cloth put them for the frame. Where a skirt's mesh couldn't be
    // measured, its outside counts as thick as its table says. What the skirts can't give, the arms make up.
    private void Press()
    {
        if (contacts == null) return;
        Array.Clear(contacts, 0, contacts.Length);
        foreach (var item in worn)
        {
            if (item.Press == null) continue;
            float length = item.Bones.Length;
            float thick = Mathf.Max(item.Part.thickness * length, ClothBones.LeastThickness);
            float limit = item.Part.press >= 0f ? item.Part.press * length : float.PositiveInfinity;
            item.Press.Apply(arms, handSink, squash, thick, pressSpread, limit, item.Room, contacts);
        }
        SwingArms(Time.deltaTime);
    }

    // An arm a skirt would still swallow once pressed as far as it goes swings out from the shoulder by just that much,
    // eased and no further than its clearance, and rests on the skirt; the elbow and wrist keep the clip's bend. A hand that
    // holds something stays where it holds, and a limp body is left to its fall. The clip poses the arm afresh every
    // frame, so the swing is laid on again each time, except while the menu has the body frozen.
    private void SwingArms(float dt)
    {
        if (GamePause.IsPaused) return;
        if (hands == null) hands = GetComponent<PlayerBodyHands>();
        bool alive = !body.IsDead && !body.IsLimp;
        for (int side = 0; side < 2; side++)
        {
            var upper = uppers[side];
            if (upper == null || upper.parent == null) continue;
            if (!alive) { swings[side] = 0f; continue; }
            int deepest = -1;
            for (int i = 0; i < arms.Length; i++)
                if (arms[i].Side == side && contacts[i].Depth > 0f && (deepest < 0 || contacts[i].Depth > contacts[deepest].Depth))
                    deepest = i;
            float wanted = 0f;
            if (deepest >= 0)
            {
                var contact = contacts[deepest];
                Vector3 lever = contact.Point - upper.position;
                // Turning at the shoulder moves the arm only square to it.
                Vector3 across = lever.sqrMagnitude > 1e-6f ? Vector3.ProjectOnPlane(contact.Outward, lever.normalized) : Vector3.zero;
                if (across.sqrMagnitude > .01f)
                {
                    float travel = Mathf.Min(contact.Depth / across.magnitude, armLengths[side] * armClearance);
                    wanted = Mathf.Asin(Mathf.Clamp01(travel / lever.magnitude)) * Mathf.Rad2Deg;
                    if (hands != null) wanted *= 1f - hands.Holding(side == 0);
                    swingAxes[side] = upper.parent.InverseTransformDirection(Vector3.Cross(lever, across).normalized);
                }
            }
            swings[side] = Mathf.Lerp(swings[side], wanted, 1f - Mathf.Exp(-ArmSwingRate * dt));
            if (swings[side] > .01f && swingAxes[side] != Vector3.zero)
                upper.rotation = Quaternion.AngleAxis(swings[side], upper.parent.TransformDirection(swingAxes[side])) * upper.rotation;
        }
    }

    /// <summary>How many bones this body's pose carries: every moving bone of every part it wears.</summary>
    internal int PoseBones
    {
        get
        {
            int bones = 0;
            foreach (var item in worn) bones += item.Bones.Moving;
            return bones;
        }
    }

    /// <summary>
    /// Everything this body's cloth is doing right now, for the other PCs: three numbers per bone, part after part. The
    /// array grows to fit and is kept. Returns how many bones it holds.
    /// </summary>
    internal int Capture(ref sbyte[] pose)
    {
        int bones = PoseBones;
        if (pose == null || pose.Length < bones * 3) pose = new sbyte[bones * 3];
        int at = 0;
        foreach (var item in worn) at = item.Bones.Write(pose, at);
        return at;
    }

    /// <summary>
    /// What the real player's PC reported, as this body's whole pose (PlayerNetworkCloth checks it fits this body's
    /// bones); only its first <paramref name="carried"/> bones are taken.
    /// </summary>
    internal void Show(sbyte[] pose, int carried)
    {
        int at = 0;
        foreach (var item in worn) at = item.Bones.Read(pose, at, carried);
    }
}
