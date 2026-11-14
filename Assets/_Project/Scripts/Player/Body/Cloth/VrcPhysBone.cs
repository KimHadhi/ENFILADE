using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One VRChat PhysBone's settings, as VRChat's documentation names them: what sways and how its maker tuned it. Read
/// from a BOOTH prefab's own text by the editor's import (VrcPrefab), or from the file a mod's avatar brings
/// (ModAvatars), and turned into the game's own sway by VrcSway either way.
/// </summary>
public sealed class VrcPhysBone
{
    public Transform Root;
    public readonly List<Transform> Ignore = new List<Transform>();
    /// <summary>The bones its colliders hang from.</summary>
    public readonly List<Transform> Colliders = new List<Transform>();
    public Vector3 Endpoint;
    public int Integration, LimitType;
    public float Pull, Spring, Stiffness, Gravity, GravityFalloff, Immobile, Radius, MaxAngleX, MaxAngleZ;
    /// <summary>
    /// How each value runs down the chain, root (0) to far end (1), as a share of it: VRChat's curve beside each
    /// value. Null where the maker left it the same all the way.
    /// </summary>
    public AnimationCurve PullCurve, SpringCurve, StiffnessCurve, GravityCurve, ImmobileCurve, RadiusCurve, MaxAngleXCurve, MaxAngleZCurve;
    public bool Collides;

    /// <summary>A value at a depth of the chain (0 root, 1 far end), its curve applied.</summary>
    public static float At(float value, AnimationCurve curve, float depth) => curve != null ? value * curve.Evaluate(depth) : value;
}
