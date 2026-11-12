using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// The ninja, an elite from the user's memo of 2026-11-11 (item 23: "닌자처럼 등 뒤로 이동해서 공격하는 애. 패링하려면
/// 재빠르게 뒤를 돌아서 상대의 공격을 튕겨내야 함"). A lean zombie with a blade and a violet light, quick on its feet. It
/// never trades plain blows (HoldsSwing): it stalks, and every attack it makes comes out of its smoke. Seeing someone
/// within blinkRange, however near, it vanishes into smoke with a whoosh, steps out of fresh smoke behind them (else at
/// their side or a back corner, never in front) with the ring of a drawn blade (the warning), and strikes from there
/// (AttackMove.Backstab, backstabWindup). That blow can only be parried by someone who has turned to face it (within
/// parryAngle; dodging works as ever): turned round in time and parried, it is thrown off balance, open to an execution,
/// and the parry pays parryReward (the user's rule: a hit is a plus, a miss is only what the blow does). On the host it
/// thinks; the vanish shows on every PC through its mood (ZombieMind.Rouse: Rousing while hidden), so no new message is
/// sent. Until 0.8.195 it fought up close as any zombie and vanished only from 3.5 m out, so once it was on someone (a
/// /spawn puts it 3 m ahead) it never vanished again and a plain parry from the front beat it (user, 2026-11-12: "닌자가
/// 공격할 때 내 등 뒤로 순간이동하지 않는데? 걍 정면에서 패링하면 끝남"). Numbers: Tables/ZombieNinja; its kind's row in
/// ZombieKinds.
/// </summary>
[DisallowMultipleComponent]
public sealed class ZombieNinja : ZombieElite
{
    public const string TableName = "ZombieNinja", TableRow = "Default";
    private const int Rate = 44100;
    private static AudioClip whoosh, ring;

    [Header("Blink")]
    [SerializeField, Min(0f), Tooltip("It vanishes for someone it sees no farther than this (m), however near: it has no other attack.")]
    private float blinkRange = 14f;
    [SerializeField, Min(0f), Tooltip("From one blow out of the smoke to the next vanish (s): meanwhile it only stalks.")]
    private float blinkCooldown = 2.2f;
    [SerializeField, Min(0f), Tooltip("From appearing to its first vanish (s).")]
    private float blinkFirst = 1f;
    [SerializeField, Min(.1f), Tooltip("Nowhere to come out near its quarry and too far to strike from where it stands: it tries again after this (s).")]
    private float blinkRetry = .8f;
    [SerializeField, Min(.05f), Tooltip("How long it is gone in the smoke before it moves (s).")]
    private float vanishSeconds = .35f;
    [SerializeField, Min(0f), Tooltip("And how long after it moves before it shows again (s): the other PCs' copies catch up meanwhile.")]
    private float settleSeconds = .3f;
    [SerializeField, Min(.5f), Tooltip("How far behind its quarry it comes out (m).")]
    private float behind = 1.6f;
    [SerializeField, Min(0f), Tooltip("Further above or below its quarry than this (m), it doesn't vanish for them.")]
    private float blinkHeight = 1.5f;
    [Header("The blow from behind")]
    [SerializeField, Min(.1f), Tooltip("From stepping out of the smoke to the blow (s): the time to turn and parry.")]
    private float backstabWindup = .7f;
    [SerializeField, Range(10f, 90f), Tooltip("It is parried only by someone facing it within this angle (degrees either side).")]
    private float parryAngle = 55f;
    [SerializeField, Min(0f), Tooltip("Parried from behind, it loses this much posture: enough breaks it, open to an execution.")]
    private float parryBreak = 80f;
    [SerializeField, Min(0f), Tooltip("And reels this long (s).")]
    private float parryStun = 2f;
    [SerializeField, Min(0f), Tooltip("Money for parrying its blow from behind.")]
    private int parryReward = 100;
    [Header("Look")]
    [SerializeField] private Color glowColor = new Color(.62f, .3f, 1f);
    [SerializeField, Min(.05f)] private float glowSize = .3f;
    [SerializeField] private Color smokeColor = new Color(.16f, .15f, .19f, .85f);
    [SerializeField, Min(.1f), Tooltip("The smoke puff's size (m).")]
    private float smokeSize = 1.1f;

    private enum Phase { Chase, Vanish, Settle }

    // The ways round its quarry it may come out (degrees off straight behind, the way they face turned round), each tried
    // on both sides, the nearer side first: behind, the back corners, the sides, the front corners. Never in front.
    private static readonly float[] Turns = { 0f, 30f, 60f, 90f, 120f };
    // How far from them, off the table's `behind` (m): there first, then a little nearer, then a little farther.
    private static readonly float[] Reaches = { 0f, -.4f, .6f };

    private Phase phase;
    private PlayerMovement quarry;
    private float left, cooldownLeft;
    // It came out of its smoke near its quarry (Blink found a place); else where it went in.
    private bool came;
    // Every PC: whether it shows hidden (its mood went from awake to rousing: a vanish, not a waking), and the body's
    // renderers it hid.
    private bool hidden;
    private ZombieMood lastMood = ZombieMood.Awake;
    private Renderer[] hiddenParts;

    protected override ZombieKind Kind => ZombieKind.Ninja;
    public override bool Driving => phase != Phase.Chase;
    // Its only attack is the one out of its smoke: a plain swing would be met by a plain parry from the front.
    public override bool HoldsSwing => Mine;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget() => whoosh = ring = null;

    protected override void Awake()
    {
        base.Awake();
        GameTables.Bind(TableName, TableRow, this);
        cooldownLeft = blinkFirst;
    }

    internal override float WindupOf(AttackMove move) => move == AttackMove.Backstab ? backstabWindup : 0f;

    // ---- The host: when it vanishes, where it comes out, and the blow ----

    private void Update()
    {
        if (!Mine || !Runs) { phase = Phase.Chase; return; }
        float dt = Time.deltaTime * (network != null ? network.Pace : 1f);
        if (dt <= 0f) return;
        if (!CanAct())
        {
            // Shoved, knocked down, held or dead in its smoke: it comes out where it is, and tries again later.
            if (phase != Phase.Chase) { phase = Phase.Chase; cooldownLeft = blinkCooldown * .5f; }
            return;
        }
        switch (phase)
        {
            case Phase.Chase: Watch(dt); break;
            case Phase.Vanish:
                if ((left -= dt) > 0f) break;
                came = Blink();
                phase = Phase.Settle;
                left = settleSeconds;
                break;
            case Phase.Settle:
                // Out when its rouse is over too: the swing waits for that (EnemyCombatAI), and would cancel the blow.
                if ((left -= dt) > 0f || !mind.Ready) break;
                phase = Phase.Chase;
                Strike();
                break;
        }
    }

    // Chasing: someone in sight within reach of a blink, however near, and the last blow long enough ago: into the smoke.
    private void Watch(float dt)
    {
        cooldownLeft -= dt;
        if (cooldownLeft > 0f || mind == null || !mind.Ready || combat == null || combat.IsPreparingAttack) return;
        var player = targeting != null ? targeting.Player : null;
        if (player == null || player.IsDead || player.IsParkour) return;
        Vector3 to = Flat(player.transform.position - transform.position);
        if (to.magnitude > blinkRange || Mathf.Abs(player.transform.position.y - Feet.y) > blinkHeight || !Sees(player)) return;
        phase = Phase.Vanish;
        left = vanishSeconds;
        quarry = player;
        came = false;
        Halt();
        // Hidden on every PC while it rouses (LateUpdate); it turns to them meanwhile and stands still.
        mind.Rouse(player, vanishSeconds + settleSeconds);
    }

    /// <summary>
    /// Out of the smoke near its quarry (the host): on its own walk mesh, reachable on foot straight from where they stand
    /// (no wall, gap or other floor between), nothing solid between at chest height, and the agent really put there.
    /// First straight behind the way they face, then turning out to the back corners, the sides and the front corners, the
    /// nearer side first, each at `behind`, a little nearer and a little farther. False when nowhere fits: it is still
    /// where it went in (Strike decides from there).
    /// </summary>
    private bool Blink()
    {
        if (quarry == null || quarry.IsDead || agent == null || !agent.isActiveAndEnabled) return false;
        // The agent's own mesh: a spot on another agent type's mesh can't be warped to.
        var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
        Vector3 feet = quarry.transform.position;
        if (!NavMesh.SamplePosition(feet, out NavMeshHit them, 1f, filter)) return false;
        Vector3 back = -Flat(quarry.transform.forward);
        if (back.sqrMagnitude < .01f) back = Flat(transform.position - feet);
        if (back.sqrMagnitude < .01f) back = Vector3.back;
        back.Normalize();
        // The side it was already on round them goes first.
        float side = Vector3.Dot(Vector3.Cross(back, Flat(transform.position - feet)), Vector3.up) >= 0f ? 1f : -1f;
        Vector3 chest = feet + Vector3.up * 1.2f;
        for (int i = 0; i < Turns.Length * 2 - 1; i++)
        {
            float turn = i == 0 ? 0f : Turns[(i + 1) / 2] * (i % 2 == 1 ? side : -side);
            Vector3 way = Quaternion.Euler(0f, turn, 0f) * back;
            foreach (float reach in Reaches)
            {
                float distance = Mathf.Max(.9f, behind + reach);
                if (!NavMesh.SamplePosition(them.position + way * distance, out NavMeshHit spot, .6f, filter)) continue;
                if (Mathf.Abs(spot.position.y - them.position.y) > 1f || Flat(spot.position - them.position).magnitude < .8f) continue;
                if (NavMesh.Raycast(them.position, spot.position, out _, filter)) continue;
                if (Physics.Linecast(chest, spot.position + Vector3.up * 1.2f, WorldMask, QueryTriggerInteraction.Ignore)) continue;
                if (!agent.Warp(spot.position)) continue;
                Vector3 face = Flat(feet - spot.position);
                if (face.sqrMagnitude > 1e-4f) transform.rotation = Quaternion.LookRotation(face);
                return true;
            }
        }
        return false;
    }

    // Out of the smoke: the blow, from where it came out. Nowhere to come out (cornered on a ledge, say) and its quarry
    // still within a step of where it stands, the same blow from there, its cue and all; farther, it stalks on and tries
    // again soon. Never a plain swing in place of it (HoldsSwing).
    private void Strike()
    {
        bool near = quarry != null && !quarry.IsDead
            && (came || Flat(quarry.transform.position - transform.position).magnitude <= behind + .6f);
        if (near)
        {
            combat.BeginSpecial(quarry, AttackMove.Backstab);
            cooldownLeft = blinkCooldown;
        }
        else cooldownLeft = blinkRetry;
    }

    // ---- Every PC: hidden in smoke, coming out, its light ----

    private void LateUpdate()
    {
        var mood = mind != null ? mind.Mood : ZombieMood.Awake;
        bool alive = Mine && health != null && health.IsAlive;
        // A vanish is an awake ninja rousing; one woken from calm rouses too, and shows.
        bool vanished = alive && mood == ZombieMood.Rousing && (hidden || lastMood == ZombieMood.Awake);
        if (mood != ZombieMood.Rousing) lastMood = mood;
        if (vanished && !hidden) Vanish();
        else if (!vanished && hidden) Appear(alive);
        if (hidden) { HideLight(); return; }
        ShowElite(glowColor, glowSize, HumanBodyBones.Head, HumanBodyBones.Neck);
    }

    private void Vanish()
    {
        hidden = true;
        // Gone into the smoke is gone: no shot or blow lands on it until it comes out behind someone.
        if (health != null) health.Untouchable = true;
        Vector3 at = transform.position + Vector3.up * 1f;
        Puff(at, smokeColor, smokeSize, 18);
        var looks = EliteLooks.Instance;
        PlayAt(Or(looks != null ? EliteLooks.Pick(looks.ninjaVanish) : null, Whoosh()), at, .8f, 2f, 30f);
        hiddenParts = body != null && body.Model != null ? body.Model.GetComponentsInChildren<Renderer>() : null;
        Show(false);
    }

    // Out of the smoke: a puff where it stands now and the ring of a drawn blade, loud right behind whoever it came for.
    private void Appear(bool alive)
    {
        hidden = false;
        if (health != null) health.Untouchable = false;
        Show(true);
        hiddenParts = null;
        if (!alive) return;
        Vector3 at = transform.position + Vector3.up * 1f;
        Puff(at, smokeColor, smokeSize * .8f, 14);
        var looks = EliteLooks.Instance;
        PlayAt(Or(looks != null ? EliteLooks.Pick(looks.ninjaAppear) : null, Ring()), at, 1f, 3f, 35f);
    }

    private void Show(bool on)
    {
        if (hiddenParts == null) return;
        foreach (var part in hiddenParts) if (part != null) part.enabled = on;
    }

    protected override void OnDisable()
    {
        base.OnDisable();
        if (hidden) { hidden = false; Show(true); }
        if (health != null) health.Untouchable = false;
    }

    // ---- Its blow from behind ----

    // Only someone turned to face it can turn it aside: the parry is the turn (memo 23).
    internal override bool GuardableBy(PlayerMovement victim, AttackMove move)
    {
        if (move != AttackMove.Backstab || victim == null) return true;
        var view = victim.IsLocal && Camera.main != null ? Camera.main.transform.forward : victim.transform.forward;
        Vector3 facing = Flat(view), toward = Flat(transform.position - victim.transform.position);
        return facing.sqrMagnitude > 1e-4f && toward.sqrMagnitude > 1e-4f && Vector3.Angle(facing, toward) <= parryAngle;
    }

    // Turned round and parried from behind: thrown off balance (an execution's chance) and paid for it.
    internal override void Parried(PlayerMovement by)
    {
        if (combat == null || combat.Move != AttackMove.Backstab || by == null || !by.IsLocal) return;
        if (melee != null) melee.ApplyPostureDamage(parryBreak, parryStun);
        if (parryReward > 0) CombatFeedHUD.Award(Texts.Get("feed.score.ninja_parry"), parryReward);
    }

    protected override void BuildHand(Transform holder)
    {
        // A katana: wrapped grip, a dark guard, a long bright blade along +Z.
        Box(holder, new Vector3(0f, 0f, -.03f), new Vector3(.032f, .032f, .24f), new Color(.08f, .06f, .07f));
        Box(holder, new Vector3(0f, 0f, .1f), new Vector3(.075f, .018f, .075f), new Color(.35f, .27f, .12f), default, .6f);
        Box(holder, new Vector3(0f, 0f, .47f), new Vector3(.012f, .034f, .72f), new Color(.78f, .8f, .84f), default, .85f);
    }

    // ---- Sounds made here when EliteLooks has none ----

    // Cloth and air torn past: noise swelling and dying fast, falling in pitch.
    private static AudioClip Whoosh()
    {
        if (whoosh != null) return whoosh;
        var data = new float[(int)(Rate * .45f)];
        var random = new System.Random(23);
        float low = 0f, band = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float share = i / (float)data.Length;
            low += ((float)random.NextDouble() * 2f - 1f - low) * Mathf.Lerp(.35f, .06f, share);
            band += (low - band) * .5f;
            data[i] = (low - band) * Mathf.Sin(Mathf.PI * Mathf.Sqrt(share)) * (1f - share);
        }
        return whoosh = Made("Ninja Vanish", data, .7f);
    }

    // A blade drawn: a bright metallic ring, a few high partials beating against each other and dying away.
    private static AudioClip Ring()
    {
        if (ring != null) return ring;
        var data = new float[(int)(Rate * .9f)];
        float[] partials = { 2210f, 3127f, 4650f, 6020f };
        float[] levels = { .5f, .35f, .25f, .15f };
        var random = new System.Random(77);
        float scrape = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)Rate;
            float sample = 0f;
            for (int p = 0; p < partials.Length; p++)
                sample += Mathf.Sin(2f * Mathf.PI * partials[p] * t * (1f + .002f * Mathf.Sin(2f * Mathf.PI * 5f * t))) * levels[p] * Mathf.Exp(-t / (.18f + .05f * p));
            // The scrape of the blade leaving its sheath, the first moment.
            scrape += ((float)random.NextDouble() * 2f - 1f - scrape) * .7f;
            sample += scrape * Mathf.Clamp01(1f - t / .12f) * .35f;
            data[i] = sample * Mathf.Min(1f, t / .004f);
        }
        return ring = Made("Ninja Appear", data, .85f);
    }

    private static AudioClip Made(string name, float[] data, float peak)
    {
        float top = 0f;
        foreach (float sample in data) top = Mathf.Max(top, Mathf.Abs(sample));
        if (top > 0f) for (int i = 0; i < data.Length; i++) data[i] *= peak / top;
        var clip = AudioClip.Create(name, data.Length, 1, Rate, false);
        clip.SetData(data, 0);
        clip.hideFlags = HideFlags.HideAndDontSave;
        return clip;
    }
}
