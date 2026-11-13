using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Which character the player is and which humanoid clips its body may play. Lives at
/// Resources/Characters/PlayerBody and is filled automatically when models and animations are imported
/// (Editor/Characters/PlayerBodyProfileBuilder); swap the character by changing Model. PlayerBody picks clips by name.
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
        [SerializeField, Tooltip("Colour sets offered on the character screen; the first is the default. " +
            "Empty: the model as imported is the only look.")]
        private SkinVariant[] variants = Array.Empty<SkinVariant>();

        public Skin() { }

        public Skin(string id) => this.id = id;

        public string Id => id;
        public string DisplayName => string.IsNullOrEmpty(displayName) ? id : displayName;
        public GameObject Model => model;
        public float LedgeHandSpacingScale => Mathf.Clamp(ledgeHandSpacingScale, .5f, 1.5f);
        public float LedgePalmInsetScale => Mathf.Clamp(ledgePalmInsetScale, 0f, 2f);
        public IReadOnlyList<SkinVariant> Variants => variants ?? Array.Empty<SkinVariant>();

        /// <summary>The colour set with this id, else the first; null when the skin has none.</summary>
        public SkinVariant VariantOf(string variantId)
        {
            if (variants == null || variants.Length == 0) return null;
            foreach (var variant in variants)
                if (variant != null && string.Equals(variant.Id, variantId, StringComparison.Ordinal)) return variant;
            return variants[0];
        }

#if UNITY_EDITOR
        /// <summary>Editor import step (BoothImport): the character as its package has it. Hand-tuned ledge scales stay.</summary>
        public void EditorSet(string name, GameObject character, SkinVariant[] colours)
        {
            displayName = name;
            model = character;
            variants = colours ?? Array.Empty<SkinVariant>();
        }
#endif
    }

    public const string ResourcePath = "Characters/PlayerBody";
    // Joins the skin and its colour set in what other players receive. Neither id may contain it.
    private const char LookSeparator = '#';

    [Tooltip("Humanoid character model from Art/Models/Characters.")]
    [SerializeField] private GameObject model;
    [Tooltip("Characters offered in the Esc menu. Model above is the default when no saved skin matches.")]
    [SerializeField] private Skin[] skins = Array.Empty<Skin>();
    [Tooltip("Humanoid clips from Art/Animations. Refreshed whenever an animation file is imported.")]
    [SerializeField] private AnimationClip[] clips = Array.Empty<AnimationClip>();
    [Tooltip("Renderers that only cast shadows in first person, because the camera sits inside the head.")]
    [SerializeField] private string[] firstPersonShadowOnly = { "Head", "Hair", "HeadAccessories" };

    public Skin CurrentSkin => SkinOf(GameSettings.Current.characterSkin);
    /// <summary>The colour set this PC wears on <see cref="CurrentSkin"/>; null when it has none.</summary>
    public SkinVariant CurrentVariant => CurrentSkin?.VariantOf(GameSettings.Current.characterVariant);
    public GameObject Model => ModelOf(CurrentSkin);
    public IReadOnlyList<Skin> Skins => skins;
    public IReadOnlyList<string> FirstPersonShadowOnly => firstPersonShadowOnly;

    public static PlayerBodyProfile Load()
    {
        var profile = Resources.Load<PlayerBodyProfile>(ResourcePath);
        return profile != null && profile.model != null ? profile : null;
    }

    /// <summary>The skin with this id, or the default character's when none matches.</summary>
    public Skin SkinOf(string id)
    {
        foreach (var skin in skins)
            if (skin != null && skin.Model != null && string.Equals(skin.Id, id, StringComparison.Ordinal)) return skin;
        foreach (var skin in skins)
            if (skin != null && skin.Model == model) return skin;
        return null;
    }

    public GameObject ModelOf(Skin skin) => (skin?.Model).OrNull() ?? model;

    /// <summary>A skin and its colour set as one string, "skin#variant", or the skin alone when there is no colour set.</summary>
    public static string LookId(string skinId, string variantId) =>
        string.IsNullOrEmpty(variantId) ? skinId ?? "" : (skinId ?? "") + LookSeparator + variantId;

    /// <summary>
    /// Splits what <see cref="LookId"/> made. A plain skin id, as older builds send, has no colour set. An outfit sent
    /// after the look (OutfitLook.Join) is left out.
    /// </summary>
    public static void SplitLook(string look, out string skinId, out string variantId)
    {
        look = OutfitLook.SkinPart(look);
        int split = look.IndexOf(LookSeparator);
        skinId = split < 0 ? look : look.Substring(0, split);
        variantId = split < 0 ? "" : look.Substring(split + 1);
    }

    public AnimationClip Find(string clipName)
    {
        if (string.IsNullOrEmpty(clipName)) return null;
        foreach (var clip in clips)
            if (clip != null && string.Equals(clip.name, clipName, StringComparison.OrdinalIgnoreCase)) return clip;
        return null;
    }

#if UNITY_EDITOR
    /// <summary>Editor import step (BoothImport): adds the character, or updates the one with this id.</summary>
    public void EditorPutSkin(string id, string name, GameObject character, SkinVariant[] colours)
    {
        foreach (var skin in skins)
            if (skin != null && string.Equals(skin.Id, id, StringComparison.Ordinal))
            {
                skin.EditorSet(name, character, colours);
                return;
            }
        var added = new Skin(id);
        added.EditorSet(name, character, colours);
        var list = new List<Skin>(skins ?? Array.Empty<Skin>()) { added };
        skins = list.ToArray();
    }

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
