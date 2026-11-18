using UnityEngine;

// dev-text: file (a probe's report for whoever checks the lean afterwards; nothing here is shown in the game)

/// <summary>
/// Console /lean: the lean measured without anyone pressing Q or E. It carries the lean of this PC's player from upright
/// to the full right, back, to the full left and back over a few seconds, in whatever stance and with whatever is in the
/// hands just then, and writes to Logs/lean-probe.txt how far each ankle went from where it stood (the feet must stay:
/// the user, 2026-11-18: "제자리에서 상체만 기울어야 하나 ... 발(하체)까지 옆으로 이동"), whether each foot kept its place
/// (FootPlanting.Hold), and how far the hips and the eyes went, which is the lean itself. Run it once with nothing in
/// hand, once with the fists and once with a gun, standing and crouched, on each body to be checked (the mannequin too):
/// each run is one line.
///
/// It runs right after the body (FrameOrder.Body + 1), because of what it reads: the feet after this frame's leg IK.
/// Read before the body's LateUpdate they are the clip's own feet, which the animation pass has just written over last
/// frame's pose, and those never move in a lean: the probe would say 0 cm whatever the feet did. The lean it asks for
/// goes through the lean's own owner (MovementLean.Probed), so the movement hands the same lean to everything that
/// reads it in the next frame.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.Body + 1)]
internal sealed class LeanProbe : MonoBehaviour
{
    private const string Log = "lean-probe";
    private const string Heading = "/lean: one line a run. feet = the most each ankle went from where it stood, level, and up or down (cm): under 1 is standing still. hips, eyes = the most they went sideways; hips down = how far the hips came down for the legs to reach (cm).";
    // How long a run takes (s), and how long it stands upright first so the feet have their places (s).
    private const float Seconds = 4f, Settle = .5f;

    private PlayerMovement player;
    private PlayerBody body;
    private Transform hips, eyes;
    private Vector3 leftStart, rightStart, hipsStart, eyesStart;
    private float time, leftMost, rightMost, leftRise, rightRise, hipsMost, hipsDown, eyesMost, leanMost;
    private bool started, leftKept, rightKept, lostKeep;

    /// <summary>Starts a run on this player; says what it is doing.</summary>
    public static string Run(PlayerMovement player)
    {
        if (player == null || !player.TryGetComponent(out PlayerBody body)) return "대상 플레이어의 몸을 찾지 못했습니다.";
        if (player.TryGetComponent(out LeanProbe running)) return "이미 재는 중입니다.";
        if (!player.IsGrounded || (player.CurrentState != PlayerMovement.MovementState.Standing && player.CurrentState != PlayerMovement.MovementState.Crouching))
            return "땅에 서 있거나 앉아 있을 때 쓰세요.";
        var probe = player.gameObject.AddComponent<LeanProbe>();
        probe.player = player;
        probe.body = body;
        return $"기울이기를 {Seconds + Settle:0.#}초 동안 저절로 끝까지 줬다 풉니다(오른쪽, 왼쪽). 그동안 움직이거나 시선을 돌리지 마세요. 결과는 Logs/{Log}.txt와 콘솔 로그에 한 줄로 나옵니다.";
    }

    private void Update()
    {
        if (player == null || body == null || player.Abilities == null) { Destroy(this); return; }
        time += Time.deltaTime;
        float along = Mathf.Clamp01((time - Settle) / Seconds);
        // Upright, right, upright, left, upright: a whole sine. The movement takes it up in its next Tick.
        player.Abilities.Lean.Probed = time < Settle ? 0f : Mathf.Sin(along * Mathf.PI * 2f);
        if (time >= Settle + Seconds)
        {
            Report();
            Destroy(this);
        }
    }

    // However the run ends (its time, the player gone), the lean goes back to the keys.
    private void OnDestroy()
    {
        if (player != null && player.Abilities != null) player.Abilities.Lean.Probed = null;
    }

    // The body has posed this frame's lean and planted the feet by now (this runs after it).
    private void LateUpdate()
    {
        if (body == null || player == null || player.Abilities == null || time < Settle) return;
        if (!body.TryReadFeet(out var left, out var right)) return;
        if (hips == null)
        {
            var animator = body.GetComponentInChildren<Animator>();
            hips = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
            eyes = PlayerView.EyesOf(player);
        }
        if (!started)
        {
            started = true;
            leftStart = left.Ankle;
            rightStart = right.Ankle;
            hipsStart = hips != null ? hips.position : Vector3.zero;
            eyesStart = eyes != null ? eyes.position : Vector3.zero;
            body.TryReadKeptFeet(out leftKept, out rightKept, out _, out _);
            return;
        }
        leanMost = Mathf.Max(leanMost, Mathf.Abs(player.Abilities.Lean.Share));
        leftMost = Mathf.Max(leftMost, Level(left.Ankle - leftStart));
        rightMost = Mathf.Max(rightMost, Level(right.Ankle - rightStart));
        leftRise = Mathf.Max(leftRise, Mathf.Abs(left.Ankle.y - leftStart.y));
        rightRise = Mathf.Max(rightRise, Mathf.Abs(right.Ankle.y - rightStart.y));
        if (hips != null)
        {
            hipsMost = Mathf.Max(hipsMost, Mathf.Abs(Vector3.Dot(hips.position - hipsStart, transform.right)));
            hipsDown = Mathf.Max(hipsDown, hipsStart.y - hips.position.y);
        }
        if (eyes != null) eyesMost = Mathf.Max(eyesMost, Mathf.Abs(Vector3.Dot(eyes.position - eyesStart, transform.right)));
        if (body.TryReadKeptFeet(out bool l, out bool r, out _, out _) && (l != leftKept || r != rightKept)) lostKeep = true;
    }

    private static float Level(Vector3 way) => new Vector2(way.x, way.z).magnitude;

    private void Report()
    {
        var weapons = player.GetComponent<WeaponController>();
        bool gun = weapons != null && weapons.Runtime != null && weapons.Runtime.Data != null && weapons.Runtime.Data.Category == WeaponCategory.Ranged;
        string hands = gun ? "gun" : player.MeleeCombat != null && player.MeleeCombat.IsActive ? "fists" : "nothing";
        var animator = body.GetComponentInChildren<Animator>();
        string model = animator != null ? animator.gameObject.name : "?";
        string line = $"body {model}, hands {hands}, {player.CurrentState}: feet L {leftMost * 100f:0.0} cm (up/down {leftRise * 100f:0.0}) R {rightMost * 100f:0.0} cm (up/down {rightRise * 100f:0.0})"
            + $" (kept at start L {leftKept} R {rightKept}{(lostKeep ? ", a foot let go or took a place during the run" : "")});"
            + $" hips {hipsMost * 100f:0.0} cm, hips down {hipsDown * 100f:0.0} cm, eyes {eyesMost * 100f:0.0} cm, lean reached {leanMost:0.00}"
            + (started ? "" : " -- NOTHING MEASURED (the feet were not on the ground)");
        PlayRecord.Line(Log, Heading, line);
        Debug.Log("[/lean] " + line, player);
    }
}
