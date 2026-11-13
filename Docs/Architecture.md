# 구조 안내

ENFILADE가 어떻게 짜여 있는지, 새 기능을 어디에 두는지를 적는다. 0.9.0에서 프로젝트 전체를 이 구조로 다시 정리했다. 원칙은 세 가지다.

1. **이름이 곧 자리다.** 폴더 하나는 한 가지 일만 하고, 폴더 이름만 보고 무엇이 들었는지 알 수 있어야 한다.
2. **숫자와 글자는 코드 밖에 있다.** 밸런스, 크기, 시간, 색, 화면 글자는 모두 CSV 표에 있고 컴파일 없이 바뀐다. 모드는 그 표 위에 덧씌운다.
3. **안 쓰는 것은 두지 않는다.** 데모, 예제, 죽은 코드는 지운다. 지운 것은 git 기록에 있다.

## 프로젝트 맨 위

```
ENFILADE/
  Assets/
    _Project/          게임 그 자체 (아래)
    ThirdParty/        사거나 받은 에셋 팩. 여기 것은 고치지 않고 가져다 쓴다
    _PoiyomiShaders/   Poiyomi 셰이더 도구 (도구가 자리를 정한다)
    _LockedShaderCache/  잠근 Poiyomi 셰이더 (캐릭터 재질이 쓴다)
    ProBuilder Data/   ProBuilder 설정 (테스팅 룸이 쓴다)
  Mods/                모드 폴더. 게임을 켤 때 읽는다
  Docs/                문서
  ArtSource/           블렌더 작업실 (.blend). 저장하면 게임에 들어간다
  Tools/               Unity 밖에서 쓰는 도구 (블렌더·파이썬·검사)
  Packages/  ProjectSettings/
```

## Assets/_Project 안

| 폴더 | 무엇 | 규칙 |
| --- | --- | --- |
| `Scripts` | 게임 코드 | 기능별 폴더 16개. [스크립트 길잡이](../Assets/_Project/Scripts/README.md) |
| `Editor` | Unity 편집기 도구 | 메뉴 막대의 **ENFILADE** 메뉴에 모인다. [편집기 도구](../Assets/_Project/Editor/README.md) |
| `Art` | 보고 듣는 것 전부 | `Animations`, `Audio`, `Models`, `Materials`, `Textures`, `Shaders`, `Sky`, `Sprites`, `Fonts`, 그리고 이름으로 부르는 것의 서랍 `Resources` |
| `Data` | 숫자와 장부 | `Resources/Tables`(표), `Resources`의 목록 에셋·소리 묶음, `Guns`(무기 데이터, 총 프리팹, 캐릭터별로 구운 총 클립), `UI` |
| `Data/Resources/Tables` | 모든 숫자와 글자 | 분야별 폴더 14개. 파일 **이름**으로만 찾으므로 폴더는 자유롭게 옮겨도 된다 |
| `Prefabs` | 조립해 둔 물건 | `UI`(GameHUD 등), `Effects`, `Props`, 이름으로 꺼내는 것의 서랍 `Resources`(플레이어, 적, 클래스 소품) |
| `Scenes` | 씬 | `MainMenu`, `Hub`, `Campaign`(1~4장, 타워), `Development`(테스팅 룸) |
| `Input`, `Settings` | 입력 액션, 렌더링 설정 | |

### Scripts의 폴더

| 폴더 | 무엇 |
| --- | --- |
| `Core` | 바탕: 프레임 순서(`FrameOrder`), 표(`Tables`), 글자(`Texts`), 모드(`Mods`), 설정(`Settings`), 로딩(`Loading`), 씬 폴더 정리(`Scene`) |
| `Player` | 플레이어 한 명: `Movement`, `Body`(+`Hands`·`IK`·`Feet`·`Cloth`·`Measure`), `Camera`, `Audio`, `Face`, `Outfits`, `Poke`, `Life`, `Comms`, `Stealth` |
| `Weapons` | 총: `Core`(규칙·데이터), `Effects`, `FPSBody`(손에 든 총) |
| `Melee` | 주먹, `Shove`, `Guard`, `Executions`, `Knives` |
| `Grenades` | 투척물 |
| `Classes` | 클래스와 능력 카드 (`Knight`, `Support`, `Detente`, `Hound`, `Point`) |
| `Enemies` | 좀비: 판단과 체력, `Body`, `Elites`, `Gore` |
| `Combat` | 공통 전투 규약: `Damage`, `HitDetection`, `Feedback`, `Corpses`, `Impacts`, `Moments`, `Marks` |
| `Modes` | 게임 방식: `Campaign`, `Tower`, `Waves`, `Hub`(+`Range`, `Results`) |
| `Economy`, `Pickups` | 돈과 상점, 줍기 |
| `Network` | 같이 하기: `Rooms`(방), `Sync`(동기화) |
| `UI` | 화면: `Kit`, `Fonts`, `Shared`, `Chat`, `Crosshair`, `Menu`, `Settings`, `Hud`(+조각별 폴더) |
| `Audio`, `World` | 소리, 맵 조각(문·사다리·집라인·표면) |
| `Dev` | 개발자 콘솔, 시험 도구, 기록 도구 |

## 게임이 켜지는 순서

1. **메인 메뉴** (`Scenes/MainMenu`, `UI/Menu/MainMenuScreen`). 혼자 하기나 방 만들기를 고르면 허브로 간다.
2. **씬 바꾸기**는 언제나 `SceneLoad.Go`를 거친다. 로딩 화면(`LoadingScreen`)이 덮고, 씬이 열리면 `SceneWarmup`이 전투가 처음 그릴 것을 미리 그린 뒤 걷힌다.
3. **플레이어**는 씬의 `PlayerSpawnPoint`가 `PlayerFactory`로 만든다(`Resources/Characters/Player` 프리팹). 프리팹에는 이동·시점·입력·무기만 있고, 나머지 부품은 Play가 시작될 때 `PlayerMovement.EnsureInitialized`가 붙이면서 `Player.csv`의 같은 이름 줄과 묶는다.
4. **혼자서 켜지는 시스템.** 소리, HUD 카메라, 콘솔, 로딩처럼 씬과 상관없는 것은 `[RuntimeInitializeOnLoadMethod]`로 스스로 켜진다. 씬에 미리 놓을 필요가 없다.
5. **한 프레임의 순서**는 [FrameOrder](../Assets/_Project/Scripts/Core/FrameOrder.cs) 한 파일에 있다. 누가 누구보다 먼저 도는지 바꾸려면 그 파일만 고친다.
6. **한 판의 흐름**은 `HubFlow`가 잇는다: 허브 → 장(`CampaignRun`) 또는 타워(`TowerRun`) → 결과(`MatchResults`, 허브의 `ResultsShow`) → 허브.

## 숫자와 글자가 흐르는 길

```
Data/Resources/Tables/<분야>/<이름>.csv          게임의 표
        +  Mods/<모드>/Tables/<이름>.csv      모드의 표 (있으면 줄 단위로 덧씌움)
        ↓  GameTables (이름으로 찾아 읽고 기억)
   Bind(표, 줄, 컴포넌트)    컴포넌트의 같은 이름 필드에 써 넣음
   TryGetRow / RowIds       줄을 직접 읽음 → TableCells: row.Number("damage", 10f)
   Texts.Get("id")          화면 글자 (Texts.csv)
```

- 표는 **파일 이름으로만** 찾는다(`"Weapons"`). 어느 폴더에 있는지는 코드, 편집기 도구(`TablePaths.Of`), 파이썬 도구(`table_file`) 어디에도 적지 않는다.
- 편집기에서 표를 저장하면 `GameTables.Reload`가 불려 실행 중인 게임에도 바로 들어간다. 빌드에서는 콘솔 `/mods reload`.
- 플레이어가 읽는 글자는 모두 `Texts.csv`의 id다. Unity의 ENFILADE → 글자 → 글자 편집기에서 고치고, 코드에 문장이 남았는지는 같은 메뉴의 하드코딩 검사가 찾는다.
- 저장되는 것(설정, 키, 기록, 옷장 프리셋)은 표가 아니라 사용자 폴더의 JSON 파일이다([유지보수 안내](Guides/MaintenanceGuide.md)).

## 맵은 코드로 짓는다

허브, 캠페인 네 장, 타워는 씬을 손으로 만들지 않고 `Editor/Maps`의 빌더가 짓는다(ENFILADE → 맵). 씬에서 손으로 고친 것은 다시 지으면 사라지므로 배치는 빌더 코드와 표(`Tables/Maps`의 `MapArt`, `MapDecals`, `MapLook`, `MapSurfaces` 등)에서 고친다. 빌더는 씬을 `◤━━ 지형`, `◤━━ 시스템`, `◤━━ 적 나오는 곳`처럼 이름 붙은 묶음으로 나눠 놓는다.

## 실행 중의 하이어라키

실행 중에 만들어지는 오브젝트는 [RuntimeSceneHierarchy](../Assets/_Project/Scripts/Core/Scene/RuntimeSceneHierarchy.cs)로 묶는다. 새 코드가 `new GameObject(...)`를 씬 맨 위에 그냥 두지 않게 한다.

| 무엇 | 어떻게 | 어디에 보이나 |
| --- | --- | --- |
| 씬이 바뀌어도 남는 것 | `RuntimeSceneHierarchy.Keep(host, "Systems")` (`DontDestroyOnLoad` 대신) | `◤━━ ENFILADE (씬이 바뀌어도 남는 것)` 아래 Systems · UI · Dev |
| 씬 안의 효과·소리 | `FeelAssets.CreateRuntimeRoot(이름)` 또는 `Group(host, Effects, "World")` | `◤━━ EFFECTS` 아래 World · Screen · Audio |
| 적 | `GetGroup(scene, Enemies, "Campaign")`을 부모로 | `◤━━ ENEMIES` 아래 Campaign · Waves · Tower · Console |
| 시체, 파편, 떨어진 것 | `Group(host, World, "Bodies")` | `◤━━ WORLD` 아래 Bodies · Debris · Pickups |
| 씬마다 하나인 관리 오브젝트 | `Group(host, Systems, "Combat")` | 빌더의 `◤━━ 시스템` 안 (없으면 `◤━━ SYSTEMS`) |
| 필요할 때 뜨는 화면 | `Group(host, Ui, "Screens")` | `◤━━ UI` 아래 Screens |

폴더는 늘 원점에 가만히 있어서 그 아래 오브젝트의 위치와 물리는 달라지지 않는다.

## 같이 하기

### 무엇이 어디에 있나 (정의)

예전에는 그림이 `Art`에도 `Resources`에도 있었다. 0.9.1부터는 아래 네 줄이 전부다.

1. **종류가 폴더를 정한다.** 그림·소리·모델·셰이더는 `Art`에만, 숫자와 목록은 `Data`에만, 조립한 것은 `Prefabs`에만 있다.
2. **`Resources`는 종류가 아니라 서랍이다.** Unity는 `Resources`라는 이름의 폴더 안의 것을 이름으로 불러올 수 있다. 게임이 이름으로 부르는 것만 서랍에 넣고, 서랍은 종류마다 하나씩 딱 셋이다: `Art/Resources`, `Data/Resources`, `Prefabs/Resources`. 게임이 부르는 이름은 서랍부터의 경로이고(`Resources.Load("UI/Icons/Fist")`), 어느 서랍에 있든 같다. 맨 위의 `Resources` 폴더는 없다.
3. **서랍 밖의 것은 연결해서 쓴다.** 프리팹·에셋·씬이 직접 가리키거나, 편집기 도구가 폴더를 읽어 장부를 만든다(예: `Art/Audio/Guns`의 폴더들이 `Data/Resources/Audio/Guns` 소리 묶음이 된다).
4. **게임이 쓰지 않는 것은 `Assets`에 두지 않는다.** 재료·후보·예전 것은 Unity 밖의 [ArtSource/Library](../ArtSource/Library/README.md)에 둔다. 쓰기로 하면 그때 `Art`의 맞는 폴더로 가져온다.

편집기 도구가 서랍의 자리를 쓸 때는 [ProjectFolders](../Assets/_Project/Scripts/Core/ProjectFolders.cs)의 이름(`ArtByName`, `DataByName`, `PrefabsByName`, `Guns`)을 쓰고 폴더를 직접 적지 않는다. 규칙 2는 구조 검사가 본다(다른 곳의 `Resources` 폴더, 서랍에 안 맞는 종류, 한 이름에 두 파일). 규칙 4는 `python Tools/Find-UnusedAssets.py`가 아무도 쓰지 않는 에셋을 찾아 `Logs/unused-assets.txt`에 적어 준다.

방은 `GameSession`이 연다(Netcode for GameObjects, 방장이 진행을 정한다). 플레이어의 자세·전투·시체는 `Network/Sync`의 부품이 다른 PC로 보낸다. 지키는 규칙은 하나다: **내가 보는 화면이 진실이다.** 내 화면에 보이는 월드의 효과는 친구 화면에도 같아야 하고, 남에게만 보이는 효과는 만들지 않는다.

## 새로 더할 때

| 더할 것 | 순서 |
| --- | --- |
| 숫자 하나 | 그 기능의 표에 칸을 더하고, 그 줄에 묶인 컴포넌트에 같은 이름의 필드를 더한다. 표가 없으면 맞는 분야 폴더에 새 CSV를 만든다 |
| 화면 글자 | `Texts.csv`에 id를 더하고 코드는 `Texts.Get("id")`. 글자 편집기에서 고친다 |
| 새 기능의 코드 | 그 기능의 폴더에. 없으면 `Scripts` 아래 새 폴더와 README. 여러 기능이 같이 쓰는 것만 `Core`·`Combat`·`UI/Shared` |
| HUD 조각 | `UI/Hud`의 맞는 폴더. 한 기능만 쓰는 화면은 그 기능 폴더에 둬도 된다. 겹침 검사(ENFILADE → HUD)에 조각을 등록한다 |
| 총, 적 | [전투 제작 안내](Guides/CombatFoundationGuide.md)의 순서 |
| 캐릭터, 옷 | [플레이어 몸](Guides/PlayerBodyGuide.md)의 BOOTH 가져오기 |
| 소리, 모델 | [유지보수 안내](Guides/MaintenanceGuide.md)의 "사운드를 추가하거나 교체하기", "3D 모델 추가하기" |
| 맵 소품 | `Tables/Maps/MapArt.csv`에 줄을 더하고 맵을 다시 짓는다 |
| 편집기 도구 | `Editor`의 맞는 폴더에, 메뉴는 `ENFILADE/<분야>/<하는 일>` |
| 실행 중에 만드는 오브젝트 | 위 "실행 중의 하이어라키" 표대로 묶는다 |

코드 주석은 **왜** 그렇게 했는지를 영어 문서 주석으로 적고, 커밋 메시지는 한국어로 `0.9.x 무엇을: 어떻게`. 바꿀 때마다 `ProjectSettings`의 `bundleVersion`을 한 칸 올린다.

## 검사

Unity 없이 몇 초 만에 도는 구조 검사가 있다. 문서 링크가 끊겼는지, 메타 파일이 짝이 맞는지, GUID가 겹치는지를 본다. 파일을 옮기거나 문서를 고친 뒤에 돌린다.

```powershell
& ./Tools/Test-ProjectStructure.ps1
```

코드는 [코드 도구](../Tools/CodeTools/README.md)가 본다. Unity를 켜지 않고 컴파일러의 라이브러리로 스크립트를 메모리에서만 읽어서, 컴파일 오류(에디터 구성과 플레이어 빌드 구성 둘 다), 아무도 쓰지 않는 코드, 같은 메서드의 사본, 파일 크기를 몇 초 만에 알려 준다. 스크립트를 여럿 고친 뒤에 돌린다.

```powershell
& ./Tools/Check-Code.ps1          # 컴파일 검사
& ./Tools/Check-Code.ps1 dead     # 죽은 코드 → Logs/CodeTools/dead.txt
& ./Tools/Check-Code.ps1 dupes    # 사본 → Logs/CodeTools/dupes.txt
```

아무도 쓰지 않는 에셋은 `python Tools/Find-UnusedAssets.py`가 찾는다(`Logs/unused-assets.txt`).

## 코드를 깔끔하게 두는 약속

0.9.2~0.9.3에서 코드를 한 번 크게 치우면서 정한 것이다.

- **쓰지 않는 코드는 두지 않는다.** "나중에 쓸지도 모르는" 메서드·프로퍼티·필드는 지운다(git에 남아 있다). `Check-Code.ps1 dead`가 0에 가깝게.
- **같은 몇 줄을 복사하지 않는다.** 비슷한 것이 필요하면 [같이 쓰는 도우미](../Assets/_Project/Scripts/README.md#새로-만들기-전에-같이-쓰는-도우미) 표에서 찾고, 없으면 거기에 더한다. 전달만 하는 한 줄짜리 메서드도 만들지 않는다.
- **표의 숫자는 코드가 읽는 것만.** 표에 있는데 아무 코드도 읽지 않는 칸은 연결하거나 지운다. 바꿔도 아무 일도 없는 숫자는 모드를 만드는 사람을 속인다.
- **큰 클래스는 주제별 파일로.** 한 파일이 600줄을 넘어가면 `// ---- 주제 ----` 줄로 구획을 긋고 `클래스.주제.cs`(partial)로 나눈다(`Check-Code.ps1 split`). 본 파일 `클래스.cs`에는 필드와 수명 주기(Awake·Update)와 밖에서 부르는 것만 남는다. 지금 나뉘어 있는 것: FPSBody, FPSGun, WeaponController, HandGrip, ZombieMind, ShopMenu, TowerRun, CampaignRun, PlayerNetworkCombat, PlayerGrenades, ClassKit, OutfitDresser, OutfitStudio, MainMenuWardrobe, PerkCardScreen, ResultsShow, DeathScreen, KillFlairFx, FieldPickups와 편집기의 CampaignMaps(장별), GreyboxBuilder, TowerMap, TacticalGunSetup, GunClipRetarget.
- **프리팹에 옛 조각을 남기지 않는다.** 코드가 새로 그리게 된 화면 조각은 프리팹에서도 지운다. 숨기기만 하면 그 조각이 붙잡은 그림과 스크립트가 계속 남는다.

0.9.4에서는 논리 오류를 찾으면서, 같은 실수가 여러 곳에서 나던 자리를 "한 곳이 맡는다"로 바꿨다.

- **여럿이 건드리는 값은 주인이 하나다.** 게임 속도는 [WorldTime](../Assets/_Project/Scripts/Core/Time/WorldTime.cs), 일시정지는 [GamePause](../Assets/_Project/Scripts/Core/Time/GamePause.cs), 입력 맵과 커서는 [InputHold](../Assets/_Project/Scripts/UI/Shared/InputHold.cs)만 쓴다. 쓰는 쪽은 자기 이름으로 잡고(`Take`/`Hold`) 놓는다(`Release`). 예전에는 화면마다 "열 때 값을 기억했다가 닫을 때 되돌리기"를 따로 해서, 두 화면이 겹치면 서로의 값을 덮었다(카드 고르는 중에 Esc 메뉴를 열었다 닫으면 게임이 풀림, 상점 위에서 채팅을 닫으면 이동이 풀림). 잡은 쪽이 씬과 함께 사라지면 저절로 놓인다.
- **글자를 값으로 바꾸는 곳은 하나다.** 표·모드·콘솔의 숫자는 `TableCells.TryNumber`·`TryWhole`, 켬/끔은 `TryFlag`로만 읽는다. 표의 칸 하나는 `GameTables.Number(표, 줄, 칸, 기본값)`이나 `row.Number(칸, 기본값)`. 숫자를 쓰고 읽는 방식은 PC의 지역 설정과 무관하다([GameCulture](../Assets/_Project/Scripts/Core/Common/GameCulture.cs)).
- **표는 잘못 쓴 것을 말해 준다.** 쉼표가 든 칸을 따옴표 없이 쓰면 그 칸은 쉼표에서 잘린다. 표를 읽을 때 그런 줄, 두 번 나온 줄·열을 콘솔에 알린다([CsvTable](../Assets/_Project/Scripts/Core/Tables/CsvTable.cs)의 `Problems`). 열 이름은 대소문자를 가리지 않는다.
- **플레이어 파일은 통째로 쓴다.** 설정·성장·기록·킬 수·총 설정·저장한 옷차림은 [SaveFile](../Assets/_Project/Scripts/Core/Common/SaveFile.cs)로 쓰고 읽는다. 쓰는 도중 꺼져도 앞 파일이 남고, 깨진 파일은 바로 앞 사본(`.bak`)으로 되돌린다.
- **Unity 오브젝트에는 `?.`·`??`를 그냥 쓰지 않는다.** 파괴된 오브젝트는 `== null`이지만 `?.`에는 살아 있는 것으로 보인다. `thing.OrNull()?.…`로 쓴다.
- **검사기가 지킨다.** `Check-Code.ps1 audit`가 위 약속(게임 속도를 다른 곳에서 씀, Unity 오브젝트의 `?.`, 표에 없는 줄·칸·글자 id, 잘린 표 줄 …)과 흔한 실수를 찾는다. 0이 아닌 줄은 `Logs/CodeTools/audit.txt`에서 본다.

## 0.9.0에서 바뀐 자리

예전 문서나 기억에 남은 경로는 아래 표로 바꿔 읽는다. 파일 이름과 클래스 이름은 그대로다.

### 스크립트

| 예전 | 지금 |
| --- | --- |
| `Scripts/Data` | `Scripts/Core/Tables`, `Scripts/Core/Texts` |
| `Scripts/Settings`, `Scripts/Loading` | `Scripts/Core/Settings`, `Scripts/Core/Loading` |
| `Scripts/Campaign`, `Scripts/Tower`, `Scripts/Hub`, `Scripts/Combat/Waves` | `Scripts/Modes/Campaign`, `Modes/Tower`, `Modes/Hub`(+`Range`·`Results`), `Modes/Waves` |
| `Scripts/Combat/Feel` | `Combat/Feedback`, `Combat/Corpses`, `Combat/Impacts`, `Combat/Moments`, `Combat/Marks` (HUD 조각은 `UI/Hud`, 클래스 효과는 `Classes`) |
| `Scripts/Combat/Weapons`, `Scripts/Player/Combat/Weapons`, `Scripts/Player/FPSBody` | `Scripts/Weapons`(+`Core`·`Effects`·`FPSBody`) |
| `Scripts/Player/Combat` | `Scripts/Melee`(+`Shove`·`Guard`·`Executions`·`Knives`), `Player/Life`, `Player/Comms`, `Player/Stealth` |
| `Scripts/Player/Grenades`, `Scripts/Player/Classes` | `Scripts/Grenades`, `Scripts/Classes` |
| `Scripts/Player/Body` (75개) | `Player/Body` + `Hands`, `IK`, `Feet`, `Cloth`, `Measure` |
| `Scripts/Enemies` (45개) | `Enemies` + `Body`, `Elites`, `Gore` |
| `Scripts/Network` (30개) | `Network` + `Rooms`, `Sync` |
| `Scripts/UI` 바로 아래, `UI/HP`, `UI/Wave Banner` | `UI/Hud/Health`, `Weapons`, `Damage`, `KillFeed`, `Mission`, `Prompts`, `Team`, `Classes`, `UI/Shared` |
| `Scripts/UI/Chatting` | `Scripts/UI/Chat` (콘솔 명령은 `Scripts/Dev/Console`) |
| `Scripts/Debug` | `Scripts/Dev` |
| `Editor/Data`, `Import`, `Utilities`, `World` | `Editor/Tables`, `Texts`, `Fonts`, `Guns`, `Characters`(+`Booth`), `Models`, `Audio`, `Hud`, `Maps`, `Build` |

### 표, 문서, 메뉴

| 예전 | 지금 |
| --- | --- |
| `Resources/Tables/<이름>.csv` | `Resources/Tables/<분야>/<이름>.csv` (0.9.1부터 `Data/Resources/Tables` 아래. Core, Audio, Player, Characters, Weapons, Melee, Grenades, Classes, Enemies, Maps, Modes, Economy, Hud, Clips) |
| `Assets/_Project/Docs/*.md` | `Docs/Guides`, `Docs/Features`, `Docs/Records` |
| `Assets/_Project/Scripts/README.md`의 긴 표 | 폴더마다의 `README.md` |
| Tools → World | ENFILADE → 맵 (캠페인 회색 맵 → 캠페인) |
| Tools → Characters | ENFILADE → 캐릭터 |
| Tools → ENFILADE → Combat, Tools → Guns | ENFILADE → 총 |
| Tools → Data Tables | ENFILADE → 표 |
| Tools → 글자, Fonts, HUD, Models, Zombies | ENFILADE → 글자, 글꼴, HUD, 모델, 좀비 |

### 0.9.1: 에셋의 자리

| 예전 | 지금 |
| --- | --- |
| `Assets/_Project/Resources/Tables` | `Assets/_Project/Data/Resources/Tables` |
| `Resources`의 그림·소리·셰이더·재질·포즈 (`UI`, `Audio/Ambience`·`Music`, `Combat/Shaders`·`BloodSplats`·`GunSkins`, `Poses`, `World`) | `Art/Resources` 아래 같은 경로 |
| `Resources`의 목록 에셋·소리 묶음·JSON (`Audio/*.asset`, `Characters`, `Cloth`, `Combat/*.asset`·`GunMoves`, `Enemies`, `Settings`, `UI/FontFaces`) | `Data/Resources` 아래 같은 경로 |
| `Resources`의 프리팹 (`Characters/Player`, `ClassProps`, `Enemies/Enemy_Test`, `Combat/MuzzleFlame`) | `Prefabs/Resources` 아래 같은 경로 |
| `Data/Combat` | `Data/Guns` |
| `Art/Audio/Library` (아직 안 쓰는 보관 소리 895개) | `ArtSource/Library/Audio` (Unity 밖) |

게임이 부르는 이름(`Resources.Load`의 경로)은 하나도 바뀌지 않았다. GameHUD 프리팹에서 코드가 숨기기만 하던 옛 조각(웨이브 배너, 궁극기 불꽃·옛 막대, 옛 탄약 표시, 옛 무기 칸 47개 오브젝트)과 그 조각만 쓰던 그림·재질·셰이더·스크립트를 지워서 `Art/Sprites`는 66개에서 13개가 됐다.

### 지운 것

데모·예제(Cartoon FX, DAVFX, Vefects, MagicaCloth2의 예제 폴더), 쓰지 않던 `Assets/RVFX` 파괴 시스템, 저폴리 무기 팩(탄약 상자 하나만 남김), Advanced Zombie AI Pro의 스크립트(쓰는 클립 7개만 남김), 안 쓰던 스크립트 4개와 씬 1개, 안 쓰던 패키지 5개(Visual Scripting, Timeline, Multiplayer Center, Version Control, Soft Mask). 되돌리려면 git 꼬리표 `pre-0.9.0`에서 꺼낸다.
