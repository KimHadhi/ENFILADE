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
        bool dead = !receiver.IsAlive, wiped = life != null && life.IsWiped, downed = receiver.IsDowned;
        if (!started) { started = true; NewLife(); }
        if (!dead && wasDead) NewLife();
        if (downed && !wasDowned) downedBy = RecentKiller();

        if (dead && !wasDead) Died(now, wiped);
        else if (wiped && !wasWiped) Wiped(now, dead);
        else if (previewing && now >= previewUntil)
        {
            previewing = false;
            Leave(now, look.outSeconds, false);
        }

        if (phase == Phase.Showing && !previewing)
        {
            // Back on their feet: a respawn, a wave, a Détente, or everyone back after the wipe. Cleared as the addon does.
            if (!dead && !wiped) Leave(now, look.clearSeconds, true);
            // Watching someone now: the world clears for it, and PlayerLifeHUD takes over.
            else if (!wipe && spectate != null && spectate.Current != PlayerSpectate.Mode.None) Leave(now, look.outSeconds, false);
            else if (look.holdSeconds > 0f && now - shownAt >= look.holdSeconds) Leave(now, look.outSeconds, false);
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
        if (GameSettings.Current.deathScreen == DeathScreenMode.Off) return;
        Show(now, lastFacts, asWipe, Current);
    }

    // The team is down (PlayerLife.BeginWipe, on every PC).
    private void Wiped(float now, bool dead)
    {
        if (life != null) wipeFrom = Mathf.Max(.01f, life.WipeLeft);
        if (GameSettings.Current.deathScreen == DeathScreenMode.Off) return;
        if (phase == Phase.Showing)
        {
            // Their own death's card becomes the wipe's: the word is slammed in again as 전멸.
            if (!wipe) card.Restamp(Texts.Get("death.title.wipe"), null, now);
            wipe = true;
            return;
        }
        var facts = dead && hasFacts ? lastFacts : Facts(Texts.Get("death.cause.wipe"), true);
        facts.Title = Texts.Get("death.title.wipe");
        Show(now, facts, true, Current);
    }

    private void Show(float now, DeathCard.Facts facts, bool asWipe, Look chosen)
    {
        look = chosen;
        wipe = asWipe;
        phase = Phase.Showing;
        shownAt = now;
        cutting = false;
        respawnFrom = life != null ? Mathf.Max(.01f, life.RespawnLeft) : 0f;
        if (asWipe && life != null && life.WipeLeft > 0f) wipeFrom = life.WipeLeft;
        facts.Next = Next(now, out _);
        card.Begin(facts, now);
        Grade(true);
        Sting();
    }

    private void Leave(float now, float seconds, bool cut)
    {
        if (phase != Phase.Showing) return;
        Levels(now, out colourFrom, out edgeFrom, out blurFrom, out inkFrom, out bloodFrom, out muffleFrom);
        phase = Phase.Leaving;
        leaveAt = now;
        leaveFor = Mathf.Max(.01f, seconds);
        if (!cut) return;
        cutting = true;
        cutAt = now;
    }

    private void Finish()
    {
        phase = Phase.Hidden;
        wipe = previewing = false;
        card.Hide();
        Grade(false);
        Muffle(0f);
    }

    private void Drive(float now)
    {
        if (cutting) Cut(now);
        if (phase == Phase.Hidden) return;
        Levels(now, out float colour, out float darkness, out float blurred, out float inkLevel, out float bloodLevel, out float muffled);
        float shown = phase == Phase.Showing ? 1f : 1f - Mathf.Clamp01((now - leaveAt) / leaveFor);
        ApplyGrade(colour, darkness, blurred);
        Muffle(muffled);
        string next = Next(now, out float fill);
        card.Tick(now, shown, next, fill, look.ink * inkLevel, look.blood * bloodLevel);
        if (phase == Phase.Leaving && now - leaveAt >= leaveFor) Finish();
    }

    // How far each part has come: in by its own time while showing, fading out together from where it got while leaving.
    private void Levels(float now, out float colour, out float darkness, out float blurred, out float inkLevel, out float bloodLevel, out float muffled)
    {
        if (phase == Phase.Showing)
        {
            float t = now - shownAt;
            colour = In(t, look.colourSeconds);
            darkness = In(t, look.vignetteSeconds);
            blurred = In(t - look.blurDelay, look.blurSeconds);
            inkLevel = In(t, look.inkSeconds);
            bloodLevel = Ease.Smooth((t - look.bloodDelay) / Mathf.Max(.01f, look.bloodSeconds));
            muffled = In(t, look.muffleSeconds);
            return;
        }
        float f = phase == Phase.Leaving ? 1f - Mathf.Clamp01((now - leaveAt) / leaveFor) : 0f;
        colour = colourFrom * f;
        darkness = edgeFrom * f;
        blurred = blurFrom * f;
        inkLevel = inkFrom * f;
        bloodLevel = bloodFrom * f;
        muffled = muffleFrom * f;
    }

    // The addon's ease: quick at first, settling at the end.
    private static float In(float t, float seconds)
    {
        if (seconds <= 0f) return t >= 0f ? 1f : 0f;
        float x = Mathf.Clamp01(t / seconds);
        return 1f - (1f - x) * (1f - x);
    }
}
