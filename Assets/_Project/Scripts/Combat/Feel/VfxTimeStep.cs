using UnityEngine;
using UnityEngine.VFX;

/// <summary>
/// VFX Graph effects (the impact pack's blood, dust and sparks) move in fixed steps of game time, Project Settings > VFX >
/// Fixed Time Step (1/60 s), so a slowed game steps them less often in real time: at a fifth of the speed they jump twelve
/// times a second while everything else moves every frame. This keeps the step the same share of real time whoever slows
/// the game (SkullSlowMotion, CombatTimeEffects alone), and hands the project's step back when time runs normally again.
/// </summary>
public sealed class VfxTimeStep : MonoBehaviour
{
    private static float baseline;
    private float applied = 1f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Startup()
    {
        baseline = VFXManager.fixedTimeStep;
        // Kept across scenes but not hidden from them: leaving Play unloads it, so OnDestroy restores the step.
        var host = new GameObject("VFX Time Step");
        DontDestroyOnLoad(host);
        host.AddComponent<VfxTimeStep>();
    }

    private void Update()
    {
        // A pause stops the effects anyway, so the step is left as it was.
        float scale = Time.timeScale;
        if (scale <= 0f || Mathf.Approximately(scale, applied)) return;
        applied = scale;
        VFXManager.fixedTimeStep = baseline * Mathf.Min(1f, scale);
    }

    // The step is a project setting: quitting Play mid-slowdown must not leave the slowed one behind.
    private void OnDestroy() => VFXManager.fixedTimeStep = baseline;
}
