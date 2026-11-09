using UnityEngine;

/// <summary>
/// A magnified scope's lens: what is ahead of the scope, rendered through a camera of its own into a texture its lens
/// shader shows, magnified, while the gun is aimed. The same as KINEMATION's Tactical Shooter Pack ScopeAttachment (its
/// lens shader reads _RenderTexture, _Magnification, _ScopeVisibility and _ScopeTexelSize), driven by FPSGun instead of
/// the pack's player; TacticalGunSetup copies the scope's numbers here. The scope's camera is made the first time the gun
/// is aimed and is switched off (its object too) whenever it isn't, so a search for the player's camera never finds it.
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

    /// <summary>
    /// This frame's view through the scope, <paramref name="aim"/> of the way down the sights, seen from
    /// <paramref name="view"/>'s settings. The gun's own layer stays out of the scope's view.
    /// </summary>
    public void Show(float aim, Camera view)
    {
        if (lens == null || lens.sharedMaterial == null || !lens.sharedMaterial.HasProperty(RenderTextureId)) return;
        float weight = isActiveAndEnabled ? Mathf.Clamp01(aim) : 0f;
        if (weight > .001f) Make(view);
        if (scopeCamera != null)
        {
            bool on = weight > .001f;
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
        scopeCamera.cullingMask = view.cullingMask & ~(1 << gameObject.layer);
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

    private void OnDisable()
    {
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
