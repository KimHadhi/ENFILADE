using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// A magnified scope's lens: what is ahead of the scope, rendered through a camera of its own into a texture its lens
/// shader shows while the gun is aimed. The lens and its shader are KINEMATION's Tactical Shooter Pack ScopeAttachment's
/// (the shader reads _RenderTexture, _Magnification, _ScopeVisibility, _ScopeTexelSize and _Distortion), driven by FPSGun
/// instead of the pack's player; TacticalGunSetup copies the scope's numbers here. The scope's camera is made the first
/// time the gun is aimed and is switched off (its object too) whenever it isn't, so a search for the player's camera never
/// finds it.
/// How the aimed view is drawn through it is the sight's own row (GunParts.csv view, GunMounts.Part):
/// - zoom (ours, the default): the whole view is magnified by the sight (PlayerCameraEffects narrows the player's field,
///   GunMounts.AimedFov) and the lens shows what lies behind it at that same magnification, so the lens and the world
///   round it are one picture, the scope's housing and its shadow framing it. The lens camera is kept for that: looked
///   through as plain glass, the pack's scopes show the inside of their own tube and the gun's front sight four times
///   over, and so would any model not built to be looked through. The pack's own way drew the world twice at two
///   magnifications, the plain view all round and a magnified circle in the middle of it (user, 2026-11-18: "배율 조준경
///   사용 시 조준경 화면과 일반 시야가 이중으로 분리 렌더링(일반 화면 + 확대 화면 2개로 보임)되는 문제").
/// - pip: the pack's way, kept for a sight whose row asks for it: the view stays as the gun has it and the lens alone
///   shows a magnified picture.
/// Three of ours on top of the pack's:
/// - The scope's camera draws everything the player's camera does, but the player's own body, gun and its parts (the lens
///   too) are switched off for its render alone. They share the world's layer (Default), and leaving that layer out, as
///   the pack does with the gun's own layer, left out every wall and floor with it (user, 2026-11-10: the 4x and 8x
///   lenses showed only the sky).
/// - It looks where the player's camera looks, from the front of the scope, upright with the lens: the picture is steady
///   on what the crosshair is on. Hung on the gun it swung with every bit of sway, magnified as many times over.
/// - Its picture is taken through the magnified field itself, on a texture about twice the lens's size on screen that is
///   averaged down to it (mip levels, trilinear). The pack's camera took the scope's whole field (captureFov) and the lens
///   shader enlarged the middle of it: a 4x scope showed 1/16 of what was drawn and a 6x one 1/36, each texel 2-5 screen
///   pixels wide with nothing smoothing it, so every edge and highlight in the scope stepped and crawled as the gun swayed
///   (user, 2026-11-12: "줌 했을 때 텍스쳐 지글 거리는 거"). Numbers: Tables/Scopes.csv, row Picture.
/// </summary>
[DisallowMultipleComponent]
public sealed class GunScope : MonoBehaviour, ITableBound
{
    public const string TableName = "Scopes", TableRow = "Picture";

    private static readonly int RenderTextureId = Shader.PropertyToID("_RenderTexture");
    private static readonly int MagnificationId = Shader.PropertyToID("_Magnification");
    private static readonly int VisibilityId = Shader.PropertyToID("_ScopeVisibility");
    private static readonly int TexelSizeId = Shader.PropertyToID("_ScopeTexelSize");
    private static readonly int ReticleZoomId = Shader.PropertyToID("_UseReticleZoom");
    private static readonly int DistortionId = Shader.PropertyToID("_Distortion");

    [Tooltip("The lens; its material shows the scope's view.")]
    public Renderer lens;
    [Tooltip("Where the scope's camera looks from.")]
    public Transform capturePoint;
    [Min(1f), Tooltip("How far it magnifies when its part has no GunParts row (the pack's number).")]
    public float magnification = 1f;
    [Min(64), Tooltip("The picture's side (px) when there is no view to size it from (the pack's number).")]
    public int textureResolution = 1024;
    [Range(1f, 179f), Tooltip("The scope's whole field (°, the pack's number); the lens shows its middle, magnification times nearer.")]
    public float captureFov = 90f;

    [SerializeField, Range(.1f, 2f), Tooltip("The picture's side as a share of the screen's height, to the nearest power of two.")]
    private float screenShare = .8f;
    [SerializeField, Min(64), Tooltip("The largest picture (px).")]
    private int largest = 2048;

    private Camera scopeCamera;
    private RenderTexture texture;
    private MaterialPropertyBlock block;
    private Transform viewer;
    // The player's own renderers, off while the scope's camera renders, and whether each was off already.
    private readonly List<Renderer> own = new List<Renderer>();
    private readonly List<bool> ownWasOff = new List<bool>();
    private bool hiding, lookingThrough;
    // What this sight is (its part's GunParts row), read again when the tables are; and the player's field fully aimed
    // through it, which the picture's own field follows (zoom).
    private GunMounts.Part sight;
    private int sightFor = -1;
    private float aimedFov = 60f, pictureFov = -1f;

    private void Awake() => GameTables.Bind(TableName, TableRow, this);

    private GunMounts.Part Sight
    {
        get
        {
            if (sightFor == GunMounts.Version) return sight;
            sight = GunMounts.PartOf(GunMounts.IdOf(gameObject));
            // After the first row is read: reading it is what starts GunMounts counting the tables' reloads.
            sightFor = GunMounts.Version;
            return sight;
        }
    }

    /// <summary>How many times it magnifies: its part's row's, else the pack's own number.</summary>
    public float Magnification => Sight.Found ? Sight.Magnification : Mathf.Max(1f, magnification);

    // The whole view is magnified with it and the lens shows the same magnification.
    private bool Zooms => Sight.Zooms;

    // Zoom: the lens picture's edge as the eye sees it (degrees from the sight line): its row's share (window) of half the
    // aimed view's height.
    private float PictureEdge => Mathf.Clamp(Sight.Window * aimedFov * .5f, 1f, 30f);

    // The next look through the scope sizes its picture again.
    public void OnTableApplied() => lookingThrough = false;

    // A lens whose reticle grows with the magnification (the pack's _UseReticleZoom) needs the shader's own enlarging,
    // which also enlarges the picture: it keeps the pack's wide picture. None of the scopes on our guns has it.
    private bool Optical
    {
        get
        {
            var material = lens != null ? lens.sharedMaterial : null;
            return material == null || !material.HasProperty(ReticleZoomId) || material.GetFloat(ReticleZoomId) < .5f;
        }
    }

    // The field the picture is taken through.
    // Zoom: the cone the lens picture covers as the eye sees it, so a thing in the lens is as large as the same thing
    // beside the scope (the shader lays the picture's edge at the sine of its angle, a camera at the tangent of half its
    // field).
    // Pip: the middle of the scope's whole field that the lens shows, the same perspective, so the lens shows the same
    // view without enlarging it.
    private float PictureFov => Zooms
        ? 2f * Mathf.Atan(Mathf.Sin(PictureEdge * Mathf.Deg2Rad)) * Mathf.Rad2Deg
        : Optical ? 2f * Mathf.Atan(Mathf.Tan(captureFov * .5f * Mathf.Deg2Rad) / Magnification) * Mathf.Rad2Deg
        : captureFov;

    // The lens shader spreads its picture out to the angle whose sine is 0.5 / (2 + 10 x _Distortion) from the sight line
    // (KShader_Scope_Base: GetDistortedScopeUv, GetScopeFovMask). Zoom: the number that puts the picture's edge at
    // PictureEdge. Pip: the lens material's own.
    private float LensSpread
    {
        get
        {
            var material = lens != null ? lens.sharedMaterial : null;
            float own = material != null && material.HasProperty(DistortionId) ? material.GetFloat(DistortionId) : .1f;
            return Zooms ? (.5f / Mathf.Sin(PictureEdge * Mathf.Deg2Rad) - 2f) / 10f : own;
        }
    }

    /// <summary>
    /// This frame's view through the scope, <paramref name="aim"/> of the way down the sights, seen from
    /// <paramref name="view"/>'s settings and looking where it looks; <paramref name="fullyAimedFov"/> is the player's
    /// vertical field (degrees) once fully aimed through this sight (FPSGun.AimedFov).
    /// </summary>
    public void Show(float aim, Camera view, float fullyAimedFov)
    {
        aimedFov = Mathf.Clamp(fullyAimedFov, 1f, 179f);
        Show(aim, view);
    }

    /// <summary>The same, the aimed field left as it was last given.</summary>
    public void Show(float aim, Camera view)
    {
        if (lens == null || lens.sharedMaterial == null || !lens.sharedMaterial.HasProperty(RenderTextureId)) return;
        // Outside play nothing has woken this component, though a check may stand a gun in the editor and look through
        // its scope there (GunSightBench): "on" is then what its switches say.
        bool awake = Application.isPlaying ? isActiveAndEnabled : enabled && gameObject.activeInHierarchy;
        float weight = awake ? Mathf.Clamp01(aim) : 0f;
        bool on = weight > .001f;
        if (on) Make(view);
        viewer = view != null ? view.transform : null;
        if (scopeCamera != null)
        {
            // Each time the scope comes up: who is "own" is looked up again (a skin or a part may have changed since), and
            // the picture is sized for the screen as it is now.
            if (on && !lookingThrough)
            {
                FindOwnRenderers();
                Size(view);
            }
            // The picture's field follows the aimed view's (the field of view changed in the Esc menu, a row changed).
            float field = PictureFov;
            if (on && !Mathf.Approximately(field, pictureFov))
            {
                pictureFov = field;
                scopeCamera.fieldOfView = field;
            }
            lookingThrough = on;
            if (scopeCamera.gameObject.activeSelf != on) scopeCamera.gameObject.SetActive(on);
            scopeCamera.enabled = on;
        }
        if (block == null) block = new MaterialPropertyBlock();
        lens.GetPropertyBlock(block);
        block.SetFloat(VisibilityId, scopeCamera != null ? weight : 0f);
        block.SetFloat(MagnificationId, Optical ? 1f : Magnification);
        block.SetFloat(DistortionId, LensSpread);
        if (texture != null)
        {
            block.SetTexture(RenderTextureId, texture);
            block.SetVector(TexelSizeId, new Vector4(1f / texture.width, 1f / texture.height, texture.width, texture.height));
        }
        lens.SetPropertyBlock(block);
    }

    private void Make(Camera view)
    {
        if (scopeCamera != null || view == null || capturePoint == null) return;
        var holder = new GameObject("Scope Camera");
        holder.transform.SetParent(capturePoint, false);
        scopeCamera = holder.AddComponent<Camera>();
        scopeCamera.enabled = false;
        scopeCamera.fieldOfView = PictureFov;
        scopeCamera.aspect = 1f;
        scopeCamera.nearClipPlane = .01f;
        scopeCamera.farClipPlane = view.farClipPlane;
        scopeCamera.clearFlags = view.clearFlags;
        scopeCamera.backgroundColor = view.backgroundColor;
        scopeCamera.allowHDR = view.allowHDR;
        // Smoothed by drawing it larger and averaging it down (Size), which a multisampled texture can't do.
        scopeCamera.allowMSAA = false;
        scopeCamera.depth = view.depth - 1f;
        scopeCamera.cullingMask = view.cullingMask;
        Size(view);
    }

    // The picture's texture.
    // Pip: about screenShare of the screen's height on a side, where the lens spans roughly 0.3-0.45 of it when aimed, so
    // each pixel of the lens averages about four texels; a power of two, for mip levels that halve cleanly.
    // Zoom: the picture is as tall on screen as its row's window says and is most of what the player sees, so it is made
    // at that very size, a texel to a pixel. The nearest power of two is up to 30% too small for it (1440p: 1024 texels
    // spread over 1296 pixels, soft beside the sharp view round the scope) or 40% too large (drawn for nothing).
    private void Size(Camera view)
    {
        int height = view != null ? view.pixelHeight : 0;
        int cap = Mathf.Max(64, largest);
        int size;
        if (Zooms && height > 0) size = Mathf.Clamp(Mathf.CeilToInt(height * Sight.Window * .5f) * 2, 64, cap);
        else size = Mathf.ClosestPowerOfTwo(Mathf.Clamp(height > 0 ? Mathf.RoundToInt(height * screenShare) : textureResolution, 64, cap));
        if (texture != null && texture.width == size) return;
        if (scopeCamera != null) scopeCamera.targetTexture = null;
        if (texture != null) { texture.Release(); Remove(texture); }
        texture = new RenderTexture(size, size, 24, RenderTextureFormat.DefaultHDR)
        {
            name = name + " scope view",
            // Its mip levels are made after each render (AfterRender), so the lens samples it averaged down to its size.
            useMipMap = true,
            autoGenerateMips = false,
            filterMode = FilterMode.Trilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        texture.Create();
        if (scopeCamera != null) scopeCamera.targetTexture = texture;
    }

    // The player who holds the gun, else the gun: everything under it stays out of the scope's picture.
    private void FindOwnRenderers()
    {
        own.Clear();
        Transform owner = null;
        var player = GetComponentInParent<PlayerMovement>(true);
        if (player != null) owner = player.transform;
        else
        {
            var gun = GetComponentInParent<GunSights>(true);
            owner = gun != null ? gun.transform : transform;
        }
        owner.GetComponentsInChildren(true, own);
    }

    private void OnEnable()
    {
        RenderPipelineManager.beginCameraRendering += BeforeRender;
        RenderPipelineManager.endCameraRendering += AfterRender;
    }

    // Just before the scope's camera culls and draws: turned to the player's view, and the player's own things off.
    private void BeforeRender(ScriptableRenderContext context, Camera camera)
    {
        if (camera == null || camera != scopeCamera) return;
        if (viewer != null && capturePoint != null)
            scopeCamera.transform.rotation = Quaternion.LookRotation(viewer.forward, capturePoint.up);
        ownWasOff.Clear();
        for (int i = 0; i < own.Count; i++)
        {
            var part = own[i];
            ownWasOff.Add(part != null && part.forceRenderingOff);
            if (part != null) part.forceRenderingOff = true;
        }
        hiding = true;
    }

    // After the scope's camera has drawn (its work is submitted by then): the player's things back on, and the picture's
    // mip levels made from it for the lens to sample.
    private void AfterRender(ScriptableRenderContext context, Camera camera)
    {
        if (camera == null || camera != scopeCamera) return;
        ShowOwnAgain();
        if (texture != null && texture.IsCreated() && texture.useMipMap && !texture.autoGenerateMips) texture.GenerateMips();
    }

    private void ShowOwnAgain()
    {
        if (!hiding) return;
        for (int i = 0; i < own.Count && i < ownWasOff.Count; i++)
            if (own[i] != null) own[i].forceRenderingOff = ownWasOff[i];
        hiding = false;
    }

    private void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= BeforeRender;
        RenderPipelineManager.endCameraRendering -= AfterRender;
        ShowOwnAgain();
        lookingThrough = false;
        if (scopeCamera != null) { scopeCamera.enabled = false; scopeCamera.gameObject.SetActive(false); }
        if (lens != null && block != null)
        {
            lens.GetPropertyBlock(block);
            block.SetFloat(VisibilityId, 0f);
            lens.SetPropertyBlock(block);
        }
    }

    private void OnDestroy() => Close();

    /// <summary>
    /// Takes the scope's picture now, as its camera takes it in play: turned to the viewer, the player's own things left
    /// out of it, its mip levels made. In play the camera renders by itself every frame and this is never needed. Outside
    /// play no camera renders by itself and this component's render hooks are not on, so whoever looks through a scope
    /// there asks for each picture (GunSightBench, which checks what the aiming eye sees without Play). Call Show first.
    /// </summary>
    public void RenderNow()
    {
        if (scopeCamera == null || texture == null || !scopeCamera.gameObject.activeSelf) return;
#if UNITY_EDITOR
        // Stood in a scene of the editor's own (a bench's), it draws that scene alone, as the view that looks at it does.
        if (UnityEditor.SceneManagement.EditorSceneManager.IsPreviewScene(gameObject.scene)) scopeCamera.scene = gameObject.scene;
#endif
        BeforeRender(default, scopeCamera);
        try
        {
            scopeCamera.Render();
        }
        finally
        {
            AfterRender(default, scopeCamera);
        }
    }

    /// <summary>
    /// Lets go of the scope's camera and picture. Unity does this as the scope goes (OnDestroy); whoever showed it
    /// outside play, where Unity calls nothing, does it themselves.
    /// </summary>
    public void Close()
    {
        ShowOwnAgain();
        lookingThrough = false;
        if (scopeCamera != null)
        {
            scopeCamera.targetTexture = null;
            Remove(scopeCamera.gameObject);
            scopeCamera = null;
        }
        if (texture != null)
        {
            texture.Release();
            Remove(texture);
            texture = null;
        }
    }

    // Outside play only DestroyImmediate may be called.
    private static void Remove(Object thing)
    {
        if (Application.isPlaying) Destroy(thing);
        else DestroyImmediate(thing);
    }
}
