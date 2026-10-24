using UnityEngine;

/// <summary>Procedural placeholder poses; keeps the authored hip pose and exposes a real muzzle.</summary>
public sealed class WeaponPresentation
{
    private readonly Transform root, muzzle;
    private readonly Vector3 hipPosition;
    private readonly Quaternion hipRotation;
    private float readyBlend, adsBlend;
    private float inverseAimDistance;
    private bool hasAimDistance;
    public bool IsHighReady => readyBlend > .05f;
    private readonly RecoilSpring recoil = new RecoilSpring();
    public WeaponPresentation(Transform root, Transform muzzle)
    {
        this.root = root; this.muzzle = muzzle;
        if (root == null) return;
        hipPosition = root.localPosition; hipRotation = root.localRotation;
    }
    public void Reset()
    {
        readyBlend = adsBlend = 0f; hasAimDistance = false; recoil.Reset();
        if (root == null) return;
        root.localPosition = hipPosition; root.localRotation = hipRotation;
    }
    public void OnShot(WeaponData data, Vector2 shotRecoil)
    {
        recoil.Kick(new Vector3(data.WeaponKickBack, data.WeaponKickUp,
            Mathf.Clamp(shotRecoil.y * .8f, -.6f, .6f)), data.WeaponRecoilResponse);
    }
    public Vector3 Prepare(Transform view, WeaponData data, float ads)
    {
        adsBlend = ads;
        if (root == null || muzzle == null) return muzzle != null ? muzzle.position : view.position;
        root.localPosition = hipPosition; root.localRotation = hipRotation;
        Vector3 localMuzzle = view.InverseTransformPoint(muzzle.position);
        Vector3 sightCorrection = view.TransformVector(new Vector3(-localMuzzle.x, -localMuzzle.y - data.SightHeight, 0f));
        root.position += sightCorrection * ads;
        return muzzle.position;
    }
    public bool Pose(GameObject owner, Transform view, Vector3 target, bool obstructed, WeaponRuntime runtime,
        float dt, int layers)
    {
        readyBlend = Mathf.MoveTowards(readyBlend, obstructed ? 1f : 0f, dt * 10f);
        recoil.Tick(dt, runtime.Data.WeaponRecoilResponse);
        if (root == null || muzzle == null) return obstructed;
        // Filter only depth-driven convergence, in the CURRENT camera frame. Smoothing a
        // world point/rotation would make mouse look and Q/E drag behind the player's view.
        if (obstructed) hasAimDistance = false;
        Vector3 visualTarget = obstructed ? target : FollowAimPoint(view, target, runtime.Data, dt);
        if (!obstructed)
        {
            // Rotate about the barrel endpoint, preserving the sight alignment calculated by Prepare.
            Vector3 endpoint = muzzle.position;
            root.rotation = Quaternion.FromToRotation(muzzle.forward, (visualTarget - endpoint).normalized) * root.rotation;
            root.position += endpoint - muzzle.position;
            obstructed = WeaponAimSolver.IsObstructed(owner, view, muzzle.position, target, layers);
        }
        if (obstructed) readyBlend = Mathf.MoveTowards(readyBlend, 1f, dt * 10f);
        root.rotation = Quaternion.Slerp(root.rotation, view.rotation * Quaternion.Euler(-runtime.Data.HighReadyPitch, 0f, 8f), readyBlend);
        root.position += view.TransformVector(runtime.Data.HighReadyOffset) * readyBlend;
        float recoilWeight = (1f - readyBlend) * (runtime.IsReloading ? .25f : 1f);
        float kickBack = recoil.Value.x * Mathf.Lerp(1f, runtime.Data.AdsWeaponKickMultiplier, adsBlend) * recoilWeight;
        // Recoil uses the SAME filtered target; snapping to the raw point here would undo
        // smoothing every frame. Gameplay hitscan/cover still use the current unfiltered point.
        root.position -= muzzle.forward * kickBack;
        if (recoilWeight > 0f && !obstructed)
        {
            Vector3 endpoint = muzzle.position;
            root.rotation = Quaternion.FromToRotation(muzzle.forward, (visualTarget - endpoint).normalized) * root.rotation;
            root.position += endpoint - muzzle.position;
            float pitchKick = recoil.Value.y * (1f - adsBlend) * recoilWeight;
            float rollKick = recoil.Value.z * (1f - adsBlend) * recoilWeight;
            root.rotation = Quaternion.AngleAxis(-pitchKick, view.right)
                * Quaternion.AngleAxis(rollKick, view.forward) * root.rotation;
        }
        if (runtime.IsReloading)
        {
            float progress = 1f - runtime.ReloadRemaining / runtime.ReloadDuration;
            float weight = Mathf.Sin(Mathf.Clamp01(progress) * Mathf.PI);
            root.position -= view.up * weight * (runtime.IsEmptyReload ? .22f : .15f);
            root.rotation *= Quaternion.Euler(15f * weight, -12f * weight, -25f * weight);
        }
        return obstructed || readyBlend > .05f;
    }

    private Vector3 FollowAimPoint(Transform view, Vector3 target, WeaponData data, float dt)
    {
        float distance = Mathf.Max(.001f, Vector3.Dot(target - view.position, view.forward));
        float requested = 1f / distance;
        float halfLife = Mathf.Lerp(data.AimFollowHalfLife, data.AdsAimFollowHalfLife, adsBlend);
        if (!hasAimDistance || halfLife <= 0f)
        { inverseAimDistance = requested; hasAimDistance = true; }
        else
        {
            // Reciprocal distance follows the angular change. A raw distance Lerp from
            // the sky to a close target would spend most of its time far behind that target.
            float blend = 1f - Mathf.Exp(-Mathf.Log(2f) * Mathf.Max(0f, dt) / halfLife);
            inverseAimDistance = Mathf.Lerp(inverseAimDistance, requested, blend);
        }
        return view.position + view.forward / Mathf.Max(.000001f, inverseAimDistance);
    }
}
