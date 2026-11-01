using UnityEngine;
using UnityEngine.VFX;
using UnityEngine.Serialization;
// RIP_INPUT_SYSTEM is defined by the asmdef only when the Input System package is installed,
// so this compiles cleanly in every project configuration.
#if RIP_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
using UnityEngine.InputSystem;
#endif

namespace RealisticImpacts
{
    public class VFXTester : MonoBehaviour
    {
        public enum SpawnMode
        {
            AllAtOnce,   // Spawn every assigned prefab on each click
            Random       // Spawn one random prefab from the list on each click
        }

        public enum AlignMode
        {
            None,             // Keep the prefab's own rotation
            UpToNormal,       // VFX Y axis along the surface normal (usually best for impacts)
            ForwardToNormal   // VFX Z axis along the surface normal
        }

        [FormerlySerializedAs("vfxPrefabs")]
        public VisualEffect[] vfxGraphPrefabs;        // VFX Graph prefabs spawned on click

        [Header("Spawn")]
        public SpawnMode spawnMode = SpawnMode.AllAtOnce;

        [Header("VFX Lifetime")]
        public bool autoDestroy = true;
        public float autoDestroyTime = 10f;

        [Header("Alignment")]
        public AlignMode alignmentMode = AlignMode.UpToNormal;

        [Header("Raycast")]
        public float maxDistance = 500f;
        public LayerMask hitLayers = ~0;

        [Header("References")]
        public Camera targetCamera;                  // Falls back to Camera.main if left empty

        [Header("Hierarchy")]
        public bool hideClonesInHierarchy = true;

        private void Awake()
        {
            if (targetCamera == null)
                targetCamera = Camera.main;

#if !RIP_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            Debug.LogWarning("[Realistic Impacts] VFXTester: click input is disabled because the " +
                "Input System package is not installed. To test the VFX in the demo scene, install " +
                "\"Input System\" from Window > Package Manager > Unity Registry.", this);
#endif
        }

        private void Update()
        {
            if (WasClickedThisFrame())
                SpawnVFXAtPointer();
        }

        private void SpawnVFXAtPointer()
        {
            if (targetCamera == null || vfxGraphPrefabs == null || vfxGraphPrefabs.Length == 0)
                return;

            Ray ray = targetCamera.ScreenPointToRay(GetPointerPosition());

            if (!Physics.Raycast(ray, out RaycastHit hit, maxDistance, hitLayers, QueryTriggerInteraction.Ignore))
                return;

            Quaternion rotation = GetAlignmentRotation(hit);

            if (spawnMode == SpawnMode.Random)
            {
                Spawn(vfxGraphPrefabs[Random.Range(0, vfxGraphPrefabs.Length)], hit.point, rotation);
            }
            else
            {
                foreach (VisualEffect prefab in vfxGraphPrefabs)
                    Spawn(prefab, hit.point, rotation);
            }
        }

        private void Spawn(VisualEffect prefab, Vector3 position, Quaternion rotation)
        {
            if (prefab == null)
                return;

            VisualEffect vfx = Instantiate(prefab, position, rotation);

            if (hideClonesInHierarchy)
                vfx.gameObject.hideFlags = HideFlags.HideInHierarchy;

            // If your Graph exposes HitPosition / HitNormal, set them here:
            // vfx.SetVector3("HitPosition", position);
            // vfx.SetVector3("HitNormal", rotation * Vector3.up);

            if (autoDestroy)
                Destroy(vfx.gameObject, autoDestroyTime);
        }

        private Quaternion GetAlignmentRotation(RaycastHit hit)
        {
            switch (alignmentMode)
            {
                case AlignMode.UpToNormal:
                    return Quaternion.FromToRotation(Vector3.up, hit.normal);

                case AlignMode.ForwardToNormal:
                    return Quaternion.LookRotation(hit.normal);

                case AlignMode.None:
                default:
                    return Quaternion.identity;
            }
        }

        // Works with the old Input Manager, the new Input System, or "Both".
        private static bool WasClickedThisFrame()
        {
#if RIP_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            return Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
#elif ENABLE_LEGACY_INPUT_MANAGER || !ENABLE_INPUT_SYSTEM
            return Input.GetMouseButtonDown(0);
#else
            // Project is set to the new Input System but the package is missing:
            // no input backend is available, so do nothing instead of throwing.
            return false;
#endif
        }

        private static Vector3 GetPointerPosition()
        {
#if RIP_INPUT_SYSTEM && ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
            return Mouse.current != null ? (Vector3)Mouse.current.position.ReadValue() : Vector3.zero;
#elif ENABLE_LEGACY_INPUT_MANAGER || !ENABLE_INPUT_SYSTEM
            return Input.mousePosition;
#else
            return Vector3.zero;
#endif
        }
    }
}
