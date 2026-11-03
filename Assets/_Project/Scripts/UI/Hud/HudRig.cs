using UnityEngine;

/// <summary>
/// Sets GameHUD up for depth when Play starts: draws its canvas through HudCamera, turns LEFT and RIGHT into the two
/// sides of a curved screen, lifts what must stay flat off those sides (the new-objective alert in the top centre), and
/// gives each element named in HudTilt.csv its own lean. MIDDLE (wave banner, crosshair, prompts) stays flat.
/// </summary>
[DisallowMultipleComponent]
public sealed class HudRig : MonoBehaviour
{
    private const string LeftName = "LEFT", RightName = "RIGHT";
    // Children of a side that sit in the centre of the screen.
    private static readonly string[] FlatNames = { "ObjectiveAlert" };

    private void Awake()
    {
        HudCamera.Adopt(GetComponent<Canvas>());
        var all = GetComponentsInChildren<RectTransform>(true);
        RectTransform flat = null;
        foreach (string name in FlatNames)
        {
            var element = Find(all, name);
            if (element == null) continue;
            if (flat == null) flat = Fill("Flat", transform);
            element.SetParent(flat, false);
        }
        var left = Find(all, LeftName);
        var right = Find(all, RightName);
        if (left != null) HudPlane.Attach(left, HudPlane.Side.Left);
        if (right != null) HudPlane.Attach(right, HudPlane.Side.Right);
        foreach (string id in GameTables.RowIds(HudTilt.TableName))
        {
            var element = Find(all, id);
            if (element != null) HudTilt.Attach(element);
        }
    }

    private static RectTransform Find(RectTransform[] all, string name)
    {
        foreach (var rect in all)
            if (rect.name == name) return rect;
        return null;
    }

    private static RectTransform Fill(string name, Transform parent)
    {
        var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        rect.SetParent(parent, false);
        rect.gameObject.layer = parent.gameObject.layer;
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = rect.offsetMax = Vector2.zero;
        return rect;
    }
}
