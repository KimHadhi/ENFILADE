# 모드 만들기

ENFILADE는 뜯어고치기 쉽게 만들었다. 모드에는 세 가지가 있다.

| | 표 모드 | 아바타 모드 | 프로젝트 모드 |
| --- | --- | --- | --- |
| 필요한 것 | 메모장이나 엑셀 | 내 Unity 프로젝트(VRChat 아바타를 올리던 그 프로젝트)와 게임에 든 SDK 폴더 | Unity와 이 프로젝트 |
| 바꿀 수 있는 것 | 게임의 모든 숫자와 글자: 무기·좀비·웨이브·상점·클래스·카드·HUD 배치와 색·화면 문구·번역 | 내 캐릭터: 어떤 휴머노이드 아바타든 캐릭터 목록에 더한다. 내 옷: 캐릭터의 옷장에 칸마다 골라 입는 옷을 더한다 | 그 밖의 전부: 새 총 모양, 새 맵, 새 기능 |
| 넣는 곳 | 게임 실행 파일 옆 `Mods/<내 모드>/` | 같은 곳 (`Mods/<내 모드>/Avatars`) | 프로젝트 안 |
| 다시 빌드 | 필요 없음 | 필요 없음 | 필요 |

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
| `Player` | Player, Downed, Spectate, ThirdPerson, LowHealth, DeathScreen, Callouts, CalloutSlots, Poke, BodyShapes, DownedAura, HoldReveal | 이동·자세·체력·주먹·몸 수치, 쓰러짐과 부활, 관전, 3인칭, 콜아웃, 가리키기 |
| `Weapons` | Weapons(총의 수치. `ammo` 칸은 쓰는 탄의 종류: 같은 이름을 적은 총끼리 예비 탄약을 함께 쓴다. 게임의 것은 상점의 총 분류를 따르는 pistol·smg·shotgun·rifle·marksman·mg이고, 새 이름을 적으면 새 탄이 된다. 비우면 그 총만의 탄이라 어떤 총과도 나누지 않는다(탄을 나누면 안 되는 특수 무기는 비워 둔다). 화면 이름은 Texts의 `ammo.kind.<이름>`. `builtInSuppressor` 칸에 true를 적으면 소음기가 몸체에 붙은 총이다: 아무것도 달지 않아도 사격이 늘 소음기 사격이라 좀비가 `suppressedNoise` 몫의 거리에서만 듣고, 제 발사음을 그대로 내며, 화염은 `flash` 줄의 `suppressedAs` 줄이다), Magazines, GunMounts(총마다의 소켓: 부착물이 앉는 자리), GunParts(부품마다의 종류·발 길이·조준 화면. 아래 "총의 부착물 자리와 조준 화면"), Scopes, Tracer, MuzzleFlash(총구 화염의 모습: 줄을 더하고 Weapons의 `flash` 칸에 그 이름을 적는다), ReloadRules(총이 장전된 뒤 남은 장전 동작을 사격·조준·달리기·밀치기가 끊는지), GunSkins, GunGrips, GunHands, Holsters, CloseHold, FPSBody, ImpactEffects, ShootOverCover, CrosshairPresets, WeaponIcons, WeaponIconSpans, WeaponSlots, KillCounter | 총의 피해·연사·탄창·반동, 조준경, 예광탄, 스킨, 쥐는 손, 탄착 효과, 조준점, 아이콘 |
| `Melee` | Combos, Executions, ExecutionRules, ClassKnives | 주먹 콤보, 처형·암살의 짝(동작)과 규칙(누구를 어디서 어떤 짝으로), 클래스 칼 |
| `Grenades` | Grenades, GrenadeRules, ThrowArms | 투척물의 수치와 규칙 |
| `Classes` | Classes, ClassAbilities, ClassColours, ClassGrowth, ClassRanks, Perks, UltMagazine, RepairArms | 클래스, 능력, 색, 성장과 등급, 능력 카드 |
| `Enemies` | Enemies, ZombieKinds, ZombieSenses, Zombie…(특수 좀비·무리·목소리·시체), Gore, Ragdoll, Stealth | 좀비의 체력·속도·피해, 종류와 비율, 감각, 특수 좀비, 고어, 잠입 |
| `Modes` | Campaign, CampaignChapters, CampaignSteps, Waves, Hub, RangeTargets, Results, Tower, TowerFloors | 캠페인의 장과 단계, 웨이브, 허브, 타워 |
| `Maps` | Maps, MapArt, MapDecals, MapLook, MapGrade, MapSurfaces, MapWindows, MapSkyline, MapLods, MapReflections, Culling, Surfaces | 맵 목록, 소품과 데칼, 하늘·조명·색감, 표면 종류 (소품·데칼·조명은 맵을 다시 지어야 바뀐다) |
| `Economy` | Money, Shop, Pickups, PickupRules | 돈, 상점 물건과 값, 줍기 |
| `Audio` | Sounds, SoundCues, Music, Ambience, Reverb | 소리 크기와 높낮이, 동작별 소리, 음악, 환경음 |
| `Hud` | Hud, HudTilt, HudOverlap, HealthBar, HitMarkers, KillFeed, KillFlairs, SpecialKills, MomentFx, PackFx, SkullSlowMotion | HUD 배치와 기울기, 체력바, 히트 마커, 처치 목록, 특수 순간 효과 |
| `Characters` | Outfits, OutfitSlots, OutfitFits, OutfitFit, CharacterParts, Cloth…, Face…, ShaderMap, 옷장 사진 | 옷 칸과 옷, 옷 맞춤, 천 흔들림, 표정 |
| `Clips` | KnifeMelee…, Grenade…, Knife…, Repair…, PointHand | 블렌더 작업실이 쓰는 손·팔 동작. 손으로 고치지 않는다. Grenade…·Knife(Draw/Hold/Throw)·Repair…·KnifeMelee…·PointHand와 몸 클립 옆의 `<클립>_fingers`는 손가락 마디마다 방향·비틀림(`index1X`…`thumb3Twist`)을 지녀 어느 캐릭터 손에도 블렌더 모양 그대로 나온다. 그 열이 없는 표는 손가락마다 굽은 정도(`curl`·`thumb`)로 쥔다 |

### 알아 둘 것

- **방 안에서는 방장의 표가 기준이다**(0.9.272, [RoomTables](../Assets/_Project/Scripts/Network/RoomTables.cs)): 게스트는 방에 들어가는 동안 자기 모드의 `Tables/*.csv`를 빼고, 방장이 보낸 방장 모드의 표를 쓴다. 나오면 자기 것이 돌아온다. 아바타·옷 파일이 스스로 깔아 두는 줄(흔들림, 얼굴, 부위)은 각자 PC의 것을 그대로 쓴다.
- 표를 바꾸는 모드가 있는 방은 서버 찾기 창의 방 카드에 "모드 서버: 이름"이 분홍으로 뜨고, 들어가기 전에 예/아니오를 묻는다. 주소를 쳐서 들어가면 방장이 한 번 돌려보내며 모드 이름을 알리고 같은 물음이 뜬다.
- 아바타 모드(`Avatars/`)는 설정 > 조작 > **친구와 아바타 모드 주고받기**가 켜져 있으면 방 안에서 방장을 거쳐 나눠 받는다(`Mods/<모드>~방/`, [ModShare](../Assets/_Project/Scripts/Network/ModShare.cs)).
- 표에 적힌 이름이 틀리면(없는 칸, 숫자 자리에 글자) 게임은 그 칸만 건너뛰고 콘솔에 `[Tables]` 경고를 남긴다.
- 같은 이름의 표가 한 모드 안에 두 번 있으면 먼저 찾은 것만 쓰고 `[Mods]` 경고를 남긴다.

### 콜아웃 줄·칸 더하기 (2026-11-18)

콜아웃은 표 둘이다. `Callouts`는 말할 줄이다(한 줄 = 한 마디: 어느 칸의 줄인지 `slot`, 언제 맞는지 `when`, 여럿이 맞을 때 누가 이기는지 `order`). `CalloutSlots`는 휠의 칸이다(어느 쪽 `page`의 몇 번째 자리 `place`인지, 언제 그 자리에 있는지 `when`, 고르면 여는 쪽 `opens`).

- `when`은 사실을 이어 쓴다: `a & b`(둘 다), `a | b`(어느 하나), `!a`(아닐 때). 비우면 늘. 예: `class:knight & taunt.ready`, `friend.down & !grabbed`, `phase:assault | horde`, `step:generator & class:support`. 쓸 수 있는 사실은 Callouts.csv 둘째 줄(#설명)에 모두 적혀 있다(코드: `CalloutFacts`).
- 한 클래스나 한 상황만의 칸: CalloutSlots에 같은 `place`로 `when`을 쓰고 `order`가 더 큰 줄을 더한다. 조건이 맞는 동안 그 줄이 그 자리를 차지한다.
- 쪽을 더하기: 어느 칸의 `opens`에 새 쪽 이름을 쓰고, `page`가 그 이름인 칸들을 더한다(자리 0~7).
- 줄의 글자는 Texts id나 글자 그대로, 목소리는 `Art/Audio/Voice/<캐릭터>/Callout/<줄 id>/`의 소리다. 소리가 없으면 입 모양만 움직인다.
- 감정 표현(0.9.300): 줄의 `gesture`에 `emote:<클립 이름>`(PlayerBody 프로필의 클립)을 쓰면 말 대신 몸 전체가 그 클립을 하고 얼굴은 `face`를 그만큼 짓는다. 글자·목소리·통신 화면은 없다. 게임의 것은 `emote` 쪽(휠의 감정 칸)의 일곱 줄이다.
- 표를 고친 뒤 콘솔 `/callouts`를 치면 `Logs/callouts.txt`에 없는 사실 이름, 말할 줄이 없는 칸, 칸이 없는 줄, 그리고 지금 상황에서 칸마다 나올 줄이 적힌다. 모르는 사실이 든 줄은 맞지 않는 것으로 친다.

### 총의 부착물 자리와 조준 화면 (2026-11-18)

부착물이 총의 어디에 앉는지, 조준하면 화면이 어떻게 보이는지는 표 둘이 정한다. 총마다 따로 짠 코드가 없어서 표 모드만으로 고칠 수 있고, 총을 다시 만들 필요도 없다(총이 만들어질 때마다 표대로 앉힌다. 실행 중에 `/mods reload`로 읽었으면 다음 판에 새로 만들어지는 총부터 보인다).

- `GunMounts`: 총마다의 **소켓**. 줄 이름은 `<총 id>.<소켓>`이고 소켓은 `eye`(그 총을 자기 조준기로 조준할 때 눈이 있는 자리), `sight`(조준경 레일), `muzzle`(총구), `grip`(손잡이 레일), `under`(하단 레일)다. `x y z`는 소켓의 시작점(레일이면 뒤끝), `rx ry rz`는 방향(도), `span`은 레일의 길이다. 좌표는 총 프리팹 기준 미터: x 오른쪽, y 위, z 총구 쪽.
- `GunParts`: **부품**마다의 줄. 줄 이름은 부품 프리팹의 이름이고 `own`은 총 자체의 조준기다. 종류, 메뉴에 보이는 이름, 레일에 닿는 발의 길이(`footBack`·`footFront`), 조준기면 배율(`magnification`)과 조준 화면을 그리는 방식(`view`).

부품은 규칙 하나로 앉는다(코드: [GunMounts](../Assets/_Project/Scripts/Weapons/FPSBody/GunMounts.cs)). 조준경은 자기 눈 자리가 총의 눈 자리와 나란해지는 곳까지 레일을 따라 물러나되 발이 레일을 벗어나지 않는다. 그래서 어느 조준경을 달아도 총을 쥔 자리가 같고, 레일이 눈에서 먼 총(권총의 슬라이드)에서는 조준경이 레일 뒤끝에 앉고 조준할 때 총이 그만큼 눈 쪽으로 온다. 라이트는 하단 레일의 앞끝에, 손잡이는 쥔 손이 정한 곳에(레일 안에서), 총구 부품은 총구에 앉는다.

| 하고 싶은 것 | 고칠 칸 |
| --- | --- |
| 조준경이 떠 있거나 묻혀 있다 | `GunMounts`의 `<총>.sight` 줄 `y`(위아래), `z`와 `span`(레일의 뒤끝과 길이) |
| 라이트·손잡이의 자리 | `<총>.under`, `<총>.grip` 줄. 핸드가드 밑면이 기울었으면 `rx` |
| 이 총에 달 수 있는 부품, 처음 달린 부품 | 그 소켓 줄의 `fits`(부품 id를 `\|`로 나열)와 `starts`. 총 프리팹에 들어 있는 부품 가운데서만 고를 수 있다. 줄이 하나라도 있는 총은 줄이 있는 소켓만 가진다(줄이 없는 소켓에는 프리팹에 부품이 들어 있어도 달 수 없다). 줄이 하나도 없는 총은 프리팹에 든 부품을 프리팹의 자리 그대로 단다 |
| 광학 조준경을 달아도 총의 쇠 가늠자가 앞을 가린다 | `<총>.sight` 줄의 `folds`에 가늠자·가늠쇠를 감싸는 상자(`x0 y0 z0 x1 y1 z1`, 여럿이면 `\|`)를 적는다. 상자 안에 통째로 든 메시 조각이 광학 조준경 밑에서 접힌다(그리지 않는다) |
| 배율 | `GunParts`의 `magnification` |
| 배율 조준경의 화면 | `GunParts`의 `view`. `zoom`(기본) = 화면 전체가 배율만큼 확대되고 렌즈 안도 같은 배율이라 한 화면으로 보인다. `pip` = 화면은 그대로 두고 렌즈 안에만 확대한 그림을 그린다. `window`는 zoom에서 렌즈 안 그림의 지름(화면 높이의 몇 배) |
| 확대했을 때 마우스 빠르기 | 플레이어가 설정 → 조작에서 배율마다 정한다. 그 줄은 `GunParts`에 있는 배율마다 하나씩 저절로 생긴다(새 배율의 조준경을 넣으면 그 줄도 생긴다). 건드리지 않은 배율의 기본값은 `Scopes`의 `Look.sensitivity`(1 = 조준하지 않았을 때와 같은 화면 속 빠르기) |

프로젝트가 있으면 새 총의 줄을 손으로 재지 않아도 된다. `Tools/Guns/gun_mounts.py`가 총의 메시에서 레일과 쇠 가늠자를 재서 줄을 쓰고, Unity를 켜지 않고 그림과 보고서(`Logs/gun-mounts.txt`, `Logs/GunMounts`)로 점검한다([사용법](../Tools/Guns/README.md)). 총에 없던 부품을 다는 것은 프로젝트 모드의 일이다: Tactical Shooter Pack에 든 부품이면 `fits`에 그 프리팹 이름을 적고 ENFILADE → 총 → Tactical Shooter Pack 총 만들기를 누르면 그 부품이 총 프리팹에 들어간다(FPS Animation Pack 총). 팩에 없는 새 조준경 **모델**을 표만으로 다는 길은 아직 없다.

## 2. 아바타 모드 (0.9.49)

사용자(11-14): "VRChat 유저들이 자기가 쓰던 캐릭터를 내 게임에서도 쓰고 싶어 할 텐데 이거 호환 그대로 잘 되게끔 … 최대한 자유를 줘 플레이어들에게. 자기가 원하는 쉐이더 쓰고 싶다면 쓰게 해 주고. 최대한 원하는 느낌이 그대로 뜨게. 유저를 제약하지 마." 캐릭터를 게임에 넣으려면 프로젝트를 열어 다시 빌드해야 하던 것을, 모드 폴더에 넣기만 하면 되게 했다.

### 만드는 법 (모드 제작자)

1. 게임 폴더의 `Mods/_SDK/ENFILADE Mod SDK` 폴더를 통째로 내 Unity 프로젝트의 `Assets` 안에 넣는다. VRChat 아바타를 올리던 프로젝트 그대로면 된다(Unity 2022·VRChat SDK·Modular Avatar가 있어도 된다). 이 프로젝트에서는 SDK 폴더 없이 바로 된다(같은 코드가 `Editor/ModSdk`에 있다). 빌드 없이 SDK 폴더만 뽑으려면 ENFILADE → 모드 → 모드 SDK 폴더 만들기.
2. ENFILADE → 모드 → **아바타를 모드로 내보내기**. 아바타(씬의 것이나 프리팹)를 넣고 id와 이름을 정하고 '내보낼 곳'에 게임의 `Mods` 폴더를 고른 뒤 **캐릭터 내보내기**. 창의 '옷 칸' 목록은 아바타가 입고 온 옷이 옷장의 어느 칸에 들어갈지를 보여 준다(아래 "입고 온 옷과 옷 모드").
3. 게임을 켜면(켜져 있으면 콘솔에서 `/mods reload`) 캐릭터 목록에 나온다. `/mods`가 읽힌 모드 캐릭터를 보여 준다.

```
Mods/
  내아바타/
    mod.txt
    Avatars/
      myavatar.bundle           프리팹·메시·텍스처 (Unity만 쌀 수 있는 것)
      myavatar.avatar.json      재질·PhysBone·표정 설정을 제작자가 둔 그대로 적은 글 (사람이 읽고 고칠 수 있다)
    Tables/…                    (선택) 내 캐릭터 id로 적은 게임 표의 줄. 게임이 알아서 정한 것 위에 덮어쓴다
```

원본 아바타는 바뀌지 않는다(내보내기는 사본에 한다). id는 영문 소문자·숫자·`-`·`_`만 쓰고, 한번 정하면 바꾸지 않는다(저장된 설정과 친구 화면이 그 이름으로 찾는다).

### 무엇이 어떻게 가나

| 아바타의 것 | 게임에서 |
| --- | --- |
| 모델·본·셰이프키·텍스처 | 그대로. 휴머노이드 Rig이어야 움직인다(게임의 모든 동작이 휴머노이드 클립이다). 키·1인칭 눈높이·손·발·머리 숨기기는 게임이 어떤 몸이든 재서 맞춘다 |
| PhysBone (머리카락·꼬리·치마·가슴) | 설정값을 그대로 적어 오고, **게임이 불러올 때** 자기 물리(Magica Cloth)의 줄로 바꾼다([VrcSway](../Assets/_Project/Scripts/Player/Body/Cloth/VrcSway.cs): 게임의 BOOTH 캐릭터들과 같은 변환). 그래서 변환이 나아지면 예전에 내보낸 아바타도 같이 나아진다 |
| 아바타 디스크립터의 깜박임·입 모양(비짐) | 그 셰이프키 이름으로 FaceKeys 줄이 생긴다. 나머지 표정(웃음·화남 …)은 게임이 흔한 VRChat·MMD 이름으로 찾는다(FaceKeys.csv의 `*` 줄) |
| Modular Avatar로 입힌 옷, 그 밖의 NDMF 도구 | 업로드할 때처럼 적용한 모습으로 내보낸다(프로젝트에 있을 때. 끌 수 있다) |
| 재질 | 아래 "쉐이더" |
| VRChat·애드온 스크립트, 콜라이더·리지드바디·조인트, 조명, 소리, 카메라, 파티클, EditorOnly 오브젝트 | 뗀다(게임에서 돌지 않거나 게임의 물리·화면을 건드린다). 아래 "입는 것이 지닐 수 있는 것" |

### 입는 것이 지닐 수 있는 것 (2026-11-18)

캐릭터 모델과 그 위에 입는 옷·장신구는 **그려지고 뼈를 따라 움직일 뿐, 게임 세상의 물체가 아니다.** 그래서 모델 파일이 지닐 수 있는 것은 한 목록뿐이다([WornParts](../Assets/_Project/Scripts/Core/Mods/WornParts.cs) `Kept`).

| 남는 것 | 떼는 것 |
| --- | --- |
| 뼈(Transform), 휴머노이드 Animator, 메시와 렌더러(SkinnedMeshRenderer·MeshRenderer·MeshFilter), LOD 그룹, Unity 컨스트레인트 | 그 밖의 전부: 콜라이더·리지드바디·조인트·캐릭터 컨트롤러(게임의 물리에 끼어든다), 스크립트(게임에 없다), 조명·소리·카메라·파티클·트레일(게임의 화면과 소리에 끼어든다) |

- 내보내기 도구가 이 목록으로 떼고, **게임도 입힐 때 같은 목록으로 한 번 더 뗀다**(`OutfitDresser`). 그래서 SDK로 만들지 않은 번들이나 손으로 고친 프리팹에 콜라이더가 남아 있어도 게임 안에서는 없다. 목록은 파일 하나(`WornParts.cs`)이고 SDK 폴더에 같이 들어간다.
- 까닭: 게임의 용접 마스크가 소품 팩의 콜라이더를 단 채 얼굴에 씌워져, 앉으면 제 마스크를 천장으로 알고 엎드려 일어나지 못했다(사용자 11-18). 입은 것이 세상의 일부가 되면 이동·카메라·파쿠르가 모두 속는다.
- 흔들림은 PhysBone 설정으로(위 표), 몸에 닿는 것은 게임이 메시를 재서 만든 천 콜라이더로 한다. 모드가 콜라이더를 가져올 일은 없다.
- 목록을 넓히려면(예: 조명이 달린 장신구) `WornParts.Kept` 한 곳을 고친다. 내보내기와 게임이 같이 바뀐다.

### 착용 위치와 자유 조정 (2026-11-18)

사용자(11-18): "추후 다른 의상 및 창작마당 모드에서도 착용 위치·오프셋을 자유롭게 커스텀할 수 있도록 확장성 있는 구조로 설계." 몸에 통째로 거는 물건(누구에게나 맞게 만든 모델, 캐릭터 id `*`)은 **걸치는 방법**을 고를 수 있고, 플레이어가 그 위에서 자유롭게 옮기고 돌리고 키운다. 방법 하나가 표 [WearPositions.csv](../Assets/_Project/Data/Resources/Tables/Characters/WearPositions.csv)의 한 줄이다(코드 [WearPosition](../Assets/_Project/Scripts/Player/Outfits/WearPosition.cs)).

| 칸 | 뜻 |
| --- | --- |
| `id` | 방법의 id. 게임의 것: `face`(얼굴에), `back`(뒤통수에), `lobes`(귓불에) |
| `name` | 옷장 칩의 이름(Texts id나 그냥 글자) |
| `fit` | `face`: 머리 겉면에 맞춘다(유리나 가운데를 눈높이에, 얼굴 크기에 맞게, 아랫단이 가슴을 비키게: 마스크·고글류). `spot`: 기준점에서 `at`만큼 떨어진 자리에 건다 |
| `yaw` | `face`에서 머리를 어느 쪽에서 보고 맞출지(도). 0 얼굴, 180 뒤통수, 90 오른쪽 옆머리 |
| `anchor` | `spot`의 기준점. `eyes`(두 눈의 가운데), 휴머노이드 뼈 이름(`Head`·`Chest`·`Hips`·`LeftLowerArm` …), 빈칸은 그 칸의 뼈 |
| `at` | 기준점에서 오른쪽\|위\|앞. 단위는 **그 몸의 두 눈 사이 거리**라 어떤 체형에도 비례해서 놓인다 |
| `turn` | 그 자리에서 숙임\|돌림\|기울임(도) |
| `scale` | 크기 배율 |
| `pair` | true면 모델 뿌리 밑 `… R`·`… L` 두 조각을 좌우에 뒤집어 건다(귀걸이) |
| `covers` | false면 이 방법으로 걸쳤을 때 옷의 `hides`가 듣지 않는다(뒤통수의 마스크는 안경을 가리지 않는다) |

- **내 물건에 방법 주기**: 옷 줄의 `positions`에 방법 id를 `|`로 적는다(첫 번째가 기본). `.avatar.json`에서는 `items`의 `"positions": "face|back"`, 표로는 `Outfits.csv`의 `positions` 칸. 적지 않으면 그 칸의 기본(마스크 칸 `face`, 귀걸이 칸 `lobes`, 나머지 칸은 그 칸의 뼈에 원점을 대고 건다)을 쓴다.
- **새 방법 만들기**: 모드 폴더의 `Tables/WearPositions.csv`에 줄을 더한다. 예: 옆머리에 쓴 마스크 `side,옆머리에,face,,90`, 이마 위로 올린 안경 `up,올려 쓰기,spot,eyes,,0|0.9|0.5,-30|0|0`, 머리 위 고리 `halo,머리 위,spot,eyes,,0|2.6|-0.4`. 숫자는 게임 옷장의 손잡이로 맞춰 본 뒤 옮겨 적으면 편하다: 손잡이의 숫자는 모델 공간의 cm이니 그 캐릭터의 두 눈 사이 거리(마요 5.8cm, 이치고 5.4cm, 카즈사 7.3cm)로 나눠 적는다. 옷장 검사대(ENFILADE → 캐릭터 → 옷장 검사대)의 `Logs/WardrobeBench/positions.txt`가 방법마다 물건의 자리를 이 단위로 보여 주고 사진을 남긴다.
- **플레이어의 조정**: 옷장에서 좌우·위아래·앞뒤(±20cm), 크기(0.3~2.5배), 돌리기·숙이기·기울이기(±180°). 범위는 [ItemAdjust.csv](../Assets/_Project/Data/Resources/Tables/Characters/ItemAdjust.csv). 고른 방법과 조정은 옷차림 문자열에 실려(`wear:<옷>=<방법>`, `adjust:<옷>[~<방법>]=…`) 프리셋과 친구 화면에 같이 간다. 친구 PC에 그 방법의 줄이 없으면(모드가 없으면) 그 물건의 기본 방법으로 보인다.
- **캐릭터마다 적는 것은 없다.** 모든 방법은 입는 순간 그 몸의 뼈와 메시를 재서 맞춘다. 손으로 잰 정밀한 자리가 있으면 [OutfitSpots.csv](../Assets/_Project/Data/Resources/Tables/Characters/OutfitSpots.csv)에 `방법 id@캐릭터 id` 줄로 덮어쓸 수 있지만(게임의 세 캐릭터 귓불), 없어도 입는다.
- **두 옷을 한 옷의 두 방법으로 합칠 때**: 남는 옷의 `was`에 `사라지는 옷 id>방법 id`를 적으면, 사라진 옷을 입고 저장한 모습과 그 옷에 해 둔 조정이 남는 옷의 그 방법으로 옮겨 온다(게임의 `welding-mask-back>back`).
- 캐릭터 뼈에 묶인 옷(아바타용 옷)은 방법도 조정도 없다: 몸이 있는 곳에 있다.

### 쉐이더

쉐이더는 컴파일된 코드라, 그 게임의 Unity와 렌더 파이프라인(이 게임은 Unity 6·URP)용으로 빌드된 것만 돌릴 수 있다. VRChat 프로젝트(Unity 2022·내장 파이프라인)에서 컴파일된 쉐이더를 그대로 가져와 돌릴 방법은 없다. 그래서 재질마다 아래 셋 가운데 되는 첫 번째로 만든다([ModMaterials](../Assets/_Project/Scripts/Core/Mods/ModMaterials.cs)).

1. **쉐이더째 담긴 것**: Unity 6 + URP 프로젝트에서 내보낼 때, lilToon이 아닌 재질은 쉐이더까지 번들에 담긴다(내보내기 창의 '게임에 없는 쉐이더는 쉐이더째 담기'). 직접 만든 쉐이더, Shader Graph, 잠근 Poiyomi URP 등 URP에서 그려지는 것이면 무엇이든 그대로 그려진다. **내 쉐이더를 그대로 쓰는 길은 이것이다.**
2. **게임에 같은 이름의 쉐이더가 있는 것**: lilToon. BOOTH 아바타 대부분이 이것이라, VRChat 프로젝트에서 그대로 내보내도 값이 전부 옮겨져 똑같이 그려진다. 빌드에는 lilToon의 모든 변형(컷아웃·반투명·외곽선·퍼·젬·굴절 …)이 들어간다([ModShaderLibrary](../Assets/_Project/Scripts/Core/Mods/ModShaderLibrary.cs)). lilToonMulti는 그 설정이 가리키는 일반 변형으로 간다.
3. **그 밖의 쉐이더**(내장 파이프라인용 Poiyomi, UTS, MToon, Standard …): 같은 그리기 방식(불투명·컷아웃·반투명, 외곽선)의 lilToon에 그림·색·노멀·발광·매트캡처럼 옮길 수 있는 것을 옮겨 비슷하게 그린다. 무엇을 어디로 옮길지는 표 [ShaderMap.csv](../Assets/_Project/Data/Resources/Tables/Characters/ShaderMap.csv)가 정하므로, 자기 쉐이더의 줄을 표 모드로 더할 수 있다. 어떤 쉐이더를 바꿔 그렸는지는 콘솔에 `[Mods]`로 한 번씩 나온다.

`.avatar.json`의 재질 값(숫자·색·텍스처 경로)은 손으로 고쳐도 된다.

얼굴 메시의 반투명 재질(홍조·눈물·땀처럼 표정 키가 꺼내 놓는 효과)은 게임이 머리카락보다 나중에 그리고, 아무것도 그리지 않는 픽셀은 깊이를 쓰지 않게 한다(0.9.84, [FaceEffects](../Assets/_Project/Scripts/Player/Face/FaceEffects.cs)). 이 게임의 렌더러에서는 같은 렌더 큐의 얼굴 효과가 머리카락보다 먼저 그려져서, 표정이 꺼낸 판이 그 뒤의 머리카락을 파내기 때문이다. 순서를 직접 정하고 싶으면 그 재질에 렌더 큐를 따로 주면 된다: 그런 재질은 게임이 건드리지 않는다. 스텐실 검사로 그리는 재질, 그리고 스텐실을 쓰는데 같은 캐릭터의 다른 재질이 불투명과 함께 그 스텐실을 검사하는 경우(머리카락 위로 비치는 눈썹)도 그대로 둔다.

### 입고 온 옷과 옷 모드 (0.9.79)

사용자(11-14, 옷만 따로 내보내는 모드를 두고): "좀 더 유저에게 자유를 주고 유동적이고 개방적인 방향이네. 좋아. 이런 거 좋아요. 바로 만들어 주세요." 0.9.49의 모드 캐릭터는 입고 온 옷까지 한 덩어리라 옷장에서 벗기거나 섞을 수 없었다. 이제 두 가지가 된다.

**캐릭터가 입고 온 옷**은 내보낼 때 옷장 칸으로 나뉜다. 내보내기 창이 메시 이름으로 옷을 알아보고(Jacket → 겉옷, Skirt → 하의, Stocking → 양말·스타킹, 리본처럼 장식 이름뿐인 것은 그것이 놓인 옷에 붙인다, 속옷 이름은 속옷 칸) '옷 칸' 목록에 보여 준다. 이름으로 옷이라고 알아보지 못한 메시는 몸으로 보고 늘 입힌 채 둔다(몸을 옷으로 잘못 보면 벗길 때 몸이 사라지므로, 캐릭터의 메시는 이름으로만 나눈다). 틀렸거나 빠진 것은 목록의 메뉴에서 칸을 고르면 된다. 그렇게 나뉜 옷은 게임에서 칸마다 벗기거나, 그 캐릭터용 옷 모드의 조각으로 갈아입힐 수 있다.

**옷 모드**는 옷만 내보내 옷장의 물건으로 만든다. BOOTH에서 산 "마요용 메이드복"을 내보내면 마요의 옷장에 재킷은 겉옷 칸, 치마는 하의 칸, 구두는 신발 칸에 따로 나와서 게임에 든 옷이나 다른 옷 모드와 섞어 입는다. 겹치는 곳(양말과 신발, 셔츠와 재킷, 속옷과 겉옷)은 게임의 옷처럼 메시를 재서 맞춘다([OutfitFit](../Assets/_Project/Scripts/Player/Outfits/OutfitFit.cs)).

1. ENFILADE → 모드 → **옷을 모드로 내보내기** (같은 창의 '옷' 탭).
2. '옷'에 옷 오브젝트를 넣는다: 아바타 밑에 넣어 둔 옷(Modular Avatar로 입혀 둔 그대로)이나 옷 프리팹. '입힐 몸'에는 그 옷이 맞는 아바타를 넣는다(아바타 밑의 옷이면 알아서 채워진다. 없으면 비워 둔다).
3. '입을 캐릭터 id'에 게임에서 이 옷을 입을 캐릭터의 id를 적는다. 게임에 든 캐릭터의 id와 옷 칸의 id는 콘솔의 `/mods ids`가 보여 준다. 모드 캐릭터에게 입히려면 그 캐릭터를 내보낼 때 정한 id를 적는다. 뼈 없이 통짜로 된, 누구나 쓰는 물건은 `*`.
4. '옷 칸' 목록에서 조각마다 들어갈 칸을 확인하고 틀린 것은 메뉴에서 고친 뒤 **옷 내보내기**.

```
Mods/
  maid-dress/
    mod.txt
    Avatars/
      maid-dress.mayo.bundle          옷 프리팹·메시·텍스처
      maid-dress.mayo.avatar.json     옷 조각이 들어갈 칸(items), 재질, PhysBone, Bone Proxy를 적은 글
      maid-dress.ichigo.bundle        같은 옷의 이치고용. 옷 id를 같게 두고 캐릭터 id만 바꿔 한 번 더 내보낸 것
      maid-dress.ichigo.avatar.json
```

| 옷의 것 | 게임에서 |
| --- | --- |
| 옷의 뼈대(아바타 뼈의 사본과 옷만의 뼈) | 입힐 때 캐릭터의 같은 이름 뼈에 붙인다(Modular Avatar의 Merge Armature와 같은 방식, [OutfitDresser](../Assets/_Project/Scripts/Player/Outfits/OutfitDresser.Placing.cs)). Merge Armature가 뼈 이름에 붙여 둔 접두·접미사는 내보낼 때 뗀다 |
| 조각마다의 칸 | 내보내기 창이 이름, 붙어 있는 옷, 몸의 어디에 걸렸는지(뼈 가중치)로 정하고 제작자가 고친 그대로 파일에 적힌다([ModPieces](../Assets/_Project/Editor/ModSdk/ModPieces.cs): 게임의 BOOTH 가져오기가 쓰는 바로 그 규칙). 한 칸의 조각은 한 벌로 묶이고, 종류가 다르면(스타킹과 양말) 하나씩도 고를 수 있게 나온다 |
| 꺼져 있던 조각(VRChat 토글의 다른 쪽), 옷 파일에 딸려 온 몸의 사본 | 넣지 않는다(번들에도 안 들어간다). 넣고 싶으면 목록에서 칸을 고른다. 속옷은 꺼져 있어도 속옷 칸에 들어간다(게임이 입힐 때 켠다) |
| PhysBone | 캐릭터와 같다: 설정값을 적어 오고 게임이 불러올 때 흔들림 줄로 바꾼다. 몸의 뼈에 선 콜라이더(다리에 부딪히는 치마)는 '입힐 몸'을 넣었을 때 같이 간다 |
| Modular Avatar Bone Proxy(머리를 따라가는 모자), 아바타의 뼈 밑에 손으로 붙여 둔 물건 | 입힐 때 그 뼈 밑에 붙인다 |
| 재질 | 캐릭터와 같다(위 "쉐이더") |

아바타용 옷은 그 아바타의 뼈 이름과 체형에 맞춰 만든 것이라 **그 캐릭터에게만** 맞는다. 다른 몸에 자동으로 맞춰 입히는 것은 하지 않는다. 같은 옷의 다른 아바타용 프리팹이 있으면 옷 id를 같게 두고 한 번 더 내보내면 된다(조각 id가 같아 옷차림도 그대로 옮겨 간다). 게임에 든 옷(예: `sailor-maiden`)을 내 모드 캐릭터용 프리팹으로 내보낼 때 옷 id를 게임의 것과 같게 하면 게임의 그 옷 조각들이 내 캐릭터에게도 나온다.

### 손으로 더 맞추기

게임은 모드 캐릭터에게도 원래 캐릭터와 같은 표를 쓴다. 같은 모드 폴더의 `Tables`에 내 캐릭터 id로 줄을 적으면 게임이 알아서 정한 것 위에 덮인다: 흔들림은 `Cloth.csv`(`skin` 칸이 내 id), 표정 키는 `FaceKeys.csv`, 입고 온 옷의 칸은 `CharacterParts.csv`(줄 id `<캐릭터 id>.<칸>`), 옷 모드의 옷은 `Outfits.csv`(줄 id가 옷 조각의 id), 걸치는 방법은 `WearPositions.csv`(위 "착용 위치와 자유 조정"), 비치는 옷이 밑을 가려 버리면 그 줄의 `sheer`, 체형 슬라이더는 `BodyShapes.csv`, 총 쥐는 손은 `GunHands.csv`. 옷의 칸과 이름은 `.avatar.json`의 `parts`·`items`를 직접 고쳐도 된다(칸 id는 `/mods ids`).

### 알아 둘 것

- 같이 하는 사람도 같은 모드 폴더를 넣어야 내 캐릭터가 보인다. 없는 PC에서는 기본 캐릭터로 보인다(오류는 없다).
- 캐릭터는 처음 고르거나 목록에 그려질 때 번들에서 불러온다. 캐릭터 화면을 열면 모드 캐릭터들의 초상을 찍느라 모두 불러온다.
- 번들은 Windows용으로 만든다. 다른 Unity 버전(2022)에서 만든 번들도 메시·텍스처·프리팹은 읽힌다(쉐이더는 위 규칙대로).
- 옷 모드를 입은 친구도, 그 모드가 없는 PC에서는 그 칸이 캐릭터의 원래 옷으로 보인다(오류는 없다).
- 옷 모드의 옷은 고르거나 옷장 타일의 사진을 찍을 때 번들에서 불러온다. 사진은 미리 구워 둔 것이 없어 처음 볼 때 한 장씩 찍힌다.
- 아직 없는 것: 캐릭터와 옷의 색 세트, 다른 체형의 캐릭터에 옷을 자동으로 맞춰 입히기, VRChat 표현 메뉴의 토글, 표정 애니메이션 자동 옮기기, 모드 파일을 친구에게 자동으로 보내기.

## 3. 프로젝트 모드

Unity로 프로젝트를 열면 게임의 전부를 고칠 수 있다. 구조는 [구조 안내](Architecture.md), 코드 지도는 [스크립트 길잡이](../Assets/_Project/Scripts/README.md)에 있다. 자주 하는 일의 순서는 다음과 같다.

| 하고 싶은 것 | 하는 법 |
| --- | --- |
| 새 총 | [전투 제작 안내](Guides/CombatFoundationGuide.md)의 "새 총 데이터 연결하기". 숫자는 `Weapons.csv`의 새 줄(id = 무기 에셋 이름), 상점에는 `Shop.csv` 줄. 아이콘은 `Documents\ENFILADE\WeaponIcons`에 무기 id 이름의 PNG를 넣어도 된다 |
| 새 좀비 종류 | `ZombieKinds.csv`에 줄을 더한다(기본 좀비 위에 덮어쓰는 값과 나오는 비율). 모습은 `Resources/Enemies/ZombieLooks` |
| 새 캐릭터, 새 옷 | [플레이어 몸](Guides/PlayerBodyGuide.md)의 "BOOTH 캐릭터·옷 가져오기". 어떤 휴머노이드든 실행 중에 몸에 맞춘다(캐릭터마다 따로 굽지 않는다). 옷끼리 겹치는 곳(양말과 신발, 속옷과 겉옷)과 굽 키가 없는 양말은 메시를 재서 맞추므로 옷 짝마다 적을 것이 없다(숫자는 OutfitFit.csv, 겹 순서는 OutfitSlots.csv `over`, 비치는 옷이 밑을 가려 버리면 Outfits.csv `sheer`). 표정도 깜박임이 맞는지 얼굴 메시로 잰다. 하이힐인지도 입힌 발 밑 메시로 재서 발소리를 고르므로(DrawnHeels) 캐릭터가 원래 신은 구두든 옷 모드의 구두든 적을 것이 없다 |
| 새 처형·암살 | 짝(플레이어·좀비 클립 한 쌍)은 블렌더 처형 작업실에서 만들어 `Executions.csv`에 줄로, 그 짝을 언제 하는지(좀비 자세·모름/무너짐·서는 쪽과 거리)는 `ExecutionRules.csv`에 줄로 더한다. 코드는 필요 없다([처형·암살](Features/Executions.md)). 고친 뒤 `Logs/execution-rules.txt`에서 어디서 무엇이 잡히는지 본다 |
| 새 근접 무기(칼) | `ClassKnives.csv`에 줄(클래스 id): 모델 프리팹, 쥐는 자리, 피해, 그리고 휘두르는 법(`windup` 선딜 · `attackSpeed` 공격 속도 · `penetration` 한 번에 베는 수 · `chargeTime` 차징). 동작은 블렌더 칼 작업실에서 만든 `KnifeMelee*.csv` 클립(칼날 구간과 다음 시작점이 표시로 들어 있다). 강공격 클립이 없으면 `slashClip`만 적어도 된다(차징 공격이 그 클립으로 나간다). **사거리와 판정은 칼 모델 자체다**(수치 없음): 프리팹 안에 빈 오브젝트로 표시를 둔다 — `Tip`(칼끝), `Pommel`(손잡이 끝), `Edge`(날 쪽), 선택으로 `BladeBase`(날이 시작되는 곳. 없으면 쥔 자리부터 칼끝까지가 벤다). 모델의 원점이 손에 쥐는 자리다. 표시가 없으면 -Z로 20cm짜리 날로 본다. `bladeRadius`(칼날 둘레 여유 m)를 적는다. `lunge`(스텝인 m)는 기본이 0이고 게임의 칼은 모두 0이다(다가서지 않고 칼날이 닿는 것만 벤다). 수치를 넣으면 그 칼만 휘두를 때 조준선의 적에게 그 거리까지 다가선다. 고친 뒤 `Logs/knife-swings.txt`를 본다(칼날 길이, 클립이 닿는 거리, 거리별 결과, 문제 목록) |
| 새 소리 | 맞는 `Art/Audio` 폴더에 파일을 넣으면 소리 묶음에 들어간다([유지보수 안내](Guides/MaintenanceGuide.md#사운드를-추가하거나-교체하기)) |
| 애니메이션 손보기 | `ArtSource`의 블렌더 작업실을 열고 고친 뒤 저장(Ctrl+S)하면 게임에 들어간다([원클릭 작업실](../Tools/Blender/OneClick/README.md)) |
| 화면 글자 | ENFILADE → 글자 → 글자 편집기 |
| 새 기능의 코드 | 그 기능의 폴더에 두고, 숫자는 표에, 글자는 `Texts.csv`에 ([구조 안내](Architecture.md#새로-더할-때)) |

만든 것을 표 모드로 나눠 주고 싶으면, 고친 표의 줄만 뽑아 `Mods/<이름>/Tables`에 넣으면 된다. 빌드할 때 프로젝트의 `Mods` 폴더는 실행 파일 옆에 그대로 복사된다.

## 4. 모드가 읽히는 자리 (코드)

| 무엇 | 파일 |
| --- | --- |
| Mods 폴더 읽기, 콘솔 `/mods` | [GameMods](../Assets/_Project/Scripts/Core/Mods/GameMods.cs) |
| 표를 이름으로 찾고 모드를 덧씌우기 | [GameTables](../Assets/_Project/Scripts/Core/Tables/GameTables.cs), [CsvTable](../Assets/_Project/Scripts/Core/Tables/CsvTable.cs) (`Overlay`) |
| 줄에서 칸 읽기 | [TableCells](../Assets/_Project/Scripts/Core/Tables/TableCells.cs) |
| 빌드 옆에 Mods 폴더 복사, SDK 폴더 넣기(`Editor/ModSdk`의 스크립트와, 게임과 같이 쓰는 두 파일 ModAvatarFile.cs·WornParts.cs) | [ModsFolderOnBuild](../Assets/_Project/Editor/Build/ModsFolderOnBuild.cs), [ModSdkPackage](../Assets/_Project/Editor/Mods/ModSdkPackage.cs) |
| 모드 캐릭터 찾기·불러오기, 흔들림·표정·입고 온 옷 줄 만들기 | [ModAvatars](../Assets/_Project/Scripts/Core/Mods/ModAvatars.cs), 파일 형식 [ModAvatarFile](../Assets/_Project/Scripts/Core/Mods/ModAvatarFile.cs) |
| 몸에 거는 물건의 착용 위치(표의 줄)와 플레이어의 조정, 입힐 때 그 방법대로 거는 곳 | [WearPosition](../Assets/_Project/Scripts/Player/Outfits/WearPosition.cs), [ItemAdjust](../Assets/_Project/Scripts/Player/Outfits/ItemAdjust.cs), [OutfitDresser.Wearing](../Assets/_Project/Scripts/Player/Outfits/OutfitDresser.Wearing.cs) `Hang`, 얼굴 겉면 맞춤 [FaceFit](../Assets/_Project/Scripts/Player/Outfits/FaceFit.cs), 표 WearPositions.csv·ItemAdjust.csv·OutfitSpots.csv |
| 캐릭터와 옷의 모델이 지닐 수 있는 부품(게임이 입힐 때와 SDK가 내보낼 때 같이 쓰는 한 목록), 플레이 없이 검사 | [WornParts](../Assets/_Project/Scripts/Core/Mods/WornParts.cs), [WardrobeBench](../Assets/_Project/Scripts/Player/Outfits/WardrobeBench.cs) (`Logs/WardrobeBench`) |
| 옷 모드 찾기·불러오기, 옷 줄과 흔들림 줄 만들기 | [ModOutfits](../Assets/_Project/Scripts/Core/Mods/ModOutfits.cs); 옷장이 모델을 찾는 곳 [OutfitCatalog](../Assets/_Project/Scripts/Player/Outfits/OutfitCatalog.cs) `Model`, 입히는 곳 [OutfitDresser](../Assets/_Project/Scripts/Player/Outfits/OutfitDresser.Placing.cs) `Place` |
| 옷과 캐릭터의 흔들림 줄에 필요한 몸 정보(편집기 가져오기와 같이 씀) | [VrcWearer](../Assets/_Project/Scripts/Player/Body/Cloth/VrcWearer.cs) |
| 모드 캐릭터의 재질(쉐이더 고르기·바꿔 그리기) | [ModMaterials](../Assets/_Project/Scripts/Core/Mods/ModMaterials.cs), [ModShaderLibrary](../Assets/_Project/Scripts/Core/Mods/ModShaderLibrary.cs) ([빌더](../Assets/_Project/Editor/Mods/ModShaderLibraryBuilder.cs)), 표 ShaderMap.csv |
| 캐릭터 목록에 모드 캐릭터 더하기 | [PlayerBodyProfile](../Assets/_Project/Scripts/Player/Body/PlayerBodyProfile.cs) `Skins` |
| 실행 중에 표에 줄 얹기 | [GameTables](../Assets/_Project/Scripts/Core/Tables/GameTables.cs) `Lay` |
| 내보내기 도구 (SDK) | [ModAvatarExporter](../Assets/_Project/Editor/ModSdk/ModAvatarExporter.cs)(창), `.Pack`(캐릭터·옷 싸기), `.Pieces`(옷 칸 목록), `.Vrc`(PhysBone·디스크립터·Bone Proxy 읽기), [ModVrcYaml](../Assets/_Project/Editor/ModSdk/ModVrcYaml.cs) |
| 메시를 옷장 칸으로 나누는 규칙 (SDK와 BOOTH 가져오기가 같이 씀) | [ModPieces](../Assets/_Project/Editor/ModSdk/ModPieces.cs), `.Words`(이름 낱말과 칸 목록), `.Meshes`(메시 재기) |
