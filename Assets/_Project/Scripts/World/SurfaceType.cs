using UnityEngine;

/// <summary>
/// Says what this object (and its children) is made of, for footsteps, impacts and how bullets get through it. Overrides
/// the keyword match of Resources/Tables/Maps/Surfaces.csv; the inspector lists that table's surfaces to pick from.
/// </summary>
[DisallowMultipleComponent]
public sealed class SurfaceType : MonoBehaviour
{
    [Tooltip("What this is made of. The list is Resources/Tables/Maps/Surfaces.csv: a row added there shows up here.")]
    [SerializeField, SurfaceName] private string surface = Surfaces.Default;

    /// <summary>Set by code that builds things at play (the hub's steel targets, SteelTarget) before anything hits them:
    /// Surfaces keeps what it read for a collider.</summary>
    public string Surface { get => surface; set => surface = value; }
}
