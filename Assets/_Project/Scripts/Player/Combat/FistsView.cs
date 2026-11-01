using UnityEngine;

/// <summary>
/// Where the fists go while fists are out: a guard, a charged wind-up and alternating punches, posed under the
/// camera. Nothing is drawn here; the body's own hands follow these points (PlayerBodyHands). Each fist's forward is
/// where the knuckles point and its up is the back of the hand.
/// </summary>
[DisallowMultipleComponent]
public sealed class FistsView : MonoBehaviour
{
    private PlayerMeleeCombat melee;
    private Transform root, left, right;
    private float leftPunchTime, rightPunchTime, guardPose;
    private float deflectElapsed, deflectSeconds, deflectAttack, deflectHold;
    private Vector3 deflectSweep;
    private bool nextRight = true, deflectLeftHand;
    private const float PunchDuration = .18f;
    // The guard goes up at once; it comes down over this long.
    private const float GuardLowerTime = .14f;
    // The sweep shoots past its pose and settles into it during the hold.
    private const float DeflectOvershoot = 1.08f;

    public Transform LeftFist => left;
    public Transform RightFist => right;
    public bool IsActive => root != null && melee != null && melee.IsActive;
    public bool IsPunching => leftPunchTime > 0f || rightPunchTime > 0f;
    private bool IsDeflecting => deflectElapsed < deflectSeconds;
    /// <summary>A punch left: with the right hand or not, and how charged it was (0-1).</summary>
    public event System.Action<bool, float> Thrown;

    private void OnEnable()
    {
        if (!Application.isPlaying) return;
        melee = GetComponent<PlayerMeleeCombat>();
        var camera = GetComponentInParent<PlayerMovement>()?.GetComponentInChildren<Camera>();
        if (melee == null || camera == null) return;
        melee.Punched -= Punch; melee.Punched += Punch;
        if (root != null) return;
        root = new GameObject("Fists").transform;
        root.SetParent(camera.transform, false);
        left = new GameObject("Left fist").transform; left.SetParent(root, false);
        right = new GameObject("Right fist").transform; right.SetParent(root, false);
    }
    private void Punch(float charge)
    {
        bool right = nextRight;
        if (right) rightPunchTime = PunchDuration;
        else leftPunchTime = PunchDuration;
        nextRight = !nextRight;
        Thrown?.Invoke(right, charge);
    }
    /// <summary>
    /// A parry: the far hand snaps across the view to the attacker's side, holds the pose and lets go. side is the
    /// attacker's offset across the view (+ right); almost dead ahead the hands take turns. Runs on real time, so the hand
    /// reaches its pose inside the hit stop.
    /// </summary>
    public void Deflect(float side, float seconds, float attack, float hold, Vector3 sweep)
    {
        bool fromRight = Mathf.Abs(side) > .08f ? side > 0f : nextRight;
        deflectLeftHand = fromRight;
        deflectSweep = new Vector3(fromRight ? sweep.x : -sweep.x, sweep.y, sweep.z);
        deflectAttack = Mathf.Min(attack, seconds * .4f); deflectHold = Mathf.Min(hold, seconds * .4f);
        deflectSeconds = seconds; deflectElapsed = 0f;
        nextRight = !nextRight;
    }
    private float DeflectAmount()
    {
        if (!IsDeflecting) return 0f;
        if (deflectElapsed < deflectAttack)
        {
            float rest = 1f - deflectElapsed / deflectAttack;
            return DeflectOvershoot * (1f - rest * rest * rest);
        }
        float held = deflectElapsed - deflectAttack;
        if (held < deflectHold) return Mathf.Lerp(DeflectOvershoot, 1f, held / deflectHold);
        return Mathf.Pow(1f - (held - deflectHold) / (deflectSeconds - deflectAttack - deflectHold), 2.4f);
    }
    private static float PunchExtension(float remaining)
    {
        if (remaining <= 0f) return 0f;
        float elapsed = PunchDuration - remaining;
        return elapsed < .04f ? Mathf.SmoothStep(0f, 1f, elapsed / .04f)
            : Mathf.SmoothStep(1f, 0f, (elapsed - .04f) / (PunchDuration - .04f));
    }
    // Within arm's reach of the body's shoulders; a full punch straightens the arm.
    private void LateUpdate()
    {
        if (root == null || melee == null) return;
        if (!melee.IsActive) { leftPunchTime = rightPunchTime = guardPose = 0f; deflectElapsed = deflectSeconds; nextRight = true; return; }
        leftPunchTime = Mathf.Max(0f, leftPunchTime - Time.deltaTime);
        rightPunchTime = Mathf.Max(0f, rightPunchTime - Time.deltaTime);
        if (Time.timeScale > 0f) deflectElapsed = Mathf.Min(deflectElapsed + Time.unscaledDeltaTime, deflectSeconds);
        float leftPunch = PunchExtension(leftPunchTime), rightPunch = PunchExtension(rightPunchTime);
        float leftCharge = nextRight ? 0f : melee.ChargeRatio, rightCharge = nextRight ? melee.ChargeRatio : 0f;
        bool guarding = melee.IsGuarding || melee.IsParryActive || melee.IsGuardBreaking || IsDeflecting;
        guardPose = guarding ? 1f : Mathf.MoveTowards(guardPose, 0f, Time.unscaledDeltaTime / GuardLowerTime);
        left.localPosition = Vector3.Lerp(new Vector3(-.2f, -.2f, .32f - leftCharge * .1f), new Vector3(-.1f, -.06f, .26f), guardPose)
            + new Vector3(leftPunch * .08f, leftPunch * .06f, leftPunch * .25f);
        right.localPosition = Vector3.Lerp(new Vector3(.2f, -.2f, .32f - rightCharge * .1f), new Vector3(.1f, -.06f, .26f), guardPose)
            + new Vector3(-rightPunch * .08f, rightPunch * .06f, rightPunch * .25f);
        right.localRotation = Quaternion.Euler(-rightCharge * 15f, -rightPunch * 20f, -10f);
        left.localRotation = Quaternion.Euler(-leftCharge * 15f, leftPunch * 20f, 10f);
        ApplyDeflect();
    }
    // The sweeping hand crosses the view, knuckles leading; the other pulls back to balance it.
    private void ApplyDeflect()
    {
        float amount = DeflectAmount();
        if (amount <= 0f) return;
        Transform sweeping = deflectLeftHand ? left : right, other = deflectLeftHand ? right : left;
        float across = deflectLeftHand ? 1f : -1f;
        sweeping.localPosition = Vector3.LerpUnclamped(sweeping.localPosition, deflectSweep, amount);
        sweeping.localRotation *= Quaternion.Euler(-25f * amount, across * 55f * amount, -across * 35f * amount);
        other.localPosition += new Vector3(-across * .04f, -.05f, -.05f) * amount;
    }
    private void OnDestroy()
    {
        if (melee != null) melee.Punched -= Punch;
        if (root != null) Destroy(root.gameObject);
    }
    private void OnDisable()
    {
        if (melee != null) melee.Punched -= Punch;
    }
}
