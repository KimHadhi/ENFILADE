using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The camera the HUD is seen through, so a HUD plane turned in depth really recedes (a Screen Space - Overlay canvas can
/// only squash it). An overlay camera stacked on whichever camera draws the world, with a fixed field of view of its own:
/// aiming, sprint and the player's field of view setting never zoom the HUD. Post-processing stays off, so bloom, the
/// kill screen and the low-health grade never touch it. It sits far below the world and sees only its own canvases.
/// It also turns the world camera's turning into the HUD's sway (Settings → HUD 흔들림) and the local player's lean into
/// the HUD's tilt (Settings → HUD 기울이기 따라가기). One per run, made on first use; Hud.csv (Camera, Planes, Sway, Lean)
/// holds its numbers.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.HudCamera)]
public sealed class HudCamera : MonoBehaviour
{
    public const string TableName = "Hud";
    // Canvas distance from the camera (m), and a spot no scene reaches.
    private const float PlaneDistance = 10f;
    private static readonly Vector3 Home = new Vector3(0f, -20000f, 0f);

    [System.Serializable]
    public sealed class Look
    {
        [Tooltip("Vertical field of view of the HUD camera (°). Wider deepens a turned plane but stretches its near side " +
            "sideways; narrower with a steep yaw squeezes its text. The boards (HudBoard) are what show the plane.")]
        public float fov = 45f;
        [Tooltip("Both planes lean back at the top by this much (°). How far they turn away at their inner edge is the " +
            "player's setting (GameSettings.hudAngle).")]
        public float pitch;
        [Tooltip("Sway per degree per second of camera turning: the HUD trails the look by this much.")]
        public float lag = .012f;
        [Tooltip("Most the sway turns the HUD sideways (°).")]
        public float max = 2.5f;
        [Tooltip("Most the sway tips the HUD up or down (°).")]
        public float pitchMax = 1.5f;
        [Tooltip("How quickly the sway catches up with the look (1/s). Higher feels stiffer.")]
        public float follow = 9f;
        [Tooltip("How far the planes slide per degree of sway (px at 1080p).")]
        public float shift = 6f;
        [Tooltip("Share of the lean's view roll the HUD's sides tilt by on the screen, the same way the world turns " +
            "(Settings → HUD 기울이기 따라가기). 1 keeps them level with the world.")]
        public float share = .6f;
    }

    private static HudCamera instance;
    private static Camera[] found = new Camera[8];
    private readonly Look look = new Look();
    private Camera view, world;
    private Quaternion lastWorldRotation;
    private bool hasLast;
    private Vector2 sway;
    private float leanRoll;

    public static Look Settings => Get().look;
    /// <summary>Sideways (x) and vertical (y) sway in degrees, already scaled by the player's setting.</summary>
    public static Vector2 Sway => instance != null ? instance.sway : Vector2.zero;
    /// <summary>How far the HUD's sides turn within the screen for the local player's lean (°, anticlockwise positive).</summary>
    public static float LeanRoll => instance != null ? instance.leanRoll : 0f;
    /// <summary>True while this camera draws, so StandbyCamera can tell it from a camera that shows the world.</summary>
    public static bool IsDrawing => instance != null && instance.view != null && instance.view.enabled;

    private static HudCamera Get()
    {
        if (instance == null)
        {
            var host = new GameObject("HUD Camera");
            DontDestroyOnLoad(host);
            instance = host.AddComponent<HudCamera>();
        }
        return instance;
    }

    /// <summary>Draw a root canvas through the HUD camera instead of over the screen.</summary>
    public static void Adopt(Canvas canvas)
    {
        if (canvas == null || !canvas.isRootCanvas) return;
        canvas.renderMode = RenderMode.ScreenSpaceCamera;
        canvas.worldCamera = Get().view;
        canvas.planeDistance = PlaneDistance;
    }

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        transform.SetPositionAndRotation(Home, Quaternion.identity);
        view = gameObject.AddComponent<Camera>();
        // Runtime canvases stay on Default; nothing else exists down here.
        view.cullingMask = (1 << LayerMask.NameToLayer("UI")) | 1;
        view.nearClipPlane = .05f;
        view.farClipPlane = 100f;
        view.enabled = false;
        var data = view.GetUniversalAdditionalCameraData();
        data.renderType = CameraRenderType.Overlay;
        data.renderPostProcessing = false;
        data.renderShadows = false;
        GameTables.BindNested(TableName, "Camera", this, look);
        GameTables.BindNested(TableName, "Planes", this, look);
        GameTables.BindNested(TableName, "Sway", this, look);
        GameTables.BindNested(TableName, "Lean", this, look);
    }

    private void OnDestroy() { if (instance == this) instance = null; }

    // After StandbyCamera and the player's camera settle this frame's camera.
    private void LateUpdate()
    {
        var target = WorldCamera();
        if (target != world) Restack(target);
        view.enabled = world != null;
        view.fieldOfView = look.fov;
        MeasureSway();
        MeasureLean();
    }

    private Camera WorldCamera()
    {
        var main = Camera.main;
        if (main != null && main != view) return CanCarry(main) ? main : null;
        // The title menu's preview camera is not tagged MainCamera: take the frontmost camera that draws to the screen.
        int count = Camera.allCamerasCount;
        if (found.Length < count) found = new Camera[count * 2];
        count = Camera.GetAllCameras(found);
        Camera best = null;
        for (int i = 0; i < count; i++)
            if (found[i] != view && CanCarry(found[i]) && (best == null || found[i].depth > best.depth)) best = found[i];
        System.Array.Clear(found, 0, count);
        return best;
    }

    private static bool CanCarry(Camera camera)
        => camera.targetTexture == null && camera.GetUniversalAdditionalCameraData().renderType == CameraRenderType.Base;

    private void Restack(Camera next)
    {
        if (world != null) world.GetUniversalAdditionalCameraData().cameraStack.Remove(view);
        world = next;
        hasLast = false;
        if (world == null) return;
        var stack = world.GetUniversalAdditionalCameraData().cameraStack;
        if (!stack.Contains(view)) stack.Add(view);
    }

    // The HUD trails the look: turning right leaves it a little to the left until the turn stops.
    private void MeasureSway()
    {
        float dt = Time.unscaledDeltaTime;
        var target = Vector2.zero;
        if (world != null && dt > 0f)
        {
            Quaternion now = world.transform.rotation;
            if (hasLast)
            {
                Vector3 before = lastWorldRotation * Vector3.forward, after = now * Vector3.forward;
                float yawRate = Mathf.DeltaAngle(Yaw(before), Yaw(after)) / dt;
                float pitchRate = Mathf.DeltaAngle(Pitch(before), Pitch(after)) / dt;
                float strength = Mathf.Clamp01(GameSettings.Current.hudSway);
                target = new Vector2(Mathf.Clamp(-yawRate * look.lag, -look.max, look.max),
                    Mathf.Clamp(-pitchRate * look.lag, -look.pitchMax, look.pitchMax)) * strength;
            }
            lastWorldRotation = now;
            hasLast = true;
        }
        sway = Vector2.Lerp(sway, target, 1f - Mathf.Exp(-Mathf.Max(0f, look.follow) * dt));
    }

    // Leaning rolls the view (PlayerCameraEffects adds MovementLean.Roll, anticlockwise positive), which turns the world
    // the other way on the screen; the HUD turns with the world by Lean.share. The lean already eases in and out.
    private void MeasureLean()
    {
        var player = PlayerRoster.Local;
        leanRoll = GameSettings.Current.hudFollowsLean && player != null ? -player.LeanRoll * look.share : 0f;
    }

    private static float Yaw(Vector3 forward) => Mathf.Atan2(forward.x, forward.z) * Mathf.Rad2Deg;
    private static float Pitch(Vector3 forward) => Mathf.Asin(Mathf.Clamp(forward.y, -1f, 1f)) * Mathf.Rad2Deg;
}
