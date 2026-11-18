# Network — 같이 하기

방 만들기와 찾기(Rooms), 모든 PC가 같은 것을 보게 하는 동기화(Sync).

전체 지도는 [스크립트 길잡이](../README.md), 프로젝트 구조는 [구조 안내](../../../../Docs/Architecture.md)에 있다.

## 폴더

| 폴더 | 스크립트 | 무엇 |
| --- | ---: | --- |
| [Rooms](Rooms/) | 12 | 방 만들기·찾기·연결 방식 |
| [Sync](Sync/) | 12 | 자세·전투·시체·적 동기화 |

## 방이 끊겼을 때 (2026-11-18)

사용자: "게임 종료 및 결과창 이후 게스트가 함께 허브로 모이지 않고 개인 허브로 튕기는 문제". 방을 끝내는 길은 [GameSession](GameSession.cs) 한 곳이다.

- **왜 끊겼나(잰 것과 추론)**: 씬에 들어온 첫 프레임이 이 PC에서 9.2~21.5초였다(`Logs/loads.txt`의 woke, `Logs/hitches.txt`: 플레이어의 몸·총·옷을 그 한 프레임에 다 짓는다). 그동안 PC는 아무것도 보내지 못하는데, 말없는 연결을 놓는 한도가 10초였다. 빌드 로그(11-16 `Player.log`)에는 씬이 바뀐 직후 `Relay: player timed out due to inactivity`가 세 번 남아 있다. 로딩이 긴 쪽이 끊기고(추론), 끊긴 PC는 방이 읽던 씬이 깨어날 때 이미 방이 없어서 혼자 하는 게임으로 깨어났다(코드에서 읽은 것: `PlayerSpawnPoint`는 방이 없으면 플레이어를 만든다).
- **말없는 연결을 놓는 한도**: `Network.csv` `silenceLimit`(30초, 전에는 코드의 10초). 직접 연결(LAN·주소·홀펀칭)에만 듣는다. **릴레이를 거치는 연결은 유니티 서버가 10초에서 끊고 이 게임이 늘릴 수 없다**: 첫 프레임을 10초 아래로 쪼개는 일(플레이어 짓기를 여러 프레임에 나누기)이 남아 있다.
- **방장 없이 깨어나는 씬은 없다**: 방을 끝낼 때, 방이 읽던 씬이 다 깨어날 때까지 방을 이 PC의 것으로 둔 채 기다린다(그 씬이 혼자 하는 게임으로 깨어나지 않는다). 그다음에 나간다.
- **말없이 끊겼으면 같은 방에 한 번 다시 들어간다**: 방에 있다가 방장의 말 없이 끊긴 게스트는 타이틀 화면을 거쳐 같은 방(인터넷 방은 그 방, 주소로 들어왔으면 그 주소)에 한 번 다시 들어간다("연결이 끊겨 같은 방에 다시 들어가는 중입니다..."). 방은 늦게 들어온 사람을 원래 받는다(방장의 지금 씬으로 맞춰 주고 플레이어를 만든다). 다시 들어가다 또 실패하면 타이틀 화면에 그 이유가 남는다. 들어간 뒤 다시 끊기면 또 한 번 시도한다.
- **방장이 방을 닫으면 그렇다고 알린다**: 방장이 나가면 게스트에게 "방장이 방을 닫았습니다."를 보내고 끊는다. 그 말을 들은 게스트는 다시 들어가려 하지 않는다.
- **기록**: 게스트는 `[GameSession] disconnected in 'Hub': … this PC's last frame took 21.4 s`, 방장은 `[GameSession] client 1 left or was lost in 'Hub'; this PC's last frame took 9.2 s`를 남긴다(빌드는 Player.log). 프레임 시간이 한도보다 길면 그 PC가 말이 없던 쪽이다.

## 좀비와 플레이어 사이: 누가 정하나

좀비의 두뇌(감각·추적·공격 고르기·무리·바리케이드·엘리트)는 좀비마다 하나, 방장 PC에서 돈다. 다른 PC의 좀비는 그 위치와 상태를 보여 주는 사본이다. 두뇌를 쫓기는 사람 PC로 옮기지 않는 까닭: 좀비가 노리는 사람이 바뀔 때마다 주인이 바뀌어 순간이동·두 번 휘두르기가 생기고, 한 플레이어를 둘러싼 무리(자리 나누기·깔림 수·부르기)는 한곳에서 봐야 맞기 때문이다. 대신 판정은 모두 그 일을 한 사람(또는 당한 사람) PC의 몫이다(사용자 2026-11-14, "본인이 계산하고 결과만 넘기는 방식").

| 일 | 정하는 PC | 방장이 하는 일 | 코드 |
| --- | --- | --- | --- |
| 좀비가 맞음 (총·주먹·칼·투척) | 쏜 사람: 자기 사본에서 깎인 체력과 처치 여부를 정해 바로 보여 주고 그 결과(`taken`, `killed`)를 보냄 | 받은 만큼 깎고, 처치라면 죽인다. 누운 좀비 2배·처형 몫·닌자 무적 같은 것을 다시 계산하지 않는다 | `EnemyHealth.ReceiveHit`(사본) → `EnemyNetwork.SendHit` → `EnemyHealth.ReceiveSent` |
| 내 타격이 방장 쪽에서 처치가 됨 (친구의 타격이 먼저 닿아 있었음) | 방장이 알려 주면 쏜 사람 PC가 자기 처치로 보여 줌(처치음·기록·시체 물리) | 타격 소식에 남은 체력 0을 실어 보냄 | `EnemyNetwork.ShowHitRpc` → `EnemyHealth.ConfirmKill` |
| 총알의 움찔(헤드샷 기절·누적 피해 끊기)·자세 깎기 | 쏜 사람 | 밀림·둔화(좀비의 움직임)만 한다 | `EnemyHitReaction.OnHit(judged, moved)` |
| 자세 무너뜨리기 | 때린 사람: 자기 화면에서 무너졌으면 `broke`로 알림 | 자기 수치가 남아 있어도 무너뜨린다 | `MeleeCombatTarget.ApplyPostureDamage` → `ApplySentPosture` |
| 처형·암살 | 하는 사람: 무너졌는지, 등 뒤인지, 좀비가 모르는지를 자기 화면에서 판정하고 바로 시작 | 그 사람 몫으로 붙잡아 둔다. 다른 사람이 이미 잡고 있을 때만 거절(먼저 온 쪽) | `MeleeCombatTarget.TryReserveExecution` → `EnemyNetwork.ExecutionRpc` → `HoldForExecution` |
| 기절·밀치기·넘어뜨리기·돌진 밀치기 | 한 사람 | 결과를 그대로 적용 | `EnemyNetwork.InterruptRpc`·`ShoveRpc`·`KnockDownRpc`·`BargeRpc` |
| 밀치기·돌진 밀치기로 밀려나는 모습 | 한 사람: 자기 화면의 사본이 방장의 좀비와 같은 속도·시간으로 바로 밀려난다(걷기 메시 끝에서 멈춤). 돌진 밀치기는 밀림이 끝나는 자리에서 바로 쓰러지고 그 인형을 자기 PC가 굴린다 | 좀비를 실제로 민다(다른 좀비에 부딪히면 그쪽으로 전달). 그 움직임이 한 왕복 뒤 참가자 화면에 도착하는 만큼 미리 보여 준 양을 도로 빼서, 끝자리는 방장의 좀비와 같다. 돌진의 쓰러짐은 그 참가자의 인형을 따라간다 | `MeleeCombatTarget.ReceiveShove`·`BargeHere`·`FallAfterBarge`, `EnemyNetwork.SlideHere`·`Slide`, Network.csv `slideAtOnce`·`slideLagExtra` |
| 넘어뜨린 좀비가 쓰러지는 모습 (인형 물리) | 넘어뜨린 사람: 자기 화면에서 맞은 순간 쓰러지고, 그 PC가 인형을 굴려 자세를 보낸다 | 그 인형을 따라가며 좀비를 누운 자리로 옮기고, 언제 어디서 일어날지 정한다. 둘이 동시에 넘어뜨리면 먼저 닿은 쪽의 인형(`Condition.Runner`), 굴리던 사람이 나가면 넘겨받음 | `MeleeCombatTarget.KnockDownHere` → `ZombieBody.FallHere`·`KnockDown(by)`·`ShowRunner`, [ZombieBody.Down](../Enemies/Body/ZombieBody.Down.cs) |
| 좀비의 공격이 플레이어에게 맞음, 패링·회피·잡힘·깔림 | 맞는 사람: 휘두름의 시간도 그 PC에서 잰다 | 휘두름을 시작한다는 것만 알림 | `EnemyNetwork.WindupRpc` → `EnemyCombatAI.ShowWindup`, `PlayerHold` → `PlayerNetworkCombat.HoldRpc` |
| 좀비가 누구를 보고 듣고 쫓는지 | 방장(좀비 자신의 일) | 기분·의심·보는 사람을 0.1초 간격으로 알림 | `ZombieMind`, `EnemyNetwork.Condition` |
| 좀비 클립의 이동 (덤벼들기·물러나기) | 방장 | 클립이 움직인 만큼 좀비를 옮긴다(걷기 메시 안). 다른 PC는 위치를 따라오고, 걷는 속도에서만 그 이동을 뺀다 | `ZombieBody.Travel` |
| 카드·궁극기가 좀비에게 하는 일 (제압·조용한 처치·안 보이는 플레이어·눈 가리는 가스·좀비 옮기기) | 쓴 사람: 자기 PC에서 `ZombieEffects`를 부르면 바로 적용 | 좀비 하나에 대한 것은 그 좀비의 RPC로(제압은 모든 PC에, 옮기기는 방장에게), 플레이어·자리에 대한 것은 이름 붙은 메시지 `ENFILADE.ZombieEffects`로 받아 적용하고 다른 참가자에게 전한다. 조용한 처치는 타격에 실려 온다 | [ZombieEffects](../Enemies/ZombieEffects.cs), `EnemyNetwork.SuppressRpc`·`MoveRpc`, `EnemyHitMessage`의 Silent |

내가 방금 한 일은 방장의 뒤늦은 보고에 덮이지 않는다: 사본은 내 타격 뒤 `EnemyNetwork.PredictionHold`(실제 왕복 시간 + 여유, Network.csv `predict*`) 동안 체력은 더 낮은 쪽만, 자세는 내 화면 것을 믿는다.

## 무엇을 고치려면 어디를

| 변경할 내용 | 시작할 파일·설정 |
| --- | --- |
| 적·웨이브 공유 (호스트가 진행) | [EnemyNetwork](Sync/EnemyNetwork.cs), [EnemyHitMessage](Sync/EnemyHitMessage.cs), [WaveSync](Sync/WaveSync.cs) |
| 친구의 무기·사격·주먹 표시 | [PlayerNetworkCombat](Sync/PlayerNetworkCombat.cs) |
| 방 채팅 (받은 줄에 말한 사람의 플레이어가 따라와 그 사람 사진·자리 색을 쓴다) / 닉네임 | [PlayerChat](PlayerChat.cs) / [ProfileView](../UI/Menu/Profile/ProfileView.cs) |
| 실제 핑·전송량·연결 경로(직접/Relay) 측정 (`/net`) | [NetworkStatsHUD](NetworkStatsHUD.cs), [CountingTransport](Rooms/CountingTransport.cs) |
| 상대 캐릭터의 몸·소리·스킨 동기화 (체력·최대 체력·쉴드는 한 묶음 `Vitals`로 주인이 보낸다) | [PlayerNetworkPose](Sync/PlayerNetworkPose.cs), [PlayerPose](Sync/PlayerPose.cs), [MovementMirror](../Player/Movement/Core/MovementMirror.cs) |
| 보간 버퍼 자동 조절과 좀비 전송 빈도 (업데이트가 실제로 늦게 오는 만큼만 과거로 그림, 방장 쪽·참가자 쪽 따로. 좀비는 가까우면 초당 60 멀면 20. `/net`의 Buffer 줄. Network.csv `smoothing`, `buffer*`, `zombie*`) | [NetSmoothing](NetSmoothing.cs), `PlayerPose.Tick`, `EnemyNetwork.LateUpdate` |
| 방 만들기·참가·네트워크 스폰 / 서버 목록 (방마다 비스듬한 카드: 맵 사진·제목·인터넷/LAN·인원·핑 막대, 참가 Enter) | [GameSession](GameSession.cs) / [ServerBrowserView](../UI/Menu/Rooms/ServerBrowserView.cs), [BusySpinner](../UI/Menu/BusySpinner.cs), [IRoomList·RoomInfo](Rooms/RoomInfo.cs), [CombinedRoomList](Rooms/CombinedRoomList.cs), [LanRoomFinder](Rooms/LanRoomFinder.cs), [LanRoomBeacon](Rooms/LanRoomBeacon.cs), [InternetRoomFinder](Rooms/InternetRoomFinder.cs), [InternetRoom](Rooms/InternetRoom.cs) (직접 연결을 먼저 시도하고 안 되는 친구만 Relay: 홀 펀칭 [PunchSocket](Rooms/PunchSocket.cs), 공유기 UPnP [RouterPortMap](Rooms/RouterPortMap.cs), 호스트가 두 길을 함께 받는 [DualTransport](Rooms/DualTransport.cs), 직접 붙은 친구의 Relay 자리 [RelayKeeper](Rooms/RelayKeeper.cs)), [OnlineServices](Rooms/OnlineServices.cs), 화면에 보이는 핑 [PingDisplay](PingDisplay.cs) ([Network.csv](../../Data/Resources/Tables/Core/Network.csv)), 네트워크 틱 초당 120(팀원의 시선·자세·위치와 좀비 위치가 이 빈도로 감. Netcode 기본 30이었다. Network.csv `tickRate`, 다른 PC 몸을 보여 주는 여유 `bufferSeconds`) |
| 아군 사격 (721: 방장이 `/ff`로 켜고 끔, 맞은 사람 PC가 판정) | [FriendlyFire](FriendlyFire.cs), [PlayerNetworkCombat](Sync/PlayerNetworkCombat.cs) `FriendlyHit`, [Network.csv](../../Data/Resources/Tables/Core/Network.csv) `friendlyFire`·`friendlyFireShare` |
