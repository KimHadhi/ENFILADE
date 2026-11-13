using UnityEngine;

// ZombieMind: Moods. One part of the class; the rest is in ZombieMind.cs and the files beside it.
public sealed partial class ZombieMind
{
    /// <summary>When an awake zombie last saw anyone (game time), horde or not: the campaign's lull waits on it.</summary>
    public static float LastNoticed { get; private set; } = float.NegativeInfinity;

    /// <summary>Where that was (a player's feet then): the horde zombies that skip their own look take it as theirs.</summary>
    public static Vector3 LastSeenAt { get; private set; }

    /// <summary>
    /// The chase dies down (CampaignRun's lull): every awake zombie on the host, horde ones included, loses its quarry and
    /// goes to look where it last knew them, then calms (Searching). Being found once no longer means being found for good
    /// (user, 2026-11-11 playtest: "한번 들키면 끝이고 의미 없는 거 아니야?").
    /// </summary>
    public static void LoseAll()
    {
        foreach (var mind in minds)
        {
            if (mind == null || !mind.Thinks || !mind.IsAwake) continue;
            if (mind.TryGetComponent(out ZombieGrip grip) && grip.IsHolding) continue;
            mind.horde = false;
            mind.focus = null;
            // Lost, none of them is noticing anyone in particular: no question marks over the whole crowd.
            mind.noticed = null;
            mind.suspicion = mind.alertAt;
            mind.Search(mind.lastKnown != Vector3.zero ? mind.lastKnown : mind.transform.position);
        }
    }

    /// <summary>
    /// It lost its quarry in smoke (SmokeScreen, on the host): as the chase dying down does to all of them (LoseAll), this
    /// one goes to look where it last knew them and must make them out again by sight or sound, horde zombie or not. A
    /// Ghost's smoke is how they slip a crowd off their back (user, 2026-11-12: "잠입과 어그로를 피하기 위한 연막탄").
    /// </summary>
    internal void Lose(Vector3 lastAt)
    {
        if (!Thinks || !IsAwake) return;
        if (TryGetComponent(out ZombieGrip grip) && grip.IsHolding) return;
        horde = false;
        focus = noticed = null;
        suspicion = alertAt;
        lastKnown = lastAt;
        Search(lastAt);
    }

    /// <summary>Calms it: it stands where it is and has to notice the players (a placed zombie, /spawn n calm).</summary>
    public void Calm()
    {
        if (IsCopy) return;
        horde = false;
        Mood = ZombieMood.Calm;
        suspicion = 0f;
        focus = noticed = null;
        glanceUntil = distractedUntil = 0f;
        lured = false;
        Halt();
        Post();
        lookAt = Time.time + Random.Range(1.5f, Mathf.Max(1.5f, lookAroundEvery));
    }

    /// <summary>
    /// Placed calm (CampaignHorde), it stands looking this way (degrees about up) and looks round from there.
    /// </summary>
    internal void FaceWhenCalm(float yaw)
    {
        if (IsCopy) return;
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        Post();
    }

    // Where it stands looking now becomes the way it looks round from.
    private void Post()
    {
        postYaw = lookYaw = transform.eulerAngles.y;
    }

    /// <summary>
    /// Awake already, it rouses again for this long: it stands, faces this player and plays its rouse on every PC, then
    /// goes on as before. A charger winding up (ZombieCharger).
    /// </summary>
    internal void Rouse(PlayerMovement at, float seconds)
    {
        if (IsCopy || Mood != ZombieMood.Awake) return;
        Mood = ZombieMood.Rousing;
        if (Alive(at)) focus = at;
        rouseUntil = Time.time + Mathf.Max(0f, seconds);
        Halt();
    }

    /// <summary>Wakes it after who (or the nearest player). Rouse: it rouses a moment first. Call: it wakes the calm near it.</summary>
    public void Wake(PlayerMovement who, bool rouse = true, bool call = true)
    {
        if (IsCopy || IsAwake) return;
        Mood = rouse && rouseSeconds > 0f ? ZombieMood.Rousing : ZombieMood.Awake;
        suspicion = 1f;
        glanceUntil = distractedUntil = 0f;
        lured = false;
        focus = Alive(who) ? who : PlayerRoster.Nearest(transform.position);
        noticed = focus;
        lastSeen = Time.time;
        if (focus != null) lastKnown = focus.transform.position;
        rouseUntil = Time.time + rouseSeconds;
        SetSpeed(1f);
        // Woken out of a pose it gets up first, from this very frame (its body takes it over from its next update).
        if (HoldsPose) GettingUp = true;
        // Rousing, it stands and faces its quarry; then EnemyChaseAI takes it.
        if (Mood == ZombieMood.Rousing || GettingUp) Halt();
        else if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh) agent.isStopped = false;
        Woke?.Invoke(this, focus);
        // Woken on its own (not by another's call), it draws breath to call the others first (callTell), the same tell on
        // every PC (EnemyNetwork shares it); killed, held, stunned or knocked down meanwhile, nobody else is told (Tell).
        // It used to wake every calm one within callRadius that instant, walls or not, and a campaign's quiet ended with
        // it: one sighting brought everyone (user, 2026-11-12: "한번 들켰다고 바로 모든 놈들이 나한테 오지 말고 들켜도
        // 들킨 놈들 빠르게 싹 다 처치하면 조용히 넘어갈 수 있었으면 좋겠어").
        if (!call) return;
        if (callTell <= 0f) { Call(); return; }
        calling = true;
        callAt = Time.time + callTell;
        ZombieCall.Tell(Eye(), callTell);
    }

    // Drawing breath to call: held (a takedown, a grab), stunned or knocked down, or no longer awake, it loses it and
    // nobody is told; its breath drawn, it roars (Call).
    private void Tell()
    {
        if (!IsAwake || (melee != null && (melee.IsStunned || melee.IsLying || melee.IsHeld))) { calling = false; return; }
        if (Time.time < callAt) return;
        calling = false;
        Call();
    }

    // The roar: every zombie not awake yet that hears it (Reaches: a sound callRadius loud, a wall halving it, through its
    // own ears, dulled eating or dozing) wakes after the same quarry, and none calls in turn; the rest keep to themselves
    // and may still notice on their own. Every PC hears the roar (ShowCall).
    private void Call()
    {
        var roar = new NoiseEvent(Eye(), callRadius, gameObject);
        foreach (var other in minds)
            if (other != this && other.Thinks && !other.horde && !other.IsAwake && other.Reaches(roar, out _, out _))
                other.Wake(focus, true, false);
        Called?.Invoke(this, focus);
        ShowCall();
        if (network != null) network.ShareCall();
    }

    /// <summary>Every PC: the call's roar from its mouth, and a word at the top of the screen for anyone near enough to hear it.</summary>
    internal void ShowCall()
    {
        Vector3 mouth = Eye();
        ZombieCall.Roar(mouth);
        var mine = PlayerRoster.Local;
        if (mine != null && (mine.transform.position - mouth).sqrMagnitude <= callRadius * callRadius)
            AlertHUD.Danger(Texts.Get("stealth.call"), transform, 2f);
    }

    private void Alert(Vector3 at)
    {
        stimulus = at;
        if (Mood == ZombieMood.Alert) return;
        Mood = ZombieMood.Alert;
        Halt();
    }

    // It turns to look at something a while (a faint sound, a stone another goes to check), then settles back where it
    // stood. One already searching keeps at it, one on its way to a stone keeps to it, and a real alert stays one.
    private void Glance(Vector3 at, PlayerMovement by, float seconds)
    {
        if (seconds <= 0f || lured) return;
        if (Mood != ZombieMood.Calm && !(Mood == ZombieMood.Alert && glanceUntil > 0f)) return;
        stimulus = at;
        if (noticed == null && Alive(by)) noticed = by;
        glanceUntil = Time.time + seconds;
        if (Mood == ZombieMood.Calm) { Mood = ZombieMood.Alert; Halt(); }
    }

    // The one nearest where a stone fell: it stares that way a moment, then walks over briskly, looks round there a good
    // while and settles on that spot. A stone never makes it any surer that someone is there: it used to, which filled
    // the thrower's stealth gauge as if they had been seen and left the zombie a glimpse from waking (user, 2026-11-11:
    // "돌 던지기가 걍 좀비 의심 게이지만 높이고 별 쓸모가 없는 거 같은데"). Turned to the stone its back is to the way
    // past, and once settled it no longer stands where it stood.
    private void Investigate(Vector3 at, PlayerMovement by)
    {
        lured = true;
        lureAt = stimulus = at;
        if (Alive(by)) noticed = by;
        if (Mood == ZombieMood.Searching) { Search(at, true); return; }
        glanceUntil = Time.time + Mathf.Max(.05f, lureStare);
        if (Mood != ZombieMood.Alert) { Mood = ZombieMood.Alert; Halt(); }
    }

    // Lure: it is going to look where a stone fell (briskly, and with calm eyes: Look).
    private void Search(Vector3 at, bool lure = false)
    {
        Mood = ZombieMood.Searching;
        lured = lure;
        glanceUntil = 0f;
        stimulus = at;
        searchStart = Time.time;
        arrived = false;
        SetSpeed(lure ? lureSpeedShare : searchSpeedShare);
        if (agent == null || !agent.isActiveAndEnabled || !agent.isOnNavMesh) return;
        // Sent off while still getting up out of a pose (or straight out of one), it walks once it stands (Update).
        if (HoldsPose) GettingUp = true;
        agent.isStopped = GettingUp;
        agent.SetDestination(at);
    }

    // Back to standing calm. Glancing, it turns back the way it stood; after a search, where it ends up is its new post.
    private void Settle(bool newPost)
    {
        glanceUntil = 0f;
        lured = false;
        Mood = ZombieMood.Calm;
        SetSpeed(1f);
        Halt();
        if (newPost) Post();
        else lookYaw = postYaw;
        lookAt = Time.time + Mathf.Max(1f, lookAroundEvery) * Random.Range(.5f, 1f);
    }

    private void Halt()
    {
        if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh) { agent.isStopped = true; agent.velocity = Vector3.zero; }
    }

    private void SetSpeed(float share)
    {
        if (agent != null && chaseSpeed > 0f) agent.speed = chaseSpeed * share;
    }

    /// <summary>
    /// Its chase speed changed (its kind, or crawling: ZombieType). Searching walks a share of it, and a flinch slows
    /// from and returns to it.
    /// </summary>
    internal void SetChaseSpeed(float speed)
    {
        chaseSpeed = speed;
        if (TryGetComponent(out EnemyHitReaction reaction)) reaction.SetBaseSpeed(speed);
        SetSpeed(Mood == ZombieMood.Searching ? lured ? lureSpeedShare : searchSpeedShare : 1f);
    }

    /// <summary>A blow (EnemyHealth, where it thinks): it wakes at once, after whoever landed it.</summary>
    internal void Struck(in DamageHit hit)
    {
        if (!Thinks || IsAwake) return;
        Wake(hit.Source != null ? hit.Source.GetComponentInParent<PlayerMovement>() : null, false);
    }

    /// <summary>On another PC: the mood the host's zombie is in, how sure it is (twentieths) and of whom (client id + 1).</summary>
    internal void ShowMind(byte mood, byte step, ulong watch, bool drawing = false)
    {
        if (!IsCopy) return;
        Mood = (ZombieMood)mood;
        shownStep = step;
        shownWatch = watch;
        // The host's zombie began drawing breath to call: the same tell here, timed from now.
        if (drawing && !shownCalling)
        {
            shownCallSince = Time.time;
            if (Living) ZombieCall.Tell(Eye(), callTell);
        }
        shownCalling = drawing;
    }
}
