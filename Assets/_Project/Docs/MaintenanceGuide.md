# 설정·유지보수 안내

[프로젝트 안내](../../../README.md) · [전투 제작](CombatFoundationGuide.md) · [이동 안내](MovementGuide.md) · [플레이어 몸](PlayerBodyGuide.md) · [검증 안내](ValidationGuide.md)

## 밸런스 표 (Data Tables)

전투·이동·몸·소리의 숫자는 [Resources/Tables](../Resources/Tables)의 CSV 표에 있다. **표를 저장하면 컴파일 없이 바로 적용되고, Play 중이면 실행 중인 게임에도 즉시 반영된다.** Excel, Google Sheets(파일 → 가져오기/CSV로 다운로드), VS Code 등 CSV를 여는 프로그램이면 무엇이든 된다.

| 표 | 내용 | 행(row) |
| --- | --- | --- |
| [Player.csv](../Resources/Tables/Player.csv) | 자세별 캡슐·눈높이, 이동·속도 쌓기·점프·슬라이드·다이브, 회피·파쿠르·착지·밀치기·사다리·집라인, 최대 체력, 궁극기 보유량·소비량·충전량, 주먹·패링·가드 브레이크, 처형, 몸 애니메이션·손, 이동 펀치·큰 낙하 화면, 발소리·동작 소리·숨소리·바람·큰 낙하 소리 | `Posture` `Move` `Ability` `Vitals` `Stats` `Ultimate` `Fists` `Defense` `Parry` `Execution` `Body` `Hands` `Punch` `FallScreen` `Sounds` `Footsteps` `MovementAudio` `Breathing` `Wind` `FallAudio` `FixtureAudio` |
| [SoundCues.csv](../Resources/Tables/SoundCues.csv) | 동작마다 나는 소리 묶음(볼트·착지·발소리…)과 적에게 들리는 거리 | 큐 이름 (`Vault`, `Land`, `StepRun` …) |
| [Sounds.csv](../Resources/Tables/Sounds.csv) | 소리 묶음 폴더별 볼륨·피치 | `<묶음>/<폴더>` (`Movement/Bar/Grab` …) |
| [Reverb.csv](../Resources/Tables/Reverb.csv) | 총소리 잔향: 방을 재는 광선 길이, 실내·실외와 방 크기 기준, 동시에 나는 꼬리 수. 구역 리버브가 바뀌는 시간 | `Gun.필드`, `Area.필드` |
| [Surfaces.csv](../Resources/Tables/Surfaces.csv) | 표면 종류의 부모와 찾을 키워드 | 표면 이름 (`Concrete` …) |
| [Weapons.csv](../Resources/Tables/Weapons.csv) | 총 피해·RPM·장탄·예비탄·장전·자동 장전·탄퍼짐·앉아 쏘기·헤드샷·ADS·흔들림, 몸의 손이 잡는 위치 | `WeaponData` 에셋 이름 (`MX16A4` …) |
| [GunRig.csv](../Resources/Tables/GunRig.csv) | 총을 KINEMATION 팔 리그로 들지, 짧은 팔에 맞춰 총을 몸 쪽으로 옮기는 정도와 빠르기, 총 모양에서 총구를 재는 깊이 | `Rig` |
| [Enemies.csv](../Resources/Tables/Enemies.csv) | 적 체력·공격·자세(체간)·이동 속도 | 적 프리팹 이름 (`Enemy_Test`) |
| [Waves.csv](../Resources/Tables/Waves.csv) | 웨이브 적 수·증가량·대기·등장 간격 | `WaveManager`의 `Table Id` (기본 `Default`) |
| [Maps.csv](../Resources/Tables/Maps.csv) | 메인 메뉴에서 고를 맵과 보이는 이름. 빌드 설정에 등록된 씬만 나온다 | 씬 이름 (`CombatFoundation` …) |

- **열 이름 = 인스펙터 필드 이름**이다(인스펙터 표시 이름이 `Attack Range`면 열은 `attackRange`). 표에 열을 추가하면 그 필드도 표가 관리한다. 적 표의 `speed`처럼 `NavMeshAgent`의 공개 속성도 쓸 수 있다.
- 표에 있는 칸이 인스펙터 값보다 우선한다. **빈칸이거나 열이 없으면 인스펙터 값**을 그대로 쓴다. 표에 행이 없는 오브젝트(테스트 표적 등)는 영향을 받지 않는다.
- `#`로 시작하는 행·열은 설명용이라 무시된다. `Player.csv`는 `key,value,note` 형식이고 key는 `행.필드`다.
- 목록과 벡터는 `;`로 구분한다: 패링 단계 `0.35;0.2;0.1;0.05`, 주먹 피해 `10;28`(최소;최대 충전). 소수점은 `.`을 쓴다.
- Excel에서는 **CSV UTF-8**로 저장해야 한글 설명이 깨지지 않는다.
- 열 이름 오타나 읽을 수 없는 값은 Console에 `[Tables]` 경고로 한 번만 표시된다. Play 중 변경이 바로 안 보이면 `Tools → Data Tables → Reload Tables`를 누르거나 Preferences → Asset Pipeline → Auto Refresh가 켜져 있는지 확인한다.
- 플레이 중 최대 체력·자세를 바꾸면 살아 있는 적과 플레이어는 현재 비율을 유지한다.

새 무기는 `WeaponData` 에셋을 만들고 `Weapons.csv`에 같은 이름의 행을 추가한다. 새 적 프리팹도 같은 방식이며, 이름과 다른 행을 쓰려면 `EnemyHealth → Table Id`를 적는다.

## 그 밖의 수치를 바꾸고 저장하기

표에 없는 값은 **Play를 종료한 뒤 실제 사용 중인 씬·프리팹·데이터를 수정한다.** C# 필드에 적힌 초기값은 새 컴포넌트의 시작값이다. 이미 저장된 씬·프리팹의 수치를 덮어쓰지 않는다.

| 바꿀 내용 | 실제 편집 위치 | 적용 범위 |
| --- | --- | --- |
| 바닥으로 인정할 레이어 | 씬의 `PlayerMovement → Ground Layers`, `Ability Settings → Interaction Layers` | 해당 플레이어 |
| 무기 반동·조준 추적·화면 흔들림·사운드·아이콘 | [Data/Combat/Weapons](../Data/Combat/Weapons)의 해당 `WeaponData` | 그 데이터를 사용하는 모든 무기 |
| 시작 무기·선택 슬롯 | 플레이어의 `PlayerWeaponSlots` | 해당 플레이어 |
| 적 크기·밀치기 연쇄 | [적 프리팹](../Resources/Enemies)의 `MeleeCombatTarget` | 프리팹 인스턴스. 씬의 Override 확인 |
| 총성·히트마커·피격·처치·점수 효과 | [GunFeel.asset](../Resources/Combat/GunFeel.asset), 무기별 `Feel` | 공통 효과 / 개별 무기 |
| 주먹·벽 타격음·방어·반격·처형·피해 팝업 | [Feedback.asset](../Resources/Combat/Feedback.asset) | 공통 전투 피드백 |
| 총구 화염·궤적, 적 몸의 상처 자국 | Player의 `Gun/…/MuzzlePoint → MuzzleEffects`, `Enemy Blood Effect` | 해당 플레이어 |
| 피 스플래시, 표면별 착탄, 총알 구멍 | [ImpactEffects.csv](../Resources/Tables/ImpactEffects.csv), 구멍 유지 시간은 [GunFeel.asset](../Resources/Combat/GunFeel.asset)의 `Bullet Hole Seconds` | 공통 전투 피드백 |
| 이동·공격·UI 입력 | [Player.inputactions](../Input/Player.inputactions) | 입력 액션 사용 기능 |
| 슬롯 1~4 입력 | [PlayerWeaponSlots](../Scripts/Player/Combat/Weapons/PlayerWeaponSlots.cs)의 직접 키보드 처리 | 숫자열·숫자패드 슬롯 선택 |

1. 사용할 씬을 열고 실제 연결된 컴포넌트·데이터를 선택한다.
2. 값 하나 또는 관련된 작은 묶음을 수정한다. 씬 인스턴스의 Override와 프리팹 원본을 구분한다.
3. 씬과 프로젝트 에셋을 저장한다. 데이터 에셋은 Play 중에도 변경이 남을 수 있으므로 Play를 종료한 상태에서 편집한다.
4. 다시 Play해 효과를 확인하고 [해당 검사](ValidationGuide.md)를 실행한다.

예제 무기의 비교표는 [전투 제작 안내](CombatFoundationGuide.md)에 있다. 장탄·예비탄의 현재 값은 `WeaponRuntime`이 관리하며 공유 데이터에 저장하지 않는다.

플레이어의 `PlayerDamageReceiver`, `PlayerStats`, `UltimateCharge`, `PlayerMeleeCombat`, `PlayerExecution`은 씬에 두지 않고 `PlayerMovement`·`PlayerWeaponSlots`가 Play 시작 때 붙인다. 이 컴포넌트의 수치는 `Player.csv`에서 바꾼다. HUD 프리팹의 `PlayerStats`/`UltimateCharge`는 편집 화면 미리보기용이다.

플레이어는 여러 명일 수 있다. 플레이어가 필요한 코드는 씬을 검색하지 말고 [PlayerRoster](../Scripts/Player/PlayerRoster.cs)를 쓴다. 화면과 입력은 `Local`, 적은 `Nearest`다. `PlayerMovement.IsLocal`이 거짓인 플레이어는 이 PC가 조종하지 않는 캐릭터로, 입력·카메라·청취자·화면 효과 없이 몸과 주변에 들리는 소리만 남는다.

플레이어는 씬에 놓지 않는다. 씬의 [PlayerSpawnPoint](../Scripts/Player/PlayerSpawnPoint.cs)가 게임 시작 때 `Resources/Characters/Player.prefab`을 만들고, 더미도 같은 프리팹을 쓴다. 플레이어의 컴포넌트나 총 위치를 고칠 때는 이 프리팹을 연다. HUD는 모든 씬이 `Prefabs/UI/HUD/GameHUD.prefab` 하나를 쓴다. 프리팹은 씬의 물건을 가리킬 수 없으므로 플레이어와 HUD는 실행 중에 서로를 찾는다.

방은 [GameSession](../Scripts/Network/GameSession.cs)이 맡는다. 만든 사람의 PC가 호스트이고, 친구는 서버 찾기의 목록이나 주소로 접속한다. 방은 만들 때 고른 대로 인터넷이나 LAN 중 하나로만 열린다. LAN 방은 게임 포트 7777을 쓰고, 호스트의 [LanRoomBeacon](../Scripts/Network/LanRoomBeacon.cs)이 UDP 7778에서 묻는 쪽의 [LanRoomFinder](../Scripts/Network/LanRoomFinder.cs)에게 방 정보를 답한다. 인터넷 방은 [InternetRoom](../Scripts/Network/InternetRoom.cs)이 Unity Lobby에 올리고, [InternetRoomFinder](../Scripts/Network/InternetRoomFinder.cs)가 목록을 읽는다. 방을 만들 때 [RouterPortMap](../Scripts/Network/RouterPortMap.cs)이 호스트의 공유기에 UPnP로 UDP 포트(7777, 차 있으면 7780까지)를 열어 달라고 하고, 공유기의 바깥 주소로 보낸 패킷이 이 PC에 돌아오는지까지 확인한다. 되면 친구가 그 주소로 호스트에 바로 붙고(직접 연결, 방 정보 `link=direct`), 공유기가 UPnP를 안 하거나 바깥 주소가 사설 주소(이중 공유기, 통신사 NAT)이거나 확인 패킷이 돌아오지 않으면(공유기가 자기 바깥 주소로 보낸 패킷을 안으로 돌려보내는 NAT 루프백을 안 하거나 방화벽이 막을 때) 전처럼 Relay로 연결한다. 포트는 1시간 임대로 열고 방이 열려 있는 동안 30분마다 다시 연다. 방을 닫으면 돌려주고, 게임이 비정상으로 꺼져도 임대가 끝나면 닫힌다. 빌드한 exe로 처음 방을 열면 Windows 방화벽 창이 뜨는데, 허용하지 않으면 확인 패킷이 막혀 Relay로 열린다. 플레이어는 [OnlineServices](../Scripts/Network/OnlineServices.cs)의 익명 로그인을 쓰므로 Unity 계정이 없어도 된다. Relay는 호스트를 가리므로 Relay 방의 핑은 내 PC와 호스트가 각각 Relay 지역까지 잰 왕복 시간을 더한 추정값이다. 직접 연결 방은 들어가기 전에는 핑을 모른다. 화면의 핑은 [PingDisplay](../Scripts/Network/PingDisplay.cs)가 [Network.csv](../Resources/Tables/Network.csv)의 `pingDivisor`로 나눈 값이라 실제보다 일부러 작다. 지연을 측정할 때는 나누기 전 값을 쓴다. 콘솔의 `/net`이 방의 실제 핑, 초당 전송량, 프레임을 [NetworkStatsHUD](../Scripts/Network/NetworkStatsHUD.cs)로 보여 주고, 다시 입력하면 끄면서 전체 결과를 출력한다. 전송량은 [CountingTransport](../Scripts/Network/CountingTransport.cs)가 세는 게임 데이터로, 패킷 헤더는 포함하지 않는다. 목록 화면은 `IRoomList`만 보므로 다른 방 찾기 방식을 더해도 화면은 그대로다. 혼자 하기에서는 GameSession이 만들어지지 않으므로 네트워크가 전혀 돌지 않는다. 내 이동은 내 PC가 계산해 보내고, 남의 캐릭터는 `PlayerMovement.IsNetworkDriven`으로 표시되어 받은 위치를 따른다. 몸과 소리, 시선은 [PlayerNetworkPose](../Scripts/Network/PlayerNetworkPose.cs)가 네트워크 틱마다(초당 30번) 보내는 [PlayerPose](../Scripts/Network/PlayerPose.cs)로 맞춘다. 좌우 시선을 `NetworkTransform`에 맡기면 보간 버퍼 때문에 돌리는 동작이 늦게 오고 꼬리가 끌리므로, 플레이어 프리팹의 `NetworkTransform`은 위치만 보낸다. 받는 쪽의 [MovementMirror](../Scripts/Player/Movement/Core/MovementMirror.cs)가 이 값을 이동 상태에 그대로 써 넣으므로, 상태 코드는 돌지 않아도 `PlayerBody`, 손 IK, 발소리가 평소처럼 읽는다. 몸이 새 이동 값을 읽게 되면 `PlayerPose`와 `MovementMirror`에도 더해야 상대에게 보인다. 스킨은 같은 컴포넌트가 따로 보낸다.

적은 호스트가 돌린다. 적 프리팹은 [Resources/Enemies](../Resources/Enemies)에 두며, 이 폴더의 네트워크 프리팹은 모든 PC에 자동으로 등록된다. 다른 PC의 적은 [EnemyNetwork](../Scripts/Network/EnemyNetwork.cs)가 AI와 NavMesh를 끈 복사본이고, 호스트의 위치와 체력만 보여 준다. 쏜 사람의 PC가 자기 복사본에서 명중을 판정해 효과를 바로 보여 주고 호스트에 알리면, 호스트가 진짜 체력을 깎고 다른 PC에 알린다. 웨이브는 호스트만 진행하고, 씬의 `WaveManager`가 네트워크 오브젝트가 아니므로 표시는 [WaveSync](../Scripts/Network/WaveSync.cs)가 메시지로 보낸다. `CombatDamageReport.ByOtherPlayer`인 타격은 세상에는 보이지만 히트 마커, 점수, 킬 화면은 쏜 사람에게만 나온다.

적의 공격은 호스트가 시작을 알리고 맞을 사람의 PC가 판정한다. `EnemyCombatAI.ShowWindup`이 그 PC에서 예고 시간을 직접 세므로 예고음과 타격의 간격이 혼자 할 때와 같고, 패리와 회피도 입력 지연 없이 판정된다. 패리나 주먹이 적에게 주는 체간 피해, 경직, 밀림은 `MeleeCombatTarget`이 복사본에 바로 적용하면서 호스트에도 보낸다. 체간, 경직, 잡힘, 쓰러짐의 기준은 호스트이고 `EnemyNetwork`가 0.1초 간격으로 복사본에 알린다. 복사본은 방금 자기가 한 일을 잠깐 유지해서, 호스트가 듣기 전에 보낸 값에 되돌려지지 않게 한다. 처형은 내 PC에서 바로 시작하고 호스트가 예약을 확인하며, 다른 사람이 먼저 잡았으면 늦은 쪽이 취소된다. 호스트는 처형 중인 사람의 복사본을 `ShowExecuting`으로 표시해 주변 적을 물러나게 한다. 넘어짐은 호스트의 물리가 위치를 만들고, 복사본은 `EnemyParryRecoil.Lie`로 쓰러진 모습만 입힌다. 플레이어 체력은 각자의 PC가 진짜이고, 다른 PC에는 `PlayerNetworkPose`로 알린다.

다른 플레이어의 복사본에도 `WeaponController`와 손 IK가 그대로 있다. [PlayerNetworkCombat](../Scripts/Network/PlayerNetworkCombat.cs)이 고른 무기, 조준, 재장전, 가드를 보내고 사격, 주먹, 주먹 소리는 일어날 때마다 알린다. 복사본은 `ShowShot` 등으로 총구 화염, 궤적, 탄착, 소리를 자기 화면에 재현할 뿐 데미지는 주지 않는다. 내 무기와 주먹 소리는 2D, 남의 것은 3D로 난다.

체력이 0이 되면 [PlayerLife](../Scripts/Player/Combat/PlayerLife.cs)가 정한다. 서 있는 동료가 있으면 쓰러져서 권총만 들고 기어 다니고, 쓰러짐 체력이 시간과 피격으로 줄어든다. 동료가 옆에서 F를 누르고 있으면 일어난다([PlayerRevive](../Scripts/Player/Combat/PlayerRevive.cs)). 살리는 동안은 출혈이 멈추고, 양쪽 화면에 누가 살리는지와 남은 시간이 나온다. 살리는 사람의 진행 상황은 그 사람의 `Stance`로 전달되므로 끊기거나 나가면 바로 출혈이 다시 시작된다. 적에게 맞으면 살리는 중에도 쓰러짐 체력이 줄어든다. 다 줄면 죽고 스폰 지점에서 돌아온다. 웨이브가 진행 중이면 다음 웨이브에, 아니면 `respawnSeconds` 뒤에 돌아온다. 서 있는 사람이 없으면 호스트가 전멸을 알리고, 모두 스폰 지점으로 돌아와 1웨이브부터 다시 한다. 혼자 하면 체력 0이 곧 전멸이다. 수치는 [Downed.csv](../Resources/Tables/Downed.csv)에 있다. 즉사 공격(`DamageHit.IsFatal`, 적은 [Enemies.csv](../Resources/Tables/Enemies.csv)의 `fatalHit`)은 남은 체력과 쉴드, 쓰러짐을 건너뛰고 바로 죽인다. 회피와 패링은 그대로 통한다. [PlayerRagdoll](../Scripts/Player/Body/PlayerRagdoll.cs)은 몸이 만들어질 때 물리 인형 [RagdollCorpse](../Scripts/Combat/Feel/RagdollCorpse.cs)를 미리 만들어 꺼 둔다. 싸움 도중 만들면 끊기기 때문이다. 인형은 주요 뼈마다 물리 부품 하나씩뿐이고 메시도 뼈대 복제본도 없다. 죽으면 진짜 몸이 그대로 쓰러진다. 클립, IK, 시선은 복귀할 때까지 멈추고(`PlayerBody.GoLimp`) 인형을 몸의 자세에 맞춘 뒤, 매 프레임 인형의 회전을 진짜 뼈에 복사한다. 물리가 진짜 뼈를 직접 건드리지 않으므로 복귀할 때는 클립만 다시 틀면 된다. 줄이 끊긴 꼭두각시처럼 한 번에 무너지지 않도록 네 가지를 더한다. 팔다리는 죽기 직전 프레임의 움직임을 이어 가고(`Launch`), 근육 역할만 하는 두 번째 관절이 죽은 순간의 자세를 붙잡았다가 잠깐 사이에 힘이 빠진다(`Brace`). 그래서 무릎부터 꺾이며 쓰러지고, 힘이 빠진 뒤에도 관절 마찰이 남아 떨지 않고 멈춘다. 팔다리는 몸을 뚫지 않는다. 처음부터 겹쳐 있던 부위끼리는 떨어질 때까지 통과시켜 튀지 않게 한다. 손과 발에도 충돌 모양이 있어 바닥에 박히지 않고, 가슴뼈가 있는 리그는 허리에서도 꺾인다. 관절은 사람 몸이 도는 방향으로 다 돈다. 처음 만든 인형은 Ragdoll Wizard처럼 팔다리가 자기 길이 방향으로 도는 회전(Swing 2)을 0으로 막아서, 굽은 팔꿈치나 무릎이 옆으로 쓰러지지 못하고 허공에 뻣뻣하게 서 있었다. 지금은 팔뚝, 정강이, 고개, 허리도 돌고, 어깨와 골반은 팔다리를 움직이는 범위의 한가운데로 돌려 놓은 자세에서 만든 구관절(`Socket`)이라 원뿔 범위가 몸이 실제로 닿는 범위를 덮는다. 관절은 만들 때의 자세를 기준으로 제한을 재기 때문이다. 인형에 없는 뼈(쇄골, 목, 손, 손가락, 발)도 근육과 함께 힘이 풀려서, 총을 쥔 손 모양이나 움츠린 어깨가 그대로 남지 않는다. 죽은 자리에 남는 캡슐은 다른 PC에서 아예 꺼 둔다. `detectCollisions`만 꺼서는 걷는 캐릭터가 부딪치기 때문이다. 시체 물리는 죽은 사람의 PC가 계산하고 [PlayerNetworkCorpse](../Scripts/Network/PlayerNetworkCorpse.cs)가 자세를 보내, 모든 PC에서 같은 모습이 된다. 그 사람의 카메라는 시체의 눈에 붙는다. 시체는 `IDamageReceiver`라서 총알, 주먹, 밀치기가 모두 같은 경로로 시체를 민다. 수치는 [Ragdoll.csv](../Resources/Tables/Ragdoll.csv)에 있다.

슬로 모션은 혼자 하면 `Time.timeScale`로 세상 전체를 늦춘다. 방에서는 호스트의 시간을 늦추면 적 전체가 느려지므로, 고스트 오브 쓰시마 전설 모드처럼 그 플레이어와 상대 적만 느려진다. 플레이어 쪽은 [CombatTimeEffects](../Scripts/Player/Combat/CombatTimeEffects.cs)의 `Pace`가 `PlayerMovement.DeltaTime`과 몸 애니메이션 속도를 정하고, 적 쪽은 `EnemyNetwork.Pace`가 모든 PC에서 공격, 이동, 체간 시간을 늦춘다. 짧은 히트스톱은 네트워크 지연보다 짧아서 본인에게만 걸린다.

치마, 겉옷, 머리카락은 그 캐릭터를 플레이하는 PC가 [PlayerBodyCloth](../Scripts/Player/Body/PlayerBodyCloth.cs)에서 계산하고, [PlayerNetworkCloth](../Scripts/Network/PlayerNetworkCloth.cs)가 움직이는 뼈의 위치를 [ClothPose](../Scripts/Network/ClothPose.cs)로 보낸다. 다른 PC의 복사본은 Magica Cloth를 돌리지 않고 받은 위치를 따라가며(`ClothBones.Follow`), 회전은 위치로 다시 계산하므로 뼈 하나에 3바이트만 쓴다. 신뢰성 없는 전송이라 잃어버린 자세는 다시 보내지 않고, 천이 멈춰 있으면 1초에 한 번만 보낸다. 통신량은 `/net`으로 본다. 치마는 주인 PC의 다리에 맞춰 계산되므로 다른 PC의 다리도 같은 박자여야 뚫리지 않는다. 그래서 걷기·달리기 박자를 `PlayerPose.Step`으로 함께 맞춘다. 팔이 치마를 누르는 것([SkirtPress](../Scripts/Player/Body/SkirtPress.cs))은 보내지 않는다. 계산하는 PC는 누르기 전의 자세를 보내고(`PlayerBodyCloth.Simulated`), PC마다 자기 화면의 팔로 누른다. 누르기에 쓰는 몸 피부와 치마 겹 두께는 PC마다 같은 메시에서 재므로 똑같다. 천과 부딪히는 콜라이더는 계산하는 PC에서만 쓴다. 복사본은 모델의 Read/Write가 꺼져 메시를 읽을 수 없을 때만 다리 콜라이더를 만들어, 눌린 치마가 다리 안으로 들어가지 않게 거리를 재는 데 쓴다. 시체는 진짜 몸이 쓰러진 것이라 천도 그대로 계속 움직인다.

쓰러진 동료는 [DownedAura](../Scripts/Combat/Feel/DownedAura.cs)가 벽 너머로도 보이게 빛내고, [TeammateMarkerHUD](../Scripts/UI/TeammateMarkerHUD.cs)가 이름과 거리, 남은 시간을 표시한다. 수치는 [DownedAura.csv](../Resources/Tables/DownedAura.csv)에 있다. 킬 피드의 줄은 [KillFeedRow](../Scripts/UI/KillFeedRow.cs)이고, 내 처치는 금빛으로 구별된다. 왼쪽의 [StreakBadge](../Scripts/UI/StreakBadge.cs)는 연속 처치를 로마 숫자와 불꽃으로 보여 주고, XXV를 넘으면 파란 눈 해골로 바뀐다. 연속 처치 판정은 점수 팝업의 `multiKillWindow`에 `streakGrace`를 더한 시간 안에 다음 처치를 해야 이어진다. 방에서는 호스트가 적의 실제 죽음을 한 번만 세어 그 숫자와 줄을 모든 PC에 보내므로(`PlayerNetworkScore.AnnounceKill`) 어느 PC에서나 같게 보인다. 불꽃은 `FeedFlame` 셰이더가 그리며 수치는 [KillFeed.csv](../Resources/Tables/KillFeed.csv)에 있다. 숫자는 TMP의 Bold 대신 재질의 `FaceDilate`로 굵기를 내고 그림자를 좁게 잡는다. Bold와 넓은 그림자를 함께 쓰면 글자 상자가 폰트 아틀라스 속 글자 자리 밖까지 늘어나, 옆에 놓인 글자 조각이 숫자 주위에 점과 선으로 묻어난다. Tab 점수판은 [ScoreboardHUD](../Scripts/UI/ScoreboardHUD.cs)이고, 숫자는 [PlayerTally](../Scripts/Player/Combat/PlayerTally.cs)에 모이며 [PlayerNetworkScore](../Scripts/Network/PlayerNetworkScore.cs)가 다른 PC로 보낸다.

채팅은 [PlayerChat](../Scripts/Network/PlayerChat.cs)이 호스트를 거쳐 다른 PC로 보낸다. `/` 명령어와 콘솔 채널은 공유되지 않는다. 표시 이름은 메인 메뉴 프로필에서 정하는 `GameSettings.DisplayName`이며, 비어 있으면 PC 사용자 이름을 쓴다.

궁극기 스탯의 기본값은 **최대 보유량 120%, 1회 소비량 100%**다. 120%에서 사용하면 20%가 남고, 소비량을 70%로 바꾸면 50%가 남는다. 현재 충전량은 `UltimateCharge`, 최대 보유량과 소비량은 `PlayerStats`가 관리한다. 최대 보유량 변경은 현재 충전량을 유지하며, 최대치가 줄어들 때만 초과분을 잘라낸다. 소비량이 최대 보유량보다 크면 사용할 수 없다.

`UltimateHUD`의 전체 바 길이는 최대 보유량에 비례한다. 기본 120%에서 배치한 폭을 유지하며 `Maximum Bar Width`(기본 480 Canvas 단위)를 넘으면 폭을 고정하고 충전량·사용 기준점을 비율로 표시한다. `UltReadyThresholdIcon`은 전체 바의 **소비량 ÷ 최대 보유량** 지점을 가리킨다. 소비량만 바꾸면 전체 바 길이는 유지된다. `/ultmax 200`은 최대 보유량을 200%로, `/ultcost 70`은 1회 소비량을 70%로 지정한다. `/ultmax 20`은 20%를 더하는 것이 아니라 최대 보유량을 20%로 설정한다. 이 명령은 Play 중 테스트 상태만 바꾼다.

## 폴더 이름·저장 위치를 바꿨을 때

1. Unity와 코드 편집기를 닫고 폴더를 이동하거나 이름을 바꾼다.
2. Unity Hub에서 새 폴더를 다시 추가한다. 프로젝트는 루트의 `Assets`, `Packages`, `ProjectSettings`를 기준으로 연다.
3. 지정 버전으로 열고 임포트가 끝날 때까지 기다린다. 검사 스크립트는 자신의 위치에서 루트를 찾으므로 절대 경로를 고칠 필요가 없다.
4. 이전 이름의 `.slnx`, `.sln`, `.csproj`는 Unity가 재생성하는 파일이다. External Tools의 프로젝트 파일 재생성을 사용한다.
5. 캐시 문제가 지속되면 Unity를 종료한 상태에서 `Library`를 재생성할 수 있다. 첫 임포트가 다시 필요하다. `Assets`, `Packages`, `ProjectSettings`와 `.meta`는 보존한다.

폴더명, Player Settings의 Product Name, 기존 셰이더·편집기 세션 키는 서로 다른 값이다. 이번 정리에서 문서·무기 생성 메뉴는 ENFILADE로 맞췄다. 기존 `Hidden/Morphang/...` 셰이더 식별자와 세션 키는 연결된 코드와 자료가 사용하므로 유지한다. 출시용 제품명·플랫폼 식별자는 Player Settings에서 별도로 결정한다.

## 에셋과 파일 정리

- 에셋 이동은 Unity Project 창에서 한다. 파일 도구로 옮긴다면 원래 `.meta`를 함께 옮겨 GUID를 보존한다. 씬·프리팹 연결은 GUID에 의존한다.
- `Resources/Combat/Feedback.asset`과 `GunFeel.asset`의 로드 키는 각각 `Combat/Feedback`, `Combat/GunFeel`이다. 경로를 바꿀 때는 코드의 키도 함께 바꾼다.
- 외부 폰트·모델은 씬·재질·프리팹뿐 아니라 문자열 로드에서도 사용한다. 참조와 라이선스를 확인한 뒤 정리한다.
- `Tools` 메뉴에는 `Data Tables`(표 폴더 열기·다시 읽기), `Models`(임포트 규칙 재적용·물리 프랍 만들기), `TextMeshPro`(Dynamic 폰트 정리·저장), `HUD`(현재 씬에 HUD 넣기·갱신, HUD 프리팹 열기)만 둔다. 전투 HUD는 [GameHUD](../Prefabs/UI/HUD/GameHUD.prefab) 프리팹 하나를 모든 플레이 씬이 같이 쓴다. 새 씬에 HUD를 넣거나 씬의 HUD를 프리팹과 똑같이 맞출 때는 `Tools → HUD → 현재 씬에 HUD 넣기·갱신`을 누른다. 이때 미션 바 모양도 Hud.csv 값으로 프리팹에 반영되어, Play 전 편집 화면에서도 Play 때와 같은 모양이 보인다. 킬로그·점수 팝업·동료 체력·Tab 화면·설정 창은 어느 씬에서나 코드가 스스로 만든다. HUD는 프리팹에서 직접 편집하며, 숨겨진 새 목표 알림은 `MissionHUD` Inspector의 편집 미리보기 버튼으로 펼친다. 목표 목록은 Tab 화면의 [ObjectiveLogPanel](../Scripts/UI/ObjectiveLogPanel.cs)이 코드로 만든다.
- 공유할 소스는 `Assets`와 `.meta`, `Packages`, `ProjectSettings`, `Tools`, 문서다. `Library`, `Temp`, `.vs`, 생성 솔루션·업그레이드 로그는 재생성 가능한 로컬 파일이다.

문서 링크·메타데이터를 확인하려면 프로젝트 루트에서 `& ./Tools/Test-ProjectStructure.ps1`을 실행한다. 게임 기능 변경은 실제 씬 조작으로 확인한다.

## 플레이어 설정 (Esc 메뉴)

**Esc**를 누르면 게임이 멈추고 설정 메뉴가 열린다. Esc를 다시 누르거나 "돌아가기"를 누르면 이어서 하고, 이때 설정이 저장된다. 채팅을 닫는 Esc로는 메뉴가 열리지 않는다.

| 분류 | 항목 |
| --- | --- |
| 소리 | 전체, 전투, 이동, 목소리, UI 볼륨 |
| 조작 | 마우스 감도(×0.2~3, `PlayerLook`의 기본 감도에 곱함), 상하 반전, 앉기·기울이기를 "누르는 동안" 또는 "눌러서 전환" |
| 화면 | 시야각(16:9 화면 기준 가로 70~120°, 기본 90°. 세로 약 59°로 이전 기본 60°와 거의 같다. 더 넓은 화면은 옆이 더 보인다. 달릴 때의 시야 넓힘과 ADS 줌은 이 값에서 계산한다), 화면 흔들림: 걸음 흔들림, 착지, 충격, 볼트 펀치, 이동·다이브 기울임에 곱한다. Q/E 기울이기와 총 반동은 영향을 받지 않는다 |
| 캐릭터 | 캐릭터 카드(카즈사(밴드)·히로·실)와 고른 캐릭터의 색 카드에서 즉시 바꾼다. 오른쪽 큰 사진이 고른 모습이다. 메인 메뉴의 `캐릭터` 버튼도 같은 화면을 연다. 선택은 메뉴를 닫을 때 저장되며 기본 캐릭터는 카즈사다. 화면 분류의 `내 머리카락 보기`는 선택한 스킨에도 적용된다. 자세한 것은 [몸 안내](PlayerBodyGuide.md) |

- **저장 위치**: 사용자별 `Application.persistentDataPath/settings.json`이다. Windows에서는 보통 `%USERPROFILE%\AppData\LocalLow\<회사>\<제품>\settings.json`이다. 파일을 지우면 기본값으로 돌아간다. 밸런스 숫자는 CSV 표, 플레이어 취향은 이 파일에 둔다.
- **앉기 전환**: 한 번 누르면 앉고, 다시 누르면 선다. Shift나 점프를 누르거나, 다이브·포복·파쿠르로 앉기·슬라이드 상태를 벗어나면 자동으로 풀린다. 키를 놓은 것과 같다. 그래서 전환 모드의 점프는 서서 하는 점프이고, 몸을 접는 앉아 점프는 Ctrl을 누른 채 점프할 때만 나온다. 점프 중에 누르면 공중에서 몸을 접고, 앉은 채로 착지한다. 달리며 누르면 슬라이드하고, 끝나면 앉은 채로 남는다. 전환 모드에서 슬라이드 감속은 S 키로 한다.
- **기울이기 전환**: Q나 E를 한 번 누르면 그쪽으로 기울고, 같은 키를 다시 누르면 돌아온다. 반대 키를 누르면 반대로 기운다. Shift를 누르거나 서기·앉기 외의 상태가 되면 풀린다.
- **일시정지**: 싱글 플레이 기준이다. 멀티 플레이로 바꿀 때는 메뉴만 열고 게임은 멈추지 않게 한다(`SettingsMenu.Open`의 `GamePause.Set(true)`). [GamePause](../Scripts/Settings/GamePause.cs)만 게임 속도를 0으로 만든다. 멈출 때의 속도(반격 슬로 포함)를 그대로 되돌리므로, `CombatTimeEffects`의 슬로는 메뉴를 닫은 뒤 이어진다. UI 채널 소리만 일시정지 중에도 재생된다.
- 메뉴는 코드로 만든 화면이다([UI/Settings](../Scripts/UI/Settings)). 항목은 `SettingsMenuView`, 모양은 `SettingsWidgets`에서 바꾼다. EventSystem이 없는 씬에서는 메뉴를 처음 열 때 하나 만든다.

## 사운드를 추가하거나 교체하기

기준 폴더는 [Art/Audio/Combat](../Art/Audio/Combat)이다. 클립을 폴더에 넣은 뒤 [Feedback.asset](../Resources/Combat/Feedback.asset)의 해당 배열에도 연결한다. 폴더 안의 모든 파일이 자동으로 재생 목록에 들어가는 구조는 아니다.

| 폴더 | 재생 상황 | Feedback 설정 |
| --- | --- | --- |
| `BOOM/Swing` | 주먹을 휘두를 때 | `Punch Swing Sounds` |
| `BOOM/Hit` | 내 주먹이 적에게 피해를 줬을 때 | `Punch Hit Sounds` |
| `BOOM/HeavyHit` | 충전 주먹·반격·가드 브레이크 타격 | `Punch Heavy Hit Sounds` |
| `Player/Attack/Punch/Wall` | 주먹이 벽·지형에 닿았을 때 | `Punch Wall Sounds`, `Punch Wall Volume` |
| `BOOM/Hurt` | 플레이어가 낙하 이외의 피해를 받았을 때 | `Player Damage Sounds` |
| `BOOM/Shove` | 밀치기·슬라이딩의 적 접촉 | `Shove Enemy Sounds` |
| `Shove` | 밀치기의 월드 접촉 | `Shove World Sound` |
| `BOOM/Kill` | 주먹·밀치기·처형 처치 | `Close Kill Sounds` |
| `BOOM/Fall` | 플레이어 낙하 피해, 적이 쓰러질 때 | `Tackle Sounds`, `Knockdown Sounds` |
| `BOOM/Grab` | 가드 브레이크로 다가갈 때, 처형 시작 | `Grab Sounds` |
| `BOOM/Block` | 가드 성공 | `Block Sounds` |
| `BOOM/Parry`, `BOOM/ParryBreak` | 주먹 패링, 체간을 무너뜨린 패링 | `Parry Sounds`, `Parry Break Sounds` |
| `Hitfullness` | 총기 명중·처치 등에 겹치는 타격 레이어 | `Impact Bullet Sounds` 등 각 레이어 |
| `Defense/Parry` | 금속 패링음. 카타나용 보관 클립 | 현재 연결하지 않음 |
| `Defense/ReservedStrongParries` | 향후 강공격용 보관 클립 | 현재 연결하지 않음 |
| `Player/Attack/Punch/Hit`·`Swing`, `Player/Damage/Hit`, `Defense/Block` | BOOM으로 바꾸기 전의 클립 | 현재 연결하지 않음 |

`BOOM` 폴더와 돌진 밀치기의 `Movement/PushHard`는 유료 BOOM Library Violent Combat의 소리를 타격 지점부터 잘라 낸 것이다. 한 번에 두 번 맞는 듯한 클립은 뺐고, 폴더 안의 음량은 약 3dB 안으로 맞췄다. 회피의 `Movement/Dodge`(`Wind` 바람은 WHOOSH Dash, `Cloth` 옷은 Kimono, `Perfect` 완벽 회피는 Phantom Wind)는 같은 팩의 WHOOSH 파일에 든 변형 6개씩을 하나씩 잘라 낸 것이고, 바람과 옷은 앞쪽에 천천히 부풀어 오르는 부분(0.1~0.2초)을 잘라 0.14초짜리 회피 안에 가장 커지게 했다(가장 큰 곳의 −12dB 지점 20ms 앞에서 자른다), SoundCues.csv의 `Dodge`·`DodgePerfect`가 낸다. 1인 라이선스이고 음원 공유 형태의 재배포가 금지되므로, 저장소는 비공개로 유지한다. `Resources/Combat/Parry`의 `ParryFlare`는 [Kenney Particle Pack](https://kenney.nl/assets/particle-pack)이고 CC0다.

패링 이펙트는 유료 Vefects Combat Flipbook VFX URP로 만든다. `Assets/Vefects` 원본은 고치지 않고, `Prefabs/Effects/Parry`의 프리팹과 `Art/Materials/Parry`의 머티리얼을 복사본으로 둔다. 색은 이 머티리얼의 `R`(밝은 몸통), `G`(어두운 속살), `B`, `Outline`에서 고친다. 복사본은 팩의 텍스처와 셰이더를 쓰므로 `Assets/Vefects`의 `VFX/Hit/Textures`와 `Shared`는 지우면 안 된다. 원본 프리팹에는 반복 재생되는 `AudioSource`가 붙어 있어서 복사본에서 뺐고, `ParryVfx`도 슬롯에 넣은 프리팹의 소리를 끈다. 주황 별인 `VFX_Parry_Sparkle`은 Cartoon FX Remaster의 `CFXR3 Hit Light B (Air)`에서 고리 두 층을 뺀 복사본이다. 머티리얼과 스크립트는 팩의 것을 쓰므로 `Assets/JMO Assets`도 지우면 안 된다.

기존 클립을 이동하거나 이름을 바꿀 때는 `.meta`도 함께 이동해 GUID를 유지한다. 새 클립은 Unity 임포트가 끝난 뒤 배열에 추가하며, `Missing` 항목이나 남은 옛 경로의 `.meta`가 없는지 확인한다. 각 배열은 직전에 재생한 클립을 피해서 고르므로 원하는 변형들을 같은 배열에 넣으면 된다.

### 이동·목소리 소리 (폴더가 곧 목록)

이동과 목소리 소리는 배열에 연결하지 않는다. **폴더에 넣으면 자동으로 들어간다.** 폴더 구조가 소리 이름이고, 파일을 추가·이동·삭제하면 편집기가 `Resources/Audio/<묶음>` 소리 묶음(SoundBank)을 다시 만든다([SoundBankBuilder](../Editor/Import/SoundBankBuilder.cs)).

| 폴더 | 묶음 | 내용 |
| --- | --- | --- |
| [Art/Audio/Movement](../Art/Audio/Movement) | `Movement` | `<행동>/<표면>/`: `Run`·`Walk`·`Sneak`·`Release`(발 떼기)·`Land`·`Slide`·`SlideLoop`·`WallStep`·`HandSoft`·`HandHard`·`HandRelease`·`Ringout` 아래에 `Concrete`·`Metal`·`Duct`·`Wood`·`Glass`… 표면이 없는 소리는 `<행동>/` 또는 `<행동>/<종류>/`: `Cloth/VaultSwish`, `Roll/Start`, `Ladder/Step`, `Bar/Grab`, `Fall/Heartbeat`, `LandHard`, `Push`, `Prone/Down`, `Lean`… |
| [Art/Audio/Voice/Hiro](../Art/Audio/Voice/Hiro) | `Voice` | 숨소리 `Breath/SoftShortIn`…, 힘쓰는 소리 `StrainSoft/Medium/Hard`, 충격음 `ImpactSoft/Impact/ImpactHard`. 캐릭터 목소리를 바꾸면 이 폴더 내용만 갈아 끼운다 |
| [Art/Audio/HitConfirm](../Art/Audio/HitConfirm) | `HitConfirm` | 히트마커와 함께 나는 확인음. `Body`(명중마다), `Head`(헤드샷 때 겹침), `Kill`(처치), `Break`(체간 붕괴). 폴더 안 파일 중 하나를 무작위로 고른다 |
| [Art/Audio/Guns](../Art/Audio/Guns) | `Guns` | 총소리. `<총>/<소리>`이고 총은 무기 데이터의 Sound Profile(`Pistol`·`Rifle`·`Heavy`), 소리는 `Shot`·`MagOut`·`MagIn`·`SlideBack`·`SlideForward`·`Bolt`·`DryFire`·`LowAmmo`·`LastRound`다. 총 이름 없이 둔 `DryFire`는 그 소리가 없는 총이 같이 쓴다. 잔탄 경고는 세 겹이다. 탄창이 3분의 1 아래로 남은 발마다 그 총의 `LowAmmo`가 빌수록 커지며 나고, 탄창을 비우는 발에는 그 대신 `LastRound`가 난다(TFA Base 규칙, WeaponFeedback.AmmoCue). 모든 총이 같이 쓰는 딸깍 `LowAmmoClick`은 SoundCues.csv의 `GunLowAmmoClick/<총>` 행이 CS2처럼 총소리 0.05초 뒤에 클립의 2.5배로 낸다(표의 볼륨이 1을 넘으면 FeelAudio가 클립보다 크게 낸다. 최대 4). 장전 끝의 노리쇠는 SoundCues.csv의 `GunBolt/<총>` 행이 당기고 놓는 소리를 시간차로 낸다. KINEMATION 총(GunRig)을 들었을 때는 장전 소리를 그 장전 동작에서 잰 순간에 낸다. 탄창이 빠지기 시작할 때 `MagOut`, 끼워질 때 `MagIn`, 노리쇠나 슬라이드가 걸쇠에서 풀려 앞으로 갈 때 `SlideForward`다(당겼다 놓는 동작이면 `SlideBack`도 낸다). 부딪는 소리는 클립에서 가장 큰 순간이 동작의 순간에 오도록 일찍 시작한다([ReloadMoments](../Scripts/Audio/ReloadMoments.cs)). 무기 데이터나 GunFeel의 소리 칸에 클립을 넣으면 그쪽이 먼저고, 둘 다 비면 합성음이다([FeelAudio](../Scripts/Combat/Feel/FeelAudio.cs)). `Reverb/<총>/<방>`은 쏜 자리의 방에 맞춰 겹치는 잔향 꼬리다([GunReverb](../Scripts/Audio/GunReverb.cs), 수치는 Reverb.csv) |
| [Art/Audio/Library](../Art/Audio/Library) | 없음 | 아직 쓰지 않는 보관 소리(Beatrun 나머지, FEAR 손전등·문, 투척 칼, 잠입). 어디에서도 참조하지 않으므로 빌드 크기에 들어가지 않는다. 쓰려면 위 폴더로 옮긴다 |

- **볼륨·피치**: [Sounds.csv](../Resources/Tables/Sounds.csv)의 `<묶음>/<폴더>` 행(예: `Movement/Walk/Concrete`). Beatrun 원본 값으로 채워져 있다. 행이 없으면 볼륨 1, 피치 1이다.
- **가져오기 설정**: 클립마다 Preload Audio Data와 Load In Background를 켠다. 맵을 불러올 때 [CombatWarmup](../Scripts/Combat/Feel/CombatWarmup.cs)이 묶음을 불러오면 그 데이터를 뒤에서 미리 읽어 둔다. 꺼 두면 처음 재생할 때 메인 스레드에서 읽느라 프레임이 7~89ms 멈췄다. 2026-11-06에 꺼져 있던 1,634개를 켰고, 메모리는 약 79MB 더 쓴다. 새 클립도 둘 다 켠다.
- **도플러**: 쓰지 않는다. [GameAudio.Bind](../Scripts/Audio/GameAudio.cs)가 모든 소리에서 끈다. 유니티는 소리의 속도를 게임 시간으로 재므로, 슬로 모션 중에 실제 시간으로 움직이는 것(완벽 회피 중 적을 지나가는 회피)은 엄청나게 빠르다고 보고 둘레 3D 소리의 높이를 튀게 했다.
- **표면**: 표면은 발소리, 충돌 이펙트, 총알이 뚫고 나가는 정도(`penetration`)를 정한다. 물체에 [SurfaceType](../Scripts/World/SurfaceType.cs) 컴포넌트를 붙이고 Inspector의 목록에서 고르면 그 값을 쓴다. 목록은 Surfaces.csv의 줄과 설명(`#note`)이라, 표에 줄을 더하면 바로 목록에 생긴다([SurfaceNameDrawer](../Editor/Data/SurfaceNameDrawer.cs)). 없으면 [Surfaces.csv](../Resources/Tables/Surfaces.csv)의 키워드를 물리 재질·렌더 재질·오브젝트 이름에서 찾는다(가장 긴 키워드가 이김). 아무것도 맞지 않으면 `Concrete`다.
- **표면이 없는 소리**: 표마다 `parent`가 있어서 소리가 없는 표면은 부모로 넘어간다. `Duct` → `Metal` → `Concrete` 순서다. 예를 들어 `Grass`는 착지 소리만 따로 있고, 발소리는 `Dirt` → `Gravel` 소리를 쓴다.
- 출처: Beatrun(미러스 엣지 원본), Dynamic Landing(`LandHard`·`LandHeavy`·`Roll`·`Push`), FEAR-like SFX(`Land/Dirt·Grass·Stone·Tile·Carpet`, `Slide/Dirt·Sand`, `Prone`, `Ledge/Reach`, `Lean`, `Cloth/SprintRustle`). 공개 빌드 전에는 교체가 필요할 수 있다.
- **언제 무슨 소리가 나는지**: 코드는 큐 이름만 부르고, 그 큐가 무엇을 재생할지는 [SoundCues.csv](../Resources/Tables/SoundCues.csv)가 정한다. `sounds` 칸은 `;`로 나눈 `묶음/키` 목록이다. `{surface}`로 끝나면 발밑·장애물·렛지·벽의 표면 소리, `*0.5`는 볼륨, `+0.1`은 0.1초 뒤다(예: `Movement/HandHard/{surface}; Voice/StrainSoft +0.1`). `Vault/Gantry`처럼 표면 이름을 붙인 행이 있으면 그 표면에서는 그 행을 쓴다. `noise`는 적에게 들리는 거리(m)이고 [Noise](../Scripts/World/Noise.cs) 이벤트로 나간다(아직 듣는 적은 없다).
- **발소리**는 타이머가 아니라 몸의 발이 땅에 닿는 순간에 난다([PlayerFootsteps](../Scripts/Player/Audio/PlayerFootsteps.cs), GMod Anim-Based Footsteps 방식). 발마다 직전 접지 높이를 추적하며 새로 들어 올린 발만 다음 걸음으로 센다. 양발이 함께 올라가는 골반 움직임은 제외하고, 낮은 프레임 속도로 발의 최저점을 건너뛰어도 하강 후 반등을 인식한다. 가만히 서 있으면 양발의 기준을 각각 다시 잰다. 시험장은 [MovementAbilityPlayground](../Scenes/Development/MovementAbilityPlayground.unity) 시작 위치 뒤의 `Surface pads`(표면별 판 16개)이고, 디버그 HUD의 `Surface:`가 마지막 걸음의 표면이다.
- **이동 소리의 비중**: 일반 이동 속도 5.2m/s는 몸의 달리기 클립과 `StepRun`을 쓴다. 반복되는 옷 소리는 이 큐에서만 `*0.22`, 발 떼기 마찰음은 `StepRelease`에서 `*0.3`으로 줄여 발 디딤이 먼저 들리게 한다. 볼트·구르기·착지의 옷 소리는 각 동작 큐에서 따로 조절한다. 몸 소리가 설정된 동시 재생 수를 넘으면 가장 먼저 끝날 소리를 멈추고 새 소리로 교체한다.
- **넘기 뒤 발소리 복귀**: 볼트의 남은 애니메이션을 재생하는 동안만 발 접지를 잠시 읽지 않는다. 마무리가 끝나거나 다른 자세로 중단되면 `playingOut`을 해제해 보행 접지와 발소리가 다시 이어진다.

**볼륨 채널**: 모든 소리는 `전투`·`이동`·`목소리`·`UI` 채널 중 하나에 속하고, Esc 메뉴의 볼륨을 따른다. 새 `AudioSource`를 만들면 [GameAudio](../Scripts/Audio/GameAudio.cs)`.Bind(source, AudioChannel.…)`로 한 번 연결한다. 소리마다 크기를 바꿀 때는 `source.volume`을 쓰지 않고 `PlayOneShot(clip, 볼륨)`의 볼륨 값을 쓴다. 커졌다 작아지는 반복 소리(슬라이드 긁는 소리)는 `GameAudio.SetGain`으로 크기를 정한다. 월드 한 지점에서 한 번 나는 소리는 `AudioSource.PlayClipAtPoint` 대신 `GameAudio.PlayAt`을 쓴다. 전체 볼륨은 `AudioListener.volume`이다.

### 구역 리버브 (맵마다 놓기)

방·복도·터널처럼 소리가 울리는 곳에는 [ReverbArea](../Scripts/Audio/ReverbArea.cs)를 놓는다. 내 귀(카메라)가 상자 안에 있는 동안 들리는 소리(발소리·숨·적·재장전·도탄)에 Unity의 리버브 프리셋이 걸린다. 총소리와 그 잔향 꼬리, 히트마커·처치 확인음, UI에는 걸리지 않는다. 총은 [GunReverb](../Scripts/Audio/GunReverb.cs)가 쏜 자리의 방을 재서 따로 울린다. 상자 밖에는 리버브가 없다.

1. Hierarchy에서 `GameObject → Audio → Reverb Area`를 고른다. 씬 화면 가운데에 파란 상자가 생긴다.
2. 오브젝트를 옮기고 돌려 공간에 맞춘 뒤, 씬 화면에서 상자 면의 점을 끌어 크기를 맞춘다. Inspector의 `Center`·`Size`로도 된다.
3. `Preset`을 고른다. 작은 방 `Room`, 타일 방 `Bathroom`, 돌로 된 방 `Stoneroom`, 복도 `Hallway`, 돌 복도 `StoneCorridor`, 하수관 `SewerPipe`, 동굴 `Cave`, 큰 홀 `Hangar`·`Arena`, 주차장 `ParkingLot`, 골목 `Alley`, 도시 거리 `City`, 들판 `Plain`. `Off`는 큰 구역 안의 한 곳(작은 부스 등)을 울리지 않게 한다.
4. 상자가 겹치면 `Priority`가 높은 쪽, 같으면 작은 상자가 이긴다. 그래서 맵 전체를 덮는 큰 상자(`City` 등)를 깔고 그 안에 방 상자를 놓으면 방 안에서는 방이 이긴다.
5. `Strength`를 낮추면 프리셋이 덜 들린다. 구역을 드나들 때 바뀌는 시간은 [Reverb.csv](../Resources/Tables/Reverb.csv)의 `Area.fadeTime`이다.
6. 씬을 저장한다.

## 3D 모델 추가하기

모델 하나당 폴더 하나를 [Art/Models](../Art/Models) 아래 분류 폴더에 만든다. 이름에는 공백 대신 PascalCase를 쓴다.

```text
Art/Models/
  Characters/  Weapons/  Props/  Environment/
    Props/TetoPear/
      TetoPear.fbx
      Textures/tetopear.png      ← 머티리얼 이름(tetopear)과 같은 이름
```

폴더에 넣기만 하면 [ModelImportRules](../Editor/Import/ModelImportRules.cs)가 처음 임포트할 때 다음을 맞춘다.

| 항목 | 처리 |
| --- | --- |
| 루트 | 씬에 놓은 오브젝트의 루트는 회전 0·크기 1. Blender의 -90°·×100은 안쪽 자식에 남는다 |
| 축·불필요 요소 | 축 변환을 메시에 굽고, 카메라·조명은 가져오지 않는다 |
| `Props`·`Environment` | 리그·애니메이션을 끈다. `Characters`·`Weapons`는 기본값(Generic)을 유지한다 |
| 머티리얼 | URP Lit으로 만들고, 모델 옆이나 `Textures/`에서 이름이 맞는 텍스처를 자동 연결한다 |
| 텍스처 | 최대 2048. `_Normal`·`_N`은 노멀맵, `_Mask`·`_Metallic`·`_Roughness`·`_AO`·`_ORM`·`_Height`는 sRGB 끔 |

텍스처 이름 규칙: 머티리얼 이름 또는 모델 이름 뒤에 아무것도 없거나 `_BaseColor`·`_Albedo`·`_Diffuse`·`_Color`이면 기본 색, `_Normal`이면 노멀맵이다. 대소문자·공백·밑줄은 구분하지 않는다. 폴더에 텍스처가 하나뿐이면 이름과 상관없이 기본 색으로 쓴다. 텍스처를 나중에 넣어도 모델이 다시 임포트되어 연결된다.

- 규칙은 **처음 임포트할 때만** 적용되므로 이후 Inspector에서 바꾼 설정은 유지된다. 규칙을 다시 적용하려면 모델이나 폴더를 선택하고 `Tools → Models → Reapply Import Rules`를 누른다.
- 머티리얼은 모델 안에 포함되어 자동으로 갱신된다. 매끄러움 등을 직접 조정하려면 모델 Inspector → Materials → **Extract Materials**로 꺼낸다. 꺼낸 뒤에는 텍스처가 자동으로 바뀌지 않는다.
- 크기는 `Model → Scale Factor`로 조정한다. 플레이어 키는 약 1.75m다.
- 게임에 쓰는 오브젝트(충돌체·스크립트 포함)는 모델을 씬에 놓고 [Prefabs](../Prefabs) 아래 프리팹으로 만든다. 굴러다니는 물체는 아래 물리 프랍 도구를 쓴다.
- 원본 `.blend`는 `Assets` 밖에 둔다. `Assets` 안에 두면 Unity가 Blender를 실행해 변환하려고 한다. `.blend1`은 Blender 백업이므로 넣지 않는다. `.fbx`·`.png`·`.blend`는 Git LFS로 저장된다.
- Blender에서 내보낼 때 Apply Scalings `FBX All`, Forward `-Z`, Up `Y`, Apply Transform을 켜면 안쪽 자식의 회전·크기도 깔끔해진다.

### 충돌과 물리 (물리 프랍)

모델을 Project 창에서 선택하고 우클릭 → `ENFILADE → Create Physics Prop Prefab`(또는 `Tools → Models → Create Physics Prop Prefab`)을 누르면 `Prefabs/<분류>/<이름>.prefab`이 만들어진다. 씬에는 이 프리팹을 놓는다.

| 구성 | 내용 |
| --- | --- |
| 루트 | `Rigidbody`(질량 1kg, 보간, 빠른 충돌 감지) + [PhysicsProp](../Scripts/World/PhysicsProp.cs) |
| 자식 | 모델 그대로. 메시마다 **convex `MeshCollider`** |
| 반응 | **질량이 결과를 정한다.** 모든 밀기는 운동량이라 같은 힘이면 가벼울수록 멀리 간다. 벽 타기·매달리기·넘기 대상에서는 빠진다 |

| 밀기 | 세기 (설정 위치) | 최고 속도 | 1kg 물체 | 20kg 물체 |
| --- | --- | --- | --- | --- |
| 걷기 | 300N으로 기댐 (`Player.csv`의 `Ability.propPushForce`) | 걷는 속도 | 바로 걷는 속도 | 서서히 밀림 |
| V 밀치기 | 60 N·s (`Ability.propShoveImpulse`) | 8m/s (`Ability.shovePushSpeed`) | 8m/s | 3m/s |
| 총알 1발 | 소총 3·권총 4 N·s ([Weapons.csv](../Resources/Tables/Weapons.csv) `propImpulse`) | 12m/s | 3·4m/s | 0.15·0.2m/s |
| 슬라이딩·다이브·돌진 밀치기로 부딪힘 | 몸무게 70kg과 함께 밀려 나감 (`Ability.propBodyMass`) | 플레이어 속도 | 플레이어 속도로 밀려 나가고 플레이어는 거의 그대로 | 둘 다 플레이어 속도의 78%로 진행 |

슬라이딩·다이브·돌진 밀치기로 부딪히면 물체와 플레이어가 한 속도(몸무게 ÷ 둘의 질량 합 × 플레이어 속도)로 함께 나아간다. 물체가 튕겨 나가며 더 빨라지지는 않는다. 그 속도가 원래의 30% 아래가 되는 물체(플레이어 몸무게의 약 2.3배, 160kg 이상)는 지금처럼 벽으로 취급해 튕기거나 멈춘다.

움직이는 물체에는 오목한 메시 충돌체를 쓸 수 없어서 볼록 껍질(convex, 최대 255면)로 감싼다. 배·공·상자처럼 거의 볼록한 모양은 이것으로 충분하다. 도넛·의자처럼 크게 오목한 모양은 자식 오브젝트에 Box·Sphere·Capsule 충돌체를 여러 개 나눠 붙이고 `MeshCollider`를 지운다. 움직이지 않는 벽·지형은 `Rigidbody` 없이 convex를 끈 `MeshCollider`를 쓰면 모양 그대로 충돌한다.

물체마다 바꿀 값은 루트 `Rigidbody → Mass` 하나다. 밀기 세기는 모든 물체에 공통이며 위 표의 위치에서 바꾼다. 프리팹이 이미 있으면 도구는 덮어쓰지 않는다.

## 화면 후처리

플레이어 카메라는 URP 후처리를 켠다([PlayerCameraEffects](../Scripts/Player/Camera/PlayerCameraEffects.cs)가 시작할 때 켬). 그래서 PC 파이프라인 볼륨 프로필 [GameLook](../Settings/Rendering/GameLook.asset)(톤매핑 끔, Bloom 0.25, 비네트 0.2)이 항상 적용된다. 톤매핑은 VRChat 기본 화면처럼 꺼 두어 캐릭터가 제작자가 정한 색 그대로 나온다. 처치 연출([KillConfirmEffect](../Scripts/Combat/Feel/KillConfirmEffect.cs))은 노출·대비·채도·색 필터·화이트 밸런스·블룸·비네트를, 저체력 화면([LowHealthGrade](../Scripts/Player/Combat/LowHealthGrade.cs))은 채도·색 필터를 절댓값으로 덮어쓴다. 저체력 화면이 켜져 있을 때 처치는 색을 돌려주지 않는다. 색이 돌아오면 회복한 것처럼 보이기 때문이다. 대신 번쩍임, 렌즈 펀치, 색 번짐, 대비, 블룸으로 처치를 보인다(GunFeel의 `Kill screen: ordinary kills at low health`). 그래서 이 값들을 프로필이나 맵 볼륨에서 올리면 처치할 때 화면이 오히려 어두워지거나 밋밋해진다. 밝기와 색감은 조명으로 맞춘다([조명 안내](LightingGuide.md)). [SampleSceneProfile](../Settings/Rendering/SampleSceneProfile.asset)은 모바일 설정만 쓴다. 큰 낙하 효과는 [Resources/Rendering/FallWarp](../Resources/Rendering/FallWarp.asset) 볼륨 프로필을 실행 중에 복사해 우선순위 100의 전역 볼륨으로 쓴다. 이 에셋이 프로젝트에 있어야 빌드에서 렌즈 왜곡·색수차 셰이더가 빠지지 않는다(URP의 쓰지 않는 후처리 변형 제거). 세기는 에셋이 아니라 `FallScreen` 행에서 바꾼다.

## 실행 중 생성되는 분류와 혈흔 풀

Play 중 효과가 필요하면 `EFFECTS → Audio / World / Screen`을 자동으로 만들고 소리·월드 효과·화면 표시를 모은다. 콘솔 소환 적은 `ENEMIES → Console`, 웨이브 적은 `ENEMIES → Waves`에 생성된다. 수동으로 빈 오브젝트를 미리 만들 필요는 없다. 스크립트 변경 후에는 Play를 종료하고 다시 시작해 생성 위치를 확인한다.

`EFFECTS → World → Impact VFX → Blood Decals (Pool)`의 회색 혈흔은 다음 타격에 재사용할 대기 오브젝트다. 풀은 **최대 32개**로 제한되며 무한히 증가하지 않는다. 비활성 혈흔은 표시하지 않고 수명 업데이트도 건너뛴다. 반복 생성·삭제 부하를 줄이기 위해 풀을 유지한다. 화면에 남는 시간은 [GunFeel.asset](../Resources/Combat/GunFeel.asset)의 `Blood Decal Seconds`로 조절한다. 이 시간이 지나면 1.2초 동안 흐려진 뒤 풀로 돌아간다.

`EFFECTS → World → Impact Pool`은 DAVFX 이펙트를 미리 만들어 둔 대기열이다. 전투 중 생성이 없도록 맵을 불러올 때 [CombatWarmup](../Scripts/Combat/Feel/CombatWarmup.cs)에서 한 번 만들고, 복사본은 땅속 5000m에서 켜진 채 기다린다. VFX Graph는 처음 그릴 때 약 0.5초 멈추므로, 맵이 뜨는 동안 모든 복사본을 시야 끝에서 한 번씩 재생해 그 멈춤을 맵 시작으로 옮긴다. 첫 타격에 처음 만들어지는 시스템을 새로 넣으면 `CombatWarmup`에도 추가한다. 킬 화면, 낮은 체력, 슬로 모션이 쓰는 후처리 효과 조합도 플레이어 카메라가 생기자마자 눈에 안 보이는 세기로 한 프레임씩 그려 셰이더를 미리 만든다([PostProcessWarmup](../Scripts/Combat/Feel/PostProcessWarmup.cs)). 효과를 새로 섞는 화면 효과를 만들면 그 조합을 여기에 더한다. 손의 엄지 피부 보정이 쓰는 몸 메시 복사본도 첫 렛지가 아니라 몸을 만들 때 만든다(`HandSkinCorrection.CopyAhead`). 소리 묶음(`Resources/Audio`의 Movement, Voice, HitConfirm, Bullets)은 처음 쓰일 때 담긴 클립을 한꺼번에 불러오므로 `CombatWarmup`에서 미리 불러 둔다. 숨소리가 힘쓸 때만 나게 된 뒤로는 Voice의 첫 사용이 첫 회피나 반격이었다. 새 소리 묶음을 만들면 `CombatWarmup`의 `SoundBanks` 목록에 더한다. 어떤 프리셋을 어떤 크기와 밝기로 쓸지는 [ImpactEffects.csv](../Resources/Tables/ImpactEffects.csv)에서 고치며, Play 중에 저장해도 바로 반영된다. 복사본을 다 쓰면 가장 오래된 것을 다시 쓰므로, 끊기거나 이펙트가 튀어 보이면 `copies`를 늘린다. 표에 적는 프리셋은 [ImpactLibrary](../Resources/Combat/ImpactLibrary.asset)에 있어야 한다. 프리셋 생김새는 DAVFX의 `URP/Demo Scene`에서 볼 수 있다. 이펙트 재생에는 `com.unity.visualeffectgraph` 패키지가 필요하다.

벽 착탄은 맞은 표면의 행을 쓰고, 그 표면의 행이 없으면 [Surfaces.csv](../Resources/Tables/Surfaces.csv)의 부모를 따라 올라가 마지막에 `Concrete`를 쓴다. 풀의 복사본은 다음 착탄으로 옮겨 가므로, 팩 이펙트에 포함된 데칼과 빛 폭발(`VFXLightBurstCompanion`)은 끈다. 총알 구멍은 `EFFECTS → World → Bullet Holes`의 데칼 64개를 돌려 쓰며, PC_Renderer의 Decal 기능이 있어야 보인다. Decal은 Screen Space 방식이라 다른 머티리얼에 셰이더 변형을 더하지 않는다. `Prefabs/Effects/Weapons`의 `Impact_*` 프리팹과 `Art/Sprites/Weapons`의 `Impact_Wall_*`은 연결을 끊은 옛 착탄 자료다.

적의 분류와 웨이브 집계는 별개다. 웨이브 일시정지·중단은 해당 WaveManager가 추적하는 적에게만 적용하며 콘솔 적은 그대로 유지한다.
