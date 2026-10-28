using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Which character the player is and which humanoid clips its body may play. Lives at
/// Resources/Characters/PlayerBody and is filled automatically when models and animations are imported
/// (Editor/Import/PlayerBodyProfileBuilder); swap the character by changing Model. PlayerBody picks clips by name.
/// </summary>
[CreateAssetMenu(fileName = "PlayerBody", menuName = "ENFILADE/Player/Body Profile")]
public sealed class PlayerBodyProfile : ScriptableObject
{
    public const string ResourcePath = "Characters/PlayerBody";

    [Tooltip("Humanoid character model from Art/Models/Characters.")]
    [SerializeField] private GameObject model;
    [Tooltip("Humanoid clips from Art/Animations. Refreshed whenever an animation file is imported.")]
    [SerializeField] private AnimationClip[] clips = Array.Empty<AnimationClip>();
    [Tooltip("Renderers that only cast shadows in first person, because the camera sits inside the head.")]
    [SerializeField] private string[] firstPersonShadowOnly = { "Head", "Hair" };

    public GameObject Model => model;
    public IReadOnlyList<AnimationClip> Clips => clips;
    public IReadOnlyList<string> FirstPersonShadowOnly => firstPersonShadowOnly;

    public static PlayerBodyProfile Load()
    {
        var profile = Resources.Load<PlayerBodyProfile>(ResourcePath);
        return profile != null && profile.model != null ? profile : null;
    }

    public AnimationClip Find(string clipName)
    {
        if (string.IsNullOrEmpty(clipName)) return null;
        foreach (var clip in clips)
            if (clip != null && string.Equals(clip.name, clipName, StringComparison.OrdinalIgnoreCase)) return clip;
        return null;
    }

#if UNITY_EDITOR
    /// <summary>Editor import step: keep a chosen model, otherwise take the default; always mirror the clip library.</summary>
    public bool EditorRefresh(GameObject defaultModel, AnimationClip[] library)
    {
        bool changed = false;
        if (model == null && defaultModel != null) { model = defaultModel; changed = true; }
        if (!System.Linq.Enumerable.SequenceEqual(clips, library)) { clips = library; changed = true; }
        return changed;
    }
#endif
}
