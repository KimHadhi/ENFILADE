# 스크립트 길잡이

게임 코드는 **기능별 폴더**로 나뉘어 있다. 고치려는 기능의 폴더를 아래 표에서 찾아 그 폴더의 README를 연다. 거기에 "무엇을 고치려면 어느 파일"이 줄마다 적혀 있다.

프로젝트 전체 구조는 [구조 안내](../../../Docs/Architecture.md), 실행과 조작은 [프로젝트 안내](../../../README.md), 모드는 [모드 만들기](../../../Docs/Modding.md)에 있다.

## 폴더 지도

| 폴더 | 무엇 | 안에 있는 것 |
| --- | --- | --- |
| [Core](Core/README.md) | 바탕 | 게임 어디서나 쓰는 바탕이다. 프레임 순서, 표, 글자, 모드, 설정, 로딩, 씬 폴더 정리. |
| [Player](Player/README.md) | 플레이어 | 플레이어 한 명의 몸과 행동이다. 이동, 보이는 몸, 카메라, 소리, 얼굴, 옷, 체력·쓰러짐·부활, 콜아웃, 잠입 행동. |
| [Weapons](Weapons/README.md) | 총 | 총의 규칙과 손에 든 총이다. 탄약·발사·조준 규칙(Core), 쏠 때의 효과(Effects), 손에 든 총과 부착물(FPSBody, KINEMATION) |
| [Melee](Melee/README.md) | 근접 | 주먹, 클래스 칼, 밀치기와 발차기, 가드와 패링, 처형·암살. |
| [Grenades](Grenades/README.md) | 투척물 | G로 던지는 것: 수류탄·섬광탄·연막탄·클레이모어·투척 칼과 던지는 팔. |
| [Classes](Classes/README.md) | 클래스 | 일곱 클래스의 능력과 능력 카드, 클래스 성장. |
| [Enemies](Enemies/README.md) | 좀비 | 좀비의 체력·판단·몸·특수 좀비·고어. |
| [Combat](Combat/README.md) | 전투 공통 | 플레이어와 적이 함께 쓰는 전투 규약이다. 피해, 판정, 맞았을 때의 표시와 소리, 시체, 탄착, 특수 순간, 표식. |
| [Modes](Modes/README.md) | 게임 방식 | 캠페인, 엔필레이드 타워, 웨이브, 허브(사격장·결과 연출). |
| [Economy](Economy/README.md) | 돈과 상점 | 돈, 돈 떨구기, 휴게소 상점, 잠입 보상. |
| [Pickups](Pickups/README.md) | 줍기 | 맵에 놓이는 총·탄약·구급상자·실드·돈. |
| [Network](Network/README.md) | 같이 하기 | 방 만들기와 찾기(Rooms), 모든 PC가 같은 것을 보게 하는 동기화(Sync). |
| [UI](UI/README.md) | 화면 | 메뉴 키트, 글꼴, 채팅, 조준점, 메인 메뉴, 설정 메뉴, HUD. |
| [Audio](Audio/README.md) | 소리 | 볼륨 채널, 소리 묶음과 큐, 환경음과 잔향, 캐릭터가 내는 소리. |
| [World](World/README.md) | 맵 조각 | 문, 사다리, 집라인, 철봉, 물리 소품, 표면 종류, 맵 목록과 색감·컬링. |
| [Dev](Dev/README.md) | 개발용 | 개발자 콘솔과 명령, 시험용 표적·더미, 기록 도구. 플레이어가 보는 기능은 여기 두지 않는다. |
| [../Editor](../Editor/README.md) | 편집기 도구 | ENFILADE 메뉴의 도구: 맵 짓기, 캐릭터·총 가져오기, 표·글자·글꼴·HUD 검사 |

지켜야 할 것 세 가지:

1. **숫자는 표에.** 밸런스·크기·시간·색은 `Data/Resources/Tables`의 CSV에 두고 코드는 이름으로 읽는다(`GameTables`, `TableCells`). 표는 분야별 폴더에 있고 파일 이름으로만 찾는다.
2. **글자도 표에.** 플레이어가 읽는 글자는 모두 `Texts.csv`의 id로 쓴다(`Texts.Get`). 코드에 문장을 적지 않는다.
3. **한 폴더에 한 가지 일.** 새 기능은 그 기능의 폴더에 두고, 한 폴더의 스크립트가 스무 개를 넘으면 나눈다. 여러 기능이 같이 쓰는 것만 `Core`·`Combat`·`UI/Shared`에 둔다.

## 새로 만들기 전에: 같이 쓰는 도우미

같은 몇 줄이 파일마다 복사되어 있던 것을 0.9.2에서 한곳으로 모았다. 비슷한 것이 필요하면 새로 적지 말고 아래 것을 쓰고, 없으면 여기에 더한다.

| 필요한 것 | 쓰는 것 |
| --- | --- |
| 표의 한 줄에서 칸 읽기 | [TableCells](Core/Tables/TableCells.cs): `row.Number("damage", 10f)`, `row.Text("name")`, `row.Vector("wrist", fallback)`, `TableCells.Parts("a\|b")` |
| 이징 곡선 | [Ease](Core/Common/Ease.cs): `Ease.Out`, `Ease.InOut`, `Ease.Smooth`, 프레임 수와 무관한 따라가기 `Ease.Share(rate, dt)` |
| 트랜스폼을 이름으로 찾기, 경로 | [Hierarchy](Core/Common/Hierarchy.cs): `Hierarchy.Find(root, name)`, `Hierarchy.PathOf(part, root)` |
| 메시·본 가중치 읽기 | [MeshTools](Core/Common/MeshTools.cs): `MeshOf(renderer)`, `BoxOf(bounds, matrix)`, `Strongest(weight)` |
| 지금 키 입력을 게임이 받는가 | [InputFocus](UI/Shared/InputFocus.cs): `TakesInput()`, 메뉴까지 보는 `TakesPlayInput()`, `Pressed(action)` |
| 코드로 UI 조각 만들기 | [FeelUI](Combat/Feedback/FeelUI.cs): `Rect`, `Panel`, `Label` / [KitStyle](UI/Kit/KitStyle.cs): `Stretch`, `Fill`, `Cut`, `With(color, alpha)` |
| 플레이어 찾기·이름 | [PlayerRoster](Player/PlayerRoster.cs): `Local`, `All`, `IdOf`, `ById` / [PlayerTally](Player/Life/PlayerTally.cs): `NameOf`, `ColoredNameOf` |
| 왼팔을 경로대로 움직여 무언가 쥐기 | [PathArm](Player/Body/Hands/PathArm.cs)을 상속 (수류탄 팔 GrenadeArm, 수리 팔 RepairArm이 쓴다) |
| 실행 중 오브젝트를 하이어라키에 정리 | [RuntimeSceneHierarchy](Core/Scene/RuntimeSceneHierarchy.cs): `Keep`, `Group` |
| 에셋 폴더의 자리 (편집기 도구) | [ProjectFolders](Core/ProjectFolders.cs): `ArtByName`, `DataByName`, `PrefabsByName`, `Guns` |

## 코드를 읽는 순서

이동의 진입점은 `PlayerMovement`다. 프레임마다 입력·상태·물리 서비스를 연결하고, 외부 기능에는 `CurrentState`, `Velocity`, `ResetVelocity()` 같은 API를 제공한다. 내부 상태 전환과 재컴파일 복구는 [이동 코드 구조](Player/Movement/README.md)를 참고한다.

전투는 **입력 → 공격 규칙 → 접촉·피해 → 반응·표시** 순서로 읽으면 된다. 예를 들어 총은 `PlayerWeaponInput → WeaponController / WeaponRuntime → AttackTrace → IDamageReceiver`로 이어진다. 피해를 받은 대상의 처리에서 발생한 전투 이벤트는 `GunFeelDirector`가 표시·소리로 연결한다. 새 무기나 적을 추가하는 순서는 [전투 코드 구조](Combat/README.md)에 있다.

## 플레이어에 붙는 것과 실행 순서

씬의 플레이어에는 이동·시점·입력·무기·슬롯만 둔다. 나머지는 Play 시작 때 `PlayerMovement.EnsureInitialized`가 붙이고 `Player.csv`의 같은 이름 행과 연결한다. 몸은 `Resources/Characters/PlayerBody` 프로필에 캐릭터가 있을 때만 붙는다.

| 행 | 컴포넌트 |
| --- | --- |
| `Body`, `Hands`, `Footsteps` | [PlayerBody](Player/Body/PlayerBody.cs), [PlayerBodyHands](Player/Body/Hands/PlayerBodyHands.cs), [PlayerFootsteps](Player/Audio/PlayerFootsteps.cs) |
| `Sounds`, `MovementAudio`, `Breathing`, `Wind`, `FallAudio`, `FixtureAudio` | [CharacterSounds](Audio/CharacterSounds.cs), [PlayerMovementAudio](Player/Audio/PlayerMovementAudio.cs), [PlayerBreathing](Player/Audio/PlayerBreathing.cs), [PlayerWindAudio](Player/Audio/PlayerWindAudio.cs), [PlayerFallAudio](Player/Audio/PlayerFallAudio.cs), [PlayerFixtureAudio](Player/Audio/PlayerFixtureAudio.cs) |
| `Punch`, `FallScreen` | [PlayerMovementPunch](Player/Camera/PlayerMovementPunch.cs), [PlayerFallScreen](Player/Camera/PlayerFallScreen.cs) |

한 프레임 안의 순서는 [FrameOrder](Core/FrameOrder.cs) 한 파일에 모여 있다(2026-11-09). 순서가 정해진 스크립트는 모두 `[DefaultExecutionOrder(FrameOrder.이름)]`으로 거기서 숫자를 가져오고, 각 자리의 이유도 거기 적혀 있다. 누가 누구보다 먼저 도는지 바꾸려면 그 파일만 고친다. 앞 단계의 결과를 뒤 단계가 읽으므로 순서를 잘못 바꾸면 카메라·손이 한 프레임 늦는다. Unity는 Update를 이 순서로 모두 돌린 뒤 Animator 그래프(몸의 클립과 FPSAF)를 돌리고, LateUpdate를 다시 이 순서로 돌린다.

몸 쪽의 큰 흐름은 다음과 같다.

| 순서 | 컴포넌트 | 하는 일 |
| ---: | --- | --- |
| -100 | `PlayerMovement` | 입력·상태·실제 이동 |
| 0 | `PlayerLook` | 시선 |
| 200 | `PlayerCameraEffects` | 흔들림·기울이기와 카메라 기준 위치 |
| 250 | `PlayerBody` | Update에서 클립 고르기. LateUpdate에서 시선 굽힘(`SpineLook`), 몸 위치, 발 IK, 눈 시점으로 카메라 최종 위치 확정(F5면 그 뒤 카메라만 뒤로), 마지막으로 머리를 그 카메라로 굽혀 옴(`HeadReach`). 이 안의 순서는 `PlayerBody.LateUpdate` 주석에 적혀 있다 |
| 260 | `PlayerFootsteps` | IK가 끝난 발로 접지 판단 |
| 270 | `ThirdPersonCamera` | 애송이 모드의 몸 밖 카메라와 처형 시네마틱. 몸이 눈에 붙인 카메라만 옮긴다. 총·조준은 그 뒤라 화면 가운데를 쏜다 |
| 295 | `FPSBody` | Update에서 FPSAF 입력. LateUpdate에서 FPSAF가 놓은 총을 시야에 붙잡기 |
| 300 | `WeaponController` | 확정된 눈 시점(`PlayerView`)으로 총 자세, 카메라 조준선으로 발사 방향 |
| 320 | `PlayerBodyHands` | 확정된 총·주먹 위치로 손 놓기 |
| 330 | `FPSBodyAnimator` | FPSAF의 늦은 일(카메라 흔들림), 그 뒤 손을 따라가는 총 |
| 336 | `PointArm` | F를 꾹 누른 손가락질: 왼팔·손·손가락을 가리키는 자세로 놓고, 손끝이 친구를 누르면 `SkinPoke`에 알린다(가리키는 몸에만 붙는다) |
| 340 | `PlayerBodyCloth` | 끝난 자세에서 옷감 |
| 345 | `SkinPoke` | 손끝 눌림 슬롯을 되돌리고 셰이더 전역 값과 눌리는 몸의 렌더러 값을 쓴다 |

## 입력과 설정

대부분의 키 바인딩은 [Player.inputactions](../Input/Player.inputactions)에 있다. 이동은 `MovementInput`, 사격은 `PlayerWeaponInput`, UI는 해당 UI 컴포넌트가 읽는다. 앉기는 상태 코드에서 키를 직접 읽지 않고 `MovementInput.CrouchHeld`/`CrouchPressed`를 쓴다. 그래야 Esc 메뉴의 "눌러서 전환" 설정이 모든 상태에 적용된다. 게임이 읽는 키는 모두 이 에셋의 액션이다(슬롯 1~4와 숫자패드도 `Combat/Slot1~4`, 사격 모드·총 꾸미기·살펴보기·라이트·돌 던지기·돈 떨구기·능력 카드·3인칭·콘솔도 액션). 플레이어가 바꾼 키는 [KeyBindings](Core/Settings/KeyBindings.cs)가 바인딩 덮어쓰기로 얹으니, 키를 새로 읽는 코드는 `Keyboard.current`가 아니라 `KeyBindings.Pressed("맵/액션")`을 쓰고 키 설정 창 줄(`KeyBindings.Entries`)을 더한다. 그러지 않으면 키 설정 창이 그 키를 모른다.

`PlayerMovement.Configuration.cs`는 별도 컴포넌트가 아니라 같은 `PlayerMovement` 클래스의 직렬화 필드다. `MovementAbilitySettings`는 그 안의 설정 데이터다. 수치는 `Player.csv`(`Posture`·`Move`·`Ability` 행)가 필드 이름으로 덮어쓰므로, 표에 있는 값은 인스펙터에서 바꿔도 Play 때 표 값으로 돌아간다. 필드 이름을 바꾸면 표의 키도 같이 바꾼다. 컴포넌트 안에 든 설정 객체는 `GameTables.BindNested`로 연결한다.

## 기능을 추가할 때

1. 플레이어 전용 입력·선택은 `Player`, 공통 피해·무기 규칙은 `Combat`에 둔다.
2. 실제 위치 변경은 이동 서비스에 연결하고, HUD·표시 코드에서 HP·탄약·플레이어 위치를 직접 바꾸지 않는다.
3. 한 공격의 여러 콜라이더는 같은 `DamageHit.Id`를 사용한다. 새 공격에는 새 ID를 발급한다.
4. 비활성화·사망·상태 취소·스크립트 재로드에서 입력, 충돌 무시, 시간 효과, 임시 표시가 정리되는지 확인한다.
5. 변경한 기능을 [검증 안내](../../../Docs/Guides/ValidationGuide.md)의 항목대로 실제 플레이로 확인한다.
6. 코드로 메시를 짓는 UI 그림(`MaskableGraphic`·`Graphic`을 이어받은 클래스)에는 `[RequireComponent(typeof(CanvasRenderer))]`를 붙인다. 이 uGUI(2.0)의 Graphic은 CanvasRenderer를 스스로 요구하지 않아서, 없이 만들면 에디터에서 그려지지 않고 RectMask2D 아래에서는 MissingComponentException이 콘솔을 채운다([유지보수 안내](../../../Docs/Guides/MaintenanceGuide.md#코드로-그리는-ui-그림-graphic)).
7. 메뉴에서 누르는 판은 그려진 모양대로 커서를 받게 한다([KitHit](UI/Kit/KitHit.cs)). 기운 판(`KitSkew`)이나 잘린 모서리 판(`KitStyle.Cut`·`Shape`)을 그대로 raycast target으로 쓰면 저절로 맞는다. 판 대신 투명한 이미지가 클릭을 받게 만들었다면 그 이미지에도 판과 같은 값으로 `KitSkew.On`을 건다(사각형으로 두면 기운 가장자리 근처에서 옆 판이 눌리거나 판을 놓친다. 사용자 11-12: "옷장이나 설정창 슬라이더나 마우스 커서대로 선택이 잘 안 됨"). 마우스를 올리면 움직이는 판은 `KitHover`처럼 원래 자리를 판정에 남긴다(`KitHit.Pad`·`LeanOf`): Unity UI 입력은 마우스가 멈춰 있어도 매 프레임 다시 판정하므로, 커서 아래에서 비켜나는 판은 들어갔다 나갔다를 되풀이한다. 판의 `raycastPadding`은 키트가 쓴다. 슬라이더는 `SettingsWidgets.Slider`(KitSlider)로 만든다. 맞는지는 콘솔 `/hits`로 본다.
8. 항목이 늘어날 수 있는 메뉴 목록(총·클래스·옷 칸·기록)은 [KitScroll](UI/Kit/KitScroll.cs)에 넣어 페이지의 제목 아래에서 페이지 바닥 위까지 늘여 붙인다(앵커를 위아래로 늘이면 16:9·16:10·21:9·4:3 어디서나 맞는다). 줄은 `Rows`에 위에서부터 놓고 다 놓은 뒤 `Fit(높이)`, 다시 지을 때는 `Offset`을 남겼다가 돌려주고 고른 줄은 `Show`로 보이게 한다. 1920 폭을 가정한 고정 x(`1920f - …`)나 972 높이를 가정한 고정 바닥(940 같은)은 쓰지 않는다: 페이지의 `rect`에서 구한다(2026-11-13 무기 탭 총 28자루가 아래 안내줄을 덮었다)

## 성능을 살필 때

Profiler에서 대표 상황의 CPU 시간과 `GC.Alloc`을 먼저 확인한다. API 이름만 보고 전체 공격이 무할당이라고 판단하지 않는다.

- 첫 사용 끊김(처음 쏘기·처음 처치·처음 고어)은 셰이더 변형·그래픽 상태·풀·글자를 처음 만드는 비용이다. 새 효과를 더하면 [CombatRehearsal](Core/Loading/CombatRehearsal.cs)이 그 재질이나 모델을 로딩 화면 뒤에서 한 번 그리게 하고(게임 셰이더는 `Resources/Combat/Shaders`에 두면 저절로), 풀은 맵을 불러올 때 [CombatWarmup](Combat/Feedback/CombatWarmup.cs)에서 만든다. 에디터의 Logs/hitches.txt(끊긴 프레임)와 Logs/loads.txt(로드 단계별 시간, 무엇이 로드 때 가장 많이 짓는지)로 재고 고친다.
- `AttackTrace`는 보통 단일 Raycast를 사용하고, 제외할 충돌체를 만나면 다중 결과를 검사한다. 버퍼가 꽉 차면 누락된 가까운 벽을 놓치지 않도록 전체 결과를 다시 조회한다. 최적화할 때 이 포화 상황과 공격자 제외 규칙을 유지한다.
- `ShoveSweep`의 밀치기는 여러 적을 스칠 수 있다. `PlayerCombatActions`의 돌진 밀치기는 벽에 가리지 않은 가장 가까운 적 하나만 처리한다. 단일 타격 질의로 바꾸면 군중 밀치기와 벽 차폐가 달라질 수 있으므로, 배열 할당 개선과 타격 규칙 변경을 구분한다.
- `WeaponAimSolver`는 쏘아 넘기는 낮은 엄폐물([ShootOverCover](Weapons/Core/ShootOverCover.cs): 서포트 바리케이드·철조망·탄약 상자, Tables/ShootOverCover.csv의 맵 꾸미기 종류)을 눈이 그 윗면보다 높을 때 건너뛰고, 조준 중에는 조준했을 때의 총구로 잰다(`FPSBody.Prepare`, `FPSGun.AimedMuzzle`). 총구 주변 검사는 재사용 버퍼를 사용한다. 버퍼가 가득 찼고 자기 콜라이더만 발견됐다면 전체 조회로 엄폐 누락을 막는다. 일반 경로의 배열 할당은 줄였지만 전체 발사의 무할당이나 프레임 성능을 보장하는 수치는 아니다.
- 찌르기(`SkinPoke`·`PokeBody`)는 누가 F로 가리키는 동안 그 시선 근처의 몸만 일한다. 몸 표면은 몸마다 처음 한 번 한 프레임에 2만 정점씩 나눠 읽고, 셰이더는 아무도 누르지 않으면 슬롯 수 하나, 눌리는 몸이 아니면 렌더러 값 하나만 보고 끝난다.
- 수신자 캐시·풀링을 도입한다면 부모 콜라이더의 대상 식별, 재사용 시 참조 갱신, 중복 타격 방지를 함께 검사한다.
