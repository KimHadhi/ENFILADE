# Network — 같이 하기

방 만들기와 찾기(Rooms), 모든 PC가 같은 것을 보게 하는 동기화(Sync).

전체 지도는 [스크립트 길잡이](../README.md), 프로젝트 구조는 [구조 안내](../../../../Docs/Architecture.md)에 있다.

## 폴더

| 폴더 | 스크립트 | 무엇 |
| --- | ---: | --- |
| [Rooms](Rooms/) | 12 | 방 만들기·찾기·연결 방식 |
| [Sync](Sync/) | 12 | 자세·전투·시체·적 동기화 |

## 무엇을 고치려면 어디를

| 변경할 내용 | 시작할 파일·설정 |
| --- | --- |
| 적·웨이브 공유 (호스트가 진행) | [EnemyNetwork](Sync/EnemyNetwork.cs), [EnemyHitMessage](Sync/EnemyHitMessage.cs), [WaveSync](Sync/WaveSync.cs) |
| 친구의 무기·사격·주먹 표시 | [PlayerNetworkCombat](Sync/PlayerNetworkCombat.cs) |
| 방 채팅 (받은 줄에 말한 사람의 플레이어가 따라와 그 사람 사진·자리 색을 쓴다) / 닉네임 | [PlayerChat](PlayerChat.cs) / [ProfileView](../UI/Menu/Profile/ProfileView.cs) |
| 실제 핑·전송량·연결 경로(직접/Relay) 측정 (`/net`) | [NetworkStatsHUD](NetworkStatsHUD.cs), [CountingTransport](Rooms/CountingTransport.cs) |
| 상대 캐릭터의 몸·소리·스킨 동기화 | [PlayerNetworkPose](Sync/PlayerNetworkPose.cs), [PlayerPose](Sync/PlayerPose.cs), [MovementMirror](../Player/Movement/Core/MovementMirror.cs) |
| 보간 버퍼 자동 조절과 좀비 전송 빈도 (업데이트가 실제로 늦게 오는 만큼만 과거로 그림, 방장 쪽·참가자 쪽 따로. 좀비는 가까우면 초당 60 멀면 20. `/net`의 Buffer 줄. Network.csv `smoothing`, `buffer*`, `zombie*`) | [NetSmoothing](NetSmoothing.cs), `PlayerPose.Tick`, `EnemyNetwork.LateUpdate` |
| 방 만들기·참가·네트워크 스폰 / 서버 목록 (방마다 비스듬한 카드: 맵 사진·제목·인터넷/LAN·인원·핑 막대, 참가 Enter) | [GameSession](GameSession.cs) / [ServerBrowserView](../UI/Menu/Rooms/ServerBrowserView.cs), [BusySpinner](../UI/Menu/BusySpinner.cs), [IRoomList·RoomInfo](Rooms/RoomInfo.cs), [CombinedRoomList](Rooms/CombinedRoomList.cs), [LanRoomFinder](Rooms/LanRoomFinder.cs), [LanRoomBeacon](Rooms/LanRoomBeacon.cs), [InternetRoomFinder](Rooms/InternetRoomFinder.cs), [InternetRoom](Rooms/InternetRoom.cs) (직접 연결을 먼저 시도하고 안 되는 친구만 Relay: 홀 펀칭 [PunchSocket](Rooms/PunchSocket.cs), 공유기 UPnP [RouterPortMap](Rooms/RouterPortMap.cs), 호스트가 두 길을 함께 받는 [DualTransport](Rooms/DualTransport.cs), 직접 붙은 친구의 Relay 자리 [RelayKeeper](Rooms/RelayKeeper.cs)), [OnlineServices](Rooms/OnlineServices.cs), 화면에 보이는 핑 [PingDisplay](PingDisplay.cs) ([Network.csv](../../Data/Resources/Tables/Core/Network.csv)), 네트워크 틱 초당 120(팀원의 시선·자세·위치와 좀비 위치가 이 빈도로 감. Netcode 기본 30이었다. Network.csv `tickRate`, 다른 PC 몸을 보여 주는 여유 `bufferSeconds`) |
| 아군 사격 (721: 방장이 `/ff`로 켜고 끔, 맞은 사람 PC가 판정) | [FriendlyFire](FriendlyFire.cs), [PlayerNetworkCombat](Sync/PlayerNetworkCombat.cs) `FriendlyHit`, [Network.csv](../../Data/Resources/Tables/Core/Network.csv) `friendlyFire`·`friendlyFireShare` |
