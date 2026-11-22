using System;
using System.Collections.Generic;
using UnityEngine;

namespace HudAddons
{
    /// <summary>
    /// The addons' pictures (their Material("...") pngs and vtfs), loaded once by name and kept. They live in
    /// Art/Resources/UI/Addons/&lt;Module&gt;/... (PlayerHud, HitFeedback, EnemyBars, Grenades, DeathScreen), brought in at
    /// full size by Tools/HudAddons/addon_picture.py (which decodes a .vtf and writes the UI picture's .meta), and are
    /// asked for as "&lt;Module&gt;/&lt;name&gt;" without the extension.
    /// </summary>
    public static class Pictures
    {
        /// <summary>The Resources folder every addon's pictures sit under.</summary>
        public const string Folder = "UI/Addons/";

        // Misses are kept too (as null), so a missing picture is reported once, not every frame it is drawn.
        private static readonly Dictionary<string, Texture2D> loaded = new Dictionary<string, Texture2D>(StringComparer.OrdinalIgnoreCase);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => loaded.Clear();

        /// <summary>The picture at Resources/UI/Addons/&lt;path&gt;, or null (reported once) when there is none.</summary>
        public static Texture2D Get(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (loaded.TryGetValue(path, out var picture)) return picture;
            picture = Resources.Load<Texture2D>(Folder + path);
            if (picture == null)
                Debug.LogWarning($"[HudAddons] No picture at Art/Resources/{Folder}{path}.png (Tools/HudAddons/addon_picture.py brings an addon's .vtf or .png in).");
            loaded[path] = picture;
            return picture;
        }
    }
}
