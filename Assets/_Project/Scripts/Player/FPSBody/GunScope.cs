using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// A magnified scope's lens: what is ahead of the scope, rendered through a camera of its own into a texture its lens
/// shader shows, magnified, while the gun is aimed. The same as KINEMATION's Tactical Shooter Pack ScopeAttachment (its
/// lens shader reads _RenderTexture, _Magnification, _ScopeVisibility and _ScopeTexelSize), driven by FPSGun instead of
/// the pack's player; TacticalGunSetup copies the scope's numbers here. The scope's camera is made the first time the gun
/// is aimed and is switched off (its object too) whenever it isn't, so a search for the player's camera never finds it.
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

    [Tooltip("The lens; its material shows the scope's view.")]
    public Renderer lens;
    [Tooltip("Where the scope's camera looks from.")]
    public Transform capturePoint;
    [Min(1f)] public float magnification = 1f;
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

    private void Awake() => GameTables.Bind(TableName, TableRow, this);

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

    // The field the picture is taken through: the middle of the scope's whole field that the lens shows, the same
    // perspective, so the lens shows the same view without enlarging it.
    private float PictureFov => Optical
        ? 2f * Mathf.Atan(Mathf.Tan(captureFov * .5f * Mathf.Deg2Rad) / Mathf.Max(1f, magnification)) * Mathf.Rad2Deg
        : captureFov;

    /// <summary>
    /// This frame's view through the scope, <paramref name="aim"/> of the way down the sights, seen from
    /// <paramref name="view"/>'s settings and looking where it looks.
    /// </summary>
    public void Show(float aim, Camera view)
    {
        if (lens == null || lens.sharedMaterial == null || !lens.sharedMaterial.HasProperty(RenderTextureId)) return;
        float weight = isActiveAndEnabled ? Mathf.Clamp01(aim) : 0f;
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
                scopeCamera.fieldOfView = PictureFov;
            }
            lookingThrough = on;
            if (scopeCamera.gameObject.activeSelf != on) scopeCamera.gameObject.SetActive(on);
            scopeCamera.enabled = on;
        }
        if (block == null) block = new MaterialPropertyBlock();
        lens.GetPropertyBlock(block);
        block.SetFloat(VisibilityId, scopeCamera != null ? weight : 0f);
        block.SetFloat(MagnificationId, Optical ? 1f : Mathf.Max(1f, magnification));
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

    // The picture's texture: about screenShare of the screen's height on a side, where the lens spans roughly 0.3-0.45 of
    // it when aimed, so each pixel of the lens averages about four texels; a power of two, for its mip levels.
    private void Size(Camera view)
    {
        int height = view != null ? view.pixelHeight : 0;
        int want = height > 0 ? Mathf.RoundToInt(height * screenShare) : textureResolution;
        int size = Mathf.ClosestPowerOfTwo(Mathf.Clamp(want, 64, Mathf.Max(64, largest)));
        if (texture != null && texture.width == size) return;
        if (scopeCamera != null) scopeCamera.targetTexture = null;
        if (texture != null) { texture.Release(); Destroy(texture); }
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

    private void OnDestroy()
    {
        if (scopeCamera != null) { scopeCamera.targetTexture = null; Destroy(scopeCamera.gameObject); }
        if (texture != null) { texture.Release(); Destroy(texture); }
    }
}
