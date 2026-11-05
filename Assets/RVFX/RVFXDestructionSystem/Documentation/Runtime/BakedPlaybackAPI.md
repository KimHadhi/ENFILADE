# Baked Playback Runtime API

`RVFXRigAPI.GetBakedPlayback` resolves one generated baked prefab's shared
Base and Debris timeline. Resolve the handle once and keep it instead of
searching every frame.

```csharp
using RVFX.DestructionSystem;
using UnityEngine;

public sealed class BakedDestructionTrigger : MonoBehaviour
{
    [SerializeField] private GameObject bakedPrefab;
    private RVFXBakedPlayback _playback;

    private void Awake()
    {
        _playback = RVFXRigAPI.GetBakedPlayback(bakedPrefab);
    }

    public void Trigger()
    {
        if (_playback != null)
            _playback.PlayDelayed(0.15f);
    }

    public void ResetPlayback()
    {
        if (_playback != null)
            _playback.Stop();
    }
}
```

Available controls are `Play`, `PlayDelayed`, `Pause`, `Stop`, and `Restart`.
The handle also exposes `IsValid`, `IsPlaying`, `IsWaitingForDelay`, and
`CurrentFrame`. These calls do not enable an inactive GameObject.

`RVFXBakedPlayback.Get(target)` is the shorter equivalent when only baked
playback control is needed.

## Placement validation

Playback commands do not bypass Transform validation. Exact supports arbitrary XYZ rotation
and positive uniform scale. With Adaptive Landing enabled, the root must remain world-up aligned
(Y rotation only). Parents, non-uniform scale, shear and zero/negative scale are checked too.
Invalid placement rejects Play and seek; an invalid Transform introduced during playback pauses
it, clears effects and disables debris drawing and collision tracks. Fix the Transform before
starting playback again. RVFX does not automatically correct it. See [Playback transforms](PlaybackTransforms.md).
