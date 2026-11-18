using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Who has the interact key (F, the key window's Player/Interact) right now, among everything in the game that would
/// do something with it. The key does one thing a press: the first thing in front of the player, by an order kept in
/// one table (Tables/Player/InteractKey.csv), and the screen shows that one thing as what F does.
/// <para>
/// Why it exists (the user's item 8, 2026-11-18: "바닥에 떨어진 체력킷, 쉴드쉘이 서포트의 구조물 설치 판정을 막는다"):
/// reading the code for that showed the key had no owner. A takedown, a pickup, a downed friend, a ladder's top, a
/// mission's button, a door, a shop, a Support's resupply and hammer, a Détente's treatment, an ammo crate, a ping:
/// each read the key for itself and kept its own list of the others it must step aside for (FieldPickups.Free,
/// SupportKit.HandsFree, PlayerPing.Busy, DetenteKit.HandsFree, CampaignRun's "not while a pickup is looked at" ...).
/// The lists disagreed, so one press did two things (an ammo crate's take and a pickup, a kind chosen for a barricade
/// and a gun swapped in), and a thing that could not be done still kept the key: a medkit looked at with health full
/// took F from the barricade behind it, which a Support then could not mend.
/// </para>
/// <para>
/// How it works, after how shipped games do it (one finder on the player, interactables that say what they offer, one
/// focus a frame, the press to the focus): each feature says, every frame it has something F would do, what that is
/// (<see cref="Offer"/>: its name, whether it is done by the press or while the key is held, and why it can't be done
/// now if it can't). What was said last frame stands this frame; not said again, it is over (as TimedEffects' effects
/// are), so nothing is ever registered and forgotten. Of what stands, the first by the table's order that takes the
/// press is the one F does (<see cref="Lead"/>), and a feature acts only when it is told the press is its own
/// (<see cref="Pressed"/>, <see cref="Held"/>). An offer that refuses does not take the press: it goes on to the next,
/// and the refusal still shows while nothing that takes the press comes before it (<see cref="Shows"/>), so the
/// screen says both why F does not take the medkit and what F does instead.
/// </para>
/// <para>
/// Last frame's offers decide this frame's press on purpose: they are what was on the screen when the key went down
/// (the user's rule: what I see on my screen is what is true), and it makes the answer the same for every asker
/// whatever order their Updates run in.
/// One press is one act. A press given to something done by the press is spent on it: the key still down afterwards
/// starts nothing else. A press given to something done while held, or to nothing, leaves the hold open, and whatever
/// leads with a held offer has it for as long as it leads: F held on the way to a downed friend gets them up on
/// arrival, and a hammer at work gives way to a friend who goes down beside the barricade.
/// </para>
/// <para>
/// Whether the key is the game's at all (a screen has the body's keys, the window is behind another) is asked before
/// any of this and is not this class's question (InputHold, InputFocus, KeyBindings). A screen reading F as its own
/// (the shop's and the dressing spot's closing key: KeyBindings.Pressed(path, screen)) never comes here.
/// A mod adds something F does with a row of the table and an Offer of its own; nothing here knows any feature by name.
/// Checked without playing: Tools/Checks/interact_key_check.py runs the same rule over the same table, and every press
/// goes to Logs/interact.txt (who offered, who got it, who refused); the console's /interact prints the order as the
/// game read it, what stands now and the last press.
/// </para>
/// </summary>
public static class InteractKey
{
    public const string TableName = "InteractKey", ActionPath = "Player/Interact", RecordName = "interact";
    /// <summary>The row an offer with no row of its own takes its place from (a mod's, until its row is written).</summary>
    public const string OtherRow = "other";

    private const string RecordHeading = "the interact key (InteractKey): PRESS → who got it, then every offer standing then in the table's order "
        + "(Tables/Player/InteractKey.csv): GOT IT | refuses \"why\" (shown greyed, the press goes on) | behind (waits its turn, prompt hidden) "
        + "| HOLD → who took up a key that was down already (something done while held)";

    /// <summary>One thing F would do, as its feature said it.</summary>
    public struct Entry
    {
        public string Id;
        /// <summary>Its place among the others: the table's, smaller first.</summary>
        public int Order;
        /// <summary>Done for as long as the key is held (getting a friend up, a hammer at work), not by the press.</summary>
        public bool Held;
        /// <summary>Why it can't be done now, in the words its own prompt shows; null when F would do it.</summary>
        public string Refusal;
        /// <summary>What it is about, for the record alone (a zombie, a friend, a pickup's name).</summary>
        public Object Target;
        public string About;

        /// <summary>F would do it now.</summary>
        public bool Takes => Refusal == null;
    }

    private static readonly List<Entry> standing = new List<Entry>(8), coming = new List<Entry>(8);
    private static readonly Dictionary<string, int> orders = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> unlisted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private static bool loaded;
    private static int otherOrder = int.MaxValue;
    // The frame `coming` is gathered in, for which `standing` was settled; who leads in it (an index of standing).
    private static int frame = -1, leadAt = -1;
    // The key this frame as the game reads it; the offer its press went to; whether that press is spent on it; and who
    // was last told a held key is theirs (for the record's HOLD line).
    private static bool pressed, down, spent;
    private static string taker, holder, lastPress;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        standing.Clear();
        coming.Clear();
        orders.Clear();
        unlisted.Clear();
        loaded = false;
        otherOrder = int.MaxValue;
        frame = leadAt = -1;
        pressed = down = spent = false;
        taker = holder = lastPress = null;
        GameTables.Reloaded -= Forget;
        GameTables.Reloaded += Forget;
    }

    // The table saved while playing: read again at the next ask.
    private static void Forget()
    {
        loaded = false;
        unlisted.Clear();
    }

    // ---- The rule by itself (pure: Tools/Checks/interact_key_check.py runs these lines over the table) ----

    /// <summary>
    /// One offer comes before another: the smaller order, and of two with one order the one whose name sorts first, so
    /// the answer never depends on whose Update happened to run first.
    /// </summary>
    public static bool Before(in Entry a, in Entry b)
        => a.Order != b.Order ? a.Order < b.Order : string.CompareOrdinal(a.Id, b.Id) < 0;

    /// <summary>
    /// Which of these gets a press made now: the first in order that takes it. One that refuses is passed over, whatever
    /// its place. -1 when none takes it.
    /// </summary>
    public static int Lead(IReadOnlyList<Entry> offers)
    {
        int lead = -1;
        for (int i = 0; i < offers.Count; i++)
            if (offers[i].Takes && (lead < 0 || Before(offers[i], offers[lead]))) lead = i;
        return lead;
    }

    /// <summary>
    /// Whether an offer's own prompt is the screen's to show: the one that leads shows what F does; one that refuses
    /// shows its refusal as long as nothing that takes the press comes before it (had it not refused, F would have been
    /// its own). One that would take the press but stands behind the leader shows nothing: one [F] at a time.
    /// </summary>
    public static bool Shown(IReadOnlyList<Entry> offers, int index)
    {
        if (index < 0 || index >= offers.Count) return false;
        int lead = Lead(offers);
        if (offers[index].Takes) return index == lead;
        return lead < 0 || Before(offers[index], offers[lead]);
    }

    /// <summary>
    /// The key's state after a frame's read. <paramref name="pressed"/>: it went down this frame; <paramref name="down"/>:
    /// it is down; <paramref name="leader"/>: who leads (null: nobody) and whether that is done while held. A press is
    /// given to the leader, and is spent on it unless it is something done while held. Let go, the key is nobody's.
    /// </summary>
    public static void Step(bool pressed, bool down, string leader, bool leaderHeld, ref string taker, ref bool spent)
    {
        if (pressed)
        {
            taker = leader;
            spent = leader != null && !leaderHeld;
        }
        else if (!down)
        {
            taker = null;
            spent = false;
        }
    }

    /// <summary>
    /// Whether a key that is down is <paramref name="id"/>'s: a spent press stays with the one it was spent on and
    /// nobody else; an open hold is the leader's while the leader is something done while held.
    /// </summary>
    public static bool HeldBy(string id, bool down, string taker, bool spent, string leader, bool leaderHeld)
        => down && id != null && (spent ? taker == id : leader == id && leaderHeld);

    // ---- What a feature says and asks ----

    /// <summary>
    /// Said every frame the feature has something F would do, by this PC's own player's side of it. <paramref name="id"/>:
    /// its row of the table. <paramref name="held"/>: it is done for as long as the key is held, not by the press.
    /// <paramref name="refusal"/>: why it can't be done now (the words its prompt shows), null when it can; a refusal
    /// leaves the press to the next offer. <paramref name="target"/> / <paramref name="about"/>: what it is about, for
    /// the record. Not said again the next frame, the offer is over.
    /// </summary>
    public static void Offer(string id, bool held = false, string refusal = null, Object target = null, string about = null)
    {
        if (string.IsNullOrEmpty(id)) return;
        Turn();
        var entry = new Entry { Id = id, Order = OrderOf(id), Held = held, Refusal = refusal, Target = target, About = about };
        for (int i = 0; i < coming.Count; i++)
        {
            if (coming[i].Id != id) continue;
            coming[i] = entry;
            return;
        }
        coming.Add(entry);
    }

    /// <summary>The key went down this frame and the press is this offer's: do it. Nobody else is told so.</summary>
    public static bool Pressed(string id)
    {
        Turn();
        return pressed && taker != null && taker == id;
    }

    /// <summary>
    /// The key is down and it is this offer's: the press was spent on it, or the hold is open and it leads with
    /// something done while held. Nobody else is told so in the same frame.
    /// </summary>
    public static bool Held(string id)
    {
        Turn();
        if (!HeldBy(id, down, taker, spent, LeaderId, LeaderHeld)) return false;
        if (holder != id)
        {
            // A key that was down already, taken up: said once, so a play shows a hold that began on the way there.
            if (PlayRecord.On && !(pressed && taker == id) && !spent)
                PlayRecord.Line(RecordName, RecordHeading, "HOLD  → " + id + " (the key was down already)");
            holder = id;
        }
        return true;
    }

    /// <summary>A press made now would be this offer's.</summary>
    public static bool Leads(string id)
    {
        Turn();
        return leadAt >= 0 && standing[leadAt].Id == id;
    }

    /// <summary>
    /// This offer's prompt may show now (<see cref="Shown"/>): it leads, or it refuses with nothing that takes the press
    /// before it. A feature shows its prompt only while this says so, so the screen never names two things for one key.
    /// </summary>
    public static bool Shows(string id)
    {
        Turn();
        return Shown(standing, IndexOf(id));
    }

    /// <summary>An offer's place: its row's order; without a row, the place the row "other" gives (said once in the log).</summary>
    public static int OrderOf(string id)
    {
        Load();
        if (orders.TryGetValue(id ?? "", out int order)) return order;
        if (unlisted.Add(id ?? ""))
            Debug.LogWarning($"[InteractKey] '{id}' has no row in Tables/Player/{TableName}.csv: it takes the place of the row '{OtherRow}' until one is written.");
        return otherOrder;
    }

    // ---- The frame ----

    private static string LeaderId => leadAt >= 0 ? standing[leadAt].Id : null;
    private static bool LeaderHeld => leadAt >= 0 && standing[leadAt].Held;

    private static int IndexOf(string id)
    {
        for (int i = 0; i < standing.Count; i++) if (standing[i].Id == id) return i;
        return -1;
    }

    // Once a frame, at the first word or question of it: what was said last frame stands now, in order; the key is read
    // once, so every asker of the frame gets one answer; and a press is given.
    private static void Turn()
    {
        int now = Time.frameCount;
        if (now == frame) return;
        standing.Clear();
        // Said in the frame before this one: it stands. Anything older was not said again and is over.
        if (frame == now - 1)
            for (int i = 0; i < coming.Count; i++) Place(coming[i]);
        coming.Clear();
        frame = now;
        leadAt = Lead(standing);
        // The key as the game reads it: not while a screen has the body's keys or the window is behind another
        // (KeyBindings, InputHold), nor in the frame the console, the shop or the perk cards closed by a key
        // (InputFocus.TakesPlayInput), so the F that closes the shop opens no door behind it.
        var action = KeyBindings.Find(ActionPath);
        bool ours = action != null && InputFocus.TakesPlayInput();
        pressed = ours && KeyBindings.Pressed(action);
        down = ours && KeyBindings.Held(action);
        Step(pressed, down, LeaderId, LeaderHeld, ref taker, ref spent);
        if (!down) holder = null;
        if (pressed) Record();
    }

    // Kept in order as they come in (a handful at most).
    private static void Place(in Entry entry)
    {
        int at = standing.Count;
        while (at > 0 && Before(entry, standing[at - 1])) at--;
        standing.Insert(at, entry);
    }

    private static void Load()
    {
        if (loaded) return;
        loaded = true;
        orders.Clear();
        int last = 0;
        foreach (string id in GameTables.RowIds(TableName))
        {
            if (string.IsNullOrEmpty(id) || id[0] == '#' || !GameTables.TryGetRow(TableName, id, out var row)) continue;
            // A row with no number of its own comes right after the row above it.
            last = row.Whole("order", last + 1);
            orders[id.Trim()] = last;
        }
        otherOrder = orders.TryGetValue(OtherRow, out int other) ? other : int.MaxValue;
        if (orders.Count == 0)
            Debug.LogError($"[InteractKey] Tables/Player/{TableName}.csv has no rows: every offer has the same place, and the key goes by name alone.");
    }

    // ---- Checked without playing ----

    // Every press the game read, with what stood then: who got it, who refused, who stood behind.
    private static void Record()
    {
        if (!PlayRecord.On) return;
        var text = new StringBuilder("PRESS → ").Append(taker ?? "nobody");
        if (standing.Count == 0) text.Append(" | nothing was offered");
        for (int i = 0; i < standing.Count; i++)
        {
            var entry = standing[i];
            text.Append(" | ").Append(Describe(entry));
            if (i == leadAt) text.Append(entry.Held ? " GOT IT (done while held)" : " GOT IT");
            else if (!entry.Takes) text.Append(Shown(standing, i) ? " refuses \"" : " refuses, behind \"").Append(entry.Refusal).Append('"');
            else text.Append(" behind");
        }
        lastPress = text.ToString();
        PlayRecord.Line(RecordName, RecordHeading, lastPress);
    }

    private static string Describe(in Entry entry)
    {
        string about = !string.IsNullOrEmpty(entry.About) ? entry.About : entry.Target != null ? entry.Target.name : null;
        return about != null ? entry.Id + " [" + about + "]" : entry.Id;
    }

    /// <summary>
    /// The console's /interact: the table's order as the game read it (a mod's rows laid over it), what stands now and
    /// who has the key, and the last press. With the console open most features offer nothing (the keys are the
    /// console's), so the last press is the line to read.
    /// </summary>
    public static string Dump(string _)
    {
        Load();
        var rows = new List<KeyValuePair<string, int>>(orders);
        rows.Sort((a, b) => a.Value != b.Value ? a.Value.CompareTo(b.Value) : string.CompareOrdinal(a.Key, b.Key));
        var text = new StringBuilder("F 키의 순서 (Tables/Player/InteractKey.csv, 위가 먼저)"); // dev-text
        foreach (var row in rows) text.Append('\n').Append(row.Value.ToString().PadLeft(5)).Append("  ").Append(row.Key);
        if (unlisted.Count > 0) text.Append("\n표에 줄이 없어 other 자리에 선 것: ").Append(string.Join(", ", unlisted)); // dev-text
        if (Application.isPlaying)
        {
            Turn();
            text.Append("\n지금 서 있는 제안 ").Append(standing.Count).Append("개"); // dev-text
            for (int i = 0; i < standing.Count; i++)
            {
                var entry = standing[i];
                text.Append("\n  ").Append(Describe(entry)).Append(i == leadAt ? "  ← F" : !entry.Takes ? "  거절: " + entry.Refusal : "  뒤에서 기다림") // dev-text
                    .Append(entry.Held ? "  (누르고 있는 동안)" : ""); // dev-text
            }
            text.Append("\n키: ").Append(down ? "눌려 있음" : "떼어짐").Append(taker != null ? " · 이번 누름은 " + taker + (spent ? " 것(끝남)" : " 것") : ""); // dev-text
        }
        text.Append("\n마지막 누름: ").Append(lastPress ?? "(없음 — 에디터·개발 빌드에서 F를 누르면 Logs/interact.txt에도 적힌다)"); // dev-text
        return text.ToString();
    }
}
