using UnityEngine;

/// <summary>Runs first in the frame and removes last frame's view punch before gameplay reads the camera.</summary>
[DisallowMultipleComponent, DefaultExecutionOrder(-10000)]
public sealed class CameraViewPunchRestore : MonoBehaviour
{
    private CameraViewPunch punch;
    private void Update()
    {
        if (punch == null) punch = GetComponent<CameraViewPunch>();
        if (punch != null) punch.Restore();
    }
}
