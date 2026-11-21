using ImpactCFX;
using UnityEngine;

/// <summary>
/// Impact CFX (Road Turtle Games' collision effects system, bought by the user, 2026-11-21: "IMPACT CFX 에셋 산 거 있는데 그거
/// implement"): the sounds, dust and scuffs a physics object makes as it hits, slides and rolls, from the Impact Material on
/// each object (its demo's items carry theirs: Assets/ThirdParty/Impact CFX Demo (URP)). One manager runs it for the whole
/// game, made once at the first scene and kept (the pack's own DontDestroyOnLoad): Resources/World/ImpactCFXManager, the
/// pack's manager exactly as its demo scene sets it up (audio, particle and decal processors, material processors, the
/// demo's material registry), so the items sound and look as the pack made them. Their voices play on the game's Combat
/// channel (GameImpactAudioSource).
/// </summary>
public static class CollisionEffects
{
    public const string ManagerPath = "World/ImpactCFXManager";
    private static bool warned;

    /// <summary>Makes the manager if the game has none yet. Cheap when it has.</summary>
    public static void Ensure()
    {
        if (!Application.isPlaying || ImpactCFXGlobal.HasInstance) return;
        var prefab = Resources.Load<GameObject>(ManagerPath);
        if (prefab == null)
        {
            if (!warned) Debug.LogWarning("[CollisionEffects] Resources/" + ManagerPath + " is missing: physics objects hit without Impact CFX's sounds and dust.");
            warned = true;
            return;
        }
        var manager = Object.Instantiate(prefab);
        manager.name = prefab.name;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void MakeAtStart() => Ensure();
}
