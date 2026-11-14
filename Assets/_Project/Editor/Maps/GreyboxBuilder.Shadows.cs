using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Map optimisation, shadows nobody sees (the user, 2026-11-14: "맵 최적화"). The only light drawing shadows live is the
/// sun (the lamps have none), and every caster is drawn again into each of its four shadow cascades. A model the sun
/// can't reach (a prop under a roof, a kiosk in the terminal, the subway's dressing) casts a shadow nobody can see, so
/// once the probe bake is done (its occluders cast as before, or lamp light would leak past them) each placed model and
/// each prop of a bought scene whose every test point is shut from the sun loses its shadow. Test points: its middle,
/// its corners drawn a little in, and a grid over its top. What shuts the sun out: the layout's solid boxes (not glass)
/// and a bought building's opaque shell (its walls, ceilings, roof and floors, which have holes where its windows are;
/// they are given colliders for this only), never a bought building's collision mesh, which closes its windows too.
/// The layout's own boxes keep their shadows: they are the walls and roofs that shade everything.
/// </summary>
public sealed partial class GreyboxBuilder
{
    // How far toward the sun a ray looks (m), and a top's grid of test points (per side).
    private const float SunReach = 400f;
    private const int SunTopGrid = 3;

    private int sunlessShadows;
    private readonly HashSet<Collider> sunShell = new HashSet<Collider>();

    // Turns off the shadows of the models the sun can't reach (see the class). Returns how many.
    private int QuietSunlessShadows()
    {
        Light sunlight = sun;
        if (sunlight == null)
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (light.type == LightType.Directional && light.gameObject.scene == scene) { sunlight = light; break; }
        if (sunlight == null) return 0;
        Vector3 toSun = -sunlight.transform.forward;
        var made = new List<GameObject>();
        // A bought building's opaque shell shuts the sun out where its glass doesn't.
        if (pack != null)
            foreach (var shown in pack.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!packOccluders.Contains(shown.gameObject) || shown.sharedMesh == null) continue;
                var shell = new GameObject("해 가림 (지을 때만)");
                shell.transform.SetParent(shown.transform, false);
                var solid = shell.AddComponent<MeshCollider>();
                solid.sharedMesh = shown.sharedMesh;
                sunShell.Add(solid);
                made.Add(shell);
            }
        Physics.SyncTransforms();
        var models = new List<GameObject>();
        if (dressing != null)
            foreach (Transform copy in dressing) models.Add(copy.gameObject);
        if (pack != null)
        {
            var units = new HashSet<GameObject>();
            foreach (var shape in pack.GetComponentsInChildren<Renderer>(true))
            {
                var unit = Unit(shape.gameObject);
                if (!IsBuilding(unit) && !packOccluders.Contains(shape.gameObject)) units.Add(unit);
            }
            models.AddRange(units);
        }
        foreach (var model in models)
        {
            if (model == null || !CastsAny(model) || SunReaches(model, toSun)) continue;
            foreach (var shape in model.GetComponentsInChildren<Renderer>(true)) shape.shadowCastingMode = ShadowCastingMode.Off;
            sunlessShadows++;
        }
        foreach (var shell in made) Object.DestroyImmediate(shell);
        sunShell.Clear();
        return sunlessShadows;
    }

    private static bool CastsAny(GameObject model)
    {
        foreach (var shape in model.GetComponentsInChildren<Renderer>(true))
            if (shape.enabled && shape.shadowCastingMode != ShadowCastingMode.Off) return true;
        return false;
    }

    // Whether the sun reaches any of the model's test points.
    private bool SunReaches(GameObject model, Vector3 toSun)
    {
        var box = WorldBounds(model);
        var points = new List<Vector3> { box.center };
        for (int k = 0; k < 8; k++)
            points.Add(box.center + Vector3.Scale(box.extents * SightCornerIn,
                new Vector3(k % 2 == 0 ? -1f : 1f, k / 2 % 2 == 0 ? -1f : 1f, k / 4 == 0 ? -1f : 1f)));
        for (int i = 0; i < SunTopGrid; i++)
            for (int j = 0; j < SunTopGrid; j++)
                points.Add(new Vector3(Mathf.Lerp(box.min.x, box.max.x, (i + .5f) / SunTopGrid), box.max.y - .02f,
                    Mathf.Lerp(box.min.z, box.max.z, (j + .5f) / SunTopGrid)));
        foreach (var point in points)
            if (!SunShut(point + toSun * .05f, toSun)) return true;
        return false;
    }

    // Whether something shuts the sun out along the ray: a solid box of the layout (not glass, not a door that opens), or
    // a bought building's shell; never its collision mesh (it closes the windows) or its props (their own boxes aside).
    private bool SunShut(Vector3 from, Vector3 toSun)
    {
        int hits = Physics.RaycastNonAlloc(from, toSun, sightHits, SunReach, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < hits; i++)
        {
            var solid = sightHits[i].collider;
            if (sunShell.Contains(solid)) return true;
            if (pack != null && solid.transform.IsChildOf(pack)) continue;
            if (seeThrough.Contains(solid.gameObject) || MovesWithGroup(solid.gameObject)) continue;
            return true;
        }
        return false;
    }

    // For the build's report.
    private string SunlessReport()
        => sunlessShadows == 0 ? "" : "\n그림자: 해가 닿지 않는 모델 " + sunlessShadows + "개는 그림자를 껐어요(굽기 뒤라 간접광은 그대로).";
}
