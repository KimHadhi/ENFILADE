using UnityEngine;

/// <summary>
/// The reaper, the memo's item 24 as the user shaped it on 2026-11-12 ("가까이 붙으면 피할 수 있는 얘는 그림 리퍼 컨셉으로
/// 낫을 천천히 차징하는 거임. 차징하면서 공격 범위 예고가 천천히 넓어지는 게 보이고 붙어서 앉으면 낫 휘두를 때 피할 수
/// 있음"). A tall, slow zombie in a hood with a long scythe. Its only attack is the reap (AttackMove.Reap): it stands and
/// draws the scythe back for a long charge (reapWindup), and a ring on the ground widens from its feet out to ringEnd,
/// slowly at first and faster toward the end: how far the blade will reach. Then the blade goes round once at waist
/// height. Inside the ring and standing, a player is cut (reapDamage) and thrown; beyond it, or with a wall between, they
/// are untouched. Hugging it (within hugReach, a thin white ring the whole charge long) and low (crouched, prone,
/// sliding, diving or down), they are inside the blade's arc, under the shaft, and it passes over them: the way out the
/// memo asked for, the opposite of the runner's (stepping back gets you cut). Going under it pays dodgeReward on the
/// dodger's own PC (the user's rule: a hit is a plus, a miss is simply a miss), and every full turn leaves it reeling for
/// overswingStun, open to everyone. Running out of the ring at once works too. Neither a guard nor a parry stops a full
/// turn of a scythe. Every PC draws the ring from the host's windup and judges only its own player when the windup runs
/// out (EnemyCombatAI), as the breadbag's cut is. Numbers: Tables/ZombieReaper; its kind's row in ZombieKinds.
/// </summary>
[DisallowMultipleComponent]
public sealed class ZombieReaper : ZombieElite
{
    public const string TableName = "ZombieReaper", TableRow = "Default";
    private const int Rate = 44100, RingPoints = 96;
    // The ring's height over the ground (m), and how long it flashes white as the blade goes round (real s).
    private const float RingLift = .06f, FlashSeconds = .35f;
    private static AudioClip windClip, whooshClip;

    [Header("The reap")]
    [SerializeField, Min(.3f), Tooltip("The charge: how long it draws the scythe back before the turn (s).")]
    private float reapWindup = 2.4f;
    [SerializeField, Min(.2f), Tooltip("The ring's radius as the charge begins (m).")]
    private float ringStart = 1.2f;
    [SerializeField, Min(.5f), Tooltip("The ring's radius as the charge ends: how far the blade reaches (m).")]
    private float ringEnd = 9f;
    [SerializeField, Min(.2f), Tooltip("How the ring widens over the charge: 1 evenly, more slowly at first and faster at the end.")]
    private float ringCurve = 1.8f;
    [SerializeField, Min(.3f), Tooltip("Within this of its feet (m) a player low enough is inside the blade's arc: it passes over.")]
    private float hugReach = 1.9f;
    [SerializeField, Min(.3f), Tooltip("Feet more than this above or below its own (m) are on another floor: the blade doesn't reach.")]
    private float floorReach = 1.6f;
    [SerializeField, Min(0f), Tooltip("Damage of the blade.")]
    private float reapDamage = 45f;
    [SerializeField, Min(0f), Tooltip("Cut, the player is thrown this fast away (m/s).")]
    private float knock = 7f;
    [SerializeField, Min(0f), Tooltip("And this fast up (m/s), so they fly and land on their back.")]
    private float knockUp = 3.2f;
    [SerializeField, Min(.05f), Tooltip("The blade's turn as it is seen on the floor (s).")]
    private float spinSeconds = .32f;
    [SerializeField, Min(0f), Tooltip("After a full turn it reels this long (s): everyone's chance.")]
    private float overswingStun = 1.4f;
    [SerializeField, Min(0f), Tooltip("Money for going under the blade.")]
    private int dodgeReward = 60;
    [Header("Look and sound")]
    [SerializeField] private Color glowColor = new Color(.62f, .38f, 1f);
    [SerializeField, Min(.05f)] private float glowSize = .32f;
    [SerializeField, Range(0f, 1f), Tooltip("The cold wind gathering round it as it charges.")]
    private float windVolume = .55f;
    [SerializeField, Range(0f, 1f), Tooltip("The scythe drawn back, as the charge begins.")]
    private float chargeVolume = .8f;
    [SerializeField, Range(0f, 1f), Tooltip("The blade going round.")]
    private float swingVolume = 1f;

    private bool charging;
    private float reelAt = -1f, flashLeft;
    private AudioSource wind;
    private LineRenderer ring, hug;

    protected override ZombieKind Kind => ZombieKind.Reaper;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget() => windClip = whooshClip = null;

    protected override void Awake()
    {
        base.Awake();
        GameTables.Bind(TableName, TableRow, this);
    }

    // ---- What EnemyCombatAI asks and tells ----

    // The reap is all it has.
    internal override AttackMove? Choose(PlayerMovement target, bool level) => AttackMove.Reap;
    internal override float WindupOf(AttackMove move) => move == AttackMove.Reap ? reapWindup : 0f;
    // A whole turn of a scythe isn't stopped by a guard or turned by a parry: under it or out of it.
    internal override bool GuardableBy(PlayerMovement victim, AttackMove move) => move != AttackMove.Reap;

    internal override void Struck(AttackMove move)
    {
        if (move != AttackMove.Reap || !Mine) return;
        // Every PC: the blade's mark round the floor, its sound, the ring's flash, and its own player judged.
        Vector3 feet = Feet;
        SweepArcVfx.Play(transform, Flat(transform.forward), 360f, ringEnd, spinSeconds);
        var looks = EliteLooks.Instance;
        PlayLow(Or(looks != null ? EliteLooks.Pick(looks.reaperSwing) : null, Whoosh()), feet + Vector3.up * 1.1f, swingVolume, .62f, 4f, 55f);
        flashLeft = FlashSeconds;
        Cut(PlayerRoster.Local, feet);
        // The host: the turn spent, it reels a moment (Interrupt tells the copies).
        if (Runs) reelAt = Time.time + spinSeconds;
    }

    // The blade at this PC's own player: inside the ring and standing, cut and thrown; hugging it low, under it.
    private void Cut(PlayerMovement mine, Vector3 feet)
    {
        if (mine == null || !mine.IsInitialized || mine.IsDead || mine.DamageReceiver == null) return;
        Vector3 to = mine.transform.position - feet;
        if (Mathf.Abs(to.y) > floorReach) return;
        Vector3 flat = Flat(to);
        float away = flat.magnitude;
        if (away > ringEnd) return;
        Vector3 waist = feet + Vector3.up, chest = mine.transform.position + Vector3.up;
        // A wall between takes the blade (there is nothing to go under then either).
        if (away > hugReach && Physics.Linecast(waist, chest, WorldMask, QueryTriggerInteraction.Ignore)) return;
        if (away <= hugReach && Low(mine))
        {
            if (dodgeReward > 0) CombatFeedHUD.Award(Texts.Get("feed.score.reap_dodge"), dodgeReward);
            return;
        }
        Vector3 direction = away > .01f ? flat / away : Flat(transform.forward).normalized;
        var hit = new DamageHit(DamageHit.NewAttackId(), reapDamage, DamageKind.Attack, chest, direction, gameObject, false,
            origin: waist);
        if (mine.DamageReceiver.ReceiveHit(hit) == DamageResult.Damaged) mine.KnockBack(direction * knock + Vector3.up * knockUp);
    }

    // Down low enough for a blade at waist height to pass over: crouched, prone or on the back, sliding, diving, or down.
    private static bool Low(PlayerMovement player)
        => player.IsCrouching || player.IsLyingDown || player.IsSliding || player.IsDiving || player.IsDowned;

    private void Update()
    {
        if (reelAt < 0f || Time.time < reelAt) return;
        reelAt = -1f;
        if (Runs && Mine && melee != null && health != null && health.IsAlive) melee.Interrupt(overswingStun);
    }

    // ---- Every PC: its light, the ring, the wind ----

    private void LateUpdate()
    {
        bool shown = ShowElite(glowColor, glowSize, HumanBodyBones.Head, HumanBodyBones.Neck);
        bool winding = shown && combat != null && combat.IsPreparingAttack && combat.Move == AttackMove.Reap;
        float share = winding ? Mathf.Clamp01(combat.WindupElapsed / Mathf.Max(.05f, combat.Windup)) : 0f;
        if (winding && !charging)
        {
            // The scythe drawn back: a blade's ring, low.
            var looks = EliteLooks.Instance;
            PlayLow(looks != null ? EliteLooks.Pick(looks.reaperCharge) : null, Feet + Vector3.up * 1.6f, chargeVolume, .7f, 3f, 45f);
        }
        charging = winding;
        if (!shown) flashLeft = 0f;
        DrawRings(winding, share);
        Gather(winding, share);
    }

    private void DrawRings(bool winding, float share)
    {
        if (flashLeft > 0f) flashLeft -= Time.unscaledDeltaTime;
        if (!winding && flashLeft <= 0f)
        {
            if (ring != null) ring.enabled = false;
            if (hug != null) hug.enabled = false;
            return;
        }
        if (ring == null) ring = MakeRing("Reaper Ring");
        if (hug == null) hug = MakeRing("Reaper Hug");
        Vector3 center = Feet + Vector3.up * RingLift;
        if (winding)
        {
            // Out from its feet, slowly and then faster; red going hot, beating quicker as the turn nears.
            float radius = Mathf.Lerp(ringStart, ringEnd, Mathf.Pow(share, ringCurve));
            float beat = .5f + .5f * Mathf.Sin(Time.time * Mathf.Lerp(5f, 24f, share));
            var edge = Color.Lerp(new Color(.7f, .08f, .16f, .5f), new Color(1f, .28f, .38f, .95f), Mathf.Clamp01(share * .75f + beat * .25f * share));
            Circle(ring, center, radius, Mathf.Lerp(.14f, .3f, share), edge);
            // The safe ring under its feet: there all the charge long, a steady white breath.
            Circle(hug, center, hugReach, .07f, new Color(1f, 1f, 1f, .5f + .25f * Mathf.Sin(Time.time * 7f)));
            return;
        }
        // The turn: the full ring flashes white and thins away; the safe ring goes at once.
        float left = Mathf.Clamp01(flashLeft / FlashSeconds);
        Circle(ring, center, ringEnd, Mathf.Lerp(.05f, .42f, left), new Color(1f, 1f, 1f, .9f * left));
        hug.enabled = false;
    }

    private static void Circle(LineRenderer line, Vector3 center, float radius, float width, Color color)
    {
        line.enabled = true;
        line.widthMultiplier = width;
        line.startColor = line.endColor = color;
        for (int i = 0; i < RingPoints; i++)
        {
            float angle = 2f * Mathf.PI * i / RingPoints;
            line.SetPosition(i, center + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius));
        }
    }

    // A loop laid flat on the floor (TauntRing's way), so it reads as a circle on the ground from any height.
    private static LineRenderer MakeRing(string name)
    {
        var host = new GameObject(name) { hideFlags = HideFlags.DontSave };
        host.transform.rotation = Quaternion.Euler(90f, 0f, 0f);
        var line = host.AddComponent<LineRenderer>();
        line.useWorldSpace = true;
        line.loop = true;
        line.positionCount = RingPoints;
        line.numCornerVertices = 0;
        line.numCapVertices = 0;
        line.alignment = LineAlignment.TransformZ;
        line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        line.receiveShadows = false;
        line.sharedMaterial = FeelAssets.Particle(Texture2D.whiteTexture, false);
        line.enabled = false;
        return line;
    }

    // The wind gathering through the charge, louder and higher as it goes.
    private void Gather(bool winding, float share)
    {
        if (wind == null) wind = Loop(Wind(), 3f, 40f);
        if (wind == null) return;
        wind.volume = Mathf.MoveTowards(wind.volume, winding ? windVolume * Mathf.Lerp(.3f, 1f, share) : 0f, Time.deltaTime * 3f);
        wind.pitch = Mathf.Lerp(.8f, 1.45f, share);
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        charging = false;
        reelAt = -1f;
        flashLeft = 0f;
        if (ring != null) ring.enabled = false;
        if (hug != null) hug.enabled = false;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (ring != null) Destroy(ring.gameObject);
        if (hug != null) Destroy(hug.gameObject);
    }

    // A one-shot pitched down where it stands (the library's light swing made a heavy scythe), through the Combat volume.
    private static void PlayLow(AudioClip clip, Vector3 at, float volume, float pitch, float near, float far)
    {
        if (clip == null) return;
        var source = new GameObject("Reaper Sound").AddComponent<AudioSource>();
        source.transform.position = at;
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        source.rolloffMode = AudioRolloffMode.Logarithmic;
        source.minDistance = near;
        source.maxDistance = far;
        // Set before Bind, which keeps it as this source's own pitch under a slowdown (GameAudio.FollowWorldPitch).
        source.pitch = pitch;
        GameAudio.Bind(source, AudioChannel.Combat);
        GameAudio.SetGain(source, volume);
        source.clip = clip;
        source.Play();
        Destroy(source.gameObject, clip.length / Mathf.Max(.1f, pitch) + .1f);
    }

    // ---- Its look: a hood and a long scythe, boxes standing in until EliteLooks has props ----

    protected override void BuildHand(Transform holder)
    {
        // The shaft along +Z from the grip, an iron collar at its head, and the blade swept out to the side and back.
        var wood = new Color(.17f, .11f, .07f);
        var steel = new Color(.78f, .8f, .84f);
        Box(holder, new Vector3(0f, 0f, .35f), new Vector3(.035f, .035f, 1.75f), wood, default, .15f);
        Box(holder, new Vector3(0f, 0f, 1.2f), new Vector3(.06f, .06f, .07f), new Color(.1f, .1f, .12f), default, .6f);
        Box(holder, new Vector3(-.17f, 0f, 1.21f), new Vector3(.34f, .012f, .085f), steel, default, .85f);
        Box(holder, new Vector3(-.45f, 0f, 1.15f), new Vector3(.27f, .012f, .07f), steel, new Vector3(0f, 18f, 0f), .85f);
        Box(holder, new Vector3(-.66f, 0f, 1.03f), new Vector3(.2f, .012f, .05f), steel, new Vector3(0f, 42f, 0f), .85f);
    }

    protected override void BuildHead(Transform holder)
    {
        // A dark hood over the head, its peak, and the black of the opening where a face should be.
        var cloth = new Color(.07f, .06f, .09f);
        Box(holder, new Vector3(0f, .05f, -.02f), new Vector3(.3f, .34f, .31f), cloth, default, .05f);
        Box(holder, new Vector3(0f, .23f, -.06f), new Vector3(.17f, .09f, .2f), cloth, new Vector3(-18f, 0f, 0f), .05f);
        Box(holder, new Vector3(0f, .02f, .145f), new Vector3(.17f, .2f, .02f), Color.black);
    }

    // ---- Its sounds, made here when EliteLooks has none ----

    private static AudioClip Wind() => windClip != null ? windClip : windClip = MakeWind();
    private static AudioClip Whoosh() => whooshClip != null ? whooshClip : whooshClip = MakeWhoosh();

    // Two seconds of low wind, looped without a seam: soft noise swelling and falling over a cold hum.
    private static AudioClip MakeWind()
    {
        int total = Rate * 2, fade = Rate / 10;
        var random = new System.Random(2411);
        var data = new float[total + fade];
        float low = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)Rate;
            low += ((float)random.NextDouble() * 2f - 1f - low) * .04f;
            float swell = .6f + .4f * Mathf.Sin(2f * Mathf.PI * 1.5f * t);
            float hum = Mathf.Sin(2f * Mathf.PI * 70f * t) * (.5f + .5f * Mathf.Sin(2f * Mathf.PI * .5f * t));
            data[i] = low * 3f * swell + hum * .18f;
        }
        // The tail laid over the head, so the loop's end runs straight into its start.
        for (int i = 0; i < fade; i++)
        {
            float k = i / (float)fade;
            data[i] = data[i] * k + data[total + i] * (1f - k);
        }
        return Clip("Reaper Wind", data, total, .7f);
    }

    // A heavy whoosh: noise through a band that sweeps down as the blade passes, quick in and slower out.
    private static AudioClip MakeWhoosh()
    {
        int total = (int)(Rate * .6f);
        var random = new System.Random(977);
        var data = new float[total];
        float low = 0f, band = 0f;
        for (int i = 0; i < total; i++)
        {
            float t = i / (float)total;
            float noise = (float)random.NextDouble() * 2f - 1f;
            // Two one-pole filters a sweeping distance apart make a moving band.
            float upper = Mathf.Lerp(.35f, .05f, t), lower = Mathf.Lerp(.08f, .01f, t);
            band += (noise - band) * upper;
            low += (noise - low) * lower;
            float envelope = t < .12f ? t / .12f : Mathf.Pow(1f - (t - .12f) / .88f, 2f);
            data[i] = (band - low) * envelope;
        }
        return Clip("Reaper Whoosh", data, total, .9f);
    }

    private static AudioClip Clip(string name, float[] data, int length, float peak)
    {
        float top = 0f;
        for (int i = 0; i < length; i++) top = Mathf.Max(top, Mathf.Abs(data[i]));
        var samples = new float[length];
        for (int i = 0; i < length; i++) samples[i] = top > 0f ? data[i] * peak / top : 0f;
        var clip = AudioClip.Create(name, length, 1, Rate, false);
        clip.SetData(samples, 0);
        clip.hideFlags = HideFlags.HideAndDontSave;
        return clip;
    }
}
