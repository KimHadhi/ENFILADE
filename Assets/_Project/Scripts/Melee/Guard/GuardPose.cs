using System;
using UnityEngine;

/// <summary>
/// The guard's parry pose struck for a moment as a look only (the user, 2026-11-12, on the special-moment picks: "아군을
/// 방어해 주는 건 특유의 패링 자세와 함께 공격을 튕겨내는 화려한 이펙트가 있었으면 좋겠음"): a Knight's cover turning a blow
/// for a friend (KnightCover.Covered) strikes the pose a parry strikes, beside the deflect's look (CardFx CoverDeflect).
/// - With the fists out: the far hand snaps across to the blow's side, holds and lets go, the guard up with it, as a
///   parry's sweep (FistsView.Deflect). The body's hands are those points (PlayerBodyHands), so the first-person view and
///   the body others see are one pose.
/// - With a gun: the gun stays in the right hand and the left hand leaves it to knock the blow aside with the forearm, the
///   fist turned out before the face, and takes the gun again (GuardPoseArm, through PlayerBodyHands.LendOffHand).
/// It opens no guard or parry, takes no input and changes no weapon: a look. Struck on this PC's own player it goes to
/// every other PC (Posed → PlayerNetworkCombat.GuardPoseRpc → Show), so friends see the stance the Knight sees (the
/// user's rule, "내가 보는 화면이 진실"). Real time. Numbers: Tables/Combos.csv, row GuardPose.
/// </summary>
[DisallowMultipleComponent]
public sealed class GuardPose : MonoBehaviour
{
    [Serializable]
    public sealed class Numbers
    {
        [Min(.05f), Tooltip("The whole pose: in, held and out (real s).")]
        public float seconds = .3f;
        [Min(.01f), Tooltip("Snapping into it (real s).")]
        public float attack = .04f;
        [Min(0f), Tooltip("Settling at full reach (real s).")]
        public float hold = .12f;
        [Tooltip("Fists: where the sweeping hand ends up, eyes space toward the blow's side (m; across;up;forward).")]
        public Vector3 sweep = new Vector3(.2f, .02f, .42f);
        [Tooltip("Gun: where the left wrist blocks, before the face (eyes space, m; across;up;forward).")]
        public Vector3 gunWrist = new Vector3(-.04f, -.07f, .36f);
        [Tooltip("Gun: where the wrist comes up from as it knocks the blow aside (eyes space, m).")]
        public Vector3 gunFrom = new Vector3(-.22f, -.2f, .3f);
        [Tooltip("Gun: where the elbow points (eyes space, m).")]
        public Vector3 gunElbow = new Vector3(-.32f, -.32f, .12f);
        [Range(0f, 270f), Tooltip("Gun: how far the fingertips turn from the hand's run into the fist (degrees).")]
        public float gunCurl = 230f;
    }

    public const string TableName = MeleeCombos.TableName;

    [SerializeField] private Numbers numbers = new Numbers();

    private PlayerMovement player;
    private PlayerMeleeCombat melee;
    private FistsView fists;

    /// <summary>The pose was struck on this PC's own player, toward this point: PlayerNetworkCombat tells the other PCs.</summary>
    public event Action<Vector3> Posed;

    /// <summary>The player's guard pose, made and bound to the table on first use.</summary>
    public static GuardPose For(PlayerMovement player)
    {
        if (player == null || !Application.isPlaying) return null;
        if (player.TryGetComponent(out GuardPose pose)) return pose;
        pose = player.gameObject.AddComponent<GuardPose>();
        GameTables.BindNested(TableName, "GuardPose", pose, pose.numbers);
        return pose;
    }

    /// <summary>
    /// Strikes the guard's parry pose on <paramref name="player"/> toward <paramref name="threat"/>, a look only (the
    /// Knight's cover, KnightCover.Covered). Struck on this PC's own player, every other PC sees it too.
    /// </summary>
    public static void Strike(PlayerMovement player, Vector3 threat)
    {
        var pose = For(player);
        if (pose == null) return;
        pose.Show(threat);
        if (player.IsLocal && !player.IsNetworkDriven) pose.Posed?.Invoke(threat);
    }

    /// <summary>The pose on this body: the player's own, or this PC's copy of another's (PlayerNetworkCombat.GuardPoseRpc).</summary>
    internal void Show(Vector3 threat)
    {
        if (player == null) player = GetComponent<PlayerMovement>();
        if (melee == null) melee = GetComponent<PlayerMeleeCombat>();
        if (player == null || player.IsDead || player.IsDowned) return;
        if (melee != null && melee.IsActive)
        {
            if (fists == null) fists = GetComponentInChildren<FistsView>();
            Transform eyes = PlayerView.EyesOf(player);
            if (fists == null || eyes == null) return;
            Vector3 toward = threat - eyes.position;
            float side = toward.sqrMagnitude > 1e-4f ? Vector3.Dot(eyes.right, toward.normalized) : 0f;
            fists.Deflect(side, numbers.seconds, numbers.attack, numbers.hold, numbers.sweep);
            return;
        }
        GuardPoseArm.Of(player)?.Strike(new GuardPoseArm.Shape
        {
            Seconds = numbers.seconds, Attack = numbers.attack, Hold = numbers.hold, Curl = numbers.gunCurl,
            From = numbers.gunFrom, Wrist = numbers.gunWrist, Elbow = numbers.gunElbow,
        });
    }
}
