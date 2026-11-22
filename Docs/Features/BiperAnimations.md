# BIPER 애니메이션 두 묶음 (Combat Girls, Female Runner)

2026-11-21 사용자: "combat girls랑 female runner 애니메이션들 싹 다 내 게임에 적용 ㄱㄱ. 그리고 총 들었을 때 바로 러닝 애니메이션으로 넘어가지 말고 드는 모션은 보였으면 좋겠어.." 물어본 것에 대한 답: 보이길 원하는 것은 꺼내는 동작과 총 든 채 달리기 **둘 다**, 그리고 "애초에 TRUE FPS라 1인칭 화면이 3인칭 화면임. 근데 칼 드는 모션이 찰져서 칼 드는데 움직이면서 칼 들면 칼 드는 팔 모션이 바로 걷기나 달리기에 뺏겨서 못 보잖아."

## 들여온 것 (0.9.240)

에셋 스토어 캐시(`F:\UnityAssetStoreCache\Asset Store-5.x\BIPER`)의 일곱 묶음에서 애니메이션 FBX만, 묶음의 GUID 그대로 꺼냈다. 캐릭터 모델·무기 모델·데모 스크립트·씬은 들이지 않았다.

| 폴더 | 클립 | 묶음 |
| --- | --- | --- |
| `Art/Animations/BiperRunner` | 237 | Female Runner Animset 넷 (Escapes, Free Run/Sprint, Free Walk/Jog, Jumps) |
| `Art/Animations/BiperCombat` | 121 | Combat Girls 셋 (Pistol, Rifle, Shotgun). 감정 표현 7개(`CBG_*`)는 세 묶음에 같은 바이트로 들어 있어서 한 벌만 |

- 폴더마다 `<폴더>.tpose.fbx`는 묶음의 `Humanoid_F`(BIPER가 클립들에 쓰는 아바타의 원본, UE 마네킹과 같은 뼈 이름)이고, 폴더의 모든 클립이 그 아바타를 쓴다(ModelImportRules의 T자 자세 규칙).
- `<폴더>.clips.csv`: 걸음·서기·조준 걸음은 반복, 나머지는 한 번. 몸이 움직이는 클립은 `inPlace` 1(이동은 루트 모션으로 빠지고, 몸은 플레이어의 길을 따른다), 제자리 클립(서기, 웅크리기 전환, 제자리 돌기, 사격·장전·꺼내기 등, 감정 표현)은 0.
- 임포트 규칙 하나를 넓혔다: **한 파일에 클립 하나인 폴더(폴더의 clips.csv)에서는 클립 이름이 파일 이름이다.** Female Runner는 모든 테이크 이름이 `Take 001`이라 몸의 클립 목록에서 이름이 다 겹쳤다. 예전에는 Mixamo의 `mixamo.com` 테이크만 파일 이름을 받았다. Mixamo·MocapOnline 폴더는 원래 클립 이름이 파일 이름과 같아서 바뀌는 것이 없다.
- 새 클립 이름 358개는 기존 클립 이름과 하나도 겹치지 않는다(대소문자 무시).

## 클립 검사대 (0.9.240)

[ClipBench](../../Assets/_Project/Scripts/Player/Body/Feet/ClipBench.cs): Play 없이 에디터에서 클립마다 이치고(기준 몸)에 재생해 게임과 같은 걸음 재기(GaitClips)로 발이 몸을 나르는 속도·방향·걸음 수를 재고, 루트 곡선(RootT·RootQ)에서 클립 전체의 이동·회전·오르내림을, 발 위 엉덩이 높이를 처음·가장 낮을 때·끝에서 잰다. 숫자는 게임 크기(눈 1.6 m)로 적는다. 메뉴 `ENFILADE → 몸 → 클립 검사대`, 또는 `Temp/enfilade-clipbench.request`(줄: `folders=`, `clips=`, `skins=`). 결과는 `Logs/ClipBench/report.txt`. **"클립 기록 봐 줘" = 그 파일을 읽는다.**

어느 순간에 어느 클립을 쓸지는 이 숫자로 정한다(파일 이름이 아니라). 특히 BIPER 클립이 이 프로젝트의 규칙(`yawOffset`)으로 앞을 보는지, `L45`/`L135`가 어느 쪽으로 가는지가 여기서 정해진다.

## 연결한 것

| 무엇 | 어디 | 상태 |
| --- | --- | --- |
| 칼 꺼내기 팔이 걸음에 뺏기던 것 | [MeleeKnife](../../Assets/_Project/Scripts/Melee/Knives/MeleeKnife.cs) | 0.9.239. 칼의 동작(꺼내기·준비·휘두르기)은 걷든 달리든 팔을 다 갖고, 쉬는 가드만 앞으로 달릴 때 달리기에 팔을 내준다 |
| 걸음의 방향 | [PlayerBody.Locomotion](../../Assets/_Project/Scripts/Player/Body/PlayerBody.Locomotion.cs) | 0.9.240. 방향은 클립마다 잰 만큼(8방향 스트레이프), 자세마다 한 줄의 표 [BodyMoves.csv](../../Assets/_Project/Data/Resources/Tables/Player/BodyMoves.csv) |
| 맨몸 서기·걷기·조깅·달리기·전력질주, 스트레이프 | BodyMoves `plain`, Player.csv `Body.idle` | 0.9.240 |
| 웅크려 서기·걷기·조깅 | BodyMoves `crouch`, `Body.crouchIdle` | 0.9.240 |
| 총 든 채 서기·걷기·조깅·전력질주 (권총·소총·샷건) | BodyMoves `<종류>` | 0.9.240. 탄 종류로 고른다. 조깅은 `*_AimJog`(0.9.294), 전력질주는 그 총의 달리기 `*_Run`(0.9.314: 몸이 앞으로 R 33°·S 20°·P 42° 기운다. 맨몸 `Sprint_Lfoot` 46°. 사용자 11-22 "combat girls 적용한 거 맞냐? 달릴 때 너무 기우는데?"). 0.9.316부터 질주 동안 그 클립이 몸 전체(상체·팔 포함)를 갖고 총은 클립의 오른손을 따라가며 조준·사격은 안 된다. 클립이 앞으로만 있어서 총을 들면 W·W+A·W+D로만 질주하고 대각선은 골반을 돌려 이 클립으로 달린다(0.9.319 `PlayerBody.SprintsToward`; `PlayerBody.GunRuns`, `Body.gunRunFrom`; 사용자 11-22 "전력질주할 때 줌이나 총 못 쏘게 하고 combat girls animation을 처쓰라고") |
| 총 겨누고 서기·걷기·조깅, 웅크려 겨누기 | BodyMoves `<종류>.aim`, `.crouch`, `.crouch.aim` | 0.9.240. 0.9.318부터 조준하는 동안 상체·팔도 이 클립이다. 총은 가늠자(눈 자리)가 카메라에 오게 들고 카메라는 오른눈에 있다(0.9.320 `PlayerBody.AimsOnClip`, `RightEyeShift`; 사용자 11-22 "조준하는 것도 이 애니메이션 써") |
| 다운될 때 무너지기 (`P_Die_F`) | [PlayerBody.Downed](../../Assets/_Project/Scripts/Player/Body/PlayerBody.Downed.cs), `Body.downedFall` | 0.9.298. 서기·회피 스텝·밀치기 중에 다운되면 무릎이 꺾여 앞으로 엎어진 뒤 엎드림으로. 쓰러지는 1초(`Move.downedFallTime`)는 기지 못한다. 웅크림·누움·슬라이드·공중에서는 바로 엎드림 |
| 맞으면 몸만 움찔 (`R_Hit_Upper`, `R_Hit_Low`) | [PlayerBody.Flinch](../../Assets/_Project/Scripts/Player/Body/PlayerBody.Flinch.cs), `Body.flinch*` | 0.9.299. 서 있을 때 맞으면 등뼈·가슴만 클립을 따른다(층 FlinchLayer). 한 번에 20(`Body.flinchHardDamage`) 이상 들어가면 R_Hit_Low. 카메라는 맞은 순간의 눈 자리를 지키고 머리가 그리로 굽는다. 친구 PC에도 같은 세기로 |
| 감정 표현 (`CBG_*` 7개) | [PlayerBody.Emote](../../Assets/_Project/Scripts/Player/Body/PlayerBody.Emote.cs), Callouts.csv `emote.*`(gesture `emote:<클립>`), CalloutSlots.csv 쪽 `emote`, HandUses.csv `body.emote` | 0.9.300. 콜아웃 휠 > 감정(고마워 자리, 고마워는 감정 쪽 8번째). 인사·미소·수줍음·놀람·화남·실망·슬픔. 말·목소리·통신 화면 없이 몸과 표정만, 모든 PC에. 상체는 늘, 엉덩이·다리는 가만히 서 있을 때만. 두 손을 쓰므로 총은 그동안 넣고, 쏘기·조준 버튼이나 더 센 손 쓰임(꺼내기·장전·수류탄 등)이 오면 끝난다 |
| 걸음새와 속도: 방향 키 조깅(0.9.283 전에는 달리기), Shift 전력질주(8방향), Caps Lock 걷기 모드의 걷기와 Shift 조깅, 웅크려 걷기·조깅. 몸은 그 걸음새 클립이 그쪽으로 나르는 속도로 간다 | [MovementGait](../../Assets/_Project/Scripts/Player/Movement/Actions/MovementGait.cs), [Gaits.csv](../../Assets/_Project/Data/Resources/Tables/Player/Gaits.csv) | 0.9.245. 가속으로 쌓이던 속도와 골반 돌림은 없앴다. 0.9.253: 게임 속도를 루트 이동으로 다시 재고(걷기 1.07→1.57, 달리기 3.93→5.20, 전력질주 5.61→7.16). 0.9.256: 출발 클립 가속은 되돌리고(얼음 느낌), 몸마다의 다리 맞춤은 그 몸의 발바닥 속도로. 0.9.283: 달리기를 빼고 방향 키는 조깅. 0.9.350: 모든 걸음새·총 든 걸음·조준 걸음을 클립의 속도 그대로(사용자 11-22 "전부 맞추기") |

## Combat Girls에서 고른 것 (3단계, 사용자 2026-11-22)

클립 검사대로 재고(`track=`, 그림은 `cyclefilm=`) 무엇을 하는 동작인지 보인 뒤 사용자가 골랐다. 잰 것: 권총은 오른손이 오른 허벅지로, 소총·샷건은 오른 어깨 너머 등으로 가서 0.45~0.58초에 겨누고, 0.5초 겨눈 채 서 있다가 내린다(TakeGun 1.73초). Evade는 제자리에서 몸을 오른쪽으로 116° 돌려 0.6m 비켰다 1.37초에 돌아온다. Die_F는 무릎이 꺾여(엉덩이 0.97→0.64m, 0.77초) 앞으로 엎어지고 0.96초에 바닥, Die_B는 뒤로 넘어져 등을 댄다(0.9m 뒤로). Hit_Upper는 상체가 뒤로 젖혀졌다 1.27초에, Hit_Low는 무릎이 0.2m 꺾였다 1.03초에 선다. Stun은 허리를 굽히고 팔을 늘어뜨린 채 3초 휘청인다. 감정 표현 7개는 맨손으로 몸 전체를 쓴다(2.5~3.3초).

| 클립 | 고른 것 | 상태 |
| --- | --- | --- |
| `P_Die_F` | 다운될 때 무너지기: 서 있다가 다운되면 앞으로 엎어져 엎드림으로 | 0.9.298 |
| `R_Hit_Upper`, `R_Hit_Low` | 맞으면 몸만 움찔(시점·총·조작은 그대로): 가벼운 공격은 상체가 뒤로, 20 이상은 앞으로 꺾임 | 0.9.299 |
| `*_TakeGun` | 안 씀: 처음에 "다리·몸통만"(꺼내기는 FPSAF 그대로, BIPER 꺼내기의 무릎·허리만 아래에)을 골랐다가, 무릎이 굽으려면 엉덩이가 내려가야 해서 카메라=머리인 이 게임에서는 꺼낼 때마다 시점이 8~13cm 내려갔다 올라온다(엉덩이 0.97→0.84m)는 것과 엉덩이를 빼면 가슴이 0.3초 20°쯤 비틀리는 것뿐이라는 것을 듣고 빼기로(사용자 11-22) | 안 씀 |
| `CBG_*` 7개 | 콜아웃 휠에 '감정' 쪽: 휠의 '고마워' 자리가 감정 쪽을 열고 고마워는 그 쪽 8번째로. 말 없이 몸과 표정만(사용자 11-22) | 0.9.300 |
| `*_PutGun` | 안 씀: 넣기가 0초(총을 바꾸면 바로 사라짐)라 틀 자리가 없다 | 안 씀 |
| `*_Evade` | 안 씀: 회피는 QuickShift 끝까지(사용자 11-22) | 안 씀 |
| `*_Shoot`, `*_Reload`, `R_AutoShoot`, `R_AimIdle_AutoShoot` | 안 씀: 1인칭 상체·손은 FPSAF와 총 트랙, 0.9.296에 총의 걸음 흔들림을 껐다 | 안 씀 |
| `*_Die_B`, 소총·샷건 `Die_F` | 안 씀: 죽음은 래그돌(맞은 쪽으로 날아감), 다운은 권총을 들어서 권총 판만 | 안 씀 |
| `*_Stun` | 안 씀: 플레이어가 기절하는 규칙이 없다 | 안 씀 |

## 아직 연결하지 않은 것 (다음 단계)

각 단계는 검사대 숫자를 먼저 보고 한다. 새 동작이 필요한 것은 그 동작의 주인(몸의 층, 무기 상태)을 먼저 정한다.

1. **꺼내기·넣기 몸 동작** (`*_TakeGun`, `*_PutGun`, 쓰지 않기로 함: 위 "Combat Girls에서 고른 것"): 총을 꺼낼 때 손이 총이 걸린 자리로 갔다가 들어 올리는 몸 전체 동작. 총의 자리는 총 다루기 다시 세우기의 GunCarry가 주인이라([GunHandling.md](GunHandling.md) 4.2, 5단계 무기 상태) 그 설계 안에서 "오른손이 든다 → 몸통이 든다"로 넘겨야 한다. FPSAF의 꺼내기 클립이 있는 총 10개와 없는 총 18개가 다르다.
2. **출발·멈춤**: 0.9.259에 연결했다가 0.9.263에 뺐다. 클립이 앞쪽뿐이라 옆·뒤·대각선 출발이 없어 앞으로만 무거웠다(사용자 판단). `*_Start_*`·`*_Stop_*`는 쓰지 않는다. **방향 꺾기**(기울기·호: 0.9.260에 연결, 시선이 도는 빠르기로 섞음. 반대로 꺾기 Turn은 쓸 순간이 없어 안 씀) (`*_Turn*`, `*_Arc_*`, `*_Lean*`, `ShiftTo*`: ShiftTo는 0.9.262에 달리며 회피로).
3. **제자리 돌기** (`TurnIP_L/R 45·90·135·180`, `*_AimTurn_*`): 0.9.261~0.9.278에 연결했다가 0.9.279에 뺐다. 쉬던 몸이 새 방향으로 돌아서는 3인칭용 동작(2.27초)이라, 시선을 따라 도는 1인칭 몸에 붙이면 다리가 기다렸다가 클립의 시간대로 돌았다(사용자: "구조와 근본의 문제인 듯"). 이제 발이 스스로 걷는다(FootPlanting 걸음, PlayerBodyGuide 제자리 돌기). 클립은 쓰지 않는다.
4. **점프·착지·구르기·슬라이드**(0.9.334부터 점프는 속도와 쪽에 상관없이 Beatrun `jumpstill` 하나다. 0.9.290에 점프·옆 점프·가벼운 달리는 착지·점프 착지는 Beatrun 것으로 되돌렸다: 팔을 너무 휘둘렀다(사용자). 0.9.262에 연결: 제자리 Jump_1h, 걸으며 RunJump_1m, 달리며 RunJump_2m, 옆으로 Jump_AirL·R·RunJump_AirL·R. 착지 가볍게 Land_1h, 세게 Land_3h, 크게 Land_4h, 앞으로 가며 Land_ToRun1·3, 전력질주에서 세게 Land_ToStumble. 구르기 느리면 Roll_* 8방향, 빠르면 RollToRun_*. 슬라이드 SlideToRun_Enter·Loop·Exit. 공중 클립은 높이를 자세에서 뺌. 0.9.266: 점프 클립은 땅을 떠나는 순간부터 게임의 비행 높이를 따른다(PlayerBody.Flight, GaitMoves.csv kind jump). 서서 가볍게 착지하면 Land_1h. 슬라이드가 웅크림으로 끝나면 Exit 없이 바로 웅크림. 0.9.271: 달려 나가며 끝나는 클립(SlideToRun_Exit, Land_ToRun*, Land_ToStumble, RollToRun_*, ShiftTo*)은 몸이 달릴 때만 끝까지, 멈추면 자세를 멈춘 채 서기로. 제자리에서 멈춘 슬라이드는 Exit를 일어나는 데까지만. Land_ToRun은 달리기 빠르기 이상에서만. 코드 PlayerBody.Ways, 표 Player.csv Body.*Ways. 0.9.336: 슬라이딩 점프(Space)가 다이브 롤 DiveRoll_F. 0.9.351: 슬라이딩·슬라이딩 끝·달리며 세게 착지·회피 뒤 달려 나가기의 몸이 클립의 빠르기로 가고(SlidePace, PlayerBody.RunOutPace), 회피 거리도 클립 그대로. 아직: Flip·높은 점프 Jump_2h·3h·RunJump_hyper·obstacle·난간 잡고 오르기 Jump_Climb_*). 예전 목록 (`Jump_*`, `RunJump_*`, `Land_*`, `Land_To*`, `Roll_*`, `DiveRoll_*`, `RollToRun_*`, `SlideToRun_*`, `Flip_*`, `Jump_Climb_*`): 지금 Beatrun·Mixamo 클립이 맡은 자리들(`Body.jump`, `land`, `roll` 등)에 높이·방향별로.
5. **회피**(0.9.262에 연결: QuickShift_* 8방향. 0.9.335부터 이동도 클립의 빠르기이고 총을 들어도 몸 전체. 0.9.341부터 전력질주하며 옆으로 해도 QuickShift이고, ShiftToSprint_*는 그 뒤 전력질주로 일어나는 뒷부분만. 달리며 ShiftToRun_*은 0.9.284에 달리기와 함께 뺌. Combat Girls `*_Evade`는 쓰지 않는다(사용자 11-22)): 코드 DodgeMove·PlayerBody.Dodge, 표 `Ability.dodge*`.
6. **맞기·쓰러지기·기절** (`*_Hit_Low`, `*_Hit_Upper`, `*_Die_F/B`, `*_Stun`): 위 "Combat Girls에서 고른 것". 다운될 때 `P_Die_F`는 0.9.298, 맞으면 몸만 움찔 `R_Hit_Upper`·`R_Hit_Low`는 0.9.299에 연결.
7. **감정 표현** (`CBG_*` 7개): 제스처 자리. 0.9.300에 콜아웃 휠의 감정 쪽으로 연결.
8. **사격·장전 몸 동작** (`*_Shoot`, `*_Reload`, `R_AutoShoot`): 1인칭 총과 손은 FPSAF와 총 트랙이 주인이라, 쓴다면 다리와 몸통의 흔들림만이다. 쓰지 않는다(사용자 11-22).
9. L135·R135 스트레이프(옆으로 갈 때 다리가 뒤로 엇갈리는 판): 검사대로 방향을 잰 뒤, 뒤쪽으로 갈 때 고르는 판으로.

**Female Runner 나머지 (0.9.285, 사용자 11-21 "Female Runner first")**: 서서 웅크리기·일어서기(`IdleToCrouch`·`CrouchToIdle`, 자세를 따라), 높은 데서 가벼운 착지(`Land_2h`·`Land_ToRun2`), 조깅으로 세게 착지(`Land_ToSlam`), 턱에서 걸어 떨어지기(`Jump_Falldown`), 슬라이딩 중의 살아 있는 가운데(`SlideToRun_10m` 0.70~1.93초), 걸으며 기어오르기(`Jump_Climb_1h`·`2h`)를 연결했다. 자세한 것은 [PlayerBodyGuide](../Guides/PlayerBodyGuide.md)와 [MovementGuide](../Guides/MovementGuide.md). 쓰지 않는 것: 공중제비 `Flip_*`(사용자 판단), `Jump_Climb_Enter_*`·`Exit`(1h·2h를 나눈 판), `SlideToRun_5m`, 앞 말고 다이브 구르기 전부(`DiveRoll_*`·`DiveRollToRun_*`: 0.9.285~0.9.288에 Z로 썼고 0.9.289에 Z와 함께 뺌. 앞 구르기 `DiveRoll_F`는 0.9.336부터 슬라이딩 점프), 그리고 앞서 뺀 출발·멈춤·제자리 돌기·반대로 꺾기·달리기·오른발 시작 판·앞쪽·엇갈림 스트레이프.

## 남은 비용

`Art/Animations` 아래 클립은 모두 몸의 클립 목록(`Resources/Characters/PlayerBody`)에 들어가 빌드에 실리고, 이 폴더의 클립은 압축 없이 들어온다(KeepEveryKey). 358개가 늘었다. 쓰는 클립만 목록에 두는 것은 따로 할 일이다.
