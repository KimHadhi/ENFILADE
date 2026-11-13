using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

/// <summary>
/// The dressing spot at the hub's mirror (user, 2026-11-12: "설정에서 캐릭터 바꾸는 거 이제 없애도 될 듯. 대신 여기 로비 무대
/// 엔필레이드 쪽에 거울 넣어서 자기 볼 수 있게 만들고 여기서 자기 캐릭터 커스터마이징 하는 기능이나 공간 넣으면 될 듯"). [F] at the
/// glass (HubLobby) opens it:
/// - The player is stood on the spot in front of the glass (Hub.csv dressDistance; moved along the glass away from anyone
///   standing there already), turned to it and looking level, the gun put away (slung on the back, where everyone sees
///   it), moving and fighting held, the cursor free.
/// - A camera of its own looks into the mirror from behind where the player stands, framing their reflection whole
///   between the wardrobe's wings, a little from above (dressFov, dressFill, dressLift). The player's own body is left out
///   of this camera's picture only, so it never stands in the way, and is drawn whole in the mirror (HubMirror). The
///   player's own camera draws nothing under it meanwhile, and the HUD, which that camera carries, is hidden under it.
/// - The title screen's wardrobe pages (MainMenuWardrobe) stand over it with this as their stage (IWardrobeStage): the
///   character cards and colour chips, the outfit slots by body part, the body page. A pick is worn on the player's own
///   body at once, so it shows in the mirror; it is saved as on the title screen (GameSettings, OutfitLook) and reaches
///   the room the way every look does (PlayerNetworkPose). Opening a slot or a body tab brings the camera close on that
///   part's reflection: nearer the glass, then, no nearer than dressGap, by narrowing the lens; the mirror's picture
///   follows the part seen, so it stays sharp. The pointer on a slot's tile marks its pieces on the body, pink and
///   breathing, in the glass. Nothing is tried on under the pointer: the body is the player's own, made again whole for
///   every outfit (its clips, hands and cloth), which a sweep of the pointer over a list would do a dozen times.
/// - Turning: dragging across the empty middle, as on the title screen, or holding A/D (←/→) turns the player round in
///   place, so the back shows in the glass (dressDrag, dressTurn).
/// - Esc goes back from a slot's list first, then closes; [F] (Player/Interact) closes at once, as do going down, dying
///   and the results starting. The camera is the player's own again, the gun comes back out, input and cursor as before.
/// The look is the title screen's (the menus' identity): its wardrobe pages, its bottom bar of key hints (MainMenuTicker),
/// near-black shades behind the wings, white and one pink.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.Look - 1)]
public sealed class HubDressing : MonoBehaviour, IWardrobeStage
{
    // Over the HUD's canvases, under the settings window (1000) and the console.
    private const int Order = 90;
    private const float Edge = 56f;
    // How quickly the player turns to the asked way and the view levels (1/s), and how many frames after the body is
    // made again it is framed again (the first frames stand it as its file was made).
    private const float TurnRate = 12f, PitchRate = 6f;
    private const int SettleFrames = 3;

    private static HubDressing instance;
    private static int closedFrame = -1;

    private HubMirror mirror;
    private PlayerMovement mine;
    private PlayerBody body;
    private PlayerLook look;
    private PlayerWeaponSlots slots;
    private CombatHeadMotion headMotion;
    private SinglePlayerChatManager chat;
    private Camera main, view;
    private int mainMask, heldSlot = -1, openedFrame, settleLeft;
    private bool open, masked, mainPost;
    private RectTransform canvas;
    private MainMenuWardrobe wardrobe;
    // The way the player is asked to face (world yaw, degrees): to the glass, then as turned.
    private float facing;
    // The part of the body in view (an OutfitSlot group or a body tab; null: all of it), and where the camera goes for it.
    private string focus;
    private bool refocus = true;
    private Animator framed;
    private Vector3 goalAt;
    private Quaternion goalTurn = Quaternion.identity;
    private float goalFov = 34f;
    private string highlighted;
    // This PC's player's renderers, left out of this camera's picture while it draws.
    private readonly List<Renderer> own = new List<Renderer>();
    private readonly List<(Renderer renderer, bool off)> hidden = new List<(Renderer, bool)>();
    private Animator ownBody;
    private float ownFoundAt = float.NegativeInfinity;

    /// <summary>The dressing spot is open on this PC.</summary>
    public static bool IsOpen => instance != null && instance.open;
    /// <summary>It has the keyboard (open, or closed by a key this very frame): Esc mustn't also open the settings, nor the
    /// [F] that closed it work a station.</summary>
    public static bool TakesKeyboard => IsOpen || Time.frameCount == closedFrame;
    /// <summary>The camera the mirror is seen through while it is open (HubMirror draws for it).</summary>
    public static Camera View => IsOpen ? instance.view : null;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        closedFrame = -1;
    }

    /// <summary>Opens the spot at <paramref name="at"/> for this PC's <paramref name="who"/>; false when it can't now (in the
    /// air, down, another menu open, the results playing).</summary>
    public static bool Open(HubMirror at, PlayerMovement who)
    {
        if (IsOpen || at == null || who == null || !who.IsInitialized || who.IsDead || who.IsDowned || !who.IsGrounded
            || ResultsShow.IsPlaying || ShopMenu.IsOpen || GunCustomizeMenu.IsOpen || SettingsMenu.IsOpen || PerkCardScreen.TakesKeyboard)
            return false;
        if (!who.TryGetComponent(out PlayerBody whose) || whose.BodyAnimator == null) return false;
        instance = new GameObject("Hub Dressing").AddComponent<HubDressing>();
        RuntimeSceneHierarchy.Group(instance.gameObject, RuntimeSceneHierarchy.Systems, "Hub");
        instance.Begin(at, who, whose);
        return true;
    }

    // ---- Opening ----

    private void Begin(HubMirror at, PlayerMovement who, PlayerBody whose)
    {
        mirror = at;
        mine = who;
        body = whose;
        look = who.GetComponent<PlayerLook>();
        slots = who.GetComponent<PlayerWeaponSlots>();
        headMotion = CombatHeadMotion.For(who);
        chat = SceneOne<SinglePlayerChatManager>.Get();
        open = true;
        openedFrame = Time.frameCount;
        who.MoveTo(Spot() + Vector3.up * .05f);
        Vector3 toGlass = -mirror.Normal;
        facing = Mathf.Atan2(toGlass.x, toGlass.z) * Mathf.Rad2Deg;
        // The gun away: slung on the back it shows in the glass, and the hands hang free.
        heldSlot = slots != null && !slots.HandsFull ? slots.SelectedSlot : -1;
        if (heldSlot >= 0) slots.SelectSlot(-1);
        InputHold.Take(this);
        SettingsMenu.EnsureEventSystem();
        MakeView();
        BuildCanvas();
        RenderPipelineManager.beginCameraRendering += BeforeRender;
        RenderPipelineManager.endCameraRendering += AfterRender;
        KitSound.Play(KitSound.Cue.Ping, .5f);
    }

    // On the spot in front of the glass where the player is across it, moved along it away from anyone standing there.
    private Vector3 Spot()
    {
        Vector3 at = mine.transform.position;
        float half = Mathf.Max(0f, mirror.Size.x * .5f - .45f);
        float across = Mathf.Clamp(Vector3.Dot(at - mirror.Center, mirror.Across), -half, half);
        float distance = HubLobby.Number("dressDistance", 1.2f);
        for (int tries = 0; tries < 4; tries++)
        {
            Vector3 spot = SpotAt(across, distance, at.y);
            PlayerMovement near = null;
            float nearest = .8f;
            foreach (var someone in PlayerRoster.All)
            {
                if (someone == null || someone == mine) continue;
                Vector3 off = someone.transform.position - spot;
                off.y = 0f;
                if (off.magnitude >= nearest) continue;
                nearest = off.magnitude;
                near = someone;
            }
            if (near == null) break;
            float away = Vector3.Dot(near.transform.position - spot, mirror.Across) > 0f ? -1f : 1f;
            float next = Mathf.Clamp(across + away * .9f, -half, half);
            if (Mathf.Abs(next - across) < .01f) next = Mathf.Clamp(across - away * .9f, -half, half);
            across = next;
        }
        return SpotAt(across, distance, at.y);
    }

    private Vector3 SpotAt(float across, float distance, float floor)
    {
        Vector3 spot = mirror.Center + mirror.Across * across + mirror.Normal * distance;
        spot.y = floor;
        return spot;
    }

    // The camera into the mirror, drawn over the player's own (as the results' is), starting where the eyes are and gliding
    // to its place. The player's camera draws nothing meanwhile and grades nothing: it is covered whole.
    private void MakeView()
    {
        main = body.View != null ? body.View.GetComponent<Camera>() : null;
        if (main == null) main = Camera.main;
        var host = new GameObject("Dressing Camera");
        view = host.AddComponent<Camera>();
        view.depth = (main != null ? main.depth : 0f) + 10f;
        view.nearClipPlane = .05f;
        view.farClipPlane = main != null ? Mathf.Max(150f, main.farClipPlane) : 500f;
        view.cullingMask = main != null ? main.cullingMask : ~0;
        view.clearFlags = main != null ? main.clearFlags : CameraClearFlags.Skybox;
        view.backgroundColor = main != null ? main.backgroundColor : Color.black;
        view.allowHDR = main == null || main.allowHDR;
        view.allowMSAA = main == null || main.allowMSAA;
        var data = view.GetUniversalAdditionalCameraData();
        data.renderType = CameraRenderType.Base;
        data.renderPostProcessing = true;
        goalFov = view.fieldOfView = main != null ? main.fieldOfView : 60f;
        if (main == null) return;
        var mainData = main.GetUniversalAdditionalCameraData();
        data.antialiasing = mainData.antialiasing;
        data.antialiasingQuality = mainData.antialiasingQuality;
        host.transform.SetPositionAndRotation(main.transform.position, main.transform.rotation);
        goalAt = main.transform.position;
        goalTurn = main.transform.rotation;
        mainMask = main.cullingMask;
        mainPost = mainData.renderPostProcessing;
        main.cullingMask = 0;
        mainData.renderPostProcessing = false;
        masked = true;
    }

    // The player's moving and fighting wait, as for the results; whatever was on comes back after.
    // ---- The screen: the title screen's wardrobe over the mirror ----

    private void BuildCanvas()
    {
        var host = new GameObject("Hub Dressing UI", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        // Over the screen, not through the HUD camera: that one draws under the dressing camera.
        var layer = host.GetComponent<Canvas>();
        layer.renderMode = RenderMode.ScreenSpaceOverlay;
        layer.sortingOrder = Order;
        var scaler = host.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;
        canvas = host.GetComponent<RectTransform>();
        BuildShade();
        BuildTop();
        // Between the bars where the title screen has them, so its pages stand as they do there.
        wardrobe = MainMenuWardrobe.Make(canvas, Edge, MainMenuTopBar.Height, MainMenuTicker.Height, this);
        var ticker = MainMenuTicker.Make(canvas, Edge, new List<(string, string)>
        {
            ("Esc", Texts.Get("hub.mirror.hint.close")),
            (Texts.Get("hub.mirror.hint.turn.key"), Texts.Get("hub.mirror.hint.turn")),
        });
        ticker.Open(.05f);
        KitGrain.Make(canvas);
        wardrobe.Open();
    }

    // Near-black at the screen's edges and deeper behind the wings, so their words read over the hall; the shade is the
    // empty middle a drag turns the player on, as the title screen's is.
    private void BuildShade()
    {
        var shade = KitStyle.Stretch("Shade", canvas);
        var edges = shade.gameObject.AddComponent<RawImage>();
        edges.texture = KitShapes.Vignette;
        edges.color = KitStyle.With(KitStyle.Night, .7f);
        edges.raycastTarget = true; // catches the drags
        shade.gameObject.AddComponent<MainMenuDrag>().Preview = this;
        Side("Left Shade", false, 900f, .84f);
        Side("Right Shade", true, 820f, .78f);
    }

    private void Side(string name, bool right, float width, float alpha)
    {
        var side = FeelUI.Rect(name, canvas);
        side.anchorMin = new Vector2(right ? 1f : 0f, 0f);
        side.anchorMax = new Vector2(right ? 1f : 0f, 1f);
        side.pivot = new Vector2(right ? 1f : 0f, .5f);
        side.sizeDelta = new Vector2(width, 0f);
        var image = side.gameObject.AddComponent<RawImage>();
        image.texture = KitShapes.Fade;
        image.color = KitStyle.With(KitStyle.Night, alpha);
        image.uvRect = right ? new Rect(1f, 0f, -1f, 1f) : new Rect(0f, 0f, 1f, 1f);
        image.raycastTarget = false;
    }

    // A slim band in the title screen's top bar's place: the mirror's name at the left, leaning as the menus' titles do,
    // and a 닫기 chip at the right for the mouse.
    private void BuildTop()
    {
        var band = FeelUI.Rect("Top Bar", canvas);
        band.anchorMin = new Vector2(0f, 1f);
        band.anchorMax = Vector2.one;
        band.pivot = new Vector2(.5f, 1f);
        band.sizeDelta = new Vector2(0f, MainMenuTopBar.Height);
        KitStyle.Fill(band, KitStyle.With(KitStyle.Night, .88f));
        var line = FeelUI.Rect("Line", band);
        line.anchorMin = Vector2.zero;
        line.anchorMax = new Vector2(1f, 0f);
        line.pivot = new Vector2(.5f, 0f);
        line.sizeDelta = new Vector2(0f, 1f);
        KitStyle.Fill(line, KitStyle.With(KitStyle.Accent, .3f));
        KitSkew.On(KitStyle.Fill(KitStyle.TopLeft("Mark", band, new Vector2(Edge, 19f), new Vector2(6f, 26f)), KitStyle.Accent), 6f);
        var title = KitStyle.Flat(KitStyle.TopLeft("Title", band, new Vector2(Edge + 18f, 8f), new Vector2(200f, 48f)),
            Texts.Get("hub.mirror.title"), 30f, TextAlignmentOptions.MidlineLeft);
        title.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 3f);
        float after = Edge + 30f + title.GetPreferredValues(title.text).x;
        var where = KitStyle.Flat(KitStyle.TopLeft("Where", band, new Vector2(after, 10f), new Vector2(520f, 44f)),
            Texts.Get("hub.mirror.where"), 15f, TextAlignmentOptions.MidlineLeft, KitStyle.Soft);
        where.fontStyle = FontStyles.Normal;

        var chip = FeelUI.Rect("Close", band);
        chip.anchorMin = chip.anchorMax = chip.pivot = new Vector2(1f, .5f);
        chip.anchoredPosition = new Vector2(-Edge, 0f);
        chip.sizeDelta = new Vector2(132f, 34f);
        var back = KitStyle.Fill(chip, KitStyle.With(KitStyle.Ice, .09f));
        KitSkew.On(back, 7f);
        back.raycastTarget = true;
        KitStyle.Flat(KitStyle.Stretch("Text", chip), Texts.Get("hub.mirror.close"), 15f, TextAlignmentOptions.Center);
        var button = chip.gameObject.AddComponent<Button>();
        button.transition = Selectable.Transition.None;
        button.targetGraphic = back;
        button.onClick.AddListener(Close);
        chip.gameObject.AddComponent<KitHover>();
    }

    // ---- Every frame ----

    private void Update()
    {
        if (!open) return;
        if (mine == null || body == null || mirror == null || !mine.IsInitialized || mine.IsDead || mine.IsDowned || ResultsShow.IsPlaying)
        {
            Close();
            return;
        }
        float dt = Time.unscaledDeltaTime;
        // Keys wait while the console or the chat has the keyboard (their Esc is theirs).
        bool keys = !DevConsole.TakesKeyboard && !(chat != null && chat.BlocksMissionInputThisFrame) && !SettingsMenu.IsOpen
            && !(wardrobe != null && wardrobe.Typing); // a preset's name being typed (F, A, D are letters there)
        var keyboard = Keyboard.current;
        if (keys && Time.frameCount != openedFrame)
        {
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                if (wardrobe != null && wardrobe.ShowsSlot) wardrobe.ShowSlots();
                else { Close(); return; }
            }
            else if (KeyBindings.Pressed("Player/Interact")) { Close(); return; }
        }
        if (keys && keyboard != null)
        {
            float turn = (keyboard.dKey.isPressed || keyboard.rightArrowKey.isPressed ? 1f : 0f)
                - (keyboard.aKey.isPressed || keyboard.leftArrowKey.isPressed ? 1f : 0f);
            facing += turn * HubLobby.Number("dressTurn", 150f) * dt;
        }
        // Round to the asked way through the look itself, as the results turn the lineup (PlayerLook takes it this frame),
        // and looking level: the spine and head bend with the view, and the look down at the glass bowed the reflection.
        float off = Mathf.DeltaAngle(mine.transform.eulerAngles.y, facing);
        if (Mathf.Abs(off) > .2f) headMotion.OrNull()?.Turn(off * Mathf.Min(1f, dt * TurnRate));
        look.OrNull()?.EasePitch(0f, 1f - Mathf.Exp(-PitchRate * dt));
        if (highlighted != null) OutfitGhost.Breathe(Time.unscaledTime);
    }

    // The camera glides to where the part in view is framed. A body made again (another outfit, another character) is
    // framed again once it stands posed, and its marks put back on.
    private void LateUpdate()
    {
        if (!open || view == null || mirror == null || body == null) return;
        var animator = body.BodyAnimator;
        if (animator != framed)
        {
            framed = animator;
            settleLeft = SettleFrames;
            refocus = true;
            if (highlighted != null) Mark();
        }
        else if (settleLeft > 0 && --settleLeft == 0) refocus = true;
        if (refocus)
        {
            refocus = false;
            Frame();
        }
        float k = 1f - Mathf.Exp(-Mathf.Max(.5f, HubLobby.Number("dressEase", 7f)) * Time.unscaledDeltaTime);
        view.transform.SetPositionAndRotation(Vector3.Lerp(view.transform.position, goalAt, k),
            Quaternion.Slerp(view.transform.rotation, goalTurn, k));
        view.fieldOfView = Mathf.Lerp(view.fieldOfView, goalFov, k);
    }

    // Where the camera stands for the part in view: that part's reflection framed dressFill of the screen high at
    // dressFov, seen out in front of the glass a little from above (dressLift). Nearer the glass than dressGap it stops
    // there and narrows the lens instead. Measured as if the player faced the glass, so turning round doesn't swing it.
    private void Frame()
    {
        var animator = body.BodyAnimator;
        Vector3 pivot = mine.transform.position;
        var whole = animator != null ? CharacterFrame.Measure(animator.gameObject)
            : new Bounds(pivot + Vector3.up * .85f, new Vector3(.5f, 1.7f, .3f));
        Vector3 toGlass = -mirror.Normal;
        float glassYaw = Mathf.Atan2(toGlass.x, toGlass.z) * Mathf.Rad2Deg;
        var unturn = Quaternion.Euler(0f, Mathf.DeltaAngle(mine.transform.eulerAngles.y, glassYaw), 0f);
        WardrobeFraming.Part(animator, whole, focus, bone => pivot + unturn * (bone.position - pivot), out Vector3 center, out float span);
        Vector3 seen = mirror.Reflect(center);
        float fov = Mathf.Clamp(HubLobby.Number("dressFov", 34f), 5f, 90f);
        float half = span * .5f / Mathf.Clamp(HubLobby.Number("dressFill", .78f), .1f, 1f);
        float distance = half / Mathf.Tan(fov * .5f * Mathf.Deg2Rad);
        float lift = HubLobby.Number("dressLift", 6f) * Mathf.Deg2Rad;
        Vector3 back = (mirror.Normal * Mathf.Cos(lift) + mirror.Up * Mathf.Sin(lift)).normalized;
        Vector3 at = seen + back * distance;
        float gap = Mathf.Max(.1f, HubLobby.Number("dressGap", .35f)), inFront = mirror.InFront(at);
        if (inFront < gap)
        {
            float more = (gap - inFront) / Mathf.Max(.05f, Vector3.Dot(back, mirror.Normal));
            at += back * more;
            distance += more;
            fov = 2f * Mathf.Atan(half / distance) * Mathf.Rad2Deg;
        }
        goalAt = at;
        goalTurn = Quaternion.LookRotation(seen - at, Vector3.up);
        goalFov = fov;
    }

    // ---- The player left out of this camera's picture ----

    private void BeforeRender(ScriptableRenderContext context, Camera camera)
    {
        if (camera == null || camera != view || mine == null) return;
        FindOwn();
        hidden.Clear();
        foreach (var renderer in own)
        {
            if (renderer == null) continue;
            hidden.Add((renderer, renderer.forceRenderingOff));
            renderer.forceRenderingOff = true;
        }
    }

    private void AfterRender(ScriptableRenderContext context, Camera camera)
    {
        if (camera != null && camera == view) ShowAgain();
    }

    private void ShowAgain()
    {
        foreach (var (renderer, off) in hidden)
            if (renderer != null) renderer.forceRenderingOff = off;
        hidden.Clear();
    }

    // Found again when the body is made again, and every half second for what comes and goes (the gun slung on the back).
    private void FindOwn()
    {
        var animator = body != null ? body.BodyAnimator : null;
        if (animator == ownBody && Time.unscaledTime - ownFoundAt < .5f) return;
        ownBody = animator;
        ownFoundAt = Time.unscaledTime;
        own.Clear();
        mine.GetComponentsInChildren(true, own);
    }

    // ---- The wardrobe's stage: the player's own body ----

    public bool CanTry => false;
    public string WhereLine => Texts.Get("wardrobe.where.mirror");

    public void Focus(string group)
    {
        focus = string.IsNullOrEmpty(group) ? null : group;
        refocus = true;
    }

    public void Highlight(string slot)
    {
        slot = string.IsNullOrEmpty(slot) ? null : slot;
        if (slot == highlighted) return;
        highlighted = slot;
        Mark();
    }

    // The highlighted slot's pieces on the body, with the breathing rim the title screen's character has.
    private void Mark()
    {
        var skin = body != null ? body.Skin : null;
        var animator = body != null ? body.BodyAnimator : null;
        if (highlighted == null || skin == null || animator == null) { OutfitGhost.Show(null); return; }
        OutfitGhost.Show(WardrobePieces.Of(animator.gameObject, skin.Id, body.Dressing, highlighted, OutfitLook.Saved(skin.Id)));
    }

    public void Try(OutfitLook look, string slot, bool removes) { }

    public void DropTry() { }

    // A slider moves the body's own keys at once, the clothes on it with them; letting go saves it, and the body is made
    // again with it, as every saved look is.
    public void Shape(BodyShapes.Option option, float value)
    {
        var skin = body != null ? body.Skin : null;
        var animator = body != null ? body.BodyAnimator : null;
        if (option == null || skin == null || animator == null || !string.Equals(option.Skin, skin.Id, StringComparison.OrdinalIgnoreCase)) return;
        BodyShapes.Set(animator.GetComponentsInChildren<SkinnedMeshRenderer>(true), option, value);
    }

    public void Kept(OutfitLook look) { }

    // A drag to the right turns the player's front to the right as seen in the glass: they face it, so that is their right.
    public void Turn(float pixels, float deltaTime) => facing += pixels * HubLobby.Number("dressDrag", .45f);

    public void Release() { }

    // ---- Closing ----

    private void Close()
    {
        if (!open) return;
        Shut();
        Destroy(gameObject);
    }

    private void Shut()
    {
        open = false;
        closedFrame = Time.frameCount;
        RenderPipelineManager.beginCameraRendering -= BeforeRender;
        RenderPipelineManager.endCameraRendering -= AfterRender;
        ShowAgain();
        highlighted = null;
        OutfitGhost.Show(null);
        if (wardrobe != null) wardrobe.Close();
        if (canvas != null) Destroy(canvas.gameObject);
        canvas = null;
        wardrobe = null;
        if (view != null) Destroy(view.gameObject);
        view = null;
        if (masked && main != null)
        {
            main.cullingMask = mainMask;
            main.GetUniversalAdditionalCameraData().renderPostProcessing = mainPost;
        }
        masked = false;
        InputHold.Release(this);
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);
        if (slots != null && heldSlot >= 0) slots.SelectSlot(heldSlot);
        heldSlot = -1;
    }

    private void OnDestroy()
    {
        if (open) Shut();
        if (instance == this) instance = null;
    }
}
