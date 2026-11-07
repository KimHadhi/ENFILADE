using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using UnityEngine;

/// <summary>
/// KINEMATION Character Animation System's Character Animation Component on the body, as it ships, run after BodyCas
/// has fed it this frame's look and leg weights (BodyCas, 296), so the spine follows this frame's view.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(297)]
public sealed class BodyCasAnimation : CharacterAnimationComponent
{
}
