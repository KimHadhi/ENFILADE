using System.Text;
using UnityEngine;

/// <summary>
/// The console's /card, for trying the class cards out (Docs/Features/ClassCards.md): a card's top level, or the ultimate
/// that waits for every card, takes a long campaign of full gauges to reach by hand. Registered beside the other console
/// commands (GameCommands). It only raises this PC's own player's perks, as picking the card would.
/// </summary>
public static class CardCommands
{
    public static void Register(ChatCommandRouter router)
    {
        router.Register("card", "아이디 [단계] — 능력 카드를 바로 얻는다(시험용, 고른 것과 같다). list: 내가 받을 수 있는 카드와 단계 · "
            + "all: 내 클래스와 공용 카드 모두 최대 단계 · clear: 모두 버림", Run);
        // What the class kits keep count of, as words (each registered here beside /card: the classes' own console lines).
        router.Register("deploy", "내가 세워 둔 설치물: 종류마다 지금 수/최대, 다음에 놓으면 사라질 것의 자리, 이 PC에 서 있는 것 모두 (Deployables)",
            Deployables.Report);
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
        if (parts.Length > 1 && !TableCells.TryWhole(parts[1], out times)) return "단계는 숫자로";
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
        }
        return built.Length > 0 ? built.ToString() : "카드가 없다";
    }
}
