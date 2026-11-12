using UnityEngine;

/// <summary>
/// A paired execution's hands kept on their holds (ExecutionPairPlayer.KeepHands): last of the arm work each frame, after
/// the hands' IK, FPS Animation Framework and the class knife's arms (ExecutionKnifeArm, GunFramework + 5), before the
/// cloth, so a sleeve follows the hand on the zombie's head and the knife's fist on its neck.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.GunFramework + 6)]
public sealed class ExecutionPairHands : MonoBehaviour
{
    private ExecutionPairPlayer pairs;

    private void LateUpdate()
    {
        if (pairs == null && !TryGetComponent(out pairs)) return;
        pairs.KeepHands();
    }
}
