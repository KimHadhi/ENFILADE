# Dev — 개발용

개발자 콘솔과 명령, 시험용 표적·더미, 기록 도구. 플레이어가 보는 기능은 여기 두지 않는다.

전체 지도는 [스크립트 길잡이](../README.md), 프로젝트 구조는 [구조 안내](../../../../Docs/Architecture.md)에 있다.

## 폴더

| 폴더 | 스크립트 | 무엇 |
| --- | ---: | --- |
| [Console](Console/) | 4 | 개발자 콘솔과 명령 |

## 무엇을 고치려면 어디를

| 변경할 내용 | 시작할 파일·설정 |
| --- | --- |
| 테스팅 룸 (11-13 사용자: "전투 시험장이랑 이동 시험장은 이제 필요 없으니 콘솔로만 접근 가능한 테스팅 룸으로. 메인 메뉴에서 안 떴으면 좋겠어." Maps.csv `list`가 0인 맵은 빌드에 남지만 메인 메뉴 맵 카드와 허브 지도 탁자에 안 나오고, 콘솔 `/testroom`이 목록을 보여 주고 `/testroom combat`·`move`·씬 이름으로 간다. 혼자면 SceneLoad로 로딩 화면을 거쳐 가고, 방에서는 방장만 쓰고 방 전체가 같이 간다(GameSession.ChangeMap). 허브의 고른 장은 그대로. 예전에 골라 둔 시험장 저장값은 메뉴·허브가 첫 장으로 읽는다) | [TestRooms](TestRooms.cs), [MapTable](../World/MapTable.cs) (`Playable`·`IsListed`·`IsTestingRoom`·`TestingRooms`), 허브의 고른 장 [HubLobby](../Modes/Hub/HubLobby.cs), 콘솔 [GameCommands](Console/GameCommands.cs), 표: [Maps.csv](../../Data/Resources/Tables/Maps/Maps.csv) `list` |
| 좀비 몸 (캡슐 대신 Synty 좀비, 우리 플레이어블 그래프로 서기·걷기·뛰기·공격·움찔·비틀, 뼈 히트박스와 머리 판정, 캡슐은 숨기고 총알이 지나감. 히트박스는 모습마다 처음 입을 때 피부를 재서 맞춤: 머리 구는 머리 정점 대부분을 감싸고 목 캡슐도 머리 판정, 몸통 상자는 그 뼈 정점의 폭·두께, 팔다리 캡슐은 정점이 뼈에서 떨어진 거리, 손·발 상자 추가. 읽을 수 없는 메시는 예전 고정 크기. 총알은 그 프레임 처음 쏠 때 물리를 화면과 맞춘 뒤 판정(`AttackTrace.SyncBodies`). `/hitbox`로 보기: 머리 빨강, 몸 노랑. 클립은 좀비마다 슬롯별로 그 칸의 클립과 목록 중 하나를 네트워크 id로 골라(모든 PC에서 같다) 걷기·뛰기·서 있기·휘두르기·움찔·알아채기가 저마다 다르고, 반복 동작의 시작 위치도 달라 떼가 발맞추지 않는다. 목록은 Nephilite 팩 클립과 `Art/Animations/Zombies/<슬롯>` 폴더의 FBX(넣으면 `ZombieClipFolders`가 알아서 목록에 넣음). 블렌더 작업실은 `ArtSource/Zombies/START_HERE.md`. 표시 없는 휘두르기의 타격 순간은 첫 휘두르기 때 손이 가장 앞으로 나간 때로 잰다. 최적화(11-11 메모 40, 측정 없이 넣어 [ZombieLod.csv](../../Data/Resources/Tables/Enemies/ZombieLod.csv) `enabled` 0으로 모두 되돌림): 40m보다 먼 좀비는 0.3초마다 생각하고 0.6초마다 길을 찾는다. 먼 좀비의 자세를 3프레임에 한 번 계산하던 것(0.8.93)은 11-13에 뺐다: 그래프를 수동 시계로 바꿨다 되돌리던 이 장치가 좀비를 굳혔다: Unity는 그래프를 Play할 때만 예약하고, 재생 중에 수동에서 게임 시계로 되돌린 그래프는 다시 예약하지 않는다(IsPlaying은 그대로 true, Unity 이슈 UUM-32824, 고치지 않기로 함). 그래서 화면에서 30m 밖에 처음 보인 좀비는 30m 안에 들어오거나 무엇이든 하는 순간(깨기·일어나기·알아채기·휘두르기·움찔) 마지막 자세로 굳었다. 조용한 좀비는 앉은·누운 채로, 나머지는 걸음 한 장면으로 미끄러져 왔다(세 번 보고. 1장 좀비는 멀리서 먼저 보이니 모든 좀비). 이제 그래프는 늘 게임 시계로 돌고, 멈추거나 시계가 바뀌면 그 자리에서 Unity가 다시 예약하는 순서(Stop → 시계 → Play)로 되돌린다(`ZombieBody.GuardGraph`). 좀비 애니메이션 기록: 에디터에서 Play하면 [Logs/zombie-anim.txt](../../../../Logs)에 45m 안 좀비마다 2초에 한 줄(기분·자세가 보이는 정도·일어나기·속도·서기/걷기/달리기 무게·맨 위 클립과 무게와 이름·그래프 시계·Animator와 컬링·카메라에 보이는지·뼈가 돈 각도)과 바뀐 일이 바로 남는다. 보이는 곳에서 움직이는데 뼈가 1초 굳으면 `FROZEN`, 자세가 0.5초 보이며 움직이면 `POSE+MOVE`, 그래프를 되돌리면 `HEAL`이 찍힌다(지난 Play 기록은 zombie-anim.previous.txt). `/zanim`(또는 `/poses`)은 30m 안 좀비 머리 위에 같은 것을 띄우고 `/zanim mark 글`은 기록에 표시를 남긴다([ZombieAnimProbe](ZombieAnimProbe.cs), [ZombieBody.Probe](../Enemies/Body/ZombieBody.Probe.cs)). 웨이브 좀비는 누군가 0.25초 안에 플레이어를 봤으면 저마다의 시야 광선을 건너뛴다. 시체 수는 ZombieCorpse.csv) | [ZombieBody](../Enemies/Body/ZombieBody.cs), [ZombieBody.Hitboxes](../Enemies/Body/ZombieBody.Hitboxes.cs), [ZombieLooks](../Enemies/ZombieLooks.cs) ([ZombieLooks.asset](../../Data/Resources/Enemies/ZombieLooks.asset): 캐릭터 목록, 클립 칸과 칸마다의 목록), [ZombieClipFolders](../../Editor/Models/ZombieClipFolders.cs), [ZombieClips.csv](../../Data/Resources/Tables/Enemies/ZombieClips.csv), [ZombieClipEvents](../Enemies/ZombieClipEvents.cs), 먼 좀비 줄이기 [ZombieLod](../Enemies/ZombieLod.cs), `AttackTrace.ShouldSkip`, [WeaponHitRegion](../Combat/HitDetection/WeaponHitRegion.cs) |
