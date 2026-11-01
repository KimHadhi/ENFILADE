using UnityEngine;

/// <summary>
/// Throws a parried attacker's body back on its heels and rocks it to a stop. Visual only: the lean is added after every
/// gameplay Update and taken off again before the next, so navigation and aiming keep the upright rotation. Enemies have
/// no attack clips yet, so this tilts the whole body; a skinned enemy can play a clip instead. Added on demand.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(-200)]
public sealed class EnemyParryRecoil : MonoBehaviour
{
    // Share of the time spent snapping back; the rest rocks and settles.
    private const float Attack = .1f, RockSwings = 2.5f;
    private Vector3 axis;
    private float degrees, seconds = 1f, age;
    private Quaternion steady;
    private bool applied;

    public static EnemyParryRecoil For(Component target)
    {
        if (target == null) return null;
        var recoil = target.GetComponent<EnemyParryRecoil>();
        return recoil != null ? recoil : target.gameObject.AddComponent<EnemyParryRecoil>();
    }

    /// <summary>away is where the blow pushes the body, flat on the ground.</summary>
    public void Kick(Vector3 away, float leanDegrees, float leanSeconds)
    {
        axis = Vector3.Cross(Vector3.up, away).normalized;
        degrees = leanDegrees; seconds = Mathf.Max(.05f, leanSeconds); age = 0f;
        enabled = axis.sqrMagnitude > .5f && degrees > 0f;
    }
    private void Update() => Restore();
    private void LateUpdate()
    {
        age += Time.deltaTime;
        float progress = age / seconds;
        if (progress >= 1f) { enabled = false; return; }
        steady = transform.rotation;
        transform.rotation = Quaternion.AngleAxis(Lean(progress) * degrees, axis) * steady;
        applied = true;
    }
    private static float Lean(float progress)
    {
        if (progress < Attack) { float rest = 1f - progress / Attack; return 1f - rest * rest * rest; }
        float settle = (progress - Attack) / (1f - Attack);
        return (1f - settle) * (1f - settle) * Mathf.Cos(settle * RockSwings * Mathf.PI);
    }
    private void Restore()
    {
        if (!applied) return;
        applied = false;
        transform.rotation = steady;
    }
    private void OnDisable() => Restore();
}
