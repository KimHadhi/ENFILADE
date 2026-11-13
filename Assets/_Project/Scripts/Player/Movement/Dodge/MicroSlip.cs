using UnityEngine;

/// <summary>
/// How a Q/E micro dodge that catches a real blow moves: a slip that turns onto its attacker. The head leaves the blow's
/// line just far enough (the eyes end at least clearance to the pressed side of where they stood, as a lean), dips a
/// little and goes back to where the key holds the lean once the blow is past. Meanwhile the look locks onto the
/// attacker: it turns to face them and keeps them there while the head slips out and comes back, so stepping aside and
/// turning in are one move. A straight counter keeps the look on its target as it dashes in; turning the look yourself
/// lets go, for instance to counter another attacker. The body never moves, so a counter is still in reach. The edge of
/// the screen on the side the blow went by glints. Real time throughout, so it is as quick inside the evade's slow
/// motion. The lean itself is MovementLean's; the dip and the turns go through CombatHeadMotion and the glint through
/// CombatEdgeGlint; PlayerPerfectDefense says whom to lock onto. Numbers: Tables/Player.csv, row Slip.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.MicroSlip)]
public sealed class MicroSlip : MonoBehaviour
{
    [SerializeField, Min(0f), Tooltip("The eyes end at least this far (m) to the pressed side of where they stood; a lean already further stays.")]
    private float clearance = .25f;
    [SerializeField, Min(.01f), Tooltip("Slipping out, real seconds.")]
    private float slipSeconds = .12f;
    [SerializeField, Min(0f), Tooltip("Held out while the blow goes by, real seconds.")]
    private float holdSeconds = .48f;
    [SerializeField, Min(.01f), Tooltip("Back to where the key holds the lean, real seconds.")]
    private float returnSeconds = .3f;
    [SerializeField, Min(0f), Tooltip("How far the head dips while slipped (m).")]
    private float dip = .06f;
    [SerializeField, Range(0f, 1f), Tooltip("Brightness of the glint along the edge the blow went by. 0 turns it off.")]
    private float glint = .35f;
    [SerializeField, Min(.01f), Tooltip("How long that glint lasts, real seconds.")]
    private float glintSeconds = .35f;
    [SerializeField, Tooltip("Colour of that glint.")]
    private Color glintColor = new Color(.88f, .93f, .97f);
    [Header("Look lock")]
    [SerializeField, Range(0f, 90f), Tooltip("The look locks onto the slipped attacker if they stand within this many degrees of it. 0 turns the lock off.")]
    private float lockAngle = 40f;
    [SerializeField, Min(1f), Tooltip("How fast the look turns onto them (1/s: the share of the turn left taken each second).")]
    private float lockRate = 20f;
    [SerializeField, Min(0f), Tooltip("Turning the look yourself this many degrees while it is locked lets go.")]
    private float lockLetGo = 5f;

    private PlayerMovement owner;
    private MeleeCombatTarget locked;
    private float lockLeft, lastYaw, asked, ownTurn;
    private bool measured;

    public float Clearance => clearance;
    public float SlipSeconds => slipSeconds;
    public float HoldSeconds => holdSeconds;
    public float ReturnSeconds => returnSeconds;
    /// <summary>The look is locked onto an attacker.</summary>
    public bool IsLocked => locked != null;

    /// <summary>The player's slip numbers, made and bound to the table on first use.</summary>
    public static MicroSlip For(PlayerMovement player)
    {
        if (player == null || !Application.isPlaying) return null;
        if (player.TryGetComponent(out MicroSlip slip)) return slip;
        slip = player.gameObject.AddComponent<MicroSlip>();
        GameTables.Bind(PlayerMovement.TableName, "Slip", slip);
        return slip;
    }

    private void Awake() => owner = GetComponent<PlayerMovement>();

    /// <summary>The slip began, towards side (+1 right): the dip and the glint, on this player's own PC only.</summary>
    internal void Show(PlayerMovement player, int side)
    {
        if (player == null || !player.IsLocal || player.IsNetworkDriven) return;
        if (dip > 0f) CombatHeadMotion.For(player).OrNull()?.Dip(dip, slipSeconds, holdSeconds, returnSeconds);
        if (glint > 0f) CombatEdgeGlint.For(player).OrNull()?.Show(-side, glintColor, glint, glintSeconds);
    }

    /// <summary>The slipped blow came from attacker: the look locks onto them for as long as the slip lasts, if they stand
    /// within lockAngle of it.</summary>
    internal void LockOn(MeleeCombatTarget attacker)
    {
        if (lockAngle <= 0f || attacker == null || !attacker.IsAvailable || !Mine()) return;
        Transform eyes = PlayerView.EyesOf(owner);
        if (eyes == null || Mathf.Abs(PlayerPerfectDefense.Bearing(eyes, attacker)) > lockAngle) return;
        Begin(attacker, slipSeconds + holdSeconds + returnSeconds);
    }

    /// <summary>A straight counter dashes in after a locked slip: the look stays on target for up to seconds, or until LetGo.</summary>
    internal void KeepOn(MeleeCombatTarget target, float seconds)
    {
        if (target != null && Mine()) Begin(target, seconds);
    }

    internal void LetGo() { locked = null; lockLeft = 0f; }

    private void Begin(MeleeCombatTarget target, float seconds)
    {
        locked = target; lockLeft = Mathf.Max(0f, seconds); ownTurn = 0f; measured = false;
    }

    private bool Mine() => owner != null && owner.IsLocal && !owner.IsNetworkDriven;

    // Before the look (PlayerLook) takes this frame's turn.
    private void Update()
    {
        if (locked == null || Time.timeScale <= 0f) return;
        float dt = Mathf.Min(.05f, Time.unscaledDeltaTime);
        lockLeft -= dt;
        Transform eyes = PlayerView.EyesOf(owner);
        if (lockLeft <= 0f || !locked.IsAvailable || eyes == null || owner.IsDead) { LetGo(); return; }
        float yaw = owner.transform.eulerAngles.y;
        // Whatever the look turned beyond the lock's own turn is the player's: enough of it and the lock lets go.
        if (measured && (ownTurn += Mathf.Abs(Mathf.DeltaAngle(lastYaw + asked, yaw))) > lockLetGo) { LetGo(); return; }
        asked = PlayerPerfectDefense.Bearing(eyes, locked) * (1f - Mathf.Exp(-lockRate * dt));
        CombatHeadMotion.For(owner).OrNull()?.Turn(asked);
        lastYaw = yaw; measured = true;
    }

    private void OnDisable() => LetGo();
}
