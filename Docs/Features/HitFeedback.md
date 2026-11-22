# 히트 피드백 (히트 마커·점수 팝업·킬 체인)

[문서 목차](../README.md) · [UI 코드 폴더](../../Assets/_Project/Scripts/UI/README.md) · [적 체력바](EnemyBars.md) · [사망 화면](DeathScreen.md)

2026-11-22 사용자: "애드온을 넣으라고", "원본 그대로 따오되 네 디자인, 기획 센스 처넣으니까 망하는 거지", "히트 마커라는 큰 주제 하나로 묶는 게 편할 거 아니야?" 게리 모드 애드온 두 개를 한 모듈(`HudAddons.HitFeedbackModule`)로 묶어 옮겼다. 모양·수치·움직임·그림·글꼴·소리·설정은 원본 그대로이고, 코드는 새로 짰다.

- **Black Ops VI Hitmarker** (`lua/autorun/blackops6_hitmarker.lua`, 934줄): 히트 마커, 그 소리, 점수 팝업(업적 줄과 합계), 설정 패널 두 장(Main, Achievements).
- **Cool Killchains** (`lua/autorun/coolzkillchains.lua`): 조준점 아래 킬 체인 해골 줄과 그 그림, 설정(Client 칸).

## 플레이에서 보이는 것

- **맞히면** 조준점(화면 가운데)에 흰 갈래 넷이 크게 떠서 0.3초 안으로 당겨져 있다가 바깥으로 풀리며 작아지고 사라진다. 처음 0.5초는 살짝 떨린다. 헤드샷은 헤드샷용 갈래 그림, 처치는 빨강. 조준(우클릭) 중이면 갈래가 가깝게, 지향 사격이면 멀리 풀린다. 기울기는 맞힘 -2.5~2.5도, 처치 -5~5도 중 하나.
- **강철 표적처럼 살이 아닌 것**을 맞히면 마커 오른쪽 아래에 prop 그림이, **불로** 살을 맞히면 불꽃 그림이 붙는다.
- **소리**: 마크 소리 하나 + 살 소리 둘이 함께 난다. 맞힘 = `Hit.Mark_bo6`(살이 아니면 `Hit.Mark.Item`) + `Hit.Body` + `Hit.Body.Layer`, 헤드샷 = `Hit.Head.Mark_bo6` + `Hit.Head` + `Hit.Head.Layer`, 처치 = 살 소리 둘 + `Kill.Mark_bo6`(헤드샷 처치는 `Kill.Head.Mark_bo6`). 마크 소리만 음높이 80~110%.
- **처치하면** 조준점 오른쪽 위에 줄이 붙는다: 처치 방식 한 줄(폭발 처치 → 화염 처치 → 헤드샷 → 잔혹한 처치 → 처치 중 처음 맞는 것, 모두 +100), 공중에서 죽였으면 Jumpshot +25, 연속 처치 3번째 Slaughter +25, 4번째 Warrior +25, 5번째부터 God Slayer +50. 이름은 왼쪽에서 휙 들어와 살짝 되돌아오고, +점수는 크게 시작해 줄어든다. 한 줄은 3초 머물고 사라지며 아래 줄이 올라온다. 새 줄은 맨 아래에 붙는다. 셋째 줄부터 흐려지고 다섯째 줄부터는 안 보인다.
- **두 줄이 함께 서면** 노란 합계가 톡 커졌다 줄며 나타나 합으로 올라간다. 마지막 줄이 사라지면 합계도 사라지고 다음 합은 0부터.
- **킬 체인**: 처치마다 조준점 아래(화면 높이 70%)에 해골 하나가 1.75배에서 줄며 들어온다. 헤드샷은 빨간 깨진 해골, 폭발은 주황 폭발, 불은 주황 불꽃, 밀치기(발차기·쓸기·대시 밀기)는 발차기, 손에 든 칼·주먹·처형·반격·암살은 빨간 칼, 던진 물건(물리 물체)은 prop. 줄은 가운데로 다시 맞춰지고, 화면 너비의 절반을 넘으면 가장 오래된 것부터 빠진다.

## 사용자 규칙과 묶은 것

- **킬 체인은 연속 처치가 끝날 때 끝난다**(사용자: "킬 체인은 연속 처치 시간이랑 동기화 시켜야지"). 연속 처치 시계 `CombatFeedHUD.StreakWindow`(GunFeel의 Multi Kill Window 1.6초 + KillFeed.csv streakGrace 0.9초 = 2.5초, 일시정지와 상관없는 시간)를 쓴다. 원본의 "마지막 처치 뒤 4초 + 해골마다 0.33초"는 끝나는 때에만 안 쓰고, 해골이 들어오는 빠르기(decay/30)와 줄이 사라지는 빠르기(마지막 2/decay초)는 원본 그대로다.
- **"연속 몇 번째"는 하나만 센다.** 두 애드온이 각자 따로 셌다(히트 마커의 서버 kill_count는 4초, 해골은 4초+). 겹치는 것이라 하나로 묶어 같은 연속 처치 시계로 센다. 그래서 Slaughter·Warrior·God Slayer 줄도 해골 줄과 함께 끝난다.
- **처치 하나가 셋을 움직인다**: 빨간 마커와 처치 소리, 점수 줄, 해골. 처치 방식도 한 번 정한다(`HitFeedbackModule.KindOf`).
- **점수 = 플레이어의 점수·경험치**(사용자: "점수 체계도 저 애드온대로"). `HitFeedbackModule.TotalScore`가 줄마다 더한 점수의 합이고 `ResetScore()`로 0이 된다. 설정을 꺼도(켜기·점수·업적) 보이는 것만 바뀌고 점수는 쌓인다. 돈은 지금 그대로다(CombatFeedHUD).
- **연습 표적(허브 사격장)** 처치는 마커와 소리만 내고 점수·해골은 없다. 게임이 연습 처치를 연속 처치에도 점수에도 세지 않기 때문이다.

## 원본과 다른 점

고친 원본의 버그:

1. 한 프레임에 맞힘이 여럿이면(산탄, 무리) 원본은 대상마다 소리 세 개를 겹쳐 울렸고 마커는 마지막 소식을 따랐다(같은 틱에 처치 뒤 맞힘이 오면 빨간 마커가 희게 바뀌었다). 이제 한 프레임에 한 번: 처치가 맞힘보다 앞서고, 헤드샷·살·불은 그 프레임의 맞힘 중 하나라도 그러면 그렇다.
2. 점수 줄을 그리는 반복 안에서 끝난 줄을 지워 바로 다음 줄이 그 프레임에 건너뛰어졌다(줄이 빠질 때마다 한 프레임 깜박). 이제 다 그린 뒤 지운다.
3. 합계의 가로 자리(total_x)가 반복 안에서만 정해져 첫 프레임에 x + 0에 그려졌다. 이제 늘 87 x screen_scale.
4. 동시에 여럿 처치하면 해골의 나타남을 마지막 decay로 나눠, 마지막 해골만 서서히 나타나고 나머지는 툭 나타났다. 이제 해골마다 자기 decay로.
5. 킬 체인이 끝나는 검사를 해골을 그리는 도중에 했다. 이제 그리기 전에.
6. Main 패널의 "Reset to Default"가 히트 마크 크기를 빠뜨렸다. 이제 함께 되돌린다.
7. `Lerp(FrameTime() x k)`와 `math.Approach(FrameTime() x k)`는 프레임과 상관없는 같은 곡선으로(`Smooth`), 화면 크기 값(screen_scale, 흔들림 폭)은 해상도가 바뀌면 따라간다(원본은 처음 읽은 값을 끝까지 썼다). 마커의 흔들림은 원본이 매 프레임 새로 뽑아 240fps에서 60fps의 네 배로 떨렸다: 초당 60번 뽑는다(`Marker.shakeSteps`, 게리 모드의 보통 60fps에서 보던 그대로).
8. 원본은 글꼴을 다시 만들 수 없어 "Restart the map to apply"라고 적었다. 이제 글자 크기가 바로 바뀌므로 그 줄은 없다.
9. 쓰지 않던 계산(`surface.GetTextSize`, glow_w, total_life)은 뺐다.

깜박임: 두 애드온에 깜박이거나 번쩍이는 것은 없다(마커의 떨림은 0.5초 동안 몇 px 움직이는 것). 바꾼 것 없음.

이 게임에 없어 뺀 것: 장치 파괴(Device Shutdown 15)·외계 생물(Cleaner 15) 줄과 그 이름 칸(포탑·스캐너·헤드크랩 같은 적이 없다), 차로 친 처치·분해 처치 해골(car, dissolve), Cool Killchains의 Server Overrides(호스트가 모두의 HUD 설정을 정하는 것이 없다)와 Cool Hitmarkers 홍보 단추. 애드온에 들어 있으나 Lua가 한 번도 틀지 않는 소리(armor, hit, mw2019·mw2022·mw2023 폴더)와 Lua가 쓰지 않는 글꼴(opensans-regular), 그림(kick, kick3, dissolve3)은 넣지 않았다.

## 수치

모두 [HitFeedback.csv](../../Assets/_Project/Data/Resources/Tables/Hud/HitFeedback.csv)에 원본 값 그대로 있다(줄 `Marker`, `Score`, `Points`, `Chain`, `Sound`). 1080p에서 screen_scale = 0.768.

| 무엇 | 원본 | 1080p |
| --- | --- | --- |
| 마커 그림 한 변 | (50 + 남은 크기 x 40) x screen_scale x 크기 | 처음 69.1px → 38.4px |
| 갈래 쉬는 자리 | 조준 20, 지향 40 x screen_scale x 크기 | 15.4 / 30.7px |
| 당김 | 20 x screen_scale x 크기, 0.3초 | 15.4px |
| 흔들림 | ScrW x 0.0025 x (random − 0.8), 0.5초 | 최대 3.8px |
| 팝업 자리 | ScrW/2 x 1.1, ScrH/2 x 0.9 | (1056, 486) |
| 글꼴 | HelveticaNeue Condensed: +점수 32(600), 이름 30(500), 합계 40(600) x screen_scale x 글자 크기 | 24 / 23 / 30 칸 |
| 해골 | 10 x ScrW/640 x 크기, 칸 12, 높이 70%, 알파 200 | 30px, 칸 36px |

## 어디에 있나

- 코드: [Scripts/UI/Hud/Addons/HitFeedback/](../../Assets/_Project/Scripts/UI/Hud/Addons/HitFeedback) — `HitFeedbackModule`(들어오는 길, 점수, 처치 방식), `HitFeedbackHud`(오버레이와 한 프레임 한 번 확인, 소리), `HitFeedbackMarker`, `HitFeedbackScore`, `HitFeedbackChain`, `HitFeedbackLook`(표 숫자), `HitFeedbackOptions`·`HitFeedbackOptionsRows`(설정 → HUD).
- 그림: `Art/Resources/UI/Addons/HitFeedback/bo6_hitmarker/`(갈래 l1·l2·r1·r2, head/, prop, burn), `.../profiteers/`(skull, skullhs, explosion, fire, knife, kick2, propkill).
- 소리: `Art/Audio/Addons/HitFeedback/<키>/`(HitMark, HeadMark, KillMark, HeadKillMark, ItemMark, Body, BodyLayer, Head, HeadLayer) → 소리 은행 `HudAddons`.
- 글꼴: `Art/Resources/UI/Fonts/helveticaneue-condensed.ttf`(AddonFonts.csv `HelveticaNeue Condensed`).
- 들어오는 길: `HitFeedbackModule.Hit(headshot, organism, burn)`(죽이지 않은 맞힘), `HitFeedbackModule.Kill(marks, headshot, organism, counted)`(처치). `HitConfirm`이 부른다.
