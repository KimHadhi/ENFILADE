using UnityEngine;

// ZombieMind: Thinking. One part of the class; the rest is in ZombieMind.cs and the files beside it.
public sealed partial class ZombieMind
{
    private void Update()
    {
        if (!Thinks) return;
        ComeTo();
        float dt = Time.deltaTime;
        // Held (a takedown, a grab), shoved or down, it turns to nothing: an alert one can be taken from behind now, and
        // turning in the executor's hands would pull the takedown apart.
        if (melee == null || !melee.IsStunned)
        {
            if (Mood == ZombieMood.Alert) TurnTo(stimulus, dt);
            else if (Mood == ZombieMood.Calm) LookRound(dt);
            else if (Mood == ZombieMood.Searching && arrived) Sweep(dt);
        }
        if (Mood == ZombieMood.Rousing || Mood == ZombieMood.Rising)
        {
            if (focus != null) TurnTo(focus.transform.position, dt);
            if (Time.time >= rouseUntil)
            {
                Mood = ZombieMood.Awake;
                if (agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh) agent.isStopped = false;
            }
        }
        // Getting up out of a pose it stays where it is, whatever it means to do next (a search sets off its walk at once,
        // and nothing else stops it). The frame it stands, a search walks on; the chase takes it itself (Ready).
        if (GettingUp) { Halt(); heldUp = true; }
        else if (heldUp)
        {
            heldUp = false;
            if (Mood == ZombieMood.Searching && agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh) agent.isStopped = false;
        }
        if (calling) Tell();
        if ((thinkLeft -= dt) > 0f) return;
        // Far from every player it thinks less often (ZombieLod); what it sees adds up over the longer step all the same.
        float step = ZombieLod.ThinkEvery(transform.position, ThinkEvery);
        thinkLeft += step;
        // A horde zombie needs no senses to hunt (EnemyTargeting gives it the nearest), but whether any zombie still has
        // someone in sight decides when the campaign lets the chase die down (LastNoticed, CampaignRun's lull). Once one
        // has seen someone very lately the others needn't cast their rays too (ZombieLod): they take that sighting.
        if (horde)
        {
            if (Mood != ZombieMood.Awake) return;
            if (!ZombieLod.HordeLooks(LastNoticed)) lastKnown = LastSeenAt;
            else if (Look(out _, out _) is PlayerMovement seen) { LastNoticed = Time.time; LastSeenAt = lastKnown = seen.transform.position; }
            return;
        }
        // Down, stunned or held it notices nothing new; a blow has woken it anyway.
        if (melee != null && melee.IsStunned) return;
        Think(step);
    }

    // Knocked down, it used to fight again the moment the get-up clip let it go, so one that had just risen grabbed a
    // player in the same instant (user, 2026-11-11 playtest: "완전히 일어나고 정신 차리고 잡아야지 ... 불공평함"). Now,
    // the frame its knockdown ends (the clip done, ZombieBody.Down's Stand), it rouses for riseRouse: it stands where it
    // got up, turns to the nearest player and plays its rouse on every PC, and only then chases and fights (Ready). A
    // legless crawler pushes straight into its crawl instead: the rouse is a standing clip.
    private void ComeTo()
    {
        bool down = melee != null && melee.IsKnockedDown;
        bool rose = wasDown && !down;
        wasDown = down;
        if (!rose || riseRouse <= 0f || Mood != ZombieMood.Awake) return;
        if (TryGetComponent(out ZombieType type) && type.Crawls) return;
        var at = Alive(focus) ? focus : PlayerRoster.Nearest(transform.position);
        Mood = ZombieMood.Rising;
        if (Alive(at)) focus = at;
        rouseUntil = Time.time + riseRouse;
        Halt();
    }

    private bool wasDown;

    private void Think(float dt)
    {
        var seen = Look(out float rate, out bool touching);
        if (IsAwake)
        {
            if (seen != null && (focus == null || seen == focus || !Alive(focus))) focus = seen;
            if (focus != null && seen == focus) { lastSeen = Time.time; LastSeenAt = lastKnown = focus.transform.position; LastNoticed = Time.time; }
            else if (Time.time - lastSeen > loseSeconds) { suspicion = alertAt; Search(lastKnown); }
            return;
        }
        if (seen != null)
        {
            suspicion += rate * dt;
            focus = noticed = seen;
            // Watching a stone, a glimpse doesn't turn its head: only once it is unsure of someone (alertAt) does it look
            // their way.
            if (!Distracted || suspicion >= alertAt) stimulus = seen.transform.position;
            if (touching) { Wake(seen); return; }
        }
        else suspicion = Mathf.Max(0f, suspicion - forget * dt);

        if (suspicion >= 1f) { Wake(focus); return; }
        // Sure enough to stop and stare: a glance becomes a real look, and one that made someone out forgets the stone.
        if (suspicion >= alertAt)
        {
            glanceUntil = 0f;
            if (seen != null) lured = false;
            Alert(stimulus);
            return;
        }
        if (Mood == ZombieMood.Alert)
        {
            // A glance at something it won't go to looks till its time is up and settles back; the stare at a stone ends
            // in a walk over to it.
            if (glanceUntil > 0f)
            {
                if (Time.time < glanceUntil) return;
                if (lured) Search(lureAt, true);
                else Settle(false);
                return;
            }
            Search(stimulus, lured);
            return;
        }
        if (Mood == ZombieMood.Searching) Searching();
        else if (Mood == ZombieMood.Calm && suspicion <= 0f) noticed = null;
    }

    private void Searching()
    {
        if (!arrived && agent != null && agent.isActiveAndEnabled && agent.isOnNavMesh && !agent.pathPending
            && agent.remainingDistance <= ArriveDistance)
        {
            arrived = true;
            arrivedAt = Time.time;
            arriveYaw = transform.eulerAngles.y;
            Halt();
        }
        if ((arrived && Time.time - arrivedAt >= LookTime) || Time.time - searchStart >= searchSeconds + (lured ? lureLook : 0f)) Settle(true);
    }

    // How long it looks round where it came to search: longer where a stone fell.
    private float LookTime => lured ? lureLook : lookSeconds;

    private void TurnTo(Vector3 at, float dt)
    {
        Vector3 way = Vector3.ProjectOnPlane(at - transform.position, Vector3.up);
        if (way.sqrMagnitude < .01f) return;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.LookRotation(way), turnSpeed * dt);
    }

    // Standing calm it looks round now and then: a slow turn some way off the way it stood, and back again the next time,
    // so no stretch of the way is watched for good and gaps open and close (the stealth view's cones show it). Every
    // calm zombie used to stare one way forever, so a scattered few covered every way past (user, 2026-11-11: "얘들이
    // 각자 흩어져 있으니까 사방을 보고 있고").
    /// <summary>
    /// Its body sits, eats or crouches while calm (ZombieBody, ZombieLooks.calmPoses): it doesn't turn to look round,
    /// as a seated body spinning on the spot looks wrong. Set by ZombieBody on every PC; only the host's is read.
    /// </summary>
    internal bool HoldsPose { get; set; }

    private void LookRound(float dt)
    {
        if (HoldsPose || lookAroundEvery <= 0f || lookAroundAngle <= 0f || lookAroundSpeed <= 0f) return;
        if (Time.time >= lookAt)
        {
            lookAt = Time.time + lookAroundEvery * Random.Range(.6f, 1.4f);
            bool atPost = Mathf.Abs(Mathf.DeltaAngle(lookYaw, postYaw)) < 1f;
            lookYaw = atPost ? postYaw + Random.Range(-lookAroundAngle, lookAroundAngle) : postYaw;
        }
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.Euler(0f, lookYaw, 0f), lookAroundSpeed * dt);
    }

    // Come to where it searches, it looks left, then right, then back ahead over lookSeconds, not a statue's stare.
    private void Sweep(float dt)
    {
        float look = LookTime, sweep = lured ? lureSweep : searchSweep;
        if (sweep <= 0f || look <= 0f) return;
        float phase = Mathf.Clamp01((Time.time - arrivedAt) / look);
        float yaw = arriveYaw + Mathf.Sin(phase * Mathf.PI * 2f) * sweep;
        transform.rotation = Quaternion.RotateTowards(transform.rotation, Quaternion.Euler(0f, yaw, 0f), turnSpeed * .5f * dt);
    }

    // The player it notices fastest now, how fast (suspicion a second), and whether they stand right before its eyes.
    private PlayerMovement Look(out float best, out bool touching)
    {
        best = 0f;
        touching = false;
        PlayerMovement found = null;
        // Going to look at a stone it is curious, not alarmed: its calm eyes. Eating or dozing, it sees less (BusyRange).
        float sight = ViewRange;
        Vector3 eye = Eye();
        foreach (var player in PlayerRoster.All)
        {
            if (!Alive(player)) continue;
            Vector3 toward = player.transform.position - transform.position;
            bool lit = Lit(player, toward);
            // Someone standing in the dark is made out from less far and more slowly (StealthLight, the memo's item 47:
            // "어두운 곳일수록 덜 보이게"); a flashlight turned on it lights them whatever the dark.
            float light = lit ? 1f : StealthLight.Level(player);
            float range = sight * (lit ? lightRange : StealthLight.RangeShare(light));
            float distance = toward.magnitude;
            if (distance > range) continue;
            float angle = Vector3.Angle(transform.forward, Vector3.ProjectOnPlane(toward, Vector3.up));
            float view = Angular(angle);
            if (view <= 0f) continue;
            float shown = Shown(player, eye);
            if (shown <= 0f) continue;
            // It hardly looks up: someone well above its eyes is seen at lookUpShare.
            float above = Mathf.Atan2(player.transform.position.y + 1f - eye.y, Mathf.Max(.01f, new Vector2(toward.x, toward.z).magnitude))
                * Mathf.Rad2Deg;
            float rate = nearRate * Mathf.Lerp(1f, farShare, distance / Mathf.Max(.01f, range)) * shown * Posture(player) * view
                * (above > lookUp ? lookUpShare : 1f) * (lit ? lightShare : StealthLight.SeenShare(light)) * BusyNotice;
            if (rate <= best) continue;
            best = rate;
            found = player;
            touching = distance <= touchRange && angle <= FrontView * .5f;
        }
        return found;
    }

    // How well it sees at this angle off its nose (degrees): fully straight ahead, a little less toward the edge of its
    // full view, then quickly down to sideShare, which holds across the edge of its sight and fades out at its very end.
    // It was a step from full to sideShare, so one degree either side of the line decided a great deal (Advanced
    // Stealth's peripheral glimpses, which gather suspicion much more slowly).
    private float Angular(float angle)
    {
        // Eating or dozing, both narrow (BusyView).
        float front = FrontView * .5f, edge = Mathf.Max(front + 1f, EdgeView * .5f);
        if (angle <= front) return Mathf.Lerp(1f, .85f, Mathf.Clamp01(angle / Mathf.Max(1f, front)));
        if (angle >= edge) return 0f;
        float t = (angle - front) / (edge - front);
        return Mathf.Lerp(.85f, sideShare, Mathf.SmoothStep(0f, 1f, t / .25f)) * (1f - Mathf.SmoothStep(0f, 1f, (t - .75f) / .25f))
            * (Distracted ? lureFocus : 1f);
    }

    // Their flashlight is on and turned this way: it makes them out sooner and from farther ("손전등 켜면 들킨다").
    private bool Lit(PlayerMovement player, Vector3 toward)
    {
        if (lightShare <= 1f && lightRange <= 1f) return false;
        var gun = GunOf(player);
        if (gun == null || !gun.LightOn) return false;
        Vector3 beam = Vector3.ProjectOnPlane(player.transform.forward, Vector3.up);
        Vector3 back = Vector3.ProjectOnPlane(-toward, Vector3.up);
        return beam.sqrMagnitude > 1e-4f && back.sqrMagnitude > 1e-4f && Vector3.Angle(beam, back) <= lightAngle;
    }

    private static WeaponController GunOf(PlayerMovement player)
    {
        if (!guns.TryGetValue(player, out var gun) || gun == null) guns[player] = gun = player.GetComponentInChildren<WeaponController>();
        return gun;
    }

    // How much of them it can see: the share of head, chest and hips no wall hides, nor a Ghost's smoke (SmokeScreen).
    private static float Shown(PlayerMovement player, Vector3 eye)
    {
        var capsule = player.GetComponent<CharacterController>();
        Bounds bounds = capsule != null && capsule.enabled ? capsule.bounds : new Bounds(player.transform.position + Vector3.up * .9f, new Vector3(.6f, 1.8f, .6f));
        int seen = 0;
        foreach (float height in LookHeights)
        {
            Vector3 point = new Vector3(bounds.center.x, bounds.min.y + bounds.size.y * height, bounds.center.z);
            Vector3 path = point - eye;
            float length = path.magnitude;
            if ((length < .01f || !Physics.Raycast(eye, path / length, out RaycastHit hit, length, Sight, QueryTriggerInteraction.Ignore)
                || hit.collider.GetComponentInParent<PlayerMovement>() == player) && !SmokeScreen.Hides(eye, point))
                seen++;
        }
        return seen / (float)LookHeights.Length;
    }

    // Crouched or lying down they are harder to make out; moving, easier, and a sprint most of all.
    private float Posture(PlayerMovement player)
    {
        float share = 1f;
        var state = player.CurrentState;
        if (player.IsProning || state == PlayerMovement.MovementState.Supine || state == PlayerMovement.MovementState.Sliding
            || state == PlayerMovement.MovementState.HeadFirstSliding || state == PlayerMovement.MovementState.SupineSliding)
            share = lyingShare;
        // A dodge made from a crouch stays low (IsCrouchStance): crouched, and no sprint for its burst of speed, so a quick
        // crouched dodge in behind one is seen as sneaking (user, 2026-11-12).
        bool low = player.IsCrouchStance;
        if (low && share > crouchShare) share = crouchShare;
        float speed = new Vector2(player.ActualVelocity.x, player.ActualVelocity.z).magnitude;
        float motion = speed >= sprintSpeed && !low ? sprintShare : Mathf.Lerp(stillShare, 1f, Mathf.Clamp01(speed / 1.5f));
        return share * motion;
    }

    private Vector3 Eye()
    {
        var head = body != null && body.Model != null ? body.Head : null;
        return head != null ? head.position : transform.position + Vector3.up * .7f;
    }

    // Walls and the world hide players (maps build them on Default); other zombies don't.
    private static int Sight => sightMask != 0 ? sightMask : sightMask = ~LayerMask.GetMask(EnemyLayer);

    // Worked out once: GetMask allocates, and this was asked three times a player every think of every zombie.
    private static int sightMask;

    // Sound goes round players, corpses and what lies on the floor (layer 31): only the world muffles it.
    private static int Hearing => Sight & ~(1 << 31);

    private static bool Alive(PlayerMovement player)
        => player != null && player.IsInitialized && !player.IsDead && player.DamageReceiver != null && player.DamageReceiver.IsAlive;
}
