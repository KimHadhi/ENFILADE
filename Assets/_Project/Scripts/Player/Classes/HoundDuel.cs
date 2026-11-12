using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A Schießhund's duel (결투; the user, 2026-11-12, after Valorant Iso's Kill Contract and Mordekaiser's Realm of Death:
/// "아이소, 모데카이저 궁처럼 상대를 지정하는 1대1임 … 나와 상대는 서로만 때릴 수 있게 됨(물론 내 쪽에서 뎀증 같은 버프는
/// 있음). 이거를 팀을 공격하려던 상대한테 걸어서 세이브 용도로 쓸 수 있음. 아군이 상대를 공격하는 거는 가능함 … 근데
/// 마무리는 못 함. 피가 1 미만으로 안 내려 감. 베이직: 위 내용대로 / 에이스: 나는 표식 상대를 제외한 공격을 받지 않게 되며,
/// 아군이 마무리까지 가능하게 됨"). The class key on a foe calls it out (ClassKit) for some seconds:
/// - the two can only hurt each other: the foe goes after the hound alone (EnemyTargeting.Duel) and its blows on anyone
///   else land on nothing (Blocks, PlayerDamageReceiver), and the hound's bullets pass through every other foe on their
///   way (PassesThrough, AttackTrace) and its blows hurt no other (Spares, EnemyHealth), so the duel can be fought
///   through a crowd and a friend about to be torn up is saved by drawing the blow;
/// - the hound hits it harder (PlayerClass.GunFactor, duelDamage), and it carries the hound's bounty mark, so a
///   Desperado round in its head ends it as a bounty;
/// - friends may shoot it to help, but never finish it: it can't drop below 1 health to anyone but the hound (Floor);
/// - with the Ace card (the Schießhund's final card) nothing but the foe can hurt the hound while it lasts, and friends
///   may finish it too.
/// It ends when either falls or time runs out; another duel called by the same hound takes its place. Every PC keeps the
/// same duels (PlayerNetworkCombat.DuelRpc), so each judges what it judges by one rule: the host its zombies' aim and
/// health, each player's own PC their own hurt, and every PC the path of each shot. A line of the mark's teal joins the
/// two while it lasts, for everyone to see who is whose. A practice duel on the hub's range is this PC's alone.
/// </summary>
public sealed class HoundDuel : MonoBehaviour
{
    private static readonly List<HoundDuel> duels = new List<HoundDuel>();
    private const int Points = 2;

    /// <summary>The hound who called it, the foe they called out, until when, and whether with the Ace card.</summary>
    public PlayerMovement Hound { get; private set; }
    public Component Foe { get; private set; }
    public bool Ace { get; private set; }
    private float until, total = 1f;
    private LineRenderer line;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => duels.Clear();

    /// <summary>How much of it is left, 0 to 1.</summary>
    public float Share => Mathf.Clamp01((until - Time.time) / total);

    /// <summary>
    /// A duel begins (this PC's hound's own, or another's as their PC said it): <paramref name="hound"/> against
    /// <paramref name="foe"/> for <paramref name="seconds"/>. The hound's last duel, if still on, gives way to it.
    /// </summary>
    public static HoundDuel Begin(PlayerMovement hound, Component foe, float seconds, bool ace)
    {
        if (!Application.isPlaying || hound == null || foe == null || !(foe is ICombatFoe alive) || !alive.IsAlive) return null;
        End(hound);
        var host = FeelAssets.CreateRuntimeRoot("Duel " + hound.name);
        var duel = host.AddComponent<HoundDuel>();
        duel.Hound = hound;
        duel.Foe = foe;
        duel.Ace = ace;
        duel.total = Mathf.Max(.1f, seconds);
        duel.until = Time.time + duel.total;
        duel.Line();
        duels.Add(duel);
        // The host's or a lone PC's zombie goes after the hound alone while it lasts (a copy follows its host).
        if (foe is EnemyHealth enemy) EnemyTargeting.Duel(enemy, hound, duel.total);
        return duel;
    }

    /// <summary>This hound's duel ends now, if one is on.</summary>
    public static void End(PlayerMovement hound)
    {
        for (int i = duels.Count - 1; i >= 0; i--)
            if (duels[i] == null || duels[i].Hound == hound) Close(i);
    }

    /// <summary>The duel this hound is in now, or null.</summary>
    public static HoundDuel Of(PlayerMovement hound)
    {
        if (hound == null) return null;
        foreach (var duel in duels) if (duel != null && duel.Hound == hound && duel.Live) return duel;
        return null;
    }

    /// <summary>The duel this foe is called out in now, or null.</summary>
    public static HoundDuel On(Component foe)
    {
        if (foe == null || duels.Count == 0) return null;
        foreach (var duel in duels) if (duel != null && duel.Live && duel.Foe != null && duel.Foe.gameObject == foe.gameObject) return duel;
        return null;
    }

    /// <summary>
    /// A bullet of <paramref name="owner"/>'s met <paramref name="collider"/>: true when the owner is a hound in a duel and
    /// that is another foe, which the bullet passes through on its way (AttackTrace). Every PC traces it the same.
    /// </summary>
    public static bool PassesThrough(GameObject owner, Collider collider)
    {
        if (duels.Count == 0 || owner == null || collider == null) return false;
        var duel = Of(owner.GetComponentInParent<PlayerMovement>());
        if (duel == null) return false;
        var foe = CombatFoe.Of(collider);
        return foe != null && foe.gameObject != duel.Foe.gameObject;
    }

    /// <summary>A blow or shot on <paramref name="foe"/> that hurts nothing: its source is a hound in a duel with another foe.</summary>
    public static bool Spares(Component foe, DamageHit hit)
    {
        if (duels.Count == 0 || foe == null || hit.Source == null) return false;
        var duel = Of(hit.Source.GetComponentInParent<PlayerMovement>());
        return duel != null && duel.Foe.gameObject != foe.gameObject;
    }

    /// <summary>
    /// The least health <paramref name="foe"/> keeps after this hit: 1 (or what it has, if less) while it is called out
    /// and the hit isn't its hound's, unless the hound holds the Ace; else 0.
    /// </summary>
    public static float Floor(Component foe, DamageHit hit, float before)
    {
        var duel = On(foe);
        if (duel == null || duel.Ace) return 0f;
        var from = hit.Source != null ? hit.Source.GetComponentInParent<PlayerMovement>() : null;
        return from == duel.Hound ? 0f : Mathf.Min(1f, before);
    }

    /// <summary>
    /// Whether a blow on <paramref name="victim"/> lands on nothing: it comes from a foe called out by another hound, or
    /// the victim is a hound holding the Ace in a duel and it comes from any foe but theirs. Their own PC asks
    /// (PlayerDamageReceiver).
    /// </summary>
    public static bool Blocks(PlayerMovement victim, DamageHit hit)
    {
        if (duels.Count == 0 || victim == null || hit.Source == null || hit.Kind == DamageKind.Fall) return false;
        var foe = hit.Source.GetComponentInParent<EnemyHealth>();
        if (foe == null) return false;
        var called = On(foe);
        if (called != null && called.Hound != victim) return true;
        var own = Of(victim);
        return own != null && own.Ace && own.Foe.gameObject != foe.gameObject;
    }

    // Still on: time left, both still standing.
    private bool Live => Time.time < until && Hound != null && Foe != null && Foe is ICombatFoe foe && foe.IsAlive
        && !Hound.IsDead && !Hound.IsDowned;

    private void Update()
    {
        if (!Live)
        {
            int index = duels.IndexOf(this);
            if (index >= 0) Close(index);
            else Destroy(gameObject);
            return;
        }
        // The line between them, the mark's teal, beating.
        Vector3 from = Hound.transform.position + Vector3.up * 1.25f, to = CombatFoe.Top(Foe) - Vector3.up * .45f;
        line.SetPosition(0, from);
        line.SetPosition(1, to);
        float beat = .55f + .25f * Mathf.Sin(Time.time * 8f);
        Color color = KitStyle.With(PingMarks.BountyColor, beat * Mathf.Clamp01((until - Time.time) / .5f));
        line.startColor = line.endColor = color;
    }

    private static void Close(int index)
    {
        var duel = duels[index];
        duels.RemoveAt(index);
        if (duel != null) Destroy(duel.gameObject);
    }

    private void Line()
    {
        line = gameObject.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.positionCount = Points;
        line.widthMultiplier = .045f;
        line.numCapVertices = 2;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = FeelAssets.Particle(Texture2D.whiteTexture, true);
        line.SetPosition(0, Hound.transform.position);
        line.SetPosition(1, Foe.transform.position);
    }
}
