using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The shaders the game keeps in its build for materials it doesn't use itself: an avatar a player brings names its
/// shaders (ModAvatarFile), and the game can only draw with shaders built into it. A build takes just the shaders its
/// own materials use, so of lilToon's sixty (cutout, transparent, outline, fur, gem, refraction … one file each) only
/// the few Mayo and Ichigo wear would be there, and an avatar using any other would be drawn with a stand-in. This
/// asset, at Resources/Characters/ModShaders, lists them all so the build takes them (the editor fills it:
/// ModShaderLibraryBuilder). Costs build time, once, for the shaders nobody in the game wears.
/// </summary>
public sealed class ModShaderLibrary : ScriptableObject
{
    public const string ResourcePath = "Characters/ModShaders";

    [SerializeField] private Shader[] shaders = Array.Empty<Shader>();

    private static Dictionary<string, Shader> byName;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() => byName = null;

    public IReadOnlyList<Shader> Shaders => shaders ?? Array.Empty<Shader>();

    /// <summary>The shader of this name the game can draw with: one kept here, else whatever Unity finds by that name; null for none.</summary>
    public static Shader Find(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        if (byName == null)
        {
            byName = new Dictionary<string, Shader>(StringComparer.Ordinal);
            var library = Resources.Load<ModShaderLibrary>(ResourcePath);
            if (library != null)
                foreach (var shader in library.Shaders)
                    if (shader != null) byName[shader.name] = shader;
        }
        return byName.TryGetValue(name, out var kept) && kept != null ? kept : Shader.Find(name);
    }

#if UNITY_EDITOR
    /// <summary>Editor step (ModShaderLibraryBuilder): the shaders to keep. True when the list changed.</summary>
    public bool EditorSet(Shader[] next)
    {
        next ??= Array.Empty<Shader>();
        bool same = shaders != null && shaders.Length == next.Length;
        for (int i = 0; same && i < next.Length; i++) same = shaders[i] == next[i];
        if (same) return false;
        shaders = next;
        byName = null;
        return true;
    }
#endif
}
