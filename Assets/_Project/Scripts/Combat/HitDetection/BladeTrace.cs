using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A blade's cut through one swing: whatever the blade itself passes through is struck, and nothing else (the user,
/// 2026-11-18: "공격 범위 및 히트 판정은 별도 박스 판정이 아닌 True FPS 1인칭 화면에 보이는 실제 무기 모델 자체를 기준으로
/// 처리"). Whoever holds the blade says where it is, from its start to its tip, each time it has moved (Pass), and the
/// cut is what lies between two such places: points along the blade (tracers, no further apart than the blade is
/// thick, so they leave no gap) are swept from where each was to where it is. The first place only finds what the blade
/// is already in. It is how the melee games that judge by the weapon do it (Chivalry's and Mordhau's tracers).
/// What it meets is what a bullet would (AttackTrace.LetsThrough): a zombie's bone hitboxes, where the picture is, not
/// the capsule it walks in, so the blade that visibly crosses an arm cuts that arm (the part goes with the hit: the jolt
/// lands there and the gore's rules for limbs and heads apply to a blade as to a shot). One swing strikes each body once,
/// at the first place the blade met it (the head when it met that in the same pass), and no more bodies than the knife
/// cuts through (ClassKnives.csv penetration). The world never stops the blade: it rings on it once a swing, and what
/// stands behind it from the eyes is not struck. A block or a parry stops the blade there.
/// Before this the swing's hit was a sphere cast out of the eyes along the aim (MeleeAttackSweep, removed): 1.6 m and
/// the knife's extra reach, 0.22 m wide, whatever the arm was doing.
/// </summary>
public sealed class BladeTrace
{
    // Tracers stand this many blade radii apart at most; a blade is cut into no more of them than this.
    private const float Spacing = 2f;
    private const int MostTracers = 16;

    private static readonly RaycastHit[] swept = new RaycastHit[48], between = new RaycastHit[24];
    private static readonly Collider[] inside = new Collider[48];
    private static int syncedFrame = -1;

    private struct Contact
    {
        public Collider Collider;
        public Vector3 Point, Way;
        public float Travelled;
    }

    private readonly List<Contact> contacts = new List<Contact>();
    private readonly HashSet<IDamageReceiver> struck = new HashSet<IDamageReceiver>();
    private PlayerMovement owner;
    private Transform eyes;
    private float damage, posture, radius;
    private int most;
    private LayerMask layers;
    private long attackId;
    private Action onCut, onWorld;
    private Action<Collider, float, bool, DamageResult> onStruck;
    private bool worldMet, placed;
    private Vector3 lastStart, lastTip;

    /// <summary>The swing's blade is live: places given now cut.</summary>
    public bool Open { get; private set; }
    /// <summary>Bodies this swing has cut so far.</summary>
    public int Cut { get; private set; }

    /// <summary>
    /// A swing's blade goes live: what it deals, how many bodies it cuts through, how thick its cut is round the blade's
    /// line (m), and who is told of each body cut and of the world met (once). <paramref name="onStruck"/> hears of every
    /// body the blade reached, cut or not: the collider met, what was dealt, whether it was the head, and what came of it
    /// (the play's record of the swing).
    /// </summary>
    public void Begin(PlayerMovement owner, Transform eyes, float damage, float posture, int most, float radius, LayerMask layers,
        Action onCut = null, Action onWorld = null, Action<Collider, float, bool, DamageResult> onStruck = null)
    {
        End();
        this.owner = owner; this.eyes = eyes; this.damage = damage; this.posture = posture;
        this.most = Mathf.Max(1, most); this.radius = Mathf.Max(.005f, radius); this.layers = layers;
        this.onCut = onCut; this.onWorld = onWorld; this.onStruck = onStruck;
        Cut = 0; worldMet = placed = false; struck.Clear();
        attackId = DamageHit.NewAttackId();
        Open = owner != null;
    }

    /// <summary>The swing already struck this one some other way (a combo's sure strike): the blade leaves them be, and they count.</summary>
    public void Spare(IDamageReceiver who)
    {
        if (!Open || who == null || !struck.Add(who)) return;
        if (++Cut >= most) End();
    }

    public void End() => Open = false;

    /// <summary>The blade is here now, from its start to its tip: it cuts what lies between its last place and this.</summary>
    public void Pass(Vector3 start, Vector3 tip)
    {
        if (!Open) return;
        if (owner == null || owner.CombatInputLocked) { End(); return; }
        // The bone hitboxes go where this frame's animation put the bones only once physics is told (AttackTrace.SyncBodies
        // does it for bullets, which may have been before the animation ran).
        if (syncedFrame != Time.frameCount)
        {
            syncedFrame = Time.frameCount;
            Physics.SyncTransforms();
        }
        contacts.Clear();
        if (!placed)
        {
            placed = true;
            int count = Physics.OverlapCapsuleNonAlloc(start, tip, radius, inside, layers, QueryTriggerInteraction.Collide);
            Vector3 middle = (start + tip) * .5f;
            for (int i = 0; i < count; i++)
                contacts.Add(new Contact { Collider = inside[i], Point = Nearest(inside[i], middle) });
        }
        else
        {
            int tracers = Mathf.Clamp(Mathf.CeilToInt(Vector3.Distance(start, tip) / (radius * Spacing)) + 1, 2, MostTracers);
            for (int i = 0; i < tracers; i++)
            {
                float along = i / (float)(tracers - 1);
                Vector3 from = Vector3.Lerp(lastStart, lastTip, along), step = Vector3.Lerp(start, tip, along) - from;
                float distance = step.magnitude;
                if (distance < 1e-4f) continue;
                Vector3 way = step / distance;
                int count = Physics.SphereCastNonAlloc(from, radius, way, swept, distance, layers, QueryTriggerInteraction.Collide);
                for (int h = 0; h < count; h++)
                {
                    // What the tracer began inside comes back with no point of its own (Unity: distance 0, the zero point).
                    bool began = swept[h].distance <= 0f && swept[h].point == Vector3.zero;
                    contacts.Add(new Contact
                    {
                        Collider = swept[h].collider, Point = began ? Nearest(swept[h].collider, from) : swept[h].point,
                        Way = way, Travelled = swept[h].distance,
                    });
                }
            }
        }
        lastStart = start;
        lastTip = tip;
        if (contacts.Count > 1) contacts.Sort((a, b) => a.Travelled.CompareTo(b.Travelled));
        for (int i = 0; i < contacts.Count && Open; i++)
        {
            var contact = contacts[i];
            var collider = contact.Collider;
            if (collider == null || AttackTrace.LetsThrough(collider, owner.gameObject)) continue;
            var receiver = collider.GetComponentInParent<IDamageReceiver>();
            if (receiver == null)
            {
                if (!worldMet && !collider.isTrigger) { worldMet = true; onWorld?.Invoke(); }
                continue;
            }
            if (struck.Contains(receiver)) continue;
            // The same pass may have met their head a little further on: a blade through the head is a blow to the head.
            for (int k = i + 1; k < contacts.Count; k++)
            {
                var later = contacts[k];
                if (later.Collider == null || !IsHead(later.Collider) || later.Collider.GetComponentInParent<IDamageReceiver>() != receiver) continue;
                contact = later;
                break;
            }
            if (Hidden(contact.Point)) continue;
            struck.Add(receiver);
            Strike(receiver, contact);
        }
    }

    private void Strike(IDamageReceiver receiver, in Contact contact)
    {
        Vector3 from = eyes != null ? eyes.position : lastStart, toward = contact.Point - from;
        toward = toward.sqrMagnitude > 1e-6f ? toward.normalized : eyes != null ? eyes.forward : Vector3.forward;
        // This PC's own player's hands carry their perk (PlayerPerks).
        float dealt = owner.IsLocal ? damage * PlayerPerks.FistFactor : damage;
        // The knife cuts deeper on the head and in the back (MeleeKnife): the back is told from where the one who cuts
        // stands, while the blow itself goes the way the blade was moving, so the body is thrown the way it was cut.
        dealt = MeleeKnife.Hit(owner, contact.Collider, contact.Point, toward, dealt, out bool head);
        Vector3 way = contact.Way.sqrMagnitude > .5f ? contact.Way : toward;
        var result = receiver.ReceiveHit(new DamageHit(attackId, dealt, DamageKind.Attack, contact.Point, way, owner.gameObject, true,
            posture, isCritical: head, method: MeleeKnife.MethodOf(owner), part: ZombieGore.PartOf(contact.Collider)));
        onStruck?.Invoke(contact.Collider, dealt, head, result);
        if (result == DamageResult.Blocked || result == DamageResult.Parried) { End(); return; }
        if (result != DamageResult.Damaged) return;
        contact.Collider.GetComponentInParent<MeleeCombatTarget>().OrNull()?.ApplyPostureDamage(posture);
        Cut++;
        onCut?.Invoke();
        if (Cut >= most) End();
    }

    // Something of the world stands between the eyes and the place the blade met: a blade through a wall's corner or a
    // door cuts nobody behind it. Another body in the way is someone to cut too, not a wall.
    private bool Hidden(Vector3 point)
    {
        if (eyes == null) return false;
        Vector3 from = eyes.position, delta = point - from;
        float length = delta.magnitude;
        if (length < .05f) return false;
        int count = Physics.RaycastNonAlloc(from, delta / length, between, length - .02f, layers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
        {
            var collider = between[i].collider;
            if (AttackTrace.LetsThrough(collider, owner.gameObject) || collider.GetComponentInParent<IDamageReceiver>() != null) continue;
            return true;
        }
        return false;
    }

    private static bool IsHead(Collider collider)
    {
        var region = collider.GetComponentInParent<WeaponHitRegion>();
        return region != null && region.Kind == WeaponHitRegion.Region.Head;
    }

    // The place on a collider nearest a point (its bounds' for a shape Unity gives no nearest point of).
    private static Vector3 Nearest(Collider collider, Vector3 to)
        => collider is MeshCollider mesh && !mesh.convex ? collider.bounds.ClosestPoint(to) : collider.ClosestPoint(to);
}
