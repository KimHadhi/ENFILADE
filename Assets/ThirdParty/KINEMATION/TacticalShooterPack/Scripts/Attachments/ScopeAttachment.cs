// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using System.Collections.Generic;
using UnityEngine;

namespace KINEMATION.TacticalShooterPack.Scripts.Attachments
{
    [AddComponentMenu("KINEMATION/Tactical Shooter Pack/Attachments/Scope")]
    public class ScopeAttachment : SightAttachment
    {
        [Min(1f)] public float magnification = 1f;
        public Transform[] secondaryMounts;
        public Renderer lens;
        public Transform capturePoint;
        [Min(64)] public int textureResolution = 1024;
        [Range(1f, 179f)] public float captureFov = 90f;

        private readonly List<SightAttachment> _secondaryScopes = new List<SightAttachment>();
        private Camera _viewCamera;
        private Camera _scopeCamera;
        private RenderTexture _renderTexture;
        private MaterialPropertyBlock _properties;
        private int _activeScope;
        private int _firstPersonLayer;
        private bool _initialized;

        private static readonly int RenderTextureId = Shader.PropertyToID("_RenderTexture");
        private static readonly int MagnificationId = Shader.PropertyToID("_Magnification");
        private static readonly int VisibilityId = Shader.PropertyToID("_ScopeVisibility");
        private static readonly int TexelSizeId = Shader.PropertyToID("_ScopeTexelSize");

        public int ActiveScopeIndex => _activeScope;
        public int SightCount => 1 + _secondaryScopes.Count;

        public virtual void Initialize(Camera viewCamera, int firstPersonLayer,
            List<SecondaryScopeSelection> secondarySelections)
        {
            if (_initialized) return;
            _initialized = true;
            _viewCamera = viewCamera;
            _firstPersonLayer = firstPersonLayer;
            if (secondarySelections == null) return;

            foreach (var selection in secondarySelections)
            {
                if (selection.prefab == null) continue;
                if (secondaryMounts == null || selection.mountIndex < 0
                    || selection.mountIndex >= secondaryMounts.Length
                    || secondaryMounts[selection.mountIndex] == null)
                {
                    Debug.LogWarning($"No secondary scope mount {selection.mountIndex} on {name}.", this);
                    continue;
                }

                var secondary = Instantiate(selection.prefab, secondaryMounts[selection.mountIndex], false);
                secondary.transform.localPosition = Vector3.zero;
                secondary.transform.localRotation = Quaternion.identity;
                foreach (Transform child in secondary.GetComponentsInChildren<Transform>(true))
                    child.gameObject.layer = firstPersonLayer;
                if (secondary is ScopeAttachment scope)
                    scope.Initialize(viewCamera, firstPersonLayer, null);
                secondary.EnableAttachment();
                _secondaryScopes.Add(secondary);
            }
        }

        public virtual bool CycleScopes(bool forward = true)
        {
            if (_secondaryScopes.Count == 0) return false;
            _activeScope = (_activeScope + (forward ? 1 : SightCount - 1)) % SightCount;
            return true;
        }

        public override SightAttachmentData GetSightData()
        {
            return _activeScope == 0 ? base.GetSightData() : _secondaryScopes[_activeScope - 1].GetSightData();
        }

        public virtual void UpdateScope(float aimingWeight)
        {
            UpdateLens(_activeScope == 0 ? aimingWeight : 0f);
            for (int i = 0; i < _secondaryScopes.Count; i++)
            {
                if (_secondaryScopes[i] is ScopeAttachment scope)
                    scope.UpdateScope(_activeScope == i + 1 ? aimingWeight : 0f);
            }
        }

        private void CreateScopeCamera()
        {
            if (_scopeCamera != null || _viewCamera == null || capturePoint == null) return;
            var cameraObject = new GameObject("Scope Camera");
            cameraObject.transform.SetParent(capturePoint, false);
            _scopeCamera = cameraObject.AddComponent<Camera>();
            _scopeCamera.enabled = false;
            _scopeCamera.fieldOfView = captureFov;
            _scopeCamera.aspect = 1f;
            _scopeCamera.nearClipPlane = 0.01f;
            _scopeCamera.farClipPlane = _viewCamera.farClipPlane;
            _scopeCamera.clearFlags = _viewCamera.clearFlags;
            _scopeCamera.backgroundColor = _viewCamera.backgroundColor;
            _scopeCamera.allowHDR = _viewCamera.allowHDR;
            _scopeCamera.allowMSAA = false;
            _scopeCamera.depth = _viewCamera.depth - 1f;
            _scopeCamera.cullingMask = _viewCamera.cullingMask & ~(1 << _firstPersonLayer);

            int resolution = Mathf.Clamp(textureResolution, 64, 4096);
            _renderTexture = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.DefaultHDR)
            {
                name = name + " Scope View",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _renderTexture.Create();
            _scopeCamera.targetTexture = _renderTexture;
        }

        private void UpdateLens(float aimingWeight)
        {
            if (lens == null || lens.sharedMaterial == null
                || !lens.sharedMaterial.HasProperty(RenderTextureId)) return;

            float weight = isActiveAndEnabled ? Mathf.Clamp01(aimingWeight) : 0f;
            if (weight > 0.001f) CreateScopeCamera();
            if (_scopeCamera != null) _scopeCamera.enabled = weight > 0.001f;
            if (_properties == null) _properties = new MaterialPropertyBlock();
            lens.GetPropertyBlock(_properties);
            _properties.SetFloat(VisibilityId, _scopeCamera != null ? weight : 0f);
            _properties.SetFloat(MagnificationId, Mathf.Max(1f, magnification));
            if (_renderTexture != null)
            {
                _properties.SetTexture(RenderTextureId, _renderTexture);
                _properties.SetVector(TexelSizeId, new Vector4(1f / _renderTexture.width,
                    1f / _renderTexture.height, _renderTexture.width, _renderTexture.height));
            }
            lens.SetPropertyBlock(_properties);
        }

        protected virtual void OnDisable() => UpdateScope(0f);

        protected virtual void OnDestroy()
        {
            if (_scopeCamera != null)
            {
                _scopeCamera.targetTexture = null;
                Destroy(_scopeCamera.gameObject);
            }
            if (_renderTexture == null) return;
            _renderTexture.Release();
            Destroy(_renderTexture);
        }
    }
}
