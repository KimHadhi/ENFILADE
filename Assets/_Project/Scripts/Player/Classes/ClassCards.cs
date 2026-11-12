using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

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
/// <item>Knight, 비껴 흘리기 (the user's name, checked against the dictionary: Docs/ClassCards.md): a parried blow slides
/// off sideways, the zombie stumbles and loses more posture, a white arc running off the parry.</item>
/// <item>Knight, 되치기 (Dying Light's grab counter, at the user's word): grabbed, a ring closes on a mark round the
/// crosshair; the shove key as it meets it throws the zombie, its posture broken for the execution, with REVERSAL slammed
/// across the screen. Early or late is no worse than without the card: the shove only tears it off as ever.</item>
/// <item>Knight, 폭렬 건틀렛 (the user's gauntlet that bursts a flash and a shotgun): a charged punch or a counter that
/// lands sets off the gauntlet, by turns a flash that stuns what is in front and a shot that blows it back.</item>
/// <item>Knight, 그림자 분신 (its final card; the user's Ryoshu of Limbus Company): a perfect parry or dodge brings out a
/// shadow that strikes every punch and counter again a beat later, a violet crescent cutting where it lands.</item>
/// </list>
/// The HUD's bar (ClassHUD) shows the card state under way: BarShare, BarWord, BarColor.
/// </summary>
[DisallowMultipleComponent]
public sealed class ClassCards : MonoBehaviour
{
    private const string GhostRow = PlayerClass.GhostRow, PointRow = "Point", AssaultRow = "Assault", KnightRow = PlayerClass.KnightRow;
    private static readonly Color Violet = new Color(.62f, .32f, 1f);

    // A shadow double's blow waiting to land: on whom, how hard, where and which way.
    private struct Echo
    {
        public float Due, Amount, Posture;
        public EnemyHealth Target;
        public Vector3 Point, Direction;
    }

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
    // Knight: the health whose parries, dodges and grabs are watched, and the fists whose punches are.
    private PlayerDamageReceiver receiver;
    private PlayerMeleeCombat fists;
    // The gauntlet: the last punch's charge and when it went; the blow the last burst went off for; which burst is next.
    private float punchCharge, punchAt = -99f;
    private long burstFor = -1;
    private bool flashNext = true;
    // The shadow double: until when and how long; when its coming out was last shown; its blows waiting to land.
    private float shadowUntil = -99f, shadowTotal = 1f, shadowShown = -99f;
    private readonly List<Echo> echoes = new List<Echo>();
    // The reversal: when a grab took hold (below 0: none waiting), whose; whether its one press is spent; the ring.
    private float grabbedAt = -1f;
    private EnemyHealth grabber;
    private bool reversalSpent;
    private GameObject ringHost;
    private RawImage ringClosing, ringMark;

    /// <summary>A Ghost's White Tiger holds: hurt takes no Head Hunter stack (ClassKit).</summary>
    public bool TigerGuards => tigerUntil > Time.time && PlayerClass.Is(PlayerClass.Ghost);
    public float TigerShare => TigerGuards ? Mathf.Clamp01((tigerUntil - Time.time) / tigerTotal) : 0f;

    /// <summary>A Knight's shadow double is out: its punches and counters strike twice.</summary>
    public bool ShadowOut => shadowUntil > Time.time && PlayerClass.Is(PlayerClass.Knight);
    public float ShadowShare => ShadowOut ? Mathf.Clamp01((shadowUntil - Time.time) / shadowTotal) : 0f;

    /// <summary>The card state the class HUD's bar shows now, 0 to 1 (0: none), its word and its colour.</summary>
    public float BarShare
    {
        get
        {
            if (ShadowShare > 0f) return ShadowShare;
            if (TigerShare > 0f) return TigerShare;
            return kit != null ? kit.GunslingerShare : 0f;
        }
    }

    public string BarWord => ShadowShare > 0f ? Texts.Get("class.hud.shadow")
        : TigerShare > 0f ? Texts.Get("class.hud.tiger") : Texts.Get("class.hud.gunslinger");
    public Color BarColor => ShadowShare > 0f ? Violet : TigerShare > 0f ? KitStyle.Ice : KitStyle.Accent;

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        kit = GetComponent<ClassKit>();
    }

    private void OnEnable() => CombatDamageEvents.Damaged += OnDamaged;

    private void OnDisable()
    {
        CombatDamageEvents.Damaged -= OnDamaged;
        if (receiver != null) receiver.HitResolved -= OnResolved;
        if (fists != null) fists.Punched -= OnPunched;
        receiver = null;
        fists = null;
        if (ringHost != null) ringHost.SetActive(false);
        if (Local == this)
        {
            Local = null;
            CardNet.Stop();
        }
    }

    private void OnDestroy()
    {
        if (ringHost != null) Destroy(ringHost);
    }

    // This player's health and fists, once they are there (both may come after this).
    private void Wire()
    {
        if (receiver == null && movement.DamageReceiver != null)
        {
            receiver = movement.DamageReceiver;
            receiver.HitResolved += OnResolved;
        }
        if (fists == null && movement.MeleeCombat != null)
        {
            fists = movement.MeleeCombat;
            fists.Punched += OnPunched;
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
        Wire();
        BrokenArrow();
        Ride();
        Reversal();
        Echoes();
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
        // A Knight's own punch or counter (the cards' own blows can't be guarded, so they never count here).
        var hit = report.Hit;
        if (hit.Kind == DamageKind.Attack && hit.Method == DamageMethod.Other && (hit.IsCounter || hit.CanBeGuarded)
            && report.Target is EnemyHealth struck && PlayerClass.Is(PlayerClass.Knight))
        {
            if (ShadowOut) Shadowed(struck, hit);
            if (PlayerPerks.Has(PlayerPerks.KnightGauntlet) && hit.Id != burstFor
                && (hit.IsCounter || (Time.time - punchAt < .5f && punchCharge >= PlayerClass.Ability(KnightRow, "gauntletCharge", .6f))))
            {
                burstFor = hit.Id;
                Gauntlet(hit);
            }
        }
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
        var body = movement.DamageReceiver;
        bool able = body != null && body.IsAlive && !movement.IsDead;
        bool high = able && body.MaximumHealth > 0f
            && body.Health / body.MaximumHealth * 100f > PlayerClass.Ability(AssaultRow, "brokenHealth", 30f);
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

    // ---- Knight ----

    private void OnPunched(float charge)
    {
        punchCharge = charge;
        punchAt = Time.time;
    }

    // A parry or a perfect dodge (a Knight's), and a grab taking hold.
    private void OnResolved(DamageHit hit, DamageResult result)
    {
        // A parried blow fills the gauge whoever parried it (UltimateCharge.AwardHelp): a Knight's job above all.
        if (result == DamageResult.Parried) UltimateCharge.AwardHelp(UltimateCharge.Help.Parried);
        if (!PlayerClass.Is(PlayerClass.Knight)) return;
        if (result == DamageResult.Parried)
        {
            Deflect(hit);
            ShadowFor(PlayerPerks.Has(PlayerPerks.KnightShadow) ? PlayerPerks.Total(PlayerPerks.KnightShadow) : 0f);
        }
        else if (result == DamageResult.Evaded) ShadowFor(PlayerPerks.Has(PlayerPerks.KnightShadow) ? PlayerPerks.Total(PlayerPerks.KnightShadow) : 0f);
        else if (result == DamageResult.Damaged && hit.Method == DamageMethod.Grab && PlayerPerks.Has(PlayerPerks.KnightReversal))
        {
            grabber = hit.Source != null ? hit.Source.GetComponentInParent<EnemyHealth>() : null;
            if (grabber == null) return;
            grabbedAt = Time.time;
            reversalSpent = false;
        }
    }

    // 비껴 흘리기: the parried zombie stumbles past, its posture worn down more; a white arc runs off sideways.
    private void Deflect(DamageHit hit)
    {
        if (!PlayerPerks.Has(PlayerPerks.KnightDeflect)) return;
        var attacker = hit.Source != null ? hit.Source.GetComponentInParent<MeleeCombatTarget>() : null;
        if (attacker == null || !attacker.IsAvailable) return;
        attacker.ApplyPostureDamage(PlayerPerks.Total(PlayerPerks.KnightDeflect));
        if (!attacker.IsStaggered) attacker.Interrupt(Mathf.Max(0f, PlayerClass.Ability(KnightRow, "deflectStagger", 1f)));
        Vector3 to = Vector3.ProjectOnPlane(attacker.transform.position - transform.position, Vector3.up);
        to = to.sqrMagnitude > 1e-4f ? to.normalized : transform.forward;
        Vector3 side = Vector3.Cross(Vector3.up, to) * (Random.value < .5f ? 1f : -1f);
        CardNet.Show(CardFx.Kind.Deflect, transform.position + Vector3.up * 1.35f + to * .75f, side, 0f);
    }

    // 되치기: grabbed, a ring closes on its mark; the shove key as they meet throws the zombie, its posture broken.
    private void Reversal()
    {
        if (grabbedAt < 0f) { Ring(false, 0f, false); return; }
        float from = Mathf.Max(.1f, PlayerClass.Ability(KnightRow, "reversalFrom", .45f));
        float half = Mathf.Max(.02f, PlayerClass.Ability(KnightRow, "reversalWindow", .4f)) * .5f;
        float since = Time.time - grabbedAt;
        bool inside = Mathf.Abs(since - from) <= half;
        // One press decides: on the mark it throws; off it, the shove only does what a shove does.
        bool pressed = movement.Input != null && movement.InputAvailable && movement.Input.ShoveKeyPressed;
        if (pressed && !reversalSpent)
        {
            reversalSpent = true;
            if (inside) Reverse();
            grabbedAt = -1f;
            Ring(false, 0f, false);
            return;
        }
        bool held = movement.IsHeld && !movement.IsPinned;
        if (since > from + half + .1f || (!held && since > .15f) || grabber == null || !grabber.IsAlive)
        {
            grabbedAt = -1f;
            Ring(false, 0f, false);
            return;
        }
        Ring(true, since / from, inside);
    }

    private void Reverse()
    {
        if (grabber == null || !grabber.IsAlive) return;
        Vector3 away = Vector3.ProjectOnPlane(grabber.transform.position - transform.position, Vector3.up);
        away = away.sqrMagnitude > 1e-4f ? away.normalized : transform.forward;
        // Thrown and broken: it reels with its posture gone (the execution is there for the taking), and its grip goes
        // with it (PlayerHold lets a stunned holder go).
        if (grabber.TryGetComponent(out MeleeCombatTarget target) && target.IsAvailable && !target.IsStaggered)
            target.ApplyPostureDamage(target.MaximumPosture * 4f + 1f);
        Vector3 contact = transform.position + Vector3.up * 1.25f + away * .6f;
        GetComponent<ParryFeedback>()?.CounterBurst(contact, (away * .35f + Vector3.up * .5f).normalized, 1.2f);
        if (grabber.TryGetComponent(out EnemyNetwork network)) network.ShareParry(contact, away, true);
        if (!TryGetComponent(out CounterBanner banner)) banner = gameObject.AddComponent<CounterBanner>();
        banner.Play(.95f, Mathf.Min(2, CounterBanner.Grades - 1), "REVERSAL", KitStyle.Accent);
        GetComponent<PlayerFistAudio>()?.PlayHeavyHit();
        GetComponentInChildren<PlayerCameraEffects>()?.ApplyCameraImpulse(new Vector3(-5f, 0f, 6f), -6f);
    }

    // The reversal's ring round the crosshair: white closing on the faint mark, both pink while the key would throw.
    private void Ring(bool show, float closing, bool inside)
    {
        if (!show)
        {
            if (ringHost != null && ringHost.activeSelf) ringHost.SetActive(false);
            return;
        }
        if (ringHost == null) BuildRing();
        if (!ringHost.activeSelf) ringHost.SetActive(true);
        float size = Mathf.Lerp(320f, 96f, Mathf.Clamp01(closing));
        ringClosing.rectTransform.sizeDelta = new Vector2(size, size);
        ringClosing.color = inside ? KitStyle.Accent : KitStyle.With(KitStyle.Ice, .9f);
        ringMark.color = inside ? KitStyle.Accent : KitStyle.With(KitStyle.Ice, .35f);
        float beat = inside ? 1.08f : 1f;
        ringMark.rectTransform.localScale = new Vector3(beat, beat, 1f);
    }

    private void BuildRing()
    {
        ringHost = new GameObject("Reversal Ring", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        ringHost.hideFlags = HideFlags.DontSave;
        var canvas = ringHost.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 79;
        var scaler = ringHost.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;
        ringMark = RingImage("Mark", 96f);
        ringClosing = RingImage("Closing", 320f);
        var label = FeelUI.Rect("Key", ringHost.transform);
        label.anchoredPosition = new Vector2(0f, -86f);
        label.sizeDelta = new Vector2(300f, 30f);
        KitStyle.Flat(label, Texts.Get("class.knight.reversal"), 20f, TextAlignmentOptions.Center, KitStyle.Ice);
    }

    private RawImage RingImage(string name, float size)
    {
        var rect = FeelUI.Rect(name, ringHost.transform);
        rect.sizeDelta = new Vector2(size, size);
        var image = rect.gameObject.AddComponent<RawImage>();
        image.texture = KitShapes.Ring;
        image.raycastTarget = false;
        return image;
    }

    // 폭렬 건틀렛: by turns, a flash that stuns what is in front, and a shot that blows it back and hurts it.
    private void Gauntlet(DamageHit hit)
    {
        Vector3 way = Vector3.ProjectOnPlane(hit.Direction, Vector3.up);
        way = way.sqrMagnitude > 1e-4f ? way.normalized : Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
        float reach = Mathf.Max(1f, PlayerClass.Ability(KnightRow, "gauntletReach", 5f));
        float cone = Mathf.Cos(Mathf.Clamp(PlayerClass.Ability(KnightRow, "gauntletAngle", 70f), 1f, 360f) * .5f * Mathf.Deg2Rad);
        near.Clear();
        foreach (var enemy in EnemyHealth.Living)
        {
            if (enemy == null || !enemy.IsAlive) continue;
            Vector3 to = Vector3.ProjectOnPlane(enemy.transform.position - transform.position, Vector3.up);
            float far = to.magnitude;
            if (far > reach || (far > 1.2f && Vector3.Dot(to / far, way) < cone)) continue;
            near.Add(enemy);
        }
        bool flash = flashNext;
        flashNext = !flashNext;
        if (flash)
        {
            float stun = Mathf.Max(0f, PlayerClass.Ability(KnightRow, "gauntletStun", 1.5f));
            foreach (var enemy in near)
                if (enemy.TryGetComponent(out MeleeCombatTarget target) && target.IsAvailable) target.Interrupt(stun);
            CardNet.Show(CardFx.Kind.Flash, hit.Point, way, 2.2f);
            return;
        }
        float damage = Mathf.Max(0f, PlayerPerks.Total(PlayerPerks.KnightGauntlet));
        float posture = Mathf.Max(0f, PlayerClass.Ability(KnightRow, "gauntletPosture", 30f));
        foreach (var enemy in near)
        {
            float far = Vector3.ProjectOnPlane(enemy.transform.position - transform.position, Vector3.up).magnitude;
            Strike(enemy, damage * (1f - .5f * Mathf.Clamp01(far / reach)), enemy.transform.position + Vector3.up * 1.2f, way, posture);
            if (!enemy.IsAlive || !enemy.TryGetComponent(out MeleeCombatTarget target) || !target.IsAvailable) continue;
            if (posture > 0f) target.ApplyPostureDamage(posture);
            if (target.Steadfast) target.Interrupt(.5f);
            else if (!target.IsStaggered) target.Barge(way * 6f, .25f, .9f);
        }
        CardNet.Show(CardFx.Kind.Blast, hit.Point, way, reach);
    }

    /// <summary>
    /// The shadow double comes out (or stays out) for <paramref name="seconds"/> (the final card's, or the ultimate's
    /// when every card is taken): violet wisps leap up round the Knight, at most once a second.
    /// </summary>
    public void ShadowFor(float seconds)
    {
        if (seconds <= 0f) return;
        if (!ShadowOut || shadowUntil < Time.time + seconds)
        {
            shadowTotal = seconds;
            shadowUntil = Time.time + seconds;
        }
        if (Time.time - shadowShown < 1f) return;
        shadowShown = Time.time;
        CardNet.Show(CardFx.Kind.Shadow, transform.position, transform.forward, 0f);
    }

    // A punch or counter landed while the shadow is out: the shadow's own blow follows a beat later.
    private void Shadowed(EnemyHealth target, DamageHit hit)
    {
        echoes.Add(new Echo
        {
            Due = Time.time + Mathf.Max(.02f, PlayerClass.Ability(KnightRow, "shadowDelay", .14f)), Target = target,
            Amount = hit.Amount * Mathf.Max(0f, PlayerClass.Ability(KnightRow, "shadowShare", 1f)), Posture = hit.PostureDamage,
            Point = hit.Point, Direction = hit.Direction
        });
    }

    // The shadow's blows land when they are due: the cut shows wherever it lands, the blow hurts what still stands.
    private void Echoes()
    {
        for (int i = echoes.Count - 1; i >= 0; i--)
        {
            var echo = echoes[i];
            if (Time.time < echo.Due) continue;
            echoes.RemoveAt(i);
            CardNet.Show(CardFx.Kind.Echo, echo.Point, echo.Direction, 0f);
            if (echo.Target == null || !echo.Target.IsAlive) continue;
            Strike(echo.Target, echo.Amount, echo.Point, echo.Direction, echo.Posture);
            if (echo.Posture > 0f && echo.Target.IsAlive && echo.Target.TryGetComponent(out MeleeCombatTarget target))
                target.ApplyPostureDamage(echo.Posture);
        }
    }

    // ---- The ultimate ----

    /// <summary>Whether this player's class has an ultimate for the full gauge once every card is taken (all seven do).</summary>
    public bool HasUltimate => PlayerClass.Current != null;

    /// <summary>
    /// Every card this player can be dealt is taken and the full gauge is spent on X (PerkCardScreen; classes-design's
    /// open question, "what a full gauge does once the cards are all taken"): the class's ultimate at once, ULTIMATE
    /// slammed across the screen. Knight: the shadow double; Assault: adrenaline full and the CHARGE, its phantom riders
    /// with it; Ghost: the Head Hunter full under the White Tiger's guard; Point: both guns loaded and the Gunslinger's
    /// moment; Détente: the syringe full and one more Resurrection; Schießhund: the Desperado's cylinder full; Support:
    /// the bag full. Numbers: ClassAbilities Cards.ultimateSeconds.
    /// </summary>
    public void Ultimate()
    {
        float seconds = Mathf.Max(1f, PlayerClass.Ability("Cards", "ultimateSeconds", 10f));
        if (PlayerClass.Is(PlayerClass.Knight)) ShadowFor(seconds);
        else if (PlayerClass.Is(PlayerClass.Assault))
        {
            kit?.UltimateCharge();
            if (!PlayerPerks.Has(PlayerPerks.AssaultCavalry)) Cavalry(transform.position);
        }
        else if (PlayerClass.Is(PlayerClass.Ghost))
        {
            kit?.FillHunter();
            tigerTotal = seconds;
            tigerUntil = Time.time + seconds;
        }
        else if (PlayerClass.Is(PlayerClass.Point)) kit?.ReloadAll();
        else if (PlayerClass.Is(PlayerClass.Detente)) DetenteKit.Local?.Ultimate();
        else if (PlayerClass.Is(PlayerClass.Schiesshund)) kit?.LoadDesperado();
        else if (PlayerClass.Is(PlayerClass.Support)) SupportKit.Local?.Restock();
        if (!TryGetComponent(out CounterBanner banner)) banner = gameObject.AddComponent<CounterBanner>();
        banner.Play(1.05f, CounterBanner.Grades - 1, "ULTIMATE", KitStyle.Accent);
        KitSound.Play(KitSound.Cue.Charge, 1f);
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
