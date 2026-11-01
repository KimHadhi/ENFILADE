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
    [Serializable]
    public sealed class Skin
    {
        [SerializeField] private string id;
        [SerializeField] private string displayName;
        [SerializeField] private GameObject model;
        [SerializeField, Range(.5f, 1.5f), Tooltip("Scales the ledge hand spacing for this character; palm and finger dimensions are measured from its mesh.")]
        private float ledgeHandSpacingScale = 1f;
        [SerializeField, Range(0f, 2f), Tooltip("Scales how far the measured palm rests inside a ledge edge for this character.")]
        private float ledgePalmInsetScale = 1f;

        public string Id => id;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? id : displayName;
        public GameObject Model => model;
        public float LedgeHandSpacingScale => Mathf.Clamp(ledgeHandSpacingScale, .5f, 1.5f);
        public float LedgePalmInsetScale => Mathf.Clamp(ledgePalmInsetScale, 0f, 2f);
    }

    public const string ResourcePath = "Characters/PlayerBody";

    [Tooltip("Humanoid character model from Art/Models/Characters.")]
    [SerializeField] private GameObject model;
    [Tooltip("Characters offered in the Esc menu. Model above is the default when no saved skin matches.")]
    [SerializeField] private Skin[] skins = Array.Empty<Skin>();
    [Tooltip("Humanoid clips from Art/Animations. Refreshed whenever an animation file is imported.")]
    [SerializeField] private AnimationClip[] clips = Array.Empty<AnimationClip>();
    [Tooltip("Renderers that only cast shadows in first person, because the camera sits inside the head.")]
    [SerializeField] private string[] firstPersonShadowOnly = { "Head", "Hair", "HeadAccessories" };

    public Skin CurrentSkin
    {
        get
        {
            foreach (var skin in skins)
                if (skin != null && skin.Model != null && string.Equals(skin.Id, GameSettings.Current.characterSkin, StringComparison.Ordinal))
                    return skin;
            foreach (var skin in skins)
                if (skin != null && skin.Model == model) return skin;
            return null;
        }
    }
    public GameObject Model => CurrentSkin?.Model ?? model;
    public IReadOnlyList<Skin> Skins => skins;
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
