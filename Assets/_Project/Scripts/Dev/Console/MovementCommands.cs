using UnityEngine;

/// <summary>
/// The console commands of the movement and input side (2026-11-18). Each prints or writes what a change there is judged
/// by, so nothing of it has to be found by playing: who holds the player's input, and the probes the movement fixes of
/// that day brought (each added beside its feature).
/// </summary>
public static class MovementCommands
{
    public static void Register(ChatCommandRouter router)
    {
        router.Register("input", "입력을 누가 쥐고 있는지 보기 — 화면마다 쥔 것(걷기 Move · 시야 Look · 총 Combat · 포인터 Cursor)과 지금 게임이 받는 입력 (InputHold, InputFocus)", _ => Input()); // dev-text
        router.Register("callouts", "콜아웃 표 점검 — 없는 사실·종류·단계 이름, 말할 줄이 없는 칸, 칸이 없는 줄을 찾고, 지금 내 상황에서 맞는 사실과 휠의 칸마다 나올 줄을 Logs/callouts.txt에 쓴다 (CalloutCheck, Callouts.csv·CalloutSlots.csv)", // dev-text
            _ => Application.isPlaying ? CalloutCheck.Write(PlayerCallouts.Local) : "이 명령어는 Play 중에 사용할 수 있습니다."); // dev-text
    }

    // Who holds what, and what the three questions answer for this PC's player right now. The console itself holds
    // everything while it is typed into, so its own line is always there: read the others.
    private static string Input()
    {
        if (!Application.isPlaying) return "이 명령어는 Play 중에 사용할 수 있습니다."; // dev-text
        var mine = PlayerRoster.Local;
        return InputHold.Describe()
            + "\n받는 입력: 걷기 " + Word(InputFocus.TakesMove()) + " · 시야 " + Word(InputFocus.TakesLook()) + " · 총 " + Word(InputFocus.TakesCombat()) // dev-text
            + (mine != null ? " · 내 캐릭터 키 " + Word(mine.InputAvailable) : ""); // dev-text
    }

    private static string Word(bool yes) => yes ? "받음" : "막힘"; // dev-text
}
