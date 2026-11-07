using System.Collections.Generic;
using KINEMATION.CharacterAnimationSystem.Integrations.FPSAnimationPack.Scripts;
using KINEMATION.FPSAnimationFramework.Runtime.Core;
using KINEMATION.FPSAnimationFramework.Runtime.Layers.AdsLayer;
using KINEMATION.FPSAnimationFramework.Runtime.Layers.AttachHandLayer;
using KINEMATION.FPSAnimationFramework.Runtime.Layers.IkMotionLayer;
using KINEMATION.FPSAnimationFramework.Runtime.Layers.LookLayer;
using KINEMATION.FPSAnimationFramework.Runtime.Layers.PoseSamplerLayer;
using KINEMATION.FPSAnimationFramework.Runtime.Playables;
using KINEMATION.FPSAnimationPack.Scripts.Sounds;
using KINEMATION.FPSAnimationPack.Scripts.Weapon;
using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Input;
using UnityEngine;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// The body holds its guns with KINEMATION FPS Animation Framework, set up on it as FPSAF's demo humanoid player is
/// (FPSHumanoidPlayer): FPSAnimator, its bone controller, input controller and playables controller (FPSAF's own, on the
/// Animator's graph, where PlayerBody plays the body's clips as the Animator output), the recoil animation, the rig with
/// its IK weapon bone, and per gun the demo's humanoid animator profile. FPSAF then poses the upper body every frame: the
/// gun's pose clip on the arms, the spine and head following the view (Look), the lean, the aim down the sight (Ads), sway,
/// recoil, the gun held back from walls (GunCover) and both arms on the gun (IK).
/// The clips are KINEMATION's own, remade for this body by KINEMATION Retarget Pro (GunBakes), and FPSAF's Pose Sampler
/// starts the gun where Retarget Pro put it on this body. The camera socket FPSAF aims the sight at is the body's eyes,
/// where the camera rides.
/// This component is the demo's FPSController for our player: it feeds FPSAF what our player does (the view, the walk,
/// the lean, aiming, sprinting, jumps and crouches) and tells the held gun (FPSGun, the demo's Weapon) when our
/// WeaponController draws it, fires it or reloads it. WeaponController keeps every rule; this only shows them.
/// Numbers come from Tables/FPSBody.csv, row Rig.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(295)]
public sealed partial class FPSBody : MonoBehaviour, IWeaponPose
{
    public const string TableName = "FPSBody";
    private const string TableRow = "Rig";

    [SerializeField, Range(0f, 1f), Tooltip("Aiming a gun with a stock, how much of the way the eyes come down onto it, so the butt "
        + "stays in the shoulder (1) instead of the gun coming up to the eyes (0).")]
    private float cheekWeld = 1f;
    [SerializeField, Tooltip("The most the neck, upper chest and chest bend for the cheek weld (degrees).")]
    private Vector3 cheekWeldLimits = new Vector3(30f, 15f, 10f);
    [SerializeField, Min(0f), Tooltip("How long a finished reload takes to settle back into the hold (s). KINEMATION's 0.15 made the arms "
        + "jump from the reload to the hold.")]
    private float reloadSettle = .35f;
    [SerializeField, Range(0f, 90f), Tooltip("Furthest the spine twists toward the view where the legs don't turn with it (degrees). "
        + "FPSAF's Look reaches its full twist at 90.")]
    private float twistLimit = 60f;
    [SerializeField, Range(0f, 1f), Tooltip("How much of the view the spine follows while sprinting (FPSAF demo LookLayerWeight).")]
    private float sprintLookWeight = .3f;
    [SerializeField, Range(0f, 1f), Tooltip("Sprinting from this share of the way between walk and sprint (PlayerMovement.SprintProgress).")]
    private float sprintFrom = .5f;
    [SerializeField, Min(0f), Tooltip("How quickly a reload cut short lets its clip go (s).")]
    private float reloadBlendOut = .15f;
    [SerializeField, Min(.001f), Tooltip("How deep the gun's front face is taken when the muzzle is found from the gun's shape (m).")]
    private float muzzleDepth = .015f;
    [SerializeField, Min(.01f), Tooltip("How quickly crouch and sprint weights follow the body (s).")]
    private float weightTime = .15f;
    [SerializeField, Min(.01f), Tooltip("How long FPSAF takes the upper body over when a gun is drawn, and gives it back when it is put away (s).")]
    private float holdBlendTime = .2f;

    // Shots further apart than this many fire intervals have let go of the trigger: the recoil then settles.
    private const float TriggerReleaseIntervals = 1.5f;
    // KINEMATION's example controller: still below this speed.
    private const float StillSpeed = .1f;
    // A gun still held back more than this share holds its fire.
    private const float RaisedFiring = .05f;
    // A hip muzzle is learnt only within this angle of where the eyes look (degrees); a hip hold is well inside it.
    private const float HipMuzzleCone = 35f;
    // GunCoverLayer's input property (InputConfig_Body).
    private const string CoverProperty = "CoverWeight";
    // The Animator parameters FPSAF's profiles read (Characters/BodyHost).
    private static readonly int CrouchWeightId = Animator.StringToHash("CrouchWeight"), SprintPoseWeightId = Animator.StringToHash("SprintPoseWeight"),
        VelocityId = Animator.StringToHash("Velocity"), FullBodyWeightId = Animator.StringToHash("FullBodyWeight");

    private WeaponController controller;
    private PlayerMovement movement;
    private PlayerBody body;
    private FPSBodyAssets assets;
    private Transform weaponRoot, muzzle, lookFrame, eyes, view;
    private Vector3 muzzleHomePosition;
    private Quaternion muzzleHomeRotation;

    // What is set up on the current body.
    private Animator animator;
    private FPSBodyRig rig;
    private UserInputController input;
    private FPSPlayablesController playables;
    private FPSBodyAnimator framework;
    private RecoilAnimation recoil;
    private FPSBodyCamera bodyCamera;
    private PlayerBodyHands hands;
    private readonly List<FPSGun> guns = new();
    private readonly List<Object> made = new();
    private readonly HashSet<string> reported = new();

    private FPSGun held;
    private WeaponData shownData;
    private bool shown = true, linked, aiming, firing, highReady, hasLook, wasGrounded = true, wasCrouched, wasMoving;
    private float lastShot, shotInterval, lastAim, lastYaw, lastPitch, leanSign, crouchWeight, sprintWeight, cover, holdWeight;
    // When the gun was last drawn, and how long FPSAF's draw motion plays (s): the hip muzzle is learnt after it.
    private float drawnAt, drawSettle;
    // How FPSAF last held the gun (KeepGunInView): from the eyes at the hip and fully aimed, both hands on it, their fingers.
    private KTransform gunAtHip, gunAimed, rightHandInGun, leftHandInGun;
    private bool hasGunInView, hasGunAimed;
    private readonly List<(Transform bone, bool left)> fingers = new();
    private readonly List<Quaternion> fingerGrip = new();

    /// <summary>Holds <paramref name="weapons"/>' guns with FPSAF and returns this as the gun's pose.</summary>
    internal static IWeaponPose Attach(WeaponController weapons, Transform weaponRoot, Transform muzzle)
    {
        if (weapons == null) return null;
        var component = weapons.GetComponent<FPSBody>();
        if (component == null) component = weapons.gameObject.AddComponent<FPSBody>();
        GameTables.Bind(TableName, TableRow, component);
        component.Begin(weapons, weaponRoot, muzzle);
        return component;
    }

    /// <summary>The FPSAF body on the player that owns <paramref name="weapons"/>, while it holds one of its guns.</summary>
    internal static FPSBody Holding(WeaponController weapons)
    {
        if (weapons == null) return null;
        var component = weapons.GetComponent<FPSBody>();
        return component != null && component.HoldsGun ? component : null;
    }

    /// <summary>FPSAF is set up on <paramref name="bodyAnimator"/>; KINEMATION's Character Animation System goes on after it (BodyCas).</summary>
    internal bool InstalledOn(Animator bodyAnimator) => bodyAnimator != null && animator == bodyAnimator && framework != null;

    private bool HoldsGun => held != null && linked && framework != null;

    private void Begin(WeaponController weapons, Transform root, Transform muzzlePoint)
    {
        controller = weapons;
        assets = FPSBodyAssets.Instance;
        weaponRoot = root;
        muzzle = muzzlePoint;
        movement = GetComponentInParent<PlayerMovement>();
        eyes = movement != null ? PlayerView.EyesOf(movement) : controller.AimOrigin;
        if (movement != null)
        {
            lookFrame = movement.transform.Find("CrouchPivot/BobPivot/LookPivot");
            if (lookFrame == null) lookFrame = movement.transform.Find("CrouchPivot/LookPivot");
        }
        var camera = GetComponentInChildren<Camera>(true);
        view = camera != null ? camera.transform : null;
        if (muzzle != null) { muzzleHomePosition = muzzle.localPosition; muzzleHomeRotation = muzzle.localRotation; }
        controller.ReloadStarted += OnReloadStarted;
        controller.ReloadCompleted += OnReloadCompleted;
        if (movement != null) movement.Jumped += OnJumped;
        if (assets == null || assets.inputConfig == null || assets.rig == null || assets.upperBody == null || assets.idleProfile == null)
            Report("Resources/Combat/FPSBody is missing or incomplete; the body can't hold KINEMATION's guns.");
    }

    private void OnDestroy()
    {
        if (controller != null)
        {
            controller.ReloadStarted -= OnReloadStarted;
            controller.ReloadCompleted -= OnReloadCompleted;
        }
        if (movement != null) movement.Jumped -= OnJumped;
        if (body != null) body.UpperBodyFromFramework = false;
        ForgetMade();
    }

    private PlayerBody Body
    {
        get
        {
            if (body == null) body = GetComponent<PlayerBody>();
            return body;
        }
    }

    private void Update()
    {
        if (controller == null || assets == null) return;
        var bodyAnimator = Body != null ? body.BodyAnimator : null;
        if (bodyAnimator != animator) Install(bodyAnimator);
        if (framework == null) return;
        float dt = Time.deltaTime;
        if (controller.EquippedData != shownData) Equip(controller.EquippedData);
        if (dt > 0f) Feed(dt);
        // FPSAF has the upper body (spine, both arms) only standing or crouched with both hands on the gun
        // (PlayerBodyHands.FrameworkShare). In the other moves (a vault, a slide, a dive, a roll) the move's own armed clip
        // poses it, as before KINEMATION, and the hands hold the gun where FPSAF last held it (KeepGunInView). FPSAF held
        // the whole upper body straight through those moves: a vault stood high with its hand off the obstacle, a slide sat
        // upright (user, 2026-11-07).
        body.UpperBodyFromFramework = HoldsGun && Framework >= .5f;
        // FPSAF's upper-body layer comes in with the gun and goes with it; the gun's pose is already full in it (its pose
        // asset blends in at once), so the body never passes through its bind pose on the way.
        holdWeight = Mathf.MoveTowards(holdWeight, HoldsGun ? 1f : 0f, dt / Mathf.Max(.01f, holdBlendTime));
        playables.SetControllerWeight(holdWeight * Framework);
        if (bodyCamera != null) bodyCamera.SetGunWeight(holdWeight * Framework);
    }

    // ---- Setting FPSAF up on the body (the demo's FPSHumanoidPlayer, as FPSAF's wizard builds it) ----

    private void Install(Animator bodyAnimator)
    {
        Forget();
        animator = bodyAnimator;
        // PlayerBody has put the body's clips into the Animator's graph first; FPSAF takes them as the Animator output.
        if (animator == null || !animator.playableGraph.IsValid() || body.IsLimp) { animator = null; return; }
        rig = FPSBodyRig.Build(animator, assets.rig);
        if (rig == null)
        {
            Report($"{animator.name} is not a humanoid body FPSAF can hold guns with.");
            return;
        }
        // FPSAF's Ads lines the sight up with the camera socket: the eyes, where the camera rides.
        rig.Camera.position = body.EyePosition;

        var host = animator.gameObject;
        input = host.AddComponent<UserInputController>();
        input.inputConfig = assets.inputConfig;
        playables = host.AddComponent<FPSPlayablesController>();
        playables.upperBodyMask = assets.upperBody;
        host.AddComponent<FPSBoneController>();
        recoil = host.AddComponent<RecoilAnimation>();
        if (view != null)
        {
            bodyCamera = view.GetComponent<FPSBodyCamera>();
            if (bodyCamera == null) bodyCamera = view.gameObject.AddComponent<FPSBodyCamera>();
            // Switched off: FPSAnimator calls it; its own Update would put the camera back where it first was.
            bodyCamera.enabled = false;
            bodyCamera.Bind(CameraPlaced);
        }
        framework = host.AddComponent<FPSBodyAnimator>();
        // Set up now rather than on its first frame, so FPSAF's outputs follow the body's clips and come before what
        // Character Animation System adds after it (BodyCas).
        framework.Begin(assets.idleProfile, bodyCamera, AfterHands);
        // FPSAF's master mixer now plays the body's clips under its upper-body layer; the body's own output steps back.
        body.HandClipsToFramework();
        // Nothing plays in FPSAF's upper-body layer until a gun is drawn, and an empty layer at full weight would pull the
        // upper body to its bind pose; the gun brings it in.
        playables.SetControllerWeight(0f);
        hands = GetComponent<PlayerBodyHands>();
        foreach (var entry in assets.guns) AddGun(entry);
    }

    // The body went (a new skin, a rebuild): what was set up on it went with it.
    private void Forget()
    {
        guns.Clear();
        held = null;
        shownData = null;
        linked = aiming = firing = highReady = hasLook = hasGunInView = hasGunAimed = false;
        fingers.Clear();
        fingerGrip.Clear();
        holdWeight = cover = 0f;
        rig = null; input = null; playables = null; framework = null; recoil = null;
        if (body != null) body.UpperBodyFromFramework = false;
        if (bodyCamera != null) bodyCamera.SetGunWeight(0f);
        ForgetMade();
    }

    private void ForgetMade()
    {
        foreach (var copy in made) if (copy != null) Destroy(copy);
        made.Clear();
    }

    // One of KINEMATION's guns into the IK weapon bone, as the demo's FPSController spawns its weapons, with its
    // FPS Animation Pack scripts made quiet (their data stays; FPSAF plays them).
    private void AddGun(FPSBodyAssets.Gun entry)
    {
        if (entry.weapon == null || entry.prefab == null || entry.profile == null) return;
        var bakes = GunBakes.Instance;
        if (bakes == null || !bakes.TryGet(body.BodyModel, entry.weapon, out var bake, out bool exact))
        {
            Report($"No clips of KINEMATION's {entry.weapon.name} were made for the bodies yet: Unity's editor makes them with Retarget Pro "
                + "(Tools → ENFILADE → Combat → Retarget KINEMATION Gun Clips).");
            return;
        }
        if (!exact) Report($"KINEMATION's gun clips were not made for {body.BodyModel.name} yet; it holds them as made for {bake.skin.name}.");

        // Woken under a root of its own: KINEMATION's gun script looks up its sound source from transform.root when it
        // wakes (even switched off), and under the player it would find our footstep or voice source and play its own
        // baked reload sounds over ours from the clip's events. With nothing to find, it stays silent.
        var holder = new GameObject(name + " gun (building)");
        holder.SetActive(false);
        var instance = Instantiate(entry.prefab, holder.transform, false);
        instance.name = entry.prefab.name;
        var kinemation = instance.GetComponent<CasFPSWeapon>();
        // FPS Animation Pack's own weapon scripts expect its own player.
        foreach (var leftover in instance.GetComponentsInChildren<FPSWeaponSound>(true)) DestroyImmediate(leftover);
        foreach (var leftover in instance.GetComponentsInChildren<FPSWeapon>(true)) DestroyImmediate(leftover);
        if (kinemation != null) kinemation.enabled = false;
        SetLayer(instance.transform, weaponRoot.gameObject.layer);
        holder.SetActive(true);
        instance.transform.SetParent(rig.IkWeaponBone, false);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;
        Destroy(holder);

        var gun = instance.AddComponent<FPSGun>();
        Quaternion gunFrame = GunFrameOf(kinemation);
        var profile = ProfileFor(entry.profile, entry.weapon, bake, gunFrame);
        gun.Set(entry, kinemation, muzzleDepth, profile, Settled(Copy(entry.reloadTactical, bake.reloadTactical)),
            Settled(Copy(entry.reloadEmpty, bake.reloadEmpty)), gunFrame);
        gun.SetGrip(bake, exact);
        // From this body's profile: a stand-in's share has gone to the bone below it.
        gun.LeanChain = LeanChainOf(profile);
        // Where this gun's reload clips move the magazine and bolt, for WeaponFeedback's reload sounds (sound session).
        if (kinemation != null) ReloadMoments.Measure(entry.weapon, kinemation);
        instance.SetActive(false);
        guns.Add(gun);
    }

    // KINEMATION's weapon bone (ik_hand_gun) has its axes a quarter turn from the gun's: its FPS Animation Pack turns the
    // bone by the gun's rotationOffset (FPSProceduralJob) before anything moves it, and the gun model hangs in that turned
    // frame. Without it the gun stood straight up in the hands (user, 2026-11-07: "high ready").
    public static Quaternion GunFrameOf(CasFPSWeapon kinemation)
        => kinemation != null && kinemation.weaponSettings != null ? kinemation.weaponSettings.rotationOffset : Quaternion.Euler(90f, 0f, 0f);

    // The demo's profile for this body: its Pose Sampler samples the pose made for this body and starts the gun where
    // Retarget Pro put it, KINEMATION's weapon bone. FPSAF's IK weapon bone, which the gun hangs from and every layer moves
    // (aim, sway, recoil, IK motions), takes the gun's own frame through the Pose Sampler's weapon bone offset, as FPSAF
    // expects: its demo's weapon bone faces the gun's way.
    // FPSAF's Ads brings the sight up at our weapon's aim time: our rule (WeaponController.AimBlend) zooms the view and
    // tightens the spread over AdsTransitionTime, and the demo's aimingSpeed (1.8 a second, 0.56 s) left the gun 0.4 s
    // behind the zoom. Before KINEMATION the gun rose with the rule (user picked it back, 2026-11-07).
    private FPSAnimatorProfile ProfileFor(FPSAnimatorProfile source, WeaponData weapon, GunBakes.Entry bake, Quaternion gunFrame)
    {
        var profile = Instantiate(source);
        Keep(profile);
        profile.settings = new List<FPSAnimatorLayerSettings>(source.settings.Count);
        foreach (var layer in source.settings)
        {
            // FPSAF's Attach Hand holds the left hand by a pose measured against the weapon bone (WeaponBone) when the
            // profile links, at full weight unless the clips carry a MaskAttachHand curve. Ours carry none and never move
            // that bone: it pulled the left hand 4 to 8 cm off the gun and laid its fingers flat (user, 2026-11-07). The
            // clips made for this body already hold the left hand on the gun (Retarget Pro's FPS feature), reloads too.
            if (layer is AttachHandLayerSettings) continue;
            if (layer is PoseSamplerLayerSettings sampler)
            {
                var mine = Instantiate(sampler);
                Keep(mine);
                mine.poseToSample = Copy(sampler.poseToSample, bake.pose);
                mine.defaultWeaponPose = new KTransform(bake.gunPosition, bake.gunRotation, Vector3.one);
                mine.weaponBoneOffset = new KTransform(Vector3.zero, gunFrame, Vector3.one);
                mine.overwriteWeaponBone = true;
                profile.settings.Add(mine);
            }
            else if (layer is LookLayerSettings look)
                profile.settings.Add(LookFor(look));
            else if (layer is AdsLayerSettings ads && weapon != null)
            {
                // The cheek weld goes just before Ads: the head comes to a shouldered gun before the sight is lined up.
                if (cheekWeld > 0f) profile.settings.Add(CheekWeldFor(ads));
                var mine = Instantiate(ads);
                Keep(mine);
                mine.aimingSpeed = 1f / weapon.AdsTransitionTime;
                profile.settings.Add(mine);
            }
            else profile.settings.Add(layer);
        }
        return profile;
    }

    // FPSAF's Look shares the view's pitch, turn and lean down the spine by bone; a body without one of them (Hiro and Sil
    // have no UpperChest) has an empty stand-in there, and its share turned nothing: the gun followed a small turn only two
    // thirds of the way, and a lean fell a third short (probe, 2026-11-08). Here a missing bone's share goes to the one below.
    private LookLayerSettings LookFor(LookLayerSettings source)
    {
        var look = Instantiate(source);
        Keep(look);
        Fold(look.pitchOffsetElements);
        Fold(look.yawOffsetElements);
        Fold(look.rollOffsetElements);
        // A peek tips the body from the waist up: the hips' share of the lean goes to the bone above, so the pelvis and
        // the legs standing on it stay put. With the lean now as deep as the peek needs, the hips' share swung the feet
        // (user, 2026-11-08).
        var roll = look.rollOffsetElements;
        for (int i = 0; i + 1 < roll.Count; i++)
        {
            if (roll[i].rigElement.name != "Hips") continue;
            var hipsShare = roll[i];
            var above = roll[i + 1];
            above.clampedAngle += hipsShare.clampedAngle;
            above.cachedClampedAngle = above.clampedAngle;
            roll[i + 1] = above;
            hipsShare.clampedAngle = hipsShare.cachedClampedAngle = Vector2.zero;
            roll[i] = hipsShare;
            break;
        }
        return look;

        void Fold(List<LookLayerElement> elements)
        {
            for (int i = 0; i < elements.Count; i++)
            {
                if (rig.IsBodyBone(rig.Component.GetRigTransform(elements[i].rigElement))) continue;
                var standIn = elements[i];
                for (int j = i - 1; j >= 0; j--)
                {
                    if (!rig.IsBodyBone(rig.Component.GetRigTransform(elements[j].rigElement))) continue;
                    var below = elements[j];
                    below.clampedAngle += standIn.clampedAngle;
                    below.cachedClampedAngle = below.clampedAngle;
                    elements[j] = below;
                    break;
                }
                standIn.clampedAngle = standIn.cachedClampedAngle = Vector2.zero;
                elements[i] = standIn;
            }
        }
    }

    // Our cheek weld layer (CheekWeldLayerSettings), on the same rig and blended out by the same full-body moves as Ads.
    private CheekWeldLayerSettings CheekWeldFor(AdsLayerSettings ads)
    {
        var weld = ScriptableObject.CreateInstance<CheekWeldLayerSettings>();
        Keep(weld);
        weld.rigAsset = ads.rigAsset;
        weld.curveBlending = new List<CurveBlend>(ads.curveBlending);
        weld.share = cheekWeld;
        weld.limits = cheekWeldLimits;
        return weld;
    }

    // How FPSAF's Look shares a lean down this body's spine for a gun's profile: each roll element's bone and its part of
    // the whole (its angle at a 90° lean).
    private List<(Transform bone, float share)> LeanChainOf(FPSAnimatorProfile profile)
    {
        var chain = new List<(Transform bone, float share)>();
        if (profile == null || rig == null || rig.Component == null) return chain;
        foreach (var layer in profile.settings)
            if (layer is LookLayerSettings look)
            {
                foreach (var element in look.rollOffsetElements)
                    if (rig.Component.GetRigTransform(element.rigElement) is Transform bone)
                        chain.Add((bone, Mathf.Abs(element.clampedAngle.x) / 90f));
                break;
            }
        return chain;
    }

    // A reload eases back into the hold over reloadSettle (user, 2026-11-08: the arms jumped from the reload to the hold).
    private FPSAnimationAsset Settled(FPSAnimationAsset reload)
    {
        if (reload != null) reload.blendTime.blendOutTime = Mathf.Max(reload.blendTime.blendOutTime, reloadSettle);
        return reload;
    }

    // FPSAF's animation asset (blend times, curves) with the clip made for this body.
    private FPSAnimationAsset Copy(FPSAnimationAsset source, AnimationClip clip)
    {
        if (source == null || clip == null) return null;
        var copy = Instantiate(source);
        Keep(copy);
        copy.clip = clip;
        return copy;
    }

    private void Keep(Object copy)
    {
        copy.hideFlags = HideFlags.DontSave;
        made.Add(copy);
    }

    private static void SetLayer(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++) SetLayer(root.GetChild(i), layer);
    }

    // ---- Drawing and putting away (the demo's EquipWeapon / UnequipWeapon) ----

    private FPSGun Find(WeaponData data)
    {
        if (data == null) return null;
        foreach (var gun in guns) if (gun.Data == data) return gun;
        return null;
    }

    private void Equip(WeaponData data)
    {
        shownData = data;
        StopFiring();
        var next = Find(data);
        if (held != null && held != next) PutAway();
        held = next;
        if (held == null)
        {
            if (muzzle != null) muzzle.SetLocalPositionAndRotation(muzzleHomePosition, muzzleHomeRotation);
            return;
        }
        if (shown) Draw();
    }

    private void Draw()
    {
        if (held == null || linked) return;
        held.gameObject.SetActive(true);
        aiming = false;
        held.Equip(framework, recoil, assets, 60f / Mathf.Max(.001f, held.Data.ShotInterval), FireModeOf(held.Data), bodyCamera);
        drawnAt = Time.time;
        drawSettle = LengthOf(assets.equipMotion);
        linked = true;
    }

    private void PutAway()
    {
        if (held == null || !linked) return;
        held.StopReload(playables, 0f, bodyCamera);
        held.Unequip(framework, recoil, assets);
        held.gameObject.SetActive(false);
        framework.UnlinkAnimatorProfile();
        linked = false;
    }

    private static FireMode FireModeOf(WeaponData data)
        => data.FireMode == WeaponFireMode.Automatic ? FireMode.Auto : data.FireMode == WeaponFireMode.Burst ? FireMode.Burst : FireMode.Semi;

    // ---- What the demo's FPSController and FPSMovement feed FPSAF, from our player ----

    private void Feed(float dt)
    {
        Vector3 velocity = movement != null ? Vector3.ProjectOnPlane(movement.ActualVelocity, Vector3.up) : Vector3.zero;
        float speed = velocity.magnitude;
        float walk = Mathf.Max(.1f, movement != null ? movement.WalkSpeed : 1f);
        Vector3 local = transform.InverseTransformDirection(velocity) / walk;
        input.SetValue(FPSANames.MoveInput, (Vector4)Vector2.ClampMagnitude(new Vector2(local.x, local.z), 1f));

        // The view: FPSAF's pitch is down-positive; its yaw is how far the view turns from where the body faces. FPSAF's
        // Look spreads that yaw over the spine without a limit (90 degrees of input, 90 of twist), so where the legs can't
        // follow the view (a vault, a slide) the spine twists only as far as a back does; the head and camera take the rest
        // (user, 2026-11-07: looking back mid-vault turned the upper body 180 degrees from the legs).
        LookDelta(out float pitchDown, out Vector2 delta);
        // Standing or crouched the legs turn after the view, and until they do the upper body keeps up all the way (FPSAF's
        // 90): pinned at 60 (probe, 2026-11-08) the gun stopped short whenever the view led the legs further, then swung back.
        var stance = movement != null ? movement.CurrentState : MovementState.Standing;
        float limit = stance == MovementState.Standing || stance == MovementState.Crouching ? 90f : twistLimit;
        float twist = Mathf.Clamp(Mathf.DeltaAngle(animator.transform.eulerAngles.y, transform.eulerAngles.y), -limit, limit);
        input.SetValue(FPSANames.MouseInput, new Vector4(twist, pitchDown));
        input.SetValue(FPSANames.TurnOffset, twist);
        input.SetValue(FPSANames.MouseDeltaInput, new Vector4(delta.x, delta.y));

        // Q/E: FPSAF's roll tips the top to the left for a positive angle, as our view's roll does.
        float share = movement != null && movement.Abilities != null ? movement.Abilities.Lean.Share : 0f;
        // As far as takes the eyes the lean's distance on this body, as FPSAF's Look shares the tip down the spine for this
        // gun (PlayerBody.LeanRollFor); the demo's fixed 25° carried them about 0.25 m of the 0.35 m peek (2026-11-08).
        float roll = Mathf.Abs(share) > .001f && held != null && body != null
            ? Mathf.Sign(share) * body.LeanRollFor(Mathf.Abs(movement.LeanOffset), held.LeanChain) : 0f;
        input.SetValue(FPSANames.LeanInput, -roll);
        float sign = Mathf.Abs(share) > .05f ? Mathf.Sign(share) : 0f;
        if (sign != leanSign) { leanSign = sign; Motion(assets.leanMotion); }

        // Sprinting the spine lets the view go and the upper body is no longer held steady (FPSController.OnSprintStarted).
        float sprint = movement != null ? movement.SprintProgress : 0f;
        bool grounded = movement == null || movement.IsGrounded;
        bool sprinting = grounded && sprint >= sprintFrom && speed > walk;
        input.SetValue(FPSANames.StabilizationWeight, sprinting ? 0f : 1f);
        input.SetValue("LookLayerWeight", (sprinting ? sprintLookWeight : 1f) * Framework);
        input.SetValue(FPSANames.PlayablesWeight, 1f);
        float follow = 1f - Mathf.Exp(-dt / weightTime);
        sprintWeight = Mathf.Lerp(sprintWeight, sprinting ? 1f : 0f, follow);

        var state = movement != null ? movement.CurrentState : MovementState.Standing;
        bool crouched = state == MovementState.Crouching;
        crouchWeight = Mathf.Lerp(crouchWeight, crouched ? 1f : 0f, follow);
        if (crouched != wasCrouched) { wasCrouched = crouched; Motion(assets.crouchingMotion); }
        if (grounded && !wasGrounded) Motion(assets.jumpingMotion);
        wasGrounded = grounded;
        bool moving = speed >= StillSpeed;
        if (wasMoving && !moving) Motion(assets.stopMotion);
        wasMoving = moving;

        // The Animator parameters FPSAF's profiles read. The demo's full-body actions take the upper body from FPSAF by
        // FullBodyWeight; ours are the climbs, hangs and rungs where the body's hands leave the gun.
        animator.SetFloat(CrouchWeightId, crouchWeight);
        animator.SetFloat(SprintPoseWeightId, sprintWeight);
        animator.SetFloat(VelocityId, speed);
        animator.SetFloat(FullBodyWeightId, 1f - Framework);

        if (!HoldsGun) return;
        float aim = controller.AimBlend;
        bool wantsAim = aim >= .999f || aim > lastAim;
        lastAim = aim;
        if (wantsAim != aiming)
        {
            aiming = wantsAim;
            held.Aim(aiming, input, recoil);
            Motion(assets.aimingMotion);
        }
    }

    // The body's fit scale (PlayerBody scales the model to the camera height); the grip was measured at scale 1.
    private float ModelScale => animator != null ? Mathf.Abs(animator.transform.lossyScale.x) : 1f;

    // How much the body's right hand holds the gun (PlayerBodyHands, eased by the movement state).
    private float GunShare => hands != null ? Mathf.Clamp01(hands.GunWeight) : 1f;
    // How much both hands hold it.
    private float BothHands => hands != null ? Mathf.Clamp01(hands.BothHandsOnGun) : 1f;
    // FPSAF's share of the upper body: both hands on the gun, standing or crouched (PlayerBodyHands.FrameworkShare).
    private float Framework => hands != null ? Mathf.Clamp01(hands.FrameworkShare) : 1f;

    // How much of a hand's hold on the gun is the move's clip's rather than FPSAF's (0-1), once FPSAF's hold is known.
    private float Released(bool isLeft)
    {
        float hold = isLeft ? BothHands : GunShare;
        return HoldsGun && hasGunInView && hold > .001f ? Mathf.Clamp01(1f - Framework / hold) : 0f;
    }

    /// <summary>Where a hand holds the gun as it sits now, as FPSAF last held it (PlayerBodyHands puts the hand there).</summary>
    internal bool HandOnGun(bool isLeft, out Vector3 position, out Quaternion rotation)
    {
        position = Vector3.zero;
        rotation = Quaternion.identity;
        if (!HoldsGun || !hasGunInView || rig == null) return false;
        var gun = rig.IkWeaponBone;
        var grip = isLeft ? leftHandInGun : rightHandInGun;
        position = gun.TransformPoint(grip.position);
        rotation = gun.rotation * grip.rotation;
        return true;
    }

    // After the body's own hands (PlayerBodyHands, 320): where they took the right hand off FPSAF's pose (a ledge, a
    // rung), the gun goes with it, held as FPSAF holds it in that hand.
    private void AfterHands()
    {
        if (!HoldsGun) return;
        float share = GunShare;
        if (share < .999f) held.FollowHand(rig.WeaponBoneRight, rig.IkWeaponBone, 1f - share);
    }

    private void Motion(IkMotionLayerSettings motion)
    {
        if (motion != null && HoldsGun) framework.LinkAnimatorLayer(motion);
    }

    // This frame's view pitch (down-positive) and turn in degrees. Someone else's view turns in network steps, which
    // would only jolt FPSAF's sway, so theirs turns nothing.
    private void LookDelta(out float pitchDown, out Vector2 delta)
    {
        Transform frame = lookFrame != null ? lookFrame : eyes;
        pitchDown = 0f;
        delta = Vector2.zero;
        if (frame == null) return;
        Vector3 forward = frame.forward;
        float yaw = Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
        pitchDown = -Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
        bool own = movement == null || (movement.IsLocal && !movement.IsNetworkDriven);
        if (hasLook && own) delta = new Vector2(Mathf.DeltaAngle(lastYaw, yaw), pitchDown - lastPitch);
        lastYaw = yaw;
        lastPitch = pitchDown;
        hasLook = true;
    }

    private void OnJumped() => Motion(assets.jumpingMotion);

    // The body placed the camera this frame (PlayerBody.Look), so FPSAF's shake and gun motion go on a fresh pose.
    private bool CameraPlaced() => body != null && !body.IsLimp && !body.IsDead;

    // After the body has been posed and before the weapon fires from its muzzle (300).
    private void LateUpdate()
    {
        if (!HoldsGun) return;
        KeepGunInView();
        RideGun();
        Probe();
        if (firing && Time.time - lastShot > shotInterval * TriggerReleaseIntervals) StopFiring();
        if (held.Reloading && (controller.Runtime == null || !controller.Runtime.IsReloading)) held.StopReload(playables, reloadBlendOut, bodyCamera);
    }

    // While FPSAF holds the upper body, how it holds the gun is learnt: where the gun sits from the eyes (which the camera
    // rides and which stay put in the F5 view), at the hip and fully aimed, both hands on the gun and their fingers. When it
    // gives the body to the move's clip, the gun stays there as the eyes move (aimed as far as the player aims), the hands
    // are put back on it (PlayerBodyHands) and their fingers keep their grip, as the gun was held before KINEMATION. Before
    // the hands (320) and after the camera is placed (PlayerBody, 250).
    private void KeepGunInView()
    {
        var gun = rig != null ? rig.IkWeaponBone : null;
        var right = rig != null ? rig.RightHand : null;
        var left = animator != null ? animator.GetBoneTransform(HumanBodyBones.LeftHand) : null;
        if (gun == null || right == null || left == null || eyes == null) return;
        float aim = controller.AimBlend;
        if (Framework >= .999f)
        {
            // Mid-reload the hands are on the magazine and the bolt, not the grips.
            if (held.Reloading) return;
            var inView = new KTransform(eyes.InverseTransformPoint(gun.position), Quaternion.Inverse(eyes.rotation) * gun.rotation, Vector3.one);
            if (aim <= .001f) { gunAtHip = inView; hasGunInView = true; }
            else if (aim >= .999f) { gunAimed = inView; hasGunAimed = true; }
            rightHandInGun = new KTransform(gun.InverseTransformPoint(right.position), Quaternion.Inverse(gun.rotation) * right.rotation, Vector3.one);
            leftHandInGun = new KTransform(gun.InverseTransformPoint(left.position), Quaternion.Inverse(gun.rotation) * left.rotation, Vector3.one);
            LearnFingers();
            return;
        }
        float released = Released(false);
        if (released <= 0f) return;
        var kept = hasGunAimed ? KTransform.Lerp(gunAtHip, gunAimed, aim) : gunAtHip;
        gun.SetPositionAndRotation(Vector3.Lerp(gun.position, eyes.TransformPoint(kept.position), released),
            Quaternion.Slerp(gun.rotation, eyes.rotation * kept.rotation, released));
        float leftReleased = Released(true);
        for (int i = 0; i < fingers.Count; i++)
            fingers[i].bone.localRotation = Quaternion.Slerp(fingers[i].bone.localRotation, fingerGrip[i], fingers[i].left ? leftReleased : released);
    }

    private void LearnFingers()
    {
        if (fingers.Count == 0)
        {
            for (var bone = HumanBodyBones.LeftThumbProximal; bone <= HumanBodyBones.LeftLittleDistal; bone++)
                if (animator.GetBoneTransform(bone) is Transform finger) fingers.Add((finger, true));
            for (var bone = HumanBodyBones.RightThumbProximal; bone <= HumanBodyBones.RightLittleDistal; bone++)
                if (animator.GetBoneTransform(bone) is Transform finger) fingers.Add((finger, false));
        }
        fingerGrip.Clear();
        foreach (var finger in fingers) fingerGrip.Add(finger.bone.localRotation);
    }

    // Our gun root and muzzle follow the gun in the body's hands: the root on the gun with the sight line forward, the
    // muzzle on the gun's measured muzzle. The muzzle flash, light, tracer and shots start there.
    private void RideGun()
    {
        var gun = held.transform;
        var aimPoint = held.AimPoint;
        Quaternion forward = aimPoint != null ? Quaternion.LookRotation(aimPoint.forward, aimPoint.up) : gun.rotation;
        weaponRoot.SetPositionAndRotation(gun.position, forward);
        if (muzzle == null) return;
        muzzle.SetPositionAndRotation(gun.TransformPoint(held.MuzzlePosition), gun.rotation * held.MuzzleRotation);
        LearnHipMuzzle();
    }

    // Where the muzzle is when the gun is held clean at the hip, from the eyes (which stay put in the F5 view), for the wall
    // rule's probes (Prepare): learnt only with FPSAF holding the whole gun at rest, the draw's motion over, no wall holding
    // it back and no reload, and only with the muzzle ahead of the eyes. Kept per gun across draws. The probes once read the
    // gun's own muzzle until the first clean frame after a draw: a gun caught at the draw was raised and swung aside, and
    // never learnt again while raised, so it stayed raised and couldn't fire (user, 2026-11-07).
    private void LearnHipMuzzle()
    {
        if (eyes == null || highReady || cover > 0f || held.Reloading || Framework < .999f || controller.AimBlend > .001f
            || Time.time - drawnAt < drawSettle) return;
        Vector3 fromEyes = muzzle.position - eyes.position;
        if (Vector3.Angle(eyes.forward, fromEyes) > HipMuzzleCone) return;
        held.HipMuzzle = eyes.InverseTransformPoint(muzzle.position);
        held.HasHipMuzzle = true;
    }

    // The support hand under the handguard: the bake left the wrist about 5 cm to the side of the gun's line (probe,
    // 2026-11-08), so the hand sat open beside the gun, and a reload's end went back to that. With FPSAF holding the gun
    // and no reload, the wrist moves onto the line under the bore (same height, same place along it, same turn), the arm
    // bending to it; a reload lets go and it comes back over leftGripEase.
    private float leftGripWeight;
    private const float LeftGripEase = .25f;

    private void LeftHandUnderGun(float dt)
    {
        bool grip = Framework >= .999f && !held.Reloading && held.AimPoint != null;
        leftGripWeight = Mathf.MoveTowards(leftGripWeight, grip ? 1f : 0f, dt / LeftGripEase);
        var upper = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
        var lower = animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        var hand = animator.GetBoneTransform(HumanBodyBones.LeftHand);
        if (leftGripWeight <= 0f || upper == null || lower == null || hand == null || muzzle == null) return;
        var aim = held.AimPoint;
        Vector3 along = aim.forward, right = Vector3.Cross(aim.up, along).normalized;
        float side = Vector3.Dot(hand.position - muzzle.position, right);
        Vector3 target = hand.position - right * side * Mathf.SmoothStep(0f, 1f, leftGripWeight);
        Quaternion turn = hand.rotation;
        Vector3 a = upper.position, b = lower.position, c = hand.position;
        float ab = (b - a).magnitude, bc = (c - b).magnitude;
        float reach = Mathf.Clamp((target - a).magnitude, Mathf.Abs(ab - bc) + .001f, ab + bc - .001f);
        Vector3 axis = Vector3.Cross(a - b, c - b);
        if (axis.sqrMagnitude > 1e-8f)
        {
            float now = Vector3.Angle(a - b, c - b);
            float want = Mathf.Acos(Mathf.Clamp((ab * ab + bc * bc - reach * reach) / (2f * ab * bc), -1f, 1f)) * Mathf.Rad2Deg;
            lower.rotation = Quaternion.AngleAxis(want - now, axis.normalized) * lower.rotation;
        }
        upper.rotation = Quaternion.FromToRotation(hand.position - a, target - a) * upper.rotation;
        hand.rotation = turn;
    }

    /// <summary>A reload is playing (its own fingers hold the magazine and bolt).</summary>
    internal bool ReloadingGun => HoldsGun && held.Reloading;

    /// <summary>
    /// The held gun's real surface while FPSAF holds it: what the hands rest on and the fingers close round, each hand
    /// by how far it is on its grip (GripHold). Null otherwise. It once waited for the draw's motion to end and for no
    /// reload, so the hands sat off the gun through the draw and the reload and snapped onto it after (user, 2026-11-08).
    /// </summary>
    internal GunSurface HeldSurface
        => HoldsGun && Framework >= .999f && held.Surface != null && !held.Surface.Empty ? held.Surface : null;

    // A hand is on its grip within this of where the bake had it, and off it beyond that (m, at model scale 1).
    private const float GripNear = .06f, GripFar = .12f;

    /// <summary>
    /// How far a hand is on its grip (0-1): by how far it is from where the bake had it on the gun as the gun sits now,
    /// on it within GripNear, off it beyond GripFar (a reload's hand on the magazine or the bolt), between in between.
    /// The draw moves the gun and both hands together, so they count as on it from its first frame. A bake with no
    /// measured grip counts a hand on it except in a reload.
    /// </summary>
    internal float GripHold(bool isLeft)
    {
        if (!HoldsGun) return 0f;
        float fallback = held.Reloading ? 0f : 1f;
        if (!held.HasGrip || rig == null || animator == null) return fallback;
        held.GripGaps(animator.GetBoneTransform(HumanBodyBones.RightHand), animator.GetBoneTransform(HumanBodyBones.LeftHand),
            rig.IkWeaponBone, ModelScale, out float rightGap, out float leftGap);
        float gap = isLeft ? leftGap : rightGap;
        if (gap < 0f) return fallback;
        float scale = Mathf.Max(.01f, ModelScale);
        return 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(GripNear * scale, GripFar * scale, gap));
    }

    // How long FPSAF's IK motion plays (s).
    private static float LengthOf(IkMotionLayerSettings motion)
        => motion == null ? 0f
            : Mathf.Max(motion.rotationCurves.GetCurveLength(), motion.translationCurves.GetCurveLength()) / Mathf.Max(.01f, motion.playRate);

    private void OnReloadStarted(bool empty)
    {
        if (!HoldsGun) return;
        StopFiring();
        StartReloadProbe(held.Reload(empty, playables, bodyCamera));
    }

    private void OnReloadCompleted()
    {
        if (held != null) held.ReloadDone();
    }

    private void StopFiring()
    {
        if (!firing) return;
        firing = false;
        if (held != null) held.StopFire(recoil);
    }

    private void Report(string message)
    {
        if (reported.Add(message)) Debug.LogWarning("FPSBody: " + message, this);
    }

    // ---- The gun's pose for WeaponController (IWeaponPose) ----

    public bool IsHighReady => highReady;

    // Our gun root (the muzzle under it) is on while a gun of ours is out: WeaponController.ShowsWeapon reads it, and with
    // it the body's hands (the gun hold FPSAF's upper body is weighed by), the armed clips and the HUD's gun anchor. Off,
    // FPSAF's layer stayed at no weight and the body walked with empty arms beside the gun (probe, 2026-11-07).
    public void SetVisible(bool visible)
    {
        shown = visible;
        bool rootOn = visible && held != null;
        if (weaponRoot != null && weaponRoot.gameObject.activeSelf != rootOn) weaponRoot.gameObject.SetActive(rootOn);
        if (held == null || framework == null) return;
        if (visible) Draw();
        else PutAway();
    }

    public void Reset()
    {
        highReady = false;
        cover = 0f;
        if (input != null) input.SetValue(CoverProperty, 0f);
        StopFiring();
    }

    public void OnShot(WeaponData data, Vector2 shotRecoil)
    {
        if (!HoldsGun) return;
        firing = true;
        lastShot = Time.time;
        shotInterval = data.ShotInterval;
        held.Fire(controller.LastShot.AmmoAfterShot == 0, recoil, bodyCamera);
    }

    // The wall rule probes the gun as held at the hip, never the gun as it is (raised, swung aside or on its way in): the
    // learnt hip muzzle, or before it is learnt one straight ahead of the eyes at the gun's own length (bone to muzzle).
    public Vector3 Prepare(Transform probeView, WeaponData data, float ads)
    {
        if (!HoldsGun || eyes == null) return muzzle != null ? muzzle.position : probeView.position;
        if (held.HasHipMuzzle) return eyes.TransformPoint(held.HipMuzzle);
        return eyes.position + eyes.forward * held.MuzzlePosition.magnitude;
    }

    // Our wall rule says whether the gun may fire and how far it is held back, eased at the weapon's high-ready speeds;
    // FPSAF's blocking pose shows it (GunCoverLayer). The gun counts as held up until it is nearly back, so it can't
    // fire on the way down.
    public bool Pose(GameObject owner, Transform poseView, Vector3 target, bool obstructed, WeaponRuntime runtime, float dt, int layers)
    {
        float halfLife = obstructed ? runtime.Data.HighReadyRaiseHalfLife : runtime.Data.HighReadyLowerHalfLife;
        cover = Mathf.Lerp(cover, obstructed ? 1f : 0f, 1f - Mathf.Exp(-Mathf.Log(2f) * Mathf.Max(0f, dt) / Mathf.Max(.001f, halfLife)));
        if (!obstructed && cover < .001f) cover = 0f;
        if (input != null) input.SetValue(CoverProperty, cover);
        highReady = obstructed || cover > RaisedFiring;
        return highReady;
    }
}
