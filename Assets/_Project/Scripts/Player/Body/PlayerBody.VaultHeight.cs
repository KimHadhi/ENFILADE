using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// In a quick vault (Over, Kong) the body stays at the height the move started from while the capsule rises over the
/// obstacle, and comes down with the capsule after; the clip lifts it over. Beatrun's vault clips were made with their
/// root on the floor: they lift the body over the obstacle themselves, legs tucked and hands down at the top. Beatrun's
/// move lifts the player to chest height too, and its body follows the player, so the body went over twice as high.
/// Measured 2026-11-07 on Sil: hands 0.5-1.1 m above the top all through the vault, with an arm of 0.48 m; the clips' own
/// hands sit about 1.3 m above their root mid-kong, legs tucked over 1 m up (user: "캐릭터가 높이 위에 있어서 그런가?").
/// Kept at its start height, the body's eyes stay within about -0.1 to +0.2 m of standing: the clip's own hop.
/// - The capsule keeps Beatrun's path and timing; only the body, and the camera on its eyes, stay down.
/// - No foot comes within vaultFeetClear of what is under it, the obstacle's top included, over the next vaultFeetAhead
///   of the way across (FeetClearance): a clip that lifts itself less than the obstacle needs (vaultover's swinging leg
///   over a box) is carried up by what it lacks, ahead of time.
/// - The body's height moves smoothly in the world (vaultHeightSmooth: up, then down), never in jumps: the feet rule
///   snapping it up and down read as stutter (user, 2026-11-07: "버벅거려"). A foot may graze a top for that moment.
/// - Raised for the feet, it stays raised for the rest of the move, and comes down only with the capsule's descent.
/// - What room is left is the hands' to bring the body further down onto the top (PlayerBodyHands.Pull).
/// Nothing is per clip or per character: a clip that lifts itself keeps its own height, one that doesn't is carried up by
/// the feet rule as the capsule would have. Body.vaultBodyOnClip 0 puts the body back on the capsule, as in Beatrun.
/// </summary>
public sealed partial class PlayerBody
{
    [SerializeField, Tooltip("Quick vaults: the body keeps the height the move started from and lets its clip lift it over, instead of riding up with the capsule.")]
    private bool vaultBodyOnClip = true;
    [SerializeField, Min(0f), Tooltip("Quick vaults: how far above the floor under them, or the obstacle's top, the feet stay while the body is kept down (m).")]
    private float vaultFeetClear = .05f;
    [SerializeField, Min(.01f), Tooltip("Quick vaults: how far ahead (s) the feet's way across is read, so the body comes up in time for a top they reach.")]
    private float vaultFeetAhead = .2f;
    [SerializeField, Tooltip("Quick vaults: how smoothly the body's height follows (s): going up, coming down.")]
    private Vector2 vaultHeightSmooth = new Vector2(.06f, .15f);

    private FeetClearance feetClearance;
    // The capsule's height as the move started; the lowest the body may be for the rest of the move (raised for the feet);
    // how far below the capsule it is; its height in the world and how fast that is changing.
    private float liftBase, raisedRoot, keptDown, heldRoot, heldRootSpeed;
    private bool wasQuickVault;

    /// <summary>A quick vault (Over, Kong) is under way or its clip is playing out.</summary>
    internal bool InQuickVault { get; private set; }
    /// <summary>How much further down the body can still come this frame before a foot nears what is under it (m).</summary>
    internal float FeetRoomNow { get; private set; }
    /// <summary>The same, over the feet's way across for vaultFeetAhead.</summary>
    internal float FeetRoomSoon { get; private set; }

    /// <summary>Temporary, for Logs/body-probe.txt.</summary>
    internal string VaultHeightProbe => $"kept down {keptDown:0.000} feet room {FeetRoomNow:0.000}/{FeetRoomSoon:0.000}";

    private void BindFeetClearance()
    {
        feetClearance = new FeetClearance(animator, transform);
        keptDown = heldRootSpeed = 0f;
        wasQuickVault = InQuickVault = false;
    }

    // How far below the capsule the body goes this frame (m). Before the hands are placed and the camera is put on the eyes.
    private float KeepDownForQuickVault(float dt)
    {
        var obstacle = player.Abilities.Traversal.Obstacle;
        float capsule = transform.position.y;
        bool quick = !dead && obstacle.Kind.ClearsTop() && (state == MovementState.Vault || PlaysOutObstacle);
        if (quick && !wasQuickVault)
        {
            liftBase = raisedRoot = capsule;
            if (keptDown <= 0f) { heldRoot = capsule; heldRootSpeed = 0f; }
        }
        wasQuickVault = InQuickVault = quick;
        if (feetClearance == null || (!quick && keptDown <= 0f && (hands == null || !hands.PullsBody)))
        {
            keptDown = heldRootSpeed = 0f;
            heldRoot = capsule;
            FeetRoomNow = FeetRoomSoon = 0f;
            return 0f;
        }
        float top = quick && obstacle.Surface != null ? obstacle.Edge.y : float.NegativeInfinity;
        feetClearance.Measure(player.groundLayers, player.ActualVelocity, top, vaultFeetClear, vaultFeetAhead, dt, out float now, out float soon);
        float down = quick && vaultBodyOnClip ? Mathf.Min(Mathf.Max(0f, capsule - liftBase), soon) : 0f;
        // Once the feet have had it raised, the body stays that high for the rest of the move and only comes down with the
        // capsule's own descent to the landing: going back down as soon as a leg had passed sank the view mid-vault (the
        // one-hand vault's low leg over a box, user 2026-11-07: "카메라가 갑자기 쑥 하고 낮아지는").
        if (quick) raisedRoot = Mathf.Max(raisedRoot, capsule - down);
        // Smoothed in the world, where the eyes are seen from: kept at its start height the target stands still while the
        // capsule rises, so it doesn't lag behind it.
        float target = quick ? Mathf.Min(capsule, raisedRoot) : capsule;
        float smooth = Mathf.Max(.001f, target > heldRoot ? vaultHeightSmooth.x : vaultHeightSmooth.y);
        heldRoot = Mathf.SmoothDamp(heldRoot, target, ref heldRootSpeed, smooth, Mathf.Infinity, dt);
        keptDown = capsule - heldRoot;
        if (keptDown <= 0f || (!quick && keptDown < .0005f))
        {
            // Never above the capsule; let go once it has come back to it.
            keptDown = heldRootSpeed = 0f;
            heldRoot = capsule;
        }
        FeetRoomNow = Mathf.Max(0f, now - keptDown);
        FeetRoomSoon = Mathf.Max(0f, soon - keptDown);
        return keptDown;
    }
}
