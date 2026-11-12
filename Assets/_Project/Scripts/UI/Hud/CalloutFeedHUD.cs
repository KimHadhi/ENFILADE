using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// The callout feed: when someone makes a callout, a small live picture of their face comes up on every teammate's HUD
/// and their own, like A.V.A.'s radio callouts (the user, 2026-11-12: "콜아웃할 때 콜아웃한 플레이어의 얼굴쪽 모습이
/// 콜아웃 모션과 함께 아군 전체한테 작게 화면으로 떴거든 … 적어도 자기가 콜아웃 할 때 자기 표정은 볼 수 있지 않을까?").
/// It is a real camera in the world facing the caller, so the battlefield shows behind them ("피 튀기는 전장에서 혼자
/// 뽀송하게만 나오면 이상할 거 같아서"): walls, blood, zombies, smoke, friends. The in-world reason is the squad's comms
/// video feed over the radio, so it runs choppy (FaceLife.csv Feed.rate) with faint scanlines and a "● 통신" tag.
/// The camera stands in front of the face, as far as a sphere cast from the head lets it, swinging aside when a wall is
/// in the way (as the third-person camera does), and draws the caller's third-person body with its gun; this PC's own
/// player is drawn whole for it, the head that first person shows only as a shadow included (HubMirror does the same).
/// One camera serves every window in turn, at most one small render a frame, only while a window shows; no MSAA, HDR,
/// post or shadows, a short far plane, occlusion culling on. The window is the HUD portrait's tile (SquarePortrait
/// shader: leaning slabs, black mat, cut corners, brackets) with the caller's name in their seat colour, in a column on
/// the right edge between the kill feed and the ammo, each leaning at its own angle (hud-tilt rule). It lasts as long as
/// the voice plus Feed.linger, then fades; at most Feed.most at once, the oldest giving way. Every PC makes its own from
/// its copy of the caller when CalloutRpc arrives (PlayerCallouts.Hear → PlayerFace.Said): nothing more goes over the
/// network. Off with Settings → 조작 → 콜아웃 통신 화면 (GameSettings.calloutFeed).
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.Cloth + 20)]
public sealed class CalloutFeedHUD : MonoBehaviour
{
    private const string TileShader = "UI/Custom/SquarePortrait";
    // The canvas, drawn over the team cards' (25).
    private const int Order = 26;
    // Most windows ever kept (the table's Feed.most is capped by it).
    private const int MostWindows = 3;
    private static readonly int MainTexId = Shader.PropertyToID("_MainTex");

    private sealed class Window
    {
        public RectTransform root, pictureRect;
        public RawImage picture, scan;
        public CanvasGroup group;
        public TextMeshProUGUI name, tag;
        public RenderTexture texture;
        public PlayerMovement speaker;
        public PlayerBody body;
        public float until, shown, lastDrawn = -99f, y, pop;
        // How far in from the right edge the window stands now: Feed.right, or past whatever shows beside it (HudRoom).
        // Below 0 until it is first placed.
        public float dodge = -1f;
        public bool open, drawn;
    }

    private static CalloutFeedHUD instance;
    private readonly Window[] windows = new Window[MostWindows];
    private readonly List<Window> order = new List<Window>(MostWindows);
    private readonly List<Renderer> own = new List<Renderer>(), shownOwn = new List<Renderer>();
    private RectTransform plane;
    private Camera lens;
    private Window drawing;
    private Material tile;
    private Texture2D lines;
    private PlayerMovement owner;
    private Animator ownerBody;
    private float ownFoundAt = float.NegativeInfinity;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    /// <summary>
    /// A callout <paramref name="speaker"/> said, heard on this PC: their face comes up for <paramref name="seconds"/>
    /// (the voice) and a moment more. A new one of theirs while it shows keeps the same window.
    /// </summary>
    public static void Show(PlayerMovement speaker, float seconds)
    {
        if (speaker == null || !GameSettings.Current.calloutFeed || ResultsShow.IsPlaying || HubDressing.IsOpen) return;
        if (!speaker.TryGetComponent(out PlayerBody body) || body.BodyAnimator == null) return;
        if (instance == null) instance = new GameObject("Callout Feed", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)).AddComponent<CalloutFeedHUD>();
        instance.Open(speaker, body, seconds);
    }

    private void Awake()
    {
        FeelUI.SetupHud(gameObject, Order);
        plane = HudPlane.Create("Right Plane", transform, HudPlane.Side.Right);
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
    }

    private void OnDestroy()
    {
        if (instance == this) instance = null;
        foreach (var window in windows)
            if (window != null && window.texture != null)
            {
                window.texture.Release();
                Destroy(window.texture);
            }
        if (tile != null) Destroy(tile);
        if (lines != null) Destroy(lines);
    }

    // ---- Windows ----

    private void Open(PlayerMovement speaker, PlayerBody body, float seconds)
    {
        float now = Time.unscaledTime;
        float last = Mathf.Max(.5f, seconds) + Mathf.Max(0f, FaceTables.Number("Feed.linger", 1f));
        Window window = null;
        foreach (var open in order)
            if (open.speaker == speaker) window = open;
        if (window == null)
        {
            int most = Mathf.Clamp(Mathf.RoundToInt(FaceTables.Number("Feed.most", 2f)), 1, MostWindows);
            if (order.Count >= most)
            {
                // The oldest gives way at once.
                window = order[0];
                order.RemoveAt(0);
            }
            else
                for (int i = 0; i < windows.Length && window == null; i++)
                {
                    if (windows[i] == null) windows[i] = Build(i);
                    if (!windows[i].open && !order.Contains(windows[i])) window = windows[i];
                }
            if (window == null) return;
            window.until = 0f;
            window.pop = 0f;
            window.drawn = false;
            window.lastDrawn = -99f;
            window.dodge = -1f;
            window.shown = now;
            order.Add(window);
            window.y = Slot(order.Count - 1);
        }
        window.speaker = speaker;
        window.body = body;
        window.open = true;
        window.until = Mathf.Max(window.until, now + last);
        Name(window);
    }

    private Window Build(int index)
    {
        float side = Mathf.Max(64f, FaceTables.Number("Feed.tile", 196f));
        float half = Mathf.Clamp(FaceTables.Number("Feed.half", .38f), .2f, .44f);
        float picture = side * half * 2f;
        var window = new Window { root = FeelUI.Rect("Callout Feed " + index, plane) };
        window.root.anchorMin = window.root.anchorMax = Vector2.one;
        window.root.sizeDelta = new Vector2(side, side);
        window.group = window.root.gameObject.AddComponent<CanvasGroup>();
        window.group.alpha = 0f;
        window.group.blocksRaycasts = window.group.interactable = false;

        int size = Mathf.Clamp(Mathf.RoundToInt(FaceTables.Number("Feed.size", 256f)), 64, 1024);
        window.texture = new RenderTexture(size, size, 16, RenderTextureFormat.ARGB32)
        {
            name = "Callout Feed " + index, antiAliasing = 1, useMipMap = false, filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.HideAndDontSave
        };
        window.texture.Create();

        var frame = FeelUI.Rect("Tile", window.root);
        frame.anchorMin = Vector2.zero;
        frame.anchorMax = Vector2.one;
        frame.offsetMin = frame.offsetMax = Vector2.zero;
        window.picture = frame.gameObject.AddComponent<RawImage>();
        window.picture.raycastTarget = false;
        window.picture.texture = window.texture;
        var look = Tile(half);
        if (look != null) window.picture.material = look;

        // The feed's faint scanlines over the picture, a line every three pixels.
        window.pictureRect = FeelUI.Rect("Lines", window.root);
        window.pictureRect.sizeDelta = new Vector2(picture, picture);
        window.scan = window.pictureRect.gameObject.AddComponent<RawImage>();
        window.scan.raycastTarget = false;
        window.scan.texture = Lines();
        window.scan.uvRect = new Rect(0f, 0f, 1f, picture / 3f);
        window.scan.color = new Color(0f, 0f, 0f, Mathf.Clamp01(FaceTables.Number("Feed.scan", .16f)));

        window.tag = FeelUI.ScoreLabel("Tag", window.root, 12f, TextAlignmentOptions.TopLeft);
        window.tag.rectTransform.anchorMin = window.tag.rectTransform.anchorMax = new Vector2(.5f, .5f);
        window.tag.rectTransform.pivot = new Vector2(0f, 1f);
        window.tag.rectTransform.sizeDelta = new Vector2(picture * .6f, 16f);
        window.tag.rectTransform.anchoredPosition = new Vector2(-picture * .5f + 7f, picture * .5f - 5f);
        window.tag.text = Texts.Get("callout.feed.tag");
        window.tag.color = KitStyle.Accent;

        var plate = FeelUI.Rect("Name Plate", window.root);
        // Just under the tile's slabs (the picture's half plus the mat and slab past it).
        plate.sizeDelta = new Vector2(picture, 22f);
        plate.anchoredPosition = new Vector2(0f, -side * (half + .035f) - 12f);
        var plateImage = plate.gameObject.AddComponent<Image>();
        plateImage.color = KitStyle.Panel;
        plateImage.raycastTarget = false;
        window.name = FeelUI.ScoreLabel("Name", plate, 14f, TextAlignmentOptions.Center);
        window.name.rectTransform.anchorMin = Vector2.zero;
        window.name.rectTransform.anchorMax = Vector2.one;
        window.name.rectTransform.offsetMin = new Vector2(6f, 0f);
        window.name.rectTransform.offsetMax = new Vector2(-6f, 0f);
        window.name.overflowMode = CombatUIAssets.ClippedOverflow(window.name);
        return window;
    }

    // The caller's name in their seat colour, this PC's own in the accent pink as everywhere else.
    private static void Name(Window window)
    {
        bool named = window.speaker.TryGetComponent(out PlayerTally tally);
        string shown = named && !string.IsNullOrEmpty(tally.DisplayName) ? tally.DisplayName : window.speaker.name;
        window.name.text = shown;
        window.name.color = window.speaker.IsLocal ? KitStyle.Accent : named ? tally.Color : KitStyle.Text;
    }

    // The HUD portrait's own tile look (its material when the HUD has one, else the shader), with a larger picture.
    private Material Tile(float half)
    {
        if (tile != null) return tile;
        Material source = null;
        var portrait = FindAnyObjectByType<HudPortrait>(FindObjectsInactive.Include);
        if (portrait != null && portrait.TryGetComponent(out RawImage image) && image.material != null
            && image.material.shader != null && image.material.shader.name == TileShader)
            source = image.material;
        var shader = source != null ? source.shader : Shader.Find(TileShader);
        if (shader == null) return null;
        tile = source != null ? new Material(source) : new Material(shader);
        tile.hideFlags = HideFlags.HideAndDontSave;
        tile.SetFloat("_Half", half);
        tile.SetFloat("_SlabHalf", Mathf.Min(.48f, half + .03f));
        tile.SetFloat("_ShadowHalf", Mathf.Min(.48f, half + .025f));
        return tile;
    }

    private Texture2D Lines()
    {
        if (lines != null) return lines;
        lines = new Texture2D(1, 3, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Repeat, filterMode = FilterMode.Point, hideFlags = HideFlags.HideAndDontSave };
        lines.SetPixels32(new[] { new Color32(255, 255, 255, 0), new Color32(255, 255, 255, 0), new Color32(255, 255, 255, 255) });
        lines.Apply(false, true);
        return lines;
    }

    // Down the right edge from Feed.top, a window's height and its plate apart (px at 1080p, from the top).
    private static float Slot(int index)
    {
        float side = Mathf.Max(64f, FaceTables.Number("Feed.tile", 196f));
        float gap = Mathf.Max(0f, FaceTables.Number("Feed.gap", 18f));
        return FaceTables.Number("Feed.top", 300f) + side * .5f + index * (side + 28f + gap);
    }

    private void LateUpdate()
    {
        float now = Time.unscaledTime, dt = Time.unscaledDeltaTime;
        bool hidden = ResultsShow.IsPlaying || HubDressing.IsOpen || !GameSettings.Current.calloutFeed;
        float popTime = Mathf.Max(.01f, FaceTables.Number("Feed.pop", .18f));
        float fadeTime = Mathf.Max(.01f, FaceTables.Number("Feed.fade", .3f));
        float tilt = GameSettings.Current.hudTilt;
        float right = FaceTables.Number("Feed.right", 74f), side = Mathf.Max(64f, FaceTables.Number("Feed.tile", 196f));
        // The name plate hangs under the tile: the picture's half, its mat and slab, the plate's 12 px drop and half its 22.
        float half = Mathf.Clamp(FaceTables.Number("Feed.half", .38f), .2f, .44f), below = side * (half + .035f) + 23f;
        float roomGap = HudRoom.Gap, follow = 1f - Mathf.Exp(-dt * Mathf.Max(.1f, HudRoom.Follow));
        for (int i = order.Count - 1; i >= 0; i--)
        {
            var window = order[i];
            if (window.speaker == null || window.body == null || hidden || now >= window.until) window.open = false;
            float target = window.open ? 1f : 0f;
            // Comes in quickly, goes out a little slower; gone, its slot is free.
            window.pop = Mathf.MoveTowards(window.pop, target, dt / (window.open ? popTime : fadeTime));
            if (!window.open && window.pop <= 0f)
            {
                order.RemoveAt(i);
                window.speaker = null;
                window.body = null;
                window.until = 0f;
                window.group.alpha = 0f;
            }
        }
        for (int i = 0; i < order.Count; i++)
        {
            var window = order[i];
            window.y = Mathf.Lerp(window.y, Slot(i), 1f - Mathf.Exp(-dt * 14f));
            float slide = (1f - Smooth(window.pop)) * 60f;
            // The right edge is shared: the weapon slots stand there while they show and a long kill feed reaches down past
            // Feed.top (the overlap check measured a window 107.5 px over the slots in play, 2026-11-13). The window keeps
            // the edge while it is free and steps in past whatever shows beside it, back out once that has gone.
            float reach = HudRoom.Reach(window.y - side * .5f, window.y + below, this);
            float stand = reach > 0f ? Mathf.Max(right, reach + roomGap) : right;
            window.dodge = window.dodge < 0f ? stand : Mathf.Lerp(window.dodge, stand, follow);
            window.root.anchoredPosition = new Vector2(-window.dodge - side * .5f + slide, -window.y);
            window.root.localRotation = Quaternion.Euler(0f, (FaceTables.Number("Feed.yaw", -8f) - i * FaceTables.Number("Feed.fan", 3f)) * tilt,
                FaceTables.Number("Feed.roll", -1.2f) * tilt * (i % 2 == 0 ? 1f : -1f));
            // Shown only once it has a picture: the texture is black before its first draw.
            window.group.alpha = window.drawn ? Smooth(window.pop) : 0f;
        }
        Draw(now);
    }

    // ---- The feed's camera ----

    // At most one window a frame, the one waiting longest, each at most Feed.rate times a second.
    private void Draw(float now)
    {
        drawing = null;
        float every = 1f / Mathf.Clamp(FaceTables.Number("Feed.rate", 15f), 1f, 60f);
        foreach (var window in order)
        {
            if (!window.open || window.body == null || now - window.lastDrawn < every) continue;
            if (drawing == null || window.lastDrawn < drawing.lastDrawn) drawing = window;
        }
        if (drawing == null || !Place(drawing))
        {
            drawing = null;
            StopLens();
            return;
        }
        drawing.lastDrawn = now;
        drawing.drawn = true;
    }

    // The camera in front of the caller's face, clear of walls, looking at their face and shoulders.
    private bool Place(Window window)
    {
        var animator = window.body.BodyAnimator;
        var head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        if (head == null) return false;
        Vector3 up = Vector3.up;
        var view = window.body.View;
        Vector3 look = view != null ? view.forward : window.speaker.transform.forward;
        Vector3 flat = Vector3.ProjectOnPlane(look, up);
        if (flat.sqrMagnitude < 1e-4f) flat = Vector3.ProjectOnPlane(window.speaker.transform.forward, up);
        flat = flat.sqrMagnitude < 1e-4f ? Vector3.forward : flat.normalized;
        // A little of the look's pitch, so a face turned up or down is still met from the front.
        Vector3 facing = Vector3.Slerp(flat, look.normalized, .3f).normalized;
        Vector3 rightWay = Vector3.Cross(up, flat).normalized;
        Vector3 face = head.position + up * FaceTables.Number("Feed.aim", .02f);
        Vector3 offset = facing * Mathf.Max(.2f, FaceTables.Number("Feed.distance", .85f))
            + rightWay * FaceTables.Number("Feed.side", .12f) + up * FaceTables.Number("Feed.lift", .04f);
        int walls = window.speaker.groundLayers & ~LayerMask.GetMask("Enemy");
        float closest = Mathf.Max(.15f, FaceTables.Number("Feed.closest", .35f));
        Vector3 at = Clear(face, facing, offset, walls);
        if ((at - face).magnitude < closest)
        {
            // Too close to a wall: swing aside, the clearer side wins.
            float swing = FaceTables.Number("Feed.swing", 40f);
            Vector3 left = Clear(face, facing, Quaternion.AngleAxis(-swing, up) * offset, walls);
            Vector3 rightSide = Clear(face, facing, Quaternion.AngleAxis(swing, up) * offset, walls);
            Vector3 best = (left - face).sqrMagnitude >= (rightSide - face).sqrMagnitude ? left : rightSide;
            if ((best - face).sqrMagnitude > (at - face).sqrMagnitude) at = best;
        }
        Vector3 aim = face - at;
        if (aim.sqrMagnitude < 1e-4f) return false;
        if (lens == null) MakeLens();
        var main = Camera.main;
        int ui = LayerMask.NameToLayer("UI");
        int mask = main != null ? main.cullingMask : ~0;
        if (ui >= 0) mask &= ~(1 << ui);
        lens.cullingMask = mask;
        lens.clearFlags = main != null ? main.clearFlags : CameraClearFlags.Skybox;
        lens.backgroundColor = main != null ? main.backgroundColor : KitStyle.Night;
        lens.depth = (main != null ? main.depth : 0f) - 2f;
        lens.fieldOfView = Mathf.Clamp(FaceTables.Number("Feed.fov", 34f), 10f, 90f);
        lens.nearClipPlane = Mathf.Clamp(FaceTables.Number("Feed.near", .05f), .01f, .3f);
        lens.farClipPlane = Mathf.Max(5f, FaceTables.Number("Feed.far", 40f));
        lens.targetTexture = window.texture;
        lens.GetUniversalAdditionalCameraData().renderShadows = FaceTables.Number("Feed.shadows", 0f) > .5f;
        lens.transform.SetPositionAndRotation(at, Quaternion.LookRotation(aim, up));
        if (!lens.gameObject.activeSelf) lens.gameObject.SetActive(true);
        lens.enabled = true;
        return true;
    }

    // As far out along the offset as the walls let a sphere go, starting just in front of the face.
    private static Vector3 Clear(Vector3 face, Vector3 facing, Vector3 offset, int walls)
    {
        Vector3 start = face + facing * .12f;
        Vector3 to = face + offset - start;
        float length = to.magnitude;
        if (length < 1e-3f) return face + offset;
        float radius = Mathf.Max(.02f, FaceTables.Number("Feed.radius", .1f));
        if (Physics.SphereCast(start, radius, to / length, out RaycastHit hit, length, walls, QueryTriggerInteraction.Ignore))
            return start + to / length * Mathf.Max(0f, hit.distance - .02f);
        return face + offset;
    }

    private void MakeLens()
    {
        var host = new GameObject("Callout Feed Camera");
        host.transform.SetParent(transform, false);
        host.SetActive(false);
        lens = host.AddComponent<Camera>();
        lens.enabled = false;
        lens.allowHDR = false;
        lens.allowMSAA = false;
        lens.useOcclusionCulling = true;
        var data = lens.GetUniversalAdditionalCameraData();
        data.renderType = CameraRenderType.Base;
        data.renderPostProcessing = false;
        data.antialiasing = AntialiasingMode.None;
        // On, though nothing here reads it. URP 17.3's screen-space decal pass takes its UV flip from the camera's
        // intermediate colour target (RenderingUtils.SetScaleBiasRt). A camera that draws straight into a RenderTexture
        // with no post-processing, no MSAA and no opaque texture gets no such target, so the pass threw a
        // NullReferenceException on every feed picture. Asking for the opaque texture keeps that target, and the
        // battlefield's blood decals still show behind the face.
        data.requiresColorOption = CameraOverrideOption.On;
        data.requiresDepthOption = CameraOverrideOption.Off;
        data.renderShadows = false;
        data.stopNaN = false;
        data.dithering = false;
    }

    private void StopLens()
    {
        if (lens == null) return;
        lens.enabled = false;
        // Switched off whole when not drawing, so nothing that looks for cameras ever finds it.
        if (lens.gameObject.activeSelf) lens.gameObject.SetActive(false);
    }

    private void BeforeRender(ScriptableRenderContext context, Camera camera)
    {
        if (camera == null || camera != lens) return;
        ShowOwn();
    }

    private void AfterRender(ScriptableRenderContext context, Camera camera)
    {
        if (camera == null || camera != lens) return;
        ShowOwnAgain();
        // Drawn once: off until the next window's turn.
        lens.enabled = false;
    }

    // This PC's own player drawn whole for the feed: the head and what is worn over the face, which first person keeps
    // to shadows only (PlayerBody.ApplyHeadVisibility), back on for this camera alone.
    private void ShowOwn()
    {
        shownOwn.Clear();
        FindOwn();
        foreach (var renderer in own)
        {
            if (renderer == null || renderer.shadowCastingMode != ShadowCastingMode.ShadowsOnly) continue;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            shownOwn.Add(renderer);
        }
    }

    private void ShowOwnAgain()
    {
        foreach (var renderer in shownOwn)
            if (renderer != null) renderer.shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        shownOwn.Clear();
    }

    // Found again when the player or their body changes, and every half second for what comes and goes on it.
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

    private static float Smooth(float t)
    {
        t = Mathf.Clamp01(t);
        return t * t * (3f - 2f * t);
    }
}
