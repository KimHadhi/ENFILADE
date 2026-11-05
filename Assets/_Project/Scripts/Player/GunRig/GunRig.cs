using System.Collections.Generic;
using KINEMATION.CharacterAnimationSystem.Integrations.FPSAnimationPack.Scripts;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables;
using KINEMATION.FPSAnimationPack.Scripts.Sounds;
using KINEMATION.FPSAnimationPack.Scripts.Weapon;
using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Holds the gun the KINEMATION way. KINEMATION's own arms rig (the FPS Animation Pack x CAS integration's
/// SK_Arms_Mono_Skeleton, its settings untouched) runs hidden beside the player with the KINEMATION gun in its hands,
/// fed with our movement, look and aim, and is moved every frame so its camera socket sits on our eyes. That gun is the
/// gun we see. The body's own arms reach for the rig's hands and its fingers bend like the rig's (PlayerBodyHands asks
/// HandTarget and FollowFingers), and WeaponController's gun root and muzzle ride the gun.
/// WeaponController keeps the rules (ammo, fire rate, aim blend, cover); this plays KINEMATION's aim, sway, recoil,
/// reload and equip motions on its events. Reload clips play at the speed that fits our reload time.
/// The rig may sit a little closer to the body than KINEMATION's arms hold it, so a shorter body's arms still reach:
/// the gun moves toward where this body's arms would hold it at KINEMATION's arm bend, less while aiming.
/// A weapon without a KINEMATION gun (GunRigAssets) keeps the procedural placeholder pose (WeaponPresentation).
/// Numbers come from Tables/GunRig.csv, row Rig.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(295)]
public sealed class GunRig : MonoBehaviour, IWeaponPose
{
    public const string TableName = "GunRig";
    private const string TableRow = "Rig";

    [SerializeField, Tooltip("Hold guns with KINEMATION's arms rig. Off keeps the procedural placeholder pose.")]
    private bool useRig = true;
    [SerializeField, Range(0f, 1f), Tooltip("How far the gun moves from where KINEMATION's arms hold it toward where this body's arms would (0 stays exactly at KINEMATION's spot).")]
    private float bodyFit = 1f;
    [SerializeField, Min(0f), Tooltip("The farthest that move goes (m).")]
    private float fitLimit = .08f;
    [SerializeField, Min(.01f), Tooltip("How quickly the gun settles into that move: the time to cover half the way (s).")]
    private float fitHalfLife = .25f;
    [SerializeField, Min(.001f), Tooltip("How deep the gun's front face is taken when the muzzle is found from the gun's shape (m).")]
    private float muzzleDepth = .015f;

    // KINEMATION's example controller: still below this speed (movingVelocityThreshold), in the air once off the ground this
    // long (fallDelay), and how quickly its animator's move and gait follow (animatorMoveInterpSpeed, animGaitSmoothing).
    private const float StillSpeed = .1f, FallDelay = .3f, MoveSmoothing = 7f, GaitSmoothing = 12f;
    // Shots further apart than this many fire intervals have let go of the trigger: KINEMATION's recoil then settles.
    private const float TriggerReleaseIntervals = 1.5f;

    private static readonly int MoveX = Animator.StringToHash("MoveX"), MoveY = Animator.StringToHash("MoveY"),
        GaitId = Animator.StringToHash("Gait"), ViewWeightId = Animator.StringToHash("ViewWeight"),
        AimingWeightId = Animator.StringToHash("AimingWeight"), IsFirstPersonId = Animator.StringToHash("IsFirstPerson"),
        IsInAirId = Animator.StringToHash("IsInAir"), IsMovingId = Animator.StringToHash("IsMoving"),
        CrouchId = Animator.StringToHash("Crouch"), JumpedId = Animator.StringToHash("Jumped");
    private static readonly Dictionary<(AnimationAsset, int), AnimationAsset> RatedClips = new();

    private sealed class HeldGun
    {
        public WeaponData Data;
        public GameObject Instance;
        public CasFPSWeapon Weapon;
        public SimpleAnimationPlayer Player;
        public Vector3 MuzzlePosition;
        public Quaternion MuzzleRotation;
    }

    private WeaponController controller;
    private PlayerMovement movement;
    private PlayerBody body;
    private GunRigAssets assets;
    private Transform weaponRoot, muzzle, eyes, lookFrame;
    private Vector3 muzzleHomePosition;
    private Quaternion muzzleHomeRotation;
    private WeaponPresentation placeholder;
    private readonly List<Renderer> placeholderModel = new();
    private readonly List<HeldGun> guns = new();
    private readonly HashSet<int> parameters = new();

    private GameObject armsRig;
    private Animator rigAnimator;
    private CharacterAnimationComponent characterAnimation;
    private ProceduralAnimationComponent procedural;
    private RecoilAnimation recoil;
    private GunRigInputs inputs;
    private Transform socket;
    private GunRigArm rigLeft, rigRight;
    private GunRigFingers fingersLeft, fingersRight;

    private HeldGun held;
    private WeaponData shownData;
    private bool shown = true, highReady, firing, wasMoving, reloadShown, hasLook;
    private float lastShot, shotInterval, lastAim, ungrounded, animGait, lastYaw, lastPitch;
    private Vector2 animMove;
    private Vector3 fit;
    private FPSAnimationPair reloading;

    /// <summary>
    /// Puts the rig on <paramref name="weapons"/>' player and returns it as the gun's pose, or null when the rig is off
    /// (GunRig.csv), its assets are missing, or KINEMATION's rig lacks the bones it needs.
    /// </summary>
    internal static IWeaponPose Attach(WeaponController weapons, Transform weaponRoot, Transform muzzle)
    {
        if (!Application.isPlaying || weapons == null || weaponRoot == null) return null;
        var source = GunRigAssets.Instance;
        if (source == null || source.armsRig == null) return null;
        var component = weapons.GetComponent<GunRig>();
        if (component == null) component = weapons.gameObject.AddComponent<GunRig>();
        GameTables.Bind(TableName, TableRow, component);
        if (component.useRig && component.Build(weapons, source, weaponRoot, muzzle)) return component;
        Destroy(component);
        return null;
    }

    /// <summary>The rig on the player that owns <paramref name="weapons"/>, while it holds a KINEMATION gun.</summary>
    internal static GunRig Holding(WeaponController weapons)
    {
        if (weapons == null) return null;
        var component = weapons.GetComponent<GunRig>();
        return component != null && component.held != null && component.armsRig != null ? component : null;
    }

    private bool Build(WeaponController weapons, GunRigAssets source, Transform root, Transform muzzlePoint)
    {
        controller = weapons; assets = source; weaponRoot = root; muzzle = muzzlePoint;
        movement = GetComponentInParent<PlayerMovement>();
        eyes = movement != null ? PlayerView.EyesOf(movement) : controller.AimOrigin;
        if (movement != null)
        {
            lookFrame = movement.transform.Find("CrouchPivot/BobPivot/LookPivot");
            if (lookFrame == null) lookFrame = movement.transform.Find("CrouchPivot/LookPivot");
        }
        if (muzzle != null) { muzzleHomePosition = muzzle.localPosition; muzzleHomeRotation = muzzle.localRotation; }
        placeholder = new WeaponPresentation(weaponRoot, muzzle);

        // Built under an inactive holder, so KINEMATION's components wake with our inputs and recoil already there.
        var holder = new GameObject(name + " gun rig (building)");
        holder.SetActive(false);
        armsRig = Instantiate(source.armsRig, holder.transform);
        armsRig.name = name + " gun rig";
        var rigRoot = armsRig.transform;
        rigAnimator = armsRig.GetComponentInChildren<Animator>(true);
        characterAnimation = armsRig.GetComponentInChildren<CharacterAnimationComponent>(true);
        procedural = armsRig.GetComponentInChildren<ProceduralAnimationComponent>(true);
        socket = GunRigArm.Find(rigRoot, "CameraSocket");
        var weaponBone = GunRigArm.Find(rigRoot, "IK weapon_bone");
        rigLeft = GunRigArm.Create(rigRoot, true);
        rigRight = GunRigArm.Create(rigRoot, false);
        if (rigAnimator == null || characterAnimation == null || socket == null || weaponBone == null || rigLeft == null || rigRight == null)
        {
            Debug.LogWarning("GunRig: KINEMATION's arms rig lacks its Animator, CAS components, CameraSocket, IK weapon_bone or arm bones; the placeholder gun pose stays.", this);
            Destroy(holder);
            armsRig = null;
            return false;
        }
        rigAnimator.applyRootMotion = false;
        rigAnimator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        // Only the guns are seen; the arms are the body's to show.
        foreach (var renderer in armsRig.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
        foreach (var collider in armsRig.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
        inputs = armsRig.AddComponent<GunRigInputs>();
        recoil = armsRig.AddComponent<RecoilAnimation>();
        rigAnimator.gameObject.AddComponent<GunRigEvents>();
        foreach (var audio in armsRig.GetComponentsInChildren<AudioSource>(true)) DestroyImmediate(audio);

        foreach (var gun in source.guns)
        {
            if (gun.weapon == null || gun.prefab == null) continue;
            var instance = Instantiate(gun.prefab, weaponBone, false);
            var weapon = instance.GetComponent<CasFPSWeapon>();
            if (weapon == null) { Destroy(instance); continue; }
            // FPS Animation Ultimate's own weapon scripts expect its own player and break on a CAS rig; KINEMATION's CAS
            // variants remove them, but the MX16A4 variant once named a stale id and left the sound one in.
            foreach (var leftover in instance.GetComponentsInChildren<FPSWeaponSound>(true)) DestroyImmediate(leftover);
            foreach (var leftover in instance.GetComponentsInChildren<FPSWeapon>(true)) DestroyImmediate(leftover);
            // KINEMATION's gun plays its own baked reload sound (its clips' PlayWeaponSound events) through the first
            // AudioSource under its root. The rig keeps none, so only our sound banks play; CasFPSWeapon looks once, on waking.
            foreach (var audio in instance.GetComponentsInChildren<AudioSource>(true)) DestroyImmediate(audio);
            SetLayer(instance.transform, weaponRoot.gameObject.layer);
            foreach (var collider in instance.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            guns.Add(new HeldGun { Data = gun.weapon, Instance = instance, Weapon = weapon,
                Player = instance.GetComponentInChildren<SimpleAnimationPlayer>(true) });
        }

        // KINEMATION's scripts look things up from their transform.root, so the rig is a root of its own, in our scene.
        rigRoot.SetParent(null, false);
        Destroy(holder);
        if (gameObject.scene.IsValid() && armsRig.scene != gameObject.scene) SceneManager.MoveGameObjectToScene(armsRig, gameObject.scene);
        Align();
        // Every gun wakes now (its scripts and its animation graph), not on its first draw, and its muzzle is measured.
        foreach (var gun in guns)
        {
            gun.Instance.SetActive(true);
            GunMuzzle.Measure(gun.Instance.transform, gun.Weapon.aimPoint, muzzleDepth, out gun.MuzzlePosition, out gun.MuzzleRotation);
            // Where this gun's reload clips move the magazine and bolt, for WeaponFeedback's reload sounds (sound session).
            ReloadMoments.Measure(gun.Data, gun.Weapon);
            gun.Instance.SetActive(false);
        }
        foreach (var renderer in weaponRoot.GetComponentsInChildren<Renderer>(true))
            if ((renderer is MeshRenderer || renderer is SkinnedMeshRenderer) && renderer.enabled
                && (muzzle == null || !renderer.transform.IsChildOf(muzzle))) placeholderModel.Add(renderer);

        controller.ReloadStarted += OnReloadStarted;
        controller.ReloadCompleted += OnReloadCompleted;
        if (movement != null) movement.Jumped += OnJumped;
        return true;
    }

    private void OnDestroy()
    {
        if (controller != null)
        {
            controller.ReloadStarted -= OnReloadStarted;
            controller.ReloadCompleted -= OnReloadCompleted;
        }
        if (movement != null) movement.Jumped -= OnJumped;
        if (armsRig != null) Destroy(armsRig);
    }

    private void OnEnable() { if (armsRig != null) armsRig.SetActive(true); }
    private void OnDisable() { if (armsRig != null) armsRig.SetActive(false); }

    private void Update()
    {
        if (armsRig == null || controller == null) return;
        float dt = Time.deltaTime;
        if (dt <= 0f) return;
        if (controller.EquippedData != shownData) Equip(controller.EquippedData);
        if (held == null) return;
        Feed(dt);
        // The eyes moved with the body this frame already; aligned now, the rig animates where it will be seen, so
        // anything KINEMATION traces in the world starts from this frame's place. LateUpdate corrects the last bit.
        Align();
    }

    // After the body has placed the eyes (PlayerBody, 250) and before the weapon fires from its muzzle (300).
    private void LateUpdate()
    {
        if (armsRig == null || held == null) return;
        Align();
        Fit(Time.deltaTime);
        RideGun();
        if (firing && Time.time - lastShot > shotInterval * TriggerReleaseIntervals) StopFiring();
        if (reloadShown && (controller.Runtime == null || !controller.Runtime.IsReloading)) StopReload();
    }

    // The body is added by PlayerMovement and may come after the weapon wakes, so it is found when first needed.
    private Animator BodyAnimator
    {
        get
        {
            if (body == null) body = GetComponent<PlayerBody>();
            return body != null ? body.BodyAnimator : null;
        }
    }

    private HeldGun Find(WeaponData data)
    {
        if (data == null) return null;
        foreach (var gun in guns) if (gun.Data == data) return gun;
        return null;
    }

    private void Equip(WeaponData data)
    {
        shownData = data;
        var next = Find(data);
        if (held != null && held != next) held.Instance.SetActive(false);
        held = next;
        foreach (var renderer in placeholderModel) if (renderer != null) renderer.enabled = held == null;
        StopFiring();
        reloadShown = false;
        if (held == null)
        {
            inputs.Weapon = null;
            // The placeholder pose moves the muzzle with the gun root again.
            if (muzzle != null) muzzle.SetLocalPositionAndRotation(muzzleHomePosition, muzzleHomeRotation);
            placeholder.Reset();
            return;
        }
        held.Instance.SetActive(shown);
        inputs.Weapon = held.Weapon;
        var settings = GunRigBindings.For(held.Weapon.animationSettings, armsRig);
        // Given both ways: CAS may still be waking (it then starts from the asset), or already running (it blends over).
        characterAnimation.SetAsset(settings);
        characterAnimation.UpdateAnimationSettings(settings);
        var weaponSettings = held.Weapon.weaponSettings;
        if (weaponSettings != null) recoil.Init(weaponSettings.recoilAnimData, 60f / data.ShotInterval, FireModeOf(data));
        Play(held.Weapon.equip);
        var equip = held.Weapon.equip.character;
        if (equip != null && equip.slot == AnimationSlot.Overlay) characterAnimation.StopAnimation(AnimationSlot.FullBody, .15f);
    }

    private static FireMode FireModeOf(WeaponData data)
        => data.FireMode == WeaponFireMode.Automatic ? FireMode.Auto : data.FireMode == WeaponFireMode.Burst ? FireMode.Burst : FireMode.Semi;

    // What KINEMATION's example controller feeds its rig (UpdateGait, UpdateCharacterRotation, UpdateAnimatorParameters),
    // from our player. The look stays level and unleaned: the rig is turned whole onto our eyes, which carry both.
    private void Feed(float dt)
    {
        float aim = controller.AimBlend;
        bool aiming = aim >= .999f || aim > lastAim;
        lastAim = aim;
        if (aiming != inputs.IsAiming)
        {
            inputs.IsAiming = aiming;
            recoil.isAiming = aiming;
            if (procedural != null && assets.aimingMotion != null) procedural.UpdateAnimationModifier(assets.aimingMotion);
        }

        Vector3 velocity = movement != null ? Vector3.ProjectOnPlane(movement.ActualVelocity, Vector3.up) : Vector3.zero;
        float speed = velocity.magnitude;
        float walk = Mathf.Max(.1f, movement != null ? movement.WalkSpeed : 1f);
        float sprint = Mathf.Max(walk + .1f, movement != null ? movement.SprintSpeed : 2f);
        // Our walk is KINEMATION's jog (2); slower is its walk (1), faster runs up to its sprint (3).
        float gait = speed < StillSpeed ? 0f : speed <= walk ? 1f + speed / walk : 2f + Mathf.InverseLerp(walk, sprint, speed);
        inputs.Gait = gait;
        Vector3 local = transform.InverseTransformDirection(velocity) / walk;
        inputs.MoveInput = Vector2.ClampMagnitude(new Vector2(local.x, local.z), 1f);
        bool grounded = movement == null || movement.IsGrounded;
        inputs.IsGrounded = grounded;
        ungrounded = grounded ? 0f : ungrounded + dt;
        bool moving = speed >= StillSpeed;
        if (moving != wasMoving)
        {
            wasMoving = moving;
            if (procedural != null && assets.movementMotion != null) procedural.UpdateAnimationModifier(assets.movementMotion);
        }
        inputs.LookInput = Vector3.zero;
        inputs.DeltaLookInput = LookDelta();
        inputs.AimingWeight = 1f;

        animMove = Vector2.Lerp(animMove, inputs.MoveInput, KMath.ExpDecayAlpha(MoveSmoothing, dt));
        animGait = KMath.FloatInterp(animGait, gait, GaitSmoothing, dt);
        SetFloat(MoveX, animMove.x);
        SetFloat(MoveY, animMove.y);
        SetFloat(GaitId, animGait);
        SetFloat(ViewWeightId, 0f);
        SetFloat(AimingWeightId, 1f);
        SetBool(IsFirstPersonId, true);
        SetBool(IsInAirId, ungrounded > FallDelay);
        SetBool(IsMovingId, moving);
        SetBool(CrouchId, movement != null && movement.IsCrouching);
    }

    // This frame's turn of the view in degrees (x right, y up), for KINEMATION's sway. Someone else's view turns in
    // network steps, which would only jolt it.
    private Vector2 LookDelta()
    {
        Transform view = lookFrame != null ? lookFrame : eyes;
        bool own = movement == null || (movement.IsLocal && !movement.IsNetworkDriven);
        if (view == null || !own) { hasLook = false; return Vector2.zero; }
        Vector3 forward = view.forward;
        float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        float pitch = Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        var delta = hasLook ? new Vector2(Mathf.DeltaAngle(lastYaw, yaw), pitch - lastPitch) : Vector2.zero;
        lastYaw = yaw; lastPitch = pitch; hasLook = true;
        return delta;
    }

    private void SetFloat(int id, float value) { if (HasParameter(id)) rigAnimator.SetFloat(id, value); }
    private void SetBool(int id, bool value) { if (HasParameter(id)) rigAnimator.SetBool(id, value); }

    // The controller's parameters are only readable once the rig's Animator is awake and playing it.
    private bool HasParameter(int id)
    {
        if (parameters.Count == 0)
        {
            if (!rigAnimator.isActiveAndEnabled || !rigAnimator.isInitialized || rigAnimator.runtimeAnimatorController == null) return false;
            foreach (var parameter in rigAnimator.parameters) parameters.Add(parameter.nameHash);
        }
        return parameters.Contains(id);
    }

    private void OnJumped()
    {
        if (held != null && HasParameter(JumpedId)) rigAnimator.SetTrigger(JumpedId);
    }

    // Turns and moves the whole rig so its camera socket sits on our eyes.
    private void Align()
    {
        if (eyes == null) return;
        var root = armsRig.transform;
        Quaternion inRoot = Quaternion.Inverse(root.rotation) * socket.rotation;
        Vector3 offset = Quaternion.Inverse(root.rotation) * (socket.position - root.position);
        root.rotation = eyes.rotation * Quaternion.Inverse(inRoot);
        root.position = eyes.position - root.rotation * offset;
    }

    // Moves the rig toward where this body's arms would hold its hands: each hand at the body's shoulder plus the rig
    // arm's reach scaled to the body's arm length. Kept in the eyes' frame, eased, and let go while aiming down sights.
    private void Fit(float dt)
    {
        Vector3 wanted = Vector3.zero;
        var animator = BodyAnimator;
        if (bodyFit > 0f && eyes != null && animator != null && animator.isHuman)
        {
            Vector3 sum = Vector3.zero;
            int count = 0;
            if (Reach(animator, rigRight, false, out Vector3 move)) { sum += move; count++; }
            if (Reach(animator, rigLeft, true, out move)) { sum += move; count++; }
            if (count > 0)
                wanted = Vector3.ClampMagnitude(Quaternion.Inverse(eyes.rotation) * (sum / count) * (bodyFit * (1f - controller.AimBlend)), fitLimit);
        }
        fit = Vector3.Lerp(fit, wanted, 1f - Mathf.Exp(-Mathf.Log(2f) * Mathf.Max(0f, dt) / fitHalfLife));
        if (eyes != null) armsRig.transform.position += eyes.rotation * fit;
    }

    private static bool Reach(Animator animator, GunRigArm arm, bool left, out Vector3 move)
    {
        move = Vector3.zero;
        var upper = animator.GetBoneTransform(left ? HumanBodyBones.LeftUpperArm : HumanBodyBones.RightUpperArm);
        var lower = animator.GetBoneTransform(left ? HumanBodyBones.LeftLowerArm : HumanBodyBones.RightLowerArm);
        var hand = animator.GetBoneTransform(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
        if (upper == null || lower == null || hand == null || arm.Length <= 0f) return false;
        float length = Vector3.Distance(upper.position, lower.position) + Vector3.Distance(lower.position, hand.position);
        Vector3 wrist = arm.Wrist.position;
        move = upper.position + (wrist - arm.Shoulder.position) * (length / arm.Length) - wrist;
        return true;
    }

    // Our gun root and muzzle follow the gun in the rig's hands: the root on the gun with the sight line forward, the
    // muzzle on the gun's measured muzzle. The muzzle flash, light, tracer and shots start there.
    private void RideGun()
    {
        var gun = held.Instance.transform;
        var aimPoint = held.Weapon.aimPoint;
        Quaternion forward = aimPoint != null ? Quaternion.LookRotation(aimPoint.forward, aimPoint.up) : gun.rotation;
        weaponRoot.SetPositionAndRotation(gun.position, forward);
        if (muzzle != null) muzzle.SetPositionAndRotation(gun.TransformPoint(held.MuzzlePosition), gun.rotation * held.MuzzleRotation);
    }

    private void Play(FPSAnimationPair pair, float rate = 1f)
    {
        if (pair.character != null) characterAnimation.PlayAnimation(Rated(pair.character, rate));
        if (pair.weapon != null && held.Player != null && held.Player.isActiveAndEnabled) held.Player.PlayAnimation(Rated(pair.weapon, rate));
    }

    // A copy of the clip asset played faster or slower, shared by every rig.
    private static AnimationAsset Rated(AnimationAsset asset, float rate)
    {
        if (asset == null || Mathf.Abs(rate - 1f) < .01f) return asset;
        var key = (asset, Mathf.RoundToInt(rate * 1000f));
        if (RatedClips.TryGetValue(key, out var copy) && copy != null) return copy;
        copy = Instantiate(asset);
        copy.name = asset.name;
        copy.hideFlags = HideFlags.DontSave;
        copy.playRate = asset.playRate * rate;
        RatedClips[key] = copy;
        return copy;
    }

    private void OnReloadStarted(bool empty)
    {
        if (held == null) return;
        var pair = empty ? held.Weapon.reloadEmpty : held.Weapon.reloadTactical;
        var clip = pair.character != null ? pair.character : pair.weapon;
        float duration = controller.Runtime != null ? controller.Runtime.ReloadDuration : 0f;
        float length = clip != null ? clip.GetPlayLength() : 0f;
        StopFiring();
        Play(pair, duration > .01f && length > .01f ? length / duration : 1f);
        reloading = pair;
        reloadShown = true;
    }

    private void OnReloadCompleted() => reloadShown = false;

    // The reload was cut short (a dodge, a climb, death): its clip goes too.
    private void StopReload()
    {
        reloadShown = false;
        if (reloading.character != null) characterAnimation.StopAnimation(reloading.character);
        if (reloading.weapon != null && held.Player != null && held.Player.enabled)
        {
            // KINEMATION's weapon player stops its clip when it is switched off.
            held.Player.enabled = false;
            held.Player.enabled = true;
        }
    }

    private void StopFiring()
    {
        if (!firing) return;
        firing = false;
        recoil.Stop();
    }

    private static void SetLayer(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++) SetLayer(root.GetChild(i), layer);
    }

    // ---- The gun's pose for WeaponController (IWeaponPose) ----

    public bool IsHighReady => held == null ? placeholder.IsHighReady : highReady;

    public void SetVisible(bool visible)
    {
        shown = visible;
        placeholder.SetVisible(visible);
        if (held != null && held.Instance.activeSelf != visible) held.Instance.SetActive(visible);
    }

    public void Reset()
    {
        if (held == null) { placeholder.Reset(); return; }
        highReady = false;
        StopFiring();
    }

    public void OnShot(WeaponData data, Vector2 shotRecoil)
    {
        if (held == null) { placeholder.OnShot(data, shotRecoil); return; }
        firing = true;
        recoil.Play();
        lastShot = Time.time;
        shotInterval = data.ShotInterval;
        var weaponSettings = held.Weapon.weaponSettings;
        bool lastRound = weaponSettings != null && weaponSettings.hasFireOut && controller.LastShot.AmmoAfterShot == 0;
        Play(lastRound ? held.Weapon.fireOut : held.Weapon.fire);
    }

    public Vector3 Prepare(Transform view, WeaponData data, float ads)
        => held == null ? placeholder.Prepare(view, data, ads) : muzzle != null ? muzzle.position : view.position;

    public bool Pose(GameObject owner, Transform view, Vector3 target, bool obstructed, WeaponRuntime runtime, float dt, int layers)
    {
        if (held == null) return placeholder.Pose(owner, view, target, obstructed, runtime, dt, layers);
        highReady = obstructed;
        return obstructed;
    }

    // KINEMATION's sway modifier sways the gun.
    public void ApplySway(Transform view, Vector3 offset, Quaternion turn)
    {
        if (held == null) placeholder.ApplySway(view, offset, turn);
    }

    // ---- The body's hands on the gun (PlayerBodyHands) ----

    /// <summary>
    /// Where the body's <paramref name="arm"/> puts its wrist to hold the gun as the rig's hand does: the hand turned
    /// like the rig's, the palm's centre on the rig palm's centre, the elbow out the way the rig's elbow goes.
    /// </summary>
    internal bool HandTarget(ArmIK arm, bool isLeft, out Vector3 position, out Quaternion rotation, out Vector3 hint)
    {
        var rigArm = isLeft ? rigLeft : rigRight;
        rigArm.Frame(out Vector3 fingers, out Vector3 palm, out Vector3 palmCentre);
        rotation = arm.HandRotation(fingers, palm);
        position = arm.WristAtPalm(palmCentre, rotation);
        hint = arm.Upper.position + (rigArm.Elbow.position - rigArm.Shoulder.position);
        return true;
    }

    /// <summary>Bends the body's fingers on one hand <paramref name="weight"/> of the way toward the rig hand's grip.</summary>
    internal void FollowFingers(bool isLeft, float weight)
    {
        var animator = BodyAnimator;
        if (animator == null || weight <= 0f) return;
        var fingers = isLeft ? fingersLeft : fingersRight;
        if (fingers == null || fingers.Animator != animator)
        {
            fingers = GunRigFingers.Create(animator, isLeft);
            if (isLeft) fingersLeft = fingers; else fingersRight = fingers;
        }
        fingers?.Follow(isLeft ? rigLeft : rigRight, weight);
    }
}
