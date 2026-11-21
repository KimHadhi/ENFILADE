# 적 체력바 (BO6 좀비 체력바)

2026-11-22 사용자: "애드온 원본대로 해 … 전혀 원본 같지 않잖아." 게리 모드 애드온 **Black Ops 6 Zombies Healthbars**(`lua/autorun/cl_bo6healthbar.lua`, `sv_bo6healthbar.lua`, 그림 `materials/bo6healthbar/*.png`)를 한 줄씩 읽고 그대로 옮겼다. 원본과 다른 곳은 아래 '원본과 다른 점'에 모두 적었다.

## 무엇인가

- 화면에 바는 **하나**만 뜬다. 조준점 아래에 있는 57.15m(애드온 3000단위) 안의 적, 또는 내가 방금 때린 적. 조준점이 벗어나도 3초 남는다.
- 적의 충돌 캡슐 꼭대기 가운데를 화면에 옮긴 점 위에 붙고, 거리와 상관없이 **크기가 늘 같다**(1080p에서 142 x 8px). 나타나고 사라질 때 흐려지지 않는다.
- 아래에서 위로: 회색 바탕 → 흰 꼬리 → 빨간 체력 → 체력 끝의 1px 검은 선 → 1px 검은 테두리 → 바 아래 이름(대문자, Hitmarker Text Medium, 흐린 검은 그림자) → 바 왼쪽 머리 아이콘(보통·보스) → 맞은 기둥.
- 맞으면 흰 꼬리가 1초 그대로 있다가(그 사이 더 맞아도 처음 맞은 때부터 1초) 체력으로 미끄러져 내려온다.
- 체력이 줄 때마다 줄어든 자리(이제 체력이 끝나는 곳)에서 반투명한 흰 기둥이 바 아래 끝부터 21px 높이까지 솟고 왼쪽에 2px 선이 먼저 솟는다. 다 솟아 가면 흐려져 사라진다. 바 안에는 같은 색의 불투명한 조각이 흰 꼬리 길이까지만 그려진다. 헤드샷이면 노랑(255,255,94).
- 처치하면 바가 체력 0으로 그 자리에 0.4초 남는다. 내가 처치한 뒤 0.4초 동안은 조준이나 타격으로 바가 다른 적에게 옮겨 가지 않는다.
- 처형 가능 상태는 바가 아니라 적의 외곽선이 보여 준다([EnemyOutlines](EnemyOutlines.md)). 예전 바(EnemyVitalsHUD: 적마다 월드 공간 바, 꺾쇠, 체간 바, 경직 표시)는 지웠다.

## 애드온의 수치

1920x1080 기준. 애드온은 길이마다 `ScrW()` 또는 `ScrH()`에 비율을 곱하므로 게임도 가로 값은 화면 너비/1920, 세로 값은 높이/1080을 곱한다.

| 무엇 | 애드온 | 1080p | 표 |
| --- | --- | --- | --- |
| 바 크기 | `ScrW*0.07395833` x `ScrH*0.00833` | 142 x 8.9964 (그릴 때 8) | `Bar.width`, `Bar.height` |
| 바 자리 | 가운데 x, 위 = y − 높이/2 − `ScrH*0.02` | (x − 71, y − 26.0982) | `Bar.above` |
| 색 | 바탕 (69,73,76), 꼬리 흰색, 체력 (221,71,59), 끝 선·테두리 검정 | | `Bar.*` |
| 꼬리 | 1초 기다림, `Lerp(프레임시간/0.11)`, 체력×1.03 > 꼬리면 붙음 | | `Bar.hold`, `follow`, `snap` |
| 이름 | Hitmarker Text Medium, 칸 `ScrH*0.014`, 바 아래 +2px, 흰색 | 칸 15px = 글자 12px | `Name.*` |
| 이름 그림자 | 칸 `ScrH*0.015`, blursize 2, 6번 겹침 + 글꼴 자체 그림자 1px | 칸 16px | `Name.blurCell`, `blur`, `passes`, `drop` |
| 보통 아이콘 | `ScrW*0.0125` 정사각, 바 왼쪽 `ScrW*(0.0125+0.00078125)`, 위 `ScrH*0.00463` | 24 x 24, x − 25.5, y − 5.0004 | `Icon.normal*`, `Icon.up` |
| 보스 아이콘 | `ScrW*0.0138` x `ScrH*0.0352` | 26.496 x 38.016, x − 27.996 | `Icon.boss*` |
| 기둥 | 처음 높이 = 바 높이/2, 끝 높이 = 바 높이 + `ScrH*0.0111`, 솟기 0.16초·선 0.08초, 95%부터 흐려짐 0.15초·선 0.10초 | 4.4982 → 20.9844 | `Hit.*` |
| 기둥 진하기 | 체력 준 것을 본 순간 60/100, 맞은 소식 0.05초 뒤 100/140(헤드샷이면 노랑) | | `Hit.firstAlpha` … `Hit.wordDelay` |
| 어느 바 | 3000단위 안 조준, 3초 유지, 처치 뒤 0.4초 멈춤, 맞힌 뒤 0.1초 | 57.15m | `Aim.*` |
| 죽은 바 | 0.4초 | | `Bar.deadHold` |

보스 아이콘은 애드온에서 이름에 `boss`가 든 NPC와 `froze_bo6hbar_add_boss` 목록이다. 게임에서는 좀비 종류로 정한다: 거구(Brute)와 엘리트(Exploder, Charger, Ninja, Candyman, Breadbag, Reaper)가 보스, 셈블러·러너와 연습 표적은 보통(`Icon.bossKinds`).

Source의 화면 함수(`surface.DrawRect` 등)는 정수 좌표만 받아서 소수점을 버린다. 그래서 1080p 바 높이는 8px이고 이름 글자 칸은 15px이다. 게임도 같이 자른다(`Bar.wholePixels`, 글자 자리는 `draw.SimpleText`처럼 올림). 이름 글꼴은 Windows가 칸 높이(winAscent+winDescent = 1.25em)로 크기를 고르므로 글자 크기는 칸 ÷ 1.25를 반올림한 px이고 기준선은 칸 위에서 1em 아래다.

## 어디에 있나

- 코드: [EnemyHealthBarHUD](../../Assets/_Project/Scripts/UI/Hud/Damage/EnemyHealthBarHUD.cs) (모든 적의 바 상태를 가진 단 하나의 주인, 화면 오버레이 하나), [PixelRects](../../Assets/_Project/Scripts/UI/Hud/Damage/PixelRects.cs) (Source의 `surface.DrawRect`처럼 픽셀 사각형을 그린 순서대로 한 메시에). 적은 [MeleeCombatTarget](../../Assets/_Project/Scripts/Combat/Damage/MeleeCombatTarget.cs)이 켜질 때 `EnemyHealthBarHUD.Track`으로 들어온다.
- 수치: [Tables/Hud/EnemyBars.csv](../../Assets/_Project/Data/Resources/Tables/Hud/EnemyBars.csv) (`Bar.*`, `Name.*`, `Icon.*`, `Hit.*`, `Aim.*`).
- 그림: [Art/Resources/UI/EnemyBars](../../Assets/_Project/Art/Resources/UI/EnemyBars) (`icon_normal.png` 47x45, `icon_boss.png` 59x81, `bg_alt_1.png` 1420x90, 애드온 파일 그대로).
- 글꼴: [Fonts.csv](../../Assets/_Project/Data/Resources/Tables/Core/Fonts.csv)의 `EnemyBar` 역할(Hitmarker Text Medium). 한글 이름은 전역 대체 글꼴로 그려진다.

## 원본과 다른 점

- **내 타격만 바를 옮긴다.** 애드온은 누구의 타격이든(다른 플레이어, NPC) 맞은 적의 바를 띄우고 나머지를 끈다. 협동에서 동료가 쏠 때마다 바가 튀므로 바를 띄우고 끄는 것, 맞힌 뒤 0.1초, 처치 뒤 0.4초 멈춤은 내 타격만 센다. 흰 꼬리를 세우는 것과 기둥 색칠(헤드샷 노랑)은 애드온처럼 누구의 타격이든 센다(안 그러면 동료가 때린 적의 꼬리가 멈춰 버린다).
- **카메라 뒤의 바는 안 그린다.** 애드온은 화면 뒤의 점도 그대로 옮겨서 엉뚱한 곳에 뒤집힌 바를 그린다(버그).
- **기둥이 한 프레임에 끝까지 솟지 않는다.** 애드온은 프레임이 0.16초보다 길면 기둥이 끝 높이에 딱 닿고, 그러면 흐려지는 조건(끝보다 아직 낮을 것)이 영영 안 맞아 기둥이 남는다. 렉 한 번에 기둥이 박혀 남지 않게 한 프레임에 99.9%까지만 간다.
- **흐린 그림자**는 Source가 글자 그림을 5x5 가우스(σ = 0.683 × 2, Source의 π 3.14와 e 2.7 그대로)로 흐린 뒤 6번 겹쳐 그린 것이다. 게임은 글자를 흐릴 수 없어 가우스 한 칸마다 이름을 한 장씩(25장) 1 − (1 − 무게)^6 진하기로 겹친다. 겹친 칸끼리 곱해지는 만큼 조금 옅다.
- 글자는 TextMeshPro로 그려서 Windows의 힌팅(픽셀에 맞춘 획)이 없다. 커닝·합자는 끈다(Source의 Windows 글꼴은 글자 폭만으로 나아간다).
- 아이콘 그림은 다른 HUD 그림과 같은 가져오기 설정(밉맵, 부드러운 필터)이다. 게리 모드는 `Material("…png")`에 `smooth`가 없으면 픽셀을 그대로 집어 그리는 것으로 안다(확인 못 함). 원본처럼 각지게 하려면 세 `.png.meta`의 `filterMode`를 0, `enableMipMap`을 0으로.
- 조준 광선은 카메라 가운데에서 나가고(애드온은 눈 위치), 바를 띄우는 거리는 적의 뿌리 위치와 내 플레이어 위치 사이다. 적은 충돌 캡슐로 맞는다(애드온의 NPC 충돌 상자). 맞힌 뒤 0.1초 동안 '적이 아닌 것'은 물리 물체나 플레이어다(게리 모드의 유효한 엔티티 자리).
- 애드온의 피해 합계 숫자(`froze_bo6hbar_enabled_dmg_numbers_cl`, 기본 꺼짐)는 옮기지 않았다.
- 애드온은 NPC가 사라지고 죽음 표시까지 받으면 0.4초를 두 배로 세어 0.2초에 지우기도 한다. 게임은 늘 0.4초.
