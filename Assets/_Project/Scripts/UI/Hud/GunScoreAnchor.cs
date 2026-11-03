using UnityEngine;

/// <summary>
/// Holds the score popup off the front of whatever is in the player's hands, the way Dead Space hangs the ammo count off
/// the gun: a little up and right of the handguard, where the support hand grips (the gun's own Left Grip, so a reload's
/// hand never drags it along), or of the right hand while the fists are out. It rides the gun's sway and recoil, leans
/// like the HUD's right side, and while aiming shrinks and fades a little so the sights stay clear. With nothing to hold
/// on to it waits right of the crosshair (GunFeel's Score Offset). Hud.csv's Popup row.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(10040)]
public sealed class GunScoreAnchor : MonoBehaviour
{
    [Tooltip("From the grip to the popup's bottom-left corner, along the screen: right, up (m).")]
    [SerializeField] private Vector2 gripOffset = new Vector2(.05f, .09f);
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

    private RectTransform holder, canvasRect;
    private CanvasGroup group;
    private PlayerMovement player;
    private WeaponController gun;
    private PlayerWeaponSlots slots;
    private Transform hand;
    private bool placed;

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
        if (TryGrip(Camera.main, out Vector2 viewport))
        {
            target = Vector2.Scale(viewport - new Vector2(.5f, .5f), size);
            Vector2 most = size * .5f - room;
            target = new Vector2(Mathf.Min(target.x, most.x), Mathf.Min(target.y, most.y));
        }
        Vector2 now = placed ? Vector2.Lerp(holder.anchoredPosition, target, 1f - Mathf.Exp(-follow * Time.unscaledDeltaTime)) : target;
        placed = true;
        holder.anchoredPosition = now;
        float aim = gun != null && gun.ShowsWeapon ? gun.AimBlend : 0f;
        holder.localRotation = Quaternion.Euler(0f, lean, 0f);
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
        if (player == null) return;
        foreach (var animator in player.GetComponentsInChildren<Animator>(true))
            if (animator.isHuman && (hand = animator.GetBoneTransform(HumanBodyBones.RightHand)) != null) break;
    }

    // Where the popup's corner goes, in the world camera's viewport: off the gun's handguard grip, or off the right hand.
    private bool TryGrip(Camera view, out Vector2 viewport)
    {
        viewport = default;
        if (view == null || player == null) return false;
        Vector3 grip;
        if (gun != null && gun.ShowsWeapon && gun.EquippedData != null) grip = gun.WeaponRoot.TransformPoint(gun.EquippedData.LeftGrip);
        else if (hand != null && slots != null && slots.UsesFists) grip = hand.position;
        else return false;
        Transform eye = view.transform;
        Vector3 point = view.WorldToViewportPoint(grip + eye.right * gripOffset.x + eye.up * gripOffset.y);
        viewport = point;
        return point.z > 0f && point.x > 0f && point.x < 1f && point.y > 0f && point.y < 1f;
    }
}
