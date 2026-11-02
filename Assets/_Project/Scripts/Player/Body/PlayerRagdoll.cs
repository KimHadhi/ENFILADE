using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The dead fall as a ragdoll, carrying the speed they had and thrown the way the last blow came from. What falls is the
/// real body, so its face and cloth go on as they were: it goes limp and takes each frame's pose from a doll, which is
/// made as soon as the body exists and waits switched off, so a death in a fight doesn't hitch. Their own PC runs the
/// physics and the others follow it (PlayerNetworkCorpse), so every PC shows the same body. This PC's own player watches
/// through its eyes, which is why this runs after PlayerBody has placed the camera. Numbers: Tables/Ragdoll.
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
    private int partsSeen;
    private bool fell;

    /// <summary>The corpse on the ground on this PC, or null while they live.</summary>
    public RagdollCorpse Corpse => fell ? corpse : null;
    private Animator Model => body != null ? body.BodyAnimator : null;

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
            if (fell) { Clear(); PassThroughTheFallen(); }
            Prepare();
            carried = player.ActualVelocity;
            return;
        }
        // A skin that changes while they are dead brings a new body, which falls from where it stands.
        if (!fell || (Model != null && Model != preparedFor)) Fall();
        if (corpse == null || Model == null) return;
        corpse.PoseBody();
        HideAllButBody();
        if (view != null) RideTheEyes();
    }

    // A spawn switches the capsule off and on, which forgets the corpses it walked through.
    private void PassThroughTheFallen()
    {
        foreach (var someone in PlayerRoster.All)
            if (someone.TryGetComponent(out PlayerRagdoll other) && other.Corpse != null) other.Corpse.PassThrough(capsule);
    }

    // Weapons and the like stay out of sight, and one that turns up while they are dead changes the hierarchy's size.
    private void HideAllButBody()
    {
        if (transform.hierarchyCount == partsSeen) return;
        partsSeen = transform.hierarchyCount;
        GetComponentsInChildren(true, hidden);
        hidden.RemoveAll(part => part.transform.IsChildOf(preparedFor.transform));
        foreach (var part in hidden) part.forceRenderingOff = true;
    }

    // At the start of the map, and again when a skin swaps the body. A doll can't outlive the model it was made for.
    private void Prepare()
    {
        var animator = Model;
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
        body.GoLimp();
        partsSeen = 0;
        foreach (var someone in PlayerRoster.All)
            if (someone.TryGetComponent(out CharacterController other) && other.enabled) corpse.PassThrough(other);
        // The capsule stays behind unseen, and nobody should bump into it. The flag doesn't stop a walking character, so
        // it goes off altogether, except on their own PC, where only they could walk into it and their movement needs it.
        capsule.detectCollisions = false;
        capsule.enabled = player.IsLocal;
        if (player.IsNetworkDriven) return;
        corpse.Launch(carried);
        if (Time.time - lastHitTime < KillingBlowAge && lastHit.Direction.sqrMagnitude > .0001f)
            corpse.Knock(lastHit.Direction.normalized * deathPush);
        if (!player.IsLocal || view != null || body.IsThirdPerson || body.View == null) return;
        view = body.View;
        var head = corpse.Head;
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
        capsule.enabled = capsule.detectCollisions = true;
        if (corpse == null) return;
        if (view != null) view.localRotation = viewHome;
        view = null;
        corpse.Stow();
    }

    private void OnDisable() => Clear();
}
