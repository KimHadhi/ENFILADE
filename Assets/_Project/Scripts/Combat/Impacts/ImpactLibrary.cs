using UnityEngine;

/// <summary>The pack effect prefabs and bullet hole materials ImpactPool can use, found by name from Tables/ImpactEffects.csv.</summary>
[CreateAssetMenu(fileName = "ImpactLibrary", menuName = "ENFILADE/Combat/Impact Library")]
public sealed class ImpactLibrary : ScriptableObject
{
    public GameObject[] effects = new GameObject[0];
    public Material[] holes = new Material[0];

    public GameObject Find(string effectName)
    {
        foreach (var effect in effects)
            if (effect != null && effect.name == effectName) return effect;
        return null;
    }

    public Material FindHole(string holeName)
    {
        foreach (var hole in holes)
            if (hole != null && hole.name == holeName) return hole;
        return null;
    }
}
