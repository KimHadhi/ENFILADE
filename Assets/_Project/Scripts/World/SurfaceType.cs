using UnityEngine;

/// <summary>
/// Says what this object (and its children) is made of, for footsteps, impacts and how bullets get through it. Overrides
/// the keyword match of Resources/Tables/Surfaces.csv; the inspector lists that table's surfaces to pick from.
/// </summary>
[DisallowMultipleComponent]
public sealed class SurfaceType : MonoBehaviour
{
    [Tooltip("What this is made of. The list is Resources/Tables/Surfaces.csv: a row added there shows up here.")]
    [SerializeField, SurfaceName] private string surface = Surfaces.Default;

    public string Surface => surface;
}
