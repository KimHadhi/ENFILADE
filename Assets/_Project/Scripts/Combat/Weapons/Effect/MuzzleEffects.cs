using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Authored muzzle visuals for the parent WeaponController: a particle muzzle flash and a short bullet tracer from the
/// muzzle to the hit point. Lives on the muzzle point, so it follows the gun model and hides with it.
/// </summary>
[DisallowMultipleComponent]
public sealed class MuzzleEffects : MonoBehaviour
{
    [Header("Muzzle Flash")]
    [SerializeField] private ParticleSystem muzzleFlash;

    [Header("Bullet Tracer")]
    [SerializeField] private LineRenderer tracerPrefab;
    [SerializeField, Min(0.01f)] private float tracerDuration = 0.12f;
    [SerializeField, Range(0f, 1f)] private float tracerTailLength = 0.35f;

    private WeaponController weapon;

    private sealed class ActiveTracer
    {
        public LineRenderer line;
        public Vector3 start;
        public Vector3 end;
        public float elapsed;
        public float duration;
        public float tailLength;
        public Color startColor;
        public Color endColor;
    }

    private readonly List<ActiveTracer> activeTracers = new();

    private void OnEnable()
    {
        weapon = GetComponentInParent<WeaponController>();
        if (weapon != null) weapon.ShotFired += Play;
    }

    private void Play(WeaponShot shot)
    {
        if (muzzleFlash != null)
        {
            muzzleFlash.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            muzzleFlash.Play();
        }
        ShowBulletTracer(shot);
    }

    private void Update()
    {
        UpdateTracers();
    }

    private void ShowBulletTracer(WeaponShot shot)
    {
        if (tracerPrefab == null)
            return;

        Vector3 startPoint = shot.Origin;

        Vector3 endPoint =
            shot.Trace.HasHit && shot.Trace.Hit.collider != null
                ? shot.Trace.Hit.point
                : shot.Trace.End;

        if ((endPoint - startPoint).sqrMagnitude < 0.0001f)
            return;

        LineRenderer line = Instantiate(tracerPrefab);

        line.useWorldSpace = true;
        line.positionCount = 2;

        ActiveTracer tracer = new ActiveTracer
        {
            line = line,
            start = startPoint,
            end = endPoint,
            elapsed = 0f,
            duration = Mathf.Max(0.01f, tracerDuration),
            tailLength = tracerTailLength,
            startColor = line.startColor,
            endColor = line.endColor
        };

        activeTracers.Add(tracer);

        line.SetPosition(0, startPoint);
        line.SetPosition(1, endPoint);
    }

    private void UpdateTracers()
    {
        for (int i = activeTracers.Count - 1; i >= 0; i--)
        {
            ActiveTracer tracer = activeTracers[i];

            if (tracer.line == null)
            {
                activeTracers.RemoveAt(i);
                continue;
            }

            tracer.elapsed += Time.deltaTime;

            float progress = Mathf.Clamp01(
                tracer.elapsed / tracer.duration);

            Vector3 currentStart = Vector3.Lerp(
                tracer.start,
                tracer.end,
                progress);

            Vector3 currentEnd = Vector3.Lerp(
                tracer.start,
                tracer.end,
                Mathf.Min(1f, progress + tracer.tailLength));

            tracer.line.SetPosition(0, currentStart);
            tracer.line.SetPosition(1, currentEnd);

            Color startColor = tracer.startColor;
            Color endColor = tracer.endColor;

            startColor.a = tracer.startColor.a * (1f - progress);
            endColor.a = tracer.endColor.a * (1f - progress);

            tracer.line.startColor = startColor;
            tracer.line.endColor = endColor;

            if (progress >= 1f)
            {
                Destroy(tracer.line.gameObject);
                activeTracers.RemoveAt(i);
            }
        }
    }

    private void OnDisable()
    {
        if (weapon != null) weapon.ShotFired -= Play;
        ClearTracers();
    }

    private void OnDestroy()
    {
        ClearTracers();
    }

    private void ClearTracers()
    {
        foreach (ActiveTracer tracer in activeTracers)
        {
            if (tracer.line != null)
                Destroy(tracer.line.gameObject);
        }

        activeTracers.Clear();
    }
}
