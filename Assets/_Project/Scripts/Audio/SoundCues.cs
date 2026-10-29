using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// What each action sounds like, from Resources/Tables/SoundCues.csv, so a vault or a landing is retuned in the table.
/// Code fires a cue by name (Vault, Land, StepRun…); its "sounds" cell lists what plays, separated by ';':
///   Movement/Cloth/VaultSwish          a sound bank and key (Tables/Sounds rows)
///   Movement/HandHard/{surface}        the key for the surface the code passes (falls back like SoundBank.Find)
///   … *0.5                             a volume scale
///   … +0.1                             seconds after the cue
/// "noise" is how far the cue carries for enemy hearing (m). A cue may have surface versions: "Vault/Gantry" is
/// used on a gantry before "Vault".
/// </summary>
public static class SoundCues
{
    public const string SurfaceMarker = "/{surface}";
    private const string Table = "SoundCues";

    public readonly struct Item
    {
        public readonly string Bank, Key;
        public readonly bool OnSurface;
        public readonly float Volume, Delay;

        public Item(string bank, string key, bool onSurface, float volume, float delay)
        {
            Bank = bank; Key = key; OnSurface = onSurface; Volume = volume; Delay = delay;
        }
    }

    public sealed class Cue
    {
        public string Id;
        public Item[] Items;
        public float Noise;
    }

    private static Dictionary<string, Cue> cues;
    private static readonly HashSet<string> reported = new HashSet<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        cues = null;
        reported.Clear();
        GameTables.Reloaded -= Forget;
        GameTables.Reloaded += Forget;
    }

    private static void Forget() { cues = null; reported.Clear(); }

    /// <summary>The cue's surface version ("Vault/Gantry", following the surface's fallbacks), else the cue; null if neither exists.</summary>
    public static Cue Find(string id, string surface)
    {
        var all = Load();
        if (!string.IsNullOrEmpty(surface))
            foreach (string candidate in Surfaces.Fallbacks(surface))
                if (all.TryGetValue(id + "/" + candidate, out var version)) return version;
        if (all.TryGetValue(id ?? "", out var cue)) return cue;
        Report($"no row '{id}'.");
        return null;
    }

    private static Dictionary<string, Cue> Load()
    {
        if (cues != null) return cues;
        cues = new Dictionary<string, Cue>(StringComparer.OrdinalIgnoreCase);
        foreach (string id in GameTables.RowIds(Table))
        {
            if (!GameTables.TryGetRow(Table, id, out var row)) continue;
            row.TryGetValue("sounds", out string sounds);
            row.TryGetValue("noise", out string noise);
            var items = new List<Item>();
            foreach (string part in (sounds ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
                if (TryParse(part, out var item)) items.Add(item);
                else Report($"'{id}': can't read '{part.Trim()}'. Write bank/key, then *volume or +delay.");
            float.TryParse(noise, NumberStyles.Float, CultureInfo.InvariantCulture, out float reach);
            cues[id] = new Cue { Id = id, Items = items.ToArray(), Noise = Mathf.Max(0f, reach) };
        }
        return cues;
    }

    private static bool TryParse(string text, out Item item)
    {
        item = default;
        var tokens = text.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return false;
        string path = tokens[0];
        int slash = path.IndexOf('/');
        if (slash <= 0 || slash == path.Length - 1) return false;
        string bank = path.Substring(0, slash), key = path.Substring(slash + 1);
        bool onSurface = key.EndsWith(SurfaceMarker, StringComparison.OrdinalIgnoreCase);
        if (onSurface) key = key.Substring(0, key.Length - SurfaceMarker.Length);
        if (key.Length == 0) return false;
        float volume = 1f, delay = 0f;
        for (int i = 1; i < tokens.Length; i++)
        {
            string token = tokens[i];
            if (token.Length < 2 || !float.TryParse(token.Substring(1), NumberStyles.Float, CultureInfo.InvariantCulture, out float value))
                return false;
            if (token[0] == '*') volume = Mathf.Max(0f, value);
            else if (token[0] == '+') delay = Mathf.Max(0f, value);
            else return false;
        }
        item = new Item(bank, key, onSurface, volume, delay);
        return true;
    }

    private static void Report(string message)
    {
        if (reported.Add(message)) Debug.LogWarning("[SoundCues] " + message);
    }
}
