using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// The guns a player carries but doesn't hold, worn on the body where everyone can see what they have (user, 2026-11-11:
/// "등에 현재 들고 있는 무기 수납할 수 있게 할 것. 현재 무기 뭐 들고 있는지 보이게끔"): long guns slung across the back,
/// short ones (a pistol, a revolver) on the thighs. Each is a copy of that gun as the body last held it (FPSBody's own
/// copy: its sight, muzzle and underbarrel parts, its skin, a magazine left out by a cut-short reload), transforms and
/// renderers only, no scripts, set on the body every frame after it is posed, in axes taken from its bones (up the spine
/// and across the shoulders, down each thigh), so it follows a lean, a crouch, a vault and a ragdoll on any body.
/// Friends, the F5 and third-person views and spectators see them; this PC's own player, with the camera at the eyes,
/// sees only their shadows. A friend's copy knows their slots from their PC (PlayerNetworkCombat). Where they sit:
/// Tables/Holsters.csv, row Holster.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.Cloth + 5)]
public sealed class HolsteredGuns : MonoBehaviour, ITableBound
{
    public const string TableName = "Holsters", TableRow = "Holster";

    [SerializeField, Min(0f), Tooltip("Guns longer than this along the barrel (m) are slung on the back; shorter ones go on the thighs.")]
    private float longGun = .45f;
    [SerializeField, Tooltip("The first long gun: right, share of the way from the hips up to the neck, forward (m; the back's axes).")]
    private Vector3 backAt = new Vector3(0f, .55f, -.17f);
    [SerializeField, Tooltip("Its barrel's way in the back's axes (right, up, forward).")]
    private Vector3 backBarrel = new Vector3(-.45f, -.89f, 0f);
    [SerializeField, Tooltip("The way its sight faces in the back's axes.")]
    private Vector3 backSight = new Vector3(0f, 0f, -1f);
    [SerializeField, Tooltip("The second long gun, crossed over the first.")]
    private Vector3 back2At = new Vector3(0f, .5f, -.24f), back2Barrel = new Vector3(.45f, -.89f, 0f), back2Sight = new Vector3(0f, 0f, -1f);
    [SerializeField, Tooltip("The first short gun, on the right thigh: outward, share of the way from the hip down to the knee, forward (m; the thigh's axes, up the thigh).")]
    private Vector3 thighAt = new Vector3(.1f, .3f, 0f);
    [SerializeField, Tooltip("Its barrel's way and its sight's in the thigh's axes (right, up the thigh, forward).")]
    private Vector3 thighBarrel = new Vector3(0f, -1f, .15f), thighSight = new Vector3(0f, 0f, 1f);
    [SerializeField, Tooltip("The second short gun, on the left thigh (its x counts to the body's right, so outward is negative).")]
    private Vector3 thigh2At = new Vector3(-.1f, .3f, 0f), thigh2Barrel = new Vector3(0f, -1f, .15f), thigh2Sight = new Vector3(0f, 0f, 1f);
    [SerializeField, Min(0f), Tooltip("This PC's own player: the camera within this of the eyes (m) is first person, where the worn guns cast only their shadows.")]
    private float firstPersonReach = .35f;

    // Parts further than this off the sight line (m) wait parked away from the gun (the R08's speedloader): never worn.
    private const float ParkedFrom = .4f;

    private enum Place { Back, Back2, Thigh, Thigh2 }

    private sealed class Worn
    {
        public WeaponData Data;
        public FPSGun Source;
        public GameObject Copy;
        public Renderer[] Renderers;
        // In the copy's own space: the middle of the gun, the way its barrel points and its sight faces.
        public Vector3 Center, Barrel, Up;
        public float Length;
        public bool Shown;
    }

    private FPSBody fpsBody;
    private PlayerWeaponSlots slots;
    private PlayerMovement movement;
    private PlayerBody body;
    private Transform holder, eyes;
    private readonly Dictionary<WeaponData, Worn> worn = new();
    private readonly List<Worn> carried = new();
    private int heldSlot = int.MinValue;

    public void OnTableApplied() { }

    private void Awake()
    {
        GameTables.Bind(TableName, TableRow, this);
        fpsBody = GetComponent<FPSBody>();
        slots = GetComponent<PlayerWeaponSlots>();
        movement = GetComponentInParent<PlayerMovement>();
        body = movement != null ? movement.GetComponent<PlayerBody>() : GetComponentInParent<PlayerBody>();
    }

    private void OnDestroy()
    {
        foreach (var gun in worn.Values) if (gun.Copy != null) Destroy(gun.Copy);
        if (holder != null) Destroy(holder.gameObject);
    }

    private void OnDisable()
    {
        foreach (var gun in worn.Values) Show(gun, false);
    }

    private void LateUpdate()
    {
        var animator = body != null ? body.BodyAnimator : null;
        if (fpsBody == null || slots == null || animator == null || !animator.isHuman || !slots.isActiveAndEnabled)
        {
            foreach (var gun in worn.Values) Show(gun, false);
            return;
        }
        // The gun just put away is copied as it was last held; the copies are made only when the hands change.
        if (slots.SelectedSlot != heldSlot) { heldSlot = slots.SelectedSlot; Refresh(); }
        Gather();
        bool firstPerson = FirstPerson();
        int longs = 0, shorts = 0;
        foreach (var gun in carried)
        {
            bool isLong = gun.Length * ScaleOf(gun) > longGun;
            if (isLong ? longs >= 2 : shorts >= 2) { Show(gun, false); continue; }
            var place = isLong ? (longs++ == 0 ? Place.Back : Place.Back2) : (shorts++ == 0 ? Place.Thigh : Place.Thigh2);
            if (!Wear(gun, place, animator)) { Show(gun, false); continue; }
            Show(gun, true);
            var mode = firstPerson ? ShadowCastingMode.ShadowsOnly : ShadowCastingMode.On;
            foreach (var renderer in gun.Renderers)
                if (renderer != null && renderer.shadowCastingMode != mode) renderer.shadowCastingMode = mode;
        }
        foreach (var gun in worn.Values)
            if (!carried.Contains(gun)) Show(gun, false);
    }

    // The ranged guns in the slots not in hand (the first two, and the special slot when it holds one: a Schießhund's
    // Desperado), each with its worn copy.
    private void Gather()
    {
        carried.Clear();
        for (int slot = 0; slot < PlayerWeaponSlots.SlotCount; slot++)
        {
            if (slot == slots.SelectedSlot) continue;
            var data = slots.GetWeapon(slot);
            if (data == null || data.Category != WeaponCategory.Ranged) continue;
            var gun = CopyOf(data);
            if (gun != null && !carried.Contains(gun)) carried.Add(gun);
        }
    }

    // Copies again every gun not in hand, so each wears what it was last given.
    private void Refresh()
    {
        foreach (var gun in worn.Values)
        {
            if (gun.Copy != null) Destroy(gun.Copy);
            gun.Copy = null;
        }
    }

    private Worn CopyOf(WeaponData data)
    {
        var source = fpsBody.GunFor(data);
        if (source == null) return null;
        if (!worn.TryGetValue(data, out var gun)) worn[data] = gun = new Worn { Data = data };
        if (gun.Copy != null && gun.Source == source) return gun;
        if (gun.Copy != null) Destroy(gun.Copy);
        gun.Source = source;
        Build(gun);
        return gun.Copy != null ? gun : null;
    }

    // ---- The copy: the gun's transforms and renderers, nothing else ----

    private void Build(Worn gun)
    {
        if (holder == null)
        {
            holder = new GameObject("Worn Guns").transform;
            holder.SetParent(movement != null ? movement.transform : transform, false);
        }
        var root = CopyLook(gun.Source.transform, out var renderers, out var map);
        gun.Copy = root;
        gun.Renderers = renderers;
        int layer = BodyLayer();
        foreach (var part in root.GetComponentsInChildren<Transform>(true)) part.gameObject.layer = layer;
        Measure(gun, map);
        root.transform.SetParent(holder, false);
        gun.Shown = true;
        Show(gun, false);
    }

    /// <summary>
    /// A copy of a gun's look: its transforms (each with its own pose, size and on/off) and its mesh and skinned
    /// renderers, with no scripts, animators, lights, colliders or cameras. Made at the scene's root, switched on; the
    /// title screen's 무기 page shows one too (MainMenuWeapons).
    /// </summary>
    internal static GameObject CopyLook(Transform source, out Renderer[] renderers, out Dictionary<Transform, Transform> map)
    {
        map = new Dictionary<Transform, Transform>();
        var root = Mirror(source, null, map);
        root.SetActive(true);
        var made = new List<Renderer>();
        foreach (var filter in source.GetComponentsInChildren<MeshFilter>(true))
        {
            var mesh = filter.GetComponent<MeshRenderer>();
            if (mesh == null || filter.sharedMesh == null || filter.GetComponent<TMPro.TMP_Text>() != null || !map.TryGetValue(filter.transform, out var at)) continue;
            at.gameObject.AddComponent<MeshFilter>().sharedMesh = filter.sharedMesh;
            var copy = at.gameObject.AddComponent<MeshRenderer>();
            copy.sharedMaterials = mesh.sharedMaterials;
            copy.enabled = mesh.enabled;
            made.Add(copy);
        }
        foreach (var skin in source.GetComponentsInChildren<SkinnedMeshRenderer>(true))
        {
            if (skin.sharedMesh == null || !map.TryGetValue(skin.transform, out var at)) continue;
            var copy = at.gameObject.AddComponent<SkinnedMeshRenderer>();
            copy.sharedMesh = skin.sharedMesh;
            copy.sharedMaterials = skin.sharedMaterials;
            var bones = skin.bones;
            var mapped = new Transform[bones.Length];
            for (int i = 0; i < bones.Length; i++) mapped[i] = bones[i] != null && map.TryGetValue(bones[i], out var bone) ? bone : null;
            copy.bones = mapped;
            copy.rootBone = skin.rootBone != null && map.TryGetValue(skin.rootBone, out var top) ? top : null;
            copy.localBounds = skin.localBounds;
            copy.updateWhenOffscreen = true;
            copy.enabled = skin.enabled;
            for (int i = 0; i < skin.sharedMesh.blendShapeCount; i++) copy.SetBlendShapeWeight(i, skin.GetBlendShapeWeight(i));
            made.Add(copy);
        }
        renderers = made.ToArray();
        return root;
    }

    private static GameObject Mirror(Transform source, Transform parent, Dictionary<Transform, Transform> map)
    {
        var made = new GameObject(source.name);
        var t = made.transform;
        if (parent != null) t.SetParent(parent, false);
        t.SetLocalPositionAndRotation(source.localPosition, source.localRotation);
        t.localScale = source.localScale;
        map[source] = t;
        foreach (Transform child in source) Mirror(child, t, map);
        if (parent != null && !source.gameObject.activeSelf) made.SetActive(false);
        return made;
    }

    // With the copy at rest at the world's origin: its sight line (the shown sight's aim point), the parts parked away from
    // it shrunk out, and its middle and length along the barrel.
    private void Measure(Worn gun, Dictionary<Transform, Transform> map)
    {
        var root = gun.Copy.transform;
        root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        root.localScale = Vector3.one;
        Transform aim = gun.Source.AimPoint != null && map.TryGetValue(gun.Source.AimPoint, out var mapped) ? mapped : root;
        Vector3 along = aim.forward, line = aim.position;
        // As FPSGun hides them in the hands: plain meshes and skin bones parked off the sight line.
        var parts = new List<Transform>();
        foreach (var renderer in gun.Renderers)
        {
            if (renderer is SkinnedMeshRenderer skin) { foreach (var bone in skin.bones) if (bone != null) parts.Add(bone); }
            else if (renderer != null) parts.Add(renderer.transform);
        }
        foreach (var part in parts)
        {
            if (part == root) continue;
            Vector3 offset = part.position - line;
            if ((offset - along * Vector3.Dot(offset, along)).sqrMagnitude > ParkedFrom * ParkedFrom) part.localScale = Vector3.zero;
        }
        gun.Barrel = along;
        gun.Up = aim.up;
        bool any = false;
        var bounds = new Bounds();
        foreach (var renderer in gun.Renderers)
        {
            if (renderer == null || !renderer.enabled || !renderer.gameObject.activeInHierarchy) continue;
            var b = renderer.bounds;
            if (b.size.sqrMagnitude <= 0f || b.size.sqrMagnitude > ParkedFrom * ParkedFrom * 16f) continue;
            if (!any) { bounds = b; any = true; }
            else bounds.Encapsulate(b);
        }
        gun.Center = any ? bounds.center : line;
        Vector3 e = bounds.extents;
        gun.Length = any ? 2f * (Mathf.Abs(along.x) * e.x + Mathf.Abs(along.y) * e.y + Mathf.Abs(along.z) * e.z) : 0f;
    }

    // The layer the body's own renderers are on, so the worn guns are seen and hidden as the body is.
    private int BodyLayer()
    {
        var animator = body != null ? body.BodyAnimator : null;
        var skin = animator != null ? animator.GetComponentInChildren<SkinnedMeshRenderer>() : null;
        return skin != null ? skin.gameObject.layer : gameObject.layer;
    }

    private static void Show(Worn gun, bool shown)
    {
        if (gun.Copy == null || gun.Shown == shown) return;
        gun.Shown = shown;
        gun.Copy.SetActive(shown);
    }

    // ---- Where it sits on the body ----

    private bool Wear(Worn gun, Place place, Animator animator)
    {
        if (gun.Copy == null) return false;
        Vector3 at, barrel, sight;
        Vector3 right, up, forward, origin;
        switch (place)
        {
            case Place.Back: case Place.Back2:
                if (!BackAxes(animator, out right, out up, out forward, out var hips, out var neck)) return false;
                at = place == Place.Back ? backAt : back2At;
                origin = Vector3.Lerp(hips, neck, at.y);
                barrel = place == Place.Back ? backBarrel : back2Barrel;
                sight = place == Place.Back ? backSight : back2Sight;
                break;
            default:
                bool left = place == Place.Thigh2;
                if (!ThighAxes(animator, left, out right, out up, out forward, out var hip, out var knee)) return false;
                at = left ? thigh2At : thighAt;
                origin = Vector3.Lerp(hip, knee, at.y);
                barrel = left ? thigh2Barrel : thighBarrel;
                sight = left ? thigh2Sight : thighSight;
                break;
        }
        Vector3 spot = origin + right * at.x + forward * at.z;
        Vector3 worldBarrel = (right * barrel.x + up * barrel.y + forward * barrel.z).normalized;
        Vector3 worldSight = right * sight.x + up * sight.y + forward * sight.z;
        if (worldBarrel.sqrMagnitude < .5f || Vector3.Cross(worldBarrel, worldSight).sqrMagnitude < 1e-6f) return false;
        // The copy turned so its barrel and sight point as asked, its middle on the spot, at the size the hands hold it.
        Quaternion own = Quaternion.LookRotation(gun.Barrel, gun.Up);
        Quaternion turn = Quaternion.LookRotation(worldBarrel, worldSight) * Quaternion.Inverse(own);
        float scale = ScaleOf(gun);
        var root = gun.Copy.transform;
        root.localScale = Vector3.one * scale;
        root.SetPositionAndRotation(spot - turn * (gun.Center * scale), turn);
        return true;
    }

    // The gun in the hands is as big as the body it hangs in (PlayerBody fits the model to the camera height).
    private static float ScaleOf(Worn gun) => gun.Source != null ? Mathf.Max(.01f, Mathf.Abs(gun.Source.transform.lossyScale.x)) : 1f;

    // The back's axes from the bones: up the spine from the hips to the neck, right across the shoulders.
    private static bool BackAxes(Animator animator, out Vector3 right, out Vector3 up, out Vector3 forward, out Vector3 hips, out Vector3 neck)
    {
        right = up = forward = hips = neck = Vector3.zero;
        var hipsBone = animator.GetBoneTransform(HumanBodyBones.Hips);
        var neckBone = animator.GetBoneTransform(HumanBodyBones.Neck) ?? animator.GetBoneTransform(HumanBodyBones.Head);
        if (hipsBone == null || neckBone == null) return false;
        hips = hipsBone.position;
        neck = neckBone.position;
        up = neck - hips;
        if (up.sqrMagnitude < 1e-6f) return false;
        up.Normalize();
        var l = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        var r = animator.GetBoneTransform(HumanBodyBones.RightUpperArm);
        right = l != null && r != null ? r.position - l.position : animator.transform.right;
        return Square(ref right, ref up, out forward);
    }

    // A thigh's axes: up the thigh from the knee to the hip, right across the hips.
    private static bool ThighAxes(Animator animator, bool left, out Vector3 right, out Vector3 up, out Vector3 forward, out Vector3 hip, out Vector3 knee)
    {
        right = up = forward = hip = knee = Vector3.zero;
        var upper = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperLeg : HumanBodyBones.RightUpperLeg);
        var lower = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerLeg : HumanBodyBones.RightLowerLeg);
        var otherUpper = animator.GetBoneTransform(left ? HumanBodyBones.RightUpperLeg : HumanBodyBones.LeftUpperLeg);
        if (upper == null || lower == null) return false;
        hip = upper.position;
        knee = lower.position;
        up = hip - knee;
        if (up.sqrMagnitude < 1e-6f) return false;
        up.Normalize();
        right = otherUpper != null ? (left ? otherUpper.position - hip : hip - otherUpper.position) : animator.transform.right;
        return Square(ref right, ref up, out forward);
    }

    // Right made square to up, and forward from the two.
    private static bool Square(ref Vector3 right, ref Vector3 up, out Vector3 forward)
    {
        right = Vector3.ProjectOnPlane(right, up);
        forward = Vector3.zero;
        if (right.sqrMagnitude < 1e-6f) return false;
        right.Normalize();
        forward = Vector3.Cross(right, up);
        return true;
    }

    // This PC's own player seen from its eyes: the worn guns only cast their shadows there, as the head does.
    private bool FirstPerson()
    {
        if (movement == null || !movement.IsLocal || movement.IsNetworkDriven || body == null) return false;
        var view = body.View;
        if (eyes == null) eyes = PlayerView.EyesOf(movement);
        if (view == null || eyes == null) return false;
        return (view.position - eyes.position).sqrMagnitude < firstPersonReach * firstPersonReach;
    }
}
