using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A Point's ultimate on its guns, seen on every PC. The user, 2026-11-12: "두 총에서 총구 쪽이 빛나는 느낌 … 어차피 총기
/// 등에 수납하고 있으니까 보일 거 아니야 … 에이펙스 레전드의 윙맨 Thermal Rise … 총구 쪽부터 달궈져서 빛나는 느낌 … 파티클들
/// 좀 반짝이면서 움직이는 거".
/// Both guns heat from the muzzle back: the one in hand (first person and as the others see it) and the one worn on the
/// body (HolsteredGuns). Each glows white-gold at the muzzle through orange to a dull red along the barrel (BodyShell
/// with the GunHeat shader), and sparks twinkle off the hot metal and drift up.
/// They burn at their hottest for the ultimate's moment, then cool. Numbers: Tables/MomentFx.csv, row Heat.
/// </summary>
[DisallowMultipleComponent]
public sealed class GunHeat : MonoBehaviour
{
    private const string Row = "Heat", HeatShader = "Combat/Shaders/GunHeat";
    private static readonly int OriginId = Shader.PropertyToID("_HeatOrigin"), AxisId = Shader.PropertyToID("_HeatAxis"),
        ReachId = Shader.PropertyToID("_HeatReach"), HeatId = Shader.PropertyToID("_Heat"), GlowId = Shader.PropertyToID("_Glow");
    private static readonly Color Ember = new Color(1f, .72f, .3f, 1f), EmberHot = new Color(1f, .95f, .8f, 1f);
    private static Material heatMaterial;

    private sealed class Lit
    {
        public object source;
        public BodyShell shell;
        public ParticleSystem sparks;
        public Vector3 muzzle, barrel;
        public float length, carry;
        public bool seen;
    }

    private readonly List<Lit> lit = new List<Lit>();
    private readonly List<(Renderer[] renderers, Vector3 muzzle, Vector3 barrel, float length)> worn = new List<(Renderer[] renderers, Vector3 muzzle, Vector3 barrel, float length)>();
    private float since, until, hot;
    private FPSBody body;
    private HolsteredGuns holsters;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => heatMaterial = null;

    /// <summary><paramref name="point"/>'s guns heat for <paramref name="seconds"/>, at their hottest for the first share.</summary>
    public static void Kindle(Component point, float seconds)
    {
        if (!Application.isPlaying || point == null || seconds <= 0f || !MomentLook.On(Row) || Material() == null) return;
        var player = point.GetComponentInParent<PlayerMovement>();
        var host = player != null ? player.gameObject : point.gameObject;
        if (!host.TryGetComponent(out GunHeat heat)) heat = host.AddComponent<GunHeat>();
        heat.since = Time.time;
        heat.until = Time.time + seconds;
        heat.hot = Mathf.Clamp(MomentLook.Number(Row, "hot", 2.5f), 0f, seconds);
        heat.enabled = true;
    }

    private static Material Material()
    {
        if (heatMaterial != null) return heatMaterial;
        var shader = Resources.Load<Shader>(HeatShader);
        if (shader == null) return null;
        return heatMaterial = new Material(shader) { name = "Gun Heat (Runtime)", hideFlags = HideFlags.HideAndDontSave };
    }

    private void LateUpdate()
    {
        float now = Time.time;
        if (now >= until) { Cool(); return; }
        if (body == null) body = GetComponentInChildren<FPSBody>(true);
        if (holsters == null) holsters = GetComponentInChildren<HolsteredGuns>(true);
        foreach (var gun in lit) gun.seen = false;
        // The gun in hand: its muzzle and the way it points, measured on the gun itself.
        var held = body != null ? body.Held : null;
        if (held != null)
        {
            var gun = Find(held) ?? Ignite(held, held.GetComponentsInChildren<Renderer>(true));
            if (gun != null)
            {
                Transform at = held.transform;
                gun.muzzle = at.TransformPoint(held.MuzzlePosition);
                gun.barrel = (at.rotation * held.MuzzleRotation * Vector3.forward).normalized;
                if (gun.length <= 0f)
                {
                    GunMuzzle.Back(at, held.AimPoint, .02f, out Vector3 back, out _);
                    gun.length = Mathf.Max(.2f, Vector3.Distance(at.TransformPoint(back), gun.muzzle));
                }
                gun.seen = true;
            }
        }
        // The guns worn on the body, as their copies sit this frame.
        if (holsters != null)
        {
            holsters.WornGuns(worn);
            foreach (var (renderers, muzzle, barrel, length) in worn)
            {
                var gun = Find(renderers) ?? Ignite(renderers, renderers);
                if (gun == null) continue;
                gun.muzzle = muzzle;
                gun.barrel = barrel;
                gun.length = Mathf.Max(.2f, length);
                gun.seen = true;
            }
        }
        // Hottest for the moment, then cooling to nothing; up quickly at the start.
        float rise = Mathf.Clamp01((now - since) / Mathf.Max(.01f, MomentLook.Number(Row, "rise", .25f)));
        float cool = now - since <= hot ? 1f : 1f - Mathf.Clamp01((now - since - hot) / Mathf.Max(.01f, until - since - hot));
        float heat = rise * cool * cool;
        float reachShare = Mathf.Clamp01(MomentLook.Number(Row, "reach", .55f)), glow = MomentLook.Number(Row, "glow", 3.2f);
        float sparksPerSecond = MomentLook.Number(Row, "sparks", 26f);
        for (int i = lit.Count - 1; i >= 0; i--)
        {
            var gun = lit[i];
            if (!gun.seen) { Drop(i); continue; }
            float reach = gun.length * reachShare * Mathf.Lerp(.55f, 1f, heat);
            gun.shell.Sync(block =>
            {
                block.SetVector(OriginId, gun.muzzle);
                block.SetVector(AxisId, -gun.barrel);
                block.SetFloat(ReachId, reach);
                block.SetFloat(HeatId, heat);
                block.SetFloat(GlowId, glow);
            });
            Sparks(gun, reach, heat, sparksPerSecond);
        }
    }

    private Lit Find(object source)
    {
        foreach (var gun in lit) if (ReferenceEquals(gun.source, source)) return gun;
        return null;
    }

    private Lit Ignite(object source, IEnumerable<Renderer> renderers)
    {
        var shell = BodyShell.Over(renderers, Material());
        if (shell.Count == 0) return null;
        var sparks = FeelAssets.CreateEmitter("Gun heat sparks", transform, FeelAssets.Particle(FeelAssets.SoftDot, true), 96, -.06f);
        var size = sparks.sizeOverLifetime;
        size.enabled = true;
        // A twinkle: they flare, dim and flare again as they go.
        var curve = new AnimationCurve(new Keyframe(0f, .4f), new Keyframe(.15f, 1f), new Keyframe(.35f, .45f), new Keyframe(.55f, 1f), new Keyframe(1f, 0f));
        size.size = new ParticleSystem.MinMaxCurve(1f, curve);
        var colour = sparks.colorOverLifetime;
        colour.enabled = true;
        colour.color = FeelAssets.Fade(EmberHot, new Color(Ember.r, Ember.g * .5f, Ember.b * .3f, 0f));
        var gun = new Lit { source = source, shell = shell, sparks = sparks };
        lit.Add(gun);
        return gun;
    }

    // Sparks off the hot part of the barrel, more the hotter it is, rising and drifting a little.
    private static void Sparks(Lit gun, float reach, float heat, float perSecond)
    {
        if (gun.sparks == null || heat <= .02f) return;
        gun.carry += perSecond * heat * Time.deltaTime;
        int count = Mathf.FloorToInt(gun.carry);
        gun.carry -= count;
        for (int i = 0; i < count; i++)
        {
            float along = Random.value * Random.value * reach;
            Vector3 at = gun.muzzle - gun.barrel * along + Random.insideUnitSphere * .012f;
            gun.sparks.Emit(new ParticleSystem.EmitParams
            {
                position = at,
                velocity = Vector3.up * Random.Range(.12f, .45f) + Random.insideUnitSphere * .18f,
                startSize = Random.Range(.008f, .02f),
                startLifetime = Random.Range(.35f, .85f),
                startColor = Color.Lerp(Ember, EmberHot, Random.value * heat),
            }, 1);
        }
    }

    private void Drop(int index)
    {
        var gun = lit[index];
        gun.shell?.Release();
        if (gun.sparks != null) Destroy(gun.sparks.gameObject, 1f);
        lit.RemoveAt(index);
    }

    private void Cool()
    {
        for (int i = lit.Count - 1; i >= 0; i--) Drop(i);
        enabled = false;
    }

    private void OnDisable()
    {
        for (int i = lit.Count - 1; i >= 0; i--) Drop(i);
    }
}
