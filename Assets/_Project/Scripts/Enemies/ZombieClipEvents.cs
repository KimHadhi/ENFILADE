using UnityEngine;

/// <summary>
/// Catches the AnimationEvent_AttackHit events baked into Advanced Zombie AI Pro's demo clips, so Unity has a receiver
/// for them. The strike itself is timed by EnemyCombatAI's windup; ZombieBody only lines the clip up with it.
/// </summary>
[DisallowMultipleComponent]
public sealed class ZombieClipEvents : MonoBehaviour
{
    private void AnimationEvent_AttackHit() { }
}
