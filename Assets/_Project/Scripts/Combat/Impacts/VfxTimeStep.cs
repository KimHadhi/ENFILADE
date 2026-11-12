using UnityEngine;
using UnityEngine.VFX;

/// <summary>
/// VFX Graph effects (the impact pack's blood, dust and sparks) move in fixed steps of game time, Project Settings > VFX >
/// Fixed Time Step (1/60 s), so a slowed game steps them less often in real time: at a fifth of the speed they jump twelve
/// times a second while everything else moves every frame. This keeps the step the same share of real time whoever slows
/// the game (SkullSlowMotion, CombatTimeEffects alone), and puts the project's step back when time runs normally again.
/// </summary>
public sealed class VfxTimeStep : MonoBehaviour
{
    // The project's step, kept here rather than read from the setting: the editor can save a slowed step into the setting
    // (that is how it once fell to Unity's floor of 0.0001 s), and each Play would then slow it further. Every Play starts
    // from this, and leaving Play writes it back. Change it together with Project Settings > VFX.
    private const float Step = 1f / 60f;
    private float applied = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Startup()
    {
        VFXManager.fixedTimeStep = Step;
        // Kept across scenes but not hidden from them: leaving Play unloads it, so OnDestroy restores the step.
        var host = new GameObject("VFX Time Step");
        RuntimeSceneHierarchy.Keep(host);
        host.AddComponent<VfxTimeStep>();
    }

    private void Update()
    {
        // A pause stops the effects anyway, so the step is left as it was.
        float scale = Time.timeScale;
        if (scale <= 0f || Mathf.Approximately(scale, applied)) return;
        applied = scale;
        VFXManager.fixedTimeStep = Step * Mathf.Min(1f, scale);
    }

    private void OnDestroy() => VFXManager.fixedTimeStep = Step;
}
