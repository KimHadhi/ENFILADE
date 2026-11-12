using UnityEngine;

/// <summary>
/// The brute's counter (user, 2026-11-12: "빠르게 다리를 쏴서 기게 만든 다음에 누운 거구의 머리를 노려서 빠르게 녹이는
/// 명확한 대처법이 있으면 좋을 것 같아"), for a kind whose legs give way at a number of their own (ZombieKinds legBreak):
/// shoot a leg and it falls, then its head melts. Every PC shows all of it from the same hits (ZombieGore.LegWorn and
/// Crippled are raised once per hit on every PC), so nothing of it goes over the network:
/// - each hit on a leg cracks (SoundCues Brute/LegHit; Brute/LegCrack, heavier, once the leg is half gone);
/// - half gone, the leg is cracked: it limps, its body sinking and leaning onto that leg once a stride, and goes slower
///   (ZombieKinds limpPace, ZombieType.Limp; only the host's pace moves it);
/// - the hit that breaks it: a snap and a pained bellow, a heavy fall (ZombieBody.Crawl.cs knocks it down) whose landing
///   thuds and shakes the view of anyone near, an alert band pointing at it, and the shooter shouts for the head;
/// - crawling, its head is the weak spot (ZombieKinds crawlHead, taken in EnemyHealth): the blood of a body dragging
///   itself along rushes into its head, shown as a red glow beating like a heart over the head, flashing white with a
///   crack at each hit there.
/// The arrival's own shout for the legs comes from the player nearest it (ZombieBody.Brute.cs). Numbers: Tables/ZombieLegs.
/// </summary>
public sealed partial class ZombieBody
{
    public const string LegsTable = "ZombieLegs", LegsRow = "Default";
    /// <summary>Tables/SoundCues rows: a hit on a leg, one on a cracked leg, the break, the body landing, a hit on the weak head.</summary>
    public const string LegHitCue = "Brute/LegHit", LegCrackCue = "Brute/LegCrack", LegBreakCue = "Brute/LegBreak",
        FallCue = "Brute/Fall", WeakHitCue = "Brute/WeakHit";
    // How far the break and the landing carry (m): a boss going down is heard across the street.
    private const float BreakHeard = 35f, FallHeard = 45f;

    [SerializeField, Range(0f, 1f), Tooltip("How far a leg must have gone toward giving way before it limps and its hits crack heavier.")]
    private float limpFrom = .5f;
    [SerializeField, Min(.01f), Tooltip("How long the limp takes to come in (s).")]
    private float limpIn = .4f;
    [SerializeField, Min(0f), Tooltip("How far the body sinks each stride onto the cracked leg (m at the pack's size; a bigger body sinks as much more).")]
    private float limpDip = .07f;
    [SerializeField, Min(0f), Tooltip("How far it leans onto the cracked leg as it sinks (degrees).")]
    private float limpLean = 6f;
    [SerializeField, Min(0f), Tooltip("From the breaking hit to the body landing (s): the thud and the shake.")]
    private float fallLands = .45f;
    [SerializeField, Min(0f), Tooltip("The view shakes fully within this distance of where it lands (m).")]
    private float fallShakeNear = 8f;
    [SerializeField, Min(0f), Tooltip("And not at all past this (m).")]
    private float fallShakeFar = 30f;
    [SerializeField, Min(0f), Tooltip("How hard the landing kicks the view at its nearest (degrees of impulse).")]
    private float fallShake = 6f;
    [SerializeField, Min(0f), Tooltip("How long the alert's mark stays on it after it falls (real s).")]
    private float fallMark = 5f;
    [SerializeField, Min(0f), Tooltip("From the breaking hit to the shooter's shout for the head (s).")]
    private float headShoutAfter = .5f;
    [SerializeField, Min(0f), Tooltip("From a brute's coming to the shout for its legs by the player nearest it (s).")]
    private float legShoutAfter = 1.2f;
    [SerializeField, Tooltip("The weak spot's glow (#RRGGBBAA; the alpha is its strength).")]
    private Color weakColor = new Color(1f, .22f, .1f, .85f);
    [SerializeField, Min(0f), Tooltip("The glow's size between beats (m at the pack's size; a bigger body's is as much bigger).")]
    private float weakSize = .45f;
    [SerializeField, Min(0f), Tooltip("Heartbeats a second.")]
    private float weakBeat = 1.4f;
    [SerializeField, Min(0f), Tooltip("How much the glow swells at a beat (share of its size).")]
    private float weakSwell = .45f;
    [SerializeField, Min(.01f), Tooltip("A hit on the weak spot flashes the glow white and big this long (s).")]
    private float weakFlash = .12f;
    [SerializeField, Min(0f), Tooltip("How much bigger the flash is (share of its size).")]
    private float weakPop = 1.2f;

    private EliteLight weakLight;
    private float limpWeight, limpSign, weakFlashLeft, fallLandsAt = -1f, headShoutAt = -1f, legShoutAt = -1f;
    // The limp this frame: how far the body sinks (m) and leans (degrees), put on its place by Rise (ZombieBody.Leap.cs)
    // and on its turn by FaceGrabs (ZombieBody.Arms.cs), which set them outright every frame, so nothing adds up.
    private float limpDrop, limpTilt;
    private bool limping, watchingLegs, watchingWeak;

    private void WatchLegs()
    {
        GameTables.Bind(LegsTable, LegsRow, this);
        if (!watchingLegs) ZombieGore.LegWorn += OnLegWorn;
        watchingLegs = true;
    }

    private void StopWatchingLegs()
    {
        if (watchingLegs) ZombieGore.LegWorn -= OnLegWorn;
        if (watchingWeak) CombatDamageEvents.Damaged -= OnStruck;
        watchingLegs = watchingWeak = false;
        weakLight?.Remove();
        weakLight = null;
    }

    // A leg of this zombie took a hit (share: how far it has gone toward giving way; 1 broke it).
    private void OnLegWorn(EnemyHealth enemy, DamageHit hit, float share)
    {
        if (enemy != enemyHealth || enemyHealth == null || !enemyHealth.IsAlive || Model == null) return;
        if (share >= 1f)
        {
            LegBroke(hit);
            return;
        }
        bool cracked = share >= limpFrom;
        BodyBreakSounds.Play(cracked ? LegCrackCue : LegHitCue, hit.Point);
        if (!cracked || limping) return;
        // Half gone: it favours that leg from now on, leaning onto it as it sinks.
        limping = true;
        limpSign = hit.Part == BodyPart.LeftUpperLeg || hit.Part == BodyPart.LeftLowerLeg ? 1f : -1f;
        if (type != null) type.Limp();
    }

    // The leg gave way: ZombieGore cut it and Crippled already brought the body down into its crawl (ZombieBody.Crawl.cs).
    // Here: the snap and a bellow, the landing a moment later, the alert, and the shooter's own shout for the head.
    private void LegBroke(in DamageHit hit)
    {
        limping = false;
        BodyBreakSounds.Play(LegBreakCue, hit.Point, BreakHeard);
        // Its roar, pitched up and cut short by the pain.
        FeelAudio.PlayClipAt(Roar(), transform.position + Vector3.up * 1.4f * grown, .85f, 1.35f, 60f);
        fallLandsAt = Time.time + fallLands;
        var health = enemyHealth;
        AlertHUD.Danger(Texts.Get("hud.alert.brute.down"), Head != null ? Head : transform, fallMark,
            () => health == null || !health.IsAlive);
        if (ShotByMe(hit)) headShoutAt = Time.time + headShoutAfter;
    }

    // This PC's own player landed it (each PC shouts only for its own player; the others hear it through the network).
    private static bool ShotByMe(in DamageHit hit)
        => hit.Source != null && hit.Source.GetComponentInParent<PlayerMovement>() is PlayerMovement player && player.IsLocal;

    /// <summary>A brute came: the player nearest it calls for its legs a moment later (ZombieBody.Brute.cs Arrive).</summary>
    private void CallForLegs() => legShoutAt = Time.time + legShoutAfter;

    /// <summary>
    /// From LateUpdate (ZombieBody.Down.cs), after the pose and even once fallen as a corpse: the limp for the next frame,
    /// the landing, the shouts, and the weak spot's glow by the head as it now stands.
    /// </summary>
    private void LegsLate()
    {
        float dt = Time.deltaTime;
        bool alive = enemyHealth != null && enemyHealth.IsAlive && Model != null;
        Limp(alive, dt);
        float now = Time.time;
        if (fallLandsAt >= 0f && now >= fallLandsAt)
        {
            fallLandsAt = -1f;
            var hips = human != null ? human[(int)HumanBodyBones.Hips] : null;
            Vector3 at = hips != null ? hips.position : transform.position;
            BodyBreakSounds.Play(FallCue, at, FallHeard);
            ShakeNear(at);
        }
        if (headShoutAt >= 0f && now >= headShoutAt)
        {
            headShoutAt = -1f;
            if (alive) PlayerCallouts.Spotted(enemyHealth);
        }
        if (legShoutAt >= 0f && now >= legShoutAt)
        {
            legShoutAt = -1f;
            if (alive && NearestIsMine()) PlayerCallouts.Spotted(enemyHealth);
        }
        WeakSpot(alive, dt);
    }

    // Once a stride (the walk's phase runs both feet's steps) the cracked leg takes the weight: the body sinks and leans
    // onto it, standing still not at all. Down, crawling or dead: none.
    private void Limp(bool alive, float dt)
    {
        bool on = limping && alive && !crawling && !down;
        limpWeight = on ? Mathf.MoveTowards(limpWeight, 1f, dt / limpIn) : 0f;
        if (limpWeight <= 0f)
        {
            limpDrop = limpTilt = 0f;
            return;
        }
        float stride = Mathf.Sin(2f * Mathf.PI * phase);
        float sink = stride > 0f ? stride * stride : 0f;
        float moving = Mathf.Clamp01(speed / Mathf.Max(.1f, walkSpeed * .5f));
        float amount = limpWeight * sink * moving;
        limpDrop = limpDip * grown * amount;
        limpTilt = limpLean * limpSign * amount;
    }

    // The landing kicks the view of this PC's player by how near it is.
    private void ShakeNear(Vector3 at)
    {
        var mine = PlayerRoster.Local;
        if (mine == null || mine.IsDead) return;
        float near = 1f - Mathf.InverseLerp(fallShakeNear, fallShakeFar, Vector3.Distance(mine.transform.position, at));
        if (near <= 0f) return;
        var view = mine.GetComponentInChildren<PlayerCameraEffects>();
        if (view != null)
            view.ApplyCameraImpulse(new Vector3(-fallShake * near, 0f, (Random.value < .5f ? -.5f : .5f) * fallShake * near), 2f * near);
    }

    // This PC's player is the living player nearest it: one voice calls it, not every PC's at once.
    private bool NearestIsMine()
    {
        var mine = PlayerRoster.Local;
        if (mine == null || mine.IsDead) return false;
        float own = (mine.transform.position - transform.position).sqrMagnitude;
        foreach (var player in PlayerRoster.All)
            if (player != null && player != mine && !player.IsDead && (player.transform.position - transform.position).sqrMagnitude < own)
                return false;
        return true;
    }

    // Crawling with a head that gives way: a glow beating over the head (EliteLight turns it to the camera, a little
    // toward it so the body doesn't hide it), flashing at each hit there. Hits are watched only from then on.
    private void WeakSpot(bool alive, float dt)
    {
        bool weak = alive && type != null && type.WeakHead;
        if (weak && !watchingWeak)
        {
            CombatDamageEvents.Damaged += OnStruck;
            watchingWeak = true;
        }
        if (!weak)
        {
            weakLight?.Hide();
            return;
        }
        if (weakLight == null) weakLight = new EliteLight(this, HumanBodyBones.Head, HumanBodyBones.Neck);
        weakFlashLeft = Mathf.Max(0f, weakFlashLeft - dt);
        float beat = Heartbeat(Time.time * weakBeat), flash = weakFlashLeft / weakFlash;
        Color color = Color.Lerp(weakColor, Color.white, flash * .8f);
        color.a = Mathf.Lerp(weakColor.a * (.6f + .4f * beat), 1f, flash);
        weakLight.Show(color, weakSize * grown * (1f + weakSwell * beat + weakPop * flash));
    }

    // A heart's beat over one period (t's fraction): a strong pulse, a softer one just after, then rest. 0 to 1.
    private static float Heartbeat(float t)
    {
        float f = t - Mathf.Floor(t);
        float lub = Mathf.Exp(-Mathf.Pow((f - .08f) / .05f, 2f));
        float dub = .6f * Mathf.Exp(-Mathf.Pow((f - .3f) / .06f, 2f));
        return Mathf.Clamp01(lub + dub);
    }

    // A hit on its weak head, on any PC: the glow flashes and the skull cracks.
    private void OnStruck(CombatDamageReport report)
    {
        if (report.Target != enemyHealth || type == null || !type.WeakHead) return;
        var hit = report.Hit;
        if (hit.Part != BodyPart.Head && !(hit.IsCritical && hit.Method == DamageMethod.Bullet)) return;
        weakFlashLeft = weakFlash;
        BodyBreakSounds.Play(WeakHitCue, hit.Point);
    }
}
