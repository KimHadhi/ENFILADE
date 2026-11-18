using System.Text;
using UnityEngine;

// dev-text: file (console lines for whoever checks the hands' owner; nothing here is shown in the game)

/// <summary>
/// The console command of the hands' owner (2026-11-18): /hands prints whose each of this PC's player's hands is, every
/// use standing with its rank, and the last changes and refusals (the same lines Logs/hands.txt gets); with "table",
/// the rows as the game read them. "take" and "state" hold a use by hand for as long as wanted, asked for or stated as
/// a fact, so a rule can be seen in play without setting its situation up: /hands state move.climb shows everything
/// let go of as on a ledge, with nothing climbed. Registered in GameCommands, so the chat's console channel has it too.
/// </summary>
public static class HandCommands
{
    public static void Register(ChatCommandRouter router)
    {
        router.Register("hands", "손의 주인 — 두 손이 지금 누구 것인지, 서 있는 쓰임과 우선순위, 마지막으로 바뀐 것과 거절된 것 · table 표의 줄 · take <줄> 그 쓰임을 "
            + "달라고 해 보기 · state <줄> 사실로 말해 보기 · give 그만두기. 바뀜과 거절은 Logs/hands.txt에도 적힌다 (HandOwner, Tables/Player/HandUses.csv)", Run);
    }

    private static string Run(string args)
    {
        string[] words = (args ?? "").Trim().Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        if (words.Length > 0 && words[0] == "table") return Table();
        if (!Application.isPlaying) return "이 명령어는 Play 중에 사용할 수 있습니다. 표는 /hands table";
        var mine = PlayerRoster.Local;
        var owner = HandOwner.Of(mine);
        if (owner == null) return "대상 플레이어를 찾지 못했습니다.";
        if (words.Length == 0) return Describe(owner);
        if (!mine.TryGetComponent(out HandTrial trial)) trial = mine.gameObject.AddComponent<HandTrial>();
        switch (words[0])
        {
            case "give":
                trial.Hold(owner, null, false);
                return "시험용 쓰임을 그만둡니다.\n" + Describe(owner);
            case "take":
            case "state":
                if (words.Length < 2 || !HandUses.Has(words[1])) return "표에 없는 줄입니다. 줄 목록: /hands table";
                trial.Hold(owner, words[1], words[0] == "state");
                return (words[0] == "state" ? "사실로 말하는 중: " : "달라고 하는 중: ") + words[1] + " (그만두려면 /hands give). 다음 프레임부터 반영됩니다.";
            default:
                return "사용법: /hands · /hands table · /hands take <줄> · /hands state <줄> · /hands give";
        }
    }

    private static string Describe(HandOwner owner)
    {
        var text = new StringBuilder();
        text.Append("왼손: ").Append(Label(owner.OwnerOf(HandSide.Left))).Append(" · 오른손: ").Append(Label(owner.OwnerOf(HandSide.Right)))
            .Append(" · 손 잠김: ").Append(owner.Locked ? "예" : "아니오");
        var standing = owner.Standing;
        text.Append("\n서 있는 쓰임 ").Append(standing.Count).Append("개");
        foreach (var entry in standing)
        {
            string who = entry.Who is Object thing && thing != null ? thing.GetType().Name : entry.Who != null ? entry.Who.ToString() : "?";
            text.Append("\n  ").Append(Label(entry.Use)).Append(entry.Fact ? "  사실" : "  달라고 함").Append("  [").Append(who).Append("]  가진 손: ")
                .Append(Hands(entry.Owns)).Append(owner.Up(entry.Use.Id) ? "" : "  (팔은 내려가 있음)");
        }
        text.Append("\n마지막으로 바뀐 것과 거절된 것");
        if (owner.Recent.Count == 0) text.Append(": 없음");
        foreach (string line in owner.Recent) text.Append("\n  ").Append(line);
        return text.ToString();
    }

    private static string Table()
    {
        var text = new StringBuilder("손의 쓰임 (Tables/Player/HandUses.csv: rank가 클수록 세고, 같으면 먼저 쥔 쪽이 지킨다. 누가 누구의 손을 가져가는지는 Logs/hands-matrix.txt)");
        foreach (var use in HandUses.All)
            text.Append("\n").Append((use.Rank == HandUse.NoRank ? "-" : use.Rank.ToString()).PadLeft(5)).Append("  ").Append(use.Id.PadRight(18))
                .Append("꼭: ").Append(Hands(use.Needs).PadRight(6)).Append(" 있으면: ").Append(Hands(use.Also).PadRight(6))
                .Append(use.Gun == GunFate.Away ? " 총을 넣음" : use.Gun == GunFate.Stays ? " 총을 든 채 멈춤" : "").Append(use.Locks ? " 손을 잠금" : "");
        return text.ToString();
    }

    private static string Label(HandUse use) => use == null ? "없음" : use.Id + " (" + use.Rank + ")";

    private static string Hands(HandSide hands)
        => hands == HandSide.Both ? "두 손" : hands == HandSide.Left ? "왼손" : hands == HandSide.Right ? "오른손" : "없음";
}

/// <summary>
/// A use held from the console (/hands take, /hands state): said every frame, as a feature says its own, until
/// /hands give. On this PC's own player only; nothing in the game makes one.
/// </summary>
[DisallowMultipleComponent]
public sealed class HandTrial : MonoBehaviour
{
    private HandOwner owner;
    private string id;
    private bool fact;

    internal void Hold(HandOwner hands, string use, bool asFact)
    {
        if (owner != null && id != null && !fact) owner.Give(this);
        owner = hands;
        id = use;
        fact = asFact;
    }

    private void Update()
    {
        if (owner == null || id == null) return;
        if (fact) owner.State(id, this);
        else owner.Take(id, this);
    }
}
