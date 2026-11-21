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
| 총 든 채 서기·걷기·달리기 (권총·소총·샷건) | BodyMoves `<종류>` | 0.9.240. 탄 종류로 고른다 |
| 총 겨누고 서기·걷기·조깅, 웅크려 겨누기 | BodyMoves `<종류>.aim`, `.crouch`, `.crouch.aim` | 0.9.240 |

## 아직 연결하지 않은 것 (다음 단계)

각 단계는 검사대 숫자를 먼저 보고 한다. 새 동작이 필요한 것은 그 동작의 주인(몸의 층, 무기 상태)을 먼저 정한다.

1. **꺼내기·넣기 몸 동작** (`*_TakeGun`, `*_PutGun`): 총을 꺼낼 때 손이 총이 걸린 자리로 갔다가 들어 올리는 몸 전체 동작. 총의 자리는 총 다루기 다시 세우기의 GunCarry가 주인이라([GunHandling.md](GunHandling.md) 4.2, 5단계 무기 상태) 그 설계 안에서 "오른손이 든다 → 몸통이 든다"로 넘겨야 한다. FPSAF의 꺼내기 클립이 있는 총 10개와 없는 총 18개가 다르다.
2. **출발·멈춤·방향 꺾기** (`*_Start_*`, `*_Stop_*`, `*_Turn*`, `*_Arc_*`, `*_Lean*`, `ShiftTo*`): 걸음 사이의 한 번짜리 동작. 다리 층의 일이다.
3. **제자리 돌기** (`TurnIP_L/R 45·90·135·180`, `*_AimTurn_*`): 각도별 클립이 있어서, `Body.turnSteps`가 꺼져 있던 까닭(클립 하나의 각도와 몸의 회전이 안 맞음)을 각도로 고르는 것으로 풀 수 있다.
4. **점프·착지·구르기·다이브·슬라이드** (`Jump_*`, `RunJump_*`, `Land_*`, `Land_To*`, `Roll_*`, `DiveRoll_*`, `RollToRun_*`, `SlideToRun_*`, `Flip_*`, `Jump_Climb_*`): 지금 Beatrun·Mixamo 클립이 맡은 자리들(`Body.jump`, `land`, `roll` 등)에 높이·방향별로.
5. **회피** (`QuickShift_*` 8방향, `*_Evade`): 지금 `Body.dodgeLeft/Right`.
6. **맞기·쓰러지기·기절** (`*_Hit_Low`, `*_Hit_Upper`, `*_Die_F/B`, `*_Stun`).
7. **감정 표현** (`CBG_*` 7개): 제스처 자리.
8. **사격·장전 몸 동작** (`*_Shoot`, `*_Reload`, `R_AutoShoot`): 1인칭 총과 손은 FPSAF와 총 트랙이 주인이라, 쓴다면 다리와 몸통의 흔들림만이다.
9. L135·R135 스트레이프(옆으로 갈 때 다리가 뒤로 엇갈리는 판): 검사대로 방향을 잰 뒤, 뒤쪽으로 갈 때 고르는 판으로.

## 남은 비용

`Art/Animations` 아래 클립은 모두 몸의 클립 목록(`Resources/Characters/PlayerBody`)에 들어가 빌드에 실리고, 이 폴더의 클립은 압축 없이 들어온다(KeepEveryKey). 358개가 늘었다. 쓰는 클립만 목록에 두는 것은 따로 할 일이다.
