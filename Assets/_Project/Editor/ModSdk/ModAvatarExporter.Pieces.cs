// dev-text: file (the exporter's window and report, read by whoever makes a mod; nothing here is shown to players)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// ModAvatarExporter: which mesh goes in which slot of the wardrobe. One part of the class; the rest is in
// ModAvatarExporter.cs and the files beside it.
public sealed partial class ModAvatarExporter
{
    // What the maker chose for meshes, by mesh name: a slot's id, or Leave. A mesh not listed is sorted by the rules.
    [Serializable]
    private sealed class Choices
    {
        public List<string> meshes = new List<string>(), slots = new List<string>();
        // Goes up with every change, so the window's list is worked out again.
        [NonSerialized] public int Stamp;

        public void Set(string mesh, string slot)
        {
            int at = meshes.IndexOf(mesh);
            if (at >= 0)
            {
                meshes.RemoveAt(at);
                if (at < slots.Count) slots.RemoveAt(at);
            }
            if (!string.IsNullOrEmpty(slot))
            {
                meshes.Add(mesh);
                slots.Add(slot);
            }
            Stamp++;
        }

        public void Clear()
        {
            meshes.Clear();
            slots.Clear();
            Stamp++;
        }

        public Dictionary<string, string> Map()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < meshes.Count && i < slots.Count; i++) map[meshes[i]] = slots[i];
            return map;
        }
    }

    // One mesh in the window's list: what the rules or the maker's choice make of it, and the choices offered.
    private sealed class Listed
    {
        public string Name, Tip;
        public string[] Options;
        public int Choice;
    }

    private List<Listed> listed;
    private string listedFor, listedNote = "";
    private bool showPieces = true;
    // The meshes as measured, kept while the same things are in the window: measuring reads every vertex.
    private List<ModPieces.Piece> measured;
    private ModPieces.Body measuredBody;
    private ModPieces.Frame measuredFrame;
    private string measuredFor;

    // ---- The window's list ----

    // The meshes of what is being exported, each with the slot it goes in and a menu to change it.
    private void PiecesGUI(bool own)
    {
        var source = own ? avatar : outfit;
        if (source == null) return;
        var choices = own ? avatarChoices : outfitChoices;
        var body = own ? source : wearer;
        string what = (own ? "c" : "o") + source.GetInstanceID() + "/" + (body != null ? body.GetInstanceID() : 0);
        string key = what + "/" + (own ? id + "|" + displayName : outfitId + "|" + outfitName + "|" + wearerId) + "/" + choices.Stamp + "/"
            + choices.meshes.Count;
        if (listed == null || listedFor != key) List(own, source, body, choices, what, key);
        showPieces = EditorGUILayout.Foldout(showPieces, listedNote, true);
        if (!showPieces) return;
        EditorGUILayout.HelpBox(own
            ? "이름으로 옷이라고 알아본 메시는 옷장 칸에 들어가, 게임에서 벗기거나 이 캐릭터용 옷으로 갈아입힐 수 있습니다. 알아보지 못한 메시는 몸으로 보고 늘 입힌 채 둡니다. 틀린 것은 오른쪽에서 고르세요."
            : "메시마다 옷장의 어느 칸에 들어갈지입니다. 같은 칸의 메시는 한 벌로 묶이고, 종류가 다르면 따로도 고를 수 있게 나옵니다. 틀린 것은 오른쪽에서 고르세요.",
            MessageType.None);
        foreach (var row in listed)
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField(new GUIContent(row.Name, row.Tip), GUILayout.MinWidth(150f));
                int next = EditorGUILayout.Popup(row.Choice, row.Options, GUILayout.Width(230f));
                if (next != row.Choice) choices.Set(row.Name, next == 0 ? "" : next == 1 ? Leave : ModPieces.Slots[next - 2].Id);
            }
        if (choices.meshes.Count > 0 && GUILayout.Button("모두 자동으로 되돌리기", GUILayout.Width(170f))) choices.Clear();
    }

    private void List(bool own, GameObject source, GameObject body, Choices choices, string what, string key)
    {
        listedFor = key;
        listed = new List<Listed>();
        try
        {
            if (measured == null || measuredFor != what)
            {
                measuredFor = what;
                // An outfit standing under its avatar is no part of that avatar's body.
                measuredBody = ModPieces.Body.Of(body, own ? null : source.transform);
                measuredFrame = ModPieces.Frame.Of(body, source);
                measured = ModPieces.Measure(source, measuredBody, null, new HashSet<string>(), true, own);
            }
            // The names typed above decide which words a mesh's name starts with; read again, the meshes need no measuring.
            var prefix = own ? Prefix(id, displayName, source.name) : Prefix(outfitId, outfitName, source.name, wearerId == ModAvatarFile.Anyone ? "" : wearerId);
            foreach (var piece in measured) piece.Words = ModPieces.PieceWords(piece.Name, prefix);
            var map = choices.Map();
            var active = Arrange(measured, map, measuredBody, measuredFrame, own, out int unknown);
            var slots = new SortedDictionary<int, int>();
            int placed = 0, left = 0;
            foreach (var piece in measured)
            {
                string name = Tidy(piece.Name);
                map.TryGetValue(name, out string choice);
                bool sorted = active.Contains(piece) && piece.Slot != null;
                if (sorted)
                {
                    placed++;
                    int at = Array.FindIndex(ModPieces.Slots, slot => string.Equals(slot.Id, piece.Slot, StringComparison.OrdinalIgnoreCase));
                    slots[at] = slots.TryGetValue(at, out int count) ? count + 1 : 1;
                }
                else left++;
                string result = sorted ? ModPieces.LabelOf(piece.Slot) + (piece.Host != null ? " (" + Tidy(piece.Host.Name) + "의 장식)" : "")
                    : piece.Off ? (own ? "꺼져 있음: 그대로 둠" : "꺼져 있음: 넣지 않음")
                    : piece.Copy ? "캐릭터 몸의 사본: 넣지 않음"
                    : own ? "몸: 늘 입힘" : "칸을 모름: 넣지 않음";
                // A / in a menu's words would open a sub-menu.
                var options = new List<string> { string.IsNullOrEmpty(choice) ? "자동 → " + result.Replace('/', ' ') : "자동", own ? "몸에 붙임 (벗지 않음)" : "넣지 않음" };
                foreach (var (_, label) in ModPieces.Slots) options.Add(label);
                int picked = string.IsNullOrEmpty(choice) ? 0 : choice == Leave ? 1
                    : 2 + Math.Max(0, Array.FindIndex(ModPieces.Slots, slot => string.Equals(slot.Id, choice, StringComparison.OrdinalIgnoreCase)));
                listed.Add(new Listed { Name = name, Tip = result, Options = options.ToArray(), Choice = picked });
            }
            var per = slots.Select(pair => (pair.Key >= 0 ? ModPieces.Slots[pair.Key].Label : "?") + " " + pair.Value);
            listedNote = own
                ? $"옷 칸: 메시 {placed}개를 칸에 나눔" + (placed > 0 ? " (" + string.Join(", ", per) + ")" : "") + $" · 몸으로 둠 {left}개"
                : $"옷 칸: 메시 {placed}개 → 옷 {Items(active, "x", "x", new HashSet<string>()).Length}개" + (placed > 0 ? " (" + string.Join(", ", per) + ")" : "")
                    + (left > 0 ? $" · 넣지 않음 {left}개" : "") + (unknown > 0 ? $" · 칸을 모르는 메시 {unknown}개는 칸을 골라야 들어갑니다" : "");
        }
        catch (Exception error)
        {
            listed.Clear();
            listedNote = "옷 칸: 메시를 읽지 못했습니다 (" + error.Message + ")";
        }
    }

    // ---- Sorting, for the list and for the export alike ----

    // The meshes that go in the mod, each with its slot: the maker's choice first (a slot, or Leave), else the rules'
    // (ModPieces.Sort). Left out with no choice: a mesh switched off as the prefab comes (the other side of a toggle),
    // but not underwear, which avatars keep switched off under their clothes and the game puts on by itself; and a
    // copy of the character's own body carried in an outfit. A mesh nothing places keeps a null slot.
    private static List<ModPieces.Piece> Arrange(List<ModPieces.Piece> pieces, IReadOnlyDictionary<string, string> choices, ModPieces.Body body,
        ModPieces.Frame frame, bool own, out int unknown)
    {
        var active = new List<ModPieces.Piece>();
        foreach (var piece in pieces)
        {
            piece.Given = piece.IsTrim = piece.Both = false;
            piece.Slot = null;
            piece.Host = null;
            string choice = null;
            choices?.TryGetValue(Tidy(piece.Name), out choice);
            if (choice == Leave) continue;
            // A character's own mesh that is switched off stays as it is: switched on, it would show beside the one worn.
            if (!string.IsNullOrEmpty(choice) && ModPieces.IsSlot(choice) && !(own && piece.Off && !piece.Under))
            {
                piece.Given = true;
                piece.Slot = ModPieces.Slots.First(slot => string.Equals(slot.Id, choice, StringComparison.OrdinalIgnoreCase)).Id;
                active.Add(piece);
                continue;
            }
            if (piece.Copy || (piece.Off && !piece.Under)) continue;
            if (piece.Under)
            {
                piece.Slot = ModPieces.UnderSlot(piece.Renderer, frame, out bool both);
                // A character's own one-piece goes in the top's row: its own piece takes no other slot.
                piece.Both = both && !own;
            }
            active.Add(piece);
        }
        unknown = ModPieces.Sort(active, body, ModPieces.Sortable, !own);
        return active;
    }

    // A character's own clothes, slot by slot in the wardrobe's order. A top named a one-piece (a dress) is listed under
    // the bottom too, as the game lists its own characters' dresses: putting on another skirt takes the dress off.
    private static ModAvatarFile.Part[] Parts(List<ModPieces.Piece> active)
    {
        var bySlot = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        void Add(string slot, string mesh)
        {
            if (!bySlot.TryGetValue(slot, out var meshes)) bySlot[slot] = meshes = new List<string>();
            if (!meshes.Contains(mesh)) meshes.Add(mesh);
        }
        foreach (var piece in active)
        {
            if (piece.Slot == null) continue;
            Add(piece.Slot, piece.Name);
            if (!piece.Given && piece.Slot == "top" && ModPieces.IsOnePiece(piece.Name)) Add("bottom", piece.Name);
        }
        var parts = new List<ModAvatarFile.Part>();
        foreach (var (slot, _) in ModPieces.Slots)
            if (bySlot.TryGetValue(slot, out var meshes)) parts.Add(new ModAvatarFile.Part { slot = slot, meshes = meshes.ToArray() });
        return parts.ToArray();
    }

    // What can be worn from an outfit: the sorted meshes as items (ModPieces.Items), each with an id of its own.
    private static ModAvatarFile.Item[] Items(List<ModPieces.Piece> active, string id, string title, ICollection<string> prefix)
    {
        var items = new List<ModAvatarFile.Item>();
        var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (itemId, slot, also, parts, words) in ModPieces.Items(active, id, prefix, title))
        {
            string unique = itemId;
            for (int n = 2; !ids.Add(unique); n++) unique = itemId + "-" + n;
            items.Add(new ModAvatarFile.Item { id = unique, slot = slot, also = also, name = words.Trim(), meshes = parts.ToArray() });
        }
        return items.ToArray();
    }

    // The words a maker starts mesh names with (the avatar's or the outfit's own name): skipped when a name is read.
    private static HashSet<string> Prefix(params string[] names)
    {
        var words = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string name in names)
            foreach (string word in ModPieces.Words(name)) words.Add(word);
        return words;
    }

    /// <summary>
    /// A mesh object's name as the game's tables can list it: no '|' (the tables' list mark) and no blank at either end.
    /// The copy's meshes are renamed so before anything reads them; a choice is kept under this name.
    /// </summary>
    public static string Tidy(string name)
    {
        string tidy = (name ?? "").Replace('|', '_').Trim();
        return tidy.Length > 0 ? tidy : "Mesh";
    }

    private static void TidyNames(GameObject clone)
    {
        foreach (var renderer in clone.GetComponentsInChildren<Renderer>(true))
        {
            string tidy = Tidy(renderer.name);
            if (tidy != renderer.name) renderer.name = tidy;
        }
    }

    // "헤어 7, 상의 2 …" for a report: how many meshes each slot got.
    private static string Count(IEnumerable<string> slots)
    {
        var text = new StringBuilder();
        foreach (var (slot, label) in ModPieces.Slots)
        {
            int count = slots.Count(each => string.Equals(each, slot, StringComparison.OrdinalIgnoreCase));
            if (count > 0) text.Append(text.Length > 0 ? ", " : "").Append(label).Append(' ').Append(count);
        }
        return text.ToString();
    }
}
