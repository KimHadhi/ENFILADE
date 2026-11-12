using UnityEngine;

/// <summary>
/// A paired execution's hands kept on their holds (ExecutionPairPlayer.KeepHands): after the hands' IK and FPS Animation
/// Framework, and before the class knife's arms (ExecutionKnifeArm, GunFramework + 5), which leave the arms to the pair by
/// the first hold and seat the knife in the fist where the hold has put it, so on a bigger brute or another character the
/// blade stays in the hand that reached the neck; before the cloth, so a sleeve follows the hand on the zombie's head.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.GunFramework + 4)]
public sealed class ExecutionPairHands : MonoBehaviour
{
    private ExecutionPairPlayer pairs;

    private void LateUpdate()
    {
        if (pairs == null && !TryGetComponent(out pairs)) return;
        pairs.KeepHands();
    }
}
