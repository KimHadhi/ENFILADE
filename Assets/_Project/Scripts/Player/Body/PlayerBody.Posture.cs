using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// Down into a crouch and up out of it standing still, on BIPER Female Runner's own clips (IdleToCrouch, CrouchToIdle;
/// 0.9.285, the user: "Female Runner first", the pack's clips not yet in the game). Until then the two stances' still
/// poses blended into each other in a straight line. Each clip plays at its own pace, 0.4 s down and 0.5 s up, from the
/// moment its body sets off (Tables/Player/GaitMoves.csv kind posture, baked by ClipBench), and turned back half way the
/// other goes on from the height the body is at: how far of its way down it is (postureDown) is the clips' common measure,
/// so neither begins at its far end. Until 0.9.348 they followed the eyes' ease (Posture.transitionSharpness), so the view
/// would not lag the key: half the way in 0.05 s, 21 % of it in the first frame, the chest dropped and shot up in one go
/// (the user, 2026-11-22: "원래 애니메이션 속도 이렇게 빨라? 가슴이 앉을 때 자연스럽게 내려가는 게 아니라 걍 한번에 확 내려가고").
/// Standing still only: moving, the walk's crouched cycles take the body down and up, over about as long (MovingPosture).
/// </summary>
public sealed partial class PlayerBody
{
    [SerializeField, Tooltip("Standing still, down into a crouch and up out of one, at their own pace.")]
    private string crouchDown = "IdleToCrouch", standUp = "CrouchToIdle";

    // A posture clip's way as baked: when its body sets off and is there (s), and how far of its way it is (0-1) at evenly
    // spaced moments from its start to its end.
    private sealed class PostureWay
    {
        public float From, To;
        public float[] Share;
    }

    private readonly Dictionary<string, PostureWay> postureWays = new Dictionary<string, PostureWay>();
    // The posture clip under way, or null, and how far down the body is (0 standing, 1 crouched).
    private string postureClip;
    private float postureDown;
    // Moving, the stances' cycles cross over this slowly (s), about as long as the clips take.
    private const float MovingPosture = .3f;

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

    // From Enter, on the ground: a body standing still going down or getting up takes its clip, from the height it is at; one
    // moving, the cycles.
    private void StartPosture(MovementState from, MovementState to)
    {
        bool turnsBack = postureClip != null;
        postureClip = null;
        string clip = from == MovementState.Standing && to == MovementState.Crouching ? crouchDown
            : from == MovementState.Crouching && to == MovementState.Standing ? standUp : null;
        if (clip == null) return;
        if (HorizontalSpeed() > StillTravel || !postureWays.ContainsKey(clip) || Find(clip) == null)
        {
            SlowBlend(MovingPosture);
            return;
        }
        if (!turnsBack) postureDown = from == MovementState.Standing ? 0f : 1f;
        Restart(Find(clip));
        postureClip = clip;
    }

    // From ChoosePose, standing or crouched on the ground: the clip a step on from where the body is in it. False once it is
    // there, or the body moves off (the stance's own moves take over, blended).
    private bool FollowPosture(float dt)
    {
        if (postureClip == null) return false;
        var channel = Find(postureClip);
        bool goesDown = postureClip == crouchDown;
        if (channel == null || channel.Synced || channel.Length <= 0f || !postureWays.TryGetValue(postureClip, out var way))
        {
            postureClip = null;
            return false;
        }
        if (HorizontalSpeed() > StillTravel)
        {
            postureClip = null;
            SlowBlend(MovingPosture);
            return false;
        }
        float moment = PostureMoment(way, channel.Length, goesDown ? postureDown : 1f - postureDown) + dt;
        if (moment >= way.To)
        {
            postureDown = goesDown ? 1f : 0f;
            postureClip = null;
            return false;
        }
        float share = PostureShare(way, channel.Length, moment);
        postureDown = goesDown ? share : 1f - share;
        Drive(postureClip, moment / channel.Length);
        return true;
    }

    // How far of its way the clip's body has come at this moment.
    private static float PostureShare(PostureWay way, float length, float moment)
    {
        float at = Mathf.Clamp01(moment / length) * (way.Share.Length - 1);
        int low = Mathf.Min(way.Share.Length - 2, Mathf.FloorToInt(at));
        return Mathf.Clamp01(Mathf.Lerp(way.Share[low], way.Share[low + 1], at - low));
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
