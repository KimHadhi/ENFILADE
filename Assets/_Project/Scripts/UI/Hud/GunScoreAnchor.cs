using UnityEngine;

/// <summary>
/// Holds the score popup off the gun itself, the way Dead Space hangs the ammo count off the weapon: just above the
/// right side of the handguard's rail, where a laser sight would mount (the gun's Side Rail, or measured from the gun:
/// its right surface at the bore's height, a little ahead of the support hand). Being a point on the gun, it rides the
/// gun's sway, recoil and reload, never the hand; when a reload drops the gun out of view it waits at the screen's
/// edge until the gun comes back. With the fists out it hangs off the right hand. It leans like the HUD's right side,
/// and while aiming shrinks and fades a little so the sights stay clear. With nothing in the hands it waits right of
/// the crosshair (GunFeel's Score Offset). Hud.csv's Popup row.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.GunScoreAnchor)]
public sealed class GunScoreAnchor : MonoBehaviour
{
    // How far along the handguard the measured mount sits, from the support hand toward the muzzle; and the most a
    // wide part (a scope's turret, a magazine) may push the measured right surface off the bore.
    private const float RailAhead = .35f, WidestSide = .06f, NarrowestSide = .02f;
    // Seconds the popup stays put while the hands are briefly empty (a weapon swap).
    private const float EmptyHandsGrace = .6f;

    [Tooltip("From the side rail to the popup's bottom-left corner, along the screen: right, up (m). Up keeps it above the gun. " +
        "The user found .08 up too high and wants it just above the rail's right end, 2026-11-09.")]
    [SerializeField] private Vector2 railOffset = new Vector2(.035f, .025f);
    [Tooltip("Turn about the vertical axis (°). Positive brings the right edge toward the player.")]
    [SerializeField] private float lean = 14f;
    [Tooltip("Size while fully aimed (share of normal).")]
    [SerializeField, Range(.3f, 1f)] private float aimScale = .82f;
    [Tooltip("Opacity while fully aimed (0-1).")]
    [SerializeField, Range(.2f, 1f)] private float aimAlpha = .82f;
    [Tooltip("How quickly the popup catches up with the gun (1/s). High keeps it attached; lower smooths jitter.")]
    [SerializeField, Min(1f)] private float follow = 30f;
    [Tooltip("Room kept free at the screen's top and right for the popup itself (px at 1080p).")]
    [SerializeField] private Vector2 room = new Vector2(240f, 150f);
    [Tooltip("Room kept at the screen's left and bottom (px at 1080p). With the gun out of view, as in a reload, the popup waits this far in.")]
    [SerializeField] private Vector2 edge = new Vector2(40f, 40f);

    private RectTransform holder, canvasRect;
    private CanvasGroup group;
    private PlayerMovement player;
    private WeaponController gun;
    private PlayerWeaponSlots slots;
    private Transform hand;
    private WeaponData measured;
    private Vector3 rail;
    private readonly Vector3[] corners = new Vector3[8];
    private bool placed;
    private float lastHeld = -99f;

    /// <summary>Move this group (the popup's parent) every frame.</summary>
    public void Hold(RectTransform group)
    {
        holder = group;
        this.group = group.gameObject.AddComponent<CanvasGroup>();
        this.group.interactable = this.group.blocksRaycasts = false;
        GameTables.Bind(HudCamera.TableName, "Popup", this);
    }

    private void LateUpdate()
    {
        if (holder == null) return;
        if (canvasRect == null) canvasRect = (RectTransform)GetComponentInParent<Canvas>().rootCanvas.transform;
        FindHands();
        Vector2 size = canvasRect.rect.size;
        Vector2 target = GunFeelSettings.Instance.scoreOffset;
        bool holding = TryAnchor(Camera.main, out Vector3 viewport);
        if (holding) lastHeld = Time.unscaledTime;
        // A weapon swap empties the hands for a moment; the popup waits where it is rather than jumping away and back.
        else if (placed && Time.unscaledTime - lastHeld < EmptyHandsGrace) target = holder.anchoredPosition;
        if (holding)
        {
            if (viewport.z > 0f)
            {
                Vector2 half = size * .5f;
                target = Vector2.Scale((Vector2)viewport - new Vector2(.5f, .5f), size);
                // Off the screen, as when a reload drops the gun out of view, it waits at the edge instead of leaving.
                target = new Vector2(Mathf.Clamp(target.x, edge.x - half.x, half.x - room.x),
                    Mathf.Clamp(target.y, edge.y - half.y, half.y - room.y));
            }
            else if (placed) target = holder.anchoredPosition;
        }
        Vector2 now = placed ? Vector2.Lerp(holder.anchoredPosition, target, 1f - Mathf.Exp(-follow * Time.unscaledDeltaTime)) : target;
        placed = true;
        holder.anchoredPosition = now;
        float aim = gun != null && gun.ShowsWeapon ? gun.AimBlend : 0f;
        holder.localRotation = Quaternion.Euler(0f, lean * GameSettings.Current.hudTilt, 0f); // Settings → HUD 기울기
        holder.localScale = Vector3.one * Mathf.Lerp(1f, aimScale, aim);
        group.alpha = Mathf.Lerp(1f, aimAlpha, aim);
    }

    private void FindHands()
    {
        var local = PlayerRoster.Local;
        if (local == player) return;
        player = local;
        gun = player != null ? player.GetComponentInChildren<WeaponController>(true) : null;
        slots = player != null ? player.GetComponentInChildren<PlayerWeaponSlots>(true) : null;
        hand = null;
        measured = null;
        if (player == null) return;
        foreach (var animator in player.GetComponentsInChildren<Animator>(true))
            if (animator.isHuman && (hand = animator.GetBoneTransform(HumanBodyBones.RightHand)) != null) break;
    }

    // Where the popup's corner goes, in the world camera's viewport (off the screen too): off the gun's side rail, or
    // off the right hand. False when the hands hold nothing to hang it on.
    private bool TryAnchor(Camera view, out Vector3 viewport)
    {
        viewport = default;
        if (view == null || player == null) return false;
        Vector3 mount;
        if (gun != null && gun.ShowsWeapon && gun.EquippedData != null)
        {
            if (measured != gun.EquippedData) Measure();
            mount = gun.WeaponRoot.TransformPoint(rail);
        }
        else if (hand != null && slots != null && slots.UsesFists) mount = hand.position;
        else return false;
        Transform eye = view.transform;
        viewport = view.WorldToViewportPoint(mount + eye.right * railOffset.x + eye.up * railOffset.y);
        return true;
    }

    // The rail mount in weapon-root space: the gun's authored Side Rail, or the gun's right surface at the bore's
    // height, a little ahead of the support hand on the handguard.
    private void Measure()
    {
        measured = gun.EquippedData;
        if (measured.SideRail != Vector3.zero) { rail = measured.SideRail; return; }
        var root = gun.WeaponRoot;
        Vector3 grip = measured.LeftGrip;
        Vector3 bore = gun.MuzzlePoint != null ? root.InverseTransformPoint(gun.MuzzlePoint.position) : new Vector3(grip.x, grip.y + .04f, grip.z + .3f);
        float right = float.NegativeInfinity;
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
        {
            // The muzzle's flash hangs off the gun but isn't part of it.
            if (gun.MuzzlePoint != null && renderer.transform.IsChildOf(gun.MuzzlePoint)) continue;
            Bounds local;
            Transform space = renderer.transform;
            if (renderer is SkinnedMeshRenderer skinned)
            {
                local = skinned.localBounds;
                if (skinned.rootBone != null) space = skinned.rootBone;
            }
            else if (renderer is MeshRenderer && renderer.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null) local = filter.sharedMesh.bounds;
            else continue;
            Vector3 c = local.center, e = local.extents;
            for (int i = 0; i < 8; i++)
                corners[i] = c + new Vector3((i & 1) == 0 ? -e.x : e.x, (i & 2) == 0 ? -e.y : e.y, (i & 4) == 0 ? -e.z : e.z);
            foreach (var corner in corners) right = Mathf.Max(right, root.InverseTransformPoint(space.TransformPoint(corner)).x);
        }
        float side = float.IsNegativeInfinity(right) ? bore.x + .03f : Mathf.Clamp(right, bore.x + NarrowestSide, bore.x + WidestSide);
        rail = new Vector3(side, bore.y, Mathf.Lerp(grip.z, bore.z, RailAhead));
    }
}
