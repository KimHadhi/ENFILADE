using System.Collections.Generic;
using KINEMATION.CharacterAnimationSystem.Integrations.FPSAnimationPack.Scripts;
using UnityEngine;

/// <summary>
/// Where a gun's reload clip moves the gun's parts, as shares of the reload: the magazine starting to leave, the new one
/// seating, and the bolt or slide. KINEMATION's clips carry one baked sound for the whole reload at their own speed, while
/// the body plays them fitted to our reload time (FPSBody), so WeaponFeedback plays MagOut, MagIn, SlideBack and SlideForward at these
/// shares instead and each lands on what the hands do. Measured once per gun from the clip's own motion while the map loads,
/// with no numbers per gun: the part that travels farthest and comes back is the magazine; of the other parts that don't just
/// ride along while the magazine is out, the one that travels farthest is the bolt or slide. One that moves and stays was let
/// go from its catch; one that goes and comes back was pulled and released.
/// </summary>
public static class ReloadMoments
{
    /// <summary>Shares of the reload, 0 to 1; -1 for a part the clip doesn't move.</summary>
    public readonly struct Moments
    {
        public readonly float MagOut, MagIn, BoltBack, BoltForward;

        public Moments(float magOut, float magIn, float boltBack, float boltForward)
        {
            MagOut = magOut; MagIn = magIn; BoltBack = boltBack; BoltForward = boltForward;
        }
    }

    // A part has left once it is this share of its farthest travel away, and is back (a magazine seated) within this share.
    private const float Leaving = .02f, Back = .04f;
    // A part travelling less than this share of the magazine's way (a release button) makes no sound of its own.
    private const float Minor = .05f;
    // Samples of slack when telling a part that rides along with the magazine.
    private const float Slack = 2f;

    /// <summary>
    /// The magazine part a reload clip moves: its path under the gun's animated model, the shares of the reload at which
    /// it leaves, is farthest off (at the pouch) and is back, and how far it was from the sight line as the clip starts
    /// (a part that waits parked off the gun, a revolver's speedloader, isn't a magazine to drop). GunMagazine drops it.
    /// </summary>
    public readonly struct Magazine
    {
        public readonly string Path;
        public readonly float Out, Far, In, Start;

        public Magazine(string path, float leaves, float far, float back, float start)
        {
            Path = path; Out = leaves; Far = far; In = back; Start = start;
        }
    }

    private static readonly Dictionary<(WeaponData, bool), Moments> measured = new Dictionary<(WeaponData, bool), Moments>();
    private static readonly Dictionary<(WeaponData, bool), Magazine> magazines = new Dictionary<(WeaponData, bool), Magazine>();

    /// <summary>The moments of this gun's tactical or empty reload, once FPSBody has measured them.</summary>
    public static bool TryGet(WeaponData gun, bool empty, out Moments moments) => measured.TryGetValue((gun, empty), out moments);

    /// <summary>The magazine part of this gun's tactical or empty reload, once measured (no entry: nothing to drop).</summary>
    public static bool TryGetMagazine(WeaponData gun, bool empty, out Magazine magazine) => magazines.TryGetValue((gun, empty), out magazine);

    /// <summary>
    /// Measures both reload clips on the gun's model. FPSBody calls it once per gun while it builds, with the gun awake;
    /// the model's pose is put back afterwards.
    /// </summary>
    public static void Measure(WeaponData gun, CasFPSWeapon weapon)
    {
        if (gun == null || weapon == null) return;
        var animator = weapon.GetComponentInChildren<Animator>(true);
        if (animator == null) return;
        Measure(gun, false, animator.gameObject, weapon.reloadTactical);
        Measure(gun, true, animator.gameObject, weapon.reloadEmpty);
        // The magazine that falls out in a reload is cut from the gun's mesh now, while the map loads, not at the first
        // reload in play (GunMagazine).
        if (weapon.TryGetComponent(out GunMagazine magazine)) magazine.Prepare();
    }

    private static void Measure(WeaponData gun, bool empty, GameObject model, FPSAnimationPair pair)
    {
        if (measured.ContainsKey((gun, empty)) || pair.weapon == null || pair.weapon.clip == null) return;
        var clip = pair.weapon.clip;
        // The reload plays the pair fitted by the character clip's play length and runs the weapon clip at that rate too,
        // so this much of the weapon clip spans the whole reload.
        var timing = pair.character != null ? pair.character : pair.weapon;
        float span = timing.GetPlayLength() * (Mathf.Approximately(pair.weapon.playRate, 0f) ? 1f : pair.weapon.playRate);
        if (span <= .01f || clip.length <= .01f) return;

        var parts = model.GetComponentsInChildren<Transform>(true);
        int samples = Mathf.Max(2, Mathf.RoundToInt(clip.length * Mathf.Max(30f, clip.frameRate)) + 1);
        float step = clip.length / (samples - 1);
        var travel = Sample(model, clip, parts, samples, step);

        int magazine = -1;
        float farthest = 0f;
        for (int p = 0; p < parts.Length; p++)
        {
            if (parts[p] == model.transform) continue;
            float far = Farthest(travel, p, samples);
            if (far > farthest && travel[p, samples - 1] <= Back * far) { magazine = p; farthest = far; }
        }
        if (magazine < 0)
        {
            if (!AnyMoved(travel, parts, model.transform, samples))
                Debug.LogWarning($"ReloadMoments: sampling {clip.name} moved no part of {model.name}; its reload sounds keep the fixed timing.", model);
            return;
        }
        float magOut = Above(travel, magazine, samples, Leaving * farthest, 0);
        float magIn = LastBelow(travel, magazine, samples, Back * farthest);

        int bolt = -1;
        float boltTravel = Minor * farthest;
        for (int p = 0; p < parts.Length; p++)
        {
            if (p == magazine || parts[p] == model.transform) continue;
            float far = Farthest(travel, p, samples);
            if (far <= boltTravel) continue;
            float leaves = Above(travel, p, samples, Leaving * far, 0), returns = LastBelow(travel, p, samples, Back * far);
            bool ridesAlong = leaves >= magOut - Slack && returns >= 0f && returns <= magIn + Slack;
            if (ridesAlong) continue;
            bolt = p; boltTravel = far;
        }
        float boltBack = -1f, boltForward = -1f;
        if (bolt >= 0)
        {
            float end = travel[bolt, samples - 1];
            if (end >= .5f * boltTravel) boltForward = Above(travel, bolt, samples, .5f * end, 0);
            else
            {
                boltBack = Above(travel, bolt, samples, Leaving * boltTravel, 0);
                boltForward = Below(travel, bolt, samples, .5f * boltTravel, FarthestAt(travel, bolt, samples));
            }
        }

        float Share(float at) => at < 0f ? -1f : Mathf.Clamp01(at * step / span);
        measured[(gun, empty)] = new Moments(Share(magOut), Share(magIn), Share(boltBack), Share(boltForward));
        // The magazine itself, for GunMagazine: where it is under the model and when it is farthest off (the pouch).
        if (magOut >= 0f && magIn >= 0f)
            magazines[(gun, empty)] = new Magazine(Hierarchy.PathOf(parts[magazine], model.transform), Share(magOut),
                Share(FarthestAt(travel, magazine, samples)), Share(magIn), OffSight(parts[magazine], model));
    }

    // How far the part sits off the gun's sight line in the pose the model is in (its idle): a magazine sits in the gun,
    // a part parked away (the R08's speedloader, 0.45 m) far off it. The model's own aim point stands for the sight line;
    // without one, its own forward through its root.
    private static float OffSight(Transform part, GameObject model)
    {
        Transform aim = null;
        foreach (var child in model.GetComponentsInChildren<Transform>(true))
            if (child.name == "AimPoint") { aim = child; break; }
        Vector3 origin = aim != null ? aim.position : model.transform.position;
        Vector3 along = aim != null ? aim.forward : model.transform.forward;
        Vector3 offset = part.position - origin;
        return (offset - along * Vector3.Dot(offset, along)).magnitude;
    }

    // How far each part has moved from where the clip starts it, relative to its parent (a part that only rides on another
    // stays put), in world units, at each sample.
    private static float[,] Sample(GameObject model, AnimationClip clip, Transform[] parts, int samples, float step)
    {
        var position = new Vector3[parts.Length];
        var rotation = new Quaternion[parts.Length];
        var scale = new Vector3[parts.Length];
        var active = new bool[parts.Length];
        for (int p = 0; p < parts.Length; p++)
        {
            position[p] = parts[p].localPosition; rotation[p] = parts[p].localRotation; scale[p] = parts[p].localScale;
            active[p] = parts[p].gameObject.activeSelf;
        }
        var travel = new float[parts.Length, samples];
        var start = new Vector3[parts.Length];
        for (int i = 0; i < samples; i++)
        {
            clip.SampleAnimation(model, i * step);
            for (int p = 0; p < parts.Length; p++)
            {
                Vector3 local = parts[p].localPosition;
                if (i == 0) start[p] = local;
                Vector3 moved = local - start[p];
                var parent = parts[p].parent;
                travel[p, i] = (parent != null ? parent.TransformVector(moved) : moved).magnitude;
            }
        }
        for (int p = 0; p < parts.Length; p++)
        {
            parts[p].localPosition = position[p]; parts[p].localRotation = rotation[p]; parts[p].localScale = scale[p];
            if (parts[p].gameObject.activeSelf != active[p]) parts[p].gameObject.SetActive(active[p]);
        }
        return travel;
    }

    private static float Farthest(float[,] travel, int p, int samples) => travel[p, FarthestAt(travel, p, samples)];

    private static int FarthestAt(float[,] travel, int p, int samples)
    {
        int at = 0;
        for (int i = 1; i < samples; i++) if (travel[p, i] > travel[p, at]) at = i;
        return at;
    }

    private static bool AnyMoved(float[,] travel, Transform[] parts, Transform root, int samples)
    {
        for (int p = 0; p < parts.Length; p++) if (parts[p] != root && Farthest(travel, p, samples) > 0f) return true;
        return false;
    }

    // The first moment after sample `from` a part gets at least `level` away, in samples (between two); -1 if it never does.
    private static float Above(float[,] travel, int p, int samples, float level, int from)
    {
        for (int i = from + 1; i < samples; i++)
            if (travel[p, i] >= level && travel[p, i - 1] < level) return i - 1 + Mathf.InverseLerp(travel[p, i - 1], travel[p, i], level);
        return -1f;
    }

    // The first moment after sample `from` a part comes within `level` again.
    private static float Below(float[,] travel, int p, int samples, float level, int from)
    {
        for (int i = from + 1; i < samples; i++)
            if (travel[p, i] <= level && travel[p, i - 1] > level) return i - 1 + Mathf.InverseLerp(travel[p, i - 1], travel[p, i], level);
        return -1f;
    }

    // The last moment a part comes within `level` (a magazine seating).
    private static float LastBelow(float[,] travel, int p, int samples, float level)
    {
        for (int i = samples - 1; i >= 1; i--)
            if (travel[p, i] <= level && travel[p, i - 1] > level) return i - 1 + Mathf.InverseLerp(travel[p, i - 1], travel[p, i], level);
        return -1f;
    }
}
