using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The dead fall as a ragdoll, carrying the speed they had and thrown the way the last blow came from. The corpse is
/// made as soon as the body exists and waits unseen, so a death in a fight doesn't hitch. Their own PC runs the physics
/// and the others follow it (PlayerNetworkCorpse), so every PC shows the same body. The real body goes on animating
/// unseen and takes over again when they come back. This PC's own player watches through the corpse's eyes, which is
/// why this runs after PlayerBody has placed the camera. Numbers: Tables/Ragdoll.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(1000)]
public sealed class PlayerRagdoll : MonoBehaviour
{
    public const string TableName = "Ragdoll", TableRow = "Default";
    private const string Enemies = "Enemy";
    // A blow this recent is the one that killed them. Above the head, the camera's sweep starts clear of what it lies on.
    private const float KillingBlowAge = .3f, SweepHeight = .3f, WalkerReach = .45f;

    [SerializeField, Min(0f), Tooltip("Momentum a hit gives the corpse per point of damage (N·s).")]
    private float pushPerDamage = 1f;
    [SerializeField, Min(0f), Tooltip("Momentum of even the weakest hit (N·s).")]
    private float leastPush = 8f;
    [SerializeField, Min(0f), Tooltip("Speed a hit can add to one body part (m/s).")]
    private float mostPushSpeed = 5f;
    [SerializeField, Min(0f), Tooltip("Momentum the killing blow gives the upper body (N·s).")]
    private float deathPush = 45f;

    private readonly List<Renderer> hidden = new List<Renderer>();
    private PlayerMovement player;
    private PlayerBody body;
    private PlayerDamageReceiver receiver;
    private CharacterController capsule;
    private RagdollCorpse corpse;
    private Animator preparedFor;
    private Transform view;
    private Vector3 eye, carried;
    private Quaternion gaze, viewHome;
    private LayerMask solid;
    private DamageHit lastHit;
    private float lastHitTime = float.NegativeInfinity, cameraRadius;
    private bool fell;

    /// <summary>The corpse on the ground on this PC, or null while they live.</summary>
    public RagdollCorpse Corpse => fell ? corpse : null;

    private void Awake()
    {
        player = GetComponent<PlayerMovement>();
        body = GetComponent<PlayerBody>();
        capsule = GetComponent<CharacterController>();
    }

    private void Start()
    {
        receiver = player.DamageReceiver;
        receiver.HitResolved += OnHit;
    }

    private void OnDestroy()
    {
        if (receiver != null) receiver.HitResolved -= OnHit;
        if (corpse != null) Destroy(corpse.gameObject);
    }

    private void OnHit(DamageHit hit, DamageResult result)
    {
        if (result != DamageResult.Damaged) return;
        lastHit = hit;
        lastHitTime = Time.time;
    }

    private void FixedUpdate()
    {
        if (!fell || corpse == null || player.IsNetworkDriven) return;
        foreach (var other in PlayerRoster.All)
            if (other != player && other.IsInitialized && !other.IsDead)
                corpse.LeanOn(other.transform.position, other.ActualVelocity, WalkerReach, player.AbilitySettings.propPushForce);
    }

    private void LateUpdate()
    {
        if (!player.IsInitialized) return;
        if (!player.IsDead)
        {
            if (fell) Clear();
            Prepare();
            carried = player.ActualVelocity;
            return;
        }
        if (!fell) Fall();
        if (corpse == null) return;
        // Every frame: a weapon or skin that changes while they are dead brings new renderers.
        GetComponentsInChildren(true, hidden);
        foreach (var part in hidden) part.forceRenderingOff = true;
        if (view != null) RideTheEyes();
    }

    // At the start of the map, and again when a skin swaps the body. A corpse can't outlive the model it copies.
    private void Prepare()
    {
        var animator = body != null ? body.BodyAnimator : null;
        if (animator == null || (animator == preparedFor && corpse != null)) return;
        if (corpse != null) Destroy(corpse.gameObject);
        preparedFor = animator;
        solid = player.groundLayers & ~LayerMask.GetMask(Enemies);
        corpse = RagdollCorpse.Prepare(animator, gameObject.layer, solid);
        if (corpse != null) corpse.Struck += OnCorpseStruck;
    }

    private void Fall()
    {
        fell = true;
        Prepare();
        if (corpse == null) return;
        corpse.Lay(!player.IsNetworkDriven);
        foreach (var someone in PlayerRoster.All)
            if (someone.TryGetComponent(out CharacterController other)) corpse.PassThrough(other);
        // The capsule stays behind unseen, and nobody should bump into it.
        capsule.detectCollisions = false;
        if (player.IsNetworkDriven) return;
        corpse.Launch(carried);
        if (Time.time - lastHitTime < KillingBlowAge && lastHit.Direction.sqrMagnitude > .0001f)
            corpse.Knock(lastHit.Direction.normalized * deathPush);
        if (!player.IsLocal || body.IsThirdPerson || body.View == null) return;
        view = body.View;
        var head = preparedFor.GetBoneTransform(HumanBodyBones.Head);
        eye = head.InverseTransformPoint(view.position);
        gaze = Quaternion.Inverse(head.rotation) * view.rotation;
        viewHome = view.localRotation;
        cameraRadius = body.CameraRadius();
    }

    // Glued to the eye, but swept in from above it so the near plane never reaches through the floor or a wall.
    private void RideTheEyes()
    {
        Vector3 wanted = corpse.Head.TransformPoint(eye);
        Vector3 start = corpse.Head.position + Vector3.up * (cameraRadius + SweepHeight), path = wanted - start;
        float reach = path.magnitude;
        Vector3 place = Physics.SphereCast(start, cameraRadius, path / reach, out RaycastHit hit, reach, solid, QueryTriggerInteraction.Ignore)
            ? start + path / reach * hit.distance : wanted;
        view.SetPositionAndRotation(place, corpse.Head.rotation * gaze);
    }

    private void OnCorpseStruck(DamageHit hit)
    {
        if (hit.Direction.sqrMagnitude < .0001f) return;
        ImpactVfx.Blood(hit.Point, hit.Direction, false, false, hit.Method == DamageMethod.Bullet, corpse);
        Vector3 impulse = hit.Direction.normalized * Mathf.Max(leastPush, hit.Amount * pushPerDamage);
        if (!player.IsNetworkDriven) TakePush(hit.Point, impulse);
        else if (TryGetComponent(out PlayerNetworkCorpse network)) network.Push(hit.Point, impulse);
    }

    /// <summary>A hit on this corpse, here or on another PC.</summary>
    internal void TakePush(Vector3 point, Vector3 impulse)
    {
        if (fell && corpse != null) corpse.Push(point, impulse, mostPushSpeed);
    }

    private void Clear()
    {
        if (!fell) return;
        fell = false;
        foreach (var part in hidden) if (part != null) part.forceRenderingOff = false;
        hidden.Clear();
        if (corpse == null) return;
        capsule.detectCollisions = true;
        if (view != null) view.localRotation = viewHome;
        view = null;
        corpse.Stow();
    }

    private void OnDisable() => Clear();
}
