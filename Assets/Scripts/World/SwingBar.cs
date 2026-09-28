using System.Collections.Generic;
using UnityEngine;

/// <summary>Place on a horizontal bar. Local X is the bar axis; local Z is its swing direction.</summary>
[DisallowMultipleComponent]
[ExecuteAlways]
public sealed class SwingBar : MonoBehaviour
{
    private static readonly HashSet<SwingBar> active = new HashSet<SwingBar>();
    public static IEnumerable<SwingBar> ActiveBars => active;
    [SerializeField, Min(0.1f)] private float gripWidth = 2f;
    [SerializeField, Min(1f)] private float hangLength = 1.9f;
    [SerializeField, Range(10f, 85f)] private float maximumSwingAngle = 75f;
    public float HangLength => hangLength;
    public float MaxAngle => maximumSwingAngle * Mathf.Deg2Rad;
    public Vector3 SwingDirection => Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
    private void OnEnable() => active.Add(this);
    private void OnDisable() => active.Remove(this);
    public Vector3 ClosestGrip(Vector3 worldPoint)
    {
        Vector3 local = transform.InverseTransformPoint(worldPoint);
        return transform.TransformPoint(new Vector3(Mathf.Clamp(local.x, -gripWidth * 0.5f, gripWidth * 0.5f), 0f, 0f));
    }
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.TransformPoint(Vector3.left * gripWidth * 0.5f), transform.TransformPoint(Vector3.right * gripWidth * 0.5f));
        Gizmos.DrawWireSphere(transform.position - Vector3.up * hangLength, 0.15f);
    }
}
