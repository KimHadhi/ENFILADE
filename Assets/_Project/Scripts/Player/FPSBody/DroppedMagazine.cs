using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A magazine let go of in a reload (GunMagazine): a body of its own that tumbles to the floor with a clack, lies there a
/// while and then sinks out of sight. It lies on the corpses' layer, the players' own, which the camera and every
/// movement probe leave out, and players pass through it, so a floor full of them never trips anyone up. Past
/// GunMagazine's mostOnFloor the oldest one goes first. Physics runs on each PC by itself; nothing is sent.
/// </summary>
[DisallowMultipleComponent]
public sealed class DroppedMagazine : MonoBehaviour
{
    // How hard it has to hit something to clack (m/s), the fewest seconds between two clacks, and the most clacks it makes.
    private const float ClackSpeed = 1.2f, ClackGap = .12f;
    private const int MostClacks = 3;
    private const float SinkDepth = .25f;

    private static readonly List<DroppedMagazine> lying = new List<DroppedMagazine>();

    private Rigidbody body;
    private float age, life, sinkSeconds, sinking = -1f, lastClack = -1f, volume;
    private int clacks;
    private Vector3 sinkFrom;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => lying.Clear();

    /// <summary>A new magazine on its way down. Over the most allowed, the oldest one starts to sink.</summary>
    internal static DroppedMagazine Make(Mesh mesh, Material[] materials, Vector3 position, Quaternion rotation, Vector3 scale,
        Vector3 velocity, Vector3 spin, float mass, float lifeSeconds, float sinkSeconds, float clackVolume, int most)
    {
        var host = new GameObject("Dropped Magazine") { layer = GoreGibs.Layer };
        host.transform.SetPositionAndRotation(position, rotation);
        host.transform.localScale = scale;
        host.AddComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = host.AddComponent<MeshRenderer>();
        renderer.sharedMaterials = materials;
        var box = host.AddComponent<BoxCollider>();
        box.center = mesh.bounds.center;
        box.size = Vector3.Max(mesh.bounds.size, Vector3.one * .005f);
        var magazine = host.AddComponent<DroppedMagazine>();
        var body = magazine.body = host.AddComponent<Rigidbody>();
        body.mass = Mathf.Max(.01f, mass);
        body.linearDamping = .05f;
        body.angularDamping = .3f;
        body.interpolation = RigidbodyInterpolation.Interpolate;
        body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
        body.linearVelocity = velocity;
        body.angularVelocity = spin;
        // Players walk through it, as through corpses and gore.
        foreach (var player in PlayerRoster.All)
        {
            if (player == null) continue;
            foreach (var collider in player.GetComponentsInChildren<Collider>()) Physics.IgnoreCollision(box, collider, true);
        }
        magazine.life = Mathf.Max(0f, lifeSeconds);
        magazine.sinkSeconds = Mathf.Max(.05f, sinkSeconds);
        magazine.volume = clackVolume;
        lying.Add(magazine);
        int over = lying.Count - Mathf.Max(1, most);
        for (int i = 0; i < lying.Count && over > 0; i++)
        {
            if (lying[i] == null || lying[i] == magazine || lying[i].sinking >= 0f) continue;
            lying[i].Sink();
            over--;
        }
        return magazine;
    }

    private void Update()
    {
        age += Time.deltaTime;
        if (sinking < 0f)
        {
            if (age >= life) Sink();
            return;
        }
        sinking += Time.deltaTime;
        float share = Mathf.Clamp01(sinking / sinkSeconds);
        transform.position = sinkFrom - Vector3.up * (SinkDepth * share);
        if (share >= 1f) Destroy(gameObject);
    }

    // Through the floor, slowly, where nobody's looking for it any more.
    private void Sink()
    {
        if (sinking >= 0f) return;
        sinking = 0f;
        sinkFrom = transform.position;
        if (body != null) { body.isKinematic = true; body.detectCollisions = false; }
    }

    private void OnCollisionEnter(Collision hit)
    {
        if (clacks >= MostClacks || age - lastClack < ClackGap || volume <= 0f) return;
        float speed = hit.relativeVelocity.magnitude;
        if (speed < ClackSpeed) return;
        clacks++;
        lastClack = age;
        // Metal on the floor: the magazine's own release sound, lower and softer the gentler it lands.
        Vector3 point = hit.contactCount > 0 ? hit.GetContact(0).point : transform.position;
        float strength = Mathf.InverseLerp(ClackSpeed, 6f, speed);
        FeelAudio.PlayAt(FeelSound.MagOut, null, point, volume * Mathf.Lerp(.35f, 1f, strength), Random.Range(.68f, .8f), 14f);
    }

    private void OnDestroy() => lying.Remove(this);
}
