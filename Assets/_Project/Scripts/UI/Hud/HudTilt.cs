using UnityEngine;

/// <summary>
/// One HUD element's own lean on top of its plane, Persona-style: the health number, its cells and the ultimate bar each
/// sit at their own angle and depth, so they read as separate pieces and drift apart a little when the HUD sways.
/// HudTilt.csv has a row per element, named after its GameObject; HudRig adds one for each row it finds under GameHUD.
/// </summary>
[DisallowMultipleComponent]
public sealed class HudTilt : MonoBehaviour, ITableBound
{
    public const string TableName = "HudTilt";

    [Tooltip("Turn about the vertical axis (°). Positive sends the right side away.")]
    [SerializeField] private float yaw;
    [Tooltip("Turn about the horizontal axis (°). Positive sends the top away.")]
    [SerializeField] private float pitch;
    [Tooltip("Turn within the screen (°). Positive is anticlockwise: the right side rises.")]
    [SerializeField] private float roll;
    [Tooltip("Distance from the plane (px at 1080p). Negative comes toward the player.")]
    [SerializeField] private float depth;
    [Tooltip("Slide along the plane (px at 1080p).")]
    [SerializeField] private float x, y;

    private RectTransform rect;
    private Vector3 home;
    private Quaternion homeRotation;

    public static void Attach(RectTransform element)
    {
        if (!element.TryGetComponent(out HudTilt tilt)) tilt = element.gameObject.AddComponent<HudTilt>();
        GameTables.Bind(TableName, element.name, tilt);
    }

    public void OnTableApplied() => Apply();

    private void OnEnable() => Apply();

    private void Apply()
    {
        if (rect == null)
        {
            rect = (RectTransform)transform;
            home = rect.localPosition;
            homeRotation = rect.localRotation;
        }
        rect.localRotation = homeRotation * Quaternion.Euler(pitch, -yaw, roll);
        rect.localPosition = home + new Vector3(x, y, depth);
    }
}
