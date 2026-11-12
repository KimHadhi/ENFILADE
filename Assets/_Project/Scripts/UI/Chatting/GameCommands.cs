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
        CardCommands.Register(router); // /card: the class cards at once, for trying them (Docs/ClassCards.md)
        CombatTestCommands.Register(router, findChat);
        router.Register("help", "명령어 목록", _ => router.Help());
        router.Register("echo", "입력한 내용 출력", text => text);
        router.Register("map", "씬 이름 — 해당 씬으로 이동", SinglePlayerChatManager.SceneConsoleCommands.LoadMap);
        router.Register("net", "방의 실제 핑·전송량·프레임 측정 켜기, 다시 입력하면 끄고 결과 출력", _ => NetworkStatsHUD.Toggle());
        router.Register("ff", "1 아군 사격 켜기, 0 끄기, 비우면 바꾸기 (방장만, 방 전체에 적용)", FriendlyFire.Command);
        router.Register("director", "캠페인 좀비 디렉터: 지금 단계·긴장도·적 수·다음 떼까지", _ => CampaignRun.DirectorReport());
        router.Register("hub", "은신처(허브)로 가기 (혼자, 또는 방장이면 방 전체)", HubFlow.Command);
        router.Register("results", "마지막으로 끝낸 장의 결과 연출을 다시 보기", ResultsShow.Command);
        router.Register("pso", "trace · save · (비우면 상태) — 빌드의 그래픽 상태 기록, 개발 빌드에서 (PsoWarmup)", PsoWarmup.Command);
        router.Register("sound", "<뱅크>/<키> · cue <큐> · list <뱅크> — 소리 미리 듣기 (예: /sound cue Execution/Behind)", SoundPreview.Command);
    }
}
