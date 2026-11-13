using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// How lit a player stands, 0 (dark) to 1 (lit): the 2026-11-11 stealth memo, item 47 ("어두운 곳일수록 덜 보이게
/// 하기"), after Advanced Stealth's light level (F:\Addons). Measured where their chest is, a few times a second, from the
/// same scene on every PC: the ambient light there (light probes, else the sky's), the sun (a directional light) unless
/// something stands between them and it, and each lamp near enough, by its distance, its cone and whether a wall hides it.
/// Their own flashlight switched on lights them fully. A zombie sees someone in the dark from less far (RangeShare) and
/// makes them out more slowly (SeenShare): ZombieMind. The stealth HUD shows the player their own (StealthMarkersHUD).
/// Numbers: Stealth.csv Light (enabled 0 switches it all off; darkAt and brightAt are in the light's own units, the sky's
/// a night map's shade being about 0 to 0.1, moonlight about 0.3, right under a street lamp about 0.9).
/// </summary>
public static class StealthLight
{
    public const string TableName = "Stealth", TableRow = "Light";
    // How far up from the feet it is measured (m), how far the sun's ray looks for a roof (m), and how often the scene's
    // lamps are looked for again (s).
    private const float ChestHeight = 1f, SunReach = 300f, LampsEvery = 3f;

    private sealed class Reading
    {
        public float Level = 1f, Raw, At = float.NegativeInfinity;
    }

    private static readonly Dictionary<PlayerMovement, Reading> readings = new Dictionary<PlayerMovement, Reading>();
    private static readonly Dictionary<PlayerMovement, WeaponController> guns = new Dictionary<PlayerMovement, WeaponController>();
    private static readonly List<Light> lamps = new List<Light>();
    private static readonly Vector3[] skyWays = { Vector3.up, Vector3.forward, Vector3.right, Vector3.back, Vector3.left };
    private static readonly Color[] skyColors = new Color[5];
    private static float lampsAt = float.NegativeInfinity;
    private static bool read, listening, on = true;
    private static float darkAt = .15f, brightAt = .8f, darkShare = .35f, darkRange = .6f, every = .25f;
    private static int mask;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Clear()
    {
        readings.Clear();
        guns.Clear();
        lamps.Clear();
        lampsAt = float.NegativeInfinity;
        read = listening = false;
    }

    private static void Forget() => read = false;

    /// <summary>How lit this player stands now, 0 to 1 (1 with the light switched off in the table).</summary>
    public static float Level(PlayerMovement player)
    {
        Read();
        if (!on || player == null) return 1f;
        if (!readings.TryGetValue(player, out var reading)) readings[player] = reading = new Reading();
        if (Time.time - reading.At >= every)
        {
            reading.At = Time.time;
            reading.Raw = Measure(player);
            reading.Level = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(darkAt, Mathf.Max(darkAt + .01f, brightAt), reading.Raw));
            if (readings.Count > 8) Prune();
        }
        return reading.Level;
    }

    /// <summary>How fast a zombie makes out someone this lit, as a share (darkShare in the dark, 1 lit).</summary>
    public static float SeenShare(float level) => Mathf.Lerp(Mathf.Clamp01(darkShare), 1f, Mathf.Clamp01(level));

    /// <summary>How far a zombie sees someone this lit, as a share of its sight (darkRange in the dark, 1 lit).</summary>
    public static float RangeShare(float level) => Mathf.Lerp(Mathf.Clamp01(darkRange), 1f, Mathf.Clamp01(level));

    /// <summary>The table switches the dark off (Light.enabled 0): the HUD then hides its meter.</summary>
    public static bool Enabled { get { Read(); return on; } }

    private static float Measure(PlayerMovement player)
    {
        // A flashlight on lights them up, whatever the dark round them.
        var gun = GunOf(player);
        if (gun != null && gun.LightOn) return float.PositiveInfinity;
        if (mask == 0) mask = Physics.DefaultRaycastLayers & ~(1 << 31) & ~LayerMask.GetMask("Enemy");
        Vector3 at = player.transform.position + Vector3.up * ChestHeight;
        float total = Ambient(at);
        Lamps();
        foreach (var lamp in lamps)
        {
            if (lamp == null || !lamp.isActiveAndEnabled || lamp.intensity <= 0f) continue;
            float strength = Luma(lamp.color.linear) * lamp.intensity;
            if (lamp.type == LightType.Directional)
            {
                // Out under it, or under a roof or in a wall's shadow.
                if (!Physics.Raycast(at, -lamp.transform.forward, SunReach, mask, QueryTriggerInteraction.Ignore)) total += strength;
                continue;
            }
            if (lamp.type != LightType.Point && lamp.type != LightType.Spot) continue;
            Vector3 from = lamp.transform.position, to = at - from;
            float distance = to.magnitude;
            if (distance >= lamp.range || distance < 1e-3f) continue;
            float share = 1f;
            if (lamp.type == LightType.Spot)
            {
                float angle = Vector3.Angle(lamp.transform.forward, to), half = lamp.spotAngle * .5f;
                if (angle >= half) continue;
                float inner = Mathf.Min(half - .5f, lamp.innerSpotAngle * .5f);
                share = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(inner, half, angle));
            }
            // As URP lights it: falling off with the square of the distance, smoothed to nothing at its range.
            float reach = distance * distance / (lamp.range * lamp.range);
            float window = Mathf.Clamp01(1f - reach * reach);
            float lit = strength * share * window * window / Mathf.Max(distance * distance, .25f);
            if (lit < .01f) continue;
            if (Physics.Linecast(from, at, mask, QueryTriggerInteraction.Ignore)) continue;
            total += lit;
        }
        return total;
    }

    // The ambient light where they stand: the light probes there, or the sky's, looked at from above and round about.
    private static float Ambient(Vector3 at)
    {
        LightProbes.GetInterpolatedProbe(at, null, out SphericalHarmonicsL2 probe);
        probe.Evaluate(skyWays, skyColors);
        float sum = 0f;
        foreach (var color in skyColors) sum += Luma(color);
        return Mathf.Max(0f, sum / skyColors.Length);
    }

    // The scene's lamps, looked for again now and then: not a player's or a zombie's own (a flashlight, a muzzle's flash).
    private static void Lamps()
    {
        if (Time.time - lampsAt < LampsEvery) return;
        lampsAt = Time.time;
        lamps.Clear();
        foreach (var lamp in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
        {
            if (lamp == null || lamp.GetComponentInParent<PlayerMovement>() != null || lamp.GetComponentInParent<EnemyHealth>() != null) continue;
            lamps.Add(lamp);
        }
    }

    private static WeaponController GunOf(PlayerMovement player)
    {
        if (!guns.TryGetValue(player, out var gun) || gun == null) guns[player] = gun = player.GetComponentInChildren<WeaponController>();
        return gun;
    }

    private static float Luma(Color color) => .2126f * color.r + .7152f * color.g + .0722f * color.b;

    // Players gone (left the room): their entries are swept out whole, as a destroyed key can't be looked up.
    private static void Prune()
    {
        var keep = new List<KeyValuePair<PlayerMovement, Reading>>();
        foreach (var pair in readings) if (pair.Key != null) keep.Add(pair);
        if (keep.Count == readings.Count) return;
        readings.Clear();
        guns.Clear();
        foreach (var pair in keep) readings[pair.Key] = pair.Value;
    }

    private static void Read()
    {
        // A live table edit reads it again (subscribed at first use: the tables clear their own events as play starts).
        if (!listening) { listening = true; GameTables.Reloaded += Forget; }
        if (read) return;
        read = true;
        if (!GameTables.TryGetRow(TableName, TableRow, out var row)) return;
        on = row.Number("enabled", 1f) > 0f;
        darkAt = row.Number("darkAt", darkAt);
        brightAt = row.Number("brightAt", brightAt);
        darkShare = row.Number("darkShare", darkShare);
        darkRange = row.Number("darkRange", darkRange);
        every = Mathf.Max(.05f, row.Number("every", every));
    }
}
