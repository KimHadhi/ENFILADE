using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A laid meal (ZombieMeal) as a body that can still be shot (the user, 2026-11-15: "시체 좀비 … 머리 터뜨려서 확인 사살
/// 할 수 있게 해 주세요. 물론 걔네가 진짜 살아 있진 않다고 해도요"). Only its head takes bullets: a trigger round the head,
/// on the zombies' layer as their own hitboxes are, so sight, feet and every other cast pass the body as before. A bullet in
/// the head bursts it as a headshot kill bursts a living zombie's (ZombieGore.BurstHeadAt): the pieces change to the same
/// body baked with its head taken off the way gore takes one off a skin (GoreBody.Hide), and the head takes no more shots.
/// The shooter's PC decides and tells the others by the eater's network id, which every PC laid its copy under
/// (PlayerNetworkCorpse), and its shooter sees a headshot's marker (ZombieGore.ShotDeadHead). Nothing else counts it: no
/// damage, number, kill or reward (DamageResult.Ignored). It costs nothing a frame: no bones, Animator or Update, one
/// static trigger.
/// </summary>
[DisallowMultipleComponent]
public sealed class ZombieMealBody : MonoBehaviour, IDamageReceiver
{
    private static readonly HashSet<Collider> heads = new HashSet<Collider>();
    private static readonly Dictionary<ulong, ZombieMealBody> byId = new Dictionary<ulong, ZombieMealBody>();

    private MeshFilter[] pieces;
    private Mesh[] headless;
    private Collider head;
    private Transform stump;
    // The head's middle in the meal's own space, where it bursts from.
    private Vector3 middle;
    private ulong id;
    private bool burst;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Clear()
    {
        heads.Clear();
        byId.Clear();
    }

    /// <summary>A meal's head: bullets meet it (AttackTrace), and the part it stands for is the head (ZombieGore.PartOf).</summary>
    public static bool IsHead(Collider collider) => collider != null && heads.Contains(collider);

    /// <summary>Another PC shot the head off the meal laid under this id: it bursts here too, along the same shot.</summary>
    internal static void TakeBurst(ulong id, Vector3 direction)
    {
        if (byId.TryGetValue(id, out var meal) && meal != null) meal.Burst(direction);
    }

    /// <summary>
    /// Made by ZombieMeal.Lay: its pieces with the same pieces baked headless (one for one), its head's trigger, the stump
    /// the neck is left as, the head's middle in its own space, and the eater's network id (0 alone).
    /// </summary>
    internal void Set(MeshFilter[] pieces, Mesh[] headless, Collider head, Transform stump, Vector3 middle, ulong id)
    {
        this.pieces = pieces;
        this.headless = headless;
        this.head = head;
        this.stump = stump;
        this.middle = middle;
        this.id = id;
        if (head != null) heads.Add(head);
        if (id != 0UL) byId[id] = this;
    }

    DamageResult IDamageReceiver.ReceiveHit(DamageHit hit)
    {
        if (hit.Method == DamageMethod.Bullet && hit.Part == BodyPart.Head && Burst(hit.Direction))
            ZombieGore.ShotDeadHead(hit, true, id);
        return DamageResult.Ignored;
    }

    private bool Burst(Vector3 direction)
    {
        if (burst || pieces == null) return false;
        burst = true;
        for (int i = 0; i < pieces.Length; i++)
            if (pieces[i] != null && headless[i] != null) pieces[i].sharedMesh = headless[i];
        if (head != null)
        {
            heads.Remove(head);
            head.enabled = false;
        }
        Vector3 along = direction.sqrMagnitude > 1e-4f ? direction.normalized : -transform.forward;
        ZombieGore.BurstHeadAt(transform.TransformPoint(middle), along, transform, this, stump);
        return true;
    }

    private void OnDestroy()
    {
        if (head != null) heads.Remove(head);
        if (id != 0UL && byId.TryGetValue(id, out var self) && self == this) byId.Remove(id);
    }
}
