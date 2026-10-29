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
    private float leftPunchTime, rightPunchTime;
    private bool nextRight = true;
    private const float PunchDuration = .18f;

    public Transform LeftFist => left;
    public Transform RightFist => right;
    public bool IsActive => root != null && melee != null && melee.IsActive;
    public bool IsPunching => leftPunchTime > 0f || rightPunchTime > 0f;

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
        if (nextRight) rightPunchTime = PunchDuration;
        else leftPunchTime = PunchDuration;
        nextRight = !nextRight;
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
        if (!melee.IsActive) { leftPunchTime = rightPunchTime = 0f; nextRight = true; return; }
        leftPunchTime = Mathf.Max(0f, leftPunchTime - Time.deltaTime);
        rightPunchTime = Mathf.Max(0f, rightPunchTime - Time.deltaTime);
        float leftPunch = PunchExtension(leftPunchTime), rightPunch = PunchExtension(rightPunchTime);
        float leftCharge = nextRight ? 0f : melee.ChargeRatio, rightCharge = nextRight ? melee.ChargeRatio : 0f;
        float guard = melee.IsGuarding || melee.IsParryActive || melee.IsGuardBreaking ? 1f : 0f;
        left.localPosition = Vector3.Lerp(new Vector3(-.2f, -.2f, .32f - leftCharge * .1f), new Vector3(-.1f, -.06f, .26f), guard)
            + new Vector3(leftPunch * .08f, leftPunch * .06f, leftPunch * .25f);
        right.localPosition = Vector3.Lerp(new Vector3(.2f, -.2f, .32f - rightCharge * .1f), new Vector3(.1f, -.06f, .26f), guard)
            + new Vector3(-rightPunch * .08f, rightPunch * .06f, rightPunch * .25f);
        right.localRotation = Quaternion.Euler(-rightCharge * 15f, -rightPunch * 20f, -10f);
        left.localRotation = Quaternion.Euler(-leftCharge * 15f, leftPunch * 20f, 10f);
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
