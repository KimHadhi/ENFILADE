using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The bullet's tracer for the parent WeaponController: a short streak from the muzzle to the hit point, and on along
/// each stretch the bullet went on through or off something. Lives on the muzzle point, so it follows the gun model and
/// hides with it. The flame is MuzzleFlash's; the particle flash this used to play (the MuzzleFlash object under the muzzle
/// in Player.prefab) no longer plays.
/// </summary>
[DisallowMultipleComponent]
public sealed class MuzzleEffects : MonoBehaviour
{
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

    private void Play(WeaponShot shot) => ShowBulletTracer(shot);

    private void Update()
    {
        UpdateTracers();
    }

    private void ShowBulletTracer(WeaponShot shot)
    {
        if (tracerPrefab == null)
            return;

        ShowTracer(shot.Origin, EndOf(shot.Trace));
        // A bullet that went on through or off something (AttackTrace.Bullet) streaks on from there.
        if (shot.Later != null)
            foreach (var leg in shot.Later) ShowTracer(leg.Onward.From, EndOf(leg.Trace));
    }

    private static Vector3 EndOf(in AttackTraceResult trace)
        => trace.HasHit && trace.Hit.collider != null ? trace.Hit.point : trace.End;

    private void ShowTracer(Vector3 startPoint, Vector3 endPoint)
    {
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
