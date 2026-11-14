using UnityEngine;

/// <summary>
/// A piece thrown off a zombie (GoreGibs): it trails blood while it flies, leaves a splat where it first lands hard, lies a
/// while, then sinks into the floor. A cut limb is then gone, taking its own mesh with it; a gut chunk (no mesh of its
/// own) is put away to be thrown again by the next burst (GoreGibs.Release).
/// </summary>
[DisallowMultipleComponent]
internal sealed class GoreGib : MonoBehaviour
{
    private const float TrailSeconds = .7f, SinkDepth = .4f;
    private static readonly System.Collections.Generic.HashSet<Collider> pieces = new System.Collections.Generic.HashSet<Collider>();

    /// <summary>A thrown piece's collider: bullets pass it (AttackTrace), so a severed limb on the floor never stops a shot.</summary>
    public static bool IsPiece(Collider collider) => collider != null && pieces.Count > 0 && pieces.Contains(collider);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget() => pieces.Clear();
    private ZombieGore owner;
    private Mesh owned;
    private Rigidbody rigid;
    private Collider[] shapes;
    private bool trail, splatted;
    private float age, lie, sinkLeft;

    public bool Sinking => sinkLeft > 0f || age >= lie;

    /// <summary>Starts it flying from where it is, fresh: a chunk thrown again starts over as a new one.</summary>
    public void Begin(ZombieGore gore, Mesh mesh, bool bleeds)
    {
        owner = gore;
        owned = mesh;
        trail = bleeds;
        if (rigid == null) rigid = GetComponent<Rigidbody>();
        if (shapes == null) shapes = GetComponents<Collider>();
        lie = gore != null ? gore.GibLie : 20f;
        age = sinkLeft = 0f;
        splatted = false;
        if (rigid != null) rigid.isKinematic = false;
        foreach (var shape in shapes)
        {
            shape.enabled = true;
            pieces.Add(shape);
        }
    }

    /// <summary>Starts sinking now (too many pieces about).</summary>
    public void SinkNow() => age = Mathf.Max(age, lie);

    private void Update()
    {
        float dt = Time.deltaTime;
        age += dt;
        if (trail && age < TrailSeconds && owner != null && rigid != null && !rigid.isKinematic)
            owner.Fx.Drip(transform.position, rigid.linearVelocity, 1f - age / TrailSeconds);
        if (age < lie) return;
        float sink = owner != null ? owner.GibSink : 2f;
        if (sinkLeft <= 0f)
        {
            sinkLeft = Mathf.Max(.05f, sink);
            if (rigid != null) rigid.isKinematic = true;
            foreach (var shape in shapes) shape.enabled = false;
        }
        transform.position += Vector3.down * (SinkDepth / Mathf.Max(.05f, sink)) * dt;
        sinkLeft -= dt;
        if (sinkLeft > 0f) return;
        if (owned == null && owner != null) owner.Gibs.Release(this);
        else Destroy(gameObject);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (splatted || owner == null || collision.relativeVelocity.sqrMagnitude < 2f || collision.contactCount == 0) return;
        if (collision.collider.GetComponentInParent<IDamageReceiver>() != null) return;
        splatted = true;
        var contact = collision.GetContact(0);
        owner.Fx.Splat(contact.point, contact.normal, owned != null ? .45f : .3f);
    }

    private void OnDestroy()
    {
        if (shapes != null) foreach (var shape in shapes) pieces.Remove(shape);
        if (owned != null) Destroy(owned);
    }
}
