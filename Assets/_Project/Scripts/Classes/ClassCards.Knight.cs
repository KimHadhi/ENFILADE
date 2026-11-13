using TMPro;
using UnityEngine;
using UnityEngine.UI;

// ClassCards: Knight. One part of the class; the rest is in ClassCards.cs and the files beside it.
public sealed partial class ClassCards
{
    private void OnPunched(float charge)
    {
        punchCharge = charge;
        punchAt = Time.time;
    }

    // A parry or a perfect dodge (a Knight's), and a grab taking hold.
    private void OnResolved(DamageHit hit, DamageResult result)
    {
        // A parried blow fills the gauge whoever parried it (UltimateCharge.AwardHelp): a Knight's job above all.
        // (A blow a Knight's cover turned on this player is the Knight's parry: its charge goes to them, KnightCover.Covered.)
        if (result == DamageResult.Parried && hit.Id != KnightCover.CoveredHit) UltimateCharge.AwardHelp(UltimateCharge.Help.Parried);
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
        GetComponent<ParryFeedback>().OrNull()?.CounterBurst(contact, (away * .35f + Vector3.up * .5f).normalized, 1.2f);
        if (grabber.TryGetComponent(out EnemyNetwork network)) network.ShareParry(contact, away, true);
        // The throw for everyone to see and its tag on this screen (the special-moment pick A), no banner over the screen.
        CardNet.Show(CardFx.Kind.Reversal, contact, away, 0f);
        MomentHUD.Reversal();
        GetComponent<PlayerFistAudio>().OrNull()?.PlayHeavyHit();
        GetComponentInChildren<PlayerCameraEffects>().OrNull()?.ApplyCameraImpulse(new Vector3(-5f, 0f, 6f), -6f);
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
    /// when every card is taken): the Knight's own character steps out beside them, violet-black and see-through, on every
    /// PC (BodyDouble), told at most once a second.
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
}
