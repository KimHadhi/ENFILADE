using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stage C-16, the first of the elites (user, 2026-11-10: "a charger and an exploder for now, more later"): the
/// exploder. One of the pack, a little bloated, with a light burning in its chest that every PC sees from afar, so it
/// can be picked out as a runner is by its eyes. It doesn't claw: it walks into the players, and within fuseRange of
/// one its fuse runs, the light beating faster and its body flashing, until after fuseSeconds it bursts. It bursts
/// when it dies too, however it dies. Shot from afar it takes the pack round it along (zombies in reach are hurt by how
/// near they stand and knocked flat, the kills credited to whoever shot it), which is why it is worth picking out;
/// next to a player it hurts and throws them. The host runs the fuse and ends it; every PC sees the death, plays the
/// burst there (CanBlastEffect, with no fire left behind) and hurts and throws its own player, a player's health being
/// their own PC's. A takedown or an execution defuses it instead (user, 2026-11-12: "자폭 좀비 암살할 땐 안 터지면 안
/// 됨? 내가 암살하고 터져서 뒤로 자빠지네"): the blade finds the brainstem before the gas sac can light, so it only
/// hisses and sags, harms nobody, and the one who did it is paid defuseReward. Every PC reads how it died from the
/// killing blow itself (CombatDamageEvents, raised once per hit on every PC; DamageKind.Execution), so all agree: the
/// death event (EnemyHealth.Died) only fires where the kill was judged, and the other players' PCs never burst it at all.
/// On every zombie, as ZombieRunner is; it does nothing for other kinds. Numbers: Tables/ZombieExploder.
/// </summary>
[DisallowMultipleComponent]
public sealed class ZombieExploder : MonoBehaviour
{
    public const string TableName = "ZombieExploder", TableRow = "Default";
    private const int SampleRate = 44100;
    private static readonly List<EnemyHealth> near = new List<EnemyHealth>();
    private static AudioClip boom;

    [Header("Fuse")]
    [SerializeField, Min(0f), Tooltip("A player this near sets its fuse running (m).")]
    private float fuseRange = 2.4f;
    [SerializeField, Min(.1f), Tooltip("How long the fuse runs before it bursts (s). It runs back at half the pace while nobody is near.")]
    private float fuseSeconds = 1f;
    [Header("Burst")]
    [SerializeField, Min(0f), Tooltip("How far the burst reaches (m).")]
    private float blastRadius = 4.5f;
    [SerializeField, Min(0f), Tooltip("What it does to a player at its middle.")]
    private float playerDamage = 45f;
    [SerializeField, Min(0f), Tooltip("And at its edge.")]
    private float playerEdgeDamage = 12f;
    [SerializeField, Min(0f), Tooltip("It throws a player this fast away from it at its middle (m/s), less toward the edge.")]
    private float knock = 8f;
    [SerializeField, Min(0f), Tooltip("And this fast up (m/s).")]
    private float knockUp = 4f;
    [SerializeField, Min(0f), Tooltip("What it does to a zombie at its middle.")]
    private float zombieDamage = 220f;
    [SerializeField, Min(0f), Tooltip("And at its edge.")]
    private float zombieEdgeDamage = 60f;
    [SerializeField, Min(0f), Tooltip("How long it knocks zombies down (s). One that stands firm (a brute) is only hurt.")]
    private float zombieKnockdown = 2.2f;
    [SerializeField, Min(0f), Tooltip("A view within this of the burst shakes, harder nearer (m).")]
    private float shakeReach = 16f;
    [SerializeField, Min(0f), Tooltip("What it does to a Support's barricade at its middle, before Support.barricadeBlow (it lights its fuse at the planks: ZombieBreach).")]
    private float barricadeDamage = 450f;
    [SerializeField, Min(0f), Tooltip("And at its edge.")]
    private float barricadeEdgeDamage = 150f;
    [Header("The light in its chest")]
    [SerializeField, Min(.05f), Tooltip("How big the light is (m).")]
    private float glowSize = .7f;
    [SerializeField, Tooltip("Its colour while it only walks.")]
    private Color glowColor = new Color(1f, .5f, .12f);
    [SerializeField, Min(0f), Tooltip("How often it beats while it only walks (a second).")]
    private float glowBeat = 1.4f;
    [SerializeField, Min(0f), Tooltip("How often at the end of the fuse (a second).")]
    private float fuseBeat = 11f;
    [Header("Defused (a takedown or an execution)")]
    [SerializeField, Min(0), Tooltip("Money for the one whose takedown or execution defused it, on top of the kill's.")]
    private int defuseReward = 40;
    [SerializeField, Min(.1f), Tooltip("The puff of gas it lets out as it sags (m).")]
    private float gasSize = .9f;
    [SerializeField, Tooltip("The gas's colour (its alpha is how thick).")]
    private Color gasColor = new Color(.66f, .72f, .35f, .75f);

    private EnemyHealth health;
    private EnemyNetwork network;
    private ZombieType type;
    private ZombieMind mind;
    private MeleeCombatTarget melee;
    private EliteLight marker;
    private GameObject lastSource;
    private float fuse, beat, nextFlash;
    private bool watching, ended;
    private static AudioClip hiss;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget() => boom = hiss = null;

    private bool IsExploder => type != null && type.Kind == ZombieKind.Exploder && !type.Crawls;
    private bool Runs => network == null || !network.IsCopy;

    private void Awake()
    {
        health = GetComponent<EnemyHealth>();
        network = GetComponent<EnemyNetwork>();
        type = GetComponent<ZombieType>();
        mind = GetComponent<ZombieMind>();
        melee = GetComponent<MeleeCombatTarget>();
        GameTables.Bind(TableName, TableRow, this);
        if (health != null) health.Died += OnDied;
    }

    private void OnDestroy()
    {
        if (health != null) health.Died -= OnDied;
        Watch(false);
        marker?.Remove();
    }

    // Only an exploder listens for who hurts it: every zombie carries this component, and most are not one.
    private void Watch(bool on)
    {
        if (on == watching) return;
        watching = on;
        if (on) CombatDamageEvents.Damaged += OnDamaged;
        else CombatDamageEvents.Damaged -= OnDamaged;
    }

    private void OnDisable() => marker?.Hide();

    // Who last hurt it: the pack its burst takes along is theirs. The blow that kills it ends it here, on every PC alike:
    // a takedown's or an execution's defuses it, anything else sets it off.
    private void OnDamaged(CombatDamageReport report)
    {
        if (report.Target != health) return;
        if (report.Hit.Source != null) lastSource = report.Hit.Source;
        if (!report.IsKillingBlow || ended || !IsExploder) return;
        ended = true;
        if (report.Hit.Kind == DamageKind.Execution) Defuse(report.Hit);
        else Burst();
    }

    private void LateUpdate()
    {
        bool lit = IsExploder && health != null && health.IsAlive;
        Watch(lit);
        if (!lit)
        {
            marker?.Hide();
            fuse = 0f;
            return;
        }
        float dt = Time.deltaTime;
        Fuse(dt);
        ShowGlow(dt);
    }

    // Every PC counts the fuse from where everyone stands, so its copies flash as the host's does; only the host ends it.
    private void Fuse(float dt)
    {
        bool standing = (mind == null || mind.Ready) && (melee == null || !(melee.IsLying || melee.IsStunned));
        bool close = false;
        if (standing)
            foreach (var player in PlayerRoster.All)
                if (player != null && player.IsInitialized && !player.IsDead
                    && (player.transform.position - transform.position).sqrMagnitude <= fuseRange * fuseRange) { close = true; break; }
        // At a Support's barricade it lights it too, on every PC alike from where the planks stand (ZombieBreach walks it
        // there and it doesn't swing): its burst is how it breaks them.
        if (standing && !close && ZombieBreach.PlanksNear(transform.position, fuseRange)) close = true;
        fuse = Mathf.Clamp(fuse + (close ? dt : -dt * .5f), 0f, fuseSeconds);
        if (close && Time.time >= nextFlash)
        {
            // Faster as it runs out.
            nextFlash = Time.time + Mathf.Lerp(.28f, .07f, fuse / fuseSeconds);
            HitFlashRenderer.For(this)?.WhiteFlash(.05f);
        }
        if (fuse < fuseSeconds || !Runs) return;
        health.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), health.Health + 1f, DamageKind.Fall, transform.position + Vector3.up,
            Vector3.up, null));
    }

    // The light in its chest (EliteLight): it beats as it walks, faster and whiter as the fuse runs.
    private void ShowGlow(float dt)
    {
        if (marker == null) marker = new EliteLight(this, HumanBodyBones.Chest, HumanBodyBones.Spine);
        float share = fuse / fuseSeconds;
        beat += dt * Mathf.Lerp(glowBeat, fuseBeat, share) * Mathf.PI * 2f;
        float pulse = .5f + .5f * Mathf.Sin(beat);
        Color color = Color.Lerp(glowColor, Color.white, share * .8f);
        color.a = Mathf.Lerp(.55f, 1f, pulse);
        marker.Show(color, glowSize * Mathf.Lerp(.8f, 1.15f, pulse) * (1f + share * .5f));
    }

    // Its death where it was judged (the host, the killer's PC): already ended by its killing blow (OnDamaged), unless that
    // went unheard here (it turned exploder that very frame), when it bursts as it always did.
    private void OnDied(EnemyHealth died)
    {
        if (!IsExploder || ended) return;
        ended = true;
        Burst();
    }

    // A takedown or an execution: the blade reached the brainstem before the gas sac lit. It hisses and sags in a puff of
    // gas, the light in its chest goes out, nobody is hurt or thrown and no zombie round it is touched (the host's burst
    // never runs). Whoever landed it is paid for the nerve it took (this PC's own player only).
    private void Defuse(in DamageHit hit)
    {
        marker?.Hide();
        var chest = Chest();
        Vector3 at = chest != null ? chest.position : transform.position + Vector3.up * 1.1f;
        GasPuff(at);
        GameAudio.PlayAt(Hiss(), at, AudioChannel.Combat, .9f, 2f, 25f);
        bool mine = hit.Source != null && hit.Source.GetComponentInParent<PlayerMovement>() is PlayerMovement by && by.IsLocal;
        if (mine && defuseReward > 0) CombatFeedHUD.Special(Texts.Get("feed.score.exploder_defuse"), defuseReward, gasColor);
    }

    private Transform Chest()
    {
        var animator = GetComponentInChildren<Animator>();
        if (animator == null || !animator.isHuman) return null;
        var chest = animator.GetBoneTransform(HumanBodyBones.Chest);
        return chest != null ? chest : animator.GetBoneTransform(HumanBodyBones.Spine);
    }

    // Every PC, as it dies: the burst where it stood, this PC's own player hurt and thrown; on the host, the zombies round it.
    private void Burst()
    {
        Vector3 at = transform.position;
        marker?.Hide();
        CanBlastEffect.Play(at, blastRadius, 0f);
        GameAudio.PlayAt(Boom(), at + Vector3.up, AudioChannel.Combat, 1f, 4f, 80f);
        var mine = PlayerRoster.Local;
        if (mine != null && mine.IsInitialized && !mine.IsDead)
        {
            Vector3 away = mine.transform.position - at;
            float far = away.magnitude;
            Vector3 flat = Vector3.ProjectOnPlane(away, Vector3.up);
            flat = flat.sqrMagnitude > 1e-4f ? flat.normalized : mine.transform.forward * -1f;
            // A wall, a floor or a car between the burst and them takes it (user, 2026-11-11 playtest: the burst hurt as
            // much through a wall). Only the world counts: bodies don't shield anyone.
            bool covered = Covered(at, mine.transform.position);
            if (far <= blastRadius && !covered)
            {
                float share = 1f - far / Mathf.Max(.01f, blastRadius);
                mine.DamageReceiver.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), Mathf.Lerp(playerEdgeDamage, playerDamage, share), DamageKind.Attack,
                    mine.transform.position + Vector3.up, flat, gameObject, origin: at + Vector3.up));
                mine.KnockBack(flat * (knock * Mathf.Lerp(.4f, 1f, share)) + Vector3.up * knockUp);
            }
            float shake = 1f - Mathf.Clamp01(far / Mathf.Max(.01f, shakeReach));
            var view = shake > 0f ? mine.GetComponentInChildren<PlayerCameraEffects>() : null;
            if (view != null) view.ApplyCameraImpulse(new Vector3(-8f * shake, 0f, (Random.value < .5f ? -4f : 4f) * shake), 4.5f * shake);
        }
        if (!Runs) return;
        WearBarricades(at);
        // A copy of the list: the burst kills, and the killed leave it (another exploder among them bursts in its turn).
        near.Clear();
        foreach (var enemy in EnemyHealth.Living)
            if (enemy != null && enemy != health && enemy.IsAlive && (enemy.transform.position - at).sqrMagnitude <= blastRadius * blastRadius) near.Add(enemy);
        var struck = near.ToArray();
        foreach (var enemy in struck)
        {
            if (enemy == null || !enemy.IsAlive || Covered(at, enemy.transform.position)) continue;
            Vector3 away = Vector3.ProjectOnPlane(enemy.transform.position - at, Vector3.up);
            float share = 1f - Mathf.Clamp01(away.magnitude / Mathf.Max(.01f, blastRadius));
            away = away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.forward;
            enemy.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), Mathf.Lerp(zombieEdgeDamage, zombieDamage, share), DamageKind.Attack,
                enemy.transform.position + Vector3.up, away, lastSource, false, 0f, true, origin: at));
            if (enemy != null && enemy.IsAlive && enemy.TryGetComponent(out MeleeCombatTarget target)) target.KnockDown(away, zombieKnockdown);
        }
    }

    private static readonly List<Barricade> nearPlanks = new List<Barricade>();

    // The host: a Support's barricades within the burst wear by it through their own damage path (Barricade.ReceiveHit:
    // the planks fly, it may break, every PC sees it; wire lets it through, a crate takes nothing). Listed first: one that
    // breaks leaves the list it is read from.
    private void WearBarricades(Vector3 at)
    {
        if (barricadeDamage <= 0f && barricadeEdgeDamage <= 0f) return;
        nearPlanks.Clear();
        foreach (var each in Barricade.Standing) if (each != null) nearPlanks.Add(each);
        foreach (var each in nearPlanks)
        {
            if (each == null || Mathf.Abs(each.transform.position.y - at.y) > blastRadius) continue;
            float gap = ZombieBreach.Gap(each, at, out _);
            if (gap > blastRadius) continue;
            float share = 1f - Mathf.Clamp01(gap / Mathf.Max(.01f, blastRadius));
            Vector3 middle = each.transform.position + Vector3.up * (Barricade.Height * .5f);
            Vector3 way = Vector3.ProjectOnPlane(middle - at, Vector3.up);
            way = way.sqrMagnitude > 1e-4f ? way.normalized : Vector3.forward;
            each.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), Mathf.Lerp(barricadeEdgeDamage, barricadeDamage, share), DamageKind.Attack,
                middle, way, gameObject, false, 0f, true, origin: at));
        }
        nearPlanks.Clear();
    }

    private static int coverMask;

    // Something solid between the burst's heart and a body's chest.
    private static bool Covered(Vector3 burst, Vector3 feet)
    {
        if (coverMask == 0) coverMask = ~(LayerMask.GetMask("Enemy") | 1 << 31);
        return Physics.Linecast(burst + Vector3.up, feet + Vector3.up, coverMask, QueryTriggerInteraction.Ignore);
    }

    // A wet, heavy burst, made here once: a thump that drops, under a short rush of dulled noise.
    private static AudioClip Boom()
    {
        if (boom != null) return boom;
        var data = new float[(int)(SampleRate * .9f)];
        var random = new System.Random(3016);
        float held = 0f, turn = 0f, top = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)SampleRate;
            turn += 2f * Mathf.PI * Mathf.Lerp(120f, 38f, Mathf.Clamp01(t / .25f)) / SampleRate;
            held += ((float)random.NextDouble() * 2f - 1f - held) * Mathf.Lerp(.5f, .06f, Mathf.Clamp01(t / .3f));
            data[i] = Mathf.Sin(turn) * Mathf.Exp(-t / .16f) * .9f + held * Mathf.Exp(-t / .22f) * .8f;
            top = Mathf.Max(top, Mathf.Abs(data[i]));
        }
        int fade = SampleRate / 50;
        for (int i = 0; i < data.Length; i++)
            data[i] = data[i] / Mathf.Max(.001f, top) * .9f * Mathf.Min(1f, (data.Length - 1 - i) / (float)fade);
        boom = AudioClip.Create("Exploder Burst", data.Length, 1, SampleRate, false);
        boom.SetData(data, 0);
        boom.hideFlags = HideFlags.HideAndDontSave;
        return boom;
    }

    // A defused sac letting go: a soft thump, then a short hiss of gas that thins and drops as it empties. Under half a
    // second, made here once.
    private static AudioClip Hiss()
    {
        if (hiss != null) return hiss;
        var data = new float[(int)(SampleRate * .45f)];
        var random = new System.Random(4127);
        float last = 0f, low = 0f, turn = 0f, top = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)SampleRate;
            float noise = (float)random.NextDouble() * 2f - 1f;
            // Air: noise with its lows taken out, getting duller as the pressure goes.
            low += (noise - low) * Mathf.Lerp(.85f, .25f, t / .45f);
            float air = low - last;
            last = low;
            turn += 2f * Mathf.PI * Mathf.Lerp(95f, 60f, Mathf.Clamp01(t / .08f)) / SampleRate;
            float thump = Mathf.Sin(turn) * Mathf.Exp(-t / .035f);
            data[i] = air * Mathf.Min(1f, t / .006f) * Mathf.Exp(-t / .16f) * 1.6f + thump * .7f;
            top = Mathf.Max(top, Mathf.Abs(data[i]));
        }
        int fade = SampleRate / 40;
        for (int i = 0; i < data.Length; i++)
            data[i] = data[i] / Mathf.Max(.001f, top) * .8f * Mathf.Min(1f, (data.Length - 1 - i) / (float)fade);
        hiss = AudioClip.Create("Exploder Defused", data.Length, 1, SampleRate, false);
        hiss.SetData(data, 0);
        hiss.hideFlags = HideFlags.HideAndDontSave;
        return hiss;
    }

    // The gas it lets out as it sags: a few soft, sickly blobs swelling out and rising a little, gone within a second.
    private void GasPuff(Vector3 at)
    {
        var host = new GameObject("Exploder Gas");
        host.transform.position = at;
        var system = host.AddComponent<ParticleSystem>();
        system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = system.main;
        main.duration = .1f;
        main.loop = false;
        main.startLifetime = new ParticleSystem.MinMaxCurve(.45f, .85f);
        main.startSpeed = new ParticleSystem.MinMaxCurve(.4f, 1.6f);
        main.startSize = new ParticleSystem.MinMaxCurve(gasSize * .45f, gasSize);
        main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
        main.startColor = gasColor;
        main.gravityModifier = -.06f;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.maxParticles = 14;
        main.stopAction = ParticleSystemStopAction.Destroy;
        var emission = system.emission;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)14) });
        var shape = system.shape;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius = gasSize * .25f;
        var growth = system.sizeOverLifetime;
        growth.enabled = true;
        growth.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, .5f, 1f, 1.5f));
        var fade = system.colorOverLifetime;
        fade.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(.6f, .3f), new GradientAlphaKey(0f, 1f) });
        fade.color = gradient;
        var slow = system.limitVelocityOverLifetime;
        slow.enabled = true;
        slow.dampen = .4f;
        slow.limit = .2f;
        var renderer = host.GetComponent<ParticleSystemRenderer>();
        renderer.sharedMaterial = FeelAssets.Particle(FeelAssets.SoftDot, false);
        renderer.renderMode = ParticleSystemRenderMode.Billboard;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
        system.Play();
    }
}
