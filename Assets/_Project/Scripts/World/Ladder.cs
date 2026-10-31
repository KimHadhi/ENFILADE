using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Place at the foot of a ladder, in the plane of its rungs, with local Z pointing out at the climber (Beatrun's
/// br_ladder). The player finds ladders by their geometry, so one needs no collider. Without a model of its own it
/// draws plain bars at runtime.
/// </summary>
[DisallowMultipleComponent]
public sealed class Ladder : MonoBehaviour
{
    private const float RungSpacing = .39f, BarThickness = .05f;
    private static readonly HashSet<Ladder> active = new HashSet<Ladder>();
    public static IEnumerable<Ladder> Active => active;
    [SerializeField, Min(.5f), Tooltip("From the foot to the top of the rails (m). Take it a little past the roof it leads to.")]
    private float height = 5.3f;
    [SerializeField, Min(.1f), Tooltip("Between the rails (m).")]
    private float width = .5f;
    [SerializeField, Tooltip("Look of the plain bars drawn when the ladder has no model of its own.")]
    private Material barMaterial;
    public float Height => height;
    public float Width => width;
    public Vector3 Outward => Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
    /// <summary>The way a climber faces (world yaw, degrees).</summary>
    public float ClimberYaw => MovementFixtures.YawOf(-Outward);
    private void OnEnable() => active.Add(this);
    private void OnDisable() => active.Remove(this);
    private void Start()
    {
        if (GetComponentInChildren<Renderer>() != null) return;
        Vector3 up = transform.up * height, side = transform.right * width * .5f, foot = transform.position;
        FixtureBars.Add(transform, foot - side, foot - side + up, BarThickness, barMaterial);
        FixtureBars.Add(transform, foot + side, foot + side + up, BarThickness, barMaterial);
        for (float y = RungSpacing; y < height; y += RungSpacing)
            FixtureBars.Add(transform, foot - side + transform.up * y, foot + side + transform.up * y, BarThickness, barMaterial);
    }
    private void OnDrawGizmos()
    {
        Vector3 up = transform.up * height, side = transform.right * width * .5f;
        Gizmos.color = Color.yellow;
        Gizmos.DrawLine(transform.position - side, transform.position - side + up);
        Gizmos.DrawLine(transform.position + side, transform.position + side + up);
        Gizmos.DrawLine(transform.position + up * .5f, transform.position + up * .5f + transform.forward * .4f);
    }
}
