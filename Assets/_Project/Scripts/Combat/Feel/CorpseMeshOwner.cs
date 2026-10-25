using UnityEngine;

/// <summary>Frees a baked skinned-mesh copy together with its corpse.</summary>
public sealed class CorpseMeshOwner : MonoBehaviour
{
    public Mesh mesh;
    private void OnDestroy() { if (mesh != null) Destroy(mesh); }
}
