using UnityEngine;

/// <summary>
/// Sets GameHUD up for depth when Play starts: draws its canvas through HudCamera, turns Left Panel and Right Panel into
/// the two sides of a curved screen and stands each on a visible glass board (HudBoard), lifts what must stay flat off
/// those sides (the new-objective alert in the top centre), and gives each element named in HudTilt.csv its own lean.
/// Center (crosshair, prompts) stays flat. The groups were LEFT, MIDDLE and RIGHT, with ★ on Mission, Chat and Health,
/// until the user asked for a tidy hierarchy (2026-11-11); the old wave indicator under Center is switched off.
/// All three keep the margins to the screen's edges the prefab drew them with on any screen shape (HudFrame).
/// </summary>
[DisallowMultipleComponent]
public sealed class HudRig : MonoBehaviour
{
    private const string LeftName = "Left Panel", RightName = "Right Panel", CenterName = "Center";
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
        if (left != null) { HudFrame.Keep(left); HudPlane.Attach(left, HudPlane.Side.Left); HudBoard.Add(left, HudPlane.Side.Left); }
        if (right != null) { HudFrame.Keep(right); HudPlane.Attach(right, HudPlane.Side.Right); HudBoard.Add(right, HudPlane.Side.Right); }
        HudFrame.Keep(Find(all, CenterName));
        foreach (string id in GameTables.RowIds(HudTilt.TableName))
        {
            var element = Find(all, id);
            if (element != null) HudTilt.Attach(element);
        }
        if (MkHud.On) Retire(all);
    }

    // HUD Mk II (MkHud, the user's pick of the HUD drafts, 2026-11-22) draws the health block and the gun itself: the
    // prefab's own pieces for them stand aside. The weapon slots stay on and draw themselves in Mk II's look on its canvas
    // (WeaponSlotsHUD), as the user wanted over Mk II's own selector (2026-11-22: "무기 슬롯 1,2,3,4번 그거는 기존 방식이
    // 더 맘에 들어. 기존 방식으로 디자인만 바꿔서"). The objective block stays on, unseen, to keep sounding a new objective
    // (MissionHUD); the chat, the crosshair and the prompts are not Mk II's.
    private static readonly (string Parent, string Name)[] Retired = { (LeftName, "Health"), (RightName, "AmmoHUD") };

    private static void Retire(RectTransform[] all)
    {
        foreach (var (parent, name) in Retired)
            foreach (var rect in all)
                if (rect.name == name && rect.parent != null && rect.parent.name == parent)
                {
                    rect.gameObject.SetActive(false);
                    break;
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
