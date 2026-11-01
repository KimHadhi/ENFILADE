using System.Collections.Generic;
using UnityEngine;
using UnityEngine.VFX;
using UnityEngine.Serialization;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace DAVFX
{
    // Spawns a particle system light burst whenever the VisualEffect on this
    // object starts emitting. Works in Play Mode, builds, and from the VFX
    // Graph's edit-time preview.
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VisualEffect))]
    [AddComponentMenu("DAVFX/VFX Light Burst Companion")]
    public class VFXLightBurstCompanion : MonoBehaviour
    {
        [FormerlySerializedAs("lightBurstTemplate")]
        [Tooltip("Particle system prefab (Shuriken) that does the light burst.")]
        public ParticleSystem lightBurstPrefab;

        [Tooltip("Local Y offset applied to the spawn point, useful for lifting the light above ground impacts.")]
        public float localYOffset = 0f;

        [Tooltip("Safety net only, not the burst duration (that's the particle system's own lifetime). " +
                 "Force-destroys a spawned instance still alive after this many seconds, e.g. if the assigned " +
                 "particle system loops by mistake. 0 disables it.")]
        public float maxLifetime = 1f;

        [Tooltip("Hide the spawned clone from the Hierarchy window. Purely cosmetic.")]
        public bool hideCloneInHierarchy = true;

        [Header("Light Overrides (per prefab)")]
        [Tooltip("Tint this impact's light without touching the shared particle system asset. " +
                 "Needs 'Use Particle Color' enabled in its Lights module.")]
        public bool overrideLightColor = false;

        public Color lightColor = Color.white;

        [Tooltip("Scales the Lights module intensity for this impact only. 1 = unchanged, 2 = twice as bright.")]
        [Min(0f)]
        public float lightIntensityScale = 1f;

        // IsAlive() can read false on the same tick an instance was spawned,
        // so the finished-check waits a couple of ticks.
        const int MinTicksBeforeCheck = 2;

        VisualEffect vfx;
        bool wasEmitting;
        readonly List<PendingBurst> pending = new List<PendingBurst>();

        struct PendingBurst
        {
            public ParticleSystem ps;
            public float spawnTime;
            public float lastTickTime;
            public int ticks;
        }

        void OnEnable()
        {
            vfx = GetComponent<VisualEffect>();
            wasEmitting = false;
#if UNITY_EDITOR
            // Update() doesn't tick while previewing a VFX outside Play Mode,
            // the editor update loop covers that case.
            EditorApplication.update += EditModeTick;
#endif
        }

        void OnDisable()
        {
#if UNITY_EDITOR
            EditorApplication.update -= EditModeTick;
#endif
            for (int i = 0; i < pending.Count; i++)
            {
                if (pending[i].ps != null)
                    DestroyInstance(pending[i].ps.gameObject);
            }
            pending.Clear();
        }

#if UNITY_EDITOR
        void EditModeTick()
        {
            if (Application.isPlaying)
                return; // handled by Update()

            CheckAndTrigger();
            DriveEditModeSimulation();
            CleanupPending();
        }

        // Outside Play Mode, Unity only simulates a particle system while it's
        // selected in the Hierarchy. Spawned clones are unselected, so they get
        // stepped forward manually here.
        void DriveEditModeSimulation()
        {
            if (pending.Count == 0)
                return;

            float now = Time.realtimeSinceStartup;

            for (int i = 0; i < pending.Count; i++)
            {
                var entry = pending[i];
                if (entry.ps == null)
                    continue;

                float dt = now - entry.lastTickTime;
                if (dt <= 0f)
                    continue;

                entry.ps.Simulate(dt, true, false, false);
                entry.lastTickTime = now;
                pending[i] = entry;
            }

            SceneView.RepaintAll();
        }
#endif

        void Update()
        {
            if (!Application.isPlaying)
                return;

            CheckAndTrigger();
            CleanupPending();
        }

        void CheckAndTrigger()
        {
            if (vfx == null)
                return;

            if (!gameObject.scene.IsValid())
                return; // prefab asset being rendered for a preview icon etc.

            bool isEmitting = vfx.aliveParticleCount > 0;

            if (isEmitting && !wasEmitting)
                TriggerBurst();

            wasEmitting = isEmitting;
        }

        void CleanupPending()
        {
            for (int i = pending.Count - 1; i >= 0; i--)
            {
                var entry = pending[i];

                if (entry.ps == null)
                {
                    pending.RemoveAt(i);
                    continue;
                }

                entry.ticks++;
                pending[i] = entry;

                bool finished = entry.ticks >= MinTicksBeforeCheck && !entry.ps.IsAlive(true);
                bool timedOut = maxLifetime > 0f && Time.realtimeSinceStartup - entry.spawnTime > maxLifetime;

                if (finished || timedOut)
                {
                    DestroyInstance(entry.ps.gameObject);
                    pending.RemoveAt(i);
                }
            }
        }

        void DestroyInstance(GameObject go)
        {
            if (Application.isPlaying)
                Destroy(go);
            else
                DestroyImmediate(go);
        }

        // Call this directly if your graph never spawns particles
        // (aliveParticleCount stays 0) and you trigger it from code.
        public void TriggerBurst()
        {
            if (lightBurstPrefab == null)
                return;

            Vector3 spawnPos = transform.position + transform.up * localYOffset;
            ParticleSystem instance = Instantiate(lightBurstPrefab, spawnPos, transform.rotation);
            float now = Time.realtimeSinceStartup;

            ApplyLightOverrides(instance);

            HideFlags flags = hideCloneInHierarchy ? HideFlags.HideInHierarchy : HideFlags.None;

            if (Application.isPlaying)
            {
                instance.gameObject.hideFlags = flags;
                instance.Play();
            }
            else
            {
                instance.gameObject.hideFlags = flags | HideFlags.DontSave;
                instance.Simulate(0f, true, true, false);
            }

            pending.Add(new PendingBurst { ps = instance, spawnTime = now, lastTickTime = now });
        }

        // Overrides go on the spawned clone only, never on the shared asset.
        void ApplyLightOverrides(ParticleSystem instance)
        {
            if (overrideLightColor)
            {
                var main = instance.main;
                main.startColor = lightColor;
            }

            if (!Mathf.Approximately(lightIntensityScale, 1f))
            {
                var lights = instance.lights;
                if (lights.enabled)
                    lights.intensity = ScaleCurve(lights.intensity, lightIntensityScale);
            }
        }

        static ParticleSystem.MinMaxCurve ScaleCurve(ParticleSystem.MinMaxCurve curve, float scale)
        {
            switch (curve.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    curve.constant *= scale;
                    return curve;
                case ParticleSystemCurveMode.TwoConstants:
                    curve.constantMin *= scale;
                    curve.constantMax *= scale;
                    return curve;
                default:
                    curve.curveMultiplier *= scale;
                    return curve;
            }
        }
    }
}