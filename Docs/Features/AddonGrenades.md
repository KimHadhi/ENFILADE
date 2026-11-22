# 수류탄 HUD 애드온 (Grenades 모듈)

2026-11-22 사용자: "애드온을 넣으라고", "원본 그대로 따오되 네 디자인, 기획 센스 처넣으니까 망하는 거지". 게리 모드 애드온 둘을 하나의 HUD 모듈로 옮겼다(두 애드온 모두 사용자가 고름: "원본대로").

- **MW Quick Grenade and Grenade Indicator**(`lua/autorun/client/cl_quicknade.lua`, `sh_quicknade.lua`, `server/sv_quicknade.lua`, `weapons/weapon_quicknade.lua`, 뷰모델 `models/weapons/quick_grenade/c_eq_frag.mdl`, 그림 `materials/vgui/frag`·`frag_arrow`, 소리 `sound/quicknade`·`sound/weapmvmt`): 쿠킹 링, 다가오는 세열 표시, 퀵 수류탄의 소리. 퀵 수류탄 무기 자체(G로 꺼내 던지기)는 게임에 이미 있는 게임플레이라 옮기지 않았다([클래스 투척물](Grenades.md)).
- **Arctic's Grenade Indicators**(`lua/autorun/cl_grenade_indicator.lua`, `cl_grenade_indicator_menu.lua`, 그림 `materials/ugi/triangle.png`, `ugi/holo.vtf`의 다섯 프레임): 빨간 세모와 홀로그램.

수류탄 개수는 이 모듈이 아니라 플레이어 HUD 모듈(PlayerHud)의 수류탄 칸이 보여 준다. 예전 투척물 판(GrenadeHUD)은 0.9.333에 지웠다(사용자: "구시대 허드 다 죽여").

## 무엇인가

수류탄 추적은 하나다(`GrenadeTracker`). 이 PC의 던진 수류탄 전부(`ThrownGrenade.Live`: 내 것과 친구 것의 사본)를 한 번 찾아 한 줄씩 두고, 매 프레임 눈에서의 방위(소스식: `((위치 - EyePos()):Angle() - EyeAngles()).y + 90`), 발과 눈에서의 거리, 모델의 가운데와 메시를 한 번만 계산한다. 네 모양이 모두 이것을 읽는다.

- **쿠킹 링**(MW `Indicator:DrawTimer`): 쿠킹하는 수류탄을 쥐고 0.5초가 지나면 조준점 둘레에 반지름 40px, 두께 2px, 30조각 원이 뜨고(0.3초 시간 상수로 진해짐), 신관이 타는 만큼 안쪽으로 0까지 줄어든다. 신관 끝 1.5초 전부터 빨개진다. 놓으면 그 크기로 0.1초 시간 상수로 사라진다. 링이 보이는 동안 총의 조준점은 비킨다(원본에서는 퀵 수류탄이 무기라서 총과 그 조준점이 손에서 빠졌다).
- **다가오는 세열**(MW `Indicator:DrawFrag`): 세열(원본 `npc_grenade_frag`, 즉 우리 Frag) 하나마다 `vgui/frag` 그림(ScreenScale 15 = 1080p 45px)과 그 둘레로 돌아간 `vgui/frag_arrow`(1.5배)가 화면 가운데에서 ScreenScale 72(216px) 원 위, 수류탄 쪽에 뜬다. 발에서 300단위(7.62 m) 안이면 나타나고 250단위(6.35 m) 안이면 빨개진다. 새 것은 화면 가운데에서 미끄러져 나가고, 자리·진하기·색은 0.1초 시간 상수로 따라간다. 터진 뒤에는 그 자리에서 흐려져 사라진다.
- **세모**(Arctic): 눈에서 512단위(13 m) 안의 수류탄마다 빨간 세모(ScreenScale 10 = 30px)가 ScreenScale 64(192px) 원 위에서 수류탄 쪽을 가리킨다.
- **홀로그램**(Arctic): 같은 수류탄의 모델이 눈앞 90단위에 작게, 수류탄 쪽으로 (거리/512 + 0.25) × 0.75만큼 비켜(위아래 최대 48, 옆 최대 64단위) 그려진다. 30° 기울여 초당 40° 돈다. 주황(1, 0.65, 0)에 진하기 0.5, 빛을 더하는 방식으로 무엇보다 위(벽·내 팔·총 위)에 그려진다. 플레이어 시야각과 상관없이 원본의 시야(소스 fov 100, 가까운 면 2, 먼 면 1024단위)로 그린다. 세상의 후처리가 끝난 뒤, 2D HUD 아래에 그린다(`GrenadeHoloPass`, 원본의 HUDPaint 안 `cam.Start3D`).
- **퀵 수류탄 소리**: 뷰모델의 소리 이벤트 그대로. 꺼내기(27프레임, 30 fps, 1.25배속): 프레임 5 장비 덜컥(Viewmodel.Small), 10 핀 뽑기 + 천 소리(QuickNade.PinPull, .Cloth), 12·21 덜컥. 던지기(21프레임, 30 fps): 0 덜컥(Viewmodel.Medium), 6 휙(QuickNade.Throw), 10 덜컥, 0.3초에 손을 떠남. 우리 손은 제 클립을 쓰므로 꺼내기의 첫 덜컥은 꺼내기 시작에서, 핀 소리와 그 뒤 덜컥은 우리 클립이 핀을 뽑는 순간에서, 던지기 소리는 우리 클립이 놓는 순간에서 원본 간격 그대로 잰다. 볼륨 1, 피치 100.

## 수치와 파일

- 수치: [Tables/Hud/AddonGrenades.csv](../../Assets/_Project/Data/Resources/Tables/Hud/AddonGrenades.csv) (`Ring.*`, `Frag.*`, `Arctic.*`, `Holo.*`, `Sound.*`). 거리는 원본대로 소스 단위(1 = 2.54 cm), 색은 0~255. 이름이 Grenades.csv가 아닌 것은 투척물 표가 이미 그 이름이라서다(표는 파일 이름으로 찾는다).
- 코드: [Scripts/UI/Hud/Addons/Grenades](../../Assets/_Project/Scripts/UI/Hud/Addons/Grenades) — `GrenadesModule`(설치, 게임이 부르는 입구), `GrenadesHud`(매 프레임 그리기), `GrenadeTracker`(수류탄 추적 하나), `GrenadeHoloPass`(홀로그램 렌더 패스), `QuickNadeSounds`(소리 시각), `GrenadesLooks`(표 줄), `GrenadesOptions`·`GrenadesOptionsRows`(설정).
- 그림: [Art/Resources/UI/Addons/Grenades](../../Assets/_Project/Art/Resources/UI/Addons/Grenades) (`frag.png` 256, `frag_arrow.png` 512, `triangle.png` 128, `holo_0~4.png` 128, 애드온 파일 그대로 풂), 셰이더 `GrenadeHolo.shader`.
- 소리: [Art/Audio/Addons/Grenades](../../Assets/_Project/Art/Audio/Addons/Grenades) (`PinPull` 3, `PinPullCloth` 6, `Throw` 1, `RattleSmall` 9, `RattleMedium` 10. 애드온 파일 그대로, 노멀라이즈 끔).

## 설정 (설정 창 > HUD)

| 원본 | 줄 | 기본 |
| --- | --- | --- |
| `cl_qg_indicator` "Grenade indicator" | 퀵 수류탄 > 수류탄 표시기 | 켬 |
| `cl_qg_fuse_indicator` "Fuse time indicator" | 퀵 수류탄 > 신관 시간 표시기 | 켬 |
| Arctic 메뉴(`grenadeindicator_menu`): 수류탄으로 칠 엔티티 이름을 더하는 목록, 기본 빈 목록 | 수류탄 표시기 목록 > 클레이모어 | 끔 |
| Arctic 메뉴 "RESET TO DEFAULT SETTINGS" | 이 탭의 초기화 | |

옮기지 않은 원본 설정: `cl_qg_camera_intensity`(뷰모델 카메라 뼈로 시야를 흔듦: 우리 손에는 그 뼈가 없다), `cl_qg_bind`(G 키: 키 설정 창), 서버 칸의 `sv_qg_weapon`·`sv_qg_trail`·`sv_qg_fuse_time`·`sv_qg_damage_mul`(게임플레이: 신관 시간은 [Grenades.csv](../../Assets/_Project/Data/Resources/Tables/Grenades/Grenades.csv)의 fuse). Arctic 목록에 더할 수 있는 것은 이 모듈이 읽을 수 있는 것 가운데 기본 목록 밖인 놓인 클레이모어뿐이다(던진 칼과 돌은 게임 코드에 읽을 목록이 없다).

## 원본과 다른 점

깜빡임 금지 규칙(부드러운 맥동, 1.6 Hz 이하)에 따른 것 둘:

- **세모**: 원본은 `clamp(sin(CurTime()*15), 0, 1)`로 초당 2.4번 켜졌다 꺼졌다. 같은 범위(알파 0.1~1)를 1.6 Hz로 부드럽게 오르내린다(`Arctic.pulseHz`).
- **홀로그램 잡음**: 원본 `ugi/holo.vmt`는 다섯 프레임을 초당 20번 끊어 넘겼다. 한 프레임이 다음 프레임으로 부드럽게 바뀌며 초당 1.6번(`Holo.frameRate`).

원본의 버그를 고친 것:

- 쿠킹 링의 시간(`m_FragSafeTime`)을 틱마다 `FrameTime()`만큼 더해 셌다. 프레임률이 틱률보다 높으면 링이 실제 신관보다 느렸다(틱률 66에서 200 fps면 약 세 배). 게임의 신관 시계를 그대로 받는다.
- 원본의 퀵 수류탄은 누르는 순간 눈 뒤 50단위에 숨긴 수류탄 엔티티를 만들어 쥐고 있어서, 쿠킹하는 동안 내 손의 수류탄이 뒤에서 오는 빨간 세열로 표시됐다. 우리 수류탄은 손을 떠나야 생기므로 그런 일이 없다.
- 세열 기록을 엔티티 번호로 두어, 흐려지는 중인 기록을 같은 번호를 받은 새 수류탄이 덮어썼다. 수류탄 자체로 찾는다.
- 수류탄 표시기를 끄면 세열 기록을 지우는 유일한 곳(`DrawFrag`)이 불리지 않아 기록이 끝없이 쌓였다. 끄면 사라진 것의 기록을 바로 지운다.
- Arctic 목록을 읽을 때 JSON을 표로 바꾸지 않아(`util.JSONToTable` 빠짐) 다음 실행에서 더한 목록이 글자열이 되어 사라지고 메뉴가 깨졌다. 설정은 settings.json에 저장된다.
- Arctic은 수류탄마다 클라이언트 모델 사본을 만들어 5초마다 치웠다. 사본 없이 수류탄 자신의 메시를 그린다.
- `Lerp(FrameTime()/t)` 꼴의 따라가기는 모두 시간 기준(1 − e^(−dt/t))으로 바꿨다. 60 fps 이상에서 같은 곡선이고 프레임이 길어도 지나치지 않는다.
- 링의 신관 0초는 원본처럼 '다 탄 것'으로 친다(Lua의 x/0 = inf가 1로 잘리던 것).
- 링 설정을 끄면 남은 진하기를 0으로 둔다(원본은 그대로 멈춰 두었다. 조준점이 비킨 채 남지 않게).

그 밖에: 두 애드온은 HUDPaint 훅 둘에서 정해진 순서 없이 그렸다. 여기서는 홀로그램 → 세모 → 링 → 세열 순이다. 세열 표시는 원본처럼 Frag만, 세모와 홀로그램은 던진 수류탄 전부(원본 기본 목록에 모든 수류탄이 있다)를 보인다. 원본의 무기 줍기 소리(`Quicknade_Pick`, HL2 소리)와 쓰지 않는 소리 파일(semtex, `grenade_pin_frag_*`, launcher·large 덜컥, `tick2.wav`: 어떤 코드·모델도 부르지 않음)은 옮기지 않았다.

## 게임과 잇는 곳

게임 코드는 이 모듈을 부르기만 한다(2026-11-22 연결).

- `PlayerGrenades.Update`(매 프레임): `HudAddons.GrenadesModule.Cook(쥔 시간, 신관)`. 쥐고 있지 않으면 −1. `OnDisable`에서 `Cook(-1f, 0f)`.
- `WeaponController.UpdateCrosshair`: `HudAddons.GrenadesModule.CrosshairHidden`이면 조준점을 숨긴다.
- 소리: 꺼내기 시작 `PullOut()`(PlayerGrenades.Begin), 핀 `PinPulled()`(PlayerGrenades.Pin), 던지기 시작 `Throw(놓기까지 초)`(StartThrow·StartTap), 도로 넣기 `PutBack()`(PutBack, OnDisable). 이 PC의 플레이어, 핀이 있는 수류탄(세열·섬광·연막·소독·염료)만. 이 PC의 플레이어는 핀과 투척의 월드 소리(Throwables.Play "Pin"·"Throw") 대신 이 소리를 듣고, 다른 PC는 그 손의 월드 소리를 그대로 듣는다.
- 모듈 설치: `HudAddons.Boot.Install`의 `GrenadesModule.Install()`.

## 확인 못 한 것

Unity를 돌리지 않고 썼다(컴파일 검사와 셰이더 문법 검사만 통과). 홀로그램이 원본 자리·크기로 보이는지(특히 렌더 타깃의 위아래 뒤집힘), 소리 시각, 세모·링·세열의 모양은 플레이에서 봐야 한다. 원본의 `render.SuppressEngineLighting(true)`는 조명 없이 텍스처 그대로로 보고 옮겼다.
