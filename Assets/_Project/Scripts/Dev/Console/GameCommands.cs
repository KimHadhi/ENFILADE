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
        CardCommands.Register(router); // /card: the class cards at once, for trying them (Docs/Features/ClassCards.md)
        CombatTestCommands.Register(router, findChat);
        MovementCommands.Register(router); // /input and the movement probes: who holds the keys, ledges, leans, drops
        MapCommands.Register(router); // /noclip and the map probes: flying through a map to look at its colliders
        GunCommands.Register(router); // /drop: the gun in hand put down as it is, until the Tab menu's drop is there
        router.Register("help", "명령어 목록", _ => router.Help());
        router.Register("mods", "불러온 모드 목록 · reload 모드와 표 다시 읽기 · export 게임의 원래 표를 Mods 폴더에 꺼내기", GameMods.Command);
        router.Register("echo", "입력한 내용 출력", text => text);
        router.Register("map", "씬 이름 — 해당 씬으로 이동", SinglePlayerChatManager.SceneConsoleCommands.LoadMap);
        router.Register("net", "방의 실제 핑·전송량·프레임 측정 켜기, 다시 입력하면 끄고 결과 출력", _ => NetworkStatsHUD.Toggle());
        router.Register("zai", "좀비 AI 비용 측정 켜기(생각·시야 광선·길 요청·무리·공격·몸·네트워크 쓰기가 프레임마다 몇 ms인지, 좀비 수), 다시 입력하면 끄고 결과 출력·복사. 혼자서도 됨", ZombieCostHUD.Command);
        router.Register("wardrobe", "옷장 미리 보기 비용 측정 켜기(캐릭터를 세울 때마다 모델·입히기·겹침 맞춤·발바닥·얼굴·천이 몇 ms인지, 그 프레임이 몇 ms였는지), 다시 입력하면 끄고 결과 출력·복사", WardrobeCostHUD.Command);
        router.Register("perf", "프레임 비용 측정 켜기(에디터 프로파일러로 스크립트마다·엔진 부분마다 한 프레임에 몇 ms인지, 게임 몫과 에디터 몫, 가장 긴 프레임이 무엇에 걸렸는지), 다시 입력하면 끄고 Logs/perf.txt에 씀. 에디터에서만", PerfProbe.Command);
        router.Register("zfx","suppress [초] · unseen [초] · gas [초] [반경] · move — 카드가 좀비에게 하는 일(제압·안 보임·눈 가리는 가스·옮기기)을 손으로 시험", ZombieEffectsCommand.Command);
        router.Register("ff", "1 아군 사격 켜기, 0 끄기, 비우면 바꾸기 (방장만, 방 전체에 적용)", FriendlyFire.Command);
        router.Register("director", "캠페인 단계(정비·습격 임박·습격)·노래와 그 위치·적 수·습격 떼 (방장)", _ => CampaignRun.DirectorReport());
        router.Register("hub", "은신처(허브)로 가기 (혼자, 또는 방장이면 방 전체)", HubFlow.Command);
        router.Register("tower", "rooms [날짜 20261118] — 타워의 그날 방마다 나올 좀비와 엘리트 수를 Logs/tower-rooms.txt에 (타워 밖에서도 된다)", TowerRun.Command);
        router.Register("alarm", "발각·외침 집계: 외침 수, 같은 좀비가 또 외친 수, 떼였다가 외친 수, 조용함이 바뀐 횟수 (방장) → Logs/stealth.txt", StealthLog.Command);
        router.Register("targets", "좀비마다 누구를 노리는지와 이미 붙잡혀서 건너뛴 생존자 (방장) · cases 규칙만 따로 검사 → Logs/zombie-targets.txt", EnemyTargeting.Command);
        router.Register("testroom", "비우면 테스팅 룸 목록, combat · move · 씬 이름 — 메뉴에 안 나오는 전투·이동 시험장으로 가기 (방에서는 방장만, 방 전체가 같이)", TestRooms.Command);
        router.Register("results", "마지막으로 끝낸 장의 결과 연출을 다시 보기", ResultsShow.Command);
        router.Register("pso", "trace · save · (비우면 상태) — 빌드의 그래픽 상태 기록, 개발 빌드에서 (PsoWarmup)", PsoWarmup.Command);
        router.Register("sound", "<뱅크>/<키> · cue <큐> · list <뱅크> — 소리 미리 듣기 (예: /sound cue Execution/Behind)", SoundPreview.Command);
        router.Register("point", "F 없이 손가락질 고정·풀기 — 손 모양을 Poke.csv Point.* 로 고치며 보기 (F5로 몸 전체)", PlayerPoint.Command);
        router.Register("death", "[좀비 종류 / fall / bleed / friend] [wipe] [short / full] — 죽지 않고 데스 스크린만 미리 보기 (DeathScreen)", DeathScreen.Command);
        router.Register("hits", "UI 클릭 판정 모양 보기 켜기·끄기 — 하늘색 = 누를 수 있는 곳, 분홍 = 지금 커서가 고르는 것, 노랑 = 호버로 비킨 원래 자리 (KitHit)", KitHitView.Command);
        router.Register("colours", "플레이어 색 — 팔레트와 색 사이 거리, 이 PC의 플레이어마다 고른 색과 받은 색 · cases 겹침 규칙만 따로 검사 → Logs/player-colours.txt (PlayerColours)", PlayerColours.Command);
        router.Register("decks", "카드 덱 — 지금 클래스와 쓰이는 덱, 다른 클래스 몫으로 보관 중인 카드 · cases 클래스마다 덱에 남의 카드가 없는지 검사 → Logs/card-decks.txt (PlayerPerks)", PlayerPerks.Report);
    }
}
