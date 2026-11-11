using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// A magnified scope's lens: what is ahead of the scope, rendered through a camera of its own into a texture its lens
/// shader shows, magnified, while the gun is aimed. The same as KINEMATION's Tactical Shooter Pack ScopeAttachment (its
/// lens shader reads _RenderTexture, _Magnification, _ScopeVisibility and _ScopeTexelSize), driven by FPSGun instead of
/// the pack's player; TacticalGunSetup copies the scope's numbers here. The scope's camera is made the first time the gun
/// is aimed and is switched off (its object too) whenever it isn't, so a search for the player's camera never finds it.
/// Two of ours on top of the pack's (user, 2026-11-10: the 4x and 8x lenses showed only the sky, and the picture shook):
/// - The scope's camera draws everything the player's camera does, but the player's own body, gun and its parts (the lens
///   too) are switched off for its render alone. They share the world's layer (Default), and leaving that layer out, as
///   the pack does with the gun's own layer, left out every wall and floor with it.
/// - It looks where the player's camera looks, from the front of the scope, upright with the lens: the picture is steady
///   on what the crosshair is on. Hung on the gun it swung with every bit of sway, magnified as many times over.
/// </summary>
[DisallowMultipleComponent]
public sealed class GunScope : MonoBehaviour
{
    private static readonly int RenderTextureId = Shader.PropertyToID("_RenderTexture");
    private static readonly int MagnificationId = Shader.PropertyToID("_Magnification");
    private static readonly int VisibilityId = Shader.PropertyToID("_ScopeVisibility");
    private static readonly int TexelSizeId = Shader.PropertyToID("_ScopeTexelSize");

    [Tooltip("The lens; its material shows the scope's view.")]
    public Renderer lens;
    [Tooltip("Where the scope's camera looks from.")]
    public Transform capturePoint;
    [Min(1f)] public float magnification = 1f;
    [Min(64)] public int textureResolution = 1024;
    [Range(1f, 179f)] public float captureFov = 90f;

    private Camera scopeCamera;
    private RenderTexture texture;
    private MaterialPropertyBlock block;
    private Transform viewer;
    // The player's own renderers, off while the scope's camera renders, and whether each was off already.
    private readonly List<Renderer> own = new List<Renderer>();
    private readonly List<bool> ownWasOff = new List<bool>();
    private bool hiding, lookingThrough;

    /// <summary>
    /// This frame's view through the scope, <paramref name="aim"/> of the way down the sights, seen from
    /// <paramref name="view"/>'s settings and looking where it looks.
    /// </summary>
    public void Show(float aim, Camera view)
    {
        if (lens == null || lens.sharedMaterial == null || !lens.sharedMaterial.HasProperty(RenderTextureId)) return;
        float weight = isActiveAndEnabled ? Mathf.Clamp01(aim) : 0f;
        if (weight > .001f) Make(view);
        viewer = view != null ? view.transform : null;
        if (scopeCamera != null)
        {
            bool on = weight > .001f;
            // Who is "own" is looked up again each time the scope comes up: a skin or a part may have changed since.
            if (on && !lookingThrough) FindOwnRenderers();
            lookingThrough = on;
            if (scopeCamera.gameObject.activeSelf != on) scopeCamera.gameObject.SetActive(on);
            scopeCamera.enabled = on;
        }
        if (block == null) block = new MaterialPropertyBlock();
        lens.GetPropertyBlock(block);
        block.SetFloat(VisibilityId, scopeCamera != null ? weight : 0f);
        block.SetFloat(MagnificationId, Mathf.Max(1f, magnification));
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
        scopeCamera.fieldOfView = captureFov;
        scopeCamera.aspect = 1f;
        scopeCamera.nearClipPlane = .01f;
        scopeCamera.farClipPlane = view.farClipPlane;
        scopeCamera.clearFlags = view.clearFlags;
        scopeCamera.backgroundColor = view.backgroundColor;
        scopeCamera.allowHDR = view.allowHDR;
        scopeCamera.allowMSAA = false;
        scopeCamera.depth = view.depth - 1f;
        scopeCamera.cullingMask = view.cullingMask;
        int size = Mathf.Clamp(textureResolution, 64, 4096);
        texture = new RenderTexture(size, size, 24, RenderTextureFormat.DefaultHDR)
        {
            name = name + " scope view",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        texture.Create();
        scopeCamera.targetTexture = texture;
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

    private void AfterRender(ScriptableRenderContext context, Camera camera)
    {
        if (camera != null && camera == scopeCamera) ShowOwnAgain();
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
        if (scopeCamera != null) Destroy(scopeCamera.gameObject);
        if (texture != null) { texture.Release(); Destroy(texture); }
    }
}
