# 플레이어 몸 (진짜 1인칭)

[프로젝트 안내](../../../README.md) · [이동 안내](MovementGuide.md) · [설정·유지보수](MaintenanceGuide.md) · [검증 안내](ValidationGuide.md)

플레이어는 보이는 몸을 가진다. 몸은 이동 상태에 맞는 애니메이션을 재생하고, 카메라는 항상 몸의 눈에 붙어 있다(Beatrun Eye View처럼, 아래 **눈 시점**). 회전·기울기·충격 흔들림은 코드가 더한다. 내려다보면 허리·목이 같이 굽어 몸통·다리가 보인다. 머리·머리카락은 Esc 설정 **내 머리카락 보기**를 켜면 보이고, 끄면 그림자만 남는다. **F5**는 뒤에서 몸을 보는 확인용 시점이다.

## 구성

| 구성 | 위치 |
| --- | --- |
| 캐릭터 | [Art/Models/Characters/Hiro](../Art/Models/Characters/Hiro) (휴머노이드). 서 있는 눈높이가 카메라(1.6m)에 맞게 자동으로 크기가 정해진다 |
| 애니메이션 | [Art/Animations/Mixamo](../Art/Animations/Mixamo)의 26개 클립(서기·걷기·달리기 8방향, 넘기, 난간, 낙하·착지·구르기, 일어나기). [Art/Animations/Beatrun](../Art/Animations/Beatrun)의 173개 + 웅크려 옆걸음 2개(슬라이딩·다이브·웅크리기·벽·봉 등, 소총·권총 버전 포함). 휴머노이드라 어떤 캐릭터에도 옮겨 붙는다 |
| 연결 | `Resources/Characters/PlayerBody` 프로필(모델 + 클립 목록). 임포트할 때 자동으로 만들어지고 클립 목록도 자동 갱신된다 |
| 상태별 클립·연출 수치 | [Player.csv](../Resources/Tables/Player.csv)의 `Body` 행 (예: `Body.slide,slideloop`) |
| 자세별 캡슐·눈높이 | `Player.csv`의 `Posture` 행. 웅크림 0.8m, 슬라이딩 0.62m, 엎드림 0.37m, 누움 0.35m. 눈이 캡슐 꼭대기를 넘지 않게 맞췄다 |
| 동작 코드 | [PlayerBody](../Scripts/Player/Body/PlayerBody.cs). Play 시작 때 `PlayerMovement`가 붙인다 |

몸의 수치는 모두 표에 있고 Play 중에도 바로 반영된다. 클립 이름을 바꾸면 몸이 다시 만들어진다.

## 캐릭터 바꾸기

새 모델을 `Art/Models/Characters/<이름>/`에 넣고, `PlayerBody` 프로필의 `Model`을 바꾼다. 1인칭에서 숨길 부분은 프로필의 `First Person Shadow Only`에 렌더러 이름으로 적는다(기본 `Head`, `Hair`).

Hiro는 [lilToon](https://github.com/lilxyzw/lilToon) 2.4.1(`Packages/jp.lilxyzw.liltoon`, MIT) 셰이더를 쓴다. 재질은 [Hiro/Materials](../Art/Models/Characters/Hiro/Materials)에 따로 있어 Inspector의 lilToon 화면에서 그림자 색·림라이트·광택을 바로 조정한다. 새 캐릭터는 모델 Inspector → Materials → **Extract Materials** 후 셰이더를 `lilToon`으로 바꾼다.

## 애니메이션 추가하기

휴머노이드 FBX를 `Art/Animations/<묶음>/`에 넣고 옆에 `<파일이름>.clips.csv`를 둔다. 클립 하나짜리 파일이 많은 폴더(Mixamo)는 폴더 이름의 표 하나(`Mixamo/Mixamo.clips.csv`)에 모은다. Mixamo는 모든 클립 이름이 `mixamo.com`이라 파일 이름이 클립 이름이 된다. 수정 후 반영되지 않으면 해당 FBX를 우클릭해 **Reimport**한다.

| 열 | 뜻 |
| --- | --- |
| `loop` | 1이면 반복 |
| `inPlace` | 1이면 클립 안의 앞뒤·옆 이동을 버리고 제자리에서 재생. 기본은 원래 위치 그대로다 |
| `inPlaceY` | 1이면 오르내림도 버린다. 넘기·올라가기처럼 플레이어 경로가 몸을 옮기는 클립에 쓴다 |
| `inPlaceYaw` | 1이면 클립 안의 회전을 버린다. 몸이 직접 도는 제자리 돌기에 쓴다 |
| `cycleOffset` | 반복 클립이 시작하는 지점(0~1). 섞이는 이동 클립들이 같은 순간에 왼발을 딛게 맞춘다 |
| `from`·`to` | 첫 프레임부터 센 프레임 번호. 그 구간만 쓴다(착지는 발이 닿는 프레임부터) |
| `yawOffset` | 클립의 앞 방향 보정 각도(도, 기본 0). 아래 설명 참고 |

`yawOffset`은 클립이 어디를 보는지에 따라 정한다. Source 클립을 그대로 변환하면 옆을 보므로 **90**이다. Hiro 뼈대에 구운 Beatrun 클립과 Mixamo 클립은 0이면 카메라 반대쪽을 보므로 **180**이다(변환 스크립트가 알맞은 값을 써 준다). 구르기·좌우 회피처럼 애니메이션에 들어 있는 회전은 그대로 남는다.

### Mixamo에서 받을 때

스킨 없이 받은 파일에는 T자 자세가 없어서, 파일마다 첫 프레임을 기본 자세로 삼은 아바타가 만들어지고 클립이 그만큼 틀어져 리타깃된다(Mixamo는 발이 반 바퀴 뒤집혀 나왔다). 그래서 같은 캐릭터를 T자 자세·스킨 포함으로 받아 폴더 이름으로 `<폴더>.tpose.fbx`(`Mixamo/Mixamo.tpose.fbx`)로 두면, 그 폴더의 다른 파일이 모두 그 아바타를 복사해 쓴다(Copy From Other Avatar). 넣으면 자동으로 다시 임포트된다.

새 클립은 지금과 같은 캐릭터(X Bot), **FBX Binary**로 받는다.

### Source(GMod·L4D2) 모델·애니메이션 변환

[Tools/Blender](../../../Tools/Blender)의 스크립트를 SourceIO 애드온이 있는 Blender로 실행한다. 모델은 `source_model_to_fbx.py`, 애니메이션은 `source_anims_to_fbx.py`. 애니메이션은 마지막 인자로 플레이어 모델의 MDL을 주면 그 모델의 뼈대(뼈 길이·기본자세)에 직접 구워 낸다. 서로 다른 뼈대 사이의 휴머노이드 리타깃이 없어져 발이 꺾이거나 몸이 뒤로 빠지지 않는다. 예: `blender -b --factory-startup --python source_anims_to_fbx.py -- new_climbanim.mdl BeatrunBody.fbx BeatrunBody * - 180 survivor_gambler.mdl`. Hiro의 뼈대 MDL은 [Tools/Blender/Source/Hiro](../../../Tools/Blender/Source/Hiro)에 있다. 맨 끝에 접미사(`_tp`)를 주면 클립 이름 뒤에 붙는다. 같은 이름의 클립 묶음을 나란히 두고 비교할 때 쓴다. 몸은 같은 이름이 여러 개면 경로 순서상 첫 번째를 쓴다.

- **블렌드 시퀀스**: 3인칭 세트의 이동 클립(`runfwd`, `walkfwd`, `crouchfwd`, 회피 등)은 이동 방향 3×3 블렌드이고, 가운데 칸이 서 있기다. 칸마다 몸 전체가 이동 방향으로 돈다. 변환 스크립트는 클립 이름에 맞는 칸을 고른다. 앞 이동은 앞 칸, `runbwd`·`walkback`·`crouchbwd`는 뒤 칸, `dodgejumpleft/right`는 옆 칸이고, 나머지 블렌드(매달리기·흔들기)는 모든 값이 0인 칸이다. 다른 칸을 따로 굽으려면 이름을 `새이름=시퀀스@옆:앞`으로 준다. 웅크려 옆걸음(`BeatrunCrouch.fbx`)은 `crouchleft=crouchfwd@-1:0,crouchright=crouchfwd@1:0`으로 구웠다. `sprintfwd` 원본은 2초에 같은 보행 주기가 네 번 들어 있다. 다른 이동 클립과 한 주기로 섞도록 `BeatrunBody.clips.csv`에서 `from=0`, `to=15`, `cycleOffset=0`으로 잘라 0.5초 한 주기만 쓴다(30fps). 전체를 다시 구우면 표가 새로 써지므로 이 세 값을 복원한다.
- **Spine4**: Source 뼈대에는 척추가 4개(`Spine`, `Spine1`, `Spine2`, `Spine4`)지만 Unity 휴머노이드에는 3자리뿐이다. 그래서 변환할 때 `Spine4`의 회전을 `Spine2`에 합쳐 굽는다. 임포트 때 "in between humanoid transforms … discarded" 경고가 나면 이 처리가 빠진 것이다.
- **손가락**: Hiro의 손가락 뼈 축은 Beatrun 뼈대와 최대 167° 달라서, 회전을 그대로 옮기면 손가락이 옆·뒤로 꺾인다. 변환할 때 클립 손가락이 자기 기본 자세에서 움직인 만큼을 Hiro 손가락 축으로 옮겨서 Hiro 기본 자세에 더한다. 클립이 기본 자세면 Hiro도 정확히 기본 자세다.
- 모든 뼈를 매 프레임 키로 굽는다(173개, 약 170MB). 움직이지 않는 뼈를 빼거나 키를 줄이면 일정한 자세를 유지하는 뼈가 기본 자세로 튀거나 중간에 꺾여서 쓰지 않는다. 한 프레임짜리 포즈와 레이어 클립은 Unity에서 빈 클립이 되므로 넣지 않는다.

## 들어 있는 애니메이션

[Beatrun](../Art/Animations/Beatrun)은 Beatrun 3인칭 여성 세트(`tp_climbanim_f.mdl`)를 Hiro 뼈대에 구운 173개 클립이다. 벽 달리기 4개는 쓰지 않아 뺐다. 소총·권총 버전은 `_ar2`·`_pistol`이 붙는다(누워 미끄러지기, 다이브, 머리부터 미끄러지기, 공중 코일 등). [Mixamo](../Art/Animations/Mixamo)는 mixamo.com에서 X Bot 기준으로 받은 클립(스킨 없음, 30fps)이다. 리타깃 기준 자세는 같은 X Bot을 T자 자세·스킨 포함·FBX Binary로 받은 `Mixamo.tpose.fbx`다(클립 파일과 같은 FBX 2019 형식, 같은 `mixamorig:` 뼈 66개). 걷기·달리기·옆걸음·뒷걸음은 제자리 버전이다. 클립마다 실제 속도·길이·발 딛는 시점은 `Mixamo.clips.csv`의 메모에 있다. 웅크리기는 Beatrun을 쓴다. Mixamo 웅크려 걷기는 눈높이가 약 1.2m라 웅크린 카메라(0.8m)와 맞지 않는다. Mixamo 클립 가운데 쓰지 않는 것은 `mx_stand_up_stomach` 하나다(11초로 너무 길어서 엎어진 착지에는 `mx_get_up`을 쓴다). `Player.csv`의 `Body` 행에서 클립 이름을 바꾸면 Play 중에도 몸이 다시 만들어진다.

## 이동

- **8방향 이동**([PlayerBody.Locomotion](../Scripts/Player/Body/PlayerBody.Locomotion.cs)): 걷기·달리기·웅크려 걷기는 앞·오른쪽·뒤·왼쪽 클립을 이동 방향으로 섞는다(대각선은 양옆 두 개). 몸은 항상 카메라 쪽을 본다. 모든 반복 클립은 한 보행 주기를 공유해서 섞여도 발이 엇갈리지 않는다. 전력질주 클립은 일부러 앞으로 달릴 때(`RunIntent`: Shift, 또는 W로 속도가 쌓이는 중) 그 속도에 이르면 쓴다. 일반 이동·뒷걸음·옆걸음에는 전력질주 클립을 섞지 않는다.
- **이동 재생 속도**: `Body.runClipSpeed=5.2`, `Body.sprintClipSpeed=8.4`, `Body.crouchClipSpeed=3`은 각각 `Move.walkSpeed`, `Move.sprintSpeed`, `Move.crouchSpeed`에 맞춘 기준값(m/s)이다. 빠른 기본 이동 때문에 평소에도 질주 자세가 섞이거나 웅크리기가 1.5배로 재생되지 않게 한다. 이동 애니메이션의 재생 배율은 0.65~1.1로 제한한다. 이동 속도를 바꾸면 이 기준값도 함께 조정한다.
- **제자리 돌기**: 서 있을 때 시선만 돌리면 다리는 그 자리에 있고 상체만 비튼다. `turnStartAngle`(60°)을 넘으면 `turnLeft`/`turnRight` 클립으로 발을 옮기며 돈다(`turnSpeed`). 돌기 클립은 `inPlaceYaw`로 회전을 빼고 발걸음만 쓴다. 이 임계각에 따른 다리 돌기는 슬라이딩 중에는 실행하지 않는다.

## 파쿠르 시간

- 넘기: 도움닫기 속도를 그대로 유지하며 지나간다. 걷기 속도보다 느려지지는 않는다. 넘기 클립은 자기 속도(`Body.vaultClipSpeed`)로 재생된다. 몸이 먼저 넘어가면 클립은 발이 닿는 지점(`vaultLandShare`)까지 이어서 재생된 뒤 달리기로 넘어간다.
- 올라서기: 매달린 난간에서 W·Space로만 된다. 장애물 앞에서는 넘기만 하고, 넘을 수 없으면 점프해서 난간을 잡는다. `Ability.mantleFromGround`를 true로 하면 예전처럼 땅·공중에서 바로 올라선다. 올라서기는 묵직하게 `Ability.mantleDuration` + 높이 1m마다 `mantleDurationPerMeter`가 걸리고, 클립이 그 진행도에 맞춰 재생된다.
- 넘기는 진행의 75%, 올라서기는 80%에서 손을 놓고 이동으로 돌아간다.
- 구르기는 `Ability.rollDuration` 동안 구르기 클립 전체가 재생된다. 세게·크게 착지하기 전 `rollInputWindow`(0.5초) 안에 Ctrl을 누르면 구르고, 착지할 때의 속도를 그대로 가져간다(가장 느려도 `rollSpeed`). `Body.rollFastSpeed`(6.5m/s)보다 빠르면 달리며 구르는 `rollFast`(mx_sprint_roll).
- 크게 착지해 쓰러져 있는 동안(`HeavyLanding`)은 쓰러지는 방향에 맞는 클립이 일어날 때까지의 시간에 맞춰 재생된다. 몸은 쓰러진 방향을 유지하고, 시선은 그 방향에서 좌우 `Body.heavyLandingTurnLimit`(90°)까지만 돈다(카메라가 눈에 붙어 있어서 더 돌면 자기 머리를 뚫어 본다).

## 상황별 클립

같은 상태라도 상황에 맞는 클립을 고른다. 모두 `Body` 행에서 이름과 기준값을 바꾼다.

- 점프: 도움닫기 속도에 따라 제자리(`jumpStill`), 걷기(`jumpSlow`), 달리기(`jump`).
- 공중에서 Ctrl: 무릎을 접는 `airTuck`(Beatrun 코일).
- 큰 낙하(`Ability.bigFallHeight`): 팔다리를 허우적거리는 `fallFlail`(Beatrun `fallinguncontrolled`). 큰 낙하 중 Ctrl은 착지 구르기만 준비하고 무릎을 접지 않는다. 이미 접고 있었으면 다리를 편다.
- 착지(단계는 [착지 단계](MovementGuide.md#착지-단계)): 세게 착지하면 무릎을 깊게 굽히는 `land`, 웅크린 채면 서지 않는 `landCrouch`. 크게 착지하면 손과 무릎을 짚었다 일어나는 `heavyLand`(mx_hard_landing), 아주 크게면 엎어졌다 일어나는 `heavyLandFaceDown`(mx_get_up), 뒤로 움직이던 중이거나 넉백이면 등으로 떨어졌다 일어나는 `heavyLandBack`(mx_stand_up_back). 가볍게 착지할 때 공중에서 무릎을 접었으면 다리를 펴는 `coilLand`/`coilLandCrouch`, 아니면 착지 동작 없이 바로 이어진다. 빠른 낙하 애니메이션은 `fallClipSpeed`(7.2m/s)부터.
- 슬라이드 끝: 웅크림·엎드림으로 이어지는 끝 동작.
- 넘기: 장애물 높이에 따라 한 손 `vault`, 1m 넘으면 두 손 `vaultHigh`. `vaultChainTime`(1.5초) 안에 다시 넘으면 직전과 다른 쪽을 쓴다. 같은 클립은 자기 자신과 섞일 수 없어서 첫 프레임으로 튀기 때문이다.
- 올라서기: 매달린 데서 올라가면 높이와 관계없이 `ledgeClimbUp`. `mantleFromGround`가 켜져 있을 때 땅에서 올라서면 높이에 따라 `stepUp`, `mantleLow`, `mantleMid`, `mantle`.
- 난간: 잡는 순간(`ledgeCatch`)과 좌우 이동(`ledgeShimmyLeft/Right`).
- 벽: 벽 오르기 시작(`wallClimbStart`), 튀는 방향별 벽 차기(`wallKick`/`wallKickRight`).
- 총: `armedClips`를 켜면 소총·권총을 들었을 때 해당 클립의 총 든 버전(`rifleSuffix`·`pistolSuffix`)을 쓴다. 이 클립들은 Source가 손을 총에 고정해 주는 것을 전제로 만들었다. 지금은 손을 몸의 손 IK가 총에 놓으므로 켜 볼 수 있지만, 아직 확인하지 않아 꺼 두었다.

## 머리 맞추기와 누운 자세

자세마다 클립 속 머리 위치가 다르므로(누워 미끄러지면 머리가 발 뒤쪽에 있다) 몸은 머리가 카메라 기준점 바로 뒤에 오도록 천천히 따라 움직인다(`Body.headFollowTime`). 매달릴 때는 손이 몸을 지지하고, 일반 슬라이딩 중에는 진행 방향 기준 위치를 유지한다.

누운 자세·누워 미끄러지기는 Beatrun의 `meslide` 계열을 쓴다. 엎드림은 전용 애니메이션이 없어 머리부터 슬라이딩 자세를 임시로 쓴다.

## 눈 시점과 시선

코드는 [PlayerBody.Look](../Scripts/Player/Body/PlayerBody.Look.cs)이다.

- 카메라는 몸의 눈에서 `eyeViewAhead`만큼 앞에 놓인다. 걸음의 흔들림, 착지·구르기의 꺼짐은 애니메이션에서 온다. 조준은 마우스 그대로이고, Q/E 기울이기의 충돌 검사된 이동량을 더한다. 정조준하면 `eyeViewTime` 동안 고정 조준 위치로 옮겨 가서 조준이 흔들리지 않는다.
- 눈 위치는 아바타에 연결된 LeftEye·RightEye의 중점이다. 눈 사이 간격은 캐릭터 크기에 비례해 검사하므로 Hiro처럼 눈이 넓게 배치된 캐릭터도 실제 눈 뼈(`pd.013`·`pd.014`)를 쓴다. 모델 기본 자세의 앞 방향은 양팔 뼈로 구한다. Hiro의 기본 자세가 모델 -Z를 향해도 눈과 `eyeViewAhead`·`eyeAdjust`를 해부학적 앞 방향으로 계산해 머리 로컬 좌표에 저장한다. 눈 뼈가 없거나 적합하지 않으면 같은 앞 방향과 카메라 높이로 추정한다. 위치를 조정하려면 Play 중 `Body.eyeAdjust`(위;앞)를 고친다.
- 벽 근처에서는 먼저 원하는 상체 자세의 머리·눈 여유 공간을 검사한다([PlayerBody.Clearance](../Scripts/Player/Body/PlayerBody.Clearance.cs)). 허리를 굽히면 얼굴이 벽에 닿는 경우, 닿지 않는 허리 굽힘 범위를 찾아 남은 시선 각도를 목·머리에 나눠 준다. 그 뒤에도 남는 충돌은 플레이어 캡슐 안의 일정한 기준점에서 눈까지 카메라 부피를 쓸어 검사한다([CameraClearance](../Scripts/Player/Camera/CameraClearance.cs)). 이 부피는 시야각·화면 비율·근거리 평면의 모서리까지 고려한다. 머리가 벽과 겹치는 순간마다 검사 시작점을 바꾸거나 뒤로 밀리는 양을 단순 보간하지 않는다.
- 몸은 머리가 카메라 기준점 바로 뒤에 오도록 계속 따라오므로(`headFollowTime`), 카메라는 플레이어 위치에서 옆으로 멀리 벗어나지 않는다.
- 위아래를 보면 허리(`spineBendDown`·`spineBendUp` 비율)·목·머리가 나눠 굽고, 머리는 카메라가 보는 곳을 본다.
- 골반이 시선과 다른 쪽을 보면 가슴·목·머리가 시선 쪽으로 비틀린다. 제자리에서 다리를 디딘 채일 때와 전력질주로 골반이 돌 때도 적용한다.
- 일반 슬라이딩 중 하체는 플레이어 캡슐에 대한 위치와 월드 진행 방향(`SlideDirection`)을 유지한다. 시선만 돌려서는 다리 위치·방향이 달라지지 않으며, 실제 이동·조향을 따라간다. 허리·가슴·목·머리만 시선을 부드럽게 따라간다. 좌우는 `Body.slideTurnLimit`(기본 각 75°), 위아래는 `Body.slidePitchLimits`(기본 위 40°·아래 55°)로 제한하며, 상체와 카메라가 같은 범위를 쓴다. 미끄러지는 방향이 시선의 반대쪽(벽에 튕겨 뒤로 밀림, 뒤로 슬라이딩 시작)이면 시선과 다리는 그대로이고 몸이 뒤로 미끄러진다. 옆으로 튕겨 기존 시선이 새 범위를 벗어나면 그 범위 안으로 서서히 돌아온다.
- 카메라 효과는 흔들림·기울이기와 기준 위치를 계산하고, 몸 코드가 그다음에 눈 시점의 최종 위치를 한 번 정한다(실행 순서 250). 기울이기 이동량을 직접 전달하므로 지난 프레임의 눈 시점이 다시 이동량으로 더해지지 않는다. 무기는 이후에 처리되어 총과 총알이 확정된 시점을 기준으로 동작한다.
- Esc로 일시정지하면 카메라 효과와 몸 자세 갱신을 함께 멈추어 마지막 눈 시점을 유지한다. 메뉴를 여는 순간 고정 카메라 위치로 바뀌거나, 재개하면서 눈에서 다시 멀어지지 않는다.
- **내 머리카락 보기**(Esc 설정, 기본 켬): 켜면 머리·머리카락(`First Person Shadow Only`에 적힌 렌더러)을 그린다. 카메라가 그 안에 있는 부분은 근거리 클리핑(`Body.headNearClip`, 0.05m)으로 잘린다.

## 몸의 손

코드는 [PlayerBodyHands](../Scripts/Player/Body/PlayerBodyHands.cs)와 [ArmIK](../Scripts/Player/Body/ArmIK.cs), 수치는 `Player.csv`의 `Hands` 행이다.

클립이 팔 자세를 잡은 뒤, 팔을 굽혀 손목을 목표에 놓는다. 팔꿈치는 자연스러운 쪽으로 향하고, 손이 닿지 않으면 어깨가 그쪽으로 돈다(`shoulderReach`). 손 방향은 캐릭터의 손가락 뼈로 잰 "손가락 방향·손바닥 방향"으로 정하므로 캐릭터를 바꿔도 맞는다.

- 총: 무기 모델 기준의 손목 위치·손가락 방향·손바닥 방향을 [Weapons.csv](../Resources/Tables/Weapons.csv)의 `rightGrip*`(방아쇠 손)·`leftGrip*`(받치는 손)에서 읽는다. 총 자세(지향·정조준·벽에 막혀 든 자세·반동·장전)는 지금처럼 카메라 기준 코드가 정하고 손이 따라간다. 지향 자세는 `holdOffset`만큼 몸 쪽으로 당겨서 팔이 닿게 했다(Hiro의 팔 길이는 약 0.51m). 벽에 막혀 총을 든 자세(하이레디)는 시선의 좌우만 따른다. 그래서 내려다보면 고개만 숙이고 총과 상체는 든 채로 있다.
- 손은 무기 코드 다음(실행 순서 320)에 놓이므로 총보다 늦게 따라오지 않는다. 넘기·올라서기·매달리기·벽 동작·구르기·봉 동작 중에는 손이 총을 놓는다.
- 주먹: [FistsView](../Scripts/Player/Combat/FistsView.cs)가 카메라 아래에 막기·충전·주먹 위치만 정하고, 몸의 손이 그 자리로 간다. 예전 상자 장갑은 없앴다.
- 난간: 매달리면 두 손이 몸 정면의 난간 모서리 양쪽(`ledgeHandSpacing`)을 잡는다.
- 넘기·올라서기: 손이 장애물 윗면 아래로 들어가면 윗면 위(`palmHeight`)로 올린다.
- F5 뒤에서 보기에서는 총·주먹 위치를 눈 위치 기준으로 옮겨서 손이 뒤쪽 카메라로 뻗지 않는다.
- 지금 장면의 총(`SK_AR_01`)은 재질을 비워 둬서 보이지 않는다. 보이게 하려면 그 오브젝트의 Inspector에서 재질 오버라이드를 Revert한다. 그 상태에서 손 위치를 맞추면 된다. 권총 모델은 아직 없어서 권총도 같은 총 위치를 쓴다.

### 난간 잡기

매달려 있는 동안 몸은 어디를 보든 벽을 향한다. 클립이 자세를 잡은 뒤에는 손목이 난간 윗면(`Body.ledgeGrip`)에 오도록 몸 전체를 옮긴다. 손은 그 자리에 있고 몸이 그 아래에 매달리므로 캐릭터나 클립이 바뀌어도 손이 허공을 잡지 않는다. 좌우 이동 클립은 실제 이동 속도에 맞춰 빨라진다(`shimmyClipSpeed`).

## 발 IK

코드는 [PlayerBody.Feet](../Scripts/Player/Body/PlayerBody.Feet.cs)이다. GMod IK Foot 애드온의 방식을 따랐다.

서 있거나 웅크려 땅에 있을 때(착지 동작 포함, 넘기 마무리 동작 제외) 발마다 뒤꿈치·발목·발끝 아래로 땅을 찾아(`Body.footReach`) 가장 높은 곳에 선다. 그래서 발 일부가 계단 속으로 들어가지 않는다. 한 발의 땅이 캡슐 바닥보다 낮으면(내려가는 계단·경사) 그 발이 닿도록 골반을 내리고(최대 `maxHipDrop`), 높은 쪽 발은 무릎을 굽혀 땅에 맞춘다. 무릎은 클립의 무릎 축으로만 굽고, 다리가 제 길이 축으로 돌지 않아서 발목이 꼬이지 않는다(GMod IK Foot도 허벅지·종아리를 앞뒤로만 굽힌다). 거의 편 다리는 무릎 축이 분명하지 않아서 골반 좌우 방향을 축으로 앞으로 굽는다. 발은 그 아래 땅 높이만큼만 옮기므로 클립의 걸음과 발 들기는 그대로 남는다. 땅을 디딘 발은 경사에 맞춰 기운다(`maxFootTilt`). 골반이 내려가면 눈과 카메라도 같이 내려간다.

- 땅과 골반 높이는 월드 기준으로 기억한다. 그래서 캡슐이 계단을 한 칸씩 툭 오르내려도 발이 계단을 뚫지 않는다. 높아진 땅에는 바로 올라서고, 낮아진 땅과 골반은 `footSettleTime`에 걸쳐 내려간다.
- 실행 중 Scene 뷰에서 플레이어를 선택하면 발마다 찾은 땅(초록 선)과 발목 목표(노란 구)가 보인다.
- 팔·발 IK는 같은 두 뼈 계산([TwoBoneIK](../Scripts/Player/Body/TwoBoneIK.cs))을 쓴다.
- **발목 제한**([AnkleLimit](../Scripts/Player/Body/AnkleLimit.cs)): 모든 상태에서 발이 정강이 축으로 돌아간 각도(`Body.maxAnkleTurn`)와 옆으로 꺾인 각도(`Body.maxAnkleRoll`)를 Hiro 기본 자세 기준으로 자른다. 발끝을 위아래로 젖히는 것은 자르지 않는다. 리타깃이 잘못된 클립이나 섞인 클립, 다리 IK 때문에 발목이 꼬여도 실제 발목이 가능한 범위 안에 머문다.
- **평균대**: 걷거나 달리며 건너는 동안에는 발을 [BalanceBeam](../Scripts/World/BalanceBeam.cs) 윗면 안쪽으로 옮겨 놓는다. 이때 난 발 디딤이 [평균대](MovementGuide.md#평균대)의 판정 기준이다.

## 사다리·집라인·평균대의 몸

클립은 [PlayerBody.Fixtures](../Scripts/Player/Body/PlayerBody.Fixtures.cs)가 고르고, 손을 사다리 가로대 면과 줄에 맞추는 것은 [PlayerBody.Grip](../Scripts/Player/Body/PlayerBody.Grip.cs)이다. 가로대와 줄에는 아직 손 IK가 없다. 클립의 손이 어긋나 보이면 `PlayerBodyHands`에 잡기를 더한다. 몸이 향할 수 있는 범위는 `Body.fixtureTurnLimit`이다.
