using UnityEngine;

/// <summary>
/// Turns one side of the HUD about the screen edge on that side, so its inner edge recedes like the inside of a curved
/// screen (The Division), and lets the look sway carry it. Goes on a group that is centred on its root canvas (GameHUD's
/// LEFT and RIGHT, or a group Create makes to fill the canvas); nothing else may move that group. Hud.csv's Planes row
/// sets the angle. Only a canvas drawn through HudCamera shows the depth.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(10030)]
public sealed class HudPlane : MonoBehaviour
{
    public enum Side { Left, Right }

    private Side side;
    private RectTransform rect, canvasRect;
    private Vector3 home;
    private Quaternion homeRotation;

    /// <summary>A new group filling the canvas, on the given side's plane.</summary>
    public static RectTransform Create(string name, Transform canvas, Side side)
    {
        var group = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
        group.SetParent(canvas, false);
        group.anchorMin = Vector2.zero;
        group.anchorMax = Vector2.one;
        group.offsetMin = group.offsetMax = Vector2.zero;
        Attach(group, side);
        return group;
    }

    public static void Attach(RectTransform group, Side side)
    {
        if (!group.TryGetComponent(out HudPlane plane)) plane = group.gameObject.AddComponent<HudPlane>();
        plane.side = side;
    }

    private void Awake()
    {
        rect = (RectTransform)transform;
        home = rect.localPosition;
        homeRotation = rect.localRotation;
    }

    private void LateUpdate()
    {
        if (canvasRect == null)
        {
            var canvas = GetComponentInParent<Canvas>();
            if (canvas == null) return;
            canvasRect = (RectTransform)canvas.rootCanvas.transform;
        }
        var look = HudCamera.Settings;
        Vector2 sway = HudCamera.Sway;
        // A positive yaw brings +x toward the viewer, so the left side turns the other way for its inner edge to recede.
        float sign = side == Side.Left ? -1f : 1f;
        var turn = Quaternion.Euler(look.pitch + sway.y, sign * look.yaw + sway.x, 0f);
        var axis = new Vector3(sign * canvasRect.rect.width * .5f, 0f, 0f);
        rect.localPosition = axis + turn * (home - axis) + (Vector3)(sway * look.shift);
        rect.localRotation = turn * homeRotation;
    }
}
