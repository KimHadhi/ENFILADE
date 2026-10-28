using UnityEngine;

/// <summary>
/// Says what this object (and its children) is made of, for footsteps and impacts. Overrides the keyword match of
/// Resources/Tables/Surfaces.csv. Use a surface id from that table: Concrete, Metal, Duct, Wood, Glass, Grass…
/// </summary>
[DisallowMultipleComponent]
public sealed class SurfaceType : MonoBehaviour
{
    [Tooltip("A surface id from Resources/Tables/Surfaces.csv, e.g. Concrete, Metal, Duct, Gantry, Wood, Glass, Grass, Water.")]
    [SerializeField] private string surface = Surfaces.Default;

    public string Surface => surface;
}
