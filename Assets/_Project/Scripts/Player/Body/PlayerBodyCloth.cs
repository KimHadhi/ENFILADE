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
[DisallowMultipleComponent, DefaultExecutionOrder(340)]
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
        var bones = new Dictionary<string, Transform>();
        foreach (var bone in animator.GetComponentsInChildren<Transform>(true)) bones.TryAdd(bone.name, bone);
        // This skin's parts first, so that measuring the body can leave everything it wears out.
        var found = new List<(string Id, ClothPart Part, List<Transform> Roots)>();
        var cloth = new HashSet<Transform>();
        foreach (string id in GameTables.RowIds(ClothPart.TableName))
        {
            var part = Part(id);
            if (!string.Equals(part.skin, body.Skin.Id, StringComparison.OrdinalIgnoreCase)) continue;
            var roots = part.FindRoots(bones, animator.name);
            if (roots.Count == 0) continue;
            part.AddTips(roots, -animator.transform.up);
            found.Add((id, part, roots));
            foreach (var root in roots) cloth.UnionWith(root.GetComponentsInChildren<Transform>(true));
        }
        var colliders = GetComponent<PlayerBodyClothColliders>();
        bool simulates = !player.IsNetworkDriven;
        SkinSample skin = null;
        BodySurface surface = null;
        foreach (var (id, part, roots) in found)
        {
            // Read before the simulation first moves them.
            var item = new Worn { Part = part, Bones = new ClothBones(roots, part.rootTurn) };
            List<ColliderComponent> touching = simulates ? colliders.For(animator, part.shapes) : null;
            if (simulates)
            {
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
                    touching ??= colliders.For(animator, part.shapes);
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

    /// <summary>Everything this body's cloth is doing right now, for the other PCs.</summary>
    internal void Capture(ref ClothPose pose)
    {
        foreach (var item in worn) item.Bones.Write(ref pose);
    }

    /// <summary>What the real player's PC reported. It is ignored if this copy hasn't changed to their skin yet.</summary>
    internal void Show(ref ClothPose pose)
    {
        int count = 0, at = 0;
        foreach (var item in worn) count += item.Bones.Moving;
        if (count != pose.Count) return;
        foreach (var item in worn) at = item.Bones.Read(ref pose, at);
    }
}
