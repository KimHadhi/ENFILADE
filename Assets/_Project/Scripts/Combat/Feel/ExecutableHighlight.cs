using UnityEngine;

/// <summary>
/// While a target can be executed, its body takes a faint red tint and a red outline, so the chance shows on the enemy
/// itself as well as on its bar. Added on demand by MeleeCombatTarget. Runs on real time so the posture break's slow
/// motion cannot delay it; the look is in Feedback → Executable body.
/// </summary>
[DisallowMultipleComponent]
public sealed class ExecutableHighlight : MonoBehaviour
{
    private MeleeCombatTarget target;
    private HitFlashRenderer overlay;
    private float amount;

    private void Awake() => target = GetComponent<MeleeCombatTarget>();

    private void Update()
    {
        if (Time.timeScale <= 0f) return;
        float fade = Mathf.Max(.01f, CombatFeedbackSettings.InstanceOrDefaults.executableBodyFade);
        float next = Mathf.MoveTowards(amount, target.CanExecute ? 1f : 0f, Time.unscaledDeltaTime / fade);
        if (next == amount) return;
        amount = next;
        if (overlay == null) overlay = HitFlashRenderer.For(this);
        overlay.SetMark(amount);
    }

    private void OnDisable()
    {
        amount = 0f;
        if (overlay != null) overlay.SetMark(0f);
    }
}
