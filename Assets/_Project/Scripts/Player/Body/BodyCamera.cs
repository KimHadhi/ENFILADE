using UnityEngine;

/// <summary>
/// The first-person camera on the visible body's eyes:
/// - it rides the eyes, as KINEMATION's first-person camera rides its camera socket, aiming too, since FPS Animation
///   Framework lines the sight up with the eyes (FPSBody). On a ladder and through a punch it steadies toward its place in
///   the capsule (EaseOntoEyes);
/// - it turns with the gaze (the mouse, plus a bounded share of selected action clips, BodyGaze), with the camera
///   effects' turn on top;
/// - the Q/E lean's peek: with nothing in hand the body carries part of it and the camera makes up the rest, as up to
///   0.7.34; holding a gun or the fists the body carries all of it and the camera stays on the eyes (BodyLean);
/// - a sweep from a steady point inside the movement capsule to the eyes keeps it out of walls (CameraClearance), the last
///   guard after the face has cleared them (PlayerBody.ClearFace);
/// - F5 puts it behind the body (PlaceBehind); the Esc menu's own-head option sets the near clip plane (CutOwnHead).
/// - Settings → 화면 → 몸 흔들림 (GameSettings.bodySway): below 1 it follows an eased path through the eyes instead of the
///   eyes themselves. Wherever it ends up, the body then bends its head to it (HeadReach, from Placed), so the camera
///   steadies without leaving the head.
/// - Running on the ground it also steadies part of the eyes' bob (Place's calm, PlayerBody.runCalm), as a runner's eyes hold
///   the view still while the head bobs; the head bends to it the same way.
/// A plain class PlayerBody owns (stage 3 of the body restructure, 2026-11-09): it holds the camera's own state, and the
/// body hands it the pose each frame (Place). The numbers stay PlayerBody's (Player.csv, row Body).
/// </summary>
internal sealed class BodyCamera
{
    private readonly CameraClearance clearance = new CameraClearance();
    private PlayerMovement player;
    private Transform owner, view, eyes;
    private Camera camera;
    private PlayerCameraEffects effects;
    private CharacterController capsule;
    private Vector3 home;
    private Quaternion rotationHome = Quaternion.identity;
    private float baseNearClip, eyeWeight = 1f;
    // The eyes' place against the camera's steady place in the capsule, in the capsule's own frame, and its eased path
    // (bodySway). In the capsule's frame, so a turn or the capsule's own move never lags.
    private Vector3 easedEyes;
    private bool eased;

    /// <summary>The player's camera, or null if it has none.</summary>
    internal Transform View => view;

    /// <summary>Where Place last put the first-person camera, walls and all (the F5 view moves it on after), and whether it
    /// has put it anywhere since the body was made. The head bends to it (HeadReach).</summary>
    internal Vector3 Placed { get; private set; }
    internal bool HasPlace { get; private set; }

    /// <summary>Finds the player's camera and its resting place. Before the body is made.</summary>
    internal void Find(PlayerMovement player, Transform owner)
    {
        this.player = player;
        this.owner = owner;
        var found = owner.GetComponentInChildren<Camera>();
        view = found != null ? found.transform : null;
        effects = owner.GetComponent<PlayerCameraEffects>();
        if (view != null) home = effects != null ? effects.CameraLocalHome : view.localPosition;
    }

    /// <summary>A new body: the camera starts on the eyes, from where it rests now.</summary>
    internal void SetUp()
    {
        camera = view != null ? view.GetComponent<Camera>() : null;
        eyes = PlayerView.EyesOf(player);
        if (camera != null) baseNearClip = camera.nearClipPlane;
        rotationHome = view != null ? view.localRotation : Quaternion.identity;
        capsule = owner != null ? owner.GetComponent<CharacterController>() : null;
        eyeWeight = 1f;
        eased = HasPlace = false;
    }

    /// <summary>The body went: the camera goes back where it rests, with its own near clip plane.</summary>
    internal void Release()
    {
        if (camera != null) camera.nearClipPlane = baseNearClip;
        camera = null;
        if (view != null) { view.localPosition = home; view.localRotation = rotationHome; }
        eased = HasPlace = false;
    }

    /// <summary>How far the camera rides the eyes (1) rather than its place in the capsule (0), eased over
    /// <paramref name="time"/> (s).</summary>
    internal void EaseOntoEyes(float target, float time, float dt) => eyeWeight = Mathf.MoveTowards(eyeWeight, target, dt / time);

    /// <summary>The camera's steady place in the capsule this frame, where it goes when it doesn't ride the eyes.</summary>
    internal Vector3 Anchor()
        => effects != null ? effects.CameraAnchorPosition
            : view != null && view.parent != null ? view.parent.TransformPoint(home) : owner.position + Vector3.up * player.runtimeBodyDefaults.w;

    /// <summary>
    /// A stable point inside the movement capsule stays on the same side of a wall as the player. In particular, the sweep's
    /// origin never changes between an overlapping head and last frame's already corrected camera.
    /// </summary>
    internal Vector3 SafeAnchor()
    {
        Vector3 anchor = effects != null ? effects.CameraAnchorPosition
            : view != null && view.parent != null ? view.parent.TransformPoint(home) : owner.position;
        if (capsule == null) return anchor;
        Vector3 local = owner.InverseTransformPoint(anchor);
        float half = Mathf.Max(0f, capsule.height * .5f - capsule.radius);
        local.x = capsule.center.x;
        local.z = capsule.center.z;
        local.y = Mathf.Clamp(local.y, capsule.center.y - half, capsule.center.y + half);
        return owner.TransformPoint(local);
    }

    /// <summary>The camera's size for its sweep: the near plane's corners as well as the optical centre, at any field of view.</summary>
    internal float Radius()
    {
        if (camera == null) return .05f;
        float halfHeight = Mathf.Tan(camera.fieldOfView * .5f * Mathf.Deg2Rad);
        return Mathf.Max(.035f, camera.nearClipPlane * Mathf.Sqrt(1f + halfHeight * halfHeight * (1f + camera.aspect * camera.aspect)));
    }

    // Walls keep the camera out, enemies don't: sliding past or under one would otherwise pull the camera back to its
    // anchor inside one's own body, and a slide passes right through them.
    private LayerMask Blockers => player.groundLayers & ~LayerMask.GetMask("Enemy");

    /// <summary>Whether a sphere of <paramref name="radius"/> goes from <paramref name="from"/> to <paramref name="to"/>
    /// without meeting a wall.</summary>
    internal bool IsClear(Vector3 from, Vector3 to, float radius) => clearance.IsClear(from, to, radius, Blockers, owner);

    /// <summary>
    /// Puts the camera on this frame's eyes. <paramref name="eye"/> is where they ended up, and <paramref name="eyeBefore"/>
    /// where they were before the lean's hanging arm, the face's clearance and the head's turn to the gaze; a camera steadied
    /// off the eyes keeps that move. <paramref name="carried"/> and <paramref name="makesUp"/> are the lean's
    /// (BodyLean.Carry, BodyLean.CameraMakesUp). <paramref name="gaze"/> is where the view looks and <paramref name="input"/>
    /// where the mouse alone has it. The sweep goes past <paramref name="ignored"/>. <paramref name="dt"/> and
    /// <paramref name="swayTime"/> (s) ease the path the camera follows with less body sway (GameSettings.bodySway).
    /// <paramref name="calm"/> is the share of the eyes' sway it keeps to that path on top (running, PlayerBody.runCalm).
    /// </summary>
    internal void Place(Vector3 eye, Vector3 eyeBefore, Vector3 carried, float makesUp, Quaternion gaze, Quaternion input, Transform ignored,
        float dt = 0f, float swayTime = 0f, float calm = 0f)
    {
        if (view == null) return;
        Vector3 anchor = Anchor();
        Turn(gaze, input);
        // Combat's head moves (a slip's dip) go on the eyes as they are; the head then bends to the camera (HeadReach).
        Vector3 headMove = effects != null ? effects.HeadDisplacement : Vector3.zero;
        // The Q/E lean's whole peek (wall-limited by the camera effects), the share the camera makes up.
        Vector3 lean = (effects != null ? effects.LeanDisplacement : owner.right * player.LeanOffset) * makesUp;
        Vector3 riding = Sway(eye, anchor, dt, swayTime, calm);
        Vector3 target = Vector3.Lerp(anchor + lean + eye - eyeBefore, riding + lean - carried * makesUp, eyeWeight) + headMove;
        // The pose cleared the wall first; this stable capsule-to-eye sweep is the final guard for any remaining contact.
        clearance.Ignored = ignored;
        view.position = clearance.Resolve(SafeAnchor(), target, Radius(), Blockers, owner);
        Placed = view.position;
        HasPlace = true;
        PlayerView.Follow(eyes, view);
    }

    // Settings → 화면 → 몸 흔들림 (bodySway): the share of the eyes' own sway the camera follows; the rest of the way it
    // keeps to their eased path, eased in the capsule's frame. The path is kept up at any setting, so changing it mid-game
    // never jumps. Someone else's copy rides its eyes: their setting isn't known on this PC.
    private Vector3 Sway(Vector3 eye, Vector3 anchor, float dt, float swayTime, float calm)
    {
        Vector3 offset = owner.InverseTransformVector(eye - anchor);
        if (!eased || dt <= 0f || swayTime <= 0f) easedEyes = offset;
        else easedEyes = Vector3.Lerp(easedEyes, offset, 1f - Mathf.Exp(-dt / swayTime));
        eased = true;
        float sway = (player != null && player.IsLocal ? GameSettings.Current.bodySway : 1f) * (1f - Mathf.Clamp01(calm));
        return sway >= .999f ? eye : anchor + owner.TransformVector(Vector3.Lerp(easedEyes, offset, sway));
    }

    // Effects (lean, impact, recoil presentation) stay additive and are applied once, after the gaze. Weapons and hands run
    // later and read this same final camera pose.
    private void Turn(Quaternion gaze, Quaternion input)
    {
        if (view.parent == null) return;
        Quaternion effectsTurn = Quaternion.Inverse(input) * view.parent.rotation;
        view.rotation = gaze * effectsTurn * rotationHome;
    }

    /// <summary>The F5 view: <paramref name="offset"/> behind the look pivot (in its space), short of walls; the own
    /// body's layer <paramref name="ownLayer"/> doesn't stop it.</summary>
    internal void PlaceBehind(Vector3 offset, int ownLayer)
    {
        if (view == null || view.parent == null) return;
        view.localRotation = rotationHome;
        Transform pivot = view.parent;
        Vector3 origin = pivot.position, wanted = pivot.TransformPoint(offset), path = wanted - origin;
        float distance = path.magnitude;
        if (distance > .01f && Physics.SphereCast(origin, .2f, path / distance, out RaycastHit hit, distance,
                ~(1 << ownLayer), QueryTriggerInteraction.Ignore))
            wanted = origin + path / distance * Mathf.Max(.1f, hit.distance - .05f);
        view.position = wanted;
    }

    /// <summary>With the own head shown in first person, the near clip plane cuts what sits around the camera (the inside
    /// of the face) at <paramref name="headNearClip"/> (m) at least; otherwise it is the camera's own.</summary>
    internal void CutOwnHead(bool cut, float headNearClip)
    {
        if (camera != null) camera.nearClipPlane = cut ? Mathf.Max(baseNearClip, headNearClip) : baseNearClip;
    }
}
