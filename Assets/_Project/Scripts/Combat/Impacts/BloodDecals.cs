using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Blood that hits leave, as URP decal projectors built while the map loads and reused oldest first: splats on the wall
/// or floor behind a target (ImpactVfx) and marks on the body a shot went into (EnemyBloodEffect), which ride the hitbox
/// they were put on. Both were flat see-through quads hung a few centimetres off the surface, and they flickered (the
/// user, 2026-11-14, blood at a slant on a wall by a door frame: "엄청 빠르게 보였다 사라졌다"): see-through quads are
/// drawn back to front by their distance from the eye, so where two splats of different shapes and shades overlapped,
/// the one on top swapped whenever the view moved, and a quad by a corner hung past the edge or went into the side face.
/// A projector paints the surface itself, only where there is one, and URP draws decals in a fixed order (the material's
/// priority, then the material: DecalEntityManager.Update), so nothing swaps from one frame to the next.
/// The pictures are the gore splats' (GoreLibrary.splats: RVFX's and the Cry of Fear decals made for projectors), so all
/// blood in the world is one family and lies under bullet holes and map decals as the gore splats already did (priority
/// -50 against their 0). CombatRehearsal draws each of them once while the map loads.
/// </summary>
[DisallowMultipleComponent]
public sealed class BloodDecals : MonoBehaviour
{
    // As many splats as ImpactVfx kept as quads. Marks fade within seconds, so this many covers a long burst.
    private const int SplatCount = 32, MarkCount = 48;
    // How far a projector reaches into and out of the surface (m): enough for an uneven wall, or for the gap between a
    // hitbox and the skin it stands for, and little enough that a splat by a corner runs only a few centimetres down the
    // side it turns onto and a mark on a body doesn't reach the wall behind it.
    private const float SplatDepth = .12f, MarkDepth = .16f;
    // A splat on the scenery fades over this long once GunFeel's bloodDecalSeconds are up, as the quads did.
    private const float SplatFadeSeconds = 1.2f;
    private static BloodDecals instance;

    private sealed class Slot
    {
        public DecalProjector Projector;
        public float Age, Stay, Fade;
        // The hitbox a mark rides and where on it; null for a splat on the scenery.
        public Transform Rides;
        public Vector3 LocalPoint;
        public Quaternion LocalTurn;
    }

    private readonly Slot[] splats = new Slot[SplatCount], marks = new Slot[MarkCount];
    private int nextSplat, nextMark, lastPicture = -1;

    public static void Prepare()
    {
        if (Application.isPlaying && instance == null)
            instance = FeelAssets.CreateRuntimeRoot("Blood Decals").AddComponent<BloodDecals>();
    }

    /// <summary>A splat `size` across (m) at point, painted into the surface whose `normal` that is. It stays GunFeel's
    /// bloodDecalSeconds, then fades.</summary>
    public static void Splat(Vector3 point, Vector3 normal, float size)
    {
        if (!Application.isPlaying) return;
        Prepare();
        instance.Stamp(instance.splats, ref instance.nextSplat, point, normal, size, SplatDepth,
            GunFeelSettings.Instance.bloodDecalSeconds, SplatFadeSeconds, null);
    }

    /// <summary>
    /// A mark `size` across (m) where a shot went into `hitbox`, riding it and fading from the start over `seconds`. A
    /// hitbox that is switched off takes its marks with it: GoreBody switches off a cut limb's, so no mark floats where
    /// the limb was, not even the one of the shot that cut it, which comes after.
    /// </summary>
    public static void Mark(Transform hitbox, Vector3 point, Vector3 normal, float size, float seconds)
    {
        if (!Application.isPlaying || hitbox == null || !hitbox.gameObject.activeInHierarchy) return;
        Prepare();
        instance.Stamp(instance.marks, ref instance.nextMark, point, normal, size, MarkDepth, 0f, seconds, hitbox);
    }

    private void Awake()
    {
        Build(splats, "Splat");
        Build(marks, "Mark");
    }

    private void OnDestroy() { if (instance == this) instance = null; }

    private void Build(Slot[] ring, string name)
    {
        for (int i = 0; i < ring.Length; i++)
        {
            var host = new GameObject(name);
            host.SetActive(false);
            host.transform.SetParent(transform, false);
            var projector = host.AddComponent<DecalProjector>();
            projector.pivot = Vector3.zero;
            ring[i] = new Slot { Projector = projector };
        }
    }

    private void Stamp(Slot[] ring, ref int next, Vector3 point, Vector3 normal, float size, float depth, float stay,
        float fade, Transform rides)
    {
        var material = Picture();
        if (material == null) return;
        var slot = ring[next];
        next = (next + 1) % ring.Length;
        if (normal.sqrMagnitude < 1e-6f) normal = Vector3.up;
        // Projects along its forward, into the surface, its box centred on the point. Turned at random so a run of hits
        // doesn't read as one stamp repeated.
        var turn = Quaternion.LookRotation(-normal.normalized) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
        var projector = slot.Projector;
        projector.material = material;
        projector.size = new Vector3(size, size, depth);
        projector.fadeFactor = 1f;
        projector.transform.SetPositionAndRotation(point, turn);
        slot.Age = 0f;
        slot.Stay = Mathf.Max(0f, stay);
        slot.Fade = Mathf.Max(.05f, fade);
        slot.Rides = rides;
        if (rides != null)
        {
            slot.LocalPoint = rides.InverseTransformPoint(point);
            slot.LocalTurn = Quaternion.Inverse(rides.rotation) * turn;
        }
        projector.gameObject.SetActive(true);
    }

    // One of the gore splats, never the same picture twice running when there is a choice.
    private Material Picture()
    {
        var library = GoreLibrary.Instance;
        int count = library != null && library.splats != null ? library.splats.Length : 0;
        if (count == 0) return null;
        int pick = Random.Range(0, count);
        if (count > 1 && pick == lastPicture) pick = (pick + 1 + Random.Range(0, count - 1)) % count;
        lastPicture = pick;
        return library.Splat(pick);
    }

    private void Update()
    {
        Age(splats);
        Age(marks);
    }

    private static void Age(Slot[] ring)
    {
        foreach (var slot in ring)
        {
            var projector = slot.Projector;
            if (!projector.gameObject.activeSelf) continue;
            slot.Age += Time.deltaTime;
            float left = slot.Age <= slot.Stay ? 1f : 1f - (slot.Age - slot.Stay) / slot.Fade;
            if (left <= 0f) Retire(slot);
            else projector.fadeFactor = left;
        }
    }

    // Marks follow their hitbox once this frame's animation has moved it.
    private void LateUpdate()
    {
        foreach (var slot in marks)
        {
            if (!slot.Projector.gameObject.activeSelf) continue;
            var rides = slot.Rides;
            if (rides == null || !rides.gameObject.activeInHierarchy) { Retire(slot); continue; }
            slot.Projector.transform.SetPositionAndRotation(rides.TransformPoint(slot.LocalPoint), rides.rotation * slot.LocalTurn);
        }
    }

    private static void Retire(Slot slot)
    {
        slot.Projector.gameObject.SetActive(false);
        slot.Rides = null;
    }
}
