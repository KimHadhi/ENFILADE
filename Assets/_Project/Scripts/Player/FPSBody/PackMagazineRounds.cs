using System.Reflection;
using UnityEngine;

/// <summary>
/// The rounds a gun shows in its see-through magazine, as the FPS Animation Pack made them for its PDW90: the belt of 50 in
/// the top magazine moves along as the magazine empties, the loose rounds ride with the magazine when a reload pulls it out,
/// the last round goes near the end, and the follower and its spring are pushed down by the pack's curve.
/// The pack drives this with Pdw90Animation, a demo script that plays the belt clip on the gun Animator's own graph and reads
/// the rounds from the pack's weapon script. Our guns have neither: the gun's Animator has no controller (its parts play
/// through CAS, TacticalGunSetup) and the pack's weapon scripts come off (FPSBody.AddGun). So it threw at every frame
/// (Editor.log 2026-11-13: "The PlayableGraph is null" once, then "The Playable is null" 384 times). FPSBody takes its settings
/// off it and puts this in its place (Replace), changing nothing of the pack's.
/// The belt clip is sampled once, on a hidden copy of the gun, for the bones its mask moves, and played back by how empty this
/// gun's magazine is (WeaponRuntime, through the player's WeaponController): every PC with the player's weapon shows the
/// same, and a gun not in hand keeps its last look.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.GunFramework + 1)]
public sealed class PackMagazineRounds : MonoBehaviour
{
    // The share of the belt clip an empty magazine plays, how quickly the belt follows the rounds (as the pack's), from how
    // empty the round in the feed goes, how far the follower travels at the curve's 1 (m), and samples kept of the belt.
    private const float EmptyShare = .7f, Follow = 45f, LastRoundFrom = .17f, FollowerTravel = .03f;
    private const int Samples = 48;

    private AnimationClip belt;
    private AvatarMask mask;
    private Transform magazine, bulletTop, bulletBottom, bullet, mechanism, spring;
    private AnimationCurve mechanismCurve;
    private WeaponData weapon;

    private WeaponController controller;
    // The belt bones and their local poses at each sample (0: full .. Samples - 1: empty).
    private Transform[] bones;
    private Vector3[] positions;
    private Quaternion[] rotations;
    private Vector3[] scales;
    private Vector3 magazineIdlePosition;
    private Quaternion magazineIdleRotation = Quaternion.identity;
    // The follower's and the spring's rest, for when the belt's bones don't include them (their push then starts from it
    // each frame instead of piling up).
    private Vector3 mechanismRest, springRest;
    private bool mechanismPosed, springPosed;
    private float empty;
    private bool ready;

    /// <summary>
    /// Takes the place of the pack's demo component <paramref name="demo"/> (Pdw90Animation) on a gun of ours, with its
    /// belt clip, mask, bones and curve, and removes it before it ever wakes. False when its settings couldn't be read (the
    /// pack renamed them): the magazine then just stays full.
    /// </summary>
    internal static bool Replace(MonoBehaviour demo, WeaponData weapon)
    {
        if (demo == null) return false;
        var type = demo.GetType();
        T Field<T>(string name) where T : class
            => type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(demo) as T;
        var rounds = demo.gameObject.AddComponent<PackMagazineRounds>();
        rounds.belt = Field<AnimationClip>("beltAnimation");
        rounds.mask = Field<AvatarMask>("beltMask");
        rounds.magazine = Field<Transform>("magazine");
        rounds.bulletTop = Field<Transform>("bulletTop");
        rounds.bulletBottom = Field<Transform>("bulletBottom");
        rounds.bullet = Field<Transform>("bullet");
        rounds.mechanism = Field<Transform>("mechanism");
        rounds.spring = Field<Transform>("spring");
        rounds.mechanismCurve = Field<AnimationCurve>("mechanismCurve");
        rounds.weapon = weapon;
        DestroyImmediate(demo);
        bool read = rounds.belt != null && rounds.mask != null && rounds.magazine != null;
        if (!read) Debug.LogWarning($"PackMagazineRounds: {type.Name}'s settings on {rounds.name} couldn't be read; its magazine stays full.", rounds);
        return read;
    }

    private void Start()
    {
        controller = GetComponentInParent<WeaponController>();
        ready = belt != null && mask != null && magazine != null && magazine.parent != null && Sample();
    }

    // The belt clip at each sample from full to empty, for the bones the mask moves (not the gun's root), read off a hidden
    // copy of the gun without its scripts or animators, so nothing on the gun itself is touched by the clip.
    private bool Sample()
    {
        var found = new System.Collections.Generic.List<Transform>();
        var paths = new System.Collections.Generic.List<string>();
        for (int i = 0; i < mask.transformCount; i++)
        {
            string path = mask.GetTransformPath(i);
            if (string.IsNullOrEmpty(path) || !mask.GetTransformActive(i)) continue;
            var bone = transform.Find(path);
            if (bone == null) continue;
            found.Add(bone);
            paths.Add(path);
        }
        if (found.Count == 0) return false;
        bones = found.ToArray();
        positions = new Vector3[bones.Length * Samples];
        rotations = new Quaternion[bones.Length * Samples];
        scales = new Vector3[bones.Length * Samples];

        var holder = new GameObject(name + " (belt)");
        holder.SetActive(false);
        try
        {
            var copy = Instantiate(gameObject, holder.transform, false);
            // Last first: a part another needs goes after the one that needs it.
            var parts = copy.GetComponentsInChildren<Component>(true);
            for (int i = parts.Length - 1; i >= 0; i--)
                if (parts[i] is Behaviour || parts[i] is Renderer || parts[i] is Collider || parts[i] is Rigidbody || parts[i] is ParticleSystem)
                    DestroyImmediate(parts[i]);
            // Bones only now: woken, nothing on it runs or shows.
            holder.SetActive(true);
            var copied = new Transform[bones.Length];
            for (int b = 0; b < bones.Length; b++) copied[b] = copy.transform.Find(paths[b]);
            for (int s = 0; s < Samples; s++)
            {
                belt.SampleAnimation(copy, belt.length * EmptyShare * s / (Samples - 1));
                for (int b = 0; b < bones.Length; b++)
                {
                    var posed = copied[b] != null ? copied[b] : bones[b];
                    positions[s * bones.Length + b] = posed.localPosition;
                    rotations[s * bones.Length + b] = posed.localRotation;
                    scales[s * bones.Length + b] = posed.localScale;
                }
            }
        }
        finally
        {
            DestroyImmediate(holder);
        }
        // As the pack does: the belt full, then the magazine's own place as the gun rests, to carry the loose rounds by.
        Pose(0f);
        magazineIdlePosition = magazine.localPosition;
        magazineIdleRotation = magazine.localRotation;
        mechanismPosed = mechanism != null && System.Array.IndexOf(bones, mechanism) >= 0;
        springPosed = spring != null && System.Array.IndexOf(bones, spring) >= 0;
        if (mechanism != null) mechanismRest = mechanism.localPosition;
        if (spring != null) springRest = spring.localScale;
        return true;
    }

    private void LateUpdate()
    {
        if (!ready) return;
        // How empty the magazine is (0 full, 1 empty): the player's weapon while this gun is the one in hand.
        if (controller != null && controller.Runtime != null && controller.EquippedData == weapon && weapon != null && weapon.MagazineSize > 0)
        {
            float target = 1f - Mathf.Clamp01((float)controller.Runtime.Ammo / weapon.MagazineSize);
            empty = Mathf.Lerp(empty, target, 1f - Mathf.Exp(-Follow * Time.deltaTime));
        }
        Pose(empty);

        // The loose rounds aren't under the magazine: they ride with it as it moves off its rest (a reload), as the pack's do.
        Quaternion idleRotation = magazine.parent.rotation * magazineIdleRotation;
        Vector3 idlePosition = magazine.parent.TransformPoint(magazineIdlePosition);
        Carry(bulletTop, idlePosition, idleRotation);
        Carry(bulletBottom, idlePosition, idleRotation);

        if (bullet != null) bullet.localScale = empty > LastRoundFrom ? Vector3.zero : Vector3.one;
        float push = mechanismCurve != null ? mechanismCurve.Evaluate(empty) : 0f;
        if (mechanism != null) mechanism.localPosition = (mechanismPosed ? mechanism.localPosition : mechanismRest) + new Vector3(0f, -push * FollowerTravel, 0f);
        if (spring != null) spring.localScale = (springPosed ? spring.localScale : springRest) + new Vector3(0f, push, 0f);
    }

    // The belt's bones as the clip has them at this emptiness, between the two nearest samples.
    private void Pose(float share)
    {
        float at = Mathf.Clamp01(share) * (Samples - 1);
        int a = Mathf.Min(Samples - 2, Mathf.FloorToInt(at));
        float t = at - a;
        for (int b = 0; b < bones.Length; b++)
        {
            int i = a * bones.Length + b, j = i + bones.Length;
            bones[b].SetLocalPositionAndRotation(Vector3.Lerp(positions[i], positions[j], t), Quaternion.Slerp(rotations[i], rotations[j], t));
            bones[b].localScale = Vector3.Lerp(scales[i], scales[j], t);
        }
    }

    private void Carry(Transform part, Vector3 idlePosition, Quaternion idleRotation)
    {
        if (part == null) return;
        Quaternion back = Quaternion.Inverse(idleRotation);
        Vector3 offset = back * (part.position - idlePosition);
        Quaternion turn = back * part.rotation;
        part.SetPositionAndRotation(magazine.position + magazine.rotation * offset, magazine.rotation * turn);
    }
}
