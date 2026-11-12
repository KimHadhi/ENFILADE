# 처형·암살 짝 애니메이션

사용자(2026-11-12, 총관리자 전달): "처형 애니메이션 만들어서 넣어 줘. 내가 블렌더로 수정할 수 있게도 해 놔. 좀비 calm idle 모션 보면 몇 놈은 누워 있거나 앉아 있거나 일어서 있던데 이거 각각 맞춰서 알아서 준비해 놔. 내가 후처리 봐 줄 테니까."

처형(체간이 무너진 좀비)과 암살(들키지 않은 좀비를 등 뒤에서)은 좀비가 지금 무엇을 하고 있는지에 맞춘 **짝 애니메이션**으로 한다. 한 짝은 플레이어 클립 하나와 좀비 클립 하나이고, 둘은 같은 뿌리(좀비의 발 자리)를 쓴다. 모든 PC가 같은 짝을 같은 자리·같은 시간에 튼다.

## 짝

| 짝(표 id) | 고르는 때 | 동작 | 길이 · 처치 |
|---|---|---|---|
| `back` | 서 있는 좀비, 플레이어가 등 뒤 90° 안 | 왼손으로 입을 막아 머리를 젖히고 목 옆을 찌름 | 1.23초 · 0.50초 |
| `front` | 서 있는 좀비, 그 밖(체간이 무너진 좀비) | 멱살을 잡아 끌고 관자놀이를 내리찍은 뒤 밀어냄 | 1.77초 · 0.67초 |
| `sit` | 앉아 조는 자세(ZombieLooks calmPoses의 sit) | 뒤에서 무릎 꿇고 뒤통수를 누른 채 목덜미를 찌름 | 1.37초 · 0.50초 |
| `feed` | 무릎 꿇고 먹는 자세 | 머리채를 잡아 일으키고 목 옆을 찌름 | 1.43초 · 0.53초 |
| `crouch` | 웅크린 자세 | 머리채를 잡아 젖히고 목 옆을 찌름 | 1.37초 · 0.50초 |
| `prone` | 엎드린 자세(엎드려 먹기·꿈틀대기), 다리를 잃고 기는 좀비, 엎어져 넘어진 좀비 | 옆에 무릎 꿇고 뒤통수를 누른 채 목덜미를 찌름 | 1.50초 · 0.57초 |
| `supine` | 등을 대고 넘어진 좀비 | 옆에 무릎 꿇고 가슴을 누른 채 얼굴을 내리찍음 | 1.57초 · 0.63초 |

암살 짝(`kind` takedown)은 짧고 조용하게, 처형 짝(`execution`)은 조금 길고 묵직하게 잡았다. 숫자는 블렌더에서 보낼 때 표에 다시 적힌다.

고르는 규칙은 [ZombieBody.Executed](../Scripts/Enemies/ZombieBody.Executed.cs)의 `ExecutionState`: 넘어져 누운 몸(인형에서 막 일어선 순간 포함)이면 가슴이 바닥을 보는지로 `prone`/`supine`, 기는 좀비면 `prone`, 조용한 자세가 보이면 자세 클립 이름(sit·feed·crouch·FD/crawl)으로, 모더의 새 자세는 하는 일(Eat·Doze·Rest)과 머리 높이로, 나머지는 서 있는 좀비로 보고 플레이어가 등 뒤에 있는지로 `back`/`front`. 표에 그 줄이 없거나 클립이 없으면 예전 처형(제자리 칼질)이 그대로 나온다.

## 게임에서 어떻게 도는지

1. **처형이 시작되면**([PlayerExecution](../Scripts/Player/Combat/PlayerExecution.cs)) 플레이어는 예전처럼 달려든다(F도). 달려들기가 끝나는 프레임에 처형하는 PC의 [ExecutionPairPlayer](../Scripts/Player/Combat/ExecutionPairPlayer.cs)가 짝을 고르고 뿌리를 놓는다.
   - 서 있는 좀비: 뿌리는 좀비 발 자리, 방향은 좀비가 보는 쪽에서 표의 `turn` 몫만큼 플레이어 쪽으로(뒤 0.5, 앞 1: 무너진 좀비는 돌려세워 마주 봄). 나머지는 플레이어가 돌아 들어간다.
   - 앉음·먹기·웅크림·엎드림: 좀비 자리와 방향 그대로(`turn` 0).
   - 넘어져 누운 좀비: 처형이 몸을 인형에서 떼는 순간의 골반·머리 자리에, 클립 첫 프레임의 골반·머리 방향(`zHipsRight`·`zHipsForward`·`zHeadYaw`)이 겹치게 놓는다.
2. **두 몸이 같은 시계로 튼다.** 플레이어 몸([PlayerBody.Execution](../Scripts/Player/Body/PlayerBody.Execution.cs))은 맨 위 층에 온몸 클립을 얹고, 몸 뿌리가 `align`초에 걸쳐 짝의 자리(`right`·`forward`·`yaw`)로 옮겨 간다. 캡슐도 같이 미끄러지고 그동안 희생자 캡슐과는 부딪치지 않는다. 그 동안 시선 굽히기·발 딛기·머리 따라가기는 쉬고, 시점은 클립의 머리를 따라 조금 움직인다(몸이 이끄는 동작처럼). 좀비 몸([ZombieBody.Executed](../Scripts/Enemies/ZombieBody.Executed.cs))은 맨 위 입력(21)에 좀비 클립을 얹어 다른 동작을 다 덮고, 뿌리를 짝의 뿌리에 둔다. 둘 다 `blendIn`초에 지금 자세에서 섞여 들어가고 `blendOut`초에 돌아온다.
3. **처치 순간**(`kill`): 처형의 타격이 이때 온다(`PlayerExecution.Retime`이 표의 impactTime·duration 대신 짝의 처치·길이를 쓴다). 칼이 닿는 좀비 뼈(`hitBone`)에서 피가 터지고, 맞은 방향은 짝의 `fall*` 쪽이라 시체가 그쪽으로 무너진다. 죽은 좀비는 그 순간의 클립 자세에서 근육이 0.3초에 풀리며 래그돌이 된다(ZombieCorpse). 거구처럼 버티는 좀비는 좀비 클립의 나머지(제자리에서 접었다 폄)를 하고 원래 동작으로 돌아간다.
4. **모든 PC에 같게**(사용자의 규칙 "내가 보는 화면이 진실"): 처형하는 PC가 짝 번호·좀비·뿌리 자리·방향·시작 시각을 보낸다([PlayerNetworkCombat](../Scripts/Network/PlayerNetworkCombat.cs) `PairBeganRpc`). 다른 PC는 그 사람의 몸과 그 좀비 몸에 같은 짝을 같은 자리에 틀고, 말이 오는 데 걸린 시간만큼 앞당겨 시작한다. 피는 PC마다 자기 시계의 처치 순간에 같은 뼈에서 터지고, 짝은 그 시계로 짝 길이에서 끝난다. 그 PC의 처형 시간도 짝에 맞춰져서(`Retime`, 앞당긴 만큼 빼고) 그 사람의 클래스 칼도 같은 때 찌르고 넣는다. 판정은 예전 그대로 방장이 한다(좀비를 잡아 주거나 거절, 피해는 방장이 받음). 거절되거나 끊기면 `PairStoppedRpc`로 모두 멈춘다.
5. **손 붙이기**: 왼손이 머리·멱살·가슴을 잡는 동안, 칼이 목·머리에 박혀 있는 동안(표의 `l*`·`r*`: 뼈, 시작·끝, 그 뼈에서 손목까지)은 손·총의 IK가 끝난 뒤, 클래스 칼이 주먹에 칼을 앉히기 바로 앞에서([ExecutionPairHands](../Scripts/Player/Combat/ExecutionPairHands.cs), 0.8.297) 두 뼈 IK로 손을 그 뼈에 붙여 둔다. 그래서 칼도 붙여 놓은 주먹을 따라 목에 박힌다. 클립을 만든 몸(카즈사와 Synty 좀비)에서는 거의 움직이지 않고, 다른 캐릭터나 덩치 큰 거구(`ZombieBody.Grown`, 자리도 그만큼 멀어짐)에서 손이 허공을 잡지 않게 한다.
6. **칼·총**: 손의 칼, 총 치우기, 뽑기·박히기·넣기 소리는 클래스 칼([ExecutionKnife](../Scripts/Player/Combat/Knives/ExecutionKnife.cs))이 맡는다. 짝이 도는 동안은 `ExecutionKnife.ArmsPosedElsewhere`를 켜서 칼은 주먹에 따라가고 팔은 짝 클립이 움직인다. 칼의 찌르기 타격 시간은 찌르기가 시작될 때 처형에서 다시 읽어서(0.8.290) 짝의 처치에 맞는다. 타격 뒤의 칼 빼기·칼집에 넣기·총 다시 들기는 짝의 남은 시간에 맞춰 늘어난다(`PlayerExecution.Duration`: 칼 찌르기의 타격 뒤 0.57초가 짝의 타격 뒤 0.73~1.1초로). 그래서 칼은 짝의 끝 무렵 손이 내려온 뒤에 들어가고 총은 짝이 끝날 때 돌아온다. 팔은 짝이 시작할 때 칼을 든 자세에서 클립 자세로 0.12초, 짝이 일찍 끊기면 클립에서 칼 쪽 팔로 0.25초에 걸쳐 넘어가서 튀지 않는다([ExecutionKnifeArm](../Scripts/Player/Combat/Knives/ExecutionKnifeArm.cs)).

클립은 휴머노이드라 어떤 플레이어 캐릭터, 어떤 인간형 좀비 몸에도 맞는다(모더의 FBX 캐릭터 포함). 클립은 플레이어 몸 프로필(`Resources/Characters/PlayerBody`, PlayerBodyProfileBuilder가 Art/Animations를 모음)에서 이름으로 찾는다.

## 블렌더로 다듬기

`ArtSource/Executions/START_EXECUTION_ANIMATION.cmd` → [START_HERE.md](../../../ArtSource/Executions/START_HERE.md). 한 장면에 카즈사 True FPS 리그와 Synty 좀비 리그가 게임 크기로 서 있고, 짝마다 두 리그의 시안이 잠긴 층, 그 위에 빈 손질 층이 있다. **게임에 보내기** 한 번이 두 FBX(`Art/Animations/Executions/enf_exe_<짝>_p`·`_z`)와 표 줄을 쓴다. 도구는 `Tools/Blender/Executions`([README](../../../Tools/Blender/Executions/README.md)). 임포트는 [ModelImportRules](../Editor/Import/ModelImportRules.cs)가 `_p`는 카즈사 아바타, `_z`는 Synty 좀비 아바타로 받는다(블렌더가 GUID만 든 .meta를 먼저 쓰므로 설정은 가져올 때마다 정한다. 규칙이 컴파일되기 전에 들어온 클립은 컴파일 뒤 한 번 다시 가져온다).

시안은 Claude가 블렌더에서 코드로 짰다(`execution_pairs.py`의 숫자): 좀비는 팩의 자세 클립 첫 프레임에서 시작해 몸축 기준으로 꺾고 떨게 했고, 카즈사는 IK로 손을 좀비 머리·목에 맞췄다. 카즈사 리그의 다리 IK는 무릎이 옆으로 꺾이는 극 각도라 이 작업 파일에서만 −90°로 바로잡았다(다른 작업실은 그대로).

## 표 [Executions.csv](../Resources/Tables/Executions.csv)

줄 하나가 짝 하나(id = 좀비 상태). 블렌더가 다시 쓰는 열: `kind` `playerClip` `zombieClip` `length` `kill` `right` `forward` `yaw` `zHips*` `zHeadYaw` `l*` `r*`. 손으로 고치는 열: `turn`(좀비가 돌아 맞추는 몫) `align`(플레이어가 미끄러져 들어가는 시간) `blendIn` `blendOut` `hitBone`(피가 나는 뼈) `fallRight`·`fallUp`·`fallForward`(시체를 미는 쪽). `Knife` 줄은 블렌더 미리보기 칼 자리만 정한다. 열마다 설명은 표의 `#설명` 줄.

## 시험

1. Unity를 누르면 `Art/Animations/Executions`의 FBX 14개를 휴머노이드로 (다시) 가져온다. Console에 Rig 오류가 없어야 한다.
2. 콘솔 `/poses stay`: 자세마다 조용한 좀비가 하나씩 선다(앉아 졸기·먹기·웅크리기·엎드려 먹기·엎드려 꿈틀대기). 각각 등 뒤에서 F → 그 자세의 짝이 나와야 한다(엎드린 둘은 `prone`).
3. `/spawn 3 calm`의 서 있는 좀비를 등 뒤에서 F → `back`.
4. 좀비를 패링·주먹으로 무너뜨리고 앞에서 F(또는 주먹 좌클릭) → `front`, 등 뒤에서 → `back`.
5. 무너진 좀비를 대시 밀치기로 넘어뜨린 뒤 누운 채 F → 엎어졌으면 `prone`, 등을 댔으면 `supine`. 다리를 잃고 기는 좀비 → `prone`.
6. 거구(`/spawn brute`)를 무너뜨려 처형 → 손이 머리에 붙고, 살아남으면 접었다 펴고 다시 싸운다.
7. 둘이서: 친구가 처형할 때 내 화면에서도 같은 짝이 같은 자리에서 나오는지, 피가 같은 데서 터지는지.
8. 3인칭 시네마틱을 끄고(Esc → 카메라) 1인칭으로도 해 본다: 시점이 눈을 따라가고 손이 화면에 보여야 한다.
9. 칼과 짝이 팔을 주고받는 순간: 달려들며 칼을 든 팔이 짝 클립 자세로 튀지 않고 넘어가는지, 짝 끝 무렵 손이 내려온 뒤 칼이 들어가고 짝이 끝날 때 총이 돌아오는지(그 전에 총이 나오지 않는지). 처형 중 채팅을 열어 끊으면(처형이 끊기는 경우) 팔이 칼 쪽으로 부드럽게 돌아오는지.

## 아직 모자란 것

- 시안이다: 타이밍과 자세의 맛은 블렌더에서 다듬을 것(특히 `front`의 좀비 팔, 앉음·엎드림의 무릎).
- 총으로 하는 처형(머리에 대고 쏘기)이나 머리 터뜨리기 짝은 없다. 모두 칼.
- 좀비가 벽에 붙어 있으면 플레이어 자리가 벽 안일 수 있다(캡슐은 벽에 막히고 몸만 그 자리로 간다).
