using System.Collections.Generic;
using UnityEngine;

// ClassCards: Knight. One part of the class; the rest is in ClassCards.cs and the files beside it.
public sealed partial class ClassCards
{
    // 비껴 흘리기: until when a landed counter keeps this Knight's parry window longer, and how long that lasts.
    private float flowUntil = -99f, flowTotal = 1f;
    // 원호 반격: until when this Knight's counters and fists hit harder after its cover turned a blow, and how long that lasts.
    private float strikeUntil = -99f, strikeTotal = 1f;
    // The zombies V throws off this Knight at once (found as it is pressed).
    private readonly List<EnemyHealth> thrown = new List<EnemyHealth>();

    /// <summary>
    /// A Knight's 비껴 흘리기 under way (a counter of theirs landed a moment ago): the factor on their parry windows now
    /// (PlayerClass.ParryWindowFactor), 1 when none runs.
    /// </summary>
    public float FlowFactor => FlowShare > 0f ? 1f + Mathf.Max(0f, PlayerPerks.Total(PlayerPerks.KnightFlow)) / 100f : 1f;
    public float FlowShare => flowUntil > Time.time && PlayerClass.Is(PlayerClass.Knight) ? Mathf.Clamp01((flowUntil - Time.time) / flowTotal) : 0f;

    /// <summary>
    /// A Knight's 원호 반격 under way (their cover turned a blow a moment ago): the factor on their counters and fists now
    /// (PlayerClass.CounterFactor, FistFactor), 1 when none runs.
    /// </summary>
    public float CoverStrikeFactor => CoverStrikeShare > 0f ? 1f + Mathf.Max(0f, PlayerPerks.Total(PlayerPerks.KnightCoverstrike)) / 100f : 1f;
    public float CoverStrikeShare => strikeUntil > Time.time && PlayerClass.Is(PlayerClass.Knight) ? Mathf.Clamp01((strikeUntil - Time.time) / strikeTotal) : 0f;

    /// <summary>
    /// Whether this PC's player throws off whatever holds them with V (the Knight's grab escape, Knight.grabThrow), for
    /// the hold's prompt (HoldFeedback).
    /// </summary>
    public static bool ThrowsOffHolds => PlayerClass.Is(PlayerClass.Knight) && PlayerClass.Ability(KnightRow, "grabThrow", 1f) > .5f;

    // The Knight's cards the class HUD's bar shows (BarShare): the shadow double out first, then the cover strike's moment,
    // then the flow's.
    private float KnightBarShare => ShadowShare > 0f ? ShadowShare : CoverStrikeShare > 0f ? CoverStrikeShare : FlowShare;

    private string KnightBarWord => ShadowShare > 0f ? Texts.Get("class.hud.shadow")
        : CoverStrikeShare > 0f ? Texts.Format("class.hud.coverstrike", Mathf.RoundToInt(PlayerPerks.Total(PlayerPerks.KnightCoverstrike)))
        : Texts.Format("class.hud.flow", Mathf.RoundToInt(PlayerPerks.Total(PlayerPerks.KnightFlow)));

    private Color KnightBarColor => ShadowShare > 0f ? Violet : CoverStrikeShare > 0f ? KitStyle.Accent : KitStyle.Ice;

    // A parry (a Knight's). The shadow double no longer comes out of one: the card that did went with the final cards on
    // 2026-11-14 (the user: "이미 궁극기를 쓸 수 있게 된다는 특권을 얻는 거에서 궁극 카드가 의미 없잖아"), and the double
    // is the Knight's ultimate alone (ClassCards.Ultimate).
    private void OnResolved(DamageHit hit, DamageResult result)
    {
        // A parried blow fills the gauge whoever parried it (UltimateCharge.AwardHelp): a Knight's job above all.
        // (A blow a Knight's cover turned on this player is the Knight's parry: its charge goes to them, KnightCover.Covered.)
        bool covered = hit.Id == KnightCover.CoveredHit;
        if (result == DamageResult.Parried && !covered) UltimateCharge.AwardHelp(UltimateCharge.Help.Parried);
        if (!PlayerClass.Is(PlayerClass.Knight)) return;
        // A friend's cover that turned a blow on this Knight was that friend's parry, not this Knight's.
        if (result == DamageResult.Parried && !covered) Deflect(hit);
    }

    // 무너뜨리기: a parried elite or brute reels a while, and a white arc runs off the parry. Its greater loss of posture is
    // the parry's own (PlayerClass.ParryPosture), taken before the parry showed; a pack zombie's broke on the parry already.
    private void Deflect(DamageHit hit)
    {
        if (!PlayerPerks.Has(PlayerPerks.KnightDeflect)) return;
        var attacker = hit.Source != null ? hit.Source.GetComponentInParent<MeleeCombatTarget>() : null;
        if (attacker == null || !attacker.IsAvailable || PlayerClass.BreaksOnParry(attacker)) return;
        if (!attacker.IsStaggered) attacker.Interrupt(Mathf.Max(0f, PlayerClass.Ability(KnightRow, "deflectStagger", 1f)));
        Vector3 to = Vector3.ProjectOnPlane(attacker.transform.position - transform.position, Vector3.up);
        to = to.sqrMagnitude > 1e-4f ? to.normalized : transform.forward;
        Vector3 side = Vector3.Cross(Vector3.up, to) * (Random.value < .5f ? 1f : -1f);
        CardNet.Show(CardFx.Kind.Deflect, transform.position + Vector3.up * 1.35f + to * .75f, side, 0f);
    }

    // This Knight's own blow landed on an enemy (OnDamaged): a punch, a slash of its knife, or a counter. The cards' own
    // blows can't be guarded and are no counter, so the shadow's never come back here to strike again. With the shadow out
    // its blow follows a beat later; a counter keeps the parry window long a while with 비껴 흘리기.
    private void KnightStruck(EnemyHealth struck, DamageHit hit)
    {
        if (struck == null || !PlayerClass.Is(PlayerClass.Knight) || hit.Kind != DamageKind.Attack) return;
        if (hit.Method != DamageMethod.Other && hit.Method != DamageMethod.Blade) return;
        if (!hit.IsCounter && !hit.CanBeGuarded) return;
        if (ShadowOut) Shadowed(struck, hit);
        if (!hit.IsCounter || !PlayerPerks.Has(PlayerPerks.KnightFlow)) return;
        flowTotal = Mathf.Max(.1f, PlayerClass.Ability(KnightRow, "flowSeconds", 3f));
        flowUntil = Time.time + flowTotal;
    }

    /// <summary>
    /// This Knight's cover turned a blow on a friend (KnightCover.Covered, on the Knight's own PC): with 원호 반격 its
    /// counters and fists hit harder for a few seconds. The friend's guns are their own PC's to strengthen, by the share
    /// the Knight's PC sent with the cover (KnightCover.Covers).
    /// </summary>
    internal void CoverStruck()
    {
        if (!PlayerClass.Is(PlayerClass.Knight) || !PlayerPerks.Has(PlayerPerks.KnightCoverstrike)) return;
        strikeTotal = Mathf.Max(.1f, PlayerClass.Ability(KnightRow, "coverStrikeSeconds", 3f));
        strikeUntil = Time.time + strikeTotal;
    }

    // Every frame on this PC's own player: V against a hold, and the shadow's blows as they come due.
    private void KnightTick()
    {
        BreakGrip();
        Echoes();
    }

    // ---- The grab escape (되치기, the Knight's own since 2026-11-14) ----

    // Held, V throws everything that holds this Knight off at once, with no ring to time, each one's posture broken so the
    // execution is there at once (the user, 2026-11-14: "잡혔을 때 빠져 나오는 기능은 카드가 아니라 기본으로"; what the
    // reversal card did on its timing ring): a shambler's grab, a candyman's hook, a zombie kneeling to eat them. The
    // holders are the zombies whose grip this PC gave them (ZombieGrip.Held: the held player's PC decides the hold), read
    // as the key goes down. A broken holder is stunned, so the hold lets it go (PlayerHold), and the host hears both as
    // results (MeleeCombatTarget's posture, PlayerHold's hold). Not pinned: a runner's pin is the kick's
    // (PlayerHold.BreakFreeEvery, Knight.breakFree). Not down: there a zombie eating them is the friends' to pull off.
    private void BreakGrip()
    {
        if (!ThrowsOffHolds || !movement.IsHeld || movement.IsPinned || movement.IsDowned || movement.IsDead) return;
        if (movement.Input == null || !movement.InputAvailable || !movement.Input.ShoveKeyPressed) return;
        thrown.Clear();
        foreach (var enemy in EnemyHealth.Living)
            if (enemy != null && enemy.IsAlive && enemy.TryGetComponent(out ZombieGrip grip) && grip.Held == movement) thrown.Add(enemy);
        if (thrown.Count == 0) return;
        foreach (var enemy in thrown) Throw(enemy);
        thrown.Clear();
        // The throw for everyone to see is each holder's; its tag, its weight and its knock are this screen's, once.
        MomentHUD.Reversal();
        GetComponent<PlayerFistAudio>().OrNull()?.PlayHeavyHit();
        GetComponentInChildren<PlayerCameraEffects>().OrNull()?.ApplyCameraImpulse(new Vector3(-5f, 0f, 6f), -6f);
    }

    // One holder thrown over: its posture broken (a brute's share counted in), the shove counter's burst where it met the
    // Knight, and the pink throw on every PC (the special-moment pick A, the same for the Knight as for the others).
    private void Throw(EnemyHealth enemy)
    {
        if (enemy == null || !enemy.IsAlive) return;
        Vector3 away = Vector3.ProjectOnPlane(enemy.transform.position - transform.position, Vector3.up);
        away = away.sqrMagnitude > 1e-4f ? away.normalized : transform.forward;
        if (enemy.TryGetComponent(out MeleeCombatTarget target) && target.IsAvailable && !target.IsStaggered)
            target.ApplyPostureDamage(target.MaximumPosture * 4f + 1f);
        Vector3 contact = transform.position + Vector3.up * 1.25f + away * .6f;
        GetComponent<ParryFeedback>().OrNull()?.CounterBurst(contact, (away * .35f + Vector3.up * .5f).normalized, 1.2f);
        if (enemy.TryGetComponent(out EnemyNetwork network)) network.ShareParry(contact, away, true);
        CardNet.Show(CardFx.Kind.Reversal, contact, away, 0f);
    }

    // ---- The shadow double (그림자 분신: the Knight's ultimate) ----

    /// <summary>
    /// The shadow double comes out (or stays out) for <paramref name="seconds"/> (the ultimate's, when every card is
    /// taken): the Knight's own character beside them, black and
    /// see-through, doing what they do a beat late, on every PC (BodyDouble), told at most once a second. While it is out
    /// the Knight's punches, slashes and counters land twice (Shadowed) and their parries take twice the posture
    /// (PlayerClass.ParryPosture). An execution it only mimes: an execution is no blow to echo.
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
        CardNet.Show(CardFx.Kind.Shadow, transform.position, transform.forward, Mathf.Max(.1f, shadowUntil - Time.time));
    }

    // A punch, slash or counter landed while the shadow is out: the shadow's own blow follows, as late as the double
    // follows the Knight (Knight.shadowDelay, BodyDouble).
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
            ShadowBlow(echo.Target, echo.Amount, echo.Point, echo.Direction, echo.Posture);
            if (echo.Posture > 0f && echo.Target.IsAlive && echo.Target.TryGetComponent(out MeleeCombatTarget target))
                target.ApplyPostureDamage(echo.Posture);
        }
    }

    // The shadow's blow on an enemy, this player's (its kill and its money are theirs, as a punch's are). It can't be
    // guarded and is no counter, so KnightStruck never echoes it again.
    private void ShadowBlow(EnemyHealth enemy, float amount, Vector3 point, Vector3 direction, float posture)
    {
        if (amount <= 0f || enemy == null || !enemy.IsAlive) return;
        enemy.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), amount, DamageKind.Attack, point, direction, gameObject,
            false, posture, true));
    }
}
