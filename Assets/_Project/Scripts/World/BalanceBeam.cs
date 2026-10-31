using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Goes on a beam with its local Z along the beam (Beatrun's br_balance). Players walk the middle of the top face of
/// its BoxCollider, so stretching the box stretches the walk.
/// </summary>
[DisallowMultipleComponent, RequireComponent(typeof(BoxCollider))]
public sealed class BalanceBeam : MonoBehaviour
{
    private static readonly HashSet<BalanceBeam> active = new HashSet<BalanceBeam>();
    public static IEnumerable<BalanceBeam> Active => active;
    private BoxCollider box;
    private BoxCollider Box => box != null ? box : box = GetComponent<BoxCollider>();
    public Collider Surface => Box;
    public Vector3 From => TopEnd(-1f);
    public Vector3 To => TopEnd(1f);
    public float Length => Vector3.Distance(From, To);
    public Vector3 Along => (To - From).normalized;
    /// <summary>How far along the beam, from From, this world point lies (m). Past either end it runs on.</summary>
    public float DistanceOf(Vector3 worldPoint) => Vector3.Dot(worldPoint - From, Along);
    public Vector3 PointAt(float distance) => From + Along * distance;
    private Vector3 TopEnd(float side) => transform.TransformPoint(Box.center + new Vector3(0f, Box.size.y * .5f, Box.size.z * .5f * side));
    private void OnEnable() => active.Add(this);
    private void OnDisable() => active.Remove(this);
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.green;
        Gizmos.DrawLine(From, To);
    }
}
