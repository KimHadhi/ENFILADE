# 적 체력바 (HUD 애드온 모듈 EnemyBars)

2026-11-22 사용자: "애드온을 넣으라고", "원본 그대로 따오되 네 디자인, 기획 센스 처넣으니까 망하는 거지". 게리 모드 애드온 **Black Ops 6 Zombies Healthbars**(`lua/autorun/cl_bo6healthbar.lua`, `sv_bo6healthbar.lua`, `sh_bo6healthbar.lua`, 그림 `materials/bo6healthbar/*.png`, 글꼴 `resource/fonts/*.ttf`)의 Lua를 모두 읽고, 생김새·수치·시간·설정은 원본 그대로 두고 코드는 새로 썼다. 원본의 버그는 고쳤다(아래 '고친 버그'). 손으로 만든 옛 복사본(EnemyHealthBarHUD, PixelRects)의 코드와 수치는 쓰지 않았다.

## 무엇인가

- 모든 적(EnemyHealth)이 나타날 때부터 바 하나씩을 갖는다(원본은 NPC가 생길 때마다). 화면에 뜨는 것은 **하나**: 조준점 아래 최대 거리(57.15m = 원본 3000단위) 안의 적, 또는 마지막으로 맞은 적. 누가 때렸든(다른 플레이어도) 원본처럼 그 적의 바가 뜨고 나머지는 꺼진다. 다만 조준한 적이 있으면 조준이 이긴다.
- 조준점이 사거리 안의 적(또는 방금 근처에서 타격이 있었을 때는 물리 물체·플레이어)에 없은 지 3초가 지나면 모든 바가 꺼진다.
- 적 충돌 캡슐 꼭대기 가운데를 화면에 옮긴 점 위에, 거리와 상관없이 같은 크기로 붙는다(1080p 141 x 8px). 그리는 순서: 회색 바탕(설정으로 무늬 그림 `bg_alt_1.png`) → 흰 꼬리 → 빨간 체력(221,71,59) → 체력 끝 1px 검은 선 → 1px 검은 테두리 → 바 아래 이름(대문자, Hitmarker Text Medium, 흐린 검은 그림자 + 1px 그림자) → 바 왼쪽 아이콘(보통: 빨간 마름모, 보스: 뿔 달린 해골) → 바 오른쪽 피해 합계(설정, 기본 끔, Hitmarker VF Condensed Light) → 맞은 기둥.
- 맞으면 흰 꼬리가 1초 그대로 있다가(그동안 더 맞아도 처음 맞은 때부터 1초) 체력으로 미끄러져 내려온다(0.11초).
- 체력이 줄면 줄어든 자리(이제 체력이 끝나는 곳)에서 반투명한 기둥이 바 아래 끝부터 바 높이 + 화면 높이의 1.11%까지 솟고(0.16초), 왼쪽 2px 선이 더 빨리 솟는다(0.08초). 95%를 넘으면 흐려져 사라진다. 바 안에는 같은 색의 불투명한 조각이 흰 꼬리 길이까지만 보인다. 헤드샷이면 노랑(255,255,94). 맞은 소식이 없는 체력 감소(네트워크 보정 등)는 원본처럼 흰색에 옅은 기둥(60/100)이고 앞 선이 있다.
- 죽거나 사라진 적의 바는 체력 0으로 그 자리에 0.4초 남는다. 처치한 사람은 0.4초 동안 조준이나 타격으로 바를 옮기지 않는다(죽은 적의 바가 보이게).
- 보스 아이콘: 원본은 클래스 이름에 `boss`가 든 NPC와 `froze_bo6hbar_add_boss` 목록. 게임은 엘리트·네임드 등급(Enemies.csv `rank`, `/spawn n elite`)과 보스 목록의 좀비 종류. 목록은 콘솔 `/hbar_add_boss 종류`, `/hbar_remove_boss 종류`(원본 명령 두 개, 원본의 `data/bo6hbar/bosses.json` 대신 설정 파일에 저장).

## 설정 (설정 창 > HUD > 적 체력바, `GameSettings.addonEnemyBars`)

| 원본 convar | 기본 | 필드 | 줄 |
| --- | --- | --- | --- |
| `froze_bo6hbar_enabled`(서버) + `froze_bo6hbar_enabled_cl` | 1, 1 | `enabled` | 체력바 표시 (둘 다 켜져야 그리던 것을 한 스위치로: 게임에 방장 전체 HUD 스위치가 없다) |
| `froze_bo6hbar_enabled_name_cl` | 1 | `name` | 이름 표시 |
| `froze_bo6hbar_enabled_name_shadow_cl` | 1 | `nameShadow` | 이름 그림자 |
| `froze_bo6hbar_enabled_dmg_numbers_cl` | 0 | `damageNumbers` | 피해 숫자 |
| `froze_bo6hbar_alt_bg_texture` | 0 | `altBackground` | 무늬 바탕 |
| `froze_bo6hbar_max_distance` | 3000단위 | `maxDistance` (57.15m, 0~19050m) | 최대 거리 (슬라이더는 조준 광선 길이 624m까지) |
| `froze_bo6hbar_add_boss` / `remove_boss` | 빈 목록 | `bossKinds` (`Brute;Ninja` 처럼) | 콘솔 명령 |

## 수치

모두 [Tables/Hud/EnemyBars.csv](../../Assets/_Project/Data/Resources/Tables/Hud/EnemyBars.csv)의 `HealthBar.*`, `NameTag.*`, `HeadIcon.*`, `Total.*`, `Pillar.*`, `Target.*` 줄(EnemyBarsLook). 길이는 원본이 `ScrW()`·`ScrH()`에 곱하는 비율 그대로라 해상도가 바뀌어도 같은 몫이다. 같은 파일의 `Bar.*`·`Name.*`·`Icon.*`·`Hit.*`·`Aim.*` 줄은 옛 복사본의 것이라 그것을 지울 때 함께 지운다.

- GMod의 `surface.DrawRect` 등은 사각형 크기를 정수로 자른다. 원본의 비율은 그 위에서 맞춰졌으므로(바 `W*0.07395833` = 141.99 → 141, `H*0.00833` = 8.996 → 8) 사각형 크기는 같이 자른다. 자리는 공용 Painter처럼 자르지 않아 움직이는 바가 계단지지 않는다.
- 이름 글꼴은 크기 `H*0.014`(1080p 15), 흐린 그림자 글꼴 `H*0.015`(16)를 이름 글꼴 위에 가운데 맞춘다(Source의 글꼴 높이는 칸 + 그림자 1px).
- 흐린 그림자: Source는 글자 그림을 5x5 가우스(σ = 0.683 x 2, Source 식의 3.14와 2.7 그대로)로 흐리고, 원본은 그것을 검게 6번 겹쳐 그린다. TextMeshPro 글자의 거리장은 이 크기에서 약 1.4px까지만 닿아(가우스는 2.5px) 머티리얼로는 그만큼 흐릴 수 없으므로, 흐림을 정의대로 그린다: 가우스의 칸마다 이름 한 장(25장)을 그 칸만큼 옮겨 1 − (1 − 무게)^6 진하기로 겹친다(같은 검정 6번 = 그 진하기).
- 글꼴의 `shadow = true`(Source의 1px 오른쪽 아래 검은 그림자)는 검은 글자를 1px 옮겨 먼저 그린다.
- 피해 합계 글꼴 "Hitmarker VF Condensed Light"는 원본 `hitmarker-vf.ttf`(변수 글꼴)의 Condensed Light 인스턴스(wght 300, wdth 0)를 정적 글꼴로 자른 `Resources/UI/Fonts/hitmarker-vf-condensed-light.ttf`.
- 흐려지는 `Lerp(RealFrameTime() / t)`는 프레임과 상관없는 `Smooth.Lerp(1/t)`(60fps 이상에서 같은 곡선). 원본 타이머(`timer.Simple`)는 게임 시간, 꼬리·기둥·남는 시간·3초는 실제 시간(원본의 CurTime과 RealFrameTime 그대로).

## 고친 버그

- 카메라 뒤의 적: 원본은 `ToScreen`의 뒤집힌 점에 바를 그렸다. 이제 안 그린다.
- 렉 한 번에 기둥이 박혀 남던 것: 0.16초 넘는 프레임이면 `Lerp`가 끝 높이에 딱 닿고, 흐려지는 조건 "아직 끝보다 낮을 것"이 영영 안 맞았다. 95% 넘음만 본다.
- 바가 지워질 때 루프 안 `table.remove`가 다음 바를 한 프레임 건너뛰었고, 죽고 사라지기까지 한 NPC는 0.4초를 두 번 세고 두 번 지워 다음 적의 바를 영영 없앴다. 한 번 세고 한 번 지운다.
- 타격마다 `timer.Simple`을 걸어 앞선 타격의 타이머가 나중 일을 끊었다: 새 꼬리 기다림을 곧바로 끝내고, `GET_DAMAGED`(0.1초)와 처치 멈춤(0.4초)을 일찍 껐다. 각자 자기 시간만 센다.
- 피해 합계가 타격마다 1초 뒤 지워져 계속 쏘면 한 발 값으로 깜빡였다. 합계를 연 타격부터 1초에 지운다.
- 처치 멈춤 중의 타격이 다른 바를 모두 끄고 자기 바는 안 띄워 0.4초 동안 아무 바도 없었다. 멈춤 중의 타격은 바를 건드리지 않는다.
- 헤드샷 표시는 NPC에 남은 마지막 총알의 표식이라 헤드샷 뒤 폭발도 노랬다. 그 타격의 것만 본다(EnemyHealth의 머리 판정).
- 네트워크를 기다리는 0.05초 동안 기둥이 흰색으로 떴다가 노랗게 바뀌었다. 게임은 타격 소식이 체력과 같은 프레임에 오므로 처음부터 제 색이다.
- 처치한 타격으로 몸이 그 프레임에 없어지면 그 타격의 기둥이 안 생겼다. 타격 보고의 체력으로 만든다.
- 조준한 적의 바로 바뀔 때 원본은 그리는 루프 안에서 바꿔 한 프레임 두 바가 보였다. 그리기 전에 정한다.
- 이름 설정을 끄면 이미 배운 이름은 계속 그렸다. 끄면 안 그린다.
- 흰 꼬리가 정확히 0이 되면(렉) 다시 가득 찼다(`follower_init`이 늘 false). 다친 채로 처음 보는 적(늦게 들어온 PC의 복사본)은 꼬리가 가득한 채 굳어 있었다. 지금 체력에서 시작한다.
- 해상도를 바꿔도 처음 크기 그대로였다(파일을 읽을 때 `ScrW`·`ScrH`를 한 번 잡음). 매 프레임 지금 크기로.
- 보스 목록에 같은 이름이 여러 번 들어가고, 빼기는 이웃한 중복을 건너뛰었다. 종류마다 한 번.

## 원본과 다른 점(데이터가 다른 곳)

- 조준 광선은 카메라에서 나간다(원본은 눈). 적은 충돌 캡슐이나 뼈 히트박스(트리거)로 맞는다(원본 NPC의 충돌 상자는 늘 단단하다). 다른 트리거는 통과한다.
- 이름은 적의 표시 이름(좀비 종류의 Texts 말). 원본 글꼴은 `extended = false`라 라틴 글자만 그리지만 한글 이름은 전역 대체 글꼴로 그린다.
- 피해 합계는 우리 피해가 소수라 `tostring`의 긴 소수(33.299999237061) 대신 짧은 표기(33.3)다.
- 아이콘 그림은 다른 HUD 애드온 그림과 같은 가져오기 설정(밉맵, 부드러운 필터). GMod의 `Material("….png")`은 `smooth`가 없으면 점 샘플링으로 그리는 것으로 안다(확인 못 함).

## 어디에 있나

- 코드: [Scripts/UI/Hud/Addons/EnemyBars](../../Assets/_Project/Scripts/UI/Hud/Addons/EnemyBars) — `EnemyBarsModule`(Install, 콘솔 명령), `EnemyBarsHud`(모든 바의 단 하나의 주인: 타격, 조준, 그리기), `EnemyBar`(적 하나의 상태, 풀), `EnemyBarsLook`(표 줄), `EnemyBarsBosses`(보스 목록), `EnemyBarsOptions`/`EnemyBarsOptionsRows`(설정).
- 데이터: `EnemyHealth.Living`(적), `CombatDamageEvents.Damaged`(모든 PC에서 모든 타격: 원본 서버의 `net.Broadcast`), `Camera.main`의 광선. 읽기만 한다.
- 그림: [Art/Resources/UI/Addons/EnemyBars](../../Assets/_Project/Art/Resources/UI/Addons/EnemyBars) (`icon_normal.png` 47x45, `icon_boss.png` 59x81, `bg_alt_1.png` 1420x90, 애드온 파일 그대로).
- 글꼴: `Hitmarker Text Medium`(hitmarker-text-medium.ttf), `Hitmarker VF Condensed Light`(hitmarker-vf-condensed-light.ttf), [AddonFonts.csv](../../Assets/_Project/Data/Resources/Tables/Hud/AddonFonts.csv).
