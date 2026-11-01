// DAVFX - short FOV push that sells the pressure wave of an impact.
// Drop it on your camera: any VFX that starts emitting triggers a punch.
// Use the name filter to restrict it to specific effects in bigger scenes.
// Not meant for Cinemachine-driven cameras.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

namespace DAVFX
{
    [DisallowMultipleComponent]
    [AddComponentMenu("DAVFX/FOV Punch")]
    public class DAVFXFovPunch : MonoBehaviour
    {
        [Tooltip("Camera to affect. Leave empty to use the camera on this GameObject.")]
        [SerializeField] Camera targetCamera;

        [Tooltip("Degrees added to the field of view at the peak of the punch. Above 2-3 it starts reading as a deliberate effect rather than blast pressure.")]
        [Range(0f, 10f)]
        [SerializeField] float strength = 1.5f;

        [Tooltip("How long it takes for the field of view to settle back.")]
        [Range(0.05f, 1f)]
        [SerializeField] float duration = 0.25f;

        [Tooltip("How fast the punch eases back. Higher values settle quicker.")]
        [Range(0.5f, 5f)]
        [SerializeField] float falloff = 2f;

        [Tooltip("Impacts lose strength with distance from the camera and stop punching beyond this range. Set to 0 to treat every impact the same.")]
        [Min(0f)]
        [SerializeField] float falloffDistance = 0f;

        [Tooltip("Minimum time between two punches, so rapid bursts don't stack.")]
        [Range(0f, 2f)]
        [SerializeField] float cooldown = 0.05f;

        [Tooltip("Only VFX whose name contains one of these words trigger the punch. Leave the list empty to react to every VFX Graph in the scene.")]
        [SerializeField] string[] nameFilter = new string[0];

        [Tooltip("How often the scene is searched for new effects, in seconds. Effects that are already being watched are checked every frame, so this only delays the first reaction to an effect that has just appeared. 0 searches every frame and reacts immediately, raise it only if the scan shows up in your profiler.")]
        [Range(0f, 0.5f)]
        [SerializeField] float scanInterval = 0f;

        struct VfxState { public int alive; public int peak; public float lastFire; }

        // keyed on the effect itself: Object.GetInstanceID() is deprecated from
        // Unity 6.5 on and its EntityId replacement does not exist in older versions
        readonly Dictionary<VisualEffect, VfxState> _tracked = new Dictionary<VisualEffect, VfxState>();
        readonly HashSet<VisualEffect> _seen = new HashSet<VisualEffect>();
        readonly List<VisualEffect> _stale = new List<VisualEffect>();
        readonly List<VisualEffect> _watched = new List<VisualEffect>();
        float _nextScan;

        bool _primed;
        float _lastTrigger = -10f;
        float _timer;
        float _mult;
        float _appliedFov;

        void OnEnable()
        {
            _tracked.Clear();
            _watched.Clear();
            _nextScan = float.MinValue;
            _primed = false;
            _timer = 0f;
        }

        void OnDisable()
        {
            RemoveApplied();
            _timer = 0f;
            _mult = 0f;
        }

        /// <summary>Fires a punch scaled by a custom multiplier. Full strength by default.</summary>
        public void TriggerPunch(float multiplier = 1f)
        {
            if (multiplier <= 0f) return;

            // keep the stronger of the two when punches overlap
            float remaining = duration > 0f ? Mathf.Clamp01(_timer / duration) : 0f;
            _mult = Mathf.Max(multiplier, _mult * remaining);
            _timer = duration;
        }

        [ContextMenu("Test Punch")]
        void TestPunch() => TriggerPunch();

        void Update()
        {
            if (Time.time >= _nextScan) ScanScene();
            DetectVfx();
        }

        // searching the whole scene is the expensive part, so it runs on its own
        // interval, while the effects it finds are polled every frame below
        void ScanScene()
        {
            _nextScan = Time.time + scanInterval;
            _watched.Clear();

            var effects = FindSceneEffects();
            for (int i = 0; i < effects.Length; i++)
            {
                var vfx = effects[i];
                if (!PassesFilter(vfx.name)) continue;
                _watched.Add(vfx);
            }
        }

        void DetectVfx()
        {
            _seen.Clear();

            for (int i = 0; i < _watched.Count; i++)
            {
                var vfx = _watched[i];
                if (vfx == null) continue; // destroyed since the last scan
                if (!vfx.isActiveAndEnabled) continue;

                int alive = vfx.aliveParticleCount;
                _seen.Add(vfx);

                bool wasTracked = _tracked.TryGetValue(vfx, out VfxState st);
                int prev = wasTracked ? st.alive : 0;
                int peak = wasTracked ? st.peak : 0;
                if (alive > peak) peak = alive;
                float lastFire = wasTracked ? st.lastFire : -10f;

                // a real impact makes the count jump up sharply, whether it is a
                // fresh effect or the same one replayed. Smoke and debris that
                // keep emitting only add a little per frame, so a sudden surge
                // is what tells the two apart and avoids a second false trigger.
                bool started = prev == 0 && alive > 0;
                bool surge = alive > prev && (alive - prev) * 2 >= peak;

                if (_primed && (started || surge) &&
                    Time.time - lastFire >= duration &&
                    Time.time - _lastTrigger >= cooldown)
                {
                    lastFire = Time.time;
                    _lastTrigger = Time.time;
                    TriggerPunch(DistanceMultiplier(vfx.transform.position));
                }

                _tracked[vfx] = new VfxState { alive = alive, peak = peak, lastFire = lastFire };
            }

            _primed = true;

            if (_tracked.Count != _seen.Count)
            {
                _stale.Clear();
                foreach (var key in _tracked.Keys)
                    if (!_seen.Contains(key)) _stale.Add(key);
                foreach (var key in _stale)
                    _tracked.Remove(key);
            }
        }

        // Unity 6.5 deprecated the FindObjectsSortMode overloads, and the
        // parameterless one does not exist before it, so both paths stay here
        // to keep the pack compiling without warnings from Unity 6.0 onwards.
        static VisualEffect[] FindSceneEffects()
        {
#if UNITY_6000_5_OR_NEWER
            return FindObjectsByType<VisualEffect>(FindObjectsInactive.Exclude);
#else
            return FindObjectsByType<VisualEffect>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
#endif
        }

        bool PassesFilter(string vfxName)
        {
            bool hasWord = false;
            for (int i = 0; i < nameFilter.Length; i++)
            {
                string word = nameFilter[i];
                if (string.IsNullOrEmpty(word)) continue;
                hasWord = true;
                if (vfxName.IndexOf(word, System.StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }
            return !hasWord; // no words set means react to everything
        }

        float DistanceMultiplier(Vector3 impactPosition)
        {
            if (falloffDistance <= 0f) return 1f;
            var cam = ResolveCamera();
            if (cam == null) return 1f;

            float dist = Vector3.Distance(cam.transform.position, impactPosition);
            return 1f - Mathf.Clamp01(dist / falloffDistance);
        }

        void LateUpdate()
        {
            var cam = ResolveCamera();
            if (cam == null) return;

            // remove last frame's contribution first, so the punch stacks
            // cleanly on top of FOV changes made by something else
            RemoveApplied();

            if (_timer <= 0f) return;
            _timer -= Time.deltaTime;
            if (_timer <= 0f) { _mult = 0f; return; }

            float u = Mathf.Clamp01(_timer / Mathf.Max(duration, 0.01f));
            u = u * u * (3f - 2f * u); // smoothstep so it always lands softly
            float envelope = Mathf.Pow(u, falloff);

            _appliedFov = strength * _mult * envelope;
            cam.fieldOfView += _appliedFov;
        }

        void RemoveApplied()
        {
            if (_appliedFov != 0f && targetCamera != null)
                targetCamera.fieldOfView -= _appliedFov;
            _appliedFov = 0f;
        }

        Camera ResolveCamera()
        {
            if (targetCamera == null)
                targetCamera = GetComponent<Camera>();
            return targetCamera;
        }
    }
}
