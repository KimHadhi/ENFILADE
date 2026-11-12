using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Map dressing, reflections inside (the user, 2026-11-13: "반사 프로브 진행"): baked, box-projected reflection probes for
/// the inside spaces, baked with the map. Until now every shiny surface reflected the sky (the default reflection), which
/// made sense outdoors but not in a tunnel or a closed room, the more so once the probe volume (GreyboxBuilder.Probes)
/// darkened those places: a terrazzo or tiled floor in the dark glowed with the sky. Now a space reflects itself, its
/// walls, ceiling, lamps and neon, and the sky only through its openings. Which spaces get probes is Tables/
/// MapReflections: a row per zone, either an ambience zone (Ambience lays the zone's probes with its box, so the chapters'
/// shops, halls and rooms need nothing more) or a name the map's builder passes to Reflections for a space with no sound
/// zone of its own (the subway's platform and tunnels, the tower's rooms and stairwells). A long space is cut into probes
/// of about the row's segment length, a tall one into its floors, so each probe's box (its box projection) is the room it
/// stands in and a shiny floor shows the right wall in the right place. Each probe takes its picture at about eye height.
/// Baked probes draw only what is static for them, so MarkStatic marks every model and box that stays put Reflection
/// Probe Static. A zone without a row has none: its surfaces reflect the sky as before.
/// </summary>
public sealed partial class GreyboxBuilder
{
    private const string ReflectionTable = "MapReflections";
    // What a row leaves empty (and what Default may not hold): a probe's picture size, the longest stretch one covers
    // (m), how far it blends into its neighbours (m), its camera over the space's floor (m), the space's storeys, and
    // which of two overlapping probes wins (higher).
    private const int ReflectionResolutionFallback = 128, ReflectionFloorsMost = 8;
    private const float ReflectionSegmentFallback = 30f, ReflectionBlendFallback = 1f, ReflectionEyeFallback = 1.7f;
    // A probe's camera keeps this far off its box's floor and ceiling (m).
    private const float ReflectionEyeRoom = .3f;

    private Transform reflections;
    private int reflectionProbes;

    /// <summary>
    /// Baked, box-projected reflection probes for an inside space from x0..x1, y0..y1 (its floor to its ceiling) and
    /// z0..z1, when Tables/MapReflections has a row for <paramref name="zone"/>; none without one.
    /// </summary>
    public void Reflections(string zone, float x0, float x1, float y0, float y1, float z0, float z1)
    {
        if (string.IsNullOrEmpty(zone) || !GameTables.TryGetRow(ReflectionTable, zone, out var row)) return;
        GameTables.TryGetRow(ReflectionTable, "Default", out var basics);
        int resolution = Mathf.Clamp(Mathf.ClosestPowerOfTwo(Mathf.RoundToInt(Value(row, basics, "resolution", ReflectionResolutionFallback))), 16, 1024);
        float segment = Mathf.Max(4f, Value(row, basics, "segment", ReflectionSegmentFallback));
        float blend = Mathf.Max(0f, Value(row, basics, "blend", ReflectionBlendFallback));
        float eye = Value(row, basics, "eye", ReflectionEyeFallback);
        int floors = Mathf.Clamp(Mathf.RoundToInt(Value(row, basics, "floors", 1f)), 1, ReflectionFloorsMost);
        int importance = Mathf.Max(0, Mathf.RoundToInt(Value(row, basics, "importance", 1f)));
        float intensity = Mathf.Max(0f, Value(row, basics, "intensity", 1f));
        float lowX = Mathf.Min(x0, x1), lowY = Mathf.Min(y0, y1), lowZ = Mathf.Min(z0, z1);
        float sizeX = Mathf.Abs(x1 - x0), sizeY = Mathf.Abs(y1 - y0) / floors, sizeZ = Mathf.Abs(z1 - z0);
        int alongX = Mathf.Max(1, Mathf.RoundToInt(sizeX / segment)), alongZ = Mathf.Max(1, Mathf.RoundToInt(sizeZ / segment));
        reflections ??= Root("◤━━ 반사 프로브 (Tables/MapReflections, 실내)");
        for (int floor = 0; floor < floors; floor++)
            for (int i = 0; i < alongX; i++)
                for (int j = 0; j < alongZ; j++)
                {
                    var size = new Vector3(sizeX / alongX, sizeY, sizeZ / alongZ);
                    var bottom = new Vector3(lowX + size.x * i, lowY + size.y * floor, lowZ + size.z * j);
                    var at = new Vector3(bottom.x + size.x * .5f, Mathf.Clamp(bottom.y + eye, bottom.y + ReflectionEyeRoom,
                        bottom.y + Mathf.Max(ReflectionEyeRoom, size.y - ReflectionEyeRoom)), bottom.z + size.z * .5f);
                    var host = new GameObject("반사 " + zone);
                    host.transform.SetParent(reflections, false);
                    host.transform.position = at;
                    var probe = host.AddComponent<ReflectionProbe>();
                    probe.mode = ReflectionProbeMode.Baked;
                    probe.boxProjection = true;
                    probe.size = size;
                    probe.center = bottom + size * .5f - at;
                    probe.resolution = resolution;
                    probe.blendDistance = blend;
                    probe.importance = importance;
                    probe.intensity = intensity;
                    probe.hdr = true;
                    probe.clearFlags = ReflectionProbeClearFlags.Skybox;
                    reflectionProbes++;
                }
    }

    // A row's number, else Default's, else the fallback.
    private static float Value(IReadOnlyDictionary<string, string> row, IReadOnlyDictionary<string, string> basics, string key, float fallback)
        => Number(row, key, basics != null ? Number(basics, key, fallback) : fallback);

    // For the build's report; baked is whether the map's lighting bake ran through (it bakes the probes too).
    private string ReflectionReport(bool baked)
        => reflectionProbes == 0 ? "" : "\n반사 프로브(Tables/MapReflections): 실내에 " + reflectionProbes + "개. "
                                        + (baked ? "맵과 같이 구웠어요." : "굽지 못했어요(Lighting 창의 Generate Lighting으로 구울 수 있어요).");
}
