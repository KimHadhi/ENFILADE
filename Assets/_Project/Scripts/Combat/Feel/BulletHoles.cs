using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Bullet holes on walls: URP decals built while the map loads and reused oldest first. They live apart from the impact
/// effects because a pooled effect moves on to its next hit, and a hole has to stay where it was shot. Needs the Decal
/// feature on the renderer. Sizes and materials per surface are in Tables/ImpactEffects.csv, how long they last in GunFeel.
/// No two are quite one size (the row's holeVary), and a bullet that came in at a slant leaves a long one (user,
/// 2026-11-10: every hole looked the same, and the angle should show): a round of one width marks a wall 1 / cos of its
/// angle long, so the hole is stretched that much along the bullet's way over the surface, up to the row's holeStretch,
/// with the point it hit at the near end. It is still projected straight into the wall; leaning the projector along the
/// bullet would smear it over whatever stands next to the hit.
/// </summary>
[DisallowMultipleComponent]
public sealed class BulletHoles : MonoBehaviour
{
    private const int Count = 64;
    private const float Depth = .1f, FadeSeconds = 1f;
    // A hole stays round until the slant would make it this much longer than wide (about 25 degrees off square).
    private const float StretchFrom = 1.1f;
    private static BulletHoles instance;
    private readonly DecalProjector[] holes = new DecalProjector[Count];
    private readonly float[] ages = new float[Count];
    // How long each one stays before it fades: its surface's own (ImpactEffects.csv holeSeconds: the hub mirror's cracks
    // go in a few seconds), else 0 for GunFeel's.
    private readonly float[] stays = new float[Count];
    private int next;

    public static void Prepare()
    {
        if (Application.isPlaying && instance == null)
            instance = FeelAssets.CreateRuntimeRoot("Bullet Holes").AddComponent<BulletHoles>();
    }

    /// <summary>
    /// A hole `size` across (m) at point, give or take `vary` of it (0-1). `incoming` is the way the bullet was going as it
    /// hit, or as it left for an exit hole: at a slant the hole is up to `stretchMost` times as long along it. Zero, or
    /// a stretchMost of 1, leaves it round. It stays `seconds` before fading (0: GunFeel's bulletHoleSeconds).
    /// </summary>
    public static void Place(Material material, float size, Vector3 point, Vector3 normal, Vector3 incoming = default,
        float vary = 0f, float stretchMost = 1f, float seconds = 0f)
    {
        if (!Application.isPlaying || material == null) return;
        Prepare();
        instance.Stamp(material, size, point, normal, incoming, vary, stretchMost, seconds);
    }

    private void Awake()
    {
        for (int i = 0; i < Count; i++)
        {
            var host = new GameObject("Hole");
            host.SetActive(false);
            host.transform.SetParent(transform, false);
            holes[i] = host.AddComponent<DecalProjector>();
            holes[i].pivot = Vector3.zero;
        }
    }

    private void OnDestroy() { if (instance == this) instance = null; }

    private void Stamp(Material material, float size, Vector3 point, Vector3 normal, Vector3 incoming, float vary, float stretchMost,
        float seconds)
    {
        int slot = next;
        next = (next + 1) % Count;
        var hole = holes[slot];
        hole.material = material;
        vary = Mathf.Clamp(vary, 0f, .9f);
        float width = size * (1f + Random.Range(-vary, vary)), length = width;
        // Projects along its forward, into the wall. Turned at random so a burst doesn't read as one stamp repeated.
        var turn = Quaternion.LookRotation(-normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f));
        if (stretchMost > 1f && incoming.sqrMagnitude > 1e-8f)
        {
            incoming.Normalize();
            // The bullet's way over the surface. An exit hole's normal faces the other way, so the slant is taken either way.
            Vector3 along = Vector3.ProjectOnPlane(incoming, normal);
            float stretch = Mathf.Min(1f / Mathf.Max(Mathf.Abs(Vector3.Dot(incoming, normal)), .01f), stretchMost);
            if (stretch >= StretchFrom && along.sqrMagnitude > 1e-8f)
            {
                along.Normalize();
                length = width * stretch;
                // Its long side lies along the bullet's way, either end first so one picture gives two marks, and it is
                // moved on by half of what it grew so that it starts where the bullet struck.
                turn = Quaternion.LookRotation(-normal, Random.value < .5f ? along : -along);
                point += along * ((length - width) * .5f);
            }
        }
        hole.size = new Vector3(width, length, Depth);
        hole.fadeFactor = 1f;
        hole.transform.SetPositionAndRotation(point, turn);
        ages[slot] = 0f;
        stays[slot] = Mathf.Max(0f, seconds);
        hole.gameObject.SetActive(true);
    }

    private void Update()
    {
        float seconds = GunFeelSettings.Instance.bulletHoleSeconds;
        for (int i = 0; i < Count; i++)
        {
            var hole = holes[i];
            if (!hole.gameObject.activeSelf) continue;
            ages[i] += Time.deltaTime;
            float stay = stays[i] > 0f ? stays[i] : seconds;
            if (ages[i] < stay) continue;
            float fade = 1f - (ages[i] - stay) / FadeSeconds;
            if (fade <= 0f) hole.gameObject.SetActive(false);
            else hole.fadeFactor = fade;
        }
    }
}
