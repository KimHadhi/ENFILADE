# 애드온 이식 기록

[프로젝트 안내](../../../README.md) · [이동 안내](MovementGuide.md) · [플레이어 몸](PlayerBodyGuide.md) · [설정·유지보수](MaintenanceGuide.md)

GMod 애드온(`F:\Addons`, 저장소 밖)의 이동·애니메이션·소리를 ENFILADE로 옮긴 기록이다. Lua 코드를 그대로 가져오지 않고 규칙과 수치를 Unity 구조에 맞춰 다시 만들었다. 애니메이션과 소리는 변환해서 넣었다.

## 어디서 무엇을 옮겼나

| 애드온 | 옮긴 것 | 지금 위치 |
| --- | --- | --- |
| Beatrun Community Edition | 넘기·올라서기 판정과 이동식, 사다리·집라인, 시점 펀치 값, 세게 착지, 숨소리 모델, 동작 소리와 재생 순서 | [VaultFinder](../Scripts/Player/Movement/Parkour/VaultFinder.cs), [VaultMotion](../Scripts/Player/Movement/Parkour/VaultMotion.cs), [Parkour](../Scripts/Player/Movement/Parkour), [PlayerMovementPunch](../Scripts/Player/Camera/PlayerMovementPunch.cs), [MovementLanding](../Scripts/Player/Movement/Actions/MovementLanding.cs), [PlayerBreathing](../Scripts/Player/Audio/PlayerBreathing.cs), [SoundCues.csv](../Resources/Tables/SoundCues.csv) |
| Beatrun Actual Thirdperson animations | 여성 3인칭 세트(`tp_climbanim_f`)를 Hiro 뼈대에 구운 클립 | [Art/Animations/Beatrun](../Art/Animations/Beatrun) |
| Eye View Attachment for Beatrun | 카메라를 몸의 눈에 붙이기 | [PlayerBody.Look](../Scripts/Player/Body/PlayerBody.Look.cs) |
| 2026 IK Foot (High FPS Version) | 발 IK. 2026-11-07 CAS Foot IK로 바꿨다가 무릎이 비틀려 되돌렸고, CAS에서 자기 충돌체를 땅으로 치지 않는 점만 가져왔다 | [FootPlanting](../Scripts/Player/Body/FootPlanting.cs) |
| Anim-Based Footsteps Timing | 발이 실제로 닿는 순간의 발소리 | [PlayerFootsteps](../Scripts/Player/Audio/PlayerFootsteps.cs) |
| Dynamic Landing | 크게 착지해 쓰러지기, 돌진 밀치기, 착지·구르기·밀치기 소리 | `MovementLanding`, [PlayerCombatActions](../Scripts/Player/Combat/PlayerCombatActions.cs), [Art/Audio/Movement](../Art/Audio/Movement) |
| FEAR-like SFX for Various VManip Addons | 일부 표면의 착지·슬라이드, 포복, 렛지, 기울이기, 옷 소리 | `Art/Audio/Movement`, 나머지는 [Art/Audio/Library](../Art/Audio/Library) |
| [wOS] DynaBase, Mixamo 확장 | 착지 클립을 시험했다. 지금은 mixamo.com에서 직접 받은 클립으로 바꿔서 남아 있지 않다 | [Art/Animations/Mixamo](../Art/Animations/Mixamo) |
| [MW] Quick Throwing Knife | 소리만 보관. 기능은 이후 전투 작업에서 다룬다 | `Art/Audio/Library/ThrowingKnife` |
| Advanced Stealth | 소리만 보관. 잠입·분대 AI는 별도 계획이 필요하다 | `Art/Audio/Library/AdvancedStealth` |
| [ArcCW] Arctic's Customizable Weapons (Base) (워크숍 2131057232, Horde가 쓰는 무기 베이스) | 총알 관통과 도탄(`DoPenetration`, `GetRicochetChance`, `PenTable`), 총마다 관통력은 Horde의 ArcCW 무기 수치(M4A1 10, G17 4), 도탄 소리, 앉아 쏘기 배율 규칙(`arccw_mult_crouchdisp`·`arccw_mult_crouchrecoil`, 값은 TFA 베이스의 0.5·0.65), 자동 장전(`arccw_automaticreload`), 뷰모델 흔들림(`cl_viewmodel.lua`의 coolsway 중 움직임·걸음·숨쉬기. 숨쉬기는 총구가 들리게 바로 세웠고 질주에서 커지는 출렁임은 뺐다. 시선 흔들림은 아래 TFA 것). 몸만 ArcCW와 다르다(2026-11-06 사용자 요청): 두께와 상관없이 몸 하나에 `pierceCost` 2mm만 깎고, 들어간 자리에서 다시 나가 붙어 선 몸도 맞힌다. `Flesh`의 재질 계수는 ArcCW의 0.1로 되돌렸고, `pierceCost`를 비울 때만 쓴다 | [BulletPenetration](../Scripts/Combat/HitDetection/BulletPenetration.cs), [AttackTrace](../Scripts/Combat/HitDetection/AttackTrace.cs), [Weapons.csv](../Resources/Tables/Weapons.csv), [Surfaces.csv](../Resources/Tables/Surfaces.csv), `Art/Audio/Bullets/Ricochet` |
| TFA Base (GMod 워크숍 2840031720) | 시선 흔들림(Sway)은 2026-11-07부터 KINEMATION FPS Animation Framework의 Sway 레이어로 바꿔 지웠다. 앉아 쏘기 배율 값 0.5·0.65. 잔탄 경고음 규칙(`shared.lua`의 `EmitLowAmmoSound`: 쏜 뒤 탄창 3분의 1 미만이면 총마다의 경고음, 비어 갈수록 크게(`RequiredVolume`), 마지막 발은 마지막 발 소리, `autodetection.lua`대로 4발 이하 탄창은 없음). 경고음과 함께 CS2 딸깍을 내는 것은 사용자가 고른 조합이다(2026-11-06) | [Weapons.csv](../Resources/Tables/Weapons.csv), `WeaponFeedback.AmmoCue`, GunFeel `Low Ammo Fraction`·`Low Ammo Small Magazine` |
| Accurate Gun Reverb (L4D2 워크숍 3629393613) | 쏠 때마다 발밑에서 9방향 광선으로 방 크기를 재고(양끝 2개씩 버린 합 ÷ 7, 위가 500유닛 넘게 트이면 실외) 총과 방에 맞는 잔향 꼬리를 겹치기. 꼬리 15개. 유니티 때문에 다른 점: 길찾기 영역 대신 2m 칸마다 기억, 광선이 사람을 지나감, 동시 꼬리 수 제한 | [GunReverb](../Scripts/Audio/GunReverb.cs), `Art/Audio/Guns/Reverb` |
| Hi-Octane Weapon Sounds: Re-Charged! (L4D2 워크숍 3810863466) | 총소리만: 권총·소총·샷건 발사, 장전, 빈 총 소리 | `Art/Audio/Guns` |
| Low Ammo Sound Cue (L4D2 워크숍 3198155663) | 한때 잔탄 경고음 규칙이었다(쏜 뒤 남은 탄이 탄창의 20%(올림)이자 적어도 3발 이하면 매 발 같은 딸깍). 2026-11-06에 사용자가 TFA 베이스의 규칙과 소리를 골라 바꿨다(위 TFA 행). 이 애드온의 `click.mp3`는 CS2의 `lowammo_01`을 키워 찌그러뜨린 것이어서 CS2 원본으로 바꿨다 | 없음 |
| mighty foot engaged (GMod 워크숍 1987521428) | 장전 중 V 발차기의 시간표와 판정: 0.2초에 시점 펀치(-10, 좌우 ±2)와 휘두르는 소리, 0.3초에 몸 가운데에서 시선이 머무는 곳 쪽으로 키 높이만큼 선 판정 후 4유닛 훑기, 피해 20~30, 맞히면 차는 사람 100유닛/초 뒤로(공중 두 배), 재사용 0.75+0.5초. 문 열기·부수기는 옮기지 않았다 | [ShoveMoves](../Scripts/Player/Combat/ShoveMoves.cs), Player.csv `Shove` |
| ArcCW 근접 도약 (위 ArcCW의 `sh_bash.lua` DoLunge) | 서서 밀칠 때 사거리 너머 64유닛 안의 조준한 적에게 거리 × 5(공중 2.5)의 속도로 도약, 앉아 있으면 안 함 | [ShoveMoves](../Scripts/Player/Combat/ShoveMoves.cs) |
| Beatrun addon compatibility layer, Landing Anim-Fix | 옮기지 않았다. GMod 안에서 동작하는 Lua 패치다 | 없음 |

소리의 표면별 출처는 [사운드 관리](MaintenanceGuide.md#이동목소리-소리-폴더가-곧-목록)에, 프로젝트 전체의 에셋 출처는 [외부 에셋 출처](AssetSources.md)에 있다.

## 워크숍 에셋을 들여올 때

1. 어떤 애드온을 쓸지는 [워크숍 애드온 이식표](https://claude.ai/artifact/TjkB8PvDvQ1dnhJr27FEbz)에서 고른다.
2. [Tools/Workshop/workshop.py](../../../Tools/Workshop/workshop.py)의 `extract`로 애드온을 프로젝트 밖 `F:\GameProject\_WorkshopImport\<워크숍 번호>`에 푼다. 받은 파일의 실행 파일은 실행하지 않는다.
3. 필요한 파일만 변환해서 아래 표의 위치에 넣는다. Source 모델과 애니메이션은 [Tools/Blender](../../../Tools/Blender)의 변환 스크립트를 쓴다.
4. 같은 작업에서 [외부 에셋 출처](AssetSources.md)에 워크숍 번호와 함께 한 줄을 추가한다.

| 종류 | 위치 |
| --- | --- |
| 캐릭터 모델 | `Art/Models/Characters/<이름>` |
| 적 모델 | `Art/Models/Enemies/<이름>` |
| 무기 모델 | `Art/Models/Weapons/<이름>` |
| 애니메이션 | `Art/Animations/<묶음>`. 폴더 표는 `<묶음>.clips.csv`. 좀비는 `Zombie` |
| 총 소리 | `Art/Audio/Weapons/<무기>` |
| 적 소리 | `Art/Audio/Enemies/<적>/<상태>` |
| 근접·타격 소리 | `Art/Audio/Combat/<용도>` |
| 이펙트 | `Prefabs/Effects/<용도>`, 재질은 `Art/Materials/<용도>` |
| 스토어 팩처럼 통째로 받는 에셋 | `Assets/ThirdParty/<팩 이름>` |

## 단위

Source 단위는 **1 = 0.025m**로 환산했다. Beatrun의 수치를 옮길 때 이 값을 쓴다. 예를 들어 사다리 한 칸은 0.39m, 사다리 미끄럼은 8.5m/s, 집라인 최고 속도는 18.75m/s, 집라인 매달림은 1.875m다.

## Beatrun과 다르게 만든 것

| 부분 | ENFILADE | 이유 |
| --- | --- | --- |
| 시간 | 넘기·올라서기·구르기는 애니메이션이 자연스러운 시간을 쓴다 | Dying Light처럼 묵직한 파쿠르를 원한다 |
| 눈 시점 | 정조준·사다리를 빼고 카메라가 눈을 따른다. 사다리는 상하 전환 중 기준 눈높이를 유지한다 | 사다리의 클립 전환이 화면을 밀지 않게 했다 |
| 손 | 넘기·높은 올라서기 때 손을 실제 모서리에 짚고, 몸은 그 손이 닿는 범위에 머물다 캡슐을 따라잡는다 | 원본은 손 고정이 없다. 판단은 캐릭터마다 실제 팔 길이로 해서 수치를 따로 넣지 않는다 |
| 매달린 뒤 올라가기의 몸 | 캡슐의 경로와 시간은 원본이지만 보이는 몸은 `hangheaveup` 대신 이동을 남긴 `mx_ledge_climb_up`을 쓴다. 손을 짚은 동안 몸은 손에 붙어 올라간다 | `hangheaveup`은 몸이 캡슐보다 최대 0.66m 벽 반대쪽으로 처지고 손도 0.3초쯤 윗면에서 떠서, 1인칭으로 내려다보면 벽 앞 허공에 떠서 허공을 짚는다 |
| 엄지 | 잡지 않은 손의 엄지는 클립 대신 캐릭터의 손에서 잰 편한 자세를 쓴다 | 리타깃을 거친 엄지가 손에서 벌어져 나온다 |
| 몸 크기 | 원본의 77·64 같은 거리를 `HeightUnit`으로 캡슐 키에 맞춘다. 캐릭터별 높이 설정은 없다 | Actual Thirdperson은 모델마다 높이 프리셋을 저장하지만, 여기서는 손 위치를 재서 몸을 맞춘다 |
| 장애물 지정 | `ParkourObstacle`로 물체별 넘기·올라타기를 고정할 수 있다 | 자동 판단이 맵 의도와 다를 때를 위한 추가 기능이다 |
| 적 위로 넘기 | 넘을 수 있다 | 원본은 NPC를 넘지 않지만 기존 동작을 유지했다 |
| 렛지 매달림 | 판정 범위, 위치, 벽을 볼 때의 클립은 기존 것을 쓰고 손을 `Grip`으로 맞춘다. 돌아보기와 올라가기는 원본이다. 돌아볼 때는 몸이 자리를 지키고 남은 손을 IK로 모서리에 놓는다 | 원본은 손이 모서리 77단위 위치에 맞도록 클립과 높이를 맞춰 두었다. 캐릭터마다 다른 팔을 맞추려고 손을 재서 맞춘다. 돌아보기 자세의 손은 몸에 가까워서 그 손에 몸을 맞추면 벽을 뚫는다 |
| 끝 지점 정리 | 끝점에 끼면 0.12초에 걸쳐 빠져나온다 | 원본은 순간이동한다. 눈 시점에서 화면이 튀지 않게 했다 |
| 일찍 끝내기 | 몸이 아무것도 안 닿아도 끝점까지 남은 길이 비어 있어야 끝낸다 | 원본 규칙은 폭 넓은 몸에서만 맞는다. 좁은 캡슐은 장애물 바로 앞에서 끝나 넘지 못했다 |
| 넘기 끝의 착지 | 넘기 경로가 장애물 너머 공중에서 끝나도 발이 땅에 닿을 때까지 넘기 상태로 내려온다(아래 땅이 60단위 안일 때). 내려오는 동안 낙하는 보통 낙하처럼 재고, 착지는 발 한가운데 아래의 바닥으로 판단한다 | 원본은 경로가 끝나면 바로 걷기 상태로 돌린다. 공중에서 상태가 바뀌어 넘기가 급하게 끊기는 느낌이라고 사용자가 지적했다(2026-11-06). 이어서 받은 영상의 '착지 뒤 0.7m 낙하'는 레일 모서리에 섰다가 떨어진 것이었다(아래 줄) |
| 넘기 끝점과 일찍 끝내기 검사 | 넘는 물체에 대해서는 몸 전체를 바닥까지 검사한다(`ParkourDetector.BodyClearOf`). 끝점과, 몸이 다 빠졌는지 보고 일찍 끝낼 때 둘 다 | 원본과 같게 되돌린 것이다. 원본은 vaultpos와 끝낼 때 몸통 전체(바닥 포함)를 검사한다. 이쪽 검사는 캡슐을 바닥에서 0.04m 띄워 해서, 넘기 끝 발 높이(35단위, 0.86m)가 0.9m 레일 윗면 바로 아래라 몸 뒷부분이 레일 위에 걸친 채 끝날 수 있었다. 그러면 레일 모서리에 잠깐 섰다가 떨어져 넘기가 일찍 끊겨 보였다(2026-11-06 세 번째 영상) |
| 벽 오르기와 넘기 | 이 점프로 곧 넘거나 올라설 수 있는 장애물이면 벽 오르기를 시작하지 않고, 벽을 오르는 중에도 넘기를 시도한다. 곧 넘을 수 있는지와 렛지를 잡은 순간의 높은 동작은 벽에 붙었을 때의 자리에서 윗면을 찾는다 | 원본은 벽 오르기가 먼저 시작될 수 있어 윗면이 높은 장애물을 넘기 어렵다. 원본처럼 지금 자리에서 찾으면 벽에서 0.5m쯤 떨어진 순간 얇은 윗면을 놓쳐, 넘어야 할 얇은 벽을 오르고 매달린 뒤 올라섰다 |
| 맨틀 중 시선 | 올라서는 동안 올라가는 쪽에서 좌우 45°까지만 돌아본다 | 원본은 매달린 데서 올라갈 때 시선을 묶지 않지만 자기 머리를 숨긴다. 여기서는 머리가 보여서 돌아보면 목 안이 보인다 |
| 높은 넘기 뒤의 손 | 윗면을 밀고 뗀 손은 넘기와 그 클립이 끝날 때까지 뗀 높이보다 올라가지 않는다 | 원본 `vaultoverhigh`는 몸이 윗면을 넘은 뒤 그 손을 얼굴 앞 머리 높이로 들어 올린다. 얇은 벽 너머엔 짚을 데가 없어 허공을 누르는 것처럼 보였다(2026-11-06 사용자 영상) |
| 착지 구르기 속도 | 수평 2m/s부터 구르고 그보다 느리면 제자리 착지 | 원본 200단위/초(약 4.9m/s)는 걷기 속도와 같아서 걷다가 떨어지면 구르지 못했다(2026-11-06 사용자 요청) |
| 점프 직후 낮은 넘기 | 점프가 오르는 동안 낮은 넘기는 원본 점프(230단위/초, 중력 600)가 지금쯤 발을 둘 높이에서 찾는다. 첫 공중 프레임은 원본의 첫 틱으로 친다 | 이 게임의 점프는 더 높고 빨리 떠서, 60fps에서는 첫 공중 프레임에 이미 0.9m 레일을 찾는 높이(가슴 상자 아래, 발 위 32단위)를 지났다. 그래서 달리며 점프하면 레일을 넘지 않고 그냥 뛰어넘었다 |
| 일찍 누른 점프 | 달리며 낮은 장애물 앞에서 일찍 누른 점프는 넘기가 걸리는 자리까지 최대 3m를 땅에서 빠르게(최소 11m/s) 달려가 바로 넘는다. 달려가기 시작할 때 카메라가 앞으로 살짝 숙고 시야각이 넓어진다 | 원본은 장애물 앞면 약 1.2m 안에서 눌러야 넘는다. 사용자가 대기 말고, 순간이동 없이 속도를 올려 빠르고 시원하게 넘기를 원했다(2026-11-06) |
| 얕은 윗면 | 몸 너비(캡슐 지름)보다 얕은 윗면에는 디뎌 오르기·올라타기를 하지 않는다. `ParkourObstacle`이 Climb Onto면 예외 | 원본은 레일 위로 올라서기도 한다. 달리며 레일을 넘으려다 레일 위에 올라서는 것을 사용자가 불쾌해했다(2026-11-06) |
| 높은 동작의 손 뻗기 | 높은 넘기·높은 올라서기를 시작하면 손이 클립 대신 짚을 자리로 `Hands.edgeReach`(0.2초)에 걸쳐 뻗어 가고, 닿은 뒤에야 손가락을 쥔다 | 두 클립 모두 손을 머리 위에서 시작해 늦게 윗면으로 내리고, 캡슐은 그동안 벽 앞으로 다가간다. 그래서 손이 윗면 위·너머 허공으로 뻗었다(2026-11-06 사용자 지적) |
| 높은 올라서기의 윗면 찾기 | 70단위 앞에 윗면이 없으면 몸이 올라설 수 있는 곳까지 당겨서 찾는다 | 원본은 깊이 약 1.3m보다 얕은 윗면을 놓치고 매달리기로 넘어간다 |
| 높은 동작의 출발 | 캡슐이 0.2초 동안 벽 앞 자리로 간다 | 원본은 캡슐을 순간이동하고 몸만 보간한다. 바닥이 막으면 높이는 유지하고 거리만 좁힌다 |
| 숨소리 | 질주(`Breathing.exertSpeed` 6m/s 초과)·파쿠르·큰 착지에서만 숨이 차고, 숨참이 `audibleStress` 이하면 조용하며, 힘쓰기를 멈추면 숨 간격이 1.25초에서 2.2초로 느려지다 멎는다 | 원본은 1.9m/s만 넘어도 숨이 차서 걷기(4.9m/s)에도 0.5~0.75초마다 숨을 쉬었다. 2026-11-05 사용자 요청(백로그 612) |
| 월 런 | 넣지 않았다 | 벽 달리기 클립 4개도 변환에서 뺐다 |

## 아직 남은 일

- **총과 손만 세상 위에 그리기**: 총이 벽을 뚫어 보이지 않게 한다. 손이 몸 메시의 일부라서 메시를 나눠야 한다. 몸 전체를 위에 그리면 발이 바닥 위로 비치고, 별도 무기 카메라는 쓰지 않기로 했다.
- **몸 클립 펀치**: 주먹이 지금은 손 IK로만 뻗는다. `punchleft`·`punchright`·`punchmid`·발차기 클립을 섞는다.
- **총 든 동작 클립**: `Body.armedClips`를 켜서 확인한다. 손 IK가 생겨서 가능해졌지만 아직 시험하지 않았다.
- **사다리·줄의 손 IK**: 했다(백로그 142). 사다리는 `PlayerBodyHands.Ladder`가 손을 가로대에, `PlayerBody.LadderFeet`가 발을 가로대에 올리고, 집라인은 `PlayerBodyHands.Zipline`이 클립의 앞뒤 손 위치 그대로 각 손을 줄 위에 감아 쥔다. 모두 캐릭터마다 손바닥 치수(ArmIK)를 쓴다.
- **총 모델**: 지금 플레이어 프리팹의 총은 재질을 비워 보이지 않고, 권총 모델은 없다.
- **중력**: 24다. 20도 시험해 볼 만하다.
- **소음을 듣는 적**: `Noise` 이벤트는 나가지만 듣는 AI가 없다.
- **멀티플레이**: 혼자일 때만 Esc 메뉴가 게임을 멈추게 한다.
