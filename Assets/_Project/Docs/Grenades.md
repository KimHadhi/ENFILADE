# 클래스 투척물 (G)

2026-11-12 사용자: "이제 슬슬 수류탄 만들자. 클래스별로 수류탄이 달랐으면 좋겠고 gmod 호드 모드처럼 G키로 퀵 그레네이드 던질 수 있었음 좋겠어."

같은 날 사용자(0.8.200, GMod의 VManip 애드온 셋을 참고로 가져옴): "그냥 G 꾹 누르면 들고 있는 자세로 바뀌면서 쿠킹할 수 있고 걍 바로 누르면 콜오브듀티 퀵그레네이드처럼 바로 던지는 모션만 나오게."

## 키와 던지는 법

- **G를 톡 누르면**(`tapHold` 0.16초보다 짧게) 콜 오브 듀티의 퀵 그레네이드: 총은 오른손에 든 채 왼손이 허리에서 투척물을 꺼내 그대로 조준점 쪽으로 던지고 총으로 돌아온다. 멈추지도 궤적을 그리지도 않는다. 누른 뒤 약 0.4초에 손을 떠나고 0.7초면 끝난다(`tapDraw`·`throwSpeed`).
- **G를 꾹 누르면** 왼손이 꺼내 든 채 멈춘다(들고 있는 자세, 핀은 뽑는 소리와 함께 빠진다). 날아갈 궤적이 그려지고 떨어질 자리에 터지는 범위 원이 깔린다. 궤적은 손이 놓는 자리에서 그리므로 그려진 그대로 난다([ThrowArc](../Scripts/Player/Grenades/ThrowArc.cs), T 돌과 같은 것). 떼면 손이 젖혔다가 채 던진다.
- **들고 있을 때 우클릭(막기 키)** 하면 핀을 도로 꽂고 손이 총으로 돌아간다. 아무것도 안 던지고 하나도 안 쓴다.
- **어썰트의 세열과 포인트의 섬광탄은 쿠킹한다**: 핀을 뽑는 순간부터 신관이 타고, 남은 신관이 조준점 둘레 링으로 줄어든다(아래 HUD). 늦게 놓으면 공중에서 터지고, 그때는 궤적 위 터질 자리에 분홍 원이 뜬다.
- **절대 손해가 없다**: 세열을 끝까지 쥐고 있으면 마지막 순간 손이 저절로 던진다(`reflexLead` 0.5초를 남기고 놓아서 8 m쯤 앞 공중에서 터진다). 어썰트는 신관을 머릿속으로 세고 있어서, 시간이 다하면 훈련된 손이 알아서 놓는다. 섬광탄은 끝까지 쥐면 손에서 터져 자기 둘레 좀비를 기절시킨다(포인트도 아군도 눈부시지 않다). 잡혀 있어도 타던 섬광 신관은 계속 타서 잡은 좀비를 기절시킨다. 세열을 든 채 잡히거나 메뉴를 열면 핀이 다시 꽂힌다.
- **나이프·연막·소독통은 쿠킹하지 않는다.** 꾹 누르면 같은 자세로 들고 겨누기만 한다.
- **왼손이 던지는 동안** 총은 그 손을 못 쓴다: G를 누르면 하던 장전이 멈추고 조준(정조준)이 풀린다(밀치기와 같은 끊김, `WeaponController.InterruptForSurvival`). 오른손 사격은 그대로 된다. F를 꾹 눌러 가리키던 손도 던지기에 넘어간다(가리키기는 그 자리에서 놓고, F를 다시 눌러야 다시 가리킨다).
- **돈 떨구기는 J로 옮겼다**(G가 수류탄이 됨). 둘 다 설정 → 조작 → 키 설정에서 바꿀 수 있다.
- HUD: 탄약 표시 바로 왼쪽에 투척물 판이 따로 있다(이름 · 모양 · ×개수 · [G], 쿠킹 중에는 아래 신관 막대). 판마다 제 각도(`hudYaw`·`hudRoll`). 탄약 표시가 없을 때(나이트의 주먹)는 그 모서리에 선다. 무기 슬롯 HUD는 건드리지 않는다.
- 쿠킹 링: 쿠킹하는 것을 들고 있으면 조준점 둘레(반지름 `ringRadius` 64 px, 1080p 기준이라 조준선을 가리지 않는다)에 검은 띠와 흰 호가 뜨고, 호가 12시에서 시계 방향 반대로 줄어든다. 남은 몫이 `ringPink`(20%)보다 적으면 분홍으로 바뀌고 뛴다(판의 신관 막대도). 참고: GMod "Quick Nade Timer HUD"(반지름 64, 굵기 4, 테두리 2, 검정 75% · 흰색 · 20% 아래 빨강)를 HUD 색(검정·흰색·분홍)으로 옮겼다. 톡 던질 때는 뜨지 않는다.

## 손 동작 (왼팔)

- 동작은 GMod 애드온 [VManip] Quick Grenades가 쓰는 VManip 기본 애드온(워크숍 2155366756)의 `thrownade` 손 애니메이션(c_vmanipgrenade.mdl, 91프레임 55fps, 원본 빠르기로 1초 주기)에서 왔다. 손이 총에서 떨어져 왼쪽 아래 허리로 가고(화면 밖에서 수류탄을 쥐고), 꺼내 들어 앞에 내밀고(주기 0.4 = 들고 있는 자세, VManip `holdtime`), 핀을 뽑고(0.3), 왼쪽 위로 젖혔다가 앞으로 채 던지고(0.61에 손을 떠남, VManip은 들고 있다 놓은 지 0.2초 뒤에 던진다), 오른쪽 아래로 따라 휘두른 뒤 총으로 돌아간다(0.85부터, VManip `lerp_peak`).
- 클립을 옮기지 않고 팔이 하는 일만 표로 뽑았다([GrenadeArm.csv](../Resources/Tables/GrenadeArm.csv), [vmanip_throw_path.py](../../../Tools/Blender/vmanip_throw_path.py)): 어깨에서 손목·팔꿈치까지(팔 길이의 배수), 손이 뻗은 쪽과 손바닥이 향한 쪽, 손가락·엄지가 굽은 정도, 손에 투척물이 있는지. 모두 시야 기준(x 오른쪽 · y 위 · z 앞). [GrenadeArm](../Scripts/Player/Grenades/GrenadeArm.cs)이 이것을 그 몸의 어깨·팔 길이로 옮겨(TwoBoneIK) 손을 돌리고 손가락을 감는다. 그래서 어떤 휴머노이드(모드 캐릭터 포함)도 굽기나 클립 없이 같은 동작을 한다.
- 손에 쥔 투척물은 그 종류의 모델이 손바닥에 앉는다(Grenades.csv `heldAcross`·`heldPalm`·`heldAlong` 손 길이의 배수, `heldTurnX/Y/Z`). 수류탄은 신관 머리가 엄지 쪽, 나이프는 손잡이를 쥐고 날이 엄지 쪽으로 나온다.
- 친구 화면에서도 보인다: 내 PC가 손의 단계(꺼내기·던지기·톡 던지기·도로 넣기)를 보내고(PlayerNetworkCombat.GrenadeHandRpc), 친구 PC의 내 사본이 같은 길을 같은 빠르기로 간다. 투척물 자체는 예전처럼 GrenadeRpc로 손이 놓는 자리에서 날아간다.
- 왼손은 모든 왼손 동작이 쓰는 한 입구로 빌린다(총관리자 결정): `PlayerBodyHands.LendOffHand(this, 1)`을 던지는 동안 부르고 끝나면 0. 그러면 왼손의 총 접점·손잡이·총 손가락이 `Hands.blendTime`(0.15초)에 풀리고 끝나면 다시 총을 잡는다. 던지는 자세는 풀린 만큼(`OffHandLent`)을 넘지 않게 섞는다.
- 순서: FPS Animation Framework·총 쥐는 손·콜아웃 몸짓·F 가리키기(PointArm, GunFramework + 6) 뒤인 GunFramework + 7에 돌고, 옷(Cloth) 앞이라 소매가 따라온다. 1인칭 카메라는 그 전에 놓여 시야는 팔을 따라 움직이지 않는다.
- 던질 때 저절로 외친다(설정의 자동 외침이 켜져 있으면): 수류탄! · 섬광 간다! · 연막 깐다! · 소독 안개! 이 안으로!(Callouts.csv `auto.grenade.*`, 목소리는 Tools/Audio/callout_voices.py의 TTS 대역). 나이프는 조용하다.

## 다섯 가지

| 클래스 | 투척물 | 하는 일 | 세계 안의 이유 |
| --- | --- | --- | --- |
| 나이트 | 투척 나이프 (6개) | 빠르고 거의 곧게 난다(중력 0.2). 총알처럼 맞힌다(AttackTrace: 뼈 히트박스, 머리면 헤드샷 320 = 보통 좀비 한 방, 몸통 80). 맞은 자리에 칼끝부터 꽂혀 남는다: 좀비면 그 뼈를 따라다니다 쓰러지면 바닥에 떨어지고, 벽이면 벽에 박혀 있다. 걸어가서 1.6 m 안에 들면 주워진다(빈자리가 있을 때). 내 화면에서만 가끔 반짝여 찾기 쉽다. 150초 뒤 사라진다 | 기사가 허리띠에 꽂아 다니는 던지는 칼 |
| 어썰트 | 세열 수류탄 (3개) | 핀을 뽑고 2초 뒤(쿠킹, 0.8.200 전에는 던진 뒤 1.6초) 6.5 m 폭발(중심 450, 끝 70). 연료통과 같은 그림체의 폭발(CanBlastEffect), 돌·흙 조각, CS:GO 고폭 소리, 거리만큼 화면 흔들림, 조용한 좀비를 부르는 소리(45 m). 중심 쪽(범위의 45%)에서 죽은 좀비는 팔다리 둘이 떨어지고 머리가 터진다. 바깥에서 죽으면 팔다리 하나, 살아남으면 다리가 떨어져 길 수도 있다. 70% 안에서 살아남은 좀비는 넘어진다(거구 제외). 시체는 위·바깥으로 날아간다. 벽·차 뒤는 안전하다. 아군 사격이 켜져 있으면 플레이어도 다친다(던진 사람은 그 몫만큼) | M67 |
| 고스트 | 연막탄 (3개) | 1.2초 뒤 5.5 m 짙은 연기가 16초. 좀비 눈길이 연기를 1.6 m 넘게 지나면 그 너머를 못 본다(조용한 좀비의 시야 광선, 앉으면 보이는 시야 콘도 연기에서 끊긴다). 깨어 있는 좀비가 쫓던 사람을 0.8초 놓치면 잃고 마지막 자리를 찾으러 간다(떼 좀비도): 다시 보거나 들어야 깬다. 그래서 연기 속에서 빠져나가 어그로를 끊거나, 수색하는 좀비를 뒤에서 암살할 수 있다. 2.2 m 안은 냄새로 찾는다. 떼 좀비는 보이는 사람을 연기 속의 더 가까운 사람보다 먼저 쫓는다 | 분대가 길을 건널 때 쓰는 군용 연막 |
| 포인트 | 섬광탄 (4개) | 0.9초의 짧은 신관, 잘 튄다(벽에 튕겨 돌려 던짐). 8 m 안에서 빛이 닿는(벽이 가리지 않는, 2.5 m 안은 무조건) 좀비는 어디를 보든 3.5초 멈추고 공격이 끊기며 자세가 무너져 처형할 수 있다. 거구는 잠깐 휘청일 뿐. 머리 위에 하얀 고리가 돈다. 플레이어는 눈이 멀지 않고, 바라보면 짧게 하얗게 번쩍일 뿐(`flashLook`) | M84. 빛과 굉음이 썩은 뇌를 흔든다 |
| 데탕트 | 소독 안개탄 (2개) | 0.8초 뒤 4.5 m 낮은 안개가 10초. 가장자리의 좀비는 기침하듯 물러서며 공격이 끊기고 밖으로 밀려난다. 0.3초 뒤부터 그 땅은 좀비의 길에서 빠진다(NavMeshObstacle 깎기). 그래서 안에서 쓰러진 동료를 일으키거나 치료할 수 있다. 안에서 누군가를 붙잡은 좀비는 놓는다. 안에 선 플레이어는 초당 4씩 낫는다. 거구는 가장자리에서 몸만 젖힌다 | 구호소의 염소 소독약: 썩은 살을 태워 좀비가 못 들어오고, 산 사람에게는 상처를 씻어 줄 뿐 |

시순트와 서포트는 아직 없다(G를 눌러도 아무 일 없음, 판도 안 뜸). 아이디어는 시안 페이지(아래)에서 고른다.

## 채우기

- 챕터를 시작할 때, 클래스를 고를 때(허브의 클래스 벽, 타이틀의 클래스 탭) 가득 찬다.
- 필드의 탄약 더미: 표의 `refill`만큼(나이프 2, 나머지 1). 총 탄약이 가득해도 투척물 칸이 비어 있으면 탄약 더미를 집을 수 있다.
- 상점: `투척물 보충` 400(Shop.csv `Grenade`, 한 개).
- 서포트의 보급과 탄약 상자: `PlayerGrenades.Resupplied(player)`가 `resupply`(1)만큼 준다(SupportKit.Receive, 0.8.179).

## 네트워크

- 던진 사람의 PC가 던지고 신관을 정한다. 다른 PC는 같은 던지기를 자기 물리로 따라 던지고(PlayerNetworkCombat.GrenadeRpc), 던진 사람의 PC가 알린 자리에서 터뜨린다(GrenadeBurstRpc). 알림이 안 오면 신관 1.5초 뒤 스스로 터진다.
- 피해와 자세 무너뜨리기는 던진 사람의 PC가 좀비 사본에 주고 사본이 호스트에 전한다(총알과 같다): 처치·돈·킬 피드·어썰트 아드레날린이 던진 사람 것.
- 섬광 기절은 모든 PC가 자기 사본에 주고(각자 호스트에 알림), 연막·안개의 좀비 반응은 호스트의 좀비가 한다(모든 PC가 구름·안개를 가짐).
- 나이프: 던진 사람의 PC가 맞히고, 다른 PC의 사본은 같은 선을 날다 무언가에 꽂히며, 던진 사람이 주우면 사라진다(KnifeTakenRpc).

## 킬 피드

- 무기 이름: `grenade.<id>.name`(Texts.csv), 피해 방식 `DamageMethod.Thrown`(나이프)·`Blast`(세열) → `Throwables.FeedName`.
- 아이콘 id(무기 아이콘 세션의 WeaponIcons): `ThrowingKnife`, `GrenadeFrag`, `GrenadeSmoke`, `GrenadeFlash`, `GrenadeCanister`(Grenades.csv `icon`). 그림(Resources/UI/WeaponIcons/<모양>/<id>.png)이 없으면 세열은 폭발 그림, 나이프는 이름 글자. 모델이 [Art/Models/Addons/Grenades](../Art/Models/Addons/Grenades)에 있어 Tools/Blender/weapon_icons.py가 같은 방법으로 그릴 수 있다.

## 고치는 곳

- 수치: [Grenades.csv](../Resources/Tables/Grenades.csv)(종류마다 한 줄: 가진 클래스, 개수, 신관·쿠킹·끝까지 쥐면(`cookOut`), 속도, 범위, 피해, 시간, 손에 쥔 자리 …), [GrenadeRules.csv](../Resources/Tables/GrenadeRules.csv)(공통: 탭 길이, 손 빠르기(`tapDraw`·`holdDraw`·`throwSpeed`), 손 궤적의 순간(`pin`·`hold`·`release`·`back`·`end`), 손이 들어가고 나오는 시간(`armIn`·`armOut`), 반사 투척(`reflexLead`), 쥐는 손가락(`gripCurl`·`gripThumb`), 쿠킹 링(`ring*`), 간격, 나이프 줍는 거리, 연막 규칙, 섬광 번쩍임, 세열 고어·넘어뜨림, HUD 자리), 손 궤적 [GrenadeArm.csv](../Resources/Tables/GrenadeArm.csv), 소리 크기는 [Sounds.csv](../Resources/Tables/Sounds.csv) `Grenades/*`.
- 코드: [Scripts/Player/Grenades](../Scripts/Player/Grenades) — PlayerGrenades(키·개수·손의 단계·쿠킹·반사 투척·던지기·줍기), GrenadeArm(왼팔을 궤적대로, 손에 쥔 모델), GrenadeArmPath(궤적 표 읽기), GrenadeCookRing(조준점 둘레 신관 링), Throwables(표·모델·소리·아이콘·킬 피드), ThrownGrenade(나는 몸·신관·튐), ThrownKnife, GrenadeBursts(종류별 터짐), FragBlast, FlashBurst(+ DazeMark, FlashLook), SmokeScreen, MistField, GrenadeHUD, ThrowArc, GrenadeLibrary.
- 다른 손 동작과: `PlayerGrenades.HandBusy(player)`가 참인 동안(꺼내기~돌아가기) 왼손은 던지기 것이다. F 가리키기(PointArm·PlayerPoint, 0.8.208)는 이것을 보고 놓는다.
- 좀비 쪽 연결: ZombieMind.Shown(연막이 시야 광선을 막음)·ZombieMind.Lose(연막에서 잃음), EnemyTargeting.NearestSeen(보이는 사람 먼저), StealthView.Cast(시야 콘이 연막에서 끊김).
- 모델: [GrenadeLibrary.asset](../Resources/Combat/GrenadeLibrary.asset). FBX는 ArcCW GSO(GMod 워크숍 2257255110)의 CS:GO 던진 월드 모델을 [addon_weapon_to_fbx.py](../../../Tools/Blender/addon_weapon_to_fbx.py)와 레시피 [Tools/Blender/Addons](../../../Tools/Blender/Addons)(GrenadeFrag·GrenadeFlash·GrenadeSmoke·GrenadeCanister·ThrowingKnife)로 바꿨다. 데탕트 통은 소이탄 몸통을 흰색 + 빨간 십자로 다시 칠했다(Textures/incendiary_grenade.png). 모델이 없으면 단순한 도형으로 그린다.
- 소리: Art/Audio/Grenades/<키>/ (소리 묶음 Grenades, SoundBankBuilder가 Resources/Audio/Grenades.asset으로 묶음). CS:GO의 핀·던지기·튐·고폭·섬광·연막(ArcCW GSO), 나이프 던지기는 MW Quick Throwing Knife(3650480912).
- 새 투척물: Grenades.csv에 줄(id는 `Throwable` 열거형 이름), 모델은 레시피 하나 + GrenadeLibrary에 칸, 터짐은 GrenadeBursts에 한 줄, 이름은 Texts.csv `grenade.<id>.name`.

## 시안 페이지

시순트·서포트 투척물 아이디어와 데탕트 변형 두 가지: 총관리자가 받은 링크(트리거 "수류탄 시안 봐 줘"). 고른 것은 페이지의 db `picks`에 남는다.
