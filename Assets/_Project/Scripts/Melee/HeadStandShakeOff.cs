using UnityEngine;

/// <summary>
/// An enemy won't keep someone standing on its head: after a moment it lurches and throws them off to the side they
/// stand towards, with a little lift so the feet clear it. Before this the head was the safest place in a fight,
/// because nothing could reach that high. The enemy below never swings at its rider (EnemyCombatAI), while the ones
/// around reach up to them (Enemies.csv reachAbove). A stunned, staggered or held enemy can't shake anyone off. Runs on
/// the rider's own PC, as all of their movement does. Numbers: Tables/Player.csv, row HeadStand.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.HeadStandShakeOff)]
public sealed class HeadStandShakeOff : MonoBehaviour
{
    [SerializeField, Min(0f), Tooltip("Standing on a head this long (s) before it throws the rider off.")]
    private float shakeDelay = .35f;
    [SerializeField, Min(0f), Tooltip("How far the throw carries the rider to the side (m).")]
    private float throwDistance = 1.1f;
    [SerializeField, Min(.05f), Tooltip("Over this long (s).")]
    private float throwSeconds = .25f;
    [SerializeField, Min(0f), Tooltip("Upward speed at the start of the throw (m/s), so the feet clear the head.")]
    private float throwLift = 2.5f;
    [SerializeField, Range(0f, 45f), Tooltip("How far the enemy lurches as it throws (degrees).")]
    private float lurchDegrees = 14f;
    [SerializeField, Min(.05f), Tooltip("That lurch and its rocking back (s).")]
    private float lurchSeconds = .45f;
    [SerializeField, Tooltip("Jolt to the view as the head gives way (degrees; pitch;roll). The roll takes the throw's side.")]
    private Vector2 viewJolt = new Vector2(-5f, 7f);

    private PlayerMovement player;
    private MeleeCombatTarget under;
    private float standing, throwLeft;
    private Vector3 throwDirection;

    private void Awake() => player = GetComponent<PlayerMovement>();

    private void Update()
    {
        if (player == null || !player.IsInitialized || !player.IsLocal || player.IsNetworkDriven) return;
        float dt = player.DeltaTime;
        if (dt <= 0f) return;
        if (throwLeft > 0f) { Throw(dt); return; }
        var enemy = player.IsGrounded && player.DamageReceiver.IsAlive && !player.CombatInputLocked ? EnemyUnderFeet() : null;
        if (enemy != under) { under = enemy; standing = 0f; }
        if (under == null || under.IsStunned) { standing = 0f; return; }
        standing += dt;
        if (standing >= shakeDelay) BeginThrow();
    }

    private MeleeCombatTarget EnemyUnderFeet()
    {
        foreach (var target in MeleeCombatTarget.Active)
            if (target != null && target.IsAvailable && target.GetComponent<EnemyHealth>() != null && EnemyHead.Carries(target, player))
                return target;
        return null;
    }

    private void BeginThrow()
    {
        var body = EnemyHead.BodyOf(under);
        Vector3 center = body != null ? body.bounds.center : under.transform.position;
        Vector3 away = Vector3.ProjectOnPlane(transform.position - center, Vector3.up);
        // Dead centre there is no side to fall to: off the back, away from where they look.
        throwDirection = away.sqrMagnitude > .0025f ? away.normalized : -Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        throwLeft = throwSeconds;
        standing = 0f;
        player.VerticalVelocity = Mathf.Max(player.VerticalVelocity, throwLift);
        EnemyParryRecoil.For(under)?.Kick(throwDirection, lurchDegrees, lurchSeconds);
        var shove = GetComponent<PlayerShoveAudio>() ?? gameObject.AddComponent<PlayerShoveAudio>();
        shove.Play(ShoveImpactKind.Enemy);
        float side = Vector3.Dot(transform.right, throwDirection) < 0f ? 1f : -1f;
        GetComponentInChildren<PlayerCameraEffects>()?.ApplyCameraImpulse(new Vector3(viewJolt.x, 0f, viewJolt.y * side));
        under = null;
    }

    // The same eased path as a micro dodge's sidestep: fast off the head, settling by the end.
    private void Throw(float dt)
    {
        float before = 1f - throwLeft / throwSeconds;
        throwLeft = Mathf.Max(0f, throwLeft - dt);
        float after = 1f - throwLeft / throwSeconds;
        float travel = EaseOut(after) - EaseOut(before);
        player.Motor.AddDisplacement(throwDirection * (throwDistance * travel));
    }

    private static float EaseOut(float t) => 1f - (1f - t) * (1f - t);

    private void OnDisable() { under = null; standing = throwLeft = 0f; }
}
