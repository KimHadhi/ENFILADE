using System;

/// <summary>
/// The commands both the chat's console channel and the developer console (` key, DevConsole) run, registered into each
/// one's own router so the two lists never drift apart. Each adds only what is its own on top (the chat's channel
/// switches, the console's log filter); "clear" means each one's own history.
/// </summary>
public static class GameCommands
{
    public static void Register(ChatCommandRouter router, Func<SinglePlayerChatManager> findChat)
    {
        PlayerConsoleCommands.Register(router, () => PlayerRoster.Local);
        CombatTestCommands.Register(router, findChat);
        router.Register("help", "명령어 목록", _ => router.Help());
        router.Register("echo", "입력한 내용 출력", text => text);
        router.Register("map", "씬 이름 — 해당 씬으로 이동", SinglePlayerChatManager.SceneConsoleCommands.LoadMap);
        router.Register("net", "방의 실제 핑·전송량·프레임 측정 켜기, 다시 입력하면 끄고 결과 출력", _ => NetworkStatsHUD.Toggle());
    }
}
