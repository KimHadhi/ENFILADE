# 데스 스크린 (죽었을 때 화면)

2026-11-12 사용자: "그리고 게리모드에 애드온 새로운 거 하나 추가했는데 그거처럼 데스 스크린도 만들어 봐." 0.8.270~0.9.308에는 그 애드온(MW3 Death Screen)을 흉내 낸 사본을 우리가 그렸다.

2026-11-22 사용자: "애드온을 넣으라고", "원본 그대로 따오되 네 디자인, 기획 센스 처넣으니까 망하는 거지", 그리고 애드온 시안을 보고 "여기에 우리 데스 정보 보여주는 거 합치면 돼". 그래서 이제 **애드온 화면은 원본 그대로**(HudAddons 모듈 `DeathScreen`: 원본 Lua를 다 읽고 C#으로 새로 쓴 것)이고, 그 위에 **우리 데스 카드**(사망/전멸·무엇에 죽었나·버틴 시간·처치·돈·다음 줄)가 뜬다. 애드온에 손댄 것은 카드를 얹은 것 하나다.

## 애드온이 하는 일 (`lua/autorun/mw3_deathscreen.lua`)

- 죽은 순간부터 부분마다 제 시계로(곡선은 `easeOut` = 1-(1-x)², 피만 `smoothstep`):
  - 색(`DrawColorModify`): 0초부터 1.2초 동안. 다 바뀌면 빨강 +0.05, 초록 0, 파랑 -0.015, 밝기 -0.03, 대비 ×1.08, 채도 0.58, mul 셋 0. 모든 숫자가 진행도만큼 자란다.
  - 흐림(`pp/blurscreen`): 0.15초 뒤부터 2.4초 동안. 네 번 겹쳐 그리고, i번째의 세기는 (i/4)×흐림 강도(기본 8)×진행도.
  - 비네트 그림(`deathscreen/vignette`, 1024×512, 제 알파): 0초부터 1.6초 동안 불투명도 0→1.
  - 피 그림(`deathscreen/blood`, 1024×512): 피 지연(기본 1초) 뒤부터 1.6초 동안, 불투명도 0→피 강도.
  - 그리는 차례: 세상 → 색 → 흐림 → 비네트 → 피 → HUD(`RenderScreenspaceEffects` 다음 `HUDPaintBackground`, 그 위에 `HUDPaint`). 두 그림은 화면 전체로 늘린다.
- 소리: `whistle.mp3`(4.2초)를 죽을 때 한 번, 음량 = 휘슬 음량(0이면 안 남).
- 다시 살아나면: 화면은 그때까지 온 데서부터 0.3초 동안 곧게 걷히고, 소리는 0.06초 만에 끊긴다.
- 설정(콘솔 변수 5개): 켜기(1), 휘슬 음량(1, 0~2), 흐림 강도(8, 0~20), 피 강도(1, 0~1), 피 페이드 인 지연(1, 0~5). 미리 보기 버튼(5초), 기본값 버튼. 끄면 죽은 채라도 화면이 걷힌다(미리 보기는 빼고).
- 글자·누가 죽였나는 없다. 깜박이는 것도 없다(깜박임 규칙으로 바꾼 것 없음).

## 우리 게임에서 어떻게 그리나

- **한 장의 렌더 패스**(`DeathScreenPass`): 세상을 그리는 카메라(Base, 화면으로 가는 것)의 그림에, 포스트 효과 뒤(`AfterRenderingPostProcessing`)·HUD 카메라 앞에서 그린다. 그래서 HUD와 우리 카드는 흐려지지 않고 원본처럼 위에 또렷하다. 카메라가 그리기 시작할 때 큐에 넣으니 렌더러 에셋은 고치지 않았고, 죽음 화면이 없으면 큐에 넣지도 않는다(그 프레임 비용 0).
- **원본의 셈 그대로**: 게리모드 설치본의 컴파일된 셰이더(`garrysmod_dir.vpk`의 `g_colourmodify_ps20.vcs`·`g_screenspace_ps20.vcs`)를 풀어 읽고, `pp/blurscreen`의 `$blur`가 샘플 간격이 되는 곳은 `game_shader_generic_garrysmod.dll`에서 읽었다.
  - 색: 채널마다 add → 세 채널 **평균** 회색 쪽으로 채도(휘도 아님) → mul 항(각 채널이 다른 둘보다 큰 만큼의 제곱, 원본은 0) → 밝기 더하기 → 대비 곱하기 → 0~1로 자르기. URP 볼륨의 색 보정(로그 공간 대비·선형 채도)으로 옮기면 회색 한 점에서만 맞으므로 쓰지 않고, 이 셈을 화면에 보이는 값(감마 인코딩 0~1)에 그대로 한다.
  - 흐림: 한 번은 자기와 이웃 8개(간격 = 그 패스의 px) 아홉 샘플(쌍선형) 평균 = 3×3 상자. 원본처럼 앞 패스의 결과를 다시 흐린다(기본 8이면 2·4·6·8px).
  - 그림: 비네트 다음 피, 각자 알파 × 그리는 색의 알파(원본 `alphaByte`처럼 0~255 정수로 반올림).
  - 원본은 이 모두를 8비트 감마 화면 버퍼에서 했으므로, 중간 그림도 8비트 감마(R8G8B8A8)에 담고 마지막에 선형 빛으로 되돌린다. UI 캔버스는 선형 공간에서 섞어 비네트 가장자리가 원본보다 옅게 보였을 것이라 캔버스에 그리지 않았다.
- **크기**: 흐림의 px는 다른 애드온 모듈처럼 **1080p px**로 읽는다(화면 배율 √(w/1920·h/1080)). 1080p에서는 원본과 같고, 1440p·4K에서는 원본(실제 화면 px)보다 같은 비율만큼 넓다.
- **소리**: `Art/Audio/Addons/DeathScreen/Whistle/whistle.mp3`(원본 파일 그대로, 정규화 없음)를 HudAddons 소리 묶음의 `DeathScreen/Whistle`로, UI 채널(UI 음량을 따르고 일시 정지·리스너 필터와 상관없음)에서 2D로. 원본 채널은 음량 2까지 키웠으므로(1이 넘으면 증폭) 1을 넘는 몫은 같은 샘플에서 함께 시작하는 두 번째 소리가 맡는다(같은 위상 둘 = 그 배율). 두 소리를 한 샘플에 맞추려고 부른 뒤 0.05초에 시작한다(원본의 `sound.PlayFile`도 파일을 읽고 비동기로 시작했다). Sounds.csv에 `HudAddons/DeathScreen/Whistle` 줄을 두면 음량·피치가 곱해진다(없으면 1).
- **시간**: 모두 실제 시간(`RealTime`). 일시 정지 메뉴 밑에서도 흐른다.

## 카드와 흐름 (우리 것, 그대로)

애드온의 Think 훅이 `ply:Alive()`를 보던 자리는 게임의 죽음 흐름(`global::DeathScreen`)이 맡는다. 그것이 카드를 띄우고 물리는 바로 그때 `HudAddons.DeathScreenModule`을 부른다: 뜰 때 `Die`, 다시 살아날 때와 미리 보기가 끝날 때 `Respawn`(0.3초 걷힘, 소리 끊김. 원본의 미리 보기도 onRespawn으로 끝난다), 관전으로 넘어갈 때 `Leave`(0.3초 걷힘, 소리는 끝까지), 켜기를 끈 채 보이던 것도 `Respawn`, 플레이어가 사라지거나 겹침 검사로 카드만 세울 때 `Stop`(한 번에 끔). "죽음이 보이는가"의 주인은 하나다.

- 진짜로 죽을 때만. 쓰러질 때는 뜨지 않는다. 진행 중인 판에 늦게 들어와 기다리는 사람(`PlayerDamageReceiver.IsWaiting`)에게도 뜨지 않는다(원본의 "처음 읽은 값은 기록만" 규칙의 자리).
- **방**: 쓰러진 몸의 눈으로 보는 동안 뜨고, 관전이 시작되면 물러난다(원본에는 관전이 없다). **혼자**: 죽음이 곧 전멸이라 `전멸`로 뜨고 모두 돌아올 때까지. **나중에 전멸**: 관전하던 사람에게 다시 `전멸`로(소리도 다시), 쓰러져 있던 사람에게도 뜬다.
- 카드는 화면과 함께, 화면이 걷히는 시간(`DeathScreenModule.ClearSeconds`, 0.3초)에 물러난다. 카드 흐름의 표 [DeathScreen.csv](../../Assets/_Project/Data/Resources/Tables/Player/DeathScreen.csv)에는 머무는 시간(`holdSeconds`)과 미리 보기 시간(`previewSeconds`)만 남았다.
- 카드: 위에 `사망`/`전멸`, 무엇에 죽었나(좀비 종류 분홍·엘리트 표·어떻게), 아래에 버틴 시간·처치·가진 돈, 맨 아래 다음에 올 일과 줄어드는 분홍 실선. 모양과 움직임은 [DeathCard](../../Assets/_Project/Scripts/UI/Hud/Death/DeathCard.cs).
- 동료 화면: 누가 죽으면 `○○ 사망`, 킬 피드 줄, 알림 소리(PlayerTally).

## 설정과 표

- 설정 → HUD → 데스 스크린(애드온 패널의 차례와 말): 설명 줄, **데스 스크린 켜기**(끄면 화면도 카드도 소리도 없다), **휘슬 음량** 0~2(소수 둘), **흐림 강도** 0~20(정수, 0이면 흐림 없음), **피 오버레이 강도** 0~1(소수 둘), **피 페이드 인 지연(초)** 0~5(소수 하나), **미리 보기**(게임 중 살아 있을 때. `/death`와 같아서 카드도 함께), **기본값으로**. 저장: `GameSettings.addonDeathScreen`([DeathScreenOptions](../../Assets/_Project/Scripts/UI/Hud/Addons/DeathScreen/DeathScreenOptions.cs)).
- 원본 상수: [AddonDeathScreen.csv](../../Assets/_Project/Data/Resources/Tables/Hud/AddonDeathScreen.csv) `Screen`(시간표·흐림 횟수·걷힘·소리 끊김)·`Grade`(색 숫자). 파일 이름이 `DeathScreen`이 아닌 것은 카드 흐름의 [Tables/Player/DeathScreen.csv](../../Assets/_Project/Data/Resources/Tables/Player/DeathScreen.csv)와 이름이 겹치지 않게(표는 파일 이름으로 찾는다).
- 글자: Texts.csv `settings.hud.death.*`(설정 줄), `death.*`(카드).

## 시험

- 콘솔 `/death`: 죽지 않고 5초. `/death wipe`, `/death exploder` 등. 설정 → HUD → 데스 스크린 → 미리 보기도 같다.
- 진짜 죽음: `/fatal`, 방에서 쓰러져 피 흘려 죽기, 전멸.
- 볼 것: 세상이 1.2초에 붉은 갈색으로 빠지고, 0.15초 뒤부터 2.4초 동안 고르게 흐려지고(손과 총도), 비네트가 1.6초에 닫히고, 1초 뒤 피가 1.6초에 걸쳐 비치고, 소리 한 번. HUD와 카드는 또렷하다. 살아나면 0.3초에 걷히고 소리가 바로 끊긴다. 관전으로 넘어가면 0.3초에 걷히고 소리는 끝까지 난다. 피 그림의 큰 얼룩은 화면 오른쪽 아래에 있어야 한다(위아래가 뒤집히면 왼쪽 위).

## 파일

- [DeathScreenModule](../../Assets/_Project/Scripts/UI/Hud/Addons/DeathScreen/DeathScreenModule.cs): 바깥에서 부르는 곳(`Install`·`Die`·`Respawn`·`Leave`·`Stop`·`Enabled`·`ClearSeconds`).
- [DeathScreenDriver](../../Assets/_Project/Scripts/UI/Hud/Addons/DeathScreen/DeathScreenDriver.cs): 원본의 상태(levels, onDeath/onRespawn, 켜기 감시)와 소리.
- [DeathScreenPass](../../Assets/_Project/Scripts/UI/Hud/Addons/DeathScreen/DeathScreenPass.cs) + 셰이더 `Art/Resources/UI/Addons/DeathScreen/DeathScreenFilm.shader`: 색·흐림·그림.
- [DeathScreenLook](../../Assets/_Project/Scripts/UI/Hud/Addons/DeathScreen/DeathScreenLook.cs)(표), [DeathScreenOptions](../../Assets/_Project/Scripts/UI/Hud/Addons/DeathScreen/DeathScreenOptions.cs)·[DeathScreenOptionsRows](../../Assets/_Project/Scripts/UI/Hud/Addons/DeathScreen/DeathScreenOptionsRows.cs)(설정).
- 그림: `Art/Resources/UI/Addons/DeathScreen/vignette.png`·`blood.png`(원본 VTF를 `Tools/HudAddons/addon_picture.py`로 원래 크기 그대로). 소리: `Art/Audio/Addons/DeathScreen/Whistle/whistle.mp3`.
- 카드와 흐름: [DeathScreen](../../Assets/_Project/Scripts/UI/Hud/Death/DeathScreen.cs)(언제·무엇에 죽었나·`/death`), [DeathCard](../../Assets/_Project/Scripts/UI/Hud/Death/DeathCard.cs), [DeathCardGraphic](../../Assets/_Project/Scripts/UI/Hud/Death/DeathCardGraphic.cs).

## 원본과 다른 것

- 카드(사용자가 더하라고 한 것)와 그 흐름(방·혼자·전멸·관전). 관전으로 넘어갈 때 화면이 걷히는 것은 원본에 없다(원본은 살아날 때까지 그대로).
- 흐림의 px를 1080p px로 읽는다(위 크기).
- 소리가 0.05초 늦게 시작한다(두 소리를 한 샘플에 맞추느라).
- mul 숫자는 다른 색 숫자처럼 진행도만큼 자란다(원본은 0을 그대로 써서 차이 없음).
- 원본에서 고친 것: 원본은 `pp/blurscreen`의 `$blur`를 공용 머티리얼에 남겨 두어 뒤에 그 머티리얼을 쓰는 다른 애드온의 흐림을 바꿨고, `DrawColorModify`가 `$pp_colour_inv`를 정하지 않아 다른 애드온이 켜 둔 색 반전이 데스 스크린에 묻어날 수 있었다(여기서는 반전 없음). 소리 파일을 못 열면 `surface.PlaySound`로 음량 설정을 무시하고 끊을 수도 없게 틀던 것도 없다(소리가 없으면 경고 한 번).
