using UnityEngine;

// DeathScreen: Each frame. One part of the class; the rest is in DeathScreen.cs and the files beside it.
public sealed partial class DeathScreen
{
    private void LateUpdate()
    {
        if (player == null || receiver == null || !player.IsInitialized || held) return;
        if (tally == null) TryGetComponent(out tally);
        if (spectate == null) TryGetComponent(out spectate);
        float now = Time.unscaledTime;
        // Someone waiting to come into a run under way never fell (PlayerDamageReceiver.IsWaiting): no death to show.
        bool dead = !receiver.IsAlive && !receiver.IsWaiting, wiped = life != null && life.IsWiped, downed = receiver.IsDowned;
        if (!started) { started = true; NewLife(); }
        if (!dead && wasDead) NewLife();
        if (downed && !wasDowned) downedBy = RecentKiller();

        if (dead && !wasDead) Died(now, wiped);
        else if (wiped && !wasWiped) Wiped(now, dead);
        else if (previewing && now >= previewUntil)
        {
            previewing = false;
            // The addon's preview ends as its onRespawn does: the screen clears and the whistle is cut.
            Leave(now, true);
        }

        if (phase == Phase.Showing && !previewing)
        {
            // Back on their feet: a respawn, a wave, a Détente, or everyone back after the wipe. Cleared as the addon does.
            if (!dead && !wiped) Leave(now, true);
            // Watching someone now: the world clears for it, and PlayerLifeHUD takes over.
            else if (!wipe && spectate != null && spectate.Current != PlayerSpectate.Mode.None) Leave(now, false);
            else if (look.holdSeconds > 0f && now - shownAt >= look.holdSeconds) Leave(now, false);
            // The addon's Enable switched off while it shows: the screen, its whistle and the card go.
            else if (!HudAddons.DeathScreenModule.Enabled) Leave(now, true);
        }
        Drive(now);
        wasDead = dead;
        wasWiped = wiped;
        wasDowned = downed;
    }

    private void NewLife()
    {
        lifeStart = Time.time;
        killsAtStart = tally != null ? tally.Kills : 0;
        downedBy = null;
        hasFacts = false;
    }

    // Damage that reached them, for what killed them. A grab taking hold costs nothing by itself (DamageHit.TakesHold).
    private void OnBlow(DamageHit hit, DamageResult result)
    {
        if (result != DamageResult.Damaged || hit.TakesHold && !hit.IsFatal) return;
        lastBlow = hit;
        lastBlowAt = Time.time;
    }

    private void Died(float now, bool wiped)
    {
        previewing = false;
        // A recent blow killed them; otherwise they were down and bled out, put down by someone or by nobody known.
        string cause = Time.time - lastBlowAt <= RecentBlow ? Describe(lastBlow)
            : downedBy != null ? Texts.Format("death.cause.bleed_by", downedBy)
            : Texts.Get(wasDowned ? "death.cause.bleed" : "death.cause.unknown");
        // Alone, death is the wipe (PlayerLife), whether or not it has begun this frame.
        bool asWipe = wiped || !GameSession.IsOnline;
        lastFacts = Facts(cause, asWipe);
        hasFacts = true;
        // The addon's one switch (mw3death_enabled) is for the screen and the card together.
        if (!HudAddons.DeathScreenModule.Enabled) return;
        Show(now, lastFacts, asWipe);
    }

    // The team is down (PlayerLife.BeginWipe, on every PC).
    private void Wiped(float now, bool dead)
    {
        if (life != null) wipeFrom = Mathf.Max(.01f, life.WipeLeft);
        if (!HudAddons.DeathScreenModule.Enabled) return;
        if (phase == Phase.Showing)
        {
            // Their own death's card becomes the wipe's: the word is slammed in again as 전멸.
            if (!wipe) card.Restamp(Texts.Get("death.title.wipe"), null, now);
            wipe = true;
            return;
        }
        var facts = dead && hasFacts ? lastFacts : Facts(Texts.Get("death.cause.wipe"), true);
        facts.Title = Texts.Get("death.title.wipe");
        Show(now, facts, true);
    }

    // The card comes in over the addon's screen, which starts now with its whistle (its onDeath).
    private void Show(float now, DeathCard.Facts facts, bool asWipe)
    {
        wipe = asWipe;
        phase = Phase.Showing;
        shownAt = now;
        respawnFrom = life != null ? Mathf.Max(.01f, life.RespawnLeft) : 0f;
        if (asWipe && life != null && life.WipeLeft > 0f) wipeFrom = life.WipeLeft;
        facts.Next = Next(now, out _);
        card.Begin(facts, now);
        HudAddons.DeathScreenModule.Die(previewing);
    }

    // The card leaves with the addon's screen, in the screen's clearing time. cut: they live again (or a preview is over),
    // the addon's onRespawn that cuts the whistle; otherwise the view hands over to spectating and the whistle plays out.
    private void Leave(float now, bool cut)
    {
        if (phase != Phase.Showing) return;
        phase = Phase.Leaving;
        leaveAt = now;
        leaveFor = Mathf.Max(.01f, HudAddons.DeathScreenModule.ClearSeconds);
        if (cut) HudAddons.DeathScreenModule.Respawn();
        else HudAddons.DeathScreenModule.Leave();
    }

    // The card is gone. The addon's screen is not stopped here: its whistle plays out past the spectate handover.
    private void Finish()
    {
        phase = Phase.Hidden;
        wipe = previewing = false;
        card.Hide();
    }

    private void Drive(float now)
    {
        if (phase == Phase.Hidden) return;
        float shown = phase == Phase.Showing ? 1f : 1f - Mathf.Clamp01((now - leaveAt) / leaveFor);
        string next = Next(now, out float fill);
        card.Tick(now, shown, next, fill);
        if (phase == Phase.Leaving && now - leaveAt >= leaveFor) Finish();
    }
}
