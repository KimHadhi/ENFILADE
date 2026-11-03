using UnityEngine;

/// <summary>
/// Shows a string field in the inspector as a pick among Surfaces.csv's surfaces (SurfaceNameDrawer) instead of a name to
/// type. The field stays a string, so a surface added to the table is in the list without any code.
/// </summary>
public sealed class SurfaceNameAttribute : PropertyAttribute { }
