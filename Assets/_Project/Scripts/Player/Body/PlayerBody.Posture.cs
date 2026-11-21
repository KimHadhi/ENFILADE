using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// Down into a crouch and up out of it standing still, on BIPER Female Runner's own clips (IdleToCrouch, CrouchToIdle;
/// 0.9.285, the user: "Female Runner first", the pack's clips not yet in the game). Until then the two stances' still
/// poses blended into each other in a straight line. The clip follows the posture rather than its own clock: the eye and
/// the capsule ease toward the new stance at Posture.transitionSharpness (nine tenths in about a sixth of a second) while
/// the clip's body takes 0.4 s, and the camera rides the head, so on its own clock the view would lag the key. Its moment
/// is where its body centre has come as far of its way as the posture has (Tables/Player/GaitMoves.csv kind posture,
/// baked by ClipBench). Standing still only: moving, the walk's crouched cycles take the body down and up.
/// </summary>
public sealed partial class PlayerBody
{
    [SerializeField, Tooltip("Standing still, down into a crouch and up out of one; they follow the posture's own ease.")]
    private string crouchDown = "IdleToCrouch", standUp = "CrouchToIdle";

    // A posture clip's way as baked: when its body sets off and is there (s), and how far of its way it is (0-1) at evenly
    // spaced moments from its start to its end.
    private sealed class PostureWay
    {
        public float From, To;
        public float[] Share;
    }

    private readonly Dictionary<string, PostureWay> postureWays = new Dictionary<string, PostureWay>();
    // The posture clip under way, or null.
    private string postureClip;
    // Past this share of its way the posture is there, and the stance's own pose takes over.
    private const float PostureThere = .97f;

    private void ReadPostures()
    {
        postureWays.Clear();
        foreach (string id in GameTables.RowIds(GaitMovesTable))
        {
            if (!GameTables.TryGetRow(GaitMovesTable, id, out var row) || row.Text("kind") != "posture") continue;
            var share = new List<float>();
            foreach (string cell in row.Text("profile").Split(';'))
                if (float.TryParse(cell, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)) share.Add(value);
            if (share.Count < 2) continue;
            postureWays[id] = new PostureWay { From = row.Number("leave", 0f), To = row.Number("land", 0f), Share = share.ToArray() };
        }
    }

    // From Enter, on the ground: a body standing still going down or getting up takes its clip; anything else, none.
    private void StartPosture(MovementState from, MovementState to)
    {
        postureClip = null;
        if (HorizontalSpeed() > StillTravel) return;
        string clip = from == MovementState.Standing && to == MovementState.Crouching ? crouchDown
            : from == MovementState.Crouching && to == MovementState.Standing ? standUp : null;
        if (clip == null || !postureWays.ContainsKey(clip) || Find(clip) == null) return;
        Restart(Find(clip));
        postureClip = clip;
    }

    // From ChoosePose, standing or crouched on the ground: the clip at the posture's share of its way. False once it is
    // there, or the body moves off (the stance's own moves take over, blended).
    private bool FollowPosture()
    {
        if (postureClip == null) return false;
        var channel = Find(postureClip);
        float share = 1f - Mathf.Exp(-Mathf.Max(.1f, player.transitionSharpness) * stateTime);
        if (channel == null || channel.Synced || channel.Length <= 0f || share >= PostureThere || HorizontalSpeed() > StillTravel
            || !postureWays.TryGetValue(postureClip, out var way))
        {
            postureClip = null;
            return false;
        }
        Drive(postureClip, PostureMoment(way, channel.Length, share) / channel.Length);
        return true;
    }

    // The moment the clip's body has come this share of its way, between setting off and being there.
    private static float PostureMoment(PostureWay way, float length, float share)
    {
        var shares = way.Share;
        float step = length / (shares.Length - 1);
        float lastTime = way.From, last = 0f;
        for (int i = 0; i < shares.Length; i++)
        {
            float time = i * step;
            if (time <= way.From) continue;
            if (time > way.To) break;
            float value = shares[i];
            if (value >= share)
                return Mathf.Lerp(lastTime, time, Mathf.Approximately(value, last) ? 1f : Mathf.InverseLerp(last, value, share));
            lastTime = time;
            last = value;
        }
        return way.To;
    }
}
