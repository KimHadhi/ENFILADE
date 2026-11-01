# 플레이어 몸 (진짜 1인칭)

[프로젝트 안내](../../../README.md) · [이동 안내](MovementGuide.md) · [설정·유지보수](MaintenanceGuide.md) · [검증 안내](ValidationGuide.md)

플레이어는 보이는 몸을 가진다. 몸은 이동 상태에 맞는 애니메이션을 재생하고, 카메라는 몸의 눈을 따른다(Beatrun Eye View처럼, 아래 **눈 시점**). 사다리에서는 상하 전환 때 시점이 튀지 않도록 기준 눈높이를 유지한다. 회전·기울기·충격 흔들림은 코드가 더한다. 내려다보면 허리·목이 같이 굽어 몸통·다리가 보인다. 머리·머리카락은 Esc 설정 **내 머리카락 보기**를 켜면 보이고, 끄면 그림자만 남는다. **F5**는 뒤에서 몸을 보는 확인용 시점이다.

## 구성

| 구성 | 위치 |
| --- | --- |
| 캐릭터 | 기본은 [KazusaBand](../Art/Models/Characters/KazusaBand)의 카즈사(밴드). Esc → 화면 → **캐릭터 스킨**에서 [Hiro](../Art/Models/Characters/Hiro)와 전환한다. 둘 다 휴머노이드이며 서 있는 눈높이가 카메라(1.6m)에 맞게 자동으로 크기가 정해진다 |
| 애니메이션 | [Art/Animations/Mixamo](../Art/Animations/Mixamo)의 26개 클립(서기·걷기·달리기 8방향, 넘기, 난간, 낙하·착지·구르기, 일어나기). [Art/Animations/Beatrun](../Art/Animations/Beatrun)의 173개 + 웅크려 옆걸음 2개(슬라이딩·다이브·웅크리기·벽·봉 등, 소총·권총 버전 포함). [Art/Animations/MocapOnline](../Art/Animations/MocapOnline)의 19개(총 든 이동, 사다리, 주먹). 휴머노이드라 어떤 캐릭터에도 옮겨 붙는다 |
| 연결 | `Resources/Characters/PlayerBody` 프로필(모델 + 클립 목록). 임포트할 때 자동으로 만들어지고 클립 목록도 자동 갱신된다 |
| 상태별 클립·연출 수치 | [Player.csv](../Resources/Tables/Player.csv)의 `Body` 행 (예: `Body.slide,slideloop`) |
| 자세별 캡슐·눈높이 | `Player.csv`의 `Posture` 행. 웅크림 0.8m, 슬라이딩 0.62m, 엎드림 0.37m, 누움 0.35m. 눈이 캡슐 꼭대기를 넘지 않게 맞췄다 |
| 동작 코드 | [PlayerBody](../Scripts/Player/Body/PlayerBody.cs). Play 시작 때 `PlayerMovement`가 붙인다 |

몸의 수치는 모두 표에 있고 Play 중에도 바로 반영된다. 클립 이름을 바꾸면 몸이 다시 만들어진다.

## 캐릭터 바꾸기

Esc → 화면 → **캐릭터 스킨**에서 카즈사(밴드)·히로를 고른다. 바꾸면 몸과 손발 IK를 다시 연결하고, 메뉴를 닫을 때 선택을 다른 설정과 함께 저장한다. 기본값으로 초기화하면 카즈사가 선택된다. F5 시점은 스킨을 바꿔도 유지된다.

새 모델을 `Art/Models/Characters/<이름>/`에 넣고, `Resources/Characters/PlayerBody` 프로필의 `Skins`에 고유 `Id`·표시 이름·모델을 추가한다. `Model`은 저장된 선택이 없거나 해당 스킨이 사라졌을 때 쓰는 기본 모델이다. 1인칭에서 숨길 부분은 프로필의 `First Person Shadow Only`에 렌더러 이름으로 적는다(기본 `Head`, `Hair`, `HeadAccessories`).

캐릭터는 [lilToon](https://github.com/lilxyzw/lilToon) 2.4.1(`Packages/jp.lilxyzw.liltoon`, MIT) 셰이더를 쓴다. 재질은 각 모델의 `Materials`에 따로 있어 Inspector의 lilToon 화면에서 그림자 색·림라이트·광택을 바로 조정한다. 새 캐릭터는 모델 Inspector → Materials → **Extract Materials** 후 셰이더를 `lilToon`으로 바꾼다.

카즈사는 KKBP FK `KazusaSFW.blend`(SFW 버전)를 [kkbp_model_to_fbx.py](../../../Tools/Blender/kkbp_model_to_fbx.py)로 변환했다. 얼굴·몸·머리카락·머리 장식을 나누고 표정 셰이프키와 실제로 가중치가 있는 보조 뼈를 보존했다. 사용하지 않는 뼈 가지를 제거해 1,067개에서 371개가 되었으며, 휴머노이드의 필수 뼈와 손가락·눈 54개를 명시적으로 연결한다. 눈 뼈로 크기를 맞춰 귀·헤일로가 눈높이 추정에 영향을 주지 않는다. `GagEyes`, `Tears`, `RiggedTongue`는 블렌더 드라이버에 의존하는 추가 효과여서 프리팹에서 꺼 두었다. 일반 표정 셰이프키는 얼굴 메시에 남아 있다. SFW 원본에서 제거한 몸의 노출 메시를 반영한다. 추가로 [KazusaBand.coverage.json](../../../Tools/Blender/KazusaBand.coverage.json)의 셔츠 안쪽 몸통 범위를 수출할 때 제외한다. 고개를 숙일 때 다른 가중치로 휘는 몸 표면이 셔츠를 뚫지 않도록 가려진 면 2,616개를 제거하며, 목·팔·허리 경계와 다리, 옷 자체는 유지한다. 모든 셰이프키와 UV를 보존하고 기존 프리팹·재질·스킨 선택·손 보정 연결도 유지한다.

KKBP 모델은 뼈 연결 뒤 **Rig → Configure → Pose → Enforce T-Pose → Apply**까지 해야 한다. 원본 FK 바인드 자세를 그대로 휴머노이드 기준 자세로 저장하면, 아바타가 유효하다고 표시돼도 카메라 반대 방향을 보고 팔을 내리는 동작에서 팔을 올릴 수 있다. 카즈사의 `.fbx.meta`에는 Unity의 자세 정렬을 적용한 기준 자세와 뼈의 부모 관계를 저장했다. 재수출할 때 이 메타데이터를 함께 보존하고, 모델별 문제를 고치려고 공유 애니메이션의 `yawOffset`을 바꾸지 않는다.

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

### MoCap Online에서 받을 때

UE4용 ZIP의 `Source/FBX`(Punches는 `Content/Boxing_01/Source/FBX`)에 클립마다 FBX가 하나씩 있다. UE4 마네킹 뼈대라 Unreal 없이 그대로 쓴다. 제자리 버전(`IP`·`IPC`, 없으면 `Root_Motion`)을 [MocapOnline](../Art/Animations/MocapOnline)에 넣고 `MocapOnline.clips.csv`에 행을 더한다. 클립 이름은 파일 이름이고, `yawOffset`은 180이다. 폴더의 `MocapOnline.tpose.fbx`는 팩의 `SKMannequin_T_Pose/SK_Mannequin.fbx`다. A자 자세 파일을 쓰면 안 된다.

- **Rifle 팩**: FBX가 없고 `.uasset`뿐이라 Unreal 에디터로 FBX를 내보내야 한다. 그 전까지 `Body.rifleMoves`는 권총 클립을 쓴다. 손은 IK가 총에 놓으므로 달라지는 것은 다리와 상체 자세다. Unreal에서 내보낸 FBX는 take 이름이 파일 이름과 다를 수 있으니 임포트 뒤 확인한다.
- **눈높이**: 웅크린 클립은 눈이 약 1.2m라 웅크린 카메라(0.8m)와 맞지 않아서 쓰지 않는다. 받아 둔 팩의 원본은 프로젝트 밖 `F:\GameProject\_MocapOnline`에 있고, 파일별 받기 횟수가 정해져 있다.
- **라이선스**: Standard License는 게임에 쓸 수 있지만 원본 파일을 따로 배포하면 안 되므로 저장소는 비공개로 둔다. 이 폴더의 FBX에 든 마네킹 메시는 Epic 것이다. 몸은 클립만 참조해서 메시는 빌드에 들어가지 않는다.

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
- **총 든 이동**: 총이 보이는 동안 서기·걷기·달리기·전력질주는 `Body.pistolMoves`·`Body.rifleMoves`의 클립을 쓴다. 순서는 서기, 걷기 앞·오른쪽·뒤·왼쪽, 달리기 앞·오른쪽·뒤·왼쪽, 전력질주다. 프로필에 없는 클립이 하나라도 있으면 맨손 클립을 쓴다. 웅크리기는 총을 들어도 그대로다. 클립은 왼발을 딛는 순간이 맞도록 `cycleOffset`을 넣어 맨손 클립과 같은 박자로 섞인다.
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
- 사다리에서는 눈 시점을 기준 카메라 위치로 부드럽게 옮겨 고정한다. 상승·하강 클립의 머리 높이 차이가 상하 반복 입력 때 화면을 밀지 않게 하며, 사다리 하강 종료 끄덕임은 `Punch.ladderSlideStopNod` 기본 0으로 둔다.
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

카즈사와 히로의 손은 손목 1개와 엄지·검지·중지·약지·새끼손가락 각 3마디로, 한 손 16개·양손 32개 뼈가 모두 Humanoid에 연결되어 있다. 손끝 뼈가 없어도 스킨의 끝을 측정한다. [HandGrip](../Scripts/Player/Body/HandGrip.cs)은 클립을 재생하기 전 기본 자세에서 각 마디 길이와 손가락 두께, 스킨의 손끝과 손바닥 중심·두께를 잰다. 그 뒤 손바닥 중심을 접촉 위치에 놓고 다섯 손가락을 각각 표면에 닿는 범위까지만 굽힌다. 봉에서는 엄지를 다른 손가락 반대쪽으로 모으며, 렛지에서는 엄지의 벌림과 마디 굽힘을 모델의 편안한 기본 자세로 유지한다. 실제 지지면에 닿을 때만 밑마디를 작은 범위에서 보정한다. 손가락의 캡슐 근사뿐 아니라 실제 스킨 웨이트를 가진 마디별 표본 정점도 움직여 관통을 검사한다. 표본 사이 삼각형에는 작은 여유를 둔다. 봉 단면이나 손에 대한 표면 위치가 같으면 계산한 자세를 재사용하므로, 잡은 채로 있는 동안 정밀 탐색을 매 프레임 반복하지 않는다. 피부 전체를 변형하는 물리 시뮬레이션은 아니다.

- 총: 무기 모델 기준의 손목 위치·손가락 방향·손바닥 방향을 [Weapons.csv](../Resources/Tables/Weapons.csv)의 `rightGrip*`(방아쇠 손)·`leftGrip*`(받치는 손)에서 읽는다. 총 자세(지향·정조준·벽에 막혀 든 자세·반동·장전)는 지금처럼 카메라 기준 코드가 정하고 손이 따라간다. 지향 자세는 `holdOffset`만큼 몸 쪽으로 당겨서 팔이 닿게 했다(Hiro의 팔 길이는 약 0.51m). 벽에 막혀 총을 든 자세(하이레디)는 시선의 좌우만 따른다. 그래서 내려다보면 고개만 숙이고 총과 상체는 든 채로 있다.
- 손은 무기 코드 다음(실행 순서 320)에 놓이므로 총보다 늦게 따라오지 않는다. 넘기·올라서기·매달리기·벽 동작·구르기·봉 동작 중에는 손이 총을 놓는다.
- 주먹: [FistsView](../Scripts/Player/Combat/FistsView.cs)가 카메라 아래에 막기·충전·주먹 위치만 정하고, 몸의 손이 그 자리로 간다. 예전 상자 장갑은 없앴다. 주먹을 올린 동안 손가락은 `Body.fistClench` 클립을 따라 주먹을 쥔다.
- 주먹질: [PlayerBody.Layers](../Scripts/Player/Body/PlayerBody.Layers.cs)가 이동 클립 위의 층에서 주먹 클립을 재생한다. 골반과 다리는 이동 클립 그대로라 걸으면서 칠 수 있다. 치는 팔은 클립을 따르고(`Body.punchBlendIn`·`punchBlendOut`) 반대 손은 주먹 위치에 남아 화면 구도를 지킨다. 왼손·오른손이 번갈아 나가고, 차징이 `Body.punchHardCharge`를 넘으면 `punchLeftHard`·`punchRightHard`를 쓴다. 판정은 버튼을 뗀 순간에 시작되므로, 클립은 다 뻗기 4~7프레임 전부터 잘라(`clips.csv`의 `from`·`to`) `Body.punchClipSpeed`로 재생한다. 클립의 상체가 머리를 앞으로 데려가므로 그동안 카메라는 `Body.punchEyeSteady` 비율만큼 고정 위치 쪽으로 옮겨 간다. 가드나 패링을 시작하면 클립은 바로 팔을 돌려준다. 클립이 없으면 예전처럼 주먹 위치만 움직인다.
- 난간: [PlayerBodyHands.Ledge](../Scripts/Player/Body/PlayerBodyHands.Ledge.cs)가 두 손의 실제 윗면을 따로 찾고(`ledgeHandSpacing`), 그 면의 높이·기울기에 손바닥을 놓는다. 손바닥 중심은 모서리 바로 안쪽에 두며(`Hands.ledgePalmInset`, 손바닥 길이의 기본 15%), 엄지는 모델의 편안한 기본 모양을 유지한다. `PlayerBody` 프로필의 각 `Skins` 항목에서 `Ledge Hand Spacing Scale`과 `Ledge Palm Inset Scale`을 조절하면 캐릭터별 손 간격과 걸치는 깊이를 바꿀 수 있다(둘 다 기본 1). 실제 손바닥·손가락 치수는 해당 모델에서 다시 측정한다. 엄지의 가운데·끝마디는 기본 굽힘을 보존하고, 지지면과 접촉하면 밑마디만 최대 12도 안에서 여유를 만든다. 모서리 밖의 빈 공간은 실제 지지면과 구분한다. 다른 네 손가락과 사다리의 기존 잡기 자세는 유지한다. [HandSkinCorrection](../Scripts/Player/Body/HandSkinCorrection.cs)은 엄지 밑부분의 접힌 피부를 최대 2.5mm 범위에서 완화하고 표면 방향을 접촉 자세에 맞춰 보정한다. UV 경계의 같은 위치를 묶어 틈이 생기지 않게 하며 지지면 안으로 밀어 넣지 않는다. 다른 네 손가락에는 적용하지 않는다. 실제 모델 메시의 복사본에 보정을 만들고 원본 모델과 기존 표정은 유지한다. 놓을 때는 접촉 가중치에 따라 해제한다. 끝부분에서 손이 허공을 짚으려 하면 안쪽의 윗면을 찾는다. 손가락은 지지 콜라이더를 뚫지 않는 범위까지만 굽힌다. 상자 지지는 선분과 상자의 거리로 계산하고, 다른 지지는 표면을 검사한다.
- 난간의 손가락 자세는 잡기로 정한 손바닥·손목 목표 좌표에서 계산한다. 실제 손목의 미세한 추적 오차나 시선 회전은 잡기 자세를 다시 풀게 하지 않는다. 모든 손가락을 기본 자세로 돌린 뒤 한 번에 계산해 이웃 손가락의 클립 동작이 해에 섞이지 않도록 했다. 실제 접촉 모양이 바뀔 때의 손가락 전환도 부드럽게 이어진다. 이 계산을 위해 잠시 사용한 목표 좌표는 복구하며 실제 손목 뼈를 이동하거나 늘리지 않는다.
- 사다리: [PlayerBodyHands.Ladder](../Scripts/Player/Body/PlayerBodyHands.Ladder.cs)가 실제 가로대에 잡는 위치를 기억한다. 마지막으로 올라간 손은 한 칸 위를 잡고, 다음 동작 때 반대 손만 그 위의 가로대로 옮긴다. 지지하는 손은 클립이나 몸의 이동을 따라 밀리지 않는다. 이동하는 손은 앞쪽으로 살짝 떼었다가 다시 잡고, [HandGrip](../Scripts/Player/Body/HandGrip.cs)이 손가락을 가로대 둘레로 굽힌다. 간격·손 이동 궤적·손바닥 여유는 `Hands.ladderHandSpacing`·`ladderReachArc`·`ladderPalmClearance`다. 빠르게 내려갈 때는 양옆 세로대를 잡는다. 손바닥은 세로대의 앞쪽·바깥쪽 모서리를 비스듬히 잡아 엄지는 안쪽에서, 다른 네 손가락은 바깥쪽에서 감싸도록 한다. 하강 중 손은 어깨보다 팔 길이의 25% 아래를 잡고 팔꿈치는 아래·바깥쪽으로 내려, 좁은 사다리에서도 팔이 눈 시점 안으로 들어오지 않게 한다. 손가락 접촉도 실제 사각 단면의 방향을 유지해 모서리에 맞춰 계산한다. 이 방향은 사다리의 회전을 따른다. 가로대·세로대 사이에서는 손 위치와 회전을 이어 주고 손가락을 잠깐 풀었다가 다시 감싼다. 하강 후 가로대를 다시 잡는 양손의 진행률은 따로 갱신하므로, 새 상승을 시작해도 반대 손의 잡기가 중간에 멈추지 않는다.
- 엄지는 사다리에서도 모델의 기본 마디 방향과 굽힘을 보존한다. 세 마디를 일렬로 정렬하지 않으며, 밑마디·가운데·끝마디를 각각 최대 20·30·40도 범위에서 접촉까지 굽힌다. 피부가 닿을 때 밑마디를 드는 보정도 최대 20도다.
- 가로대는 실제 사각 단면과 모서리에 맞춰 손가락을 굽힌다. 손바닥의 피부 두께를 먼저 반영하고 `Hands.ladderPalmClearance`(기본 1.5mm)를 더한다. 손가락 피부의 표면 여유는 사다리·난간 공통 `Hands.fingerSurfaceClearance`(기본 1mm)다. 작은 손이나 굵은 봉은 손이 닿는 범위만 감싸며, 뼈를 늘리거나 손가락을 표면 안으로 밀어 넣지 않는다. 손을 옮길 때는 먼저 펴고, 다음 가로대에 도착하며 다시 감싼다.
- 사다리 꼭대기: 올라서기의 `ObstacleTarget.ExitLadder`로 출발한 사다리를 이어받는다. 높은 손은 고정하고 낮은 손을 같은 가로대로 모은 뒤, 몸을 당기는 동안 양손이 계속 가로대를 잡는다. 팔이 닿지 않으면 눈 시점을 놓기 전에 몸도 필요한 만큼 옮긴다. 이후 높은 손·지지 손 순서로 부드럽게 놓는다(`Hands.ladderExitRelease`, 동작 진행률 기준). 놓은 손은 올라서기 클립과 장애물 윗면 보정을 따르며, 다른 곳의 일반 올라서기는 사다리 잡기를 사용하지 않는다.
- 넘기·올라서기: 손이 장애물 윗면 아래로 들어가면 윗면 위(`palmHeight`)로 올린다.
- F5 뒤에서 보기에서는 총·주먹 위치를 눈 위치 기준으로 옮겨서 손이 뒤쪽 카메라로 뻗지 않는다.
- 지금 장면의 총(`SK_AR_01`)은 재질을 비워 둬서 보이지 않는다. 보이게 하려면 그 오브젝트의 Inspector에서 재질 오버라이드를 Revert한다. 그 상태에서 손 위치를 맞추면 된다. 권총 모델은 아직 없어서 권총도 같은 총 위치를 쓴다.

### 난간 잡기

매달려 있는 동안 몸은 어디를 보든 벽을 향한다. 클립이 자세를 잡은 뒤에는 양손의 측정된 손바닥 접촉 위치에서 손목 목표를 계산하고, 그 목표에 맞춰 몸 전체를 옮긴다. 손과 몸이 같은 기준을 쓰며 눈 시점도 그 뒤에 놓는다. 손 접촉을 계산할 수 없을 때는 기존 `Body.ledgeGrip` 위치를 몸 정렬의 기준으로 쓴다. 좌우 이동 클립은 실제 이동 속도에 맞춰 빨라진다(`shimmyClipSpeed`).

## 발 IK

코드는 [PlayerBody.Feet](../Scripts/Player/Body/PlayerBody.Feet.cs)이다. GMod IK Foot 애드온의 방식을 따랐다.

서 있거나 웅크려 땅에 있을 때(착지 동작 포함, 넘기 마무리 동작 제외) 발마다 뒤꿈치·발목·발끝 아래로 땅을 찾아(`Body.footReach`) 가장 높은 곳에 선다. 그래서 발 일부가 계단 속으로 들어가지 않는다. 한 발의 땅이 캡슐 바닥보다 낮으면(내려가는 계단·경사) 그 발이 닿도록 골반을 내리고(최대 `maxHipDrop`), 높은 쪽 발은 무릎을 굽혀 땅에 맞춘다. 무릎은 클립의 무릎 축으로만 굽고, 다리가 제 길이 축으로 돌지 않아서 발목이 꼬이지 않는다(GMod IK Foot도 허벅지·종아리를 앞뒤로만 굽힌다). 거의 편 다리는 무릎 축이 분명하지 않아서 골반 좌우 방향을 축으로 앞으로 굽는다. 발은 그 아래 땅 높이만큼만 옮기므로 클립의 걸음과 발 들기는 그대로 남는다. 땅을 디딘 발은 경사에 맞춰 기운다(`maxFootTilt`). 골반이 내려가면 눈과 카메라도 같이 내려간다.

- 땅과 골반 높이는 월드 기준으로 기억한다. 그래서 캡슐이 계단을 한 칸씩 툭 오르내려도 발이 계단을 뚫지 않는다. 높아진 땅에는 바로 올라서고, 낮아진 땅과 골반은 `footSettleTime`에 걸쳐 내려간다.
- 발의 수평 위치는 애니메이션을 따른다. 발밑을 놓쳤을 때 마지막 높이·기울기 보정은 `footSettleTime`에 걸쳐 풀고, 다시 지지를 찾으면 같은 시간에 보정의 영향을 늘린다. 실제 지지가 없는 발은 발소리를 내지 않는다. 이 표현은 물리 접지를 결정하지 않는다.
- 실행 중 Scene 뷰에서 플레이어를 선택하면 발마다 찾은 땅(초록 선)과 발목 목표(노란 구)가 보인다.
- 팔·발 IK는 같은 두 뼈 계산([TwoBoneIK](../Scripts/Player/Body/TwoBoneIK.cs))을 쓴다.
- **발목 제한**([AnkleLimit](../Scripts/Player/Body/AnkleLimit.cs)): 모든 상태에서 발이 정강이 축으로 돌아간 각도(`Body.maxAnkleTurn`)와 옆으로 꺾인 각도(`Body.maxAnkleRoll`)를 Hiro 기본 자세 기준으로 자른다. 발끝을 위아래로 젖히는 것은 자르지 않는다. 리타깃이 잘못된 클립이나 섞인 클립, 다리 IK 때문에 발목이 꼬여도 실제 발목이 가능한 범위 안에 머문다.

## 사다리·집라인의 몸

클립은 [PlayerBody.Fixtures](../Scripts/Player/Body/PlayerBody.Fixtures.cs)가 고른다. 사다리에서는 [PlayerBody.Grip](../Scripts/Player/Body/PlayerBody.Grip.cs)이 어깨를 사다리 중앙의 등반 거리로 맞추고, [PlayerBodyHands.Ladder](../Scripts/Player/Body/PlayerBodyHands.Ladder.cs)가 양손을 각각 실제 가로대에 고정한다. 오르는 손은 `LadderLeftHand`와 실제 한 칸 이동 진행률(`LadderStepProgress`)을 따른다. 한 칸 오르기와 멈춤은 MoCap Online의 가로대 간격 400mm 클립(`Ability.ladderRungHeight` 0.39m와 맞다)이고, 잡기·꼭대기 넘기·미끄러져 내려오기는 게임의 짧은 시간에 맞는 Beatrun 클립이다. 원래 두 칸을 오르는 반복 클립을 `clips.csv`의 `to`로 절반만 쓰고, `Body.ladderClipSpeed`로 `Ability.ladderRungInterval`에 맞춘다. `ladderRungInterval`을 바꾸면 이 값도 함께 바꾼다. 클립 이름의 `LU`는 왼발이 위라는 뜻이라 손은 반대로 오른손이 위다. 집라인에서는 몸 전체를 옮겨 손을 줄에 맞추며 개별 손 IK는 아직 없다. 몸이 향할 수 있는 범위는 `Body.fixtureTurnLimit`이다.
