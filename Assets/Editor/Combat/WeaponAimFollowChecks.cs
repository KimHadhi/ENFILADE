using System;
using System.Reflection;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>Presentation checks for smooth convergence without delayed camera input or cover checks.</summary>
public static class WeaponAimFollowChecks
{
    private const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic;
    private static void Set(WeaponData data, string name, float value)
        => typeof(WeaponData).GetField(name, Fields).SetValue(data, value);
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Aim follow: " + message); }

    public static void Run()
    {
        DistanceTransitions(); FrameRatesAndProbes(); CameraRelativeFollow(); RecoilAndAds(); ResetAndInstant();
        Debug.Log("WEAPON_AIM_FOLLOW_CHECKS_PASSED: 5 groups");
    }

    private static void DistanceTransitions()
    {
        foreach (bool approaching in new[] { true, false })
        using (var f = new Fixture())
        {
            float start = approaching ? 60f : 2f, end = approaching ? 2f : 60f;
            f.Step(start, .01f);
            Require(f.AimError(start) < .05f, "first pose starts aligned without an equip sweep");
            Vector3 initial = f.LocalDirection;
            f.Step(end, 1f / 60f);
            Require(Vector3.Distance(initial, f.LocalDirection) > .001f, "tracking begins on the first changed frame");
            Require(f.AimError(end) > .1f, "near/far changes cannot snap in either direction");
            float depth = f.VisualDepth;
            Require(depth > 2f && depth < 60f, "visible convergence remains between the old and new targets");
            for (int i = 0; i < 60; i++) f.Step(end, 1f / 60f);
            Require(f.AimError(end) < .05f, "tracking settles onto the actual aim point");
        }
    }

    private static void FrameRatesAndProbes()
    {
        Vector3 reference = Vector3.zero;
        foreach (int hz in new[] { 30, 60, 120 })
        using (var f = new Fixture())
        {
            f.Step(60f, .01f);
            for (int i = 0; i < hz / 10; i++) f.Step(2f, 1f / hz);
            if (hz == 30) reference = f.LocalDirection;
            else Require(Vector3.Distance(reference, f.LocalDirection) < .00002f,
                "equal elapsed time produces equal tracking at " + hz + " Hz");
        }
        using (var plain = new Fixture())
        using (var probed = new Fixture())
        {
            plain.Step(60f, .01f); probed.Step(60f, .01f);
            plain.Step(2f, 1f / 60f); probed.Step(2f, 1f / 60f, probes: 8);
            Require(Vector3.Distance(plain.LocalDirection, probed.LocalDirection) < .00002f,
                "ADS obstruction probes cannot advance tracking or alter its result");
        }
    }

    private static void CameraRelativeFollow()
    {
        using (var still = new Fixture())
        using (var moving = new Fixture())
        {
            still.Step(60f, .01f); moving.Step(60f, .01f);
            still.Step(2f, 1f / 60f); moving.Step(2f, 1f / 60f);
            moving.View.SetPositionAndRotation(new Vector3(4f, 2f, -3f), Quaternion.Euler(22f, 113f, -32f));
            still.Step(2f, 1f / 60f); moving.Step(2f, 1f / 60f);
            Require(Vector3.Distance(still.LocalDirection, moving.LocalDirection) < .00003f,
                "mouse turns, translations and lean roll do not leave the weapon aiming at an old world position");
            Require(Vector3.Distance(still.LocalMuzzle, moving.LocalMuzzle) < .00003f,
                "camera changes preserve the authored camera-relative muzzle position");
            foreach (float roll in new[] { -18f, 18f })
            {
                moving.View.SetPositionAndRotation(new Vector3(roll * .02f, 1f, 0f), Quaternion.Euler(0f, 25f, roll));
                moving.Step(2f, .1f, ads: 1f);
                Require(Mathf.Abs(moving.LocalMuzzle.x) < .0001f
                    && Mathf.Abs(moving.LocalMuzzle.y + moving.Data.SightHeight) < .0001f,
                    "both Q/E-style lean directions keep the ADS sight position centered");
            }
        }
    }

    private static void RecoilAndAds()
    {
        using (var plain = new Fixture())
        using (var kicked = new Fixture())
        {
            // Isolate rearward recoil: pitch is a deliberate additional hip-fire animation.
            Set(kicked.Data, "weaponKickUp", 0f);
            plain.Step(2f, .01f); kicked.Step(2f, .01f);
            kicked.Presentation.OnShot(kicked.Data, Vector2.zero);
            plain.Step(60f, 1f / 60f); kicked.Step(60f, 1f / 60f);
            Require(kicked.LocalMuzzle.z < plain.LocalMuzzle.z - .001f, "test exercises an actual rearward weapon kick");
            Require(kicked.AimError(60f) > .1f, "the alignment pass after recoil cannot bypass aim smoothing");
            Require(Mathf.Abs(kicked.VisualDepth - plain.VisualDepth) < .005f,
                "recoil reconverges to the same visual target instead of changing the filter");
        }
        using (var hip = new Fixture())
        using (var ads = new Fixture())
        {
            hip.Step(60f, .01f); ads.Step(60f, .01f, ads: 1f);
            hip.Step(2f, .05f); ads.Step(2f, .05f, ads: 1f);
            Require(Mathf.Abs(ads.VisualDepth - 2f) < Mathf.Abs(hip.VisualDepth - 2f),
                "ADS tracks a changed target faster than hip fire");
        }
    }

    private static void ResetAndInstant()
    {
        using (var f = new Fixture())
        {
            f.Step(60f, .01f); f.Step(2f, .01f);
            f.Presentation.Reset(); f.Step(60f, .01f);
            Require(f.AimError(60f) < .05f, "reset discards the previous weapon's filtered target");
            f.Step(.1f, .02f, blocked: true);
            f.Step(2f, .3f);
            Require(f.AimError(2f) < .05f, "leaving high ready does not revive an unsafe near-wall target");
            Set(f.Data, "aimFollowHalfLife", 0f); Set(f.Data, "adsAimFollowHalfLife", 0f);
            f.Step(60f, .01f);
            Require(f.AimError(60f) < .05f, "zero hip half-life restores immediate convergence");
            f.Step(2f, .01f, ads: 1f);
            Require(f.AimError(2f) < .05f, "zero ADS half-life restores immediate convergence");
        }
    }

    private sealed class Fixture : IDisposable
    {
        public readonly GameObject Owner;
        public readonly Transform View, Model, Muzzle;
        public readonly WeaponData Data;
        public readonly WeaponRuntime Runtime;
        public readonly WeaponPresentation Presentation;
        public Vector3 LocalMuzzle => View.InverseTransformPoint(Muzzle.position);
        public Vector3 LocalDirection => View.InverseTransformDirection(Muzzle.forward);
        public float VisualDepth
        {
            get
            {
                Vector3 origin = LocalMuzzle, direction = LocalDirection;
                float transverse = direction.x * direction.x + direction.y * direction.y;
                Require(transverse > .00000001f, "fixture barrel permits measuring its convergence depth");
                float along = -(origin.x * direction.x + origin.y * direction.y) / transverse;
                return origin.z + along * direction.z;
            }
        }
        public Fixture()
        {
            Owner = new GameObject("Aim follow check");
            View = new GameObject("View").transform; View.SetParent(Owner.transform, false);
            Model = new GameObject("Model").transform; Model.SetParent(View, false);
            Muzzle = new GameObject("Muzzle").transform; Muzzle.SetParent(Model, false);
            Muzzle.localPosition = new Vector3(.3f, -.18f, .8f);
            Data = ScriptableObject.CreateInstance<WeaponData>();
            Runtime = new WeaponRuntime(Data);
            Presentation = new WeaponPresentation(Model, Muzzle);
        }
        public void Step(float distance, float dt, float ads = 0f, int probes = 0, bool blocked = false)
        {
            for (int i = 0; i < probes; i++) Presentation.Prepare(View, Data, i % 2);
            Presentation.Prepare(View, Data, ads);
            Presentation.Pose(Owner, View, View.position + View.forward * distance, blocked, Runtime, dt, 0);
        }
        public float AimError(float distance)
            => Vector3.Angle(Muzzle.forward, View.position + View.forward * distance - Muzzle.position);
        public void Dispose() { Object.DestroyImmediate(Owner); Object.DestroyImmediate(Data); }
    }
}
