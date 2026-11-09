using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Takes the AnimationEvents that KINEMATION's gun clips carry for KINEMATION's own players (the Tactical Shooter Pack's
/// KShooterCore, the FPS Animation Pack's), so Unity has a receiver wherever the clips play: on the body (the clips made
/// for it keep the events), on FPSBodyCamera's camera bone, and on each gun's parts. Without one, Unity logged "has no
/// receiver" for every reload (user, 2026-11-09). WeaponController times the reloads and WeaponFeedback plays their sounds,
/// so nearly every event does nothing here. On a gun, HideBoneByName and UnhideBoneByName hide and show the named part,
/// as the pack's own gun does.
/// </summary>
[DisallowMultipleComponent]
public sealed class KinemationClipEvents : MonoBehaviour
{
    // Parts hidden by the clips, with their own scale to show them again.
    private readonly Dictionary<string, (Transform bone, Vector3 scale)> hidden = new();

    /// <summary>Puts one on <paramref name="target"/>, unless it has one.</summary>
    public static void On(GameObject target)
    {
        if (target != null && !target.TryGetComponent(out KinemationClipEvents _)) target.AddComponent<KinemationClipEvents>();
    }

    // KShooterCore's reload and action bookkeeping, the pack's own sounds and brass, and its suppressor: ours instead.
    private void OnActionStarted() { }
    private void OnActionEnded() { }
    private void ReloadWeapon() { }
    private void PlayEmptyCasing() { }
    private void PlayWeaponSound() { }
    private void PlayRevolverSound() { }
    private void AttachSuppressor() { }
    private void DetachSuppressor() { }

    private void HideBoneByName(string boneName)
    {
        if (string.IsNullOrEmpty(boneName) || hidden.ContainsKey(boneName)) return;
        var bone = Find(transform, boneName);
        if (bone == null) return;
        hidden[boneName] = (bone, bone.localScale);
        bone.localScale = Vector3.zero;
    }

    private void UnhideBoneByName(string boneName)
    {
        if (string.IsNullOrEmpty(boneName) || !hidden.TryGetValue(boneName, out var part)) return;
        hidden.Remove(boneName);
        if (part.bone != null) part.bone.localScale = part.scale;
    }

    // A gun put away mid-reload shows its parts again when it is next drawn.
    private void OnDisable()
    {
        foreach (var (bone, scale) in hidden.Values)
            if (bone != null) bone.localScale = scale;
        hidden.Clear();
    }

    private static Transform Find(Transform root, string name)
    {
        if (root.name == name) return root;
        foreach (Transform child in root)
            if (Find(child, name) is Transform found) return found;
        return null;
    }
}
