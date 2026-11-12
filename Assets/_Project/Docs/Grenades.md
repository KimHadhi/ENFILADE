# 클래스 투척물 (G)

2026-11-12 사용자: "이제 슬슬 수류탄 만들자. 클래스별로 수류탄이 달랐으면 좋겠고 gmod 호드 모드처럼 G키로 퀵 그레네이드 던질 수 있었음 좋겠어."

## 키와 던지는 법

- **G를 톡 누르면** 총을 든 채로 왼손에서 조준점 쪽으로 바로 던진다. GMod 호드의 퀵 그레네이드와 같다(호드 `sh_perk.lua` `UseQuickGrenade`: 무기를 바꾸지 않고 눈 앞 왼쪽에서 15 m/s로 던지고, 0.35초 간격, 수류탄 탄약 하나). 여기서는 누름이 `tapHold`(0.16초)보다 짧으면 뗄 때 바로 나간다.
- **G를 누르고 있으면** T 돌처럼 겨눈다. 날아갈 궤적이 그려지고, 떨어질 자리에 터지는 범위 원이 깔린다. 떼면 던진다. 궤적 그리기는 돌과 같은 [ThrowArc](../Scripts/Player/Grenades/ThrowArc.cs)를 쓴다.
- **누른 채 우클릭(막기 키)** 하면 핀을 도로 꽂는다. 아무것도 안 던지고 하나도 안 쓴다.
- **포인트의 섬광탄만 쿠킹한다.** 누르는 순간 신관이 탄다. 늦게 놓으면 공중에서 터지고, 그때는 궤적 위 터질 자리에 분홍 원이 뜬다. 끝까지 쥐면 손에서 터져 자기 둘레 좀비를 기절시킨다(포인트도 아군도 눈부시지 않다). 잡혀 있어도 타던 신관은 계속 타서 잡은 좀비를 기절시킨다.
- **돈 떨구기는 J로 옮겼다**(G가 수류탄이 됨). 둘 다 설정 → 조작 → 키 설정에서 바꿀 수 있다.
- HUD: 탄약 표시 바로 왼쪽에 투척물 판이 따로 있다(이름 · 모양 · ×개수 · [G], 쿠킹 중에는 아래 신관 막대). 판마다 제 각도(`hudYaw`·`hudRoll`). 탄약 표시가 없을 때(나이트의 주먹)는 그 모서리에 선다. 무기 슬롯 HUD는 건드리지 않는다.
- 던질 때 저절로 외친다(설정의 자동 외침이 켜져 있으면): 수류탄! · 섬광 간다! · 연막 깐다! · 소독 안개! 이 안으로!(Callouts.csv `auto.grenade.*`, 목소리는 Tools/Audio/callout_voices.py의 TTS 대역). 나이프는 조용하다.

## 다섯 가지

| 클래스 | 투척물 | 하는 일 | 세계 안의 이유 |
| --- | --- | --- | --- |
| 나이트 | 투척 나이프 (6개) | 빠르고 거의 곧게 난다(중력 0.2). 총알처럼 맞힌다(AttackTrace: 뼈 히트박스, 머리면 헤드샷 320 = 보통 좀비 한 방, 몸통 80). 맞은 자리에 칼끝부터 꽂혀 남는다: 좀비면 그 뼈를 따라다니다 쓰러지면 바닥에 떨어지고, 벽이면 벽에 박혀 있다. 걸어가서 1.6 m 안에 들면 주워진다(빈자리가 있을 때). 내 화면에서만 가끔 반짝여 찾기 쉽다. 150초 뒤 사라진다 | 기사가 허리띠에 꽂아 다니는 던지는 칼 |
| 어썰트 | 세열 수류탄 (3개) | 1.6초 뒤 6.5 m 폭발(중심 450, 끝 70). 연료통과 같은 그림체의 폭발(CanBlastEffect), 돌·흙 조각, CS:GO 고폭 소리, 거리만큼 화면 흔들림, 조용한 좀비를 부르는 소리(45 m). 중심 쪽(범위의 45%)에서 죽은 좀비는 팔다리 둘이 떨어지고 머리가 터진다. 바깥에서 죽으면 팔다리 하나, 살아남으면 다리가 떨어져 길 수도 있다. 70% 안에서 살아남은 좀비는 넘어진다(거구 제외). 시체는 위·바깥으로 날아간다. 벽·차 뒤는 안전하다. 아군 사격이 켜져 있으면 플레이어도 다친다(던진 사람은 그 몫만큼) | M67 |
| 고스트 | 연막탄 (3개) | 1.2초 뒤 5.5 m 짙은 연기가 16초. 좀비 눈길이 연기를 1.6 m 넘게 지나면 그 너머를 못 본다(조용한 좀비의 시야 광선, 앉으면 보이는 시야 콘도 연기에서 끊긴다). 깨어 있는 좀비가 쫓던 사람을 0.8초 놓치면 잃고 마지막 자리를 찾으러 간다(떼 좀비도): 다시 보거나 들어야 깬다. 그래서 연기 속에서 빠져나가 어그로를 끊거나, 수색하는 좀비를 뒤에서 암살할 수 있다. 2.2 m 안은 냄새로 찾는다. 떼 좀비는 보이는 사람을 연기 속의 더 가까운 사람보다 먼저 쫓는다 | 분대가 길을 건널 때 쓰는 군용 연막 |
| 포인트 | 섬광탄 (4개) | 0.9초의 짧은 신관, 잘 튄다(벽에 튕겨 돌려 던짐). 8 m 안에서 빛이 닿는(벽이 가리지 않는, 2.5 m 안은 무조건) 좀비는 어디를 보든 3.5초 멈추고 공격이 끊기며 자세가 무너져 처형할 수 있다. 거구는 잠깐 휘청일 뿐. 머리 위에 하얀 고리가 돈다. 플레이어는 눈이 멀지 않고, 바라보면 짧게 하얗게 번쩍일 뿐(`flashLook`) | M84. 빛과 굉음이 썩은 뇌를 흔든다 |
| 데탕트 | 소독 안개탄 (2개) | 0.8초 뒤 4.5 m 낮은 안개가 10초. 가장자리의 좀비는 기침하듯 물러서며 공격이 끊기고 밖으로 밀려난다. 0.3초 뒤부터 그 땅은 좀비의 길에서 빠진다(NavMeshObstacle 깎기). 그래서 안에서 쓰러진 동료를 일으키거나 치료할 수 있다. 안에서 누군가를 붙잡은 좀비는 놓는다. 안에 선 플레이어는 초당 4씩 낫는다. 거구는 가장자리에서 몸만 젖힌다 | 구호소의 염소 소독약: 썩은 살을 태워 좀비가 못 들어오고, 산 사람에게는 상처를 씻어 줄 뿐 |

시순트와 서포트는 아직 없다(G를 눌러도 아무 일 없음, 판도 안 뜸). 아이디어는 시안 페이지(아래)에서 고른다.

## 채우기

- 챕터를 시작할 때, 클래스를 고를 때(허브의 클래스 벽, 타이틀의 클래스 탭) 가득 찬다.
- 필드의 탄약 더미: 표의 `refill`만큼(나이프 2, 나머지 1). 총 탄약이 가득해도 투척물 칸이 비어 있으면 탄약 더미를 집을 수 있다.
- 상점: `투척물 보충` 400(Shop.csv `Grenade`, 한 개).
- 서포트의 보급: `PlayerGrenades.Resupplied(player)`가 `resupply`(1)만큼 준다. **SupportKit.Receive에 이 한 줄이 들어가야 동작한다**(클래스 파일은 클래스 세션이 맡고 있어 총관리자를 거친다).

## 네트워크

- 던진 사람의 PC가 던지고 신관을 정한다. 다른 PC는 같은 던지기를 자기 물리로 따라 던지고(PlayerNetworkCombat.GrenadeRpc), 던진 사람의 PC가 알린 자리에서 터뜨린다(GrenadeBurstRpc). 알림이 안 오면 신관 1.5초 뒤 스스로 터진다.
- 피해와 자세 무너뜨리기는 던진 사람의 PC가 좀비 사본에 주고 사본이 호스트에 전한다(총알과 같다): 처치·돈·킬 피드·어썰트 아드레날린이 던진 사람 것.
- 섬광 기절은 모든 PC가 자기 사본에 주고(각자 호스트에 알림), 연막·안개의 좀비 반응은 호스트의 좀비가 한다(모든 PC가 구름·안개를 가짐).
- 나이프: 던진 사람의 PC가 맞히고, 다른 PC의 사본은 같은 선을 날다 무언가에 꽂히며, 던진 사람이 주우면 사라진다(KnifeTakenRpc).

## 킬 피드

- 무기 이름: `grenade.<id>.name`(Texts.csv), 피해 방식 `DamageMethod.Thrown`(나이프)·`Blast`(세열) → `Throwables.FeedName`.
- 아이콘 id(무기 아이콘 세션의 WeaponIcons): `ThrowingKnife`, `GrenadeFrag`, `GrenadeSmoke`, `GrenadeFlash`, `GrenadeCanister`(Grenades.csv `icon`). 그림(Resources/UI/WeaponIcons/<모양>/<id>.png)이 없으면 세열은 폭발 그림, 나이프는 이름 글자. 모델이 [Art/Models/Addons/Grenades](../Art/Models/Addons/Grenades)에 있어 Tools/Blender/weapon_icons.py가 같은 방법으로 그릴 수 있다.

## 고치는 곳

- 수치: [Grenades.csv](../Resources/Tables/Grenades.csv)(종류마다 한 줄: 가진 클래스, 개수, 신관, 속도, 범위, 피해, 시간 …), [GrenadeRules.csv](../Resources/Tables/GrenadeRules.csv)(공통: 탭 길이, 간격, 나이프 줍는 거리, 연막 규칙, 섬광 번쩍임, 세열 고어·넘어뜨림, HUD 자리), 소리 크기는 [Sounds.csv](../Resources/Tables/Sounds.csv) `Grenades/*`.
- 코드: [Scripts/Player/Grenades](../Scripts/Player/Grenades) — PlayerGrenades(키·개수·던지기·줍기), Throwables(표·모델·소리·아이콘·킬 피드), ThrownGrenade(나는 몸·신관·튐), ThrownKnife, GrenadeBursts(종류별 터짐), FragBlast, FlashBurst(+ DazeMark, FlashLook), SmokeScreen, MistField, GrenadeHUD, ThrowArc, GrenadeLibrary.
- 좀비 쪽 연결: ZombieMind.Shown(연막이 시야 광선을 막음)·ZombieMind.Lose(연막에서 잃음), EnemyTargeting.NearestSeen(보이는 사람 먼저), StealthView.Cast(시야 콘이 연막에서 끊김).
- 모델: [GrenadeLibrary.asset](../Resources/Combat/GrenadeLibrary.asset). FBX는 ArcCW GSO(GMod 워크숍 2257255110)의 CS:GO 던진 월드 모델을 [addon_weapon_to_fbx.py](../../../Tools/Blender/addon_weapon_to_fbx.py)와 레시피 [Tools/Blender/Addons](../../../Tools/Blender/Addons)(GrenadeFrag·GrenadeFlash·GrenadeSmoke·GrenadeCanister·ThrowingKnife)로 바꿨다. 데탕트 통은 소이탄 몸통을 흰색 + 빨간 십자로 다시 칠했다(Textures/incendiary_grenade.png). 모델이 없으면 단순한 도형으로 그린다.
- 소리: Art/Audio/Grenades/<키>/ (소리 묶음 Grenades, SoundBankBuilder가 Resources/Audio/Grenades.asset으로 묶음). CS:GO의 핀·던지기·튐·고폭·섬광·연막(ArcCW GSO), 나이프 던지기는 MW Quick Throwing Knife(3650480912).
- 새 투척물: Grenades.csv에 줄(id는 `Throwable` 열거형 이름), 모델은 레시피 하나 + GrenadeLibrary에 칸, 터짐은 GrenadeBursts에 한 줄, 이름은 Texts.csv `grenade.<id>.name`.

## 시안 페이지

시순트·서포트 투척물 아이디어와 데탕트 변형 두 가지: 총관리자가 받은 링크(트리거 "수류탄 시안 봐 줘"). 고른 것은 페이지의 db `picks`에 남는다.
