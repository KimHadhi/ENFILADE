using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Assault's frag (Throwable.Frag; the user, 2026-11-12: "어썰트: 시원한 고폭 수류탄"). Where it bursts: the blast in
/// the game's own drawn look (CanBlastEffect, the user's pick for the fuel can), stones and grit thrown up and out, CS:GO's
/// HE detonation, the view shaken the more the nearer, and a bang that brings calm zombies to look. On the thrower's PC it
/// hurts what it reaches (no wall between: Throwables.Covered), most at its heart: a zombie it kills near the heart
/// (GrenadeRules fragGib) is torn apart, two limbs off while it still stands and the head burst by the last of it
/// (ZombieGore cuts the parts the hits name, on every PC); further out a kill takes a limb with it, and one that lives
/// may lose a leg and crawl. Survivors near it are knocked down (not a brute), and the bodies are thrown. Players are hurt
/// only with friendly fire on (FriendlyFire), the thrower too, at its share. The hits go as the thrower's (kills, money,
/// the kill feed's frag, an Assault's adrenaline) through the zombies' own copies, as bullets do.
/// </summary>
public static class FragBlast
{
    private static readonly List<EnemyHealth> near = new List<EnemyHealth>();
    // Legs first and more often: a blast comes from the floor.
    private static readonly BodyPart[] Limbs =
    {
        BodyPart.LeftUpperLeg, BodyPart.RightUpperLeg, BodyPart.LeftLowerLeg, BodyPart.RightLowerLeg, BodyPart.LeftUpperLeg,
        BodyPart.RightUpperLeg, BodyPart.LeftUpperArm, BodyPart.RightUpperArm, BodyPart.LeftLowerArm, BodyPart.RightLowerArm,
    };

    /// <summary>A frag bursts here, on every PC; mine: this PC's player threw it, so its hits are this PC's to land.</summary>
    public static void Play(Vector3 at, ThrowableInfo info, PlayerMovement thrower, bool mine)
    {
        if (info == null) return;
        float radius = Mathf.Max(.5f, info.Radius);
        CanBlastEffect.Play(at, radius, 0f);
        FragChunk.Throw(at, Mathf.RoundToInt(Throwables.Rule("fragChunks", 12f)));
        Throwables.Play("Frag", at + Vector3.up * .3f, 1f, 8f, 150f);
        Shake(at, radius);
        // The bang carries: calm zombies near come to look (the host's hear it).
        Noise.Make(at + Vector3.up * .5f, info.Noise > 0f ? info.Noise : 40f, thrower != null ? thrower.gameObject : null);
        if (!mine) return;
        var source = thrower != null ? thrower.gameObject : null;
        Hurt(at, info, radius, source);
        HurtPlayers(at, info, radius, thrower, source);
        FragAftermath.Begin(at, radius, info.Damage, source);
    }

    // The view kicks back and shakes, the more the nearer; out past fragShake times its reach it isn't felt.
    private static void Shake(Vector3 at, float radius)
    {
        var mine = PlayerRoster.Local;
        if (mine == null) return;
        float reach = radius * Mathf.Max(1f, Throwables.Rule("fragShake", 4f));
        float share = 1f - Mathf.Clamp01(Vector3.Distance(mine.transform.position, at) / reach);
        if (share <= 0f) return;
        share *= share;
        var effects = mine.GetComponentInChildren<PlayerCameraEffects>();
        if (effects != null) effects.ApplyCameraImpulse(new Vector3(-10f * share, 0f, (Random.value < .5f ? -5f : 5f) * share), 6f * share);
        var eye = Camera.main;
        if (eye != null) CameraViewPunch.For(eye)?.Punch(new Vector3(-2f * share, 0f, 0f), 0f, .6f * share);
    }

    private static void Hurt(Vector3 at, ThrowableInfo info, float radius, GameObject source)
    {
        near.Clear();
        float most = radius * radius;
        foreach (var enemy in EnemyHealth.Living)
            if (enemy != null && enemy.IsAlive && (enemy.transform.position - at).sqrMagnitude <= most) near.Add(enemy);
        float gib = Mathf.Clamp01(Throwables.Rule("fragGib", .45f)), knock = Mathf.Clamp01(Throwables.Rule("fragKnock", .7f));
        Vector3 heart = at + Vector3.up * .35f;
        foreach (var enemy in near)
        {
            if (enemy == null || !enemy.IsAlive) continue;
            Vector3 chest = enemy.transform.position + Vector3.up;
            if (Throwables.Covered(heart, chest)) continue;
            float distance = Vector3.Distance(at, enemy.transform.position);
            float share = 1f - Mathf.Clamp01(distance / radius);
            float amount = Mathf.Lerp(info.Edge, info.Damage, share);
            if (amount <= 0f) continue;
            Vector3 away = Vector3.ProjectOnPlane(enemy.transform.position - at, Vector3.up);
            away = away.sqrMagnitude > 1e-4f ? away.normalized : Quaternion.Euler(0f, Random.Range(0f, 360f), 0f) * Vector3.forward;
            // Thrown up and away: the kill's doll flies that way (ZombieCorpse), a cut part too (ZombieGore).
            Vector3 thrown = (away + Vector3.up * .8f).normalized;
            if (amount >= enemy.Health && share >= 1f - gib) Gib(enemy, amount, thrown, chest, source, at);
            else Strike(enemy, amount, Limbs[Random.Range(0, Limbs.Length)], thrown, chest, source, at);
            if (enemy != null && enemy.IsAlive && distance <= radius * knock && info.Knock > 0f && enemy.TryGetComponent(out MeleeCombatTarget body))
                body.KnockDown(away, info.Knock);
        }
        near.Clear();
    }

    // Torn apart: while it still stands, a limb and another come off (each blow over ZombieGore's blowCut of its health, as
    // gore counts it), and the rest of the blast bursts its head.
    private static void Gib(EnemyHealth enemy, float amount, Vector3 thrown, Vector3 point, GameObject source, Vector3 at)
    {
        float limbHealth = Mathf.Min(enemy.MaximumHealth, Mathf.Max(1f, Throwables.Rule("fragLimbHealth", 600f)));
        float tear = .47f * limbHealth;
        var first = Limbs[Random.Range(0, Limbs.Length)];
        var second = Other(first);
        foreach (var part in new[] { first, second })
        {
            if (enemy == null || !enemy.IsAlive || enemy.Health - tear < 1f) break;
            Strike(enemy, tear, part, thrown, point, source, at);
            amount -= tear;
        }
        if (enemy != null && enemy.IsAlive) Strike(enemy, Mathf.Max(amount, enemy.Health + 1f), BodyPart.Head, thrown, point, source, at);
    }

    // A second limb on the other side of the body from the first.
    private static BodyPart Other(BodyPart part)
    {
        switch (part)
        {
            case BodyPart.LeftUpperLeg: case BodyPart.LeftLowerLeg: return Random.value < .5f ? BodyPart.RightUpperArm : BodyPart.RightUpperLeg;
            case BodyPart.RightUpperLeg: case BodyPart.RightLowerLeg: return Random.value < .5f ? BodyPart.LeftUpperArm : BodyPart.LeftUpperLeg;
            case BodyPart.LeftUpperArm: case BodyPart.LeftLowerArm: return BodyPart.RightUpperLeg;
            default: return BodyPart.LeftUpperLeg;
        }
    }

    private static void Strike(EnemyHealth enemy, float amount, BodyPart part, Vector3 thrown, Vector3 point, GameObject source, Vector3 at)
        => enemy.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), amount, DamageKind.Attack, point, thrown, source,
            method: DamageMethod.Blast, origin: at, part: part));

    // Friendly fire on: the players it reaches are hurt, the thrower too (at the rule's share) and thrown a little. A
    // friend's copy here passes the blow on to their own PC (PlayerDamageReceiver, FriendlyFire.PassOn).
    private static void HurtPlayers(Vector3 at, ThrowableInfo info, float radius, PlayerMovement thrower, GameObject source)
    {
        if (!FriendlyFire.Enabled || info.PlayerDamage <= 0f) return;
        Vector3 heart = at + Vector3.up * .35f;
        foreach (var player in PlayerRoster.All)
        {
            if (player == null || !player.IsInitialized || player.IsDead || player.DamageReceiver == null) continue;
            float distance = Vector3.Distance(at, player.transform.position);
            if (distance > radius || Throwables.Covered(heart, player.transform.position + Vector3.up)) continue;
            float share = 1f - Mathf.Clamp01(distance / radius);
            float amount = Mathf.Lerp(info.PlayerEdge, info.PlayerDamage, share);
            if (amount <= 0f) continue;
            Vector3 away = Vector3.ProjectOnPlane(player.transform.position - at, Vector3.up);
            away = away.sqrMagnitude > 1e-4f ? away.normalized : -player.transform.forward;
            bool self = player == thrower;
            player.DamageReceiver.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), self ? amount * FriendlyFire.Share : amount,
                DamageKind.Attack, player.transform.position + Vector3.up, away, source, method: DamageMethod.Blast, origin: at));
            if (self && player.IsLocal) player.KnockBack(away * (6f * share) + Vector3.up * (2.5f * share));
        }
    }
}

/// <summary>
/// The frag's aftermath, on the thrower's PC a moment after the burst: the bodies in its reach, the ones it just made
/// among them, are thrown up and away (a hit on a corpse's doll pushes it: ZombieCorpse).
/// </summary>
internal sealed class FragAftermath : MonoBehaviour
{
    private const float Delay = .05f;
    private static readonly Collider[] found = new Collider[128];
    private static readonly HashSet<RagdollCorpse> pushed = new HashSet<RagdollCorpse>();
    private Vector3 at;
    private float radius, damage, age;
    private GameObject source;

    internal static void Begin(Vector3 at, float radius, float damage, GameObject source)
    {
        var host = new GameObject("Frag Aftermath");
        var after = host.AddComponent<FragAftermath>();
        after.at = at;
        after.radius = radius;
        after.damage = damage;
        after.source = source;
    }

    private void Update()
    {
        if ((age += Time.deltaTime) < Delay) return;
        int count = Physics.OverlapSphereNonAlloc(at, radius, found, ~0, QueryTriggerInteraction.Collide);
        pushed.Clear();
        for (int i = 0; i < count; i++)
        {
            var doll = found[i] != null ? found[i].GetComponentInParent<RagdollCorpse>() : null;
            if (doll == null || !pushed.Add(doll)) continue;
            Vector3 point = found[i].bounds.center;
            float share = 1f - Mathf.Clamp01(Vector3.Distance(at, point) / Mathf.Max(.5f, radius));
            Vector3 away = Vector3.ProjectOnPlane(point - at, Vector3.up);
            away = away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.forward;
            ((IDamageReceiver)doll).ReceiveHit(new DamageHit(DamageHit.NewAttackId(), Mathf.Max(1f, damage * .5f * share), DamageKind.Attack,
                point, (away + Vector3.up * .9f).normalized, source, method: DamageMethod.Blast, origin: at));
        }
        pushed.Clear();
        Destroy(gameObject);
    }
}

/// <summary>A stone or a lump of grit the frag throws up: it flies, tumbles, bounces and shrinks away (layer 31, as gore's pieces).</summary>
internal sealed class FragChunk : MonoBehaviour
{
    private float life, age;
    private Vector3 size;

    internal static void Throw(Vector3 at, int count)
    {
        for (int i = 0; i < count; i++)
        {
            var chunk = GameObject.CreatePrimitive(i % 3 == 0 ? PrimitiveType.Sphere : PrimitiveType.Cube);
            chunk.name = "Frag Chunk";
            chunk.layer = GoreGibs.Layer;
            float size = Random.Range(.025f, .075f);
            chunk.transform.position = at + Vector3.up * .15f + Random.insideUnitSphere * .2f;
            chunk.transform.rotation = Random.rotation;
            chunk.transform.localScale = new Vector3(size, size * Random.Range(.5f, 1f), size * Random.Range(.6f, 1.2f));
            float dark = Random.Range(.08f, .22f);
            chunk.GetComponent<Renderer>().sharedMaterial = Throwables.Look(new Color(dark, dark * .95f, dark * .9f));
            var collider = chunk.GetComponent<Collider>();
            collider.excludeLayers = LayerMask.GetMask("Enemy") | (1 << 31);
            var body = chunk.AddComponent<Rigidbody>();
            body.mass = .05f;
            Vector2 flat = Random.insideUnitCircle.normalized * Random.Range(2.5f, 7f);
            body.linearVelocity = new Vector3(flat.x, Random.Range(4f, 10f), flat.y);
            body.angularVelocity = Random.insideUnitSphere * 25f;
            var self = chunk.AddComponent<FragChunk>();
            self.life = Random.Range(1.1f, 1.9f);
            self.size = chunk.transform.localScale;
        }
    }

    private void Update()
    {
        age += Time.deltaTime;
        float left = life - age;
        if (left < .3f) transform.localScale = size * Mathf.Clamp01(left / .3f);
        if (left <= 0f) Destroy(gameObject);
    }
}
