using UnityEngine;

/// <summary>
/// A set piece's motion, authored once: rigid parts that each go from where the builder laid them to where they end,
/// all by one progress from 0 to 1 (a jet bridge's telescoping lengths sliding out of one another, GimmickJetBridge).
/// The user, 2026-11-18: "4장 탑승구 확장 연출 최적화: 실시간 연산 대신 베이크된(Baked) 애니메이션으로 교체하여 프레임
/// 드랍(렉) 개선". What ran live was a parent's scale written every frame on every PC for the whole chapter, which
/// resized four colliders that had no body (the physics takes a moved or resized collider with no Rigidbody as part of
/// the still world, and rebuilds for it). Here nothing is worked out at play but where on its track each part is:
/// - the track is the builder's (each part's whole move and the easing curve are saved with the map, as a clip's keys
///   are; kept as numbers on the set piece, not as an AnimationClip, because sampling a clip onto a scene at play
///   could not be tried without running the game, and these few keys need no animation system);
/// - a part is a rigid thing on a kinematic body of its own (the builder adds it): it is moved, never resized;
/// - Show writes only when the progress shown has changed, so a bridge that stands still costs nothing.
/// </summary>
[System.Serializable]
public sealed class SetPieceTrack
{
    // Two progresses nearer than this show the same pose.
    private const float Same = .0005f;

    [SerializeField, Tooltip("The parts that move, each a rigid group on a kinematic body.")]
    private Transform[] parts = new Transform[0];
    [SerializeField, Tooltip("Each part's whole move from where it was laid, in its parent's space (m).")]
    private Vector3[] travel = new Vector3[0];
    [SerializeField, Tooltip("How far along its move a part is at each progress (0..1 both ways): a straight line unless the builder shaped it.")]
    private AnimationCurve ease = AnimationCurve.Linear(0f, 0f, 1f, 1f);

    private Vector3[] rest;
    private float shown = float.NaN;

    /// <summary>It has parts to move (a map built before the track was made has none).</summary>
    public bool Any => parts != null && parts.Length > 0;

    /// <summary>Puts every part where the track has it at this progress (0 as laid, 1 at its end). Nothing is written
    /// while the progress stays where it was shown last.</summary>
    public void Show(float progress)
    {
        progress = Mathf.Clamp01(progress);
        if (!Any || Mathf.Abs(progress - shown) < Same) return;
        if (rest == null)
        {
            rest = new Vector3[parts.Length];
            for (int i = 0; i < parts.Length; i++) rest[i] = parts[i] != null ? parts[i].localPosition : Vector3.zero;
        }
        shown = progress;
        float along = ease != null && ease.length > 0 ? ease.Evaluate(progress) : progress;
        for (int i = 0; i < parts.Length; i++)
            if (parts[i] != null && i < travel.Length) parts[i].localPosition = rest[i] + travel[i] * along;
    }
}
