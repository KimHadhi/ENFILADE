using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A Point's cards that watch its own shots and kills (Docs/Features/ClassCards.md; the user's card feedback of
/// 2026-11-14), at work for this PC's own player (ClassKit adds it). Each is judged here, where the shots are. What other
/// PCs must see goes the ways that already carry it: a zombie knocked down or staggered through its own sync (on a copy,
/// MeleeCombatTarget tells the host, which applies it as sent), and the Gun Kata's aura held on longer through CardNet
/// (ClassKit.LengthenKata). A miss changes nothing (the user's rule: a hit is a plus, a miss is never a minus).
/// <list type="bullet">
/// <item>곡예 사격 (point_slide): in a slide, a jump or a fall, the spread that moving and leaving the ground add to its
/// shots is smaller (PlayerClass.MovingSpreadFactor and AirSpreadFactor ask AcrobatSpread). The draft's damage bonus
/// went at the user's word, because it would force a slide to get it ("데미지 버프를 받기 위해 굳이 슬라이딩하면서 쏴야
/// 된다고 강제 되니까 … 스프레드 디버프를 안 받는 카드로 바꿔").</item>
/// <item>문전박대 (point_door): a shotgun's pellets that hit within reach knock a common zombie off its feet and stagger an
/// elite (each elite once in a while), and a brute stands, as it does against every knockdown (the user: "샷건에 버프 주는
/// 느낌은 좋았어").</item>
/// <item>탄피 회수 (point_brass): a kill by its gun puts rounds back into the gun in hand, and only a headshot kill at the
/// first level (the user: "1단계는 헤드샷 처치 시 1발 회복, 2단계는 몸샷 처치로도 가능하게 바꾸고, 3단계는 2발로").</item>
/// <item>탄막 춤 (point_ballet, its final card): every kill in a Gun Kata keeps it going longer, up to a limit, and the
/// kata's shots fly with no spread at all, on the run too (PlayerClass.KataSpreadFactor asks BalletSpread). The kata
/// alone only keeps running from adding spread; the hip's own spread and the bloom of a long burst stay.</item>
/// </list>
/// Numbers: Tables/Perks (each card's value) and Tables/ClassAbilities, Point.door*, Point.brass*, Point.ballet*.
/// </summary>
[DisallowMultipleComponent]
public sealed class PointCards : MonoBehaviour
{
    public const string SlideCard = "point_slide", DoorCard = "point_door", BrassCard = "point_brass", BalletCard = "point_ballet";
    private const string Row = "Point";
    // Elites remembered as lately staggered: past this many, those free again are forgotten.
    private const int StaggerMemory = 32;

    private PlayerMovement movement;
    private ClassKit kit;
    private PlayerWeaponSlots slots;
    private WeaponController weapon;
    // 문전박대: what the shot under way has knocked or staggered already (its pellets knock one zombie once, and the host
    // hears of it once), and when each elite it staggered may be staggered again.
    private readonly HashSet<MeleeCombatTarget> struck = new HashSet<MeleeCombatTarget>();
    private readonly Dictionary<MeleeCombatTarget, float> staggerAgain = new Dictionary<MeleeCombatTarget, float>();
    private readonly List<MeleeCombatTarget> forget = new List<MeleeCombatTarget>();

    // ---- What the gun asks (PlayerClass) ----

    /// <summary>
    /// 곡예 사격: the factor on the spread that moving and leaving the ground add to <paramref name="who"/>'s shots. It is
    /// below 1 only while this PC's own Point holding the card slides, jumps or falls.
    /// </summary>
    public static float AcrobatSpread(PlayerMovement who)
    {
        if (!Mine(who) || !PlayerPerks.Has(SlideCard) || (who.IsGrounded && !who.IsSliding)) return 1f;
        return Mathf.Clamp01(1f - PlayerPerks.Total(SlideCard) / 100f);
    }

    /// <summary>
    /// 탄막 춤: the factor on all the spread of this PC's own Point's shots while its Gun Kata runs (ClassAbilities
    /// Point.balletSpread, 0: none at all), 1 without the card. PlayerClass asks only during a kata.
    /// </summary>
    public static float BalletSpread()
        => PlayerClass.Is(PlayerClass.Point) && PlayerPerks.Has(BalletCard) ? Mathf.Clamp01(PlayerClass.Ability(Row, "balletSpread", 0f)) : 1f;

    // This PC's own player, playing a Point.
    private static bool Mine(PlayerMovement who)
    {
        var local = ClassKit.Local;
        return who != null && local != null && local.Movement == who && PlayerClass.Is(PlayerClass.Point);
    }

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        kit = GetComponent<ClassKit>();
        slots = GetComponent<PlayerWeaponSlots>();
    }

    private void OnEnable() => CombatDamageEvents.Damaged += OnDamaged;

    private void OnDisable()
    {
        CombatDamageEvents.Damaged -= OnDamaged;
        if (weapon != null) weapon.ShotFired -= OnShot;
        weapon = null;
        struck.Clear();
    }

    // This PC's own player's gun, once it is there (it may be made after this).
    private void Update()
    {
        if (weapon != null || movement == null || !movement.IsLocal) return;
        weapon = GetComponent<WeaponController>();
        if (weapon != null) weapon.ShotFired += OnShot;
    }

    // ---- 문전박대 ----

    // Each pellet of this PC's own shot, once traced (its damage is already done): with a shotgun, what it hit within
    // reach is knocked down.
    private void OnShot(WeaponShot shot)
    {
        if (shot.Pellet == 0) struck.Clear();
        if (!PlayerClass.Is(PlayerClass.Point) || !PlayerPerks.Has(DoorCard)) return;
        var gun = weapon != null ? weapon.EquippedData : null;
        if (gun == null || gun.Pellets <= 1) return;
        float reach = PlayerPerks.Total(DoorCard);
        if (reach <= 0f) return;
        SlamDoor(shot.Origin, shot.Trace, reach);
        // A pellet that went on through one zombie into the next knocks that one too.
        if (shot.Later != null)
            foreach (var leg in shot.Later) SlamDoor(shot.Origin, leg.Trace, reach);
    }

    private void SlamDoor(Vector3 from, AttackTraceResult trace, float reach)
    {
        if (!trace.HasHit || trace.Damage != DamageResult.Damaged || (trace.Hit.point - from).sqrMagnitude > reach * reach) return;
        var foe = CombatFoe.Of(trace.Hit.collider);
        // Killed by the shot, it is no longer there to knock down.
        if (foe == null || !foe.TryGetComponent(out MeleeCombatTarget target) || !target.IsAvailable || !struck.Add(target)) return;
        var enemy = foe as EnemyHealth;
        // A brute stands firm against every knockdown (friend feedback C2), and this one is no different.
        if (target.Steadfast || (enemy != null && enemy.TryGetComponent(out ZombieType type) && type.Kind == ZombieKind.Brute)) return;
        if (enemy != null && PlayerClass.IsElite(enemy)) { Stagger(target); return; }
        Vector3 away = Vector3.ProjectOnPlane(trace.Hit.point - from, Vector3.up);
        if (away.sqrMagnitude < 1e-4f) away = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (away.sqrMagnitude < 1e-4f) away = Vector3.forward;
        target.KnockDown(away.normalized, Mathf.Max(.1f, PlayerClass.Ability(Row, "doorDown", 1.5f)));
    }

    // An elite reels a moment, a stun that cuts off its blow or its charge (on a copy the stun itself tells the host),
    // unless this Point staggered it lately: a pump's every shell must not hold an elite for good.
    private void Stagger(MeleeCombatTarget target)
    {
        if (staggerAgain.TryGetValue(target, out float again) && Time.time < again) return;
        float seconds = PlayerClass.Ability(Row, "doorStagger", 1f);
        if (seconds <= 0f) return;
        target.Interrupt(seconds);
        if (staggerAgain.Count >= StaggerMemory) Forget();
        staggerAgain[target] = Time.time + seconds + Mathf.Max(0f, PlayerClass.Ability(Row, "doorRest", 3f));
    }

    private void Forget()
    {
        forget.Clear();
        foreach (var pair in staggerAgain) if (pair.Key == null || Time.time >= pair.Value) forget.Add(pair.Key);
        foreach (var key in forget) staggerAgain.Remove(key);
    }

    // ---- 탄피 회수 and 탄막 춤 ----

    // Any foe's kill by this player: a zombie's, or a practice target's on the hub's range, where the cards are tried out.
    private void OnDamaged(CombatDamageReport report)
    {
        if (movement == null || !movement.IsLocal || !report.IsKillingBlow || !(report.Target is ICombatFoe)) return;
        if (!PlayerClass.Is(PlayerClass.Point) || !PlayerPerks.IsMine(report.Hit.Source)) return;
        if (PlayerPerks.Has(BrassCard)) RecoverBrass(report.Hit);
        if (PlayerPerks.Has(BalletCard) && kit != null)
            kit.LengthenKata(PlayerPerks.Total(BalletCard), Mathf.Max(0f, PlayerClass.Ability(Row, "balletMost", 6f)));
    }

    // A kill by its gun puts the card's rounds back into the gun in hand, up to its magazine's own size, and the reserve
    // pays nothing for them. Below the card's level for body kills (Point.brassBodyFrom), only a headshot kill does.
    private void RecoverBrass(DamageHit hit)
    {
        if (hit.Method != DamageMethod.Bullet || slots == null) return;
        if (!hit.IsCritical && PlayerPerks.Level(BrassCard) < PlayerClass.Ability(Row, "brassBodyFrom", 2f)) return;
        // One of its two guns in hand (slot 1 or 2), not a knife or a throwable.
        int slot = slots.SelectedSlot;
        var gun = slot >= 0 && slot < 2 && slots.GetWeapon(slot) != null ? slots.GetRuntime(slot) : null;
        if (gun == null) return;
        int rounds = Mathf.RoundToInt(PlayerPerks.Total(BrassCard)), loaded = 0;
        while (loaded < rounds && gun.LoadRound()) loaded++;
        if (loaded > 0) slots.NotifyAmmo();
    }
}
