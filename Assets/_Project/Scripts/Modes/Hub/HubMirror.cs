using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The hub stage's mirror (user, 2026-11-12: "로비 무대 엔필레이드 쪽에 거울 넣어서 자기 볼 수 있게"): a full-length glass on the
/// stage's back wall under the ENFILADE sign, where everyone sees themself whole, head and all, and the hall behind them.
/// [F] at it opens the dressing spot in front of it (HubDressing, through HubLobby).
/// How it draws, kept clean and cheap in URP:
/// - A camera of its own stands where the eye looking at the glass would be if it stood behind the wall (the eye's
///   reflection in the glass) and looks out through the glass into the hall. Its frustum is fitted to the glass as a
///   window: an off-centre perspective whose near plane is the glass itself, so the wall and everything else behind the
///   glass are cut away by the near plane. It is an ordinary projection, so what reads the depth (SSAO, decals, the
///   shadow cascades) stays right, which an oblique near plane, the usual mirror trick, breaks.
/// - Only the part of the glass the eye sees is drawn (the glass clipped by the eye's view), at about as many pixels as
///   it covers on the screen (Hub.csv mirrorResolution, at most mirrorLargest a side), in one of a few sizes kept for
///   reuse: a close look at the face from a step away is as sharp as a glance across the hall is cheap.
/// - It draws only while the glass is in view, from in front, within mirrorReach; no post-processing of its own (the
///   eye's camera grades the reflection with the rest of the frame), shadows as mirrorShadows says (on: the hall's roof
///   keeps the sun off the stage, and without shadows the reflected room came out sunlit).
/// - The glass shows the picture with its own unlit shader (Hidden/ENFILADE/HubMirror), turned left for right as a
///   mirror does and a touch darker than the room (mirrorTint), and goes dark glass when the mirror stops drawing.
/// - This PC's own player is whole in it: what first person draws only as a shadow (the head when the Esc setting says
///   so, a mask or glasses, the guns slung on the back) is drawn for this camera alone. The HUD is not in the world, so
///   none shows; nor is there a first-person arm or gun model apart from the body.
/// - Every PC draws its own, from its own eye: the player's camera, or the dressing spot's while it is open. While a
///   chapter's results play on the stage (ResultsShow) the glass stays dark: a second, wrong-angled picture of the lineup
///   behind the lineup would spoil the shot, and the results camera isn't an eye this follows.
/// The builder (HubBuilder) stands it on the glass's middle with a HubStation of kind Mirror, whose size is the glass's;
/// its forward points out of the glass into the hall. The glass itself is made here at play.
/// </summary>
[DisallowMultipleComponent, RequireComponent(typeof(HubStation)), DefaultExecutionOrder(FrameOrder.HubMirror)]
public sealed class HubMirror : MonoBehaviour
{
    private const string ShaderPath = "World/HubMirror";
    // How quickly the glass goes dark and lights again (1/s), and how far in front of the glass the eye must be (m).
    private const float LiveRate = 6f, Front = .03f;
    // The camera's near plane stands this far out from the glass, so the glass is never in its own picture (m).
    private const float NearPast = .002f;
    // The picture reaches this share of the glass past what the eye sees, so its edge never shows in a quick turn.
    private const float Margin = .02f;
    // The picture's sizes, as shares of the eye's screen (times mirrorResolution) across and up, picked separately: the
    // smallest that holds what is seen. A few are kept for reuse, so walking up to the glass doesn't remake it every step.
    private static readonly float[] Classes = { .125f, .25f, .5f, 1f };
    private const int Kept = 3;

    private static readonly int MainTexId = Shader.PropertyToID("_MainTex"), WindowId = Shader.PropertyToID("_Window"),
        TintId = Shader.PropertyToID("_Tint"), LiveId = Shader.PropertyToID("_Live");

    private static HubMirror active;
    private HubStation station;
    private MeshRenderer glass;
    private Mesh mesh;
    private Material material;
    private Camera lens;
    private Camera eye;
    private readonly RenderTexture[] pictures = new RenderTexture[Classes.Length * Classes.Length];
    private readonly float[] usedAt = new float[Classes.Length * Classes.Length];
    // The picture this frame draws into.
    private RenderTexture target;
    private Vector2Int screenWas;
    private float live;
    private bool drawing;
    // The part of the glass in the picture, on the glass's axes from its middle (m): across to the right of someone
    // facing it (a), and up (b).
    private float a0, a1, b0, b1;
    private readonly Plane[] planes = new Plane[6];
    private readonly List<Vector3> polygon = new List<Vector3>(16), clipped = new List<Vector3>(16);
    // This PC's own player's renderers, found again now and then, and those drawn for the mirror alone this render.
    private PlayerMovement owner;
    private Animator ownerBody;
    private float ownFoundAt = float.NegativeInfinity;
    private readonly List<Renderer> own = new List<Renderer>();
    private readonly List<Renderer> shown = new List<Renderer>();
    private static bool warned;

    /// <summary>The hub's mirror, if this scene has one.</summary>
    public static HubMirror Active => active;
    /// <summary>The glass's middle (world).</summary>
    public Vector3 Center => transform.position;
    /// <summary>Out of the glass, into the hall.</summary>
    public Vector3 Normal => transform.forward;
    /// <summary>To the right of someone facing the glass.</summary>
    public Vector3 Across => -transform.right;
    public Vector3 Up => transform.up;
    /// <summary>The glass's width and height (m).</summary>
    public Vector2 Size => station != null ? new Vector2(station.Size.x, station.Size.y) : new Vector2(3f, 2.2f);
    /// <summary>How far in front of the glass a point is (m); behind it is negative.</summary>
    public float InFront(Vector3 point) => Vector3.Dot(point - Center, Normal);
    /// <summary>Where a point shows in the glass: its reflection, as far behind the glass as it is in front.</summary>
    public Vector3 Reflect(Vector3 point) => point - 2f * InFront(point) * Normal;

    private void Awake()
    {
        active = this;
        station = GetComponent<HubStation>();
        MakeGlass();
    }

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += BeforeRender;
        RenderPipelineManager.endCameraRendering += AfterRender;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= BeforeRender;
        RenderPipelineManager.endCameraRendering -= AfterRender;
        ShowOwnAgain();
        if (glass != null) glass.forceRenderingOff = false;
        StopDrawing();
    }

    private void OnDestroy()
    {
        if (active == this) active = null;
        if (lens != null) { lens.targetTexture = null; Destroy(lens.gameObject); }
        for (int i = 0; i < pictures.Length; i++) Drop(i);
        if (mesh != null) Destroy(mesh);
        if (material != null) Destroy(material);
    }

    // ---- The glass ----

    // A quad the size of the glass facing along this object's forward. Someone facing it has this object's -x on their
    // right, so its u runs that way and v up: the shader turns the picture left for right on that.
    private void MakeGlass()
    {
        var shader = Resources.Load<Shader>(ShaderPath);
        if (shader == null)
        {
            if (!warned) Debug.LogWarning("[Hub] Resources/" + ShaderPath + ".shader not found: the hub's mirror stays blank.");
            warned = true;
            return;
        }
        material = new Material(shader) { name = "Hub Mirror (Runtime)", hideFlags = HideFlags.HideAndDontSave };
        Vector2 size = Size;
        float w = size.x * .5f, h = size.y * .5f;
        mesh = new Mesh { name = "Hub Mirror Glass", hideFlags = HideFlags.HideAndDontSave };
        mesh.vertices = new[] { new Vector3(w, -h, 0f), new Vector3(-w, -h, 0f), new Vector3(w, h, 0f), new Vector3(-w, h, 0f) };
        mesh.uv = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(1f, 1f) };
        mesh.normals = new[] { Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward };
        // Clockwise as seen from in front, Unity's front face.
        mesh.triangles = new[] { 0, 2, 3, 0, 3, 1 };
        mesh.RecalculateBounds();
        var host = new GameObject("Glass", typeof(MeshFilter), typeof(MeshRenderer));
        host.transform.SetParent(transform, false);
        host.GetComponent<MeshFilter>().sharedMesh = mesh;
        glass = host.GetComponent<MeshRenderer>();
        glass.sharedMaterial = material;
        glass.shadowCastingMode = ShadowCastingMode.Off;
        glass.receiveShadows = false;
        glass.lightProbeUsage = LightProbeUsage.Off;
        glass.reflectionProbeUsage = ReflectionProbeUsage.Off;
        glass.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        material.SetFloat(LiveId, 0f);
    }

    // ---- Every frame: whether it draws, and how big ----

    private void LateUpdate()
    {
        if (material == null) return;
        eye = Eye();
        Vector3 at = eye != null ? eye.transform.position : Vector3.zero;
        float reach = HubLobby.Number("mirrorReach", 30f);
        bool allowed = eye != null && !ResultsShow.IsPlaying && InFront(at) > Front && (at - Center).sqrMagnitude <= reach * reach;
        drawing = allowed && See(eye, out Vector2 pixels) && Picture(pixels);
        // Out of view it simply stops; it goes dark only for a reason that shows (the results, too far, from behind), and
        // lights again from what it draws the same frame it is let draw again.
        live = Mathf.MoveTowards(live, allowed ? 1f : 0f, Time.unscaledDeltaTime * LiveRate);
        material.SetFloat(LiveId, live);
        float tint = Mathf.Clamp01(HubLobby.Number("mirrorTint", .93f));
        material.SetColor(TintId, new Color(tint, tint, tint, 1f));
        if (drawing) StartDrawing();
        else StopDrawing();
    }

    // The eye the glass is seen with on this PC: the dressing spot's camera while it is open, else the player's.
    private static Camera Eye()
    {
        var dressing = HubDressing.View;
        if (dressing != null && dressing.isActiveAndEnabled) return dressing;
        var main = Camera.main;
        return main != null && main.isActiveAndEnabled && main.targetTexture == null ? main : null;
    }

    // The part of the glass the eye sees: the glass clipped by the eye's view, its extent on the glass (a0..a1, b0..b1)
    // and how many screen pixels it covers. False when none of it is in view.
    private bool See(Camera from, out Vector2 pixels)
    {
        pixels = Vector2.zero;
        Vector2 size = Size;
        float w = size.x * .5f, h = size.y * .5f;
        Vector3 c = Center, across = Across, up = Up;
        polygon.Clear();
        polygon.Add(c - across * w - up * h);
        polygon.Add(c + across * w - up * h);
        polygon.Add(c + across * w + up * h);
        polygon.Add(c - across * w + up * h);
        GeometryUtility.CalculateFrustumPlanes(from, planes);
        for (int i = 0; i < planes.Length && polygon.Count >= 3; i++) Clip(planes[i]);
        if (polygon.Count < 3) return false;
        float lowA = float.MaxValue, highA = float.MinValue, lowB = float.MaxValue, highB = float.MinValue;
        Vector2 low = new Vector2(float.MaxValue, float.MaxValue), high = new Vector2(float.MinValue, float.MinValue);
        foreach (var point in polygon)
        {
            Vector3 off = point - c;
            float a = Vector3.Dot(off, across), b = Vector3.Dot(off, up);
            lowA = Mathf.Min(lowA, a); highA = Mathf.Max(highA, a);
            lowB = Mathf.Min(lowB, b); highB = Mathf.Max(highB, b);
            Vector2 screen = from.WorldToScreenPoint(point);
            low = Vector2.Min(low, screen);
            high = Vector2.Max(high, screen);
        }
        float padA = size.x * Margin, padB = size.y * Margin;
        a0 = Mathf.Max(-w, lowA - padA);
        a1 = Mathf.Min(w, highA + padA);
        b0 = Mathf.Max(-h, lowB - padB);
        b1 = Mathf.Min(h, highB + padB);
        if (a1 - a0 < .001f || b1 - b0 < .001f) return false;
        pixels = Vector2.Max(high - low, Vector2.one);
        return true;
    }

    // One step of Sutherland-Hodgman: what of the polygon lies on the inner side of a frustum plane.
    private void Clip(Plane plane)
    {
        clipped.Clear();
        int count = polygon.Count;
        for (int i = 0; i < count; i++)
        {
            Vector3 p = polygon[i], q = polygon[(i + 1) % count];
            float dp = plane.GetDistanceToPoint(p), dq = plane.GetDistanceToPoint(q);
            if (dp >= 0f) clipped.Add(p);
            if ((dp >= 0f) != (dq >= 0f)) clipped.Add(Vector3.Lerp(p, q, dp / (dp - dq)));
        }
        polygon.Clear();
        polygon.AddRange(clipped);
    }

    // The picture for this frame: the smallest of the kept sizes that holds what is seen at mirrorResolution texels a
    // pixel, across and up separately. False when there is none to draw into.
    private bool Picture(Vector2 pixels)
    {
        var screen = new Vector2Int(Mathf.Max(1, eye.pixelWidth), Mathf.Max(1, eye.pixelHeight));
        if (screen != screenWas)
        {
            for (int i = 0; i < pictures.Length; i++) Drop(i);
            screenWas = screen;
        }
        float share = Mathf.Clamp(HubLobby.Number("mirrorResolution", .8f), .1f, 2f);
        int largest = Mathf.Max(64, Mathf.RoundToInt(HubLobby.Number("mirrorLargest", 2048f)));
        int across = SizeClass(screen.x * share, pixels.x * share), up = SizeClass(screen.y * share, pixels.y * share);
        int index = across * Classes.Length + up;
        var picture = pictures[index];
        if (picture == null || !picture.IsCreated())
        {
            Drop(index);
            int width = Mathf.Clamp(Mathf.CeilToInt(screen.x * share * Classes[across]), 16, largest);
            int height = Mathf.Clamp(Mathf.CeilToInt(screen.y * share * Classes[up]), 16, largest);
            int samples = Mathf.ClosestPowerOfTwo(Mathf.Clamp(Mathf.RoundToInt(HubLobby.Number("mirrorMsaa", 2f)), 1, 8));
            picture = new RenderTexture(width, height, 24, RenderTextureFormat.DefaultHDR)
            {
                name = "Hub Mirror " + width + "x" + height,
                antiAliasing = samples,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                hideFlags = HideFlags.HideAndDontSave
            };
            if (!picture.Create()) { Destroy(picture); return false; }
            pictures[index] = picture;
            ForgetOldest(index);
        }
        usedAt[index] = Time.unscaledTime;
        target = picture;
        return true;
    }

    private static int SizeClass(float full, float needed)
    {
        for (int i = 0; i < Classes.Length; i++)
            if (full * Classes[i] >= needed) return i;
        return Classes.Length - 1;
    }

    // Past the few kept, the one not used longest goes.
    private void ForgetOldest(int keep)
    {
        int count = 0, oldest = -1;
        for (int i = 0; i < pictures.Length; i++)
        {
            if (pictures[i] == null) continue;
            count++;
            if (i != keep && (oldest < 0 || usedAt[i] < usedAt[oldest])) oldest = i;
        }
        if (count > Kept && oldest >= 0) Drop(oldest);
    }

    private void Drop(int index)
    {
        var picture = pictures[index];
        pictures[index] = null;
        if (picture == null) return;
        if (target == picture) target = null;
        if (lens != null && lens.targetTexture == picture) lens.targetTexture = null;
        picture.Release();
        Destroy(picture);
    }

    // ---- The mirror's camera ----

    private void StartDrawing()
    {
        // Never without its picture: a camera with no target draws over the whole screen.
        if (target == null) { StopDrawing(); return; }
        if (lens == null) MakeLens();
        if (lens.targetTexture != target) lens.targetTexture = target;
        // Drawn before the eye's camera (lower depth), so the eye sees this frame's picture; with what the eye draws.
        lens.depth = eye.depth - 1f;
        lens.cullingMask = eye.cullingMask;
        lens.clearFlags = eye.clearFlags;
        lens.backgroundColor = eye.backgroundColor;
        lens.GetUniversalAdditionalCameraData().renderShadows = HubLobby.Number("mirrorShadows", 1f) > .5f;
        if (!lens.gameObject.activeSelf) lens.gameObject.SetActive(true);
        lens.enabled = true;
    }

    private void StopDrawing()
    {
        if (lens == null) return;
        lens.enabled = false;
        // Switched off whole when not drawing, so nothing that looks for cameras ever finds it.
        if (lens.gameObject.activeSelf) lens.gameObject.SetActive(false);
    }

    private void MakeLens()
    {
        var host = new GameObject("Mirror Camera");
        host.transform.SetParent(transform, false);
        host.SetActive(false);
        lens = host.AddComponent<Camera>();
        lens.enabled = false;
        lens.allowHDR = true;
        lens.allowMSAA = true;
        lens.useOcclusionCulling = false;
        var data = lens.GetUniversalAdditionalCameraData();
        data.renderType = CameraRenderType.Base;
        data.renderPostProcessing = false;
        data.antialiasing = AntialiasingMode.None;
        data.requiresColorOption = CameraOverrideOption.Off;
        data.requiresDepthOption = CameraOverrideOption.Off;
        data.stopNaN = false;
        data.dithering = false;
    }

    // Just before the mirror's camera culls and draws, from where the eye is now: the eye's reflection, looking out of
    // the glass, its frustum the seen part of the glass at the glass. The glass out of its own picture, and this PC's
    // player drawn whole.
    private void BeforeRender(ScriptableRenderContext context, Camera camera)
    {
        if (camera == null || camera != lens || eye == null) return;
        Vector3 at = eye.transform.position;
        float d = InFront(at);
        if (d <= Front * .5f) return;
        Vector3 mirrored = at - 2f * d * Normal;
        lens.transform.SetPositionAndRotation(mirrored, Quaternion.LookRotation(Normal, Up));
        // On the camera's axes (its right is the glass's -Across), a point of the glass at (a, b) is at (cx - a, cy + b)
        // and d away; at the near plane, scaled to it.
        Vector3 toCenter = Center - mirrored;
        float cx = -Vector3.Dot(toCenter, Across), cy = Vector3.Dot(toCenter, Up);
        float near = d + NearPast, k = near / d;
        float far = near + Mathf.Max(5f, HubLobby.Number("mirrorFar", 60f));
        lens.nearClipPlane = near;
        lens.farClipPlane = far;
        lens.projectionMatrix = Matrix4x4.Frustum((cx - a1) * k, (cx - a0) * k, (cy + b0) * k, (cy + b1) * k, near, far);
        material.SetTexture(MainTexId, lens.targetTexture);
        material.SetVector(WindowId, Window());
        if (glass != null) glass.forceRenderingOff = true;
        ShowOwn();
    }

    private void AfterRender(ScriptableRenderContext context, Camera camera)
    {
        if (camera == null || camera != lens) return;
        if (glass != null) glass.forceRenderingOff = false;
        ShowOwnAgain();
    }

    // The glass's uv (0-1, u to the right of someone facing it) to the picture's: the picture's left is the glass's
    // a1 side (it looks out of the glass, so left and right swap), its bottom the glass's b0.
    private Vector4 Window()
    {
        Vector2 size = Size;
        float spanA = Mathf.Max(.0001f, a1 - a0), spanB = Mathf.Max(.0001f, b1 - b0);
        return new Vector4(-size.x / spanA, size.y / spanB, (a1 + size.x * .5f) / spanA, (-size.y * .5f - b0) / spanB);
    }

    // ---- This PC's own player, whole in the mirror ----

    private void ShowOwn()
    {
        shown.Clear();
        FindOwn();
        foreach (var renderer in own)
        {
            if (renderer == null || renderer.shadowCastingMode != ShadowCastingMode.ShadowsOnly) continue;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            shown.Add(renderer);
        }
    }

    private void ShowOwnAgain()
    {
        foreach (var renderer in shown)
            if (renderer != null) renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        shown.Clear();
    }

    // Found again when the player or their body changes (a new outfit makes the body again), and every half second for
    // what comes and goes on it (a gun slung on the back).
    private void FindOwn()
    {
        var mine = PlayerRoster.Local;
        var body = mine != null && mine.TryGetComponent(out PlayerBody found) ? found.BodyAnimator : null;
        if (mine == owner && body == ownerBody && Time.unscaledTime - ownFoundAt < .5f) return;
        owner = mine;
        ownerBody = body;
        ownFoundAt = Time.unscaledTime;
        own.Clear();
        if (mine != null) mine.GetComponentsInChildren(true, own);
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = new Color(1f, .31f, .47f, .8f);
        Gizmos.DrawRay(transform.position, transform.forward * .5f);
    }
}
