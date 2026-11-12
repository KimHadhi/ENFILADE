using System;
using UnityEngine;

/// <summary>
/// The bought effect prefabs the game plays by name (PackFx), kept here so the game can reach them at run time. Today
/// they come from Piloto Studio's Realistic Gunslinger pack (Assets/ThirdParty/Piloto Studio/Super Realistic FX Bundle
/// 02), the effects the user found fit the Schießhund (2026-11-13: "Realistic Gunslingers VFX에 시순트용 이펙트 꽤
/// 있는데? 해골 머리에 구멍 뚫린 건 결투 승리했을 때 상대 시체에서 나오는 이펙트로도 쓸 만하고, 총알 이펙트도 데스페라도용으로
/// 멋진 거 쓸 만한데?").
/// Only the listed ones load with the game, so a prefab is added here when a use of it is made, then given its look in
/// Tables/PackFx.csv. The prefabs themselves are never changed; PackFx tones each copy.
/// </summary>
[CreateAssetMenu(fileName = "PackFxLibrary", menuName = "ENFILADE/Pack FX Library")]
public sealed class PackFxLibrary : ScriptableObject
{
    public const string ResourcePath = "Combat/PackFxLibrary";

    [Serializable]
    public struct Entry
    {
        [Tooltip("The name Tables/PackFx.csv calls it by: its prefab's name.")]
        public string name;
        public GameObject prefab;
    }

    public Entry[] entries = Array.Empty<Entry>();

    private static PackFxLibrary instance;
    private static bool looked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        looked = false;
    }

    /// <summary>The library at Resources/Combat/PackFxLibrary, or null when it isn't there.</summary>
    public static PackFxLibrary Instance
    {
        get
        {
            if (!looked)
            {
                looked = true;
                instance = Resources.Load<PackFxLibrary>(ResourcePath);
            }
            return instance;
        }
    }

    /// <summary>The prefab called <paramref name="name"/> (case aside), or null.</summary>
    public GameObject Find(string name)
    {
        if (string.IsNullOrEmpty(name) || entries == null) return null;
        foreach (var entry in entries)
            if (entry.prefab != null && string.Equals(entry.name, name.Trim(), StringComparison.OrdinalIgnoreCase)) return entry.prefab;
        return null;
    }
}
