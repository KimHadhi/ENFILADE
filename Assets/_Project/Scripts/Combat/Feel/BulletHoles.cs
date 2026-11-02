using UnityEngine;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Bullet holes on walls: URP decals built while the map loads and reused oldest first. They live apart from the impact
/// effects because a pooled effect moves on to its next hit, and a hole has to stay where it was shot. Needs the Decal
/// feature on the renderer. Sizes and materials per surface are in Tables/ImpactEffects.csv, how long they last in GunFeel.
/// </summary>
[DisallowMultipleComponent]
public sealed class BulletHoles : MonoBehaviour
{
    private const int Count = 64;
    private const float Depth = .1f, FadeSeconds = 1f;
    private static BulletHoles instance;
    private readonly DecalProjector[] holes = new DecalProjector[Count];
    private readonly float[] ages = new float[Count];
    private int next;

    public static void Prepare()
    {
        if (Application.isPlaying && instance == null)
            instance = FeelAssets.CreateRuntimeRoot("Bullet Holes").AddComponent<BulletHoles>();
    }

    public static void Place(Material material, float size, Vector3 point, Vector3 normal)
    {
        if (!Application.isPlaying || material == null) return;
        Prepare();
        instance.Stamp(material, size, point, normal);
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

    private void Stamp(Material material, float size, Vector3 point, Vector3 normal)
    {
        int slot = next;
        next = (next + 1) % Count;
        var hole = holes[slot];
        hole.material = material;
        hole.size = new Vector3(size, size, Depth);
        hole.fadeFactor = 1f;
        // Projects along its forward, into the wall. Turned at random so a burst doesn't read as one stamp repeated.
        hole.transform.SetPositionAndRotation(point, Quaternion.LookRotation(-normal) * Quaternion.Euler(0f, 0f, Random.Range(0f, 360f)));
        ages[slot] = 0f;
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
            if (ages[i] < seconds) continue;
            float fade = 1f - (ages[i] - seconds) / FadeSeconds;
            if (fade <= 0f) hole.gameObject.SetActive(false);
            else hole.fadeFactor = fade;
        }
    }
}
