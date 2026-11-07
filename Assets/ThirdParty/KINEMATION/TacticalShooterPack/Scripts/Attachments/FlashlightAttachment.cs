// Copyright (c) 2026 KINEMATION.
// All rights reserved.

using UnityEngine;

namespace KINEMATION.TacticalShooterPack.Scripts.Attachments
{
    [AddComponentMenu("KINEMATION/Tactical Shooter Pack/Attachments/Flashlight")]
    public class FlashlightAttachment : WeaponAttachment
    {
        public Light flashlight;
        public Renderer emissiveRenderer;
        [Min(0f)] public float emissionStrength = 1f;
        public bool IsOn => _isOn;

        private bool _isOn;
        private MaterialPropertyBlock _properties;
        private static readonly int EmissionStrength = Shader.PropertyToID("_EmissionStrength");

        public override void UseAttachment()
        {
            _isOn = !_isOn;
            UpdateLight();
        }

        private void UpdateLight()
        {
            bool visible = _isOn && isActiveAndEnabled;
            if (flashlight != null) flashlight.enabled = visible;
            if (emissiveRenderer == null) return;
            if (_properties == null) _properties = new MaterialPropertyBlock();
            emissiveRenderer.GetPropertyBlock(_properties);
            _properties.SetFloat(EmissionStrength, visible ? emissionStrength : 0f);
            emissiveRenderer.SetPropertyBlock(_properties);
        }

        protected virtual void OnEnable() => UpdateLight();
        protected virtual void OnDisable() => UpdateLight();
    }
}
