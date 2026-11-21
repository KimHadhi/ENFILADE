using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// The player's own visible body for true first person. The PlayerBody profile's character stands under the
/// code-driven camera, scaled so its eyes meet the camera, and plays clips chosen from the movement state.
/// Its eyes also carry the camera (PlayerBody.Look); F5 toggles a view from behind to check the body.
/// The clips play in the Animator's own graph as its first output, the way KINEMATION's FPS Animation Framework expects the
/// Animator output to be: the body's Animator gets an empty Animator Controller (Characters/BodyHost) for the parameters
/// it reads, and FPSAF (FPSBody) runs on top of the clips while a gun is held. Everything after the animation pass is
/// ours, in the order LateUpdate lists it, starting with the look bend (SpineLook).
/// Presentation only: it never moves the player. Clip names and feel come from Tables/Player.csv, row Body.
/// Runs after the camera effects and before the weapons, so shots and the gun start from where the eyes are.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.Body)]
public sealed partial class PlayerBody : MonoBehaviour, ITableBound
{
    [Header("Clips (names in the PlayerBody profile)")]
    [SerializeField] private string idle = "mx_idle";
    // The walk and run cycles of every stance are rows of Tables/Player/BodyMoves.csv (PlayerBody.Locomotion), each going the
    // way its feet were measured to go; idle and crouchIdle are the plain and crouched sets' idles.
    [SerializeField] private string crouchIdle = "crouchstill";
    [SerializeField] private string jump = "jumpfast", air = "jumpidle", fall = "mx_fall";
    [SerializeField] private string land = "mx_fall_land";
    [SerializeField] private string slideStart = "slidestart", slide = "slideloop", slideEnd = "slideend";
    [SerializeField, Min(.01f), Tooltip("A slide that ends in a crouch goes from the slide's pose into the crouch this slowly (s). "
        + "No pack has a clip for it; the slide's getting up (slideEnd) stood the body up first.")]
    private float slideCrouchTime = .3f;
    [SerializeField, Min(0f), Tooltip("A slide that stops on the spot plays its end (slideEnd, which gets up and runs on) only this "
        + "far: up off the ground and facing ahead, before its first running step (s).")]
    private float slideEndUp = .4f;
    [SerializeField, Min(.01f), Tooltip("A clip that ends running, given up because the body has stopped, holds its pose and the "
        + "standing takes over this slowly (s).")]
    private float standSettleTime = .3f;
    [SerializeField] private string diveStart = "divestart", dive = "diveidle";
    [SerializeField, Tooltip("A dive on the back, as Beatrun's jump turn: lying back in the air, then its loop. Turned over "
        + "from a front dive, or off an edge while lying, the body is already down and goes straight to the loop.")]
    private string supineDiveStart = "jumpturnfly", supineDive = "jumpturnflyidle";
    [SerializeField] private string bellySlideStart = "diveslidestart", bellySlide = "diveslideidle", bellySlideEnd = "diveslideend";
    [SerializeField] private string supineSlideStart = "meslidestart", supineSlide = "meslideloop", supineSlideEnd = "meslideend";
    [SerializeField] private string prone = "diveslideidle", supine = "jumpturnlandidle", roll = "mx_sprint_roll";
    [SerializeField] private string dodgeLeft = "dodgejumpleft", dodgeRight = "dodgejumpright", dashPush = "bargein";
    [SerializeField] private string vault = "vaultover", mantle = "climb1", wallClimb = "wallrunvertical", wallKick = "jumpwrleft";
    [SerializeField] private string barSwing = "swing", barRelease = "swingjumpoff", ledgeHang = "mx_ledge_hang_wall", ledgeJump = "hangheaveup";
    [SerializeField] private string death = "diestand";

    [Header("Feel")]
    [SerializeField, Min(.01f), Tooltip("Crossfade between clips (s).")]
    private float blendTime = .15f;
    // A while (slowBlendLeft, s) when the clips cross over this slowly instead (slowBlendTime, s): standing up from the
    // crouch a climb ends in, going from a slide into a crouch (SlowBlend).
    private float slowBlendTime, slowBlendLeft;
    [SerializeField, Min(.1f), Tooltip("Faster than this the player counts as moving off (m/s): a landing or a slide's end stops "
        + "playing out, and a landing from a tuck takes its running version. The cycles' own paces are measured (GaitClips).")]
    private float walkClipSpeed = 1.7f;
    [SerializeField, Min(0f), Tooltip("The head sits this far behind the camera so looking down never shows the inside of the neck (m).")]
    private float backOffset = .12f;
    [SerializeField, Min(.01f), Tooltip("How quickly the body slides to keep its head under the camera (s). Slower than a stride, so running sway stays.")]
    private float headFollowTime = .25f;
    [SerializeField, Min(0f), Tooltip("Falling faster than this plays the fast-fall clip (m/s).")]
    private float fallClipSpeed = 7.2f;
    [SerializeField, Tooltip("F5 view from behind, in look-pivot space (m).")]
    private Vector3 thirdPersonOffset = new Vector3(.35f, .2f, -2.4f);

    private sealed class Channel
    {
        public AnimationClipPlayable Playable;
        public int Input;
        public float Length, AuthoredSpeed, Weight, Target;
        public bool Synced;
        /// <summary>A walk or run cycle's measured gait on this body (GaitClips), or null.</summary>
        public GaitClips.Gait Gait;
    }

    private readonly List<Channel> channels = new List<Channel>();
    private readonly Dictionary<string, Channel> byName = new Dictionary<string, Channel>(StringComparer.OrdinalIgnoreCase);
    private readonly List<Renderer> headParts = new List<Renderer>();
    // Worn over the face (a mask, glasses): only their shadows in first person, whatever the head setting says.
    private readonly List<Renderer> faceParts = new List<Renderer>();
    private readonly HashSet<string> reported = new HashSet<string>();
    private OutfitDresser.Dressing dressing;
    private string loadedOutfit;
    private PlayerMovement player;
    private PlayerBodyProfile profile;
    private GameObject loadedModel;
    private PlayerBodyProfile.Skin loadedSkin;
    private SkinVariant loadedVariant;
    private string shownSkin;
    private Transform body, view, head, hips;
    private Animator animator;
    private Vector3 headAlignment;
    // How far the body's root stands under the capsule's floor (m; under zero, over it): the height of the soles it
    // draws over its model's floor, at its scale (StandOnSoles).
    private float soleLift;
    // The soles of the look the model comes in, its own shoes (model units, measured before dressing), which set the
    // body's size; and how far the worn shoes then put the eyes over the camera's height (m, PlayerMovement.EyeRise).
    private float ownSole, eyeRise;
    private PlayableGraph graph;
    private AnimationMixerPlayable mixer;
    private Channel transition;
    private MovementState state;
    private string dodgeClip;
    private float transitionLeft, stateTime, airTime, phase, yaw, yawVelocity, targetYaw;
    // Strides taken so far, for cycles holding more than one stride (GaitClips.Gait.ShareAt); phase is within the stride.
    private int strideCount;
    private bool started, grounded, jumped, dead, thirdPerson, rebuild, playingOut, springing;
    private bool supineDiveAtOnce, slidesOnBack;
    private Vector3 lastHeadInBody;
    private int lastHeadFrame = -1;

    public bool IsThirdPerson => thirdPerson;
    /// <summary>The barge clip is playing: a dash push under way, or one that stopped on someone playing out.</summary>
    public bool PlaysDashPush => state == MovementState.DashPush || (transition != null && transition == Find(dashPush));
    internal Animator BodyAnimator => animator;
    /// <summary>The model the body was made from (the current skin's), which KINEMATION's gun clips were remade for (GunBakes).</summary>
    internal GameObject BodyModel => loadedModel;
    /// <summary>FPS Animation Framework holds a gun and poses the upper body (FPSBody): its Look layer bends, twists and leans it.</summary>
    internal bool UpperBodyFromFramework { get; set; }
    internal Transform View => view;
    internal bool IsDead => dead;
    /// <summary>Where the eyes are this frame, after the spine has bent with the view.</summary>
    internal Vector3 EyePosition => head != null ? head.TransformPoint(eyeLocal) : transform.position;
    internal PlayerBodyProfile.Skin Skin => loadedSkin;
    // A copy of someone else wears what they chose, whatever this PC's own setting says.
    private PlayerBodyProfile.Skin ChosenSkin
    {
        get
        {
            if (shownSkin == null) return profile.CurrentSkin;
            PlayerBodyProfile.SplitLook(shownSkin, out string skinId, out _);
            return profile.SkinOf(skinId);
        }
    }
    private SkinVariant ChosenVariant
    {
        get
        {
            if (shownSkin == null) return profile.CurrentVariant;
            PlayerBodyProfile.SplitLook(shownSkin, out string skinId, out string variantId);
            return profile.SkinOf(skinId)?.VariantOf(variantId);
        }
    }

    // The outfit in its slots: what this copy's player sent after their skin, or this PC's own saved one for the character.
    private string ChosenOutfit => shownSkin == null ? OutfitLook.SavedText(ChosenSkin?.Id) : OutfitLook.OutfitPart(shownSkin);
    private bool WearsChosenOutfit => OutfitLook.Tidy(ChosenOutfit) == (loadedOutfit ?? "");

    /// <summary>
    /// Whether the body has the outfit item or model with this id on (OutfitDresser): their rows in Cloth.csv swing their
    /// loose parts.
    /// </summary>
    internal bool Wears(string id) => dressing != null && (dressing.Items.Contains(id ?? "") || dressing.Models.Contains(id ?? ""));
    /// <summary>What dressing put on this body: the hub's mirror marks a wardrobe slot's pieces on it (HubDressing).</summary>
    internal OutfitDresser.Dressing Dressing => dressing;

    /// <summary>
    /// Wears the skin, colour set and outfit the player of this copy chose on their own PC (PlayerBodyProfile.LookId,
    /// OutfitLook.Join). Another outfit makes the body again, as another skin does: items are joined to the bones in the
    /// pose the model was made in.
    /// </summary>
    internal void ShowSkin(string look)
    {
        shownSkin = look;
        if (!started) return;
        if (profile == null || ChosenSkin != loadedSkin || !WearsChosenOutfit) rebuild = true;
        else WearChosenVariant();
    }

    // Another colour set of the same skin only swaps materials: the body, its pose and its cloth stay.
    private void WearChosenVariant()
    {
        var next = ChosenVariant;
        if (body == null || next == loadedVariant) return;
        SkinVariant.Wear(body.gameObject, loadedVariant, next);
        loadedVariant = next;
    }

    private void Start() { started = true; Build(); }
    private void OnEnable() { if (started) rebuild = true; }
    private void OnDisable() => Teardown();
    private void OnDestroy() => Teardown();

    // Clip names or feel may have changed in the table: rebuild the graph on the next frame.
    void ITableBound.OnTableApplied() => rebuild = started;

    private void Build()
    {
        bool wasThirdPerson = thirdPerson;
        Teardown();
        thirdPerson = wasThirdPerson;
        rebuild = false;
        player = GetComponent<PlayerMovement>();
        profile = PlayerBodyProfile.Load();
        if (player == null || profile == null) { enabled = false; return; }
        bodyCamera.Find(player, transform);
        view = bodyCamera.View;

        loadedSkin = ChosenSkin;
        loadedModel = profile.ModelOf(loadedSkin);
        body = Instantiate(loadedModel, transform, false).transform;
        body.name = "Body (" + loadedModel.name + ")";
        loadedVariant = ChosenVariant;
        SkinVariant.Wear(body.gameObject, null, loadedVariant);
        animator = body.GetComponentInChildren<Animator>();
        if (animator == null) animator = body.gameObject.AddComponent<Animator>();
        if (animator.avatar == null || !animator.avatar.isHuman)
            Report(loadedModel.name + " has no humanoid avatar; set Rig → Animation Type to Humanoid.");
        animator.runtimeAnimatorController = Host;
        animator.applyRootMotion = false;
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        var headBone = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        foreach (var renderer in body.GetComponentsInChildren<Renderer>(true))
        {
            bool onHead = false;
            if (renderer is SkinnedMeshRenderer skinned)
            {
                skinned.updateWhenOffscreen = true;
                onHead = HeadMeshes.OnHead(skinned, headBone);
            }
            foreach (string part in profile.FirstPersonShadowOnly)
                onHead |= string.Equals(renderer.name, part, StringComparison.OrdinalIgnoreCase);
            if (onHead) headParts.Add(renderer);
        }
        float fit = FitScale();
        body.localScale = Vector3.one * fit;
        // Before dressing: the look the model comes in, standing on its own shoes, is the one sized to the camera.
        ownSole = DrawnSole.Of(body);
        // Dressed after the fit, so a tall helmet never makes the body that wears it smaller, and before the feet, hands
        // and cloth read the model, so they find the items' bones on it.
        var outfit = OutfitLook.Parse(ChosenOutfit);
        dressing = OutfitDresser.Dress(body.gameObject, loadedModel, loadedSkin, outfit);
        loadedOutfit = outfit.ToString();
        StandOnSoles(fit);
        // At the size it stands at, before any clip, as the soles are.
        MeasureHeels();
        // The face once dressed, so a mask's keys that follow the face's move with it (PlayerBody.Face).
        WearFace();
        headParts.AddRange(dressing.HeadParts);
        faceParts.AddRange(dressing.FaceParts);
        legYaw = transform.eulerAngles.y;
        head = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        headAlignment = new Vector3(0f, -soleLift, -backOffset);
        body.localPosition = headAlignment;
        SetUpLook();
        SetUpFeet();
        BindFeetClearance();
        hands = GetComponent<PlayerBodyHands>();
        hands.OrNull()?.BindAnimator(animator);
        GameSettings.Changed += OnSettingsChanged;

        graph = HostGraph();
        if (!graph.IsValid())
        {
            Report($"{loadedModel.name}'s Animator made no graph for the body's clips.");
            enabled = false;
            return;
        }
        mixer = AnimationMixerPlayable.Create(graph, 0);
        bodyOutput = (AnimationPlayableOutput)graph.GetOutputByType<AnimationPlayableOutput>(0);
        bodyOutput.SetSourcePlayable(BuildLayers());
        foreach (string clip in new[] { idle, crouchIdle, jump, air, fall, land, slideStart, slide, slideEnd,
                     diveStart, dive, supineDiveStart, supineDive, bellySlideStart, bellySlide, bellySlideEnd,
                     supineSlideStart, supineSlide, supineSlideEnd,
                     prone, supine, roll, dodgeLeft, dodgeRight, dashPush,
                     vault, mantle, wallClimb, wallKick, barSwing, barRelease, ledgeHang, ledgeJump, death })
            AddChannel(clip, 0f);
        // The rolls, dodges, jumps and landings by the way they go (PlayerBody.Ways).
        foreach (string clip in WayClips()) AddChannel(clip, 0f);
        foreach (string clip in VariantClips) AddChannel(clip, 0f);
        AddHangChannels();
        foreach (string clip in FixtureClips) AddChannel(clip, 0f);
        BindHops();
        AddArmedChannels();
        AddLocomotionChannels();
        SetUpAnimationViewChannels();
        leftHand = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.LeftHand) : null;
        rightHand = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.RightHand) : null;

        state = player.CurrentState;
        grounded = player.IsGrounded;
        stateTime = airTime = gripWeight = slowBlendLeft = 0f;
        heaving = false;
        heaveClip = null;
        hips = animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Hips) : null;
        jumpClip = jump; obstacleClip = vault; wallKickClip = wallKick; rollClip = roll; downClip = heavyLand; tucked = flailing = false;
        var rest = Find(idle);
        if (rest != null) rest.Weight = rest.Target = 1f;
        ApplyView();
        // How the vault clips' hands meet an obstacle, measured now rather than on the first vault (PlayerBody.ObstacleMotion).
        MeasureObstacleClips();
    }

    private static RuntimeAnimatorController host;
    // The empty Animator Controller the body's Animator takes, so the Animator makes its graph (KINEMATION's frameworks run
    // in it) and holds the parameters they read.
    private static RuntimeAnimatorController Host => host != null ? host : host = Resources.Load<RuntimeAnimatorController>("Characters/BodyHost");

    // The Animator's own graph, made now if the Animator has not made it yet.
    private PlayableGraph HostGraph()
    {
        if (!animator.playableGraph.IsValid())
        {
            animator.Rebind();
            if (!animator.playableGraph.IsValid()) animator.Update(0f);
        }
        var made = animator.playableGraph;
        if (made.IsValid()) made.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
        return made;
    }

    // The Animator's own output, which plays the body's clips under everything KINEMATION adds: the first in its graph as
    // the body is made, and held as that output from then on. FPSAF adds its outputs to the same graph, so "the graph's
    // first", looked up each time, would be one of FPSAF's if the Animator ever made its own anew; this one then reads as
    // gone instead, and nothing is done to it.
    private AnimationPlayableOutput bodyOutput;

    /// <summary>The body's clips (its layers), which FPS Animation Framework plays as its base layer (FPSBodyPlayables).</summary>
    internal Playable Clips => layers;

    /// <summary>
    /// FPS Animation Framework has set up on the body and taken its clips as its base layer (FPSBodyPlayables): the
    /// Animator's own output steps back at once (PlayClipsInOnePlace). Says so if FPSAF has not got them, with what stands
    /// in the graph, for a build's log.
    /// </summary>
    internal void HandClipsToFramework()
    {
        if (!graph.IsValid() || !layers.IsValid()) return;
        if (PlayClipsInOnePlace()) return;
        var output = bodyOutput;
        var source = output.IsOutputValid() ? output.GetSourcePlayable() : Playable.Null;
        string plays = !output.IsOutputValid() ? "is gone" : !source.IsValid() ? "plays nothing"
            : source.Equals(layers) ? "plays the clips" : "plays " + source.GetPlayableType().Name;
        Report("FPS Animation Framework did not take the body's clips; its gun pose may not show. The Animator's own output "
            + $"{plays}; its graph is {(animator.playableGraph.Equals(graph) ? "the clips' own" : "another than the clips'")}.");
    }

    /// <summary>
    /// The body's clips play in one place at a time: FPS Animation Framework's base layer while it has them (the Animator's
    /// own output then lets go of them and steps back to no weight: both outputs start from the default pose, so whichever
    /// the Animator played last would otherwise wipe out the other, FPSAF's gun pose with it), and the Animator's own
    /// output at full weight while nothing else has them. Kept every frame, because both sides change under the body: FPSAF
    /// takes the clips when it sets up and again when the Animator comes back on, and the Animator puts its own controller
    /// back on its output when it sets itself up again. That second case had no answer: the output's weight was set once,
    /// when FPSAF set up, and a body whose clips nothing played stood in the pose of no clip at all, its hips at its feet.
    /// The user's build of 2026-11-15 showed it ("빌드 버전 보니까 나는 땅 속에 박혀서 머리만 바닥 위에 나와 있어"); its
    /// Player.log had one line, FPSAF not having taken the body's clips, which the editor never wrote. Read from that line
    /// and the code, not watched in a build: there the clips were off the Animator's output before FPSAF set up, and FPSAF
    /// took what was on it in their place. Returns whether FPSAF has them.
    /// </summary>
    private bool PlayClipsInOnePlace()
    {
        if (!graph.IsValid() || !layers.IsValid()) return false;
        bool taken = KeepClipsInFramework();
        var output = bodyOutput;
        if (!output.IsOutputValid()) return taken;
        if (!taken && !output.GetSourcePlayable().Equals(layers)) output.SetSourcePlayable(layers);
        float weight = taken ? 0f : 1f;
        if (output.GetWeight() != weight) output.SetWeight(weight);
        return taken;
    }

    /// <summary>
    /// Once FPS Animation Framework plays the body's clips (its master mixer has them), the Animator's own output lets go
    /// of them, so they play through one place only. Left in both, the same clips hung under two parents in the graph: the
    /// user found the body high over a vault, its hands waving in the air instead of on the obstacle, and sliding broken,
    /// none of which happened when the clips had one graph of their own (0.7.34). FPSAF takes the clips again when the
    /// Animator comes back on (ReattachGraph puts them on that output first), so this runs every frame (PlayClipsInOnePlace).
    /// Returns whether FPSAF has them.
    /// </summary>
    private bool KeepClipsInFramework()
    {
        if (!graph.IsValid() || !layers.IsValid()) return false;
        var output = bodyOutput;
        // With the Animator's own output gone there is nothing to let go of; FPSAF has the clips if anything plays them.
        var source = output.IsOutputValid() ? output.GetSourcePlayable() : Playable.Null;
        bool framework = false;
        for (int i = 0; i < layers.GetOutputCount(); i++)
        {
            var parent = layers.GetOutput(i);
            if (parent.IsValid() && (!source.IsValid() || !parent.Equals(source))) framework = true;
        }
        if (!framework || !source.IsValid()) return framework;
        if (Time.frameCount - reattachedFrame < ReattachFrames || !animator.isActiveAndEnabled) return true;
        if (source.Equals(layers))
        {
            output.SetSourcePlayable(Playable.Null);
            return true;
        }
        for (int i = 0; i < source.GetInputCount(); i++)
            if (source.GetInput(i).Equals(layers)) source.DisconnectInput(i);
        return true;
    }

    // The Animator back on after the body lay limp: its controller output plays the clips again. If the Animator made its
    // graph anew, the whole body is made again.
    private void ReattachGraph()
    {
        if (animator == null) return;
        if (!graph.IsValid() || !mixer.IsValid() || !animator.playableGraph.IsValid() || !animator.playableGraph.Equals(graph))
        {
            rebuild = true;
            return;
        }
        var output = bodyOutput;
        if (output.IsOutputValid() && !output.GetSourcePlayable().Equals(layers)) output.SetSourcePlayable(layers);
        // FPSAF takes them from there in its next update (FPSAnimator rebuilds once the Animator is back on).
        reattachedFrame = Time.frameCount;
    }

    // The frame the clips were put back on the Animator's own output; FPSAF needs them there for a few frames.
    private int reattachedFrame = -1000;
    private const int ReattachFrames = 3;

    private void Teardown()
    {
        GameSettings.Changed -= OnSettingsChanged;
        UnbindHops();
        // The graph is the Animator's; it goes with the body.
        graph = default;
        bodyOutput = default;
        spineLook.Clear();
        TeardownLayers();
        channels.Clear();
        byName.Clear();
        headParts.Clear();
        faceParts.Clear();
        dressing = null;
        loadedOutfit = null;
        transition = null;
        playingOut = false;
        if (body != null) Destroy(body.gameObject);
        body = head = hips = leftHand = rightHand = null;
        loadedModel = null;
        loadedSkin = null;
        loadedVariant = null;
        animator = null;
        headReach.Clear();
        bodyCamera.Release();
        // Without a body the camera rests at the capsule's own height again.
        eyeRise = 0f;
        if (player != null) player.EyeRise = 0f;
        // … and the feet stand in nothing in particular.
        heelHeight = 0f;
        if (TryGetComponent(out CharacterSounds sounds)) sounds.Footwear = null;
        thirdPerson = false;
    }

    private float FitScale() => FitScaleOf(animator, body, player.runtimeBodyDefaults.w);

    // Stands the dressed body on the soles it draws (DrawnSole) instead of its model's floor, where the clips and the feet
    // on the ground put the soles. BOOTH avatars stand on their own shoes' heels, so other shoes or bare feet floated over
    // the ground or sank into it, by up to 8 cm at the size the game draws them (the user, 2026-11-12: "하이힐 신었을 때랑
    // 그런 거 없을 때랑 … 바닥에서 떠 있다든지 그런 문제 발생해?"). The body goes down by that much (or up). Its size is set
    // once per character, by the look it comes in (ownSole) standing on its soles with its eyes at the camera's height, and
    // no shoe changes it: heels make the body taller and bare feet shorter, the eyes and the camera with them (EyeRise), as
    // the user asked ("몸 크기 그대로로 해서 힐 신으면 키 커졌으면"). 0.8.144 kept the eyes and rescaled the body instead.
    // The capsule stays as it is.
    private void StandOnSoles(float fit)
    {
        float camera = player.runtimeBodyDefaults.w;
        bool sized = SizeOnSoles(animator, camera, ownSole, ref fit);
        body.localScale = Vector3.one * fit;
        float sole = DrawnSole.Of(body);
        soleLift = sole * fit;
        eyeRise = sized ? (ownSole - sole) * fit : 0f;
        player.EyeRise = eyeRise;
    }

    // The fit that stands a body on the soles of the look it comes in (ownSole, model units over its floor) with its eyes at
    // the camera's height: the eyes' height the fit was made from is over the model's floor, the soles may not be. False
    // (and the fit as it was) for a body that can't be sized so.
    private static bool SizeOnSoles(Animator animator, float camera, float ownSole, ref float fit)
    {
        // The eyes' height over the model's floor that the fit was made from (model units).
        float eye = fit > 0f ? camera / fit : 0f;
        bool sized = animator != null && animator.isHuman && eye - ownSole > .1f;
        if (sized) fit = camera / (eye - ownSole);
        return sized;
    }

    /// <summary>
    /// The size the game draws <paramref name="model"/> at (a copy of a character's model as it comes, undressed and before
    /// any clip): its eyes at the standing camera height <paramref name="camera"/>, standing on its own soles, as a player's
    /// body is sized (Build, StandOnSoles). For measuring a character that is in nobody's hands: the body every gun's hold is
    /// shared from (FPSBody.ArmReach) and the editor's hold check.
    /// </summary>
    internal static float DrawnScaleOf(Animator animator, Transform model, float camera)
    {
        float fit = FitScaleOf(animator, model, camera);
        SizeOnSoles(animator, camera, DrawnSole.Of(model), ref fit);
        return fit;
    }

    /// <summary>
    /// The standing camera height the game sizes every body to (m), read off the player prefab (its CrouchPivot), for the
    /// editor's benches, which size bodies nobody plays (DrawnScaleOf): the gun, hold, healing-arm and wardrobe benches.
    /// </summary>
    internal static float StandingCamera()
    {
        var player = Resources.Load<GameObject>("Characters/Player");
        var pivot = player != null ? player.transform.Find("CrouchPivot") : null;
        return pivot != null ? pivot.localPosition.y : 1.6f;
    }

    /// <summary>
    /// The scale that brings <paramref name="model"/>'s eyes to the standing camera height <paramref name="camera"/>.
    /// Prefers mapped eyes; otherwise estimates them from the head and crown. The editor's gun bakes use it too
    /// (GunClipRetarget), so the clips are made for the body at the size it plays at.
    /// </summary>
    public static float FitScaleOf(Animator animator, Transform model, float camera)
    {
        var head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head == null || model == null) return 1f;
        float headY = model.InverseTransformPoint(head.position).y, top = headY;
        foreach (var renderer in model.GetComponentsInChildren<Renderer>())
            top = Mathf.Max(top, renderer.bounds.max.y - model.position.y);
        float eye = top > headY + .02f ? headY + (top - headY) * .45f : headY * 1.08f;
        var leftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye);
        var rightEye = animator.GetBoneTransform(HumanBodyBones.RightEye);
        if (leftEye != null && rightEye != null)
        {
            float apart = Vector3.Distance(leftEye.position, rightEye.position);
            float eyeY = model.InverseTransformPoint((leftEye.position + rightEye.position) * .5f).y;
            // Hair, animal ears and halos must not raise the estimated eye point on stylised characters.
            if (apart > .015f && apart < .2f && eyeY > headY && eyeY < headY + .25f) eye = eyeY;
        }
        return eye > .1f && camera > .1f ? camera / eye : 1f;
    }

    private void AddChannel(string clipName, float authoredSpeed)
    {
        if (string.IsNullOrEmpty(clipName) || byName.ContainsKey(clipName)) return;
        var clip = profile.Find(clipName);
        if (clip == null)
        {
            Report($"clip '{clipName}' is not in the PlayerBody profile.");
            return;
        }
        var playable = AnimationClipPlayable.Create(graph, clip);
        // Foot IK chases the source clip's foot goals, which the yawOffset turn leaves twisted at the ankles.
        playable.SetApplyFootIK(false);
        int input = mixer.GetInputCount();
        mixer.SetInputCount(input + 1);
        graph.Connect(playable, 0, mixer, input);
        var channel = new Channel
        {
            Playable = playable, Input = input, Length = Mathf.Max(.01f, clip.length),
            AuthoredSpeed = authoredSpeed, Synced = authoredSpeed > 0f
        };
        // Locomotion cycles share one phase so blending between them never puts the feet out of step.
        if (channel.Synced) playable.SetSpeed(0);
        channels.Add(channel);
        byName[clipName] = channel;
    }

    // With a gun out, the gun-holding version of the clip when there is one.
    private Channel Find(string clipName) => !string.IsNullOrEmpty(clipName) && byName.TryGetValue(Armed(clipName), out var c) ? c : null;

    private void Update()
    {
        if (rebuild) Build();
        if (!graph.IsValid() || player == null) return;
        float dt = Time.deltaTime;
        if (HoldsForPause() || dt <= 0f) return;
        // Player/ThirdPerson (F5 unless the key window says otherwise).
        if (player.IsLocal && KeyBindings.Pressed("Player/ThirdPerson"))
        {
            thirdPerson = !thirdPerson;
            ApplyView();
        }
        if (StaysLimp()) return;
        foreach (var channel in channels) channel.Target = 0f;
        targetYaw = 0f;
        legsPlanted = keepsPlace = false;
        ChoosePose(dt);
        Blend(dt);
        BlendLayers(dt);
        if (legsPlanted) { yaw = targetYaw; yawVelocity = 0f; }
        else
        {
            yaw = Mathf.SmoothDampAngle(yaw, targetYaw, ref yawVelocity, .12f);
            legYaw = transform.eulerAngles.y + yaw;
        }
        body.localRotation = Quaternion.Euler(0f, yaw, 0f);
        KeepPlaceAsTheViewTurns();
        PlaceBody();
        PlacePaired();
        AimLook(dt);
    }

    /// <summary>
    /// The pose work after the animation pass, in this order:
    /// 1. the look bend (SpineLook), keeping the clip's head first;
    /// 2. the aim's steady hips, then the body placed under the camera (HoldHeadPlace, FollowHead) or by the hands (Grip);
    /// 3. the legs (tucked feet, the lean's hips, planted feet, ladder feet, legs off walls);
    /// 4. the eyes and the camera (Look: the hanging lean arm, the face clear of walls, the head to the gaze, the camera).
    /// Then the gun kept in view (FPSBody), the weapons, the hands' IK (PlayerBodyHands), FPSAF's late work
    /// (FPSBodyAnimator) and the cloth run in their own LateUpdates, in the order FrameOrder sets.
    /// </summary>
    private void LateUpdate()
    {
        if (GamePause.IsPaused) { HoldFrozenPose(); return; }
        if (Time.deltaTime <= 0f) return;
        if (body != null && !limp) PlayClipsInOnePlace();
        if (body != null && !limp)
        {
            // Where the clip has the chest, before the look bends it: how far the head can turn (PlayerBody.Neck).
            MeasureClipChest();
            // First, as the last step of the animation pass: the spine, neck and head bend with the view.
            spineLook.KeepClipHead();
            spineLook.Apply(spineLookWeight, spineLookPitch, spineLookYaw, spineLookRoll);
            // Then the steadied body, so the feet, the hands and the camera all work from it.
            SteadyAim(Time.deltaTime);
            // Hanging, the hands hold the body in place instead of the head following the camera. FollowHead aims at
            // the camera's pivot, not the camera, so it keeps running while the camera rides the eyes: the camera
            // stays over the capsule and takes the head's dips and quick sway.
            HoldHeadPlace();
            if (gripWeight <= 0f && !slideBodyAnchored && !ClipCarriesBody && !keepsPlace) FollowHead(Time.deltaTime);
            else if (gripWeight <= 0f && slideBodyAnchored && !ClipCarriesBody) FollowHeadAlongSlide(Time.deltaTime);
            using (GripMarker.Auto()) Grip(Time.deltaTime);
            using (FeetMarker.Auto())
            {
                LiftTuckedFeet(Time.deltaTime);
                hipsCarried = bodyLean.MoveHips(hips, bendWeight, dead, leftLeg?.Upper, rightLeg?.Upper);
                PlantFeet(Time.deltaTime);
                PlantLadderFeet(Time.deltaTime);
                SweepLeg(Time.deltaTime);
                KeepLegsOffWall();
            }
            using (LookMarker.Auto()) Look(Time.deltaTime);
        }
        if (thirdPerson) PlaceViewBehind();
    }

    // Named spans of the late pose work, so the profiler and HitchProbe can tell which part of it a stall is in.
    private static readonly Unity.Profiling.ProfilerMarker GripMarker = new Unity.Profiling.ProfilerMarker("PlayerBody.Grip"),
        FeetMarker = new Unity.Profiling.ProfilerMarker("PlayerBody.Feet"), LookMarker = new Unity.Profiling.ProfilerMarker("PlayerBody.Look");

    // The body's place (headAlignment) is held in the view's frame, so as the view turned, the body went round the capsule
    // with it: standing still with the legs planted, a look 60° round moved the feet about 12 cm sideways before any step,
    // and turning on the spot the feet crept round and then stepped (the user, 2026-11-21: "쪼로로... 오다가 쿵!"). Standing
    // still, the place is turned back by the view's own turn, so the body stays where it stands on the ground: the head
    // turns on the neck and the camera on the eyes goes with it, as a head does. Moving, it follows the view as before.
    private float lastViewYaw;
    private bool hasViewYaw;

    private void KeepPlaceAsTheViewTurns()
    {
        float view = transform.eulerAngles.y;
        if (keepsPlace && hasViewYaw && !slideBodyAnchored)
            headAlignment = Quaternion.Euler(0f, -Mathf.DeltaAngle(lastViewYaw, view), 0f) * headAlignment;
        lastViewYaw = view;
        hasViewYaw = true;
    }

    // Clips put the head in different places (lying slides keep it far behind the feet). Ease the body so its head
    // stays just behind the camera; the follow is slower than a stride, so running sway is not cancelled. Standing still it
    // rests (keepsPlace): the feet stay where they stand, and the camera rides the eyes wherever the head is.
    private void FollowHead(float dt)
    {
        if (head == null || view == null || view.parent == null || dt <= 0f) return;
        Vector3 camera = transform.InverseTransformPoint(view.parent.position) - Vector3.forward * backOffset;
        // Not the Q/E lean's part of where the head is: followed, it walked the body back under the pivot (BodyLean).
        Vector3 error = camera - transform.InverseTransformPoint(spineLook.ClipHeadPosition - bodyLean.ClipHeadOffset(dead, dt));
        error.y = 0f;
        headAlignment += error * (1f - Mathf.Exp(-dt / headFollowTime));
        headAlignment = Vector3.ClampMagnitude(headAlignment, 1.5f);
    }

    // Lying back from a slide or a dive, Beatrun's clips carry the whole body on ahead: meslideendprone moves it about
    // 0.9 m, starting from a slide pose that sits that much further back than our slide's. Beatrun's camera isn't on the
    // head; ours is, so through those clips the head keeps its place over the ground instead. The view only goes down,
    // and the legs stretch out ahead of it.
    private void HoldHeadPlace()
    {
        if (head == null) return;
        Vector3 inBody = body.InverseTransformPoint(spineLook.ClipHeadPosition);
        bool hold = !dead && transition != null && (transition == Find(supineSlideEndProne) || transition == Find(supineLand));
        if (hold && lastHeadFrame == Time.frameCount - 1)
        {
            Vector3 drift = transform.InverseTransformVector(body.TransformVector(inBody - lastHeadInBody));
            drift.y = 0f;
            headAlignment -= drift;
            body.localPosition -= drift;
        }
        lastHeadInBody = inBody;
        lastHeadFrame = Time.frameCount;
    }

    private void ChoosePose(float dt)
    {
        var receiver = player.DamageReceiver;
        if (receiver != null && !receiver.IsAlive)
        {
            if (!dead) Restart(Find(death));
            dead = true;
            Want(death);
            return;
        }
        dead = false;

        var current = player.CurrentState;
        bool onGround = player.IsGrounded;
        if (current != state) { Enter(state, current, onGround); state = current; stateTime = 0f; }
        if (onGround != grounded) { Touch(onGround); grounded = onGround; }
        stateTime += dt;
        airTime = onGround ? 0f : airTime + dt;
        TickHops(dt);

        // Landing and slide exits play out on the feet unless the player is already moving off; a vault plays on to
        // its touchdown whatever the feet are doing.
        if (transition != null)
        {
            transitionLeft -= dt;
            bool onFeet = onGround && (state == MovementState.Standing || state == MovementState.Crouching
                || state == MovementState.Proning || state == MovementState.Supine);
            bool movingOff = transitionLeft < transition.Length - .25f && HorizontalSpeed() > walkClipSpeed;
            bool upright = state == MovementState.Standing || state == MovementState.Crouching;
            // A landing that runs on out of itself (Land_ToRun) holds on the move, and gives way once the body slows or
            // leaves its feet.
            bool hold = transitionRuns ? onFeet && upright && HorizontalSpeed() > walkClipSpeed
                : playingOut ? upright && (!onGround || !movingOff) : onFeet && !movingOff;
            // A transition is the stance it began in: a getting up gives way to a crouch, a crouched landing to standing up.
            if (upright && transitionStance != state
                && (transitionStance == MovementState.Standing || transitionStance == MovementState.Crouching))
                hold = false;
            if (transitionLeft > blendTime && hold)
            {
                transition.Target = 1f;
                return;
            }
            if (RunsOut(transition)) GiveUpRunOut(transition);
            transition = null;
            playingOut = transitionRuns = false;
        }

        switch (state)
        {
            case MovementState.Standing:
            case MovementState.Crouching:
                if (onGround) Locomotion(state == MovementState.Crouching, dt);
                else if (ShowsTouchdown) Want(touchdown);
                else AirPose();
                break;
            case MovementState.Sliding: Sequence(slideStart, slide); FaceSlide(); break;
            case MovementState.SupineSliding:
                if (slidesOnBack) Sequence(supineLand, supine);
                else Sequence(supineSlideStart, supineSlide);
                // Held in the slide's frame as the feet-first slide is: it used to face the view, so a look round turned
                // the lying body round under the camera.
                FaceSlide();
                break;
            // Down from a side dive the body already lies in the slide's pose (SideDivePose): no landing clip to swing the arms.
            case MovementState.HeadFirstSliding:
                Sequence(sideDiveLanded ? null : bellySlideStart, bellySlide);
                FaceLying(dt);
                break;
            case MovementState.Diving:
                if (SideDivePose) Want(bellySlide);
                else Sequence(diveStart, dive);
                FaceLying(dt);
                break;
            case MovementState.SupineDiving:
                Sequence(supineDiveAtOnce ? null : supineDiveStart, supineDive);
                FaceBack(dt);
                break;
            case MovementState.Proning: Want(prone); FaceLying(dt); break;
            case MovementState.Supine: Want(supine); FaceLying(dt); break;
            case MovementState.StepDodge: DodgePose(); break;
            case MovementState.LandingRoll:
                // A roll lasts as long as the move; a landing on the spot plays at its own pace and outlasts it.
                if (player.Abilities.Landing.LandsInPlace) Want(rollClip);
                else Drive(rollClip, stateTime / Mathf.Max(.05f, player.AbilitySettings.rollDuration));
                break;
            case MovementState.HeavyLanding: DownPose(); break;
            case MovementState.DashPush: Want(dashPush); break;
            case MovementState.Vault: ObstaclePose(dt); break;
            case MovementState.Mantle:
                var exitLadder = player.Abilities.Traversal.Obstacle.ExitLadder;
                if (exitLadder != null) targetYaw = Mathf.DeltaAngle(transform.eulerAngles.y, exitLadder.ClimberYaw);
                // Climbing off a ladder's top warped on its hands (WarpedTraversal) plays at the clip time the movement drives.
                if (player.Abilities.Traversal.Obstacle.Kind == VaultKind.None && player.Abilities.Traversal.ObstacleClipTime >= 0f
                    && Find(obstacleClip) is Channel warped && warped.Length > 0f)
                    Drive(obstacleClip, player.Abilities.Traversal.ObstacleClipTime / warped.Length);
                else if (player.Abilities.Traversal.Obstacle.Kind == VaultKind.None) Drive(obstacleClip, player.Abilities.Traversal.ObstacleProgress);
                else ObstaclePose(dt);
                break;
            case MovementState.WallClimb: Sequence(wallClimbStart, wallClimb); break;
            case MovementState.WallKick: Want(wallKickClip); break;
            case MovementState.BarGrab:
            case MovementState.BarSwing: BarPose(dt); break;
            case MovementState.LacheRelease: Want(barRelease); break;
            case MovementState.LedgeHang: LedgePose(); break;
            case MovementState.LedgeJump: Want(ledgeJump); break;
            case MovementState.Ladder: LadderPose(); break;
            case MovementState.Zipline: ZiplinePose(); break;
            default: Want(idle); break;
        }
    }

    // One-shot clips start from their first frame when their state begins.
    // Climbing off a ladder's top ends a little over the floor and touches down a moment later; its landing clip (a
    // crouched dip, held while the player stands still) made the body look crouched after every ladder (user, 2026-11-08).
    private float climbedOffLadderAt = float.NegativeInfinity;
    private const float ClimbOffTouchdown = .5f;

    private void Enter(MovementState from, MovementState to, bool onGround)
    {
        if (from == MovementState.Mantle && player.Abilities.Traversal.Obstacle.ExitLadder != null) climbedOffLadderAt = Time.time;
        if (onGround && (to == MovementState.Standing || to == MovementState.Crouching || to == MovementState.Proning
            || to == MovementState.Supine))
        {
            string exit = SlideEndFor(from, to);
            if (exit != null) PlayTransition(exit);
            // The slide's end gets up and runs on (SlideToRun_Exit): cancelled into a run it runs out with the body; stopped
            // on the spot it gets up only (slideEndUp), and its running steps never show (GiveUpRunOut). It played whole, so
            // a slide that stopped ran in place before standing (the user, 2026-11-21).
            if (exit != null && exit == slideEnd)
            {
                if (HorizontalSpeed() > walkClipSpeed) transitionRuns = true;
                else transitionLeft = Mathf.Min(transitionLeft, slideEndUp + blendTime);
            }
        }
        if ((from == MovementState.Vault || from == MovementState.Mantle) && (to == MovementState.Standing || to == MovementState.Crouching))
            PlayOutObstacle();
        if (heaving && from == MovementState.Mantle) SlowBlend(heaveStandTime);
        // Out of a slide straight into a crouch: no clip goes there, so the slide's pose turns into the crouch's slowly.
        if (from == MovementState.Sliding && to == MovementState.Crouching) SlowBlend(slideCrouchTime);
        if (from == MovementState.DashPush && onGround && (to == MovementState.Standing || to == MovementState.Crouching))
            PlayOn(dashPush);
        if (from == MovementState.LandingRoll && onGround && (to == MovementState.Standing || to == MovementState.Crouching))
            PlayOn(rollClip);
        if (from == MovementState.StepDodge && onGround && to == MovementState.Standing) PlayOutDodge();
        // Down from a side dive, sliding or lying (PlayerBody.SideDive).
        if (from == MovementState.Diving)
            sideDiveLanded = sideDiveSide != 0 && (to == MovementState.HeadFirstSliding || to == MovementState.Proning);
        StartLying(from, to);
        switch (to)
        {
            case MovementState.Sliding: Restart(Find(slideStart)); break;
            case MovementState.SupineSliding:
                // Down from a dive on the back still sliding: Beatrun lands its jump turn with jumpturnland at any speed.
                slidesOnBack = from == MovementState.SupineDiving;
                Restart(Find(slidesOnBack ? supineLand : supineSlideStart));
                break;
            case MovementState.HeadFirstSliding:
                if (!sideDiveLanded) Restart(Find(bellySlideStart));
                break;
            case MovementState.Diving: if (player.Actions.Dive.Side == 0) Restart(Find(diveStart)); break;
            case MovementState.SupineDiving:
                // Turned over from a front dive, or off an edge while lying, the body is already down.
                supineDiveAtOnce = from == MovementState.Diving || from == MovementState.Supine;
                if (!supineDiveAtOnce) Restart(Find(supineDiveStart));
                break;
            case MovementState.StepDodge:
                dodgeLegsOut = false;
                dodgeClip = DodgeFor();
                // From a crouch the body stays low and slides along (DodgePose): no hop out of the crouch.
                if (player.IsCrouchStance) break;
                Restart(Find(dodgeClip));
                StartDodgeLegs(dodgeClip);
                break;
            case MovementState.LandingRoll:
                rollClip = RollFor();
                Restart(Find(rollClip));
                break;
            case MovementState.HeavyLanding: downClip = DownClip(player.Abilities.Landing.HeavyPose); break;
            case MovementState.DashPush: Restart(Find(dashPush)); break;
            case MovementState.Vault:
            case MovementState.Mantle:
                // The clip the movement already measured its warp on, if it asked (PlayerBody.ObstacleMotion).
                obstacleClip = from == MovementState.Ladder ? LadderExitClip() : PickObstacleClip();
                StartObstacleClip();
                break;
            case MovementState.WallClimb: Restart(Find(wallClimbStart)); break;
            case MovementState.WallKick:
                wallKickClip = WallKickFor();
                Restart(Find(wallKickClip));
                break;
            case MovementState.LedgeHang:
                Restart(Find(ledgeCatch));
                hangYaw = 0f;
                break;
            case MovementState.LacheRelease: Restart(Find(barRelease)); break;
            case MovementState.LedgeJump: Restart(Find(ledgeJump)); break;
            case MovementState.Ladder: StartLadder(); break;
            case MovementState.Zipline: Restart(Find(ziplineStart)); break;
        }
    }

    private void Touch(bool onGround)
    {
        bool wasTucked = tucked;
        tucked = flailing = false;
        if (onGround && springing) { transition = null; playingOut = springing = false; }
        if (!onGround)
        {
            StartFlight();
            // A jump starts its clip in TickHops; anything else that throws the body up starts it here.
            jumped = jumpQueued || jumpPending || player.VerticalVelocity > 1f;
            if (jumped && !jumpQueued && !jumpPending)
            {
                jumpClip = JumpFor(HorizontalSpeed());
                Restart(Find(jumpClip));
            }
            return;
        }
        if ((state == MovementState.Standing || state == MovementState.Crouching) && Time.time - climbedOffLadderAt > ClimbOffTouchdown)
        {
            string landing = LandingFor(state == MovementState.Crouching, wasTucked);
            if (landing != null)
            {
                PlayTransition(landing);
                transitionRuns = landing == landRunLight || landing == landRunHard || landing == landRunStumble;
            }
            else if (wasTucked && state == MovementState.Standing) SettleFromTuck();
        }
    }

    private void Blend(float dt)
    {
        float step = dt / (slowBlendLeft > 0f ? slowBlendTime : blendTime), total = 0f;
        slowBlendLeft -= dt;
        foreach (var channel in channels)
        {
            channel.Weight = Mathf.MoveTowards(channel.Weight, channel.Target, step);
            total += channel.Weight;
        }
        if (total <= .0001f) return;
        foreach (var channel in channels)
        {
            mixer.SetInputWeight(channel.Input, channel.Weight / total);
            // A measured cycle is put at the same moment of the step as every other (its own left foot down at phase 0,
            // its strides in turn); an unmeasured one at the same share of its length.
            if (channel.Synced)
                channel.Playable.SetTime((channel.Gait != null ? channel.Gait.ShareAt(strideCount, phase) : phase) * channel.Length);
        }
    }

    private void Want(string clipName)
    {
        var channel = Find(clipName) ?? Find(idle);
        if (channel != null) channel.Target = 1f;
    }

    private void Mix(string clipName, float weight)
    {
        var channel = Find(clipName);
        if (channel != null && weight > 0f) channel.Target += weight;
    }

    // An entry clip plays once, then the state's loop takes over.
    private void Sequence(string entry, string loop)
    {
        var first = Find(entry);
        Want(first != null && stateTime < first.Length - blendTime ? entry : loop);
    }

    private void PlayTransition(string clipName)
    {
        var channel = Find(clipName);
        if (channel == null) return;
        Restart(channel);
        transition = channel;
        transitionStance = player.CurrentState;
        transitionLeft = channel.Length;
        playingOut = transitionRuns = false;
    }

    // The stance a transition began in (PlayTransition, PlayOn, PlayOutObstacle, SettleFromTuck).
    private MovementState transitionStance;

    // The clips cross over this slowly for this long (s), where no clip goes from one pose to the other.
    private void SlowBlend(float time) => slowBlendTime = slowBlendLeft = time;

    // The transition playing is a landing that runs on (PlayerBody.Ways): it holds on the move.
    private bool transitionRuns;

    // A one-shot clip carries on from where it is as a landing-like transition: the barge recovers on the spot when a
    // dash push stops on someone, and gives way at once to a run that carries on.
    private void PlayOn(string clipName)
    {
        var channel = Find(clipName);
        if (channel == null || channel.Synced) return;
        float left = channel.Length - (float)channel.Playable.GetTime();
        if (left <= blendTime) return;
        // One that ends running plays on only on the run (RunsOut); on the spot its steps would run in place.
        bool runs = RunsOut(channel);
        if (runs && HorizontalSpeed() <= walkClipSpeed)
        {
            GiveUpRunOut(channel);
            return;
        }
        transition = channel;
        transitionStance = player.CurrentState;
        transitionLeft = left;
        playingOut = false;
        transitionRuns = runs;
        // A clip the move drove (a dodge's step) goes on at its own pace.
        channel.Playable.SetSpeed(1);
    }

    private static void Restart(Channel channel)
    {
        if (channel == null || channel.Synced) return;
        channel.Playable.SetSpeed(1);
        channel.Playable.SetTime(0);
        channel.Playable.SetDone(false);
    }

    // A one-shot clip held at the share of its length the movement has reached (a vault's progress), so the body
    // takes exactly as long as the move.
    private void Drive(string clipName, float share)
    {
        var channel = Find(clipName);
        if (channel == null || channel.Synced) { Want(clipName); return; }
        channel.Playable.SetSpeed(0);
        channel.Playable.SetTime(Mathf.Clamp01(share) * channel.Length);
        channel.Target = 1f;
    }

    private float HorizontalSpeed() => new Vector2(player.ActualVelocity.x, player.ActualVelocity.z).magnitude;

    private void ApplyView()
    {
        ApplyHeadVisibility();
    }

    private void OnSettingsChanged()
    {
        if (profile != null && (ChosenSkin != loadedSkin || !WearsChosenOutfit)) rebuild = true;
        else if (profile != null) WearChosenVariant();
        ApplyView();
    }

    private void PlaceViewBehind() => bodyCamera.PlaceBehind(thirdPersonOffset, gameObject.layer);

    private void Report(string message)
    {
        if (reported.Add(message)) Debug.LogWarning("[PlayerBody] " + message, this);
    }
}
