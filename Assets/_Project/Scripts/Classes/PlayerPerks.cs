using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// The perk cards this PC's player has picked (stage 6C, the user's rules on 2026-11-09): a full ultimate gauge buys a
/// pick of one card from three (PerkCardScreen), each card raises one perk a level, and the perks last the whole
/// campaign, from chapter to chapter, until the title screen. The cards and their numbers are Tables/Perks. Only this
/// player's own perks exist here; each one works where its effect is judged, which is always this PC (the shooter's
/// hits, this player's health, getting a friend up). The effect lookups below are what the game's systems ask.
/// <para>
/// Class cards (2026-11-11, the user's "클래스별 스킬(카드)"): a card with a class in its row is dealt only to a player of
/// that class, and before the cards everyone can draw (Draw, 2026-11-12); and a card naming a ClassAbilities number
/// ("ability") simply adds to it, so most class cards need no code (PlayerClass.Ability reads the sum). The final cards
/// that once came after four levels of a class's cards are gone (2026-11-14, the user: "이미 궁극기를 쓸 수 있게 된다는
/// 특권을 얻는 거에서 궁극 카드가 의미 없잖아. 없애 줘."): every card taken, X is the class's ultimate. Every card says
/// why it does what it does (the why line, the user's rule that an effect comes with a reason that makes it believable).
/// What the code-backed class cards do is ClassCards' (Player/Classes).
/// </para>
/// <para>
/// In the hub the picks are practice (BeginPractice, 2026-11-12): dealt by the same rule from the range's kills, working
/// as cards do, and gone again when the hub is left or a class is picked there (EndPractice, RestartPractice), so the
/// campaign's own cards are what a chapter gets.
/// </para>
/// <para>
/// Decks (2026-11-18). Every card belongs to one deck by its own row: its class's, or everyone's when it names none. What
/// was picked is kept by card for the whole campaign, but only one deck is in play: the class played now and everyone's
/// cards. Every question the game asks (Level, Has, Total, Held, AbilityBonus and all the effect lookups under them)
/// reads the deck in play and nothing else, so a card of another class can show nowhere and do nothing, whatever asks.
/// Before, the picks were one list with no deck in it, and each lookup had to remember to ask the class for itself:
/// four did not (the Knight's butcher, the Détente's rescue, the Schießhund's heriot, the Assault's momentum), and the
/// Tab board listed every card held, so a class changed in the hub after a chapter kept the old class's effects and
/// showed its cards (the user: "챕터 클리어 후 허브에서 클래스 변경 시 이전 클래스 카드 효과가 남고 Tab 메뉴에 이전 클래스
/// 카드가 노출되는 문제"). A class left keeps its deck: picked again in the same campaign, its cards are in play again as
/// they were, so nothing earned is thrown away by trying another class.
/// </para>
/// </summary>
public static class PlayerPerks
{
    public const string TableName = "Perks";

    /// <summary>One card in the table.</summary>
    public sealed class Perk
    {
        public string Id, Name, Class, Tag, Icon, Text, Why, Ability;
        public int Levels;
        public float Value, Value2, Display = 1f, Weight;
        /// <summary>
        /// What the card adds in all at each level, when its value cell lists them ("-4/-10/-16"), or null when every
        /// level adds the same value. Some steps the user asked for are not even (2026-11-14, the Knight's cover wait
        /// "기본 24초 … 20초 / 14초 / 8초"), which value × level can't give.
        /// </summary>
        public float[] Steps;
        /// <summary>
        /// The same for the second number, when its value2 cell lists each level's own ("45/40/35"), or null. A card may
        /// have two numbers that move apart: the Assault's 데인저 클로즈 drops more shells and comes back sooner each
        /// level (2026-11-14: 6/9/12 shells, 45/40/35 s).
        /// </summary>
        public float[] Steps2;
        /// <summary>Dealt only to a player alone (the user, 2026-11-14, of 두 번째 숨: "솔로일 때만 나와서").</summary>
        public bool Solo;

        /// <summary>Everything the card adds in all at <paramref name="level"/> (0 below the first).</summary>
        public float At(int level)
        {
            if (level <= 0) return 0f;
            return Steps != null ? Steps[Mathf.Min(level, Steps.Length) - 1] : Value * level;
        }

        /// <summary>The second number at <paramref name="level"/>: its listed step, or value2 × level (0 below the first).</summary>
        public float At2(int level)
        {
            if (level <= 0) return 0f;
            return Steps2 != null ? Steps2[Mathf.Min(level, Steps2.Length) - 1] : Value2 * level;
        }

        /// <summary>
        /// The effect line at <paramref name="level"/>: {0} is everything up to that level (At, times its display
        /// factor), {1} the same of value2 (At2), and {Row.name} a number of Tables/ClassAbilities as it stands.
        /// </summary>
        public string Describe(int level)
        {
            int at = Mathf.Max(0, level);
            string text = string.IsNullOrEmpty(Text) ? "{0}" : Text;
            text = text.Replace("{0}", Shown(At(at) * Display)).Replace("{1}", Shown(At2(at)));
            return Abilities(text);
        }

        /// <summary>The why line, its {Row.name} numbers filled in.</summary>
        public string Reason => string.IsNullOrEmpty(Why) ? "" : Abilities(Why);
    }

    // The perk ids the code knows; the table can add others for later. Four of the old common cards became class cards on
    // 2026-11-14 (the user: Butcher "나이트 쪽으로 빼지", Rescue "데탕트 쪽 아니냐?", Heriot "시순트 쪽 아니냐?", Momentum
    // "어썰트 쪽 아니냐?"); their effects still ask here, and the table deals them to that class only. The two common gun
    // cards (머리 사냥꾼, 철갑탄) went in the second round of that feedback (the user: "흠... 다 공용으로 쓰기 애매한데").
    public const string ExecutionHeal = "knight_butcher",
        Toughness = "body_tough", Grit = "body_grit", Rescue = "detente_rescue", Charge = "gauge_charge",
        Heriot = "hound_heriot", Momentum = "assault_momentum";
    // The common cards of the 2026-11-14 card feedback: a bigger ammo pouch, a scavenger's share of every ammo pile, more
    // throwables on the belt, and the lone player's phoenix (dealt only to a player alone: PhoenixRise).
    public const string AmmoPouch = "ammo_pouch", Scavenger = "scavenger", NadePouch = "nade_pouch", Phoenix = "phoenix";
    // The class cards with code behind them (ClassCards, ClassKit, PlayerClass).
    public const string KnightDeflect = "knight_deflect",
        AssaultDanger = "assault_danger", AssaultBroken = "assault_broken", AssaultCavalry = "assault_cavalry",
        GhostPierce = "ghost_pierce", GhostTiger = "ghost_whitetiger", PointGunslinger = "point_gunslinger",
        HoundAce = "hound_ace", SupportTorch = "support_torch", KnightShadowstep = "knight_shadowstep",
        SupportWire = "support_wire", SupportCrate = "support_crate", SupportLight = "support_light";
    // The Support's cards of the 2026-11-14 card feedback (SupportKit): a supply pack thrown to a friend, and structures
    // that go up sturdier (its PC sends the health with the build).
    public const string SupportThrow = "support_throw", SupportFortify = "support_fortify";
    // The Knight's cards of the 2026-11-14 card feedback (ClassCards, KnightCover, PlayerClass): 비껴 흘리기, a landed
    // counter keeping the parry window longer a while, and 원호 반격, a blow the cover turned strengthening the Knight's
    // counters and fists and the covered friend's guns a while (the Knight's PC sends the friend's share with the cover).
    public const string KnightFlow = "knight_flow", KnightCoverstrike = "knight_coverstrike";
    // The Assault's cards of the 2026-11-14 card feedback (ClassKit, PlayerClass): 제압 사격, its rounds pinning zombies
    // down; 선두, less taken in a CHARGE; 돌격 나팔, a longer CHARGE (table-only: Assault.chargeSeconds, which its PC sends
    // with the CHARGE); 엄호 사격, friends' kills in a CHARGE feeding its adrenaline.
    public const string AssaultSuppress = "assault_suppress", AssaultLead = "assault_lead", AssaultBugle = "assault_bugle",
        AssaultCover = "assault_cover";

    // Every card picked this campaign and its level, in play or not: the decks, each card in its own class's or everyone's.
    private static readonly Dictionary<string, int> levels = new Dictionary<string, int>();
    // The deck in play: the picked cards of the class played now and everyone's, with their levels. Made again whenever
    // a pick, a clearing, the table or the class played changes (InPlay).
    private static readonly Dictionary<string, int> inPlay = new Dictionary<string, int>();
    private static string inPlayFor;
    private static bool inPlayStale = true, hearsPicks;
    private static List<Perk> catalog;
    // What the held cards add to ClassAbilities numbers, by row then name (null: to be summed again).
    private static Dictionary<string, Dictionary<string, float>> bonuses;
    // Momentum (기세): until when this player's hits keep it going, and how long one hit buys.
    private static float momentumUntil = -99f, momentumSeconds = 1f;

    /// <summary>A perk went up a level, or all were cleared.</summary>
    public static event Action Changed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        levels.Clear();
        inPlay.Clear();
        inPlayFor = null;
        inPlayStale = true;
        hearsPicks = false;
        kept = null;
        catalog = null;
        bonuses = null;
        Changed = null;
        momentumUntil = -99f;
        starterOwed = true;
        // A table saved during play is read again (GameTables.Reload), the cards included.
        GameTables.Reloaded -= Forget;
        GameTables.Reloaded += Forget;
    }

    private static void Forget()
    {
        catalog = null;
        bonuses = null;
        inPlayStale = true;
    }

    // ---- The deck in play ----

    /// <summary>
    /// Goes up whenever the deck in play may have changed, for whoever shows or sends it (the Tab board, PlayerNetworkScore).
    /// Asking it looks at the class played first (InPlay), so a class picked a moment ago is already counted: whoever
    /// compares versions never holds the old class's deck for the current one.
    /// </summary>
    public static int DeckVersion
    {
        get
        {
            InPlay();
            return deckVersion;
        }
    }

    private static int deckVersion;

    /// <summary>
    /// Whether a card is of the deck in play: everyone's, or the class's that this PC's player plays now (a variant plays
    /// its base's cards).
    /// </summary>
    public static bool InDeck(Perk perk) => InDeck(perk, PlayerClass.CurrentId);

    /// <summary>
    /// Whether a card is of the deck a player of <paramref name="played"/> has in play: a card of no class is everyone's,
    /// a class's card is that class's and its variants' (PlayerClass.KitOf). The one place that says which deck a card is
    /// in; a pure question of the two tables (Perks.csv class, Classes.csv base).
    /// </summary>
    public static bool InDeck(Perk perk, string played)
        => perk != null && (string.IsNullOrEmpty(perk.Class) || string.Equals(perk.Class, played, StringComparison.OrdinalIgnoreCase)
            || string.Equals(perk.Class, PlayerClass.KitOf(played), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The console's /decks: the class played and its deck in play, the decks kept for other classes, and with "cases" the
    /// deck rule over every class of the table (no card of one class in another's deck, and the cards whose effects are
    /// asked by name in play for their own class alone). Also written to Logs/card-decks.txt.
    /// </summary>
    public static string Report(string args)
    {
        var text = new StringBuilder();
        string played = PlayerClass.CurrentId;
        text.AppendLine("class played: " + played + (PlayerClass.KitOf(played) != played ? " (plays " + PlayerClass.KitOf(played) + ")" : "")); // dev-text
        text.Append("in play:"); // dev-text
        int shown = 0;
        foreach (var (perk, level) in Held()) { text.Append(' ').Append(perk.Id).Append(' ').Append(level).Append('/').Append(perk.Levels).Append(';'); shown++; }
        text.AppendLine(shown == 0 ? " nothing" : ""); // dev-text
        text.Append("kept for other classes:"); // dev-text
        int keptCards = 0;
        foreach (var perk in Catalog)
        {
            int stored = Stored(perk.Id);
            if (stored <= 0 || InDeck(perk, played)) continue;
            text.Append(' ').Append(perk.Id).Append(' ').Append(stored).Append('/').Append(perk.Levels).Append(" (").Append(perk.Class).Append(");");
            keptCards++;
        }
        text.AppendLine(keptCards == 0 ? " nothing" : ""); // dev-text
        if ((args ?? "").Trim().ToLowerInvariant() == "cases") Cases(text);
        string all = text.ToString();
        try { System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "..", "Logs", "card-decks.txt"), all); }
        catch (Exception problem) { Debug.LogWarning("[Cards] Logs/card-decks.txt: " + problem.Message); }
        return all + "Logs/card-decks.txt"; // dev-text
    }

    // The deck rule over every class: what each class has in play were every card of the table picked, and what must
    // never be there.
    private static void Cases(StringBuilder text)
    {
        int wrong = 0;
        foreach (var info in PlayerClass.Catalog)
        {
            int own = 0, common = 0, others = 0;
            foreach (var perk in Catalog)
            {
                if (!InDeck(perk, info.Id)) continue;
                if (string.IsNullOrEmpty(perk.Class)) common++;
                else if (string.Equals(perk.Class, info.Family, StringComparison.OrdinalIgnoreCase)) own++;
                else others++;
            }
            wrong += others;
            text.AppendLine("  " + info.Id.PadRight(14) + " deck: " + own + " of its class + " + common + " of everyone's, of other classes " + others); // dev-text
        }
        // The cards whose effects are asked by name without asking the class (the four that leaked): each in its own class's deck alone.
        foreach (string id in new[] { ExecutionHeal, Rescue, Heriot, Momentum })
        {
            var perk = Find(id);
            if (perk == null) { text.AppendLine("  " + id + ": not in the table"); continue; } // dev-text
            var where = new StringBuilder();
            foreach (var info in PlayerClass.Catalog)
            {
                if (!InDeck(perk, info.Id)) continue;
                where.Append(' ').Append(info.Id);
                if (!string.Equals(perk.Class, info.Family, StringComparison.OrdinalIgnoreCase)) wrong++;
            }
            text.AppendLine("  " + id.PadRight(18) + " (class " + (perk.Class.Length > 0 ? perk.Class : "none") + ") in play for:" + where); // dev-text
        }
        text.AppendLine(wrong == 0 ? "CASES OK" : "CASES FAILED: " + wrong); // dev-text
    }

    // The deck in play, made again when what it is made from changed. The class played is asked every time rather than
    // heard of: a class is picked in the middle of a frame, and what runs after the pick in that frame (the new kit being
    // handed out) must already read the new class's deck, whatever order the pick's listeners run in.
    private static Dictionary<string, int> InPlay()
    {
        string now = PlayerClass.CurrentId;
        if (!inPlayStale && string.Equals(now, inPlayFor, StringComparison.Ordinal)) return inPlay;
        inPlayStale = false;
        inPlayFor = now;
        inPlay.Clear();
        foreach (var pair in levels)
            if (InDeck(Find(pair.Key))) inPlay[pair.Key] = pair.Value;
        bonuses = null;
        deckVersion++;
        // A class picked changes what is in play: those who keep something worked out from the cards hear of it as of a
        // pick. Listened for from the first deck on (PlayerClass empties its event when a session starts).
        if (!hearsPicks)
        {
            hearsPicks = true;
            PlayerClass.Picked -= OnClassPicked;
            PlayerClass.Picked += OnClassPicked;
        }
        return inPlay;
    }

    private static void OnClassPicked(string before, string now)
    {
        inPlayStale = true;
        bonuses = null;
        Changed?.Invoke();
    }

    // Something about the picks changed: the deck in play is made again at the next question.
    private static void Touched()
    {
        inPlayStale = true;
        bonuses = null;
        deckVersion++;
    }

    /// <summary>Every perk in the table, in its order.</summary>
    public static IReadOnlyList<Perk> Catalog
    {
        get
        {
            if (catalog != null) return catalog;
            catalog = new List<Perk>();
            foreach (string id in GameTables.RowIds(TableName))
            {
                if (!GameTables.TryGetRow(TableName, id, out var row)) continue;
                // The name, tag, text and why cells name Texts rows (perk.<id>.name …, a tag the class's title or
                // perk.tag.<kind>), where the words are written; a modder's plain words work as well.
                float[] steps = StepsOf(row.Text("value", "")), steps2 = StepsOf(row.Text("value2", ""));
                catalog.Add(new Perk
                {
                    Id = id, Name = Texts.Cell(row.Text("name", id)), Class = row.Text("class", ""), Tag = Texts.Cell(row.Text("tag", "")),
                    Icon = row.Text("icon", ""), Text = Texts.Cell(row.Text("text", "")), Why = Texts.Cell(row.Text("why", "")),
                    Ability = row.Text("ability", ""),
                    Levels = Mathf.Max(1, (int)row.Number("levels", 1f)),
                    Value = steps != null ? steps[0] : row.Number("value", 0f), Steps = steps,
                    Value2 = steps2 != null ? steps2[0] : row.Number("value2", 0f), Steps2 = steps2, Display = row.Number("display", 1f),
                    Weight = Mathf.Max(0f, row.Number("weight", 1f)), Solo = row.Number("solo", 0f) > .5f
                });
            }
            return catalog;
        }
    }

    public static Perk Find(string id)
    {
        foreach (var perk in Catalog) if (perk.Id == id) return perk;
        return null;
    }

    /// <summary>
    /// The card's level in the deck in play: 0 for a card not picked and for a card of a class that is not played now,
    /// however many levels of it are kept (Stored). Everything the game asks of the cards comes down to this.
    /// </summary>
    public static int Level(string id) => id != null && InPlay().TryGetValue(id, out int level) ? level : 0;

    /// <summary>The card's level as picked this campaign, whether its deck is in play or not (a class left keeps its cards).</summary>
    public static int Stored(string id) => id != null && levels.TryGetValue(id, out int level) ? level : 0;

    public static bool Has(string id) => Level(id) > 0;

    /// <summary>Everything the perk adds at its level now (Perk.At), 0 without it.</summary>
    public static float Total(string id)
    {
        var perk = Find(id);
        return perk != null ? perk.At(Mathf.Min(Level(id), perk.Levels)) : 0f;
    }

    /// <summary>The same of its second number (Perk.At2: its listed step, or value2 × level).</summary>
    public static float Total2(string id)
    {
        var perk = Find(id);
        return perk != null ? perk.At2(Mathf.Min(Level(id), perk.Levels)) : 0f;
    }

    /// <summary>
    /// The deck in play with its levels: the class's own cards first, then everyone's, each in the table's order (the
    /// order they are dealt in, so a deck reads class first wherever it is listed).
    /// </summary>
    public static IEnumerable<(Perk perk, int level)> Held()
    {
        for (int pass = 0; pass < 2; pass++)
            foreach (var perk in Catalog)
            {
                if (string.IsNullOrEmpty(perk.Class) != (pass == 1)) continue;
                int level = Level(perk.Id);
                if (level > 0) yield return (perk, level);
            }
    }

    /// <summary>
    /// The deck in play as words that travel (PlayerNetworkScore): "id:level;id:level", by card id and never by place in
    /// the table, so a PC whose table has other cards (a mod's) still reads the ones it knows.
    /// </summary>
    public static string HeldText()
    {
        var text = new StringBuilder();
        foreach (var (perk, level) in Held())
        {
            if (text.Length > 0) text.Append(';');
            text.Append(perk.Id).Append(':').Append(level);
        }
        return text.ToString();
    }

    /// <summary>
    /// Reads a deck sent as HeldText into <paramref name="into"/> (cleared first), in the order sent: the cards this PC's
    /// table knows, each level held to the card's own top. A card it does not know is left out.
    /// </summary>
    public static void Read(string text, List<(Perk perk, int level)> into)
    {
        into.Clear();
        if (string.IsNullOrEmpty(text)) return;
        foreach (string part in text.Split(';'))
        {
            int colon = part.LastIndexOf(':');
            if (colon <= 0 || colon >= part.Length - 1) continue;
            var perk = Find(part.Substring(0, colon).Trim());
            if (perk == null || !int.TryParse(part.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int level) || level <= 0) continue;
            into.Add((perk, Mathf.Min(level, perk.Levels)));
        }
    }

    /// <summary>Whether this card may be dealt now: not at its top level, and everyone's or this player's class's.</summary>
    public static bool Dealable(Perk perk)
    {
        if (perk == null || perk.Weight <= 0f || !InDeck(perk) || Level(perk.Id) >= perk.Levels) return false;
        return !perk.Solo || Alone;
    }

    /// <summary>Any card left to deal this player.</summary>
    public static bool AnyLeft()
    {
        foreach (var perk in Catalog) if (Dealable(perk)) return true;
        return false;
    }

    /// <summary>Levels held of a class's cards (FirstHand: whether a Ghost holds any of its own yet).</summary>
    public static int ClassLevels(string classId)
    {
        int sum = 0;
        foreach (var perk in Catalog)
            if (string.Equals(perk.Class, classId, StringComparison.OrdinalIgnoreCase)) sum += Mathf.Min(Level(perk.Id), perk.Levels);
        return sum;
    }

    /// <summary>
    /// Up to <paramref name="count"/> different perks dealable now, the heavier ones likelier: this player's class's own
    /// cards first, and everyone's only once the class has none left to deal (the user, 2026-11-12: "처음에는 클래스 카드만
    /// 나오다가 클래스 카드에서 뽑을 게 더 없으면 공통 카드가 나오다가 더 뽑을 카드가 없으면 그제서야 궁극기로"), so a class
    /// grows into itself before anything else. A class hand of fewer than <paramref name="count"/> is dealt as it is, not
    /// filled up with everyone's, which would let those be picked ahead of the class's own.
    /// </summary>
    public static List<Perk> Draw(int count)
    {
        var pool = new List<Perk>();
        // Dealable already keeps another class's cards out, so any card with a class here is this player's.
        foreach (var perk in Catalog) if (!string.IsNullOrEmpty(perk.Class) && Dealable(perk)) pool.Add(perk);
        if (pool.Count == 0) foreach (var perk in Catalog) if (Dealable(perk)) pool.Add(perk);
        var drawn = new List<Perk>();
        var first = FirstHand(pool);
        if (first != null) pool.Remove(first);
        while (drawn.Count < count - (first != null ? 1 : 0) && pool.Count > 0)
        {
            float sum = 0f;
            foreach (var perk in pool) sum += perk.Weight;
            float pick = UnityEngine.Random.value * sum;
            int index = 0;
            for (; index < pool.Count - 1; index++)
            {
                pick -= pool[index].Weight;
                if (pick <= 0f) break;
            }
            drawn.Add(pool[index]);
            pool.RemoveAt(index);
        }
        if (first != null) drawn.Insert(UnityEngine.Random.Range(0, drawn.Count + 1), first);
        return drawn;
    }

    // The card a class's first hand always holds, among the others drawn as ever: a Ghost's 정찰 while it holds none of its
    // class's cards (the user, 2026-11-14: "이 카드는 고스트일 때 무조건 처음 카드 선택 목록 중에 뜨게. 그 이후론 맘대로 해").
    private static Perk FirstHand(List<Perk> pool)
    {
        if (!PlayerClass.Is(PlayerClass.Ghost) || ClassLevels(PlayerClass.Ghost) > 0) return null;
        foreach (var perk in pool) if (perk.Id == GhostCards.ReconCard) return perk;
        return null;
    }

    /// <summary>
    /// Raises the perk a level (up to its top), in its own deck: a card of a class not played now is kept there and
    /// works when that class is played.
    /// </summary>
    public static void Raise(string id)
    {
        var perk = Find(id);
        if (perk == null) return;
        levels[id] = Mathf.Min(perk.Levels, Stored(id) + 1);
        Touched();
        Changed?.Invoke();
    }

    /// <summary>Forgets every perk (back at the title screen: a new campaign starts bare).</summary>
    public static void Clear()
    {
        momentumUntil = -99f;
        starterOwed = true;
        // Practising, the campaign's own cards go too, so nothing comes back when practice ends, whichever comes first
        // (the hub left for the title screen: its end and this Clear run in either order).
        kept?.Clear();
        if (levels.Count == 0) return;
        levels.Clear();
        Touched();
        Changed?.Invoke();
    }

    // ---- Practice (the hub, 2026-11-12) ----

    // The cards held when practice began (the campaign's own so far), or null when not practising.
    private static Dictionary<string, int> kept;

    /// <summary>
    /// Picks are practice now (the hub: the user, "궁극기가 좀 이상한 게. 카드를 뽑아야 하는 거 아니야?"): its range's kills
    /// fill the gauge and deal cards by the mission's own rule, and they work as cards do, but they go again when practice
    /// ends or starts over, so nothing picked in the hub reaches a chapter.
    /// </summary>
    public static bool IsPractising => kept != null;

    /// <summary>From now on picks are practice; the cards held now (the campaign's) are what practice goes back to.</summary>
    public static void BeginPractice()
    {
        kept = new Dictionary<string, int>(levels);
        momentumUntil = -99f;
    }

    /// <summary>Practice starts over (a class picked in the hub): the practice cards go, the campaign's stay.</summary>
    public static void RestartPractice()
    {
        if (kept != null) Restore();
    }

    /// <summary>Practice is over (the hub left): the practice cards go, and picks count for the campaign again.</summary>
    public static void EndPractice()
    {
        if (kept == null) return;
        Restore();
        kept = null;
    }

    private static void Restore()
    {
        momentumUntil = -99f;
        bool same = levels.Count == kept.Count;
        if (same)
            foreach (var pair in levels)
                if (!kept.TryGetValue(pair.Key, out int level) || level != pair.Value) { same = false; break; }
        if (same) return;
        levels.Clear();
        foreach (var pair in kept) levels[pair.Key] = pair.Value;
        Touched();
        Changed?.Invoke();
    }

    // ---- What the game's systems ask ----

    /// <summary>
    /// What the cards add to a class's number in Tables/ClassAbilities (row "Ghost", name "hunterMax"): every card of the
    /// deck in play naming it, value × level. PlayerClass.Ability adds it in; it is asked many times a frame, so the sums are kept
    /// (by row, then name) and made again only when a card is picked or the cards are cleared.
    /// </summary>
    public static float AbilityBonus(string row, string name)
    {
        // The deck in play first: a class picked since the last question empties the sums with the deck.
        if (levels.Count == 0 || string.IsNullOrEmpty(row) || string.IsNullOrEmpty(name) || InPlay().Count == 0) return 0f;
        if (bonuses == null) SumBonuses();
        return bonuses.TryGetValue(row, out var names) && names.TryGetValue(name, out float sum) ? sum : 0f;
    }

    private static void SumBonuses()
    {
        bonuses = new Dictionary<string, Dictionary<string, float>>(StringComparer.OrdinalIgnoreCase);
        foreach (var perk in Catalog)
        {
            int level = Mathf.Min(Level(perk.Id), perk.Levels);
            int dot = string.IsNullOrEmpty(perk.Ability) ? -1 : perk.Ability.IndexOf('.');
            if (level <= 0 || dot <= 0 || dot >= perk.Ability.Length - 1) continue;
            string row = perk.Ability.Substring(0, dot), name = perk.Ability.Substring(dot + 1);
            if (!bonuses.TryGetValue(row, out var names)) bonuses[row] = names = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
            names[name] = (names.TryGetValue(name, out float sum) ? sum : 0f) + perk.At(level);
        }
    }

    /// <summary>
    /// The bullet's damage factor for this PC's shooter: head or body. No common card adds to it any more (the gun cards
    /// went 2026-11-14). A Ghost's over-penetration card (과관통) carries the user's own trade, once the weak-point card's
    /// (dropped 2026-11-14: "헤드 헌터가 있어서 약점 데미지 증가가 따로 있는 게 맞아?"): the head, a crawling brute's glowing
    /// one among them, takes more (its value2), the body less (ClassAbilities Ghost.pierceBody a level). Momentum adds while
    /// it runs.
    /// </summary>
    public static float GunFactor(bool headshot)
    {
        float percent = !Has(GhostPierce) ? 0f
            : headshot ? Total2(GhostPierce) : -PlayerClass.Ability(PlayerClass.GhostRow, "pierceBody", 8f) * Level(GhostPierce);
        return Mathf.Max(.05f, 1f + percent / 100f) * MomentumFactor;
    }

    /// <summary>
    /// The fists' damage factor: Momentum's while it runs. The fist card (쇠주먹) went on 2026-11-14 (the user: "클래스별로
    /// 칼 나와서 주먹이 의미가 없음").
    /// </summary>
    public static float FistFactor => MomentumFactor;

    /// <summary>
    /// How far this PC's own bullets get through bodies and walls (a factor on the gun's penetration): a Ghost's
    /// over-penetration card (과관통). The shooter's PC alone judges the hits (AttackTrace.Bullet asks), and sends this with
    /// each shot so friends' PCs trace the same path and draw the same tracer (PlayerNetworkCombat.ShotRpc).
    /// </summary>
    public static float PierceFactor(DamageHit attack) => attack.Method == DamageMethod.Bullet && IsMine(attack.Source) ? OwnPierce : 1f;

    /// <summary>This PC's own player's factor on their guns' penetration (과관통), 1 without the card.</summary>
    public static float OwnPierce => Has(GhostPierce) ? 1f + Mathf.Max(0f, Total(GhostPierce)) / 100f : 1f;

    /// <summary>
    /// Heriot (사망세): the share of a kill's money a card adds on top, from the kill's own points
    /// (CombatFeedHUD.Kill, this PC's own kills only). 0 without the card.
    /// </summary>
    public static int DeathTax(int points) => points > 0 && Has(Heriot) ? Mathf.RoundToInt(points * Total(Heriot) / 100f) : 0;

    /// <summary>Health an execution gives back.</summary>
    public static float ExecutionHealth => Total(ExecutionHeal);

    /// <summary>Health added on top of the body's own.</summary>
    public static float ExtraHealth => Total(Toughness);

    /// <summary>How much longer a downed player lasts (factor on the bleed-out time).</summary>
    public static float BleedFactor => 1f + Total(Grit) / 100f;

    /// <summary>How long getting a friend up takes (factor on the time, never below a fifth).</summary>
    public static float ReviveFactor => Mathf.Max(.2f, 1f - Total(Rescue) / 100f);

    /// <summary>
    /// How fast the gauge fills (factor on each hit's charge): toward the next card and toward the ultimate alike (the user,
    /// 2026-11-14: "걍 궁극기 게이지만이 아니라 걍 채우는 걸로 가자").
    /// </summary>
    public static float ChargeFactor => 1f + Total(Charge) / 100f;

    /// <summary>
    /// The ammo pouch (탄약 주머니): how much reserve this PC's player's guns hold, as a factor on each gun's starting
    /// reserve. It is the one number every fill-up asks, through PlayerWeaponSlots.ReserveCap: a fresh gun, an ammo pile,
    /// the shop, a refill and a Support's resupply, so "full" means the same everywhere. For this PC's own player's guns
    /// only; a friend's copy keeps the gun's own.
    /// </summary>
    public static float ReserveFactor => 1f + Mathf.Max(0f, Total(AmmoPouch)) / 100f;

    /// <summary>The scavenger (청소부): the factor on what an ammo pile on the field gives this player (FieldPickups).</summary>
    public static float ScavengeFactor => 1f + Mathf.Max(0f, Total(Scavenger)) / 100f;

    /// <summary>The grenade pouch (수류탄 주머니): throwables this player carries on top of their kind's (PlayerGrenades.Carry).</summary>
    public static int ExtraThrowables => Mathf.Max(0, Mathf.RoundToInt(Total(NadePouch)));

    // ---- Momentum (기세, the user's "때릴 때마다 갱신되는 강화 효과") ----

    /// <summary>
    /// This player's hit landed on an enemy (ClassCards): Momentum, if held, runs on from now. A miss changes nothing;
    /// it only lets it run out (the user's rule: a hit is a plus, a miss is never a minus).
    /// </summary>
    public static void Struck()
    {
        if (!Has(Momentum)) return;
        momentumSeconds = Mathf.Max(.1f, PlayerClass.Ability("Cards", "momentumSeconds", 3f));
        momentumUntil = Time.time + momentumSeconds;
    }

    /// <summary>How much of Momentum is left, 0 to 1 (0: not running).</summary>
    public static float MomentumShare => Has(Momentum) && momentumUntil > Time.time ? Mathf.Clamp01((momentumUntil - Time.time) / momentumSeconds) : 0f;

    private static float MomentumFactor => MomentumShare > 0f ? 1f + Total(Momentum) / 100f : 1f;

    /// <summary>Whether <paramref name="source"/> is this PC's own player, whose perks these are.</summary>
    public static bool IsMine(GameObject source)
        => source != null && source.GetComponentInParent<PlayerMovement>() is PlayerMovement player && player.IsLocal;

    // ---- Reading the table ----

    /// <summary>
    /// A value cell listing each level's total ("30/50/70"), as numbers, or null for a single value (or anything that
    /// isn't a list of at least two numbers, which Number then reads as before).
    /// </summary>
    private static float[] StepsOf(string cell)
    {
        if (string.IsNullOrEmpty(cell) || cell.IndexOf('/') < 0) return null;
        string[] parts = cell.Split('/');
        if (parts.Length < 2) return null;
        var steps = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
            if (!float.TryParse(parts[i].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out steps[i]) || float.IsNaN(steps[i]) || float.IsInfinity(steps[i]))
                return null;
        return steps;
    }

    /// <summary>
    /// This player plays alone: no room, or nobody else in it. A solo card (Perk.Solo) is dealt only then, because what
    /// saves a lone player would be too much beside friends who can get them up (the user, 2026-11-14).
    /// </summary>
    public static bool Alone => !GameSession.IsOnline || PlayerRoster.All.Count <= 1;

    // ---- The first pick (the user, 2026-11-14: "처음부터 카드 뽑기 한 번 쓸 수 있는 상태로 시작하는 거지") ----

    // Owed from the start of a campaign until this player's gauge has been filled for it once.
    private static bool starterOwed = true;

    /// <summary>
    /// Whether this player's gauge should start full for one pick now, taken once a campaign: a new campaign (Clear at
    /// the title screen or after a finished one) owes it, nothing is held yet, and it is not the hub's practice.
    /// UltimateCharge asks for the local player.
    /// </summary>
    public static bool TakeStarterPick()
    {
        if (!starterOwed || IsPractising || HubLobby.IsActive) return false;
        starterOwed = false;
        return levels.Count == 0;
    }

    private static string Shown(float number) => number.ToString("0.##", CultureInfo.InvariantCulture);

    // {Row.name} in a card's line: the number of Tables/ClassAbilities as it stands (cards included).
    private static string Abilities(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('{') < 0) return text;
        var built = new StringBuilder(text.Length);
        int at = 0;
        while (at < text.Length)
        {
            int open = text.IndexOf('{', at);
            int close = open >= 0 ? text.IndexOf('}', open + 1) : -1;
            if (open < 0 || close < 0) { built.Append(text, at, text.Length - at); break; }
            built.Append(text, at, open - at);
            string inner = text.Substring(open + 1, close - open - 1);
            int dot = inner.IndexOf('.');
            if (dot > 0 && dot < inner.Length - 1 && inner.IndexOf(' ') < 0)
                built.Append(Shown(PlayerClass.Ability(inner.Substring(0, dot), inner.Substring(dot + 1), 0f)));
            else built.Append(text, open, close - open + 1);
            at = close + 1;
        }
        return built.ToString();
    }
}
