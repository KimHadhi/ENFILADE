using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A Schießhund's duel (결투; the user, 2026-11-12, after Valorant Iso's Kill Contract and Mordekaiser's Realm of Death:
/// "아이소, 모데카이저 궁처럼 상대를 지정하는 1대1임 … 나와 상대는 서로만 때릴 수 있게 됨(물론 내 쪽에서 뎀증 같은 버프는
/// 있음). 이거를 팀을 공격하려던 상대한테 걸어서 세이브 용도로 쓸 수 있음. 아군이 상대를 공격하는 거는 가능함 … 근데
/// 마무리는 못 함. 피가 1 미만으로 안 내려 감. 베이직: 위 내용대로 / 에이스: 나는 표식 상대를 제외한 공격을 받지 않게 되며,
/// 아군이 마무리까지 가능하게 됨"). The class key on a foe calls it out (ClassKit) for some seconds:
/// - the foe goes after the hound alone (EnemyTargeting.Duel) and its blows on anyone else land on nothing (Blocking,
///   PlayerDamageReceiver). A blow it was already winding up is finished first and lands on the friend it was meant for,
///   and does nothing: the friend feels it land, wonders, and finds the hound's line on it (the user, 2026-11-12: "구해지는
///   입장에선 공격을 맞았는데 맞는 느낌이 안 나는 거지. 그래서 오잉?했는데 알고 보니 시순트가 결투를 걸어 구해준 거였다라는
///   느낌이 나는 게 더 뽕이 차"); their screen says whose duel it was (Shielded). The hound's own shots hit whatever they
///   meet as ever, so it can clear the foes in its way ("앞에 길을 막는 적을 처치할 수 없는 거 아니야?");
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
    /// The duel a blow on <paramref name="victim"/> lands on nothing for, or null: it comes from a foe called out by
    /// another hound, or the victim is a hound holding the Ace in a duel and it comes from any foe but theirs. Their own
    /// PC asks (PlayerDamageReceiver).
    /// </summary>
    public static HoundDuel Blocking(PlayerMovement victim, DamageHit hit)
    {
        if (duels.Count == 0 || victim == null || hit.Source == null || hit.Kind == DamageKind.Fall) return null;
        var foe = hit.Source.GetComponentInParent<EnemyHealth>();
        if (foe == null) return null;
        var called = On(foe);
        if (called != null && called.Hound != victim) return called;
        var own = Of(victim);
        return own != null && own.Ace && own.Foe.gameObject != foe.gameObject ? own : null;
    }

    // Friends whose blow this duel turned aside, told once each.
    private readonly HashSet<PlayerMovement> told = new HashSet<PlayerMovement>();

    /// <summary>
    /// The duel turned a blow aside from <paramref name="victim"/> (this PC's player): the first time, their screen says
    /// whose duel saved them, from the hound's side, in the mark's teal.
    /// </summary>
    public void Shielded(PlayerMovement victim)
    {
        if (victim == null || victim == Hound || !told.Add(victim)) return;
        KitSound.Play(KitSound.Cue.Mark, .6f);
        AlertHUD.Team(Texts.Format("class.schiesshund.duel.saved", NameOf(Hound)), Hound.transform, PingMarks.BountyColor);
    }

    private static string NameOf(PlayerMovement player)
        => player != null && player.TryGetComponent(out PlayerTally tally) && !string.IsNullOrEmpty(tally.DisplayName) ? tally.DisplayName
            : player != null ? player.name : "";

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
