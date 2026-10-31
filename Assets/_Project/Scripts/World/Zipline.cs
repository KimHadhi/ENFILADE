using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A cable from this object to its end point (Beatrun's br_zipline). The player finds cables by their geometry, so one
/// needs no collider. Without a model of its own it draws a plain bar at runtime.
/// </summary>
[DisallowMultipleComponent]
public sealed class Zipline : MonoBehaviour
{
    private const float CableThickness = .04f;
    private static readonly HashSet<Zipline> active = new HashSet<Zipline>();
    public static IEnumerable<Zipline> Active => active;
    [SerializeField, Tooltip("Where the cable ends, relative to this object (m).")]
    private Vector3 end = new Vector3(0f, -3f, 20f);
    [SerializeField, Tooltip("Riders go whichever way they look. Otherwise only from this object to the end.")]
    private bool twoWay;
    [SerializeField, Tooltip("Look of the plain bar drawn when the cable has no model of its own.")]
    private Material barMaterial;
    public bool TwoWay => twoWay;
    public Vector3 From => transform.position;
    public Vector3 To => transform.TransformPoint(end);
    private void OnEnable() => active.Add(this);
    private void OnDisable() => active.Remove(this);
    private void Start()
    {
        if (GetComponentInChildren<Renderer>() == null) FixtureBars.Add(transform, From, To, CableThickness, barMaterial);
    }
    private void OnDrawGizmos()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(From, To);
        Gizmos.DrawWireSphere(To, .15f);
    }
}
