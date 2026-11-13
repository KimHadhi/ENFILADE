# 모드 만들기

ENFILADE는 뜯어고치기 쉽게 만들었다. 모드에는 두 가지가 있다.

| | 표 모드 | 프로젝트 모드 |
| --- | --- | --- |
| 필요한 것 | 메모장이나 엑셀 | Unity와 이 프로젝트 |
| 바꿀 수 있는 것 | 게임의 모든 숫자와 글자: 무기·좀비·웨이브·상점·클래스·카드·HUD 배치와 색·화면 문구·번역 | 그 밖의 전부: 새 모델, 새 총 모양, 새 캐릭터와 옷, 새 맵, 새 기능 |
| 넣는 곳 | 게임 실행 파일 옆 `Mods/<내 모드>/` | 프로젝트 안 |
| 다시 빌드 | 필요 없음 | 필요 |

## 1. 표 모드

### 폴더

게임을 켜면 실행 파일 옆의 `Mods` 폴더를 읽는다. Unity 편집기에서는 프로젝트 맨 위의 [Mods](../Mods/README.md) 폴더다.

```
Mods/
  내모드/                  폴더 하나가 모드 하나
    mod.txt               (선택) 설명 한 줄
    Tables/
      Weapons.csv         게임의 표와 같은 이름. 하위 폴더는 마음대로
      Texts.csv
  다른모드/
  _꺼둔모드/               이름이 _ 나 . 으로 시작하면 읽지 않는다
```

- 폴더 이름 순서로 읽는다. 같은 줄의 같은 칸을 두 모드가 고치면 **뒤에 읽은 모드**가 이긴다. 순서를 정하고 싶으면 이름 앞에 `10_`, `20_`처럼 숫자를 붙인다.
- 모드를 끄려면 폴더 이름 앞에 `_`를 붙인다.

### 표를 고치는 법

1. 게임에서 `` ` `` 키로 콘솔을 열고 `/mods export`. 게임의 원래 표가 `Mods/_게임 원본/Tables`에 나온다(이 폴더는 모드로 읽지 않는다).
2. 고칠 표를 내 모드의 `Tables`에 복사한다. **고칠 줄과 고칠 칸만 남겨도 된다.**
3. 저장은 **UTF-8**로 한다. 엑셀이면 "CSV UTF-8(쉼표로 분리)".
4. 게임을 다시 켜거나 콘솔에서 `/mods reload`. `/mods`는 지금 읽힌 모드와 표 수를 보여 준다.

표는 두 가지 모양이다.

```
줄 표: 첫 칸이 줄 이름(id), 첫 줄이 칸 이름       키 표: key,value 두 칸, key는 "줄.칸"
id,magazineSize,damage                          key,value
M1911,15,                                       Move.runSpeed,7.5
```

덧씌우는 규칙:

- 게임에 **있는 id**의 줄: 내가 **채운 칸만** 바뀐다. 비운 칸은 게임 값 그대로다.
- 게임에 **없는 id**: 새 줄로 더해진다. (새 상점 물건, 새 좀비 종류, 새 글자, 새 능력 카드…)
- `#`으로 시작하는 줄과 칸 이름은 설명이라 읽지 않는다.
- 줄을 지우는 문법은 없다. 나오지 않게 하려면 그 줄의 비율·수량 칸을 0으로 둔다.
- 화면 글자는 `Texts.csv`다. `ko` 칸을 고치면 문구가 바뀌고 `en` 칸을 채우면 번역이 된다.
- **쉼표가 든 칸은 큰따옴표로 감싼다**: `id,"첫째, 둘째",` (엑셀은 알아서 감싼다). 감싸지 않으면 칸이 쉼표에서 잘리고, 게임이 콘솔에 그 줄을 알려 준다.
- 칸 이름과 줄 이름은 대소문자를 가리지 않는다(`Damage`와 `damage`는 같은 칸).
- 숫자는 소수점으로 쓴다(`7.5`). 끝의 `%`는 무시한다. 켬/끔은 `1`/`0`, `true`/`false`, `on`/`off` 어느 것이든 된다.
- 표를 읽다가 이상한 것(없는 칸 이름, 숫자가 아닌 값, 두 번 나온 줄, UTF-8이 아닌 파일)은 콘솔에 `[Tables]`·`[Mods]`로 나온다. 모드를 만들 때는 콘솔을 한 번 본다.

예시는 [Mods/_Example](../Mods/_Example)에 있다(M1911 탄창 15발, 메인 메뉴 소식 한 줄). 폴더 이름의 `_`를 지우면 켜진다.

### 표 목록

표는 프로젝트의 `Assets/_Project/Data/Resources/Tables` 아래 분야별 폴더에 있다. 칸마다의 뜻은 각 표 둘째 줄의 `#설명`에 적혀 있다.

| 폴더 | 표 | 무엇 |
| --- | --- | --- |
| `Core` | Texts, Fonts, SettingsDefaults, Loading, Network, Chat, MenuTicker | 화면 글자와 번역, 글꼴 역할, 설정 기본값, 로딩, 방 설정, 채팅 모양, 메뉴 소식 |
| `Player` | Player, Downed, Spectate, ThirdPerson, LowHealth, DeathScreen, Callouts, Poke, BodyShapes, DownedAura, HoldReveal | 이동·자세·체력·주먹·몸 수치, 쓰러짐과 부활, 관전, 3인칭, 콜아웃, 가리키기 |
| `Weapons` | Weapons, Magazines, Scopes, Tracer, GunSkins, GunGrips, GunHands, Holsters, CloseHold, FPSBody, ImpactEffects, ShootOverCover, CrosshairPresets, WeaponIcons, WeaponIconSpans, WeaponSlots, KillCounter | 총의 피해·연사·탄창·반동, 조준경, 예광탄, 스킨, 쥐는 손, 탄착 효과, 조준점, 아이콘 |
| `Melee` | Combos, Executions, ClassKnives | 주먹 콤보, 처형·암살, 클래스 칼 |
| `Grenades` | Grenades, GrenadeRules, ThrowArms | 투척물의 수치와 규칙 |
| `Classes` | Classes, ClassAbilities, ClassColours, ClassGrowth, ClassRanks, Perks, UltMagazine, RepairArms | 클래스, 능력, 색, 성장과 등급, 능력 카드 |
| `Enemies` | Enemies, ZombieKinds, ZombieSenses, Zombie…(특수 좀비·무리·목소리·시체), Gore, Ragdoll, Stealth | 좀비의 체력·속도·피해, 종류와 비율, 감각, 특수 좀비, 고어, 잠입 |
| `Modes` | Campaign, CampaignChapters, CampaignSteps, Waves, Hub, RangeTargets, Results, Tower, TowerFloors | 캠페인의 장과 단계, 웨이브, 허브, 타워 |
| `Maps` | Maps, MapArt, MapDecals, MapLook, MapGrade, MapSurfaces, MapWindows, MapSkyline, MapLods, MapReflections, Culling, Surfaces | 맵 목록, 소품과 데칼, 하늘·조명·색감, 표면 종류 (소품·데칼·조명은 맵을 다시 지어야 바뀐다) |
| `Economy` | Money, Shop, Pickups, PickupRules | 돈, 상점 물건과 값, 줍기 |
| `Audio` | Sounds, SoundCues, Music, Ambience, Reverb | 소리 크기와 높낮이, 동작별 소리, 음악, 환경음 |
| `Hud` | Hud, HudTilt, HudOverlap, HealthBar, HitMarkers, KillFeed, KillFlairs, SpecialKills, MomentFx, PackFx, SkullSlowMotion | HUD 배치와 기울기, 체력바, 히트 마커, 처치 목록, 특수 순간 효과 |
| `Characters` | Outfits, OutfitSlots, OutfitFits, CharacterParts, Cloth…, Face…, 옷장 사진 | 옷 칸과 옷, 옷 맞춤, 천 흔들림, 표정 |
| `Clips` | KnifeMelee…, Grenade…, Knife…, Repair…, PointHand | 블렌더 작업실이 쓰는 손·팔 동작. 손으로 고치지 않는다 |

### 알아 둘 것

- 같이 하는 사람은 모두 같은 모드를 넣는다. 숫자 대부분은 방장이 정하지만 화면은 각자 자기 표로 그린다.
- 표에 적힌 이름이 틀리면(없는 칸, 숫자 자리에 글자) 게임은 그 칸만 건너뛰고 콘솔에 `[Tables]` 경고를 남긴다.
- 같은 이름의 표가 한 모드 안에 두 번 있으면 먼저 찾은 것만 쓰고 `[Mods]` 경고를 남긴다.

## 2. 프로젝트 모드

Unity로 프로젝트를 열면 게임의 전부를 고칠 수 있다. 구조는 [구조 안내](Architecture.md), 코드 지도는 [스크립트 길잡이](../Assets/_Project/Scripts/README.md)에 있다. 자주 하는 일의 순서는 다음과 같다.

| 하고 싶은 것 | 하는 법 |
| --- | --- |
| 새 총 | [전투 제작 안내](Guides/CombatFoundationGuide.md)의 "새 총 데이터 연결하기". 숫자는 `Weapons.csv`의 새 줄(id = 무기 에셋 이름), 상점에는 `Shop.csv` 줄. 아이콘은 `Documents\ENFILADE\WeaponIcons`에 무기 id 이름의 PNG를 넣어도 된다 |
| 새 좀비 종류 | `ZombieKinds.csv`에 줄을 더한다(기본 좀비 위에 덮어쓰는 값과 나오는 비율). 모습은 `Resources/Enemies/ZombieLooks` |
| 새 캐릭터, 새 옷 | [플레이어 몸](Guides/PlayerBodyGuide.md)의 "BOOTH 캐릭터·옷 가져오기". 어떤 휴머노이드든 실행 중에 몸에 맞춘다(캐릭터마다 따로 굽지 않는다) |
| 새 맵 소품, 데칼 | `MapArt.csv`·`MapDecals.csv`에 줄을 더하고 ENFILADE → 맵에서 다시 짓는다. 팩에서 들이는 법은 [소품 가져오기](../Tools/Props/README.md) |
| 새 소리 | 맞는 `Art/Audio` 폴더에 파일을 넣으면 소리 묶음에 들어간다([유지보수 안내](Guides/MaintenanceGuide.md#사운드를-추가하거나-교체하기)) |
| 애니메이션 손보기 | `ArtSource`의 블렌더 작업실을 열고 고친 뒤 저장(Ctrl+S)하면 게임에 들어간다([원클릭 작업실](../Tools/Blender/OneClick/README.md)) |
| 화면 글자 | ENFILADE → 글자 → 글자 편집기 |
| 새 기능의 코드 | 그 기능의 폴더에 두고, 숫자는 표에, 글자는 `Texts.csv`에 ([구조 안내](Architecture.md#새로-더할-때)) |

만든 것을 표 모드로 나눠 주고 싶으면, 고친 표의 줄만 뽑아 `Mods/<이름>/Tables`에 넣으면 된다. 빌드할 때 프로젝트의 `Mods` 폴더는 실행 파일 옆에 그대로 복사된다.

## 3. 모드가 읽히는 자리 (코드)

| 무엇 | 파일 |
| --- | --- |
| Mods 폴더 읽기, 콘솔 `/mods` | [GameMods](../Assets/_Project/Scripts/Core/Mods/GameMods.cs) |
| 표를 이름으로 찾고 모드를 덧씌우기 | [GameTables](../Assets/_Project/Scripts/Core/Tables/GameTables.cs), [CsvTable](../Assets/_Project/Scripts/Core/Tables/CsvTable.cs) (`Overlay`) |
| 줄에서 칸 읽기 | [TableCells](../Assets/_Project/Scripts/Core/Tables/TableCells.cs) |
| 빌드 옆에 Mods 폴더 복사 | [ModsFolderOnBuild](../Assets/_Project/Editor/Build/ModsFolderOnBuild.cs) |
