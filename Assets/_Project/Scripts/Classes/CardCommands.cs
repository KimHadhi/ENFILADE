using System.Globalization;
using System.Text;
using UnityEngine;

/// <summary>
/// The console's /card, for trying the class cards out (Docs/Features/ClassCards.md): a final card waits until four levels of
/// its class's cards are held, which takes a long campaign of full gauges to reach by hand. Registered beside the other
/// console commands (GameCommands). It only raises this PC's own player's perks, as picking the card would.
/// </summary>
public static class CardCommands
{
    public static void Register(ChatCommandRouter router)
    {
        router.Register("card", "아이디 [단계] — 능력 카드를 바로 얻는다(시험용, 고른 것과 같다). list: 내가 받을 수 있는 카드와 단계 · "
            + "all: 내 클래스와 공용 카드 모두 최대 단계 · clear: 모두 버림", Run);
    }

    private static string Run(string args)
    {
        string text = (args ?? "").Trim();
        if (text.Length == 0 || text == "list") return List();
        if (text == "clear")
        {
            PlayerPerks.Clear();
            return "카드를 모두 버렸다";
        }
        if (text == "all")
        {
            int raised = 0;
            foreach (var perk in PlayerPerks.Catalog)
            {
                if (!Mine(perk)) continue;
                while (PlayerPerks.Level(perk.Id) < perk.Levels) { PlayerPerks.Raise(perk.Id); raised++; }
            }
            return "내 클래스와 공용 카드를 모두 최대 단계로 (+" + raised + "단계)";
        }
        string[] parts = text.Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        var card = PlayerPerks.Find(parts[0]);
        if (card == null) return "그런 카드가 없다: " + parts[0] + " (card list)";
        int times = 1;
        if (parts.Length > 1 && !int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out times)) return "단계는 숫자로";
        for (int i = 0; i < Mathf.Max(1, times) && PlayerPerks.Level(card.Id) < card.Levels; i++) PlayerPerks.Raise(card.Id);
        string other = Mine(card) ? "" : " (다른 클래스 카드라 효과가 없을 수 있다)";
        return card.Name + " " + PlayerPerks.Level(card.Id) + "/" + card.Levels + "단계" + other;
    }

    // Everyone's cards and this player's class's.
    private static bool Mine(PlayerPerks.Perk perk) => string.IsNullOrEmpty(perk.Class) || PlayerClass.Is(perk.Class);

    private static string List()
    {
        var built = new StringBuilder();
        foreach (var perk in PlayerPerks.Catalog)
        {
            if (!Mine(perk)) continue;
            if (built.Length > 0) built.Append('\n');
            built.Append(perk.Id).Append("  ").Append(perk.Name).Append(' ').Append(PlayerPerks.Level(perk.Id)).Append('/').Append(perk.Levels);
            if (perk.Final) built.Append(" 궁극");
        }
        return built.Length > 0 ? built.ToString() : "카드가 없다";
    }
}
