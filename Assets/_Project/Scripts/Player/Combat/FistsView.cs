using UnityEngine;

/// <summary>
/// Where the fists go while fists are out: a guard, a charged wind-up, alternating punches and the counter's back elbow, posed under the
/// camera. Nothing is drawn here; the body's own hands follow these points (PlayerBodyHands). Each fist's forward is
/// where the knuckles point and its up is the back of the hand.
/// </summary>
[DisallowMultipleComponent]
public sealed class FistsView : MonoBehaviour
{
    private PlayerMeleeCombat melee;
    private PlayerMovement player;
    private Transform root, left, right;
    private float leftPunchTime, rightPunchTime, guardPose;
    // Each hand's punch under way: how long it takes and how far it reaches (a combo strike's are its own).
    private float leftPunchLength = PunchDuration, rightPunchLength = PunchDuration, leftReach = PunchReach, rightReach = PunchReach;
    private float deflectElapsed, deflectSeconds, deflectAttack, deflectHold;
    private Vector3 deflectSweep;
    private bool nextRight = true, deflectLeftHand, elbowRight;
    private float elbowElapsed = -1f;
    private const float PunchDuration = .18f, PunchReach = .25f;
    // The riposte's straight: quicker out and further, the shoulder behind it.
    private const float StraightDuration = .16f, StraightReach = .33f;
    // The spinning backfist (a link or a chase): wound across the chest, whipped out past the shoulder knuckles first,
    // brought back. Game seconds, as a punch's; its parts are shares of the whole.
    private const float SpinSeconds = .26f, SpinWind = .22f, SpinWhip = .3f;
    // How far the view whips round with it (degrees: yaw against the swing, roll with it), out, held and back (real s).
    private const float SpinYaw = 14f, SpinRoll = 5f, SpinOut = .05f, SpinHold = .03f, SpinBack = .16f;
    private float spinElapsed = -1f;
    private bool spinRight;
    // The back elbow: driven back, held at the contact, brought round again (real seconds).
    private const float ElbowDrive = .07f, ElbowHold = .08f, ElbowBack = .17f;
    // The guard goes up at once; it comes down over this long.
    private const float GuardLowerTime = .14f;
    // The sweep shoots past its pose and settles into it during the hold.
    private const float DeflectOvershoot = 1.08f;

    public Transform LeftFist => left;
    public Transform RightFist => right;
    public bool IsActive => root != null && melee != null && melee.IsActive;
    /// <summary>
    /// A strike is under way: a punch, the counter's dash-in with the next fist drawn all the way back, or its back elbow.
    /// The body's hands come up for it at once, running or not (PlayerBodyHands): the elbow has no arm clip, so without
    /// them it would be posed here and never seen.
    /// </summary>
    public bool IsPunching => leftPunchTime > 0f || rightPunchTime > 0f || elbowElapsed >= 0f || spinElapsed >= 0f || Countering;
    private bool Countering => player != null && player.PerfectDefense != null && player.PerfectDefense.IsCountering;
    private bool IsDeflecting => deflectElapsed < deflectSeconds;
    /// <summary>A punch left: with the right hand or not, and how charged it was (0-1).</summary>
    public event System.Action<bool, float> Thrown;

    private void OnEnable()
    {
        if (!Application.isPlaying) return;
        melee = GetComponent<PlayerMeleeCombat>();
        player = GetComponentInParent<PlayerMovement>();
        var camera = player != null ? player.GetComponentInChildren<Camera>() : null;
        if (melee == null || camera == null) return;
        melee.Punched -= Punch; melee.Punched += Punch;
        melee.Elbowed -= Elbow; melee.Elbowed += Elbow;
        if (root != null) return;
        root = new GameObject("Fists").transform;
        // On the eyes, not the camera: the F5 view moves the camera behind the player.
        root.SetParent(PlayerView.EyesOf(player), false);
        left = new GameObject("Left fist").transform; left.SetParent(root, false);
        right = new GameObject("Right fist").transform; right.SetParent(root, false);
    }
    private void Punch(float charge)
    {
        var strike = melee != null ? melee.LastStrike : ComboStrike.None;
        // A riposte goes from the hand that didn't sweep the blow aside; the other is still holding the deflect.
        bool right = strike == ComboStrike.Riposte && IsDeflecting ? deflectLeftHand : nextRight;
        if (strike == ComboStrike.Spin || strike == ComboStrike.Chase)
        {
            spinRight = right; spinElapsed = 0f;
            leftPunchTime = rightPunchTime = 0f;
            nextRight = !right;
            // The view whips round against the swing as the body turns into it, and settles.
            float across = right ? 1f : -1f;
            CombatHeadMotion.For(this)?.Move(Vector3.zero, new Vector3(0f, -across * SpinYaw, across * SpinRoll), SpinOut, SpinHold, SpinBack);
            Thrown?.Invoke(right, charge);
            return;
        }
        bool straight = strike == ComboStrike.Riposte;
        float length = straight ? StraightDuration : PunchDuration, reach = straight ? StraightReach : PunchReach;
        if (right) { rightPunchTime = rightPunchLength = length; rightReach = reach; }
        else { leftPunchTime = leftPunchLength = length; leftReach = reach; }
        nextRight = !right;
        Thrown?.Invoke(right, charge);
    }
    // The fist on the attacker's side pulls back beside the chest, past the eyes, so the arm drives its elbow behind.
    private void Elbow(int side) { elbowRight = side >= 0; elbowElapsed = 0f; leftPunchTime = rightPunchTime = 0f; spinElapsed = -1f; }
    // The backfist: wound across to the far side of the chest, back of the hand turned out; whipped out past its own
    // shoulder, knuckles leading; then back to the guard. The other fist tucks in to the chin meanwhile.
    private void ApplySpin()
    {
        if (spinElapsed < 0f) return;
        float t = spinElapsed / SpinSeconds;
        if (t >= 1f) { spinElapsed = -1f; return; }
        float weight = t < SpinWind ? EaseOut(t / SpinWind) : t < SpinWind + SpinWhip ? 1f
            : 1f - EaseInOut((t - SpinWind - SpinWhip) / (1f - SpinWind - SpinWhip));
        float swing = t < SpinWind ? 0f : EaseOut(Mathf.Clamp01((t - SpinWind) / SpinWhip));
        Transform hand = spinRight ? right : left, other = spinRight ? left : right;
        float across = spinRight ? 1f : -1f;
        Vector3 wound = new Vector3(-across * .12f, -.1f, .22f), whipped = new Vector3(across * .31f, -.03f, .36f);
        hand.localPosition = Vector3.Lerp(hand.localPosition, Vector3.Lerp(wound, whipped, swing), weight);
        Quaternion pose = Quaternion.Euler(0f, across * Mathf.Lerp(70f, -30f, swing), -across * 85f);
        hand.localRotation = Quaternion.Slerp(hand.localRotation, pose, weight);
        other.localPosition += new Vector3(across * .04f, .03f, -.05f) * weight;
    }
    private static float EaseOut(float x) { x = Mathf.Clamp01(x); return 1f - (1f - x) * (1f - x) * (1f - x); }
    private static float EaseInOut(float x) { x = Mathf.Clamp01(x); return x < .5f ? 4f * x * x * x : 1f - Mathf.Pow(-2f * x + 2f, 3f) * .5f; }
    private float ElbowAmount()
    {
        if (elbowElapsed < 0f) return 0f;
        if (elbowElapsed < ElbowDrive) { float rest = 1f - elbowElapsed / ElbowDrive; return 1f - rest * rest * rest; }
        if (elbowElapsed < ElbowDrive + ElbowHold) return 1f;
        return 1f - Mathf.SmoothStep(0f, 1f, (elbowElapsed - ElbowDrive - ElbowHold) / ElbowBack);
    }
    private void ApplyElbow()
    {
        float amount = ElbowAmount();
        if (amount <= 0f) return;
        Transform driving = elbowRight ? right : left, other = elbowRight ? left : right;
        float across = elbowRight ? 1f : -1f;
        driving.localPosition = Vector3.Lerp(driving.localPosition, new Vector3(across * .26f, -.16f, -.04f), amount);
        driving.localRotation *= Quaternion.Euler(-70f * amount, across * 25f * amount, 0f);
        other.localPosition += new Vector3(across * .03f, .02f, -.03f) * amount;
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
    private static float PunchExtension(float remaining, float length)
    {
        if (remaining <= 0f || length <= 0f) return 0f;
        float elapsed = length - remaining, snap = Mathf.Min(.04f, length * .3f);
        return elapsed < snap ? Mathf.SmoothStep(0f, 1f, elapsed / snap)
            : Mathf.SmoothStep(1f, 0f, (elapsed - snap) / (length - snap));
    }
    // Within arm's reach of the body's shoulders; a full punch straightens the arm.
    private void LateUpdate()
    {
        if (root == null || melee == null) return;
        if (!melee.IsActive) { leftPunchTime = rightPunchTime = guardPose = 0f; deflectElapsed = deflectSeconds; elbowElapsed = spinElapsed = -1f; nextRight = true; return; }
        if (elbowElapsed >= 0f && Time.timeScale > 0f)
        {
            elbowElapsed += Time.unscaledDeltaTime;
            if (elbowElapsed > ElbowDrive + ElbowHold + ElbowBack) elbowElapsed = -1f;
        }
        float dt = player != null ? player.DeltaTime : Time.deltaTime;
        leftPunchTime = Mathf.Max(0f, leftPunchTime - dt);
        rightPunchTime = Mathf.Max(0f, rightPunchTime - dt);
        if (spinElapsed >= 0f) spinElapsed += dt;
        if (Time.timeScale > 0f) deflectElapsed = Mathf.Min(deflectElapsed + Time.unscaledDeltaTime, deflectSeconds);
        float leftPunch = PunchExtension(leftPunchTime, leftPunchLength), rightPunch = PunchExtension(rightPunchTime, rightPunchLength);
        // Dashing in to counter, the next fist is drawn all the way back.
        float charge = Countering ? 1f : melee.ChargeRatio;
        float leftCharge = nextRight ? 0f : charge, rightCharge = nextRight ? charge : 0f;
        bool guarding = melee.IsGuarding || melee.IsParryActive || melee.IsGuardBreaking || IsDeflecting;
        guardPose = guarding ? 1f : Mathf.MoveTowards(guardPose, 0f, Time.unscaledDeltaTime / GuardLowerTime);
        left.localPosition = Vector3.Lerp(new Vector3(-.2f, -.2f, .32f - leftCharge * .1f), new Vector3(-.1f, -.06f, .26f), guardPose)
            + new Vector3(leftPunch * .08f, leftPunch * .06f, leftPunch * leftReach);
        right.localPosition = Vector3.Lerp(new Vector3(.2f, -.2f, .32f - rightCharge * .1f), new Vector3(.1f, -.06f, .26f), guardPose)
            + new Vector3(-rightPunch * .08f, rightPunch * .06f, rightPunch * rightReach);
        right.localRotation = Quaternion.Euler(-rightCharge * 15f, -rightPunch * 20f, -10f);
        left.localRotation = Quaternion.Euler(-leftCharge * 15f, leftPunch * 20f, 10f);
        ApplyDeflect();
        ApplyElbow();
        ApplySpin();
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
        if (melee != null) { melee.Punched -= Punch; melee.Elbowed -= Elbow; }
        if (root != null) Destroy(root.gameObject);
    }
    private void OnDisable()
    {
        if (melee != null) { melee.Punched -= Punch; melee.Elbowed -= Elbow; }
    }
}
