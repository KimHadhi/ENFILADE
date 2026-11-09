using System;
using UnityEngine;

/// <summary>
/// The sights a gun can wear, as Rainbow Six Siege lets an operator pick one: the gun's own iron sights, red dots,
/// holographic sights and magnified scopes (GunScope). Every one is on the gun from the start (TacticalGunSetup puts the
/// pack's choices on its sight mount); one shows at a time, with the gun's own iron sights up when it is an iron sight
/// itself or there is none. FPSGun aims through the shown one's aim point; the customize menu picks it (C, GunCustomizeMenu).
/// </summary>
[DisallowMultipleComponent]
public sealed class GunSights : MonoBehaviour
{
    [Serializable]
    public struct Sight
    {
        [Tooltip("The sight on the gun's mount; none for the gun's own iron sights.")]
        public GameObject part;
        [Tooltip("Where the eye lines up through it (FPSAF's aim point).")]
        public Transform aimPoint;
        [Tooltip("The gun's own iron sights stay up with it.")]
        public bool ironSight;
        [Tooltip("A magnified scope's lens, or none.")]
        public GunScope scope;
    }

    public Sight[] sights = Array.Empty<Sight>();
    [Tooltip("The gun's own iron sight parts, folded away under any other sight.")]
    public GameObject[] ironSights = Array.Empty<GameObject>();
    [Tooltip("The sight it starts with.")]
    public int initial;

    public int Current { get; private set; } = -1;
    public int Count => sights != null ? sights.Length : 0;
    private bool Valid => Current >= 0 && Current < Count;
    /// <summary>The shown sight's aim point; null when there is none.</summary>
    public Transform AimPoint => Valid ? sights[Current].aimPoint : null;
    /// <summary>The shown sight's scope, if it is a magnified one.</summary>
    public GunScope Scope => Valid ? sights[Current].scope : null;

    /// <summary>Shows sight <paramref name="index"/> (wrapped round) and hides the others.</summary>
    public void Select(int index)
    {
        if (Count == 0) return;
        Current = ((index % Count) + Count) % Count;
        for (int i = 0; i < Count; i++)
            if (sights[i].part != null) sights[i].part.SetActive(i == Current);
        bool irons = sights[Current].part == null || sights[Current].ironSight;
        foreach (var iron in ironSights)
            if (iron != null) iron.SetActive(irons);
    }
}
