using UnityEngine;

/// <summary>
/// Choosing where the ride comes down (the map gimmick card "착륙 자리 고르기", after PAYDAY 2's Big Bank escapes): while
/// its step runs (a Gimmick step), [F] at one of its landing zones lights a signal smoke there, and the hold and the escape
/// that follow are at that zone (CampaignRun.Respot of spot) on every PC. The one easiest to hold is the farthest; the
/// near ones are open on every side. Lit again after a wipe sends the run back before it. States: 0 none yet, else the
/// zone lit plus one.
/// </summary>
public sealed class GimmickLanding : GimmickSwitch
{
    [SerializeField, Tooltip("The landing zones, each where the ride would set down (its spot moves there).")]
    private Transform[] zones = new Transform[0];
    [SerializeField, Tooltip("The spot the hold and the escape are at (CampaignSteps spot), moved to the zone lit.")]
    private string spot = "Helipad";
    [SerializeField, Tooltip("The Gimmick step that lighting a zone finishes (CampaignSteps id).")]
    private string step = "";

    private ParticleSystem plume;

    private int ZoneNear(Vector3 at)
    {
        int best = -1;
        float closest = float.MaxValue;
        for (int i = 0; i < zones.Length; i++)
        {
            if (zones[i] == null) continue;
            float distance = (zones[i].position - at).sqrMagnitude;
            if (distance < closest) { closest = distance; best = i; }
        }
        return best;
    }

    internal override Vector3 Handle
    {
        get
        {
            var mine = Mine;
            int near = mine != null ? ZoneNear(mine.transform.position) : -1;
            return near >= 0 ? zones[near].position + Vector3.up : transform.position;
        }
    }

    internal override string Offer(PlayerMovement mine)
        => CampaignRun.IsRunning(step) && ZoneNear(mine.transform.position) >= 0 ? Texts.Get("gimmick.landing.light") : null;

    internal override int Use(PlayerMovement mine)
    {
        if (!CampaignRun.IsRunning(step)) return -1;
        int near = ZoneNear(mine.transform.position);
        return near >= 0 ? near + 1 : -1;
    }

    // After the zone's word has gone out: the step it finishes (the host lays the zone out before it moves on).
    internal override void Used(PlayerMovement mine) => CampaignRun.FinishStep(step);

    internal override void Show(byte state, bool fresh)
    {
        int zone = state - 1;
        if (zone < 0 || zone >= zones.Length || zones[zone] == null) return;
        CampaignRun.Respot(spot, zones[zone]);
        Smoke(zones[zone].position);
        if (fresh) GimmickSounds.PlayAt(GimmickSounds.Cue.Hiss, zones[zone].position, .8f, 3f, 30f);
    }

    // A signal smoke rising from the zone, red-pink so it reads from the air: a plume, not a cloud to hide in.
    private void Smoke(Vector3 at)
    {
        if (plume == null)
        {
            var host = new GameObject("착륙 신호 연막");
            host.transform.SetParent(transform, false);
            plume = host.AddComponent<ParticleSystem>();
            plume.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = plume.main;
            main.loop = true;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = 7f;
            main.startSpeed = 1.6f;
            main.startSize = 1.4f;
            main.maxParticles = 80;
            main.startColor = new Color(.95f, .32f, .42f, .55f);
            var emission = plume.emission;
            emission.rateOverTime = 8f;
            var shape = plume.shape;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 8f;
            shape.radius = .25f;
            shape.rotation = new Vector3(-90f, 0f, 0f);
            var size = plume.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, .6f, 1f, 3f));
            var color = plume.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, .1f), new GradientAlphaKey(.8f, .6f), new GradientAlphaKey(0f, 1f) });
            color.color = new ParticleSystem.MinMaxGradient(fade);
            var renderer = host.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.sharedMaterial = FeelAssets.Particle(FeelAssets.SoftDot, false);
        }
        plume.transform.position = at;
        if (!plume.isPlaying) plume.Play();
    }
}
