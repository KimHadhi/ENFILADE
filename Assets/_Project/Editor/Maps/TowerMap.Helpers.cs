using TMPro;
using UnityEngine;

// TowerMap: Helpers. One part of the class; the rest is in TowerMap.cs and the files beside it.
public static partial class TowerMap
{
    private static string Floor(int level) => TowerLayout.IsHall(level) ? (level == 0 ? "로비" : "휴게층" + level / 4) : TowerLayout.FloorOf(level) + "층";

    // A level's own wall and floor: a hall's, or its block of floors' (the segment whose hall it climbs from; the roof,
    // one past the last level, is the top floor's ceiling, so it takes that block's floor).
    private static Material WallAt(int level) => TowerLayout.IsHall(level) ? hallWall : roomWalls[BlockOf(level)];

    private static Material FloorAt(int level) => TowerLayout.IsHall(level) ? hallFloor : roomFloors[BlockOf(level)];

    private static int BlockOf(int level) => Mathf.Clamp((level - 1) / 4, 0, TowerLayout.Segments - 1);

    // A point light with no shadows, in the tower's cool white.
    private static Light Light(string name, Vector3 at, float range, float intensity)
    {
        var light = new GameObject(name, typeof(Light)).GetComponent<Light>();
        light.transform.SetParent(lights, false);
        light.transform.position = at;
        light.type = LightType.Point;
        light.range = range;
        light.intensity = intensity;
        light.color = Cool;
        light.shadows = LightShadows.None;
        return light;
    }

    // A wide spot under a ceiling shining down, with no shadows. Not a point light: without shadows a point light shines
    // through the slab over it onto the walls of the level above, and the long floors need three times the lights; a cone
    // pointing down lights nothing above its own height.
    private static void Downlight(string name, Vector3 at, float range, float intensity)
    {
        var light = Light(name, at, range, intensity);
        light.type = LightType.Spot;
        light.spotAngle = 150f;
        light.innerSpotAngle = 100f;
        light.transform.rotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
    }

    // A cube outside the terrain (no walk mesh, not static): a fixture's part.
    private static GameObject Cube(string name, Transform parent, Vector3 centre, Vector3 size, Material material, bool solid = false)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(parent, false);
        cube.transform.position = centre;
        cube.transform.localScale = size;
        cube.GetComponent<Renderer>().sharedMaterial = material;
        if (!solid) Object.DestroyImmediate(cube.GetComponent<Collider>());
        return cube;
    }

    // A cube placed in its parent's space.
    private static GameObject Child(Transform parent, string name, Vector3 local, Vector3 size, Material material, bool solid)
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = name;
        cube.transform.SetParent(parent, false);
        cube.transform.localPosition = local;
        cube.transform.localScale = size;
        cube.GetComponent<Renderer>().sharedMaterial = material;
        if (!solid) Object.DestroyImmediate(cube.GetComponent<Collider>());
        return cube;
    }

    // Words standing in the world, read by someone looking along <paramref name="looking"/>, in the HUD's label face.
    private static void Words3D(string name, Vector3 at, Vector3 looking, string words, float size, Color colour, Vector2 box)
    {
        var holder = new GameObject(name, typeof(RectTransform));
        holder.transform.SetParent(fixtures, false);
        holder.transform.SetPositionAndRotation(at, Quaternion.LookRotation(looking, Vector3.up));
        var text = holder.AddComponent<TextMeshPro>();
        FontRoles.Apply(text, FontRoles.Label);
        text.rectTransform.sizeDelta = box;
        text.alignment = TextAlignmentOptions.Center;
        text.textWrappingMode = TextWrappingModes.NoWrap;
        text.overflowMode = TextOverflowModes.Overflow;
        text.fontSize = size;
        text.color = colour;
        text.text = words;
    }
}
