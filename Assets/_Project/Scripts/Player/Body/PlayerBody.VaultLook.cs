using UnityEngine;
using UnityEngine.Playables;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// The body in a vault while the player looks round, with the view always free (user, 2026-11-07: "유저의 자유를 빼앗는 건
/// 정답이 아니야"; Beatrun holds a vault's view within 70 degrees, which was tried and dropped).
/// - The whole body turns with the view once the view leads it by more than vaultLeadLimit, legs and all, so the
///   camera on the real eyes never looks back through the own neck, and the upper body never twists away from the legs
///   (user, 2026-11-07: looking back mid-vault showed the inside of the body, and with a gun the upper body stood 180
///   degrees from the legs). Moving the camera to the eyes of a body turned to the view (Beatrun's CamIgnoreAng) was
///   tried first and still showed the body from inside.
/// - Otherwise, for the first part of the vault's clip (vaultover and vaultkong to 0.65 of it, vaultoverhigh to 0.75) the
///   body keeps the vault's way; then it turns after the view as Beatrun's does (LerpAngle at lerpspeed 10 a second,
///   times the speed's share of 200 units/s; at 8 a second once the view is more than about 105 degrees round).
/// Numbers come from Tables/Player.csv, row Body.
/// </summary>
public sealed partial class PlayerBody
{
    [Header("Vault look")]
    [SerializeField, Range(0f, 90f), Tooltip("In a vault, how far the view may lead the body before the whole body turns with it "
        + "(degrees), so the camera on the eyes never looks through the own neck.")]
    private float vaultLeadLimit = 45f;
    [SerializeField, Tooltip("Share of the vault's clip after which the body turns after the view: over and kong; the high "
        + "vault. Beatrun's 0.65 and 0.75.")]
    private Vector2 vaultTurnFrom = new Vector2(.65f, .75f);

    // Beatrun's body turn after the view (jumpanim.lua): lerpspeed, the speed (units/s) it reaches full rate at, below which
    // it doesn't scale (5 units/s), the slower turn past the dot below, and that dot (about 105 degrees).
    private const float VaultTurnRate = 10f, VaultTurnFullSpeed = 200f, VaultTurnLeastSpeed = 5f,
        VaultTurnBackRate = 8f, VaultTurnBackDot = -.25f;

    private bool InVault(out Channel channel)
    {
        channel = !dead && state == MovementState.Vault && player.Abilities.Traversal.Obstacle.Kind != VaultKind.None
            ? Find(obstacleClip) : null;
        return channel != null && channel.Length > 0f;
    }

    // obstacleYaw is the way the vault's body faces.
    private void TurnVaultWithView(float dt)
    {
        if (dt <= 0f || !InVault(out var channel)) return;
        float view = transform.eulerAngles.y;
        float from = obstacleClip == vaultHigh ? vaultTurnFrom.y : vaultTurnFrom.x;
        if ((float)channel.Playable.GetTime() / channel.Length >= from)
        {
            float speed = player.ActualVelocity.magnitude / Mathf.Max(.0001f, player.HeightUnit);
            float share = speed > VaultTurnLeastSpeed ? Mathf.Min(speed / VaultTurnFullSpeed, 1f) : 1f;
            float next = Mathf.LerpAngle(obstacleYaw, view, Mathf.Min(VaultTurnRate * dt * share, 1f));
            if (Mathf.Cos(Mathf.DeltaAngle(next, view) * Mathf.Deg2Rad) <= VaultTurnBackDot)
                next = Mathf.LerpAngle(obstacleYaw, view, Mathf.Min(VaultTurnBackRate * dt, 1f));
            obstacleYaw = next;
        }
        // However early in the vault: the view never leads the body by more than the neck allows.
        float lead = Mathf.DeltaAngle(obstacleYaw, view), limit = Mathf.Clamp(vaultLeadLimit, 0f, 90f);
        if (Mathf.Abs(lead) > limit) obstacleYaw = view - Mathf.Sign(lead) * limit;
    }
}
