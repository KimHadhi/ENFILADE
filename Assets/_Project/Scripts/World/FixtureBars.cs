using UnityEngine;

/// <summary>
/// Plain bars standing in for a fixture with no model of its own: a ladder's rails and rungs, a zipline's cable. With
/// no material given they wear Resources/World/FixtureBar (dark steel): a primitive's own default material isn't in a
/// built game under URP, so ladders and ziplines came out magenta in the exe (friend feedback C15).
/// </summary>
public static class FixtureBars
{
    private const string FallbackPath = "World/FixtureBar";
    private static Material fallback;

    public static void Add(Transform parent, Vector3 from, Vector3 to, float thickness, Material material)
    {
        var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(bar.GetComponent<Collider>());
        bar.name = "Bar";
        bar.transform.SetParent(parent, false);
        bar.transform.SetPositionAndRotation((from + to) * .5f, Quaternion.LookRotation(to - from));
        bar.transform.localScale = new Vector3(thickness, thickness, Vector3.Distance(from, to));
        bar.GetComponent<Renderer>().sharedMaterial = material != null ? material : Fallback;
    }

    private static Material Fallback
    {
        get
        {
            if (fallback != null) return fallback;
            fallback = Resources.Load<Material>(FallbackPath);
            if (fallback != null) return fallback;
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (lit != null) fallback = new Material(lit) { name = "Fixture Bar", color = new Color(.34f, .35f, .37f) };
            return fallback;
        }
    }
}
