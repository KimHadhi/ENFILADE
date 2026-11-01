// DAVFX - camera shake driven by VFX Graph activity.
// Drop it on your camera: any VFX that starts emitting triggers a shake.
// Use the name filter to restrict it to specific effects in bigger scenes.
// Not meant for Cinemachine-driven cameras, use a Cinemachine Impulse there.

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;

namespace DAVFX
{
    [DisallowMultipleComponent]
    [AddComponentMenu("DAVFX/Camera Shake")]
    public class DAVFXCameraShake : MonoBehaviour
    {
        public enum ShakeType { Perlin, Jitter, Sine, Kick }

        [Tooltip("Camera to shake. Leave empty to use the camera on this GameObject.")]
        [SerializeField] Camera targetCamera;

        [Tooltip("Perlin is a smooth wandering motion, Jitter is a harsh random rattle, Sine oscillates along one direction, Kick is a single push that settles back.")]
        [SerializeField] ShakeType shakeType = ShakeType.Perlin;

        [Tooltip("How far the camera moves, in meters. Small values work best with a static camera.")]
        [Range(0f, 0.5f)]
        [SerializeField] float amplitude = 0.06f;

        [Tooltip("Tilt around the view axis, in degrees. Set to 0 for a purely positional shake.")]
        [Range(0f, 3f)]
        [SerializeField] float roll = 0.4f;

        [Tooltip("How long the shake lasts before the camera comes to rest.")]
        [Range(0.05f, 2f)]
        [SerializeField] float duration = 0.35f;

        [Tooltip("Speed of the motion. Higher feels sharper and more violent. Kick ignores this.")]
        [Range(1f, 60f)]
        [SerializeField] float frequency = 22f;

        [Tooltip("How fast the shake dies out. Higher values fade quicker.")]
        [Range(0.5f, 5f)]
        [SerializeField] float falloff = 2f;

        [Tooltip("Minimum time between two shakes, so rapid bursts don't stack.")]
        [Range(0f, 2f)]
        [SerializeField] float cooldown = 0.05f;

        [Tooltip("Only VFX whose name contains one of these words trigger the shake. Leave the list empty to react to every VFX Graph in the scene.")]
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
        float _shakeTimer;
        float _noiseTime;
        float _seedX, _seedY, _seedR;
        Vector2 _dir = Vector2.up;
        Vector2 _jitter;
        float _jitterRoll;
        float _jitterClock;
        Vector3 _appliedOffset;
        Quaternion _appliedRoll = Quaternion.identity;

        void OnEnable()
        {
            _tracked.Clear();
            _watched.Clear();
            _nextScan = float.MinValue;
            _primed = false;
            _shakeTimer = 0f;
        }

        void OnDisable()
        {
            RemoveApplied();
            _shakeTimer = 0f;
        }

        /// <summary>Fires a shake with the current settings. Also available from the component context menu.</summary>
        [ContextMenu("Test Shake")]
        public void TriggerShake()
        {
            _shakeTimer = duration;
            _seedX = Random.value * 100f;
            _seedY = Random.value * 100f;
            _seedR = Random.value * 100f;
            _noiseTime = 0f;
            _dir = Random.insideUnitCircle.normalized;
            if (_dir.sqrMagnitude < 0.001f) _dir = Vector2.up;
            _jitterClock = 999f;
        }

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
                    TriggerShake();
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

        void LateUpdate()
        {
            var cam = ResolveCamera();
            if (cam == null) return;

            Transform t = cam.transform;

            // remove last frame's contribution first, so the shake stacks
            // cleanly on top of a camera that is being moved by something else
            RemoveApplied();

            if (_shakeTimer <= 0f) return;
            _shakeTimer -= Time.deltaTime;
            if (_shakeTimer <= 0f) return;

            float u = Mathf.Clamp01(_shakeTimer / Mathf.Max(duration, 0.01f));
            u = u * u * (3f - 2f * u); // smoothstep so it always lands softly
            float envelope = Mathf.Pow(u, falloff);
            float elapsed = duration - _shakeTimer;

            float nx, ny, nr;
            switch (shakeType)
            {
                case ShakeType.Jitter:
                    _jitterClock += Time.deltaTime;
                    if (_jitterClock >= 1f / Mathf.Max(frequency, 1f))
                    {
                        _jitterClock = 0f;
                        _jitter = Random.insideUnitCircle;
                        _jitterRoll = Random.Range(-1f, 1f);
                    }
                    nx = _jitter.x; ny = _jitter.y; nr = _jitterRoll;
                    break;

                case ShakeType.Sine:
                    float s = Mathf.Sin(elapsed * frequency * 2f * Mathf.PI);
                    nx = _dir.x * s; ny = _dir.y * s; nr = s;
                    break;

                case ShakeType.Kick:
                    nx = _dir.x; ny = _dir.y; nr = _dir.x;
                    break;

                default:
                    _noiseTime += Time.deltaTime * frequency;
                    nx = Mathf.PerlinNoise(_seedX, _noiseTime) * 2f - 1f;
                    ny = Mathf.PerlinNoise(_seedY, _noiseTime) * 2f - 1f;
                    nr = Mathf.PerlinNoise(_seedR, _noiseTime) * 2f - 1f;
                    break;
            }

            Vector3 offset = (t.right * nx + t.up * ny) * (amplitude * envelope);
            if (t.parent != null)
                offset = t.parent.InverseTransformVector(offset);

            _appliedOffset = offset;
            _appliedRoll = Quaternion.Euler(0f, 0f, nr * roll * envelope);
            t.localPosition += _appliedOffset;
            t.localRotation *= _appliedRoll;
        }

        void RemoveApplied()
        {
            if (targetCamera != null)
            {
                targetCamera.transform.localPosition -= _appliedOffset;
                targetCamera.transform.localRotation *= Quaternion.Inverse(_appliedRoll);
            }
            _appliedOffset = Vector3.zero;
            _appliedRoll = Quaternion.identity;
        }

        Camera ResolveCamera()
        {
            if (targetCamera == null)
                targetCamera = GetComponent<Camera>();
            return targetCamera;
        }
    }
}
