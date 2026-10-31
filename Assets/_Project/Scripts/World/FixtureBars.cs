using UnityEngine;

/// <summary>Plain bars standing in for a fixture with no model of its own: a ladder's rails and rungs, a zipline's cable.</summary>
public static class FixtureBars
{
    public static void Add(Transform parent, Vector3 from, Vector3 to, float thickness, Material material)
    {
        var bar = GameObject.CreatePrimitive(PrimitiveType.Cube);
        Object.Destroy(bar.GetComponent<Collider>());
        bar.name = "Bar";
        bar.transform.SetParent(parent, false);
        bar.transform.SetPositionAndRotation((from + to) * .5f, Quaternion.LookRotation(to - from));
        bar.transform.localScale = new Vector3(thickness, thickness, Vector3.Distance(from, to));
        if (material != null) bar.GetComponent<Renderer>().sharedMaterial = material;
    }
}
