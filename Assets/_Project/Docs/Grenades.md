# 클래스 투척물 (G)

2026-11-12 사용자: "이제 슬슬 수류탄 만들자. 클래스별로 수류탄이 달랐으면 좋겠고 gmod 호드 모드처럼 G키로 퀵 그레네이드 던질 수 있었음 좋겠어."

같은 날 사용자(0.8.200, GMod의 VManip 애드온 셋을 참고로 가져옴): "그냥 G 꾹 누르면 들고 있는 자세로 바뀌면서 쿠킹할 수 있고 걍 바로 누르면 콜오브듀티 퀵그레네이드처럼 바로 던지는 모션만 나오게."

## 키와 던지는 법

- **G를 톡 누르면**(`tapHold` 0.16초보다 짧게) 콜 오브 듀티의 퀵 그레네이드: 총은 오른손에 든 채 왼손이 허리에서 투척물을 꺼내 그대로 조준점 쪽으로 던지고 총으로 돌아온다. 멈추지도 궤적을 그리지도 않는다. 누른 뒤 0.3초에 손을 떠나고 0.6초면 끝난다(톡 던지기 클립 [GrenadeTap.csv](../Resources/Tables/GrenadeTap.csv)의 놓기 표시).
- **G를 꾹 누르면** 왼손이 꺼내 든 채 멈춘다(들고 있는 자세, 핀은 뽑는 소리와 함께 빠진다). 날아갈 궤적이 그려지고 떨어질 자리에 터지는 범위 원이 깔린다. 궤적은 손이 놓는 자리에서 그리므로 그려진 그대로 난다([ThrowArc](../Scripts/Player/Grenades/ThrowArc.cs), T 돌과 같은 것). 떼면 손이 젖혔다가 채 던진다.
- **들고 있을 때 우클릭(막기 키)** 하면 핀을 도로 꽂고 손이 총으로 돌아간다. 아무것도 안 던지고 하나도 안 쓴다.
- **어썰트의 세열과 포인트의 섬광탄은 쿠킹한다**: 핀을 뽑는 순간부터 신관이 타고, 남은 신관이 조준점 둘레 링으로 줄어든다(아래 HUD). 늦게 놓으면 공중에서 터지고, 그때는 궤적 위 터질 자리에 분홍 원이 뜬다.
- **절대 손해가 없다**: 세열을 끝까지 쥐고 있으면 마지막 순간 손이 저절로 던진다(`reflexLead` 0.5초를 남기고 놓아서 8 m쯤 앞 공중에서 터진다). 어썰트는 신관을 머릿속으로 세고 있어서, 시간이 다하면 훈련된 손이 알아서 놓는다. 섬광탄은 끝까지 쥐면 손에서 터져 자기 둘레 좀비를 기절시킨다(포인트도 아군도 눈부시지 않다). 잡혀 있어도 타던 섬광 신관은 계속 타서 잡은 좀비를 기절시킨다. 세열을 든 채 잡히거나 메뉴를 열면 핀이 다시 꽂힌다.
- **나이프·연막·소독통·염료탄은 쿠킹하지 않는다.** 꾹 누르면 같은 자세로 들고 겨누기만 한다.
- **서포트의 지뢰는 던지지 않고 놓는다**(사용자: "던지는 느낌보단 설치로 가도 되긴 하겠다"). 꾹 누르면 놓을 자리(조준점이 가슴에서 `claymoreReach` 3 m 안이면 그 바닥이나 벽, 아니면 한 걸음 앞 바닥)와 지킬 부채꼴(분홍 선, `claymoreCone` 60° · `claymoreTrigger` 4.5 m)이 보인다. 떼거나 톡 누르면 손이 거기에 놓는다. 놓을 데가 없으면 판이 흔들리고 아무것도 안 쓴다.
- **왼손이 던지는 동안** 총은 그 손을 못 쓴다: G를 누르면 하던 장전이 멈추고 조준(정조준)이 풀린다(밀치기와 같은 끊김, `WeaponController.InterruptForSurvival`). 오른손 사격은 그대로 된다. F를 꾹 눌러 가리키던 손도 던지기에 넘어간다(가리키기는 그 자리에서 놓고, F를 다시 눌러야 다시 가리킨다).
- **돈 떨구기는 J로 옮겼다**(G가 수류탄이 됨). 둘 다 설정 → 조작 → 키 설정에서 바꿀 수 있다.
- HUD: 탄약 표시 바로 왼쪽에 투척물 판이 따로 있다(이름 · 모양 · ×개수 · [G], 쿠킹 중에는 아래 신관 막대). 판마다 제 각도(`hudYaw`·`hudRoll`). 탄약 표시가 없을 때(나이트의 주먹)는 그 모서리에 선다. 무기 슬롯 HUD는 건드리지 않는다.
- 쿠킹 링: 쿠킹하는 것을 들고 있으면 조준점 둘레(반지름 `ringRadius` 64 px, 1080p 기준이라 조준선을 가리지 않는다)에 검은 띠와 흰 호가 뜨고, 호가 12시에서 시계 방향 반대로 줄어든다. 남은 몫이 `ringPink`(20%)보다 적으면 분홍으로 바뀌고 뛴다(판의 신관 막대도). 참고: GMod "Quick Nade Timer HUD"(반지름 64, 굵기 4, 테두리 2, 검정 75% · 흰색 · 20% 아래 빨강)를 HUD 색(검정·흰색·분홍)으로 옮겼다. 톡 던질 때는 뜨지 않는다.

## 손 동작 (왼팔)

- **클립 넷을 블렌더에서 손본다**(0.8.276, 사용자: "수류탄 던지는 모션도 아쉬운데 드는 모션, 대기 모션, 던지는 모션 내가 블렌더로 수정할 수 있게 만들어서 적용해 봐"): 손 동작마다 꺼내기·대기(되풀이)·던지기·톡 던지기 클립이 있다. [블렌더 투척 작업실](../../../ArtSource/Grenades/START_HERE.md)(열면 마지막 클립과 왼손이 골라져 있다. 키포즈 클립은 키를 바로 고치고, 프레임마다 키가 있는 나이프 꺼내기·던지기만 잠긴 시안 위 손질 층. 저장(Ctrl+S)하면 바뀐 클립만 보낸다. 공용 부분은 Tools/Blender/OneClick)에서 고치고 저장하면 클립 표가 바뀌고, Play 중이어도 다음 G부터 그 동작이 나온다.
  - 수류탄(연막·섬광·소독통·염료·지뢰도 같이 씀) 1차 시안, 30fps: **꺼내기** 0.433초(1 총 → 4 허리 주머니 → 6 쥐기 → 9 총 앞으로 올라옴 → 11 핀 고리를 총 쥔 오른손 엄지에 걸고 → 12(0.367초) **핀**: 왼쪽으로 당겨 뽑음, 소리와 쿠킹 시작 → 14 대기 자세), **대기** 1.2초 되풀이(화면 왼쪽 아래에서 손을 세워 감싸 쥐고 4mm 숨쉬기), **던지기** 0.6초(3 살짝 낮춤 → 5 머리 옆으로 젖힘 → 7 채기 → 8(0.233초) **놓기**: 조준점 쪽으로 뻗으며 손가락을 폄 → 10 아래로 따라 휘두름 → 19 총), **톡 던지기** 0.6초(3 주머니 → 4 쥐기 → 6(0.167초) 핀 → 8 젖힘 → 10(0.3초) 놓기 → 19 총). 예전 VManip `thrownade`(0.8.211)는 버렸다.
  - 클립 표([GrenadeDraw](../Resources/Tables/GrenadeDraw.csv)·[GrenadeHold](../Resources/Tables/GrenadeHold.csv)·[GrenadeThrow](../Resources/Tables/GrenadeThrow.csv)·[GrenadeTap](../Resources/Tables/GrenadeTap.csv), 나이프 [KnifeDraw](../Resources/Tables/KnifeDraw.csv)·[KnifeHold](../Resources/Tables/KnifeHold.csv)·[KnifeThrow](../Resources/Tables/KnifeThrow.csv))는 블렌더가 반 프레임마다 쓴다: 눈에서 손목·팔꿈치까지(m), 손이 뻗은 쪽·손바닥이 향한 쪽, 손가락마다 굽은 정도(`curlIndex`·`curlMiddle`·`curlRing`·`curlLittle`)와 엄지, 손에 있는지(`held`: 쥐기~놓기 전), 순간(`pin`·`release` 1인 줄 = 타임라인의 핀·놓기 표시). 모두 머리 카메라 기준(x 오른쪽 · y 위 · z 앞). [GrenadeArm](../Scripts/Player/Grenades/GrenadeArm.cs)이 이것을 그 몸에 맞춘다(TwoBoneIK로 손목·팔꿈치, 손을 돌리고 손가락마다 감는다). 그래서 어떤 휴머노이드(모드 캐릭터 포함)도 굽기 없이 같은 동작을 한다.
  - 흐름([PlayerGrenades](../Scripts/Player/Grenades/PlayerGrenades.cs)): G를 누르면 꺼내기 → 꾹 누르고 있으면 대기를 되풀이 → 떼면 던지기, 놓기 순간에 손을 떠난다. 톡 누르면 톡 던지기 클립이 꺼내기가 간 데서 이어받는다. 클립이 바뀔 때 앞 자세에서 섞는다(`fade`). 핀 표시가 없어도 꺼내기가 끝날 때(톡 던지기는 놓을 때) 핀을 뽑아, 핀 꽂힌 수류탄이 날아가는 일은 없다.
- 손에 쥔 투척물은 그 종류의 모델이 손에 앉는다(Grenades.csv `heldAcross`·`heldPalm`·`heldAlong` 손 길이의 배수 = 주먹이 쥐는 자리, `heldTurnX/Y/Z`, 모델 위의 쥐는 점 `gripX/Y/Z` m). 0.8.276에 수류탄들은 손바닥 가운데로(0.12·0.6·0.9 → 0.05·0.62·0.62): 앞은 손가락 앞에 떠서 옆에서 집은 것처럼 보였다. 블렌더 작업실의 미리보기도 이 숫자로 쥐여 둔다.
- **나이트의 투척 나이프는 제 손 동작이 따로 있다**(사용자: "throwing knife는 gmod에 있는 느낌대로 한 거 맞지?", 0.8.219): GMod 애드온 [MW] Quick Throwing Knife(워크숍 3650480912)의 MW2019 투척 나이프 뷰모델 `c_eq_knife.mdl`에서 꺼내기(draw)와 던지기(throw)를 [hand_path.py](../../../Tools/Blender/hand_path.py)와 레시피 [HandPaths/KnifeArm.json](../../../Tools/Blender/HandPaths/KnifeArm.json)으로 뽑아 [KnifeArm.csv](../Resources/Tables/KnifeArm.csv)에 넣었다. 원본은 오른손으로 던진다(총을 집어넣는다). 여기서는 좌우를 뒤집어 왼손이 던지고, 총은 오른손에 그대로 있다. 꾹 누르면 원본의 꺼내기 그대로 0.6초에 낮게 쥐고 대기하고(꺼내는 소리: 칼날 + 소매), 떼면 원본처럼 1.5배속으로 왼쪽 위로 젖혔다가 앞으로 채서 0.27초 뒤 손가락이 펴지며 날아간다. 톡 누르면 꺼내기를 거의 건너뛰어 0.42초에 나간다(원본은 떼고 약 0.4초). 던질 때 칼날·소매 소리, 시야가 2° 아래로 튀고 조금 기운다(원본 ViewPunch). 날아가는 나이프는 원본처럼 1초에 3000° 돌고 하얀 줄이 0.3초 남는다(`knifeSpin`·`knifeTrail`). 좀비에 꽂히는 소리와 벽 소리는 원본과 같은 Far Cry 4 `knife_stab`·`knife_hit`이다. 원본과 일부러 다르게 둔 것: 헤드샷 320(보통 좀비 한 방, 총 규칙과 같게, 원본은 어디든 80), 다시 줍기(원본은 5초 뒤 사라짐), 거의 곧은 비행(중력 0.2, 원본은 온 중력).
- **2차(사용자: "나이트의 퀵그레네이드인 쓰로잉 나이프가 전혀 내가 Gmod에서 보던 쓰로잉 나이프 같지 않아. 그대로 시원하고 빠르고 찰진(타격감 있는) 느낌이면 좋겠는데.")**: 왜 달랐나와 무엇을 바꿨나.
  - 보이지 않았다: 궤적을 어깨에서 팔 길이로 옮겨서 들고 있는 나이프가 화면 아래 약 35~40°(우리 세로 화각은 ±29°)에 있었다. 이제 KnifeArm은 눈에서 미터로 잰 뷰모델 자리 그대로다(ThrowArms `space` eye). 원본 뷰모델의 세로 화각(`viewFov` 50.2°, GMod ViewModelFOV 64)에서 이 게임 화각으로 늘려, 나이프가 GMod처럼 화면 아래쪽에 보인다.
  - 느렸다: 톡 누른 뒤 0.42초 → 약 0.27초에 놓는다(ThrowArms knife `tapDraw` 10, `throwSpeed` 2). 꾹 누를 때 꺼내기 0.6초 → 0.375초(`holdDraw` 1.6). 다음 나이프까지 약 0.7초 → 0.5초. 날아가는 빠르기 36 → 50 m/s(애드온 2000유닛/초 ≈ 50.8 m/s).
  - 찰지지 않았다: 좀비 움찔(EnemyHitReaction)이 총알만 받아서 나이프에 맞아도 움찔하지 않았다. 이제 나이프도 총알처럼 움찔시킨다(뒤로 밀림·느려짐·헤드샷이면 잠깐 멈춤, `knifeFlinch` 1.5). 피도 총알만큼(0.6배 근접 피 → 총알 피와 핏자국), 킬 피드 헤드샷 해골, 총 처치의 처치 연출(GunFeelDirector).
  - 시야: 놓을 때 2.5° 아래로 튀고 2° 기울었다가 0.08초 뒤 되돌아오며 짧게 흔들린다(`knifeKick`·`knifeRoll`, 애드온 ViewPunch 2 + 뷰모델 카메라 ×1.5 + 0.15초 흔들림).
  - 그대로인 것: 1초 3000° 회전, 0.3초 하얀 줄, FC4 꽂힘 소리, 꽂힌 데 남기, 다시 줍기(애드온은 5초 뒤 사라짐), 헤드샷 320, 거의 곧은 비행(중력 0.2배, 애드온은 GMod 중력 600유닛/초² ≈ 1.55배).
- **3차: 모델과 동작을 애드온 그대로(0.8.276, 사용자가 고른 "2번": 총은 오른손에 든 채 원본 동작을 거울로 왼손에)**. 2차까지는 MW 뷰모델에서 손목·팔꿈치가 지나는 길만 뽑아 손가락은 '얼마나 쥐었나' 하나로 근사했고, 모델은 CS:GO 투척 나이프였다.
  - 모델: 애드온의 MW2019 투척 나이프 `w_eq_knife_thrown.mdl`([MWThrowingKnife.json](../../../Tools/Blender/Addons/MWThrowingKnife.json) → Art/Models/Addons/Grenades/MWThrowingKnife, 21.1 cm, 원본 텍스처). 뷰모델 속 나이프와 같은 메시라(3201점, 맞춤 오차 0) 손에 쥔 자리도 원본 그대로 잰다. MW는 나이프를 칼끝 가까이 쥔다(`grip`: 가운데에서 칼끝 쪽 9 cm).
  - 동작: 블렌더 작업실이 `@draw`(19프레임)·`@throw`(26프레임)를 프레임마다 뼈 그대로 옮긴다: 오른팔을 왼팔로 뒤집어 손목 자리, 팔꿈치 방향, 손의 돌림, 손가락 마디마다의 방향. 크기는 이 프로젝트의 무기 단위(0.01905 m)로, 이때 MW 손과 팔이 카즈사와 거의 같다. 원본 화각 50.2°에서 머리 카메라 58.7°(게임 기본)로 늘려 GMod에서 보던 화면 자리 그대로다. 대기는 원본 `@idle`(가만히 든 자세)에 숨쉬기만 더했다.
  - 빠르기: 꺼내기 1배(0.3초에 든 자세), 던지기 1.5배(애드온의 SetPlaybackRate) = 놓기 0.222초·끝 0.556초, 클립 사이 섞기 0.05초(원본 시퀀스 페이드). 톡 누르면 애드온처럼 바로 던지기를 처음부터 재생한다(`tap` = KnifeThrow).
  - 날아가는 나이프가 칼끝부터: 코드는 모델의 칼끝을 +Z로 봤지만 Art/Models 가져오기는 레시피의 칼끝을 −Z에 둔다(데스페라도에서 잰 반 바퀴). 그래서 예전 CS:GO 나이프도 손잡이부터 꽂혔을 수 있다. 이제 ThrownKnife가 모델의 `Tip` 표시에서 칼끝을 찾아 그쪽을 앞으로 돌리고, 꽂힐 때 칼끝 길이(MW 10.6 cm)만큼 앞에서 멈춘다. 모드 나이프도 Tip 표시만 있으면 된다.
- 손 동작마다 클립 넷과 빠르기는 [ThrowArms.csv](../Resources/Tables/ThrowArms.csv)에 한 줄씩 있다(`grenade`, `knife`): `draw`·`hold`·`throw`·`tap` 클립 표, `drawSpeed`·`tapSpeed`·`throwSpeed`(배수), `fade`(섞는 초), `space`(eye: 눈에서 미터), `viewFov`(클립을 만든 카메라의 세로 화각 58.72°: 시야각을 넓히면 내 화면에서만 손이 그만큼 바깥으로 가 같은 자리에 보인다. 0이면 안 옮김). `tap`을 비우면 꺼내기를 `tapSpeed`로 빨리 재생한 뒤 던진다. Grenades.csv `arm`이 줄을 고르고 비우면 `grenade`. 새 동작은 블렌더 작업실에 클립을 더하고(grenade_tools.py `MOVES`) 줄을 하나 더하면 된다.
- 친구 화면에서도 보인다: 내 PC가 손의 단계(꺼내기·던지기·톡 던지기·도로 넣기)를 보내고(PlayerNetworkCombat.GrenadeHandRpc), 친구 PC의 내 사본이 같은 클립을 같은 빠르기로 재생한다(3인칭도 같은 몸의 팔이다). 투척물 자체는 예전처럼 GrenadeRpc로 손이 놓는 자리에서 날아간다.
- 왼손은 모든 왼손 동작이 쓰는 한 입구로 빌린다(총관리자 결정): `PlayerBodyHands.LendOffHand(this, 1)`을 던지는 동안 부르고 끝나면 0. 그러면 왼손의 총 접점·손잡이·총 손가락이 `Hands.blendTime`(0.15초)에 풀리고 끝나면 다시 총을 잡는다. 던지는 자세는 풀린 만큼(`OffHandLent`)을 넘지 않게 섞는다.
- 순서: FPS Animation Framework·총 쥐는 손·콜아웃 몸짓·F 가리키기(PointArm, GunFramework + 6) 뒤인 GunFramework + 7에 돌고, 옷(Cloth) 앞이라 소매가 따라온다. 1인칭 카메라는 그 전에 놓여 시야는 팔을 따라 움직이지 않는다.
- 던질 때 저절로 외친다(설정의 자동 외침이 켜져 있으면): 수류탄! · 섬광 간다! · 연막 깐다! · 소독 안개! 이 안으로!(Callouts.csv `auto.grenade.*`, 목소리는 Tools/Audio/callout_voices.py의 TTS 대역). 나이프는 조용하다.

## 일곱 가지

| 클래스 | 투척물 | 하는 일 | 세계 안의 이유 |
| --- | --- | --- | --- |
| 나이트 | 투척 나이프 (6개) | 손 동작은 GMod [MW] Quick Throwing Knife 그대로(위). 빠르고 거의 곧게 난다(중력 0.2), 1초에 3000° 돌며 하얀 줄을 남긴다. 총알처럼 맞힌다(AttackTrace: 뼈 히트박스, 머리면 헤드샷 320 = 보통 좀비 한 방, 몸통 80). 맞은 자리에 칼끝부터 꽂혀 남는다: 좀비면 그 뼈를 따라다니다 쓰러지면 바닥에 떨어지고, 벽이면 벽에 박혀 있다. 걸어가서 1.6 m 안에 들면 주워진다(빈자리가 있을 때). 내 화면에서만 가끔 반짝여 찾기 쉽다. 150초 뒤 사라진다 | 기사가 허리띠에 꽂아 다니는 던지는 칼 |
| 어썰트 | 세열 수류탄 (3개) | 핀을 뽑고 2초 뒤(쿠킹, 0.8.200 전에는 던진 뒤 1.6초) 6.5 m 폭발(중심 450, 끝 70). Realistic Gunslinger 팩의 세열 폭발(팩이 꺼지면 연료통과 같은 그림체의 CanBlastEffect), 돌·흙 조각, CS:GO 고폭 소리, 거리만큼 화면 흔들림, 조용한 좀비를 부르는 소리(45 m). 중심 쪽(범위의 45%)에서 죽은 좀비는 팔다리 둘이 떨어지고 머리가 터진다. 바깥에서 죽으면 팔다리 하나, 살아남으면 다리가 떨어져 길 수도 있다. 70% 안에서 살아남은 좀비는 넘어진다(거구 제외). 시체는 위·바깥으로 날아간다. 벽·차 뒤는 안전하다. 아군 사격이 켜져 있으면 플레이어도 다친다(던진 사람은 그 몫만큼) | M67 |
| 고스트 | 연막탄 (3개) | 1.2초 뒤 5.5 m 짙은 연기가 16초. 좀비 눈길이 연기를 1.6 m 넘게 지나면 그 너머를 못 본다(조용한 좀비의 시야 광선, 앉으면 보이는 시야 콘도 연기에서 끊긴다). 깨어 있는 좀비가 쫓던 사람을 0.8초 놓치면 잃고 마지막 자리를 찾으러 간다(떼 좀비도): 다시 보거나 들어야 깬다. 그래서 연기 속에서 빠져나가 어그로를 끊거나, 수색하는 좀비를 뒤에서 암살할 수 있다. 2.2 m 안은 냄새로 찾는다. 떼 좀비는 보이는 사람을 연기 속의 더 가까운 사람보다 먼저 쫓는다 | 분대가 길을 건널 때 쓰는 군용 연막 |
| 포인트 | 섬광탄 (4개) | 0.9초의 짧은 신관, 잘 튄다(벽에 튕겨 돌려 던짐). 8 m 안에서 빛이 닿는(벽이 가리지 않는, 2.5 m 안은 무조건) 좀비는 어디를 보든 3.5초 멈추고 공격이 끊기며 자세가 무너져 처형할 수 있다. 거구는 잠깐 휘청일 뿐. 머리 위에 하얀 고리가 돈다. 플레이어는 눈이 멀지 않고, 바라보면 짧게 하얗게 번쩍일 뿐(`flashLook`) | M84. 빛과 굉음이 썩은 뇌를 흔든다 |
| 데탕트 | 소독 안개탄 (2개) | 0.8초 뒤 4.5 m 낮은 안개가 10초. 가장자리의 좀비는 기침하듯 물러서며 공격이 끊기고 밖으로 밀려난다. 0.3초 뒤부터 그 땅은 좀비의 길에서 빠진다(NavMeshObstacle 깎기). 그래서 안에서 쓰러진 동료를 일으키거나 치료할 수 있다. 안에서 누군가를 붙잡은 좀비는 놓는다. 안에 선 플레이어는 초당 4씩 낫는다. 거구는 가장자리에서 몸만 젖힌다 | 구호소의 염소 소독약: 썩은 살을 태워 좀비가 못 들어오고, 산 사람에게는 상처를 씻어 줄 뿐 |

| 시순트 | 현상금 염료탄 (3개) | 1초 뒤 4 m에 청록 형광 염료가 퍼진다(짧은 가루 한 번). 닿은 좀비(벽 너머 제외)는 8초 동안 작은 현상금이 붙는다: 청록 테두리(`dyeOutline`, 어둠에서도 빛남. F 핑·현상금 표시가 있으면 그쪽 테두리가 먼저), 그 시순트의 총알 +15%(`dyeDamage`), 처치마다 현상금 30(`dyeMoney`) + 데스페라도 점수 1(`dyePoints`). 그중 가장 센 엘리트 하나에는 진짜 현상금 표시(F 핑과 같음). 혼자서 떼를 한 번에 사냥감으로 만들어 데스페라도 탄을 빨리 채운다 | 사냥개의 형광 염료: 사냥개가 그 냄새를 쫓는다 |
| 서포트 | 급조 지향성 지뢰 (2개) | 던지지 않고 놓는다(위). 바닥에는 휴대폰을 위로 누워 바라본 쪽을, 벽에는 등을 붙이고 방 안쪽을 지킨다. 놓고 1초(`claymoreArm`) 동안 불이 깜빡이다 딸깍 켜지면 불이 켜진 채 부채꼴 가운데에 옅은 붉은 줄이 선다. 켜진 뒤 좀비가 부채꼴(60°, 4.5 m) 안에 들어오면 앞쪽으로만 터진다: 세열과 같은 고어(중심 400, 끝 80, 6 m), 살아남으면 넘어지고(거구는 버팀), 뒤쪽 시체는 안 날아간다. 바로 옆 1 m는 모든 쪽. 한 사람이 2개까지(`claymoreMost`): 더 놓으면 가장 오래된 것이 넘어져 사라진다(터지지 않음). 아군 사격이 켜져 있으면 앞에 선 플레이어도 다친다 | 공병이 C4 두 장에 휴대폰 기폭 장치를 묶은 급조 지뢰. 휴대폰이 켜지는 데 1초 |

시순트·서포트 것은 사용자가 시안 페이지에서 골랐다(염료탄, 지뢰. 데탕트는 지금의 소독 안개 그대로).

**터지는 모습은 산 팩으로**(0.8.284, 사용자가 moment-vfx의 Realistic Gunslinger 지도에 "ㅇㅇ 제안대로 써"): 세열은 팩의 Frag Granade, 섬광탄은 Flashbang granade(우리의 강한 빛·기절 범위까지 달리는 바닥 고리·바라보는 사람 화면의 짧은 번쩍임은 그대로), 연막탄은 통이 터지는 순간만 Smoke Granade(좀비 시야를 막고 16초 남는 구름은 우리 SmokeScreen), 지뢰는 파편 폭발 Bullet Granade(앞으로 튀는 흙·돌은 그대로). [PackFx](../Scripts/Combat/Feel/PackFx.cs)가 [PackFx.csv](../Resources/Tables/PackFx.csv)의 `fragBurst`·`flashBurst`·`smokeBurst`·`claymoreBurst` 줄대로 크기·빠르기·뺄 부분(25~40 m 퍼짐·후광·5초 넘게 남는 그을음처럼 화면을 덮거나 오래 남는 것)을 맞춘다. 팩의 수류탄은 던진 알갱이가 3초 뒤 몇 m 옆에서 터지는 시연이라, [GrenadePackFx](../Scripts/Player/Grenades/GrenadePackFx.cs)가 같은 줄의 `hide`(던진 알갱이를 숨김)·`center`(터지는 부분을 그 자리에)·`now`(팩의 기다림을 뺌)·`fire`(한 번에 터뜨림)로 터지는 것만 지금 이 자리에 낸다. 모든 PC가 저마다 낸다. 프리팹은 PackFxLibrary에 들어 있어 로딩 화면 뒤 미리 그리기(CombatRehearsal)가 함께 그린다. 팩이 꺼지거나(`on` 0) 없으면 예전 그림이 나온다. 몰로토프·블레이저(소이)는 맞는 투척물이 없어 쓰지 않았다(새 투척물은 사용자가 정할 일). 클러스터는 moment-vfx의 브로큰 애로우 몫이다.

## 채우기

- 챕터를 시작할 때, 클래스를 고를 때(허브의 클래스 벽, 타이틀의 클래스 탭) 가득 찬다.
- 필드의 탄약 더미: 표의 `refill`만큼(나이프 2, 나머지 1). 총 탄약이 가득해도 투척물 칸이 비어 있으면 탄약 더미를 집을 수 있다.
- 상점: `투척물 보충` 400(Shop.csv `Grenade`, 한 개).
- 서포트의 보급과 탄약 상자: `PlayerGrenades.Resupplied(player)`가 `resupply`(1)만큼 준다(SupportKit.Receive, 0.8.179).
- 허브의 탄약 상자: [F] 한 번에 총의 탄약과 클래스 투척물이 모두 가득 찬다(HubLobby.Stations Act.Ammo → `PlayerGrenades.Fill`, 사용자: "로비에서 탄약 보충시켜 주는 그거 수류탄도 보충시켜주게 해 줘").
- 지뢰: 놓은 것은 터지거나 밀려날 때까지 그 자리에 있다. 놓은 사람이 게임을 나가면 함께 사라진다.

## 네트워크

- 던진 사람의 PC가 던지고 신관을 정한다. 다른 PC는 같은 던지기를 자기 물리로 따라 던지고(PlayerNetworkCombat.GrenadeRpc), 던진 사람의 PC가 알린 자리에서 터뜨린다(GrenadeBurstRpc). 알림이 안 오면 신관 1.5초 뒤 스스로 터진다.
- 피해와 자세 무너뜨리기는 던진 사람의 PC가 좀비 사본에 주고 사본이 호스트에 전한다(총알과 같다): 처치·돈·킬 피드·어썰트 아드레날린이 던진 사람 것.
- 섬광 기절은 모든 PC가 자기 사본에 주고(각자 호스트에 알림), 연막·안개의 좀비 반응은 호스트의 좀비가 한다(모든 PC가 구름·안개를 가짐).
- 나이프: 던진 사람의 PC가 맞히고, 다른 PC의 사본은 같은 선을 날다 무언가에 꽂히며, 던진 사람이 주우면 사라진다(KnifeTakenRpc).
- 염료: 모든 PC가 자기 사본 좀비에 염료를 묻히고 같은 엘리트에 현상금 표시를 단다(보이는 것은 모두 같다). 총알·돈·점수는 던진 시순트의 PC에서만.
- 지뢰: 놓은 사람의 PC가 놓고(MineRpc로 모두에게), 자기 사본 좀비를 보고 터뜨린다. 피해는 세열처럼 그 PC가 사본을 통해 준다. 다른 PC의 사본 지뢰는 GrenadeBurstRpc가 알려 준 자리에서 같은 쪽으로 터진다. 2개 넘는 것은 PC마다 같은 순서로 오래된 것부터 치운다.

## 킬 피드

- 무기 이름: `grenade.<id>.name`(Texts.csv), 피해 방식 `DamageMethod.Thrown`(나이프)·`Blast`(세열) → `Throwables.FeedName`.
- 아이콘 id(무기 아이콘 세션의 WeaponIcons): `ThrowingKnife`, `GrenadeFrag`, `GrenadeSmoke`, `GrenadeFlash`, `GrenadeCanister`, `GrenadeDye`, `Claymore`(Grenades.csv `icon`). 지뢰 처치는 그림이 생기기 전까지 폭발 그림이다. 염료탄은 피해가 없어 킬 피드에 나오지 않는다. 그림(Resources/UI/WeaponIcons/<모양>/<id>.png)이 없으면 세열은 폭발 그림, 나이프는 이름 글자. 모델이 [Art/Models/Addons/Grenades](../Art/Models/Addons/Grenades)에 있어 Tools/Blender/weapon_icons.py가 같은 방법으로 그릴 수 있다.

## 고치는 곳

- 수치: [Grenades.csv](../Resources/Tables/Grenades.csv)(종류마다 한 줄: 가진 클래스, 개수, 신관·쿠킹·끝까지 쥐면(`cookOut`), 속도, 범위, 피해, 시간, 손에 쥔 자리 …), [GrenadeRules.csv](../Resources/Tables/GrenadeRules.csv)(공통: 탭 길이, 손이 들어가고 나오는 시간(`armIn`·`armOut`), 나이프 회전·하얀 줄(`knifeSpin`·`knifeTrail`), 반사 투척(`reflexLead`), 쥐는 손가락(`gripCurl`·`gripThumb`), 쿠킹 링(`ring*`), 간격, 나이프 줍는 거리, 연막 규칙, 섬광 번쩍임, 세열 고어·넘어뜨림, HUD 자리), 손 동작마다의 클립과 빠르기(`draw`·`hold`·`throw`·`tap`·`drawSpeed`·`tapSpeed`·`throwSpeed`·`fade`) [ThrowArms.csv](../Resources/Tables/ThrowArms.csv), 클립 표 Grenade*/Knife*(Draw·Hold·Throw·Tap)는 블렌더 작업실 [ArtSource/Grenades](../../../ArtSource/Grenades/START_HERE.md)에서 고쳐 보낸다(손으로 고치지 않는다), 소리 크기는 [Sounds.csv](../Resources/Tables/Sounds.csv) `Grenades/*`.
- 코드: [Scripts/Player/Grenades](../Scripts/Player/Grenades) — PlayerGrenades(키·개수·손의 단계·쿠킹·반사 투척·던지기·줍기), GrenadeArm(왼팔을 클립대로, 클립 사이 섞기, 손에 쥔 모델과 쥐는 점), GrenadePackFx(팩 효과로 터지기: PackFx 뒤에 시연을 걷어 냄), GrenadeArmPath(손 동작과 클립 표 읽기: 순간, 손가락마다의 굽힘), GrenadeCookRing(조준점 둘레 신관 링), DyeBurst(+ DyePuff)·DyeMarks(염료: 표시·테두리, 클래스가 묻는 GunFactor·Money·KillPoints), ClaymoreMine(+ ClaymorePreview: 놓을 자리와 부채꼴), Throwables(표·모델·소리·아이콘·킬 피드), ThrownGrenade(나는 몸·신관·튐), ThrownKnife, GrenadeBursts(종류별 터짐), FragBlast, FlashBurst(+ DazeMark, FlashLook), SmokeScreen, MistField, GrenadeHUD, ThrowArc, GrenadeLibrary.
- 시순트 클래스와: 염료가 묻은 좀비에 시순트 총알이 더 들어가는 것(`PlayerClass.GunFactor`에 `DyeMarks.GunFactor(target, kit.Movement)`)과 처치의 데스페라도 점수(`ClassKit`의 처치에 `DyeMarks.KillPoints`)는 클래스 파일 쪽에 있다(데스페라도 점수는 `ClassKit.DyedKill`. 진짜 표식도 붙은 엘리트는 표식 처치로 쳐서 표식의 점수를 받는다). 현상금 30은 `GunFeelDirector`가 킬과 함께 준다.
- 다른 손 동작과: `PlayerGrenades.HandBusy(player)`가 참인 동안(꺼내기~돌아가기) 왼손은 던지기 것이다. F 가리키기(PointArm·PlayerPoint, 0.8.208)는 이것을 보고 놓는다.
- 좀비 쪽 연결: ZombieMind.Shown(연막이 시야 광선을 막음)·ZombieMind.Lose(연막에서 잃음), EnemyTargeting.NearestSeen(보이는 사람 먼저), StealthView.Cast(시야 콘이 연막에서 끊김).
- 모델: [GrenadeLibrary.asset](../Resources/Combat/GrenadeLibrary.asset). 염료탄은 연막탄 모델을 표의 `tint`(청록)로 물들여 쓰고(Throwables.Dress), 지뢰는 Sierra Division Heist Essentials의 `Bomb_03`(C4 두 장 + 휴대폰 기폭 장치, 이미 들어와 있던 구매 에셋, URP 재질)이다. FBX는 ArcCW GSO(GMod 워크숍 2257255110)의 CS:GO 던진 월드 모델과 나이트 나이프만 [MW] Quick Throwing Knife(3650480912)의 MW2019 나이프를 [addon_weapon_to_fbx.py](../../../Tools/Blender/addon_weapon_to_fbx.py)와 레시피 [Tools/Blender/Addons](../../../Tools/Blender/Addons)(GrenadeFrag·GrenadeFlash·GrenadeSmoke·GrenadeCanister·MWThrowingKnife)로 바꿨다(예전 CS:GO 나이프 ThrowingKnife는 쓰지 않고 남겨 둠). 나이프의 HUD 아이콘(`ThrowingKnife`)은 아직 예전 그림이다. 데탕트 통은 소이탄 몸통을 흰색 + 빨간 십자로 다시 칠했다(Textures/incendiary_grenade.png). 모델이 없으면 단순한 도형으로 그린다.
- 소리: Art/Audio/Grenades/<키>/ (소리 묶음 Grenades, SoundBankBuilder가 Resources/Audio/Grenades.asset으로 묶음). CS:GO의 핀·던지기·튐·고폭·섬광·연막(ArcCW GSO), 나이프 던지기는 MW Quick Throwing Knife(3650480912).
- 새 투척물: Grenades.csv에 줄(id는 `Throwable` 열거형 이름), 모델은 레시피 하나 + GrenadeLibrary에 칸, 터짐은 GrenadeBursts에 한 줄, 이름은 Texts.csv `grenade.<id>.name`.
- 블렌더 작업실을 처음부터 다시 만들기(손질이 지워진다): [build_grenade_workspace.py](../../../Tools/Blender/Grenades/build_grenade_workspace.py) 머리말과 START_HERE.md 끝. 예전 경로 추출기 [vmanip_throw_path.py](../../../Tools/Blender/vmanip_throw_path.py)·[hand_path.py](../../../Tools/Blender/hand_path.py)는 다른 뷰모델 손을 표로 뽑는 데 그대로 쓸 수 있다(순간 표시 없이 나오므로 작업실에서 더한다).

## 시안 페이지

시순트·서포트 투척물 아이디어와 데탕트 변형 두 가지: 총관리자가 받은 링크(트리거 "수류탄 시안 봐 줘"). 고른 것은 페이지의 db `picks`에 남는다. 2026-11-12에 고른 것: 시순트 `dye`(현상금 염료탄), 서포트 `claymore`(지뢰, 메모: "던지는 느낌보단 설치로 가도 되긴 하겠다"), 데탕트 `mist`(그대로). 같은 메모의 "간이 보급 던지기"와 "속경화 폼탄(GTFO의 GOO 수류탄 느낌)"은 나중에 서포트 스킬 아이디어로 남겨 두었다(아직 짓지 않음).
