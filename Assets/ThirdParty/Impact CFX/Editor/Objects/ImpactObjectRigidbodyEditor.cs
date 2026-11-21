using UnityEditor;

namespace ImpactCFX.EditorScripts
{
    [CustomEditor(typeof(ImpactObjectRigidbody))]
    [CanEditMultipleObjects]
    public class ImpactObjectRigidbodyEditor : ImpactObjectRigidbodyCheapEditor
    {

    }

    [CustomEditor(typeof(ImpactObjectRigidbodyCheap))]
    [CanEditMultipleObjects]
    public class ImpactObjectRigidbodyCheapEditor : ImpactObjectSingleMaterialEditor
    {

    }
}