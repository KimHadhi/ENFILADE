using System;
using System.Text;

namespace HudAddons
{
    /// <summary>
    /// The addon's extra boss list as our zombie kinds. The addon keeps NPC class names that wear the boss icon besides
    /// those with "boss" in their class (froze_bo6hbar_add_boss / froze_bo6hbar_remove_boss, saved in
    /// data/bo6hbar/bosses.json); here EnemyBarsOptions.bossKinds holds ZombieKind names separated by ';' (saved with the
    /// settings), read into a mask, one bit per kind, only when the text changes.
    /// </summary>
    internal static class EnemyBarsBosses
    {
        private static readonly ZombieKind[] Kinds = (ZombieKind[])Enum.GetValues(typeof(ZombieKind));
        private static readonly char[] Separators = { ';', ',', ' ', '\t' };

        /// <summary>Every kind's bit: the whole list, for naming them all.</summary>
        public const int All = -1;

        public static int Bit(ZombieKind kind) => 1 << (int)kind;

        /// <summary>The kinds named in the list (unknown names are skipped).</summary>
        public static int Mask(string list)
        {
            if (string.IsNullOrWhiteSpace(list)) return 0;
            int mask = 0;
            foreach (var part in list.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
                if (TryKind(part, out var kind)) mask |= Bit(kind);
            return mask;
        }

        /// <summary>The list text of a mask, in ZombieKind's order: no duplicates, unlike the addon's table.insert.</summary>
        public static string Write(int mask)
        {
            var text = new StringBuilder();
            foreach (var kind in Kinds)
            {
                if ((mask & Bit(kind)) == 0) continue;
                if (text.Length > 0) text.Append(';');
                text.Append(kind);
            }
            return text.ToString();
        }

        /// <summary>A kind by its name (Brute, brute) or by its words in the chosen language (Texts zombie.brute).</summary>
        public static bool TryKind(string text, out ZombieKind kind)
        {
            kind = default;
            text = text?.Trim() ?? "";
            if (text.Length == 0) return false;
            foreach (var each in Kinds)
            {
                if (!string.Equals(each.ToString(), text, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(Texts.Get(ZombieType.NameId(each)), text, StringComparison.OrdinalIgnoreCase)) continue;
                kind = each;
                return true;
            }
            return false;
        }

        /// <summary>The kinds of a mask as the console shows them: their words and their names ("거구 (Brute), ...").</summary>
        public static string Names(int mask)
        {
            var text = new StringBuilder();
            foreach (var kind in Kinds)
            {
                if ((mask & Bit(kind)) == 0) continue;
                if (text.Length > 0) text.Append(", ");
                text.Append(Texts.Get(ZombieType.NameId(kind))).Append(" (").Append(kind).Append(')');
            }
            return text.Length > 0 ? text.ToString() : Texts.Get("addon.enemybars.cmd.none");
        }
    }
}
