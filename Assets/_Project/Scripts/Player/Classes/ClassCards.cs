using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The class cards that need more than a number (Docs/ClassCards.md; the numbers alone are PlayerPerks.AbilityBonus),
/// at work for this PC's own player (ClassKit adds it). What they do is judged here, where the player's own hits are;
/// how they look goes to every PC (CardNet, CardFx).
/// <list type="bullet">
/// <item>Momentum (기세, everyone's; the user's "때릴 때마다 갱신되는 강화 효과"): every hit this player lands on an
/// enemy keeps it going a few seconds more (PlayerPerks.Struck). A miss takes nothing; it only lets it run out.</item>
/// <item>Ghost, White Tiger (화이트 타이거, its final card; the user named it for the snipers called White Death and
/// White Feather): a headshot kill with the Head Hunter full rakes white claws across the head, staggers the enemies
/// round it, and for some seconds no hurt takes the stacks (ClassKit asks TigerGuards).</item>
/// <item>Point, Gunslinger (건슬링어): the gun just drawn hits harder a moment (ClassKit.DamageFactor); its time shows
/// on the class HUD's bar.</item>
/// <item>Assault, Broken Arrow (브로큰 애로우, the user's name: the radio call for every gun in reach when a unit is being
/// overrun): falling under a share of health or being grabbed calls a barrage down after a moment, shells landing one
/// after another on the enemies nearest first (the ones holding them before all), never hurting a player; once in a
/// while. Danger Close (데인저 클로즈) is a plain factor on near enemies (PlayerClass.GunFactor).</item>
/// <item>Assault, the phantom cavalry (유령 기병대, its final card; the user's "돌진할 때 일직선으로 적들 쓸고 나가는"):
/// a CHARGE sends five riders galloping out along where they look, and whatever stands in their line is hit and thrown
/// aside, knocked down (a brute only reels).</item>
/// </list>
/// The HUD's bar (ClassHUD) shows the card state under way: BarShare, BarWord, BarColor.
/// </summary>
[DisallowMultipleComponent]
public sealed class ClassCards : MonoBehaviour
{
    private const string GhostRow = PlayerClass.GhostRow, PointRow = "Point", AssaultRow = "Assault";

    /// <summary>This PC's own player's cards at work.</summary>
    public static ClassCards Local { get; private set; }

    private PlayerMovement movement;
    private ClassKit kit;
    // White Tiger: until when the stacks are safe and how long that lasts; when its claws may strike again; a headshot
    // kill waiting for its shot's stack to be counted (ClassKit counts it after the hit), and where.
    private float tigerUntil = -99f, tigerTotal = 1f, tigerAgain = -99f;
    private int tigerFrame = -1;
    private Vector3 tigerAt, tigerFrom;
    // Broken Arrow: when it may be called again; whether health stood above its line last frame and whether they were
    // held; the shells still to call and when the next one is; the shells on their way down, landing when and where.
    private float brokenAgain = -99f, shellAt;
    private bool wasHigh = true, wasHeld;
    private int shellsLeft;
    private readonly List<(float due, Vector3 spot)> falling = new List<(float due, Vector3 spot)>();
    private readonly List<EnemyHealth> near = new List<EnemyHealth>();
    // The phantom cavalry riding: from where, which way, since when; and whom it has hit.
    private bool riding;
    private Vector3 rideFrom, rideWay;
    private float rideSince;
    private readonly HashSet<EnemyHealth> ridden = new HashSet<EnemyHealth>();

    /// <summary>A Ghost's White Tiger holds: hurt takes no Head Hunter stack (ClassKit).</summary>
    public bool TigerGuards => tigerUntil > Time.time && PlayerClass.Is(PlayerClass.Ghost);
    public float TigerShare => TigerGuards ? Mathf.Clamp01((tigerUntil - Time.time) / tigerTotal) : 0f;

    /// <summary>The card state the class HUD's bar shows now, 0 to 1 (0: none), its word and its colour.</summary>
    public float BarShare
    {
        get
        {
            if (TigerShare > 0f) return TigerShare;
            return kit != null ? kit.GunslingerShare : 0f;
        }
    }

    public string BarWord => TigerShare > 0f ? Texts.Get("class.hud.tiger") : Texts.Get("class.hud.gunslinger");
    public Color BarColor => TigerShare > 0f ? KitStyle.Ice : KitStyle.Accent;

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        kit = GetComponent<ClassKit>();
    }

    private void OnEnable() => CombatDamageEvents.Damaged += OnDamaged;

    private void OnDisable()
    {
        CombatDamageEvents.Damaged -= OnDamaged;
        if (Local == this)
        {
            Local = null;
            CardNet.Stop();
        }
    }

    private void Update()
    {
        if (movement == null || !movement.IsLocal) return;
        Local = this;
        if (kit == null) kit = GetComponent<ClassKit>();
        // In a room, hear the others' card effects.
        var network = GameSession.Network;
        if (network != null && network.IsListening) CardNet.Listen();
        BrokenArrow();
        Ride();
    }

    // After the frame's shots are counted (ClassKit adds a headshot's stack once the shot is traced).
    private void LateUpdate()
    {
        if (tigerFrame < 0) return;
        tigerFrame = -1;
        if (kit != null && kit.Hunter >= kit.HunterMax) Tiger();
    }

    // Every hit of this player's on an enemy: Momentum goes on; a Ghost's headshot kill may wake the White Tiger.
    private void OnDamaged(CombatDamageReport report)
    {
        if (movement == null || !movement.IsLocal || report.Target == null || !PlayerPerks.IsMine(report.Hit.Source)) return;
        if (report.Target.GetComponentInParent<PlayerMovement>() != null) return;
        PlayerPerks.Struck();
        if (report.IsKillingBlow && report.Hit.Method == DamageMethod.Bullet && report.Hit.IsCritical
            && PlayerClass.Is(PlayerClass.Ghost) && PlayerPerks.Has(PlayerPerks.GhostTiger))
        {
            tigerFrame = Time.frameCount;
            tigerAt = report.Hit.Point;
            tigerFrom = report.Hit.Origin ?? transform.position + Vector3.up * 1.5f;
        }
    }

    // ---- Assault: Broken Arrow ----

    // Watched every frame: health falling through its line, or a grab taking hold, calls the barrage (when it may be
    // called); then its shells go out one by one, and each one landing hurts what is round it.
    private void BrokenArrow()
    {
        var receiver = movement.DamageReceiver;
        bool able = receiver != null && receiver.IsAlive && !movement.IsDead;
        bool high = able && receiver.MaximumHealth > 0f
            && receiver.Health / receiver.MaximumHealth * 100f > PlayerClass.Ability(AssaultRow, "brokenHealth", 30f);
        bool held = able && movement.IsHeld;
        if (able && PlayerClass.Is(PlayerClass.Assault) && PlayerPerks.Has(PlayerPerks.AssaultBroken) && Time.time >= brokenAgain
            && ((wasHigh && !high) || (held && !wasHeld)))
            CallBrokenArrow();
        wasHigh = high || !able;
        wasHeld = held;
        if (shellsLeft > 0 && Time.time >= shellAt)
        {
            shellsLeft--;
            shellAt = Time.time + Mathf.Max(.02f, PlayerClass.Ability(AssaultRow, "brokenEvery", .22f));
            Shell();
        }
        for (int i = falling.Count - 1; i >= 0; i--)
        {
            if (Time.time < falling[i].due) continue;
            Land(falling[i].spot);
            falling.RemoveAt(i);
        }
    }

    private void CallBrokenArrow()
    {
        brokenAgain = Time.time + Mathf.Max(1f, PlayerClass.Ability(AssaultRow, "brokenCooldown", 75f));
        shellsLeft = Mathf.Max(1, Mathf.RoundToInt(PlayerPerks.Total(PlayerPerks.AssaultBroken)));
        shellAt = Time.time + Mathf.Max(0f, PlayerClass.Ability(AssaultRow, "brokenDelay", 1.5f));
        AlertHUD.Self(Texts.Get("class.assault.broken.self"));
        KitSound.Play(KitSound.Cue.Alert, .8f);
    }

    // One shell called down: on the next of the nearest enemies round this player (in turn, the nearest first), or on
    // open ground round them when nobody is there.
    private void Shell()
    {
        Vector3 me = transform.position;
        float reach = Mathf.Max(1f, PlayerClass.Ability(AssaultRow, "brokenRadius", 9f));
        near.Clear();
        foreach (var enemy in EnemyHealth.Living)
            if (enemy != null && enemy.IsAlive && (enemy.transform.position - me).sqrMagnitude <= reach * reach) near.Add(enemy);
        near.Sort((a, b) => (a.transform.position - me).sqrMagnitude.CompareTo((b.transform.position - me).sqrMagnitude));
        int total = Mathf.Max(1, Mathf.RoundToInt(PlayerPerks.Total(PlayerPerks.AssaultBroken))), index = Mathf.Max(0, total - 1 - shellsLeft);
        Vector3 at;
        if (near.Count > 0)
        {
            var target = near[index % near.Count];
            Vector2 jitter = Random.insideUnitCircle * .5f;
            at = target.transform.position + new Vector3(jitter.x, 0f, jitter.y);
        }
        else
        {
            Vector2 spot = Random.insideUnitCircle.normalized * Random.Range(3f, reach);
            at = me + new Vector3(spot.x, 0f, spot.y);
        }
        if (Physics.Raycast(at + Vector3.up * 3f, Vector3.down, out RaycastHit ground, 8f, ~(1 << 31), QueryTriggerInteraction.Ignore))
            at = ground.point;
        float blast = Mathf.Max(.5f, PlayerClass.Ability(AssaultRow, "brokenBlast", 3f));
        Vector2 lean = Random.insideUnitCircle;
        CardNet.Show(CardFx.Kind.Shell, at, new Vector3(lean.x, 0f, lean.y), blast);
        falling.Add((Time.time + CardFx.ShellFall, at));
    }

    // A shell landing: every enemy in its blast is hurt (less toward its edge) and thrown down; no player is.
    private void Land(Vector3 at)
    {
        float blast = Mathf.Max(.5f, PlayerClass.Ability(AssaultRow, "brokenBlast", 3f));
        float damage = Mathf.Max(0f, PlayerClass.Ability(AssaultRow, "brokenDamage", 150f));
        near.Clear();
        foreach (var enemy in EnemyHealth.Living)
            if (enemy != null && enemy.IsAlive && (enemy.transform.position - at).sqrMagnitude <= blast * blast) near.Add(enemy);
        foreach (var enemy in near)
        {
            Vector3 away = Vector3.ProjectOnPlane(enemy.transform.position - at, Vector3.up);
            float share = 1f - .5f * Mathf.Clamp01(away.magnitude / blast);
            away = away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.forward;
            Strike(enemy, damage * share, enemy.transform.position + Vector3.up, away, 40f);
            if (enemy.IsAlive && enemy.TryGetComponent(out MeleeCombatTarget target) && target.IsAvailable)
            {
                if (target.Steadfast) target.Interrupt(.8f);
                else target.KnockDown(away, 1.5f);
            }
        }
    }

    // ---- Assault: the phantom cavalry ----

    /// <summary>
    /// This PC's Assault sounded a CHARGE at <paramref name="at"/> (ClassKit): with the final card, the riders go out
    /// along where they look.
    /// </summary>
    public void Charged(Vector3 at)
    {
        if (!PlayerClass.Is(PlayerClass.Assault) || !PlayerPerks.Has(PlayerPerks.AssaultCavalry)) return;
        Cavalry(at);
    }

    private void Cavalry(Vector3 at)
    {
        var view = Camera.main;
        Vector3 way = Vector3.ProjectOnPlane(view != null ? view.transform.forward : transform.forward, Vector3.up);
        if (way.sqrMagnitude < 1e-4f) way = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (way.sqrMagnitude < 1e-4f) way = Vector3.forward;
        rideWay = way.normalized;
        rideFrom = at;
        rideSince = Time.time;
        riding = true;
        ridden.Clear();
        CardNet.Show(CardFx.Kind.Cavalry, at, rideWay, Mathf.Max(1f, PlayerClass.Ability(AssaultRow, "cavalryReach", 24f)));
    }

    // The riders' front sweeps on; whatever it reaches in its lane is hit once and thrown aside.
    private void Ride()
    {
        if (!riding) return;
        float reach = Mathf.Max(1f, PlayerClass.Ability(AssaultRow, "cavalryReach", 24f));
        float half = Mathf.Max(.5f, PlayerClass.Ability(AssaultRow, "cavalryWidth", 4.5f)) * .5f;
        float front = Mathf.Max(0f, Time.time - rideSince - CardFx.CavalryIn) * CardFx.CavalrySpeed;
        if (front >= reach + 1f) riding = false;
        Vector3 aside = Vector3.Cross(Vector3.up, rideWay);
        float damage = Mathf.Max(0f, PlayerPerks.Total(PlayerPerks.AssaultCavalry));
        if (damage <= 0f) damage = Mathf.Max(0f, PlayerClass.Ability(AssaultRow, "cavalryDamage", 80f));
        float down = Mathf.Max(.1f, PlayerClass.Ability(AssaultRow, "cavalryKnockdown", 2f));
        near.Clear();
        foreach (var enemy in EnemyHealth.Living)
        {
            if (enemy == null || !enemy.IsAlive || ridden.Contains(enemy)) continue;
            Vector3 off = enemy.transform.position - rideFrom;
            float along = Vector3.Dot(off, rideWay), lateral = Vector3.Dot(off, aside);
            if (along < -.5f || along > Mathf.Min(front, reach) + .5f || Mathf.Abs(lateral) > half || Mathf.Abs(off.y) > 3f) continue;
            near.Add(enemy);
        }
        foreach (var enemy in near)
        {
            ridden.Add(enemy);
            float lateral = Vector3.Dot(enemy.transform.position - rideFrom, aside);
            Vector3 thrown = rideWay * 7f + aside * (lateral >= 0f ? 4f : -4f);
            Strike(enemy, damage, enemy.transform.position + Vector3.up, rideWay, 60f);
            if (!enemy.IsAlive || !enemy.TryGetComponent(out MeleeCombatTarget target) || !target.IsAvailable) continue;
            if (target.Steadfast) target.Interrupt(1f);
            else target.Barge(thrown, .35f, down);
        }
    }

    // A card's own blow on an enemy, this player's (its kill and its money are theirs, as a punch's are).
    private void Strike(EnemyHealth enemy, float amount, Vector3 point, Vector3 direction, float posture)
    {
        if (amount <= 0f || enemy == null || !enemy.IsAlive) return;
        enemy.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), amount, DamageKind.Attack, point, direction, gameObject,
            false, posture, true));
    }

    // ---- Ghost: White Tiger ----

    // The stacks are safe a while; the claws, and the stagger round the kill, at most once in a while.
    private void Tiger()
    {
        tigerTotal = Mathf.Max(.1f, PlayerPerks.Total(PlayerPerks.GhostTiger));
        tigerUntil = Time.time + tigerTotal;
        if (Time.time < tigerAgain) return;
        tigerAgain = Time.time + Mathf.Max(0f, PlayerClass.Ability(GhostRow, "tigerRest", 1.5f));
        CardNet.Show(CardFx.Kind.Claws, tigerAt, tigerAt - tigerFrom, 1f);
        KitSound.Play(KitSound.Cue.Hunter, 1f);
        float reach = Mathf.Max(0f, PlayerClass.Ability(GhostRow, "tigerReach", 6f)), seconds = PlayerClass.Ability(GhostRow, "tigerStagger", 1.2f);
        if (reach <= 0f || seconds <= 0f) return;
        foreach (var enemy in EnemyHealth.Living)
        {
            if (enemy == null || !enemy.IsAlive || (enemy.transform.position - tigerAt).sqrMagnitude > reach * reach) continue;
            if (enemy.TryGetComponent(out MeleeCombatTarget target) && target.IsAvailable) target.Interrupt(seconds);
        }
    }
}
