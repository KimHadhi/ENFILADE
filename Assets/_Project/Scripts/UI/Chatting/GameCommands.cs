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
        router.Register("point", "F 없이 손가락질 고정·풀기 — 손 모양을 Poke.csv Point.* 로 고치며 보기 (F5로 몸 전체)", PlayerPoint.Command);
        router.Register("death", "[좀비 종류 / fall / bleed / friend] [wipe] [short / full] — 죽지 않고 데스 스크린만 미리 보기 (DeathScreen)", DeathScreen.Command);
        router.Register("hits", "UI 클릭 판정 모양 보기 켜기·끄기 — 하늘색 = 누를 수 있는 곳, 분홍 = 지금 커서가 고르는 것, 노랑 = 호버로 비킨 원래 자리 (KitHit)", KitHitView.Command);
    }
}
