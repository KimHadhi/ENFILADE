# 애드온 이식 기록

[프로젝트 안내](../../../README.md) · [이동 안내](MovementGuide.md) · [플레이어 몸](PlayerBodyGuide.md) · [설정·유지보수](MaintenanceGuide.md)

GMod 애드온(`F:\Addons`, 저장소 밖)의 이동·애니메이션·소리를 ENFILADE로 옮긴 기록이다. Lua 코드를 그대로 가져오지 않고 규칙과 수치를 Unity 구조에 맞춰 다시 만들었다. 애니메이션과 소리는 변환해서 넣었다.

## 어디서 무엇을 옮겼나

| 애드온 | 옮긴 것 | 지금 위치 |
| --- | --- | --- |
| Beatrun Community Edition | 사다리·집라인, 시점 펀치 값, 세게 착지, 숨소리 모델, 동작 소리와 재생 순서 | [Parkour](../Scripts/Player/Movement/Parkour), [PlayerMovementPunch](../Scripts/Player/Camera/PlayerMovementPunch.cs), [MovementLanding](../Scripts/Player/Movement/Actions/MovementLanding.cs), [PlayerBreathing](../Scripts/Player/Audio/PlayerBreathing.cs), [SoundCues.csv](../Resources/Tables/SoundCues.csv) |
| Beatrun Actual Thirdperson animations | 여성 3인칭 세트(`tp_climbanim_f`)를 Hiro 뼈대에 구운 클립 | [Art/Animations/Beatrun](../Art/Animations/Beatrun) |
| Eye View Attachment for Beatrun | 카메라를 몸의 눈에 붙이기 | [PlayerBody.Look](../Scripts/Player/Body/PlayerBody.Look.cs) |
| 2026 IK Foot (High FPS Version) | 발 IK | [PlayerBody.Feet](../Scripts/Player/Body/PlayerBody.Feet.cs) |
| Anim-Based Footsteps Timing | 발이 실제로 닿는 순간의 발소리 | [PlayerFootsteps](../Scripts/Player/Audio/PlayerFootsteps.cs) |
| Dynamic Landing | 크게 착지해 쓰러지기, 돌진 밀치기, 착지·구르기·밀치기 소리 | `MovementLanding`, [PlayerCombatActions](../Scripts/Player/Combat/PlayerCombatActions.cs), [Art/Audio/Movement](../Art/Audio/Movement) |
| FEAR-like SFX for Various VManip Addons | 일부 표면의 착지·슬라이드, 포복, 렛지, 기울이기, 옷 소리 | `Art/Audio/Movement`, 나머지는 [Art/Audio/Library](../Art/Audio/Library) |
| [wOS] DynaBase, Mixamo 확장 | 착지 클립을 시험했다. 지금은 mixamo.com에서 직접 받은 클립으로 바꿔서 남아 있지 않다 | [Art/Animations/Mixamo](../Art/Animations/Mixamo) |
| [MW] Quick Throwing Knife | 소리만 보관. 기능은 이후 전투 작업에서 다룬다 | `Art/Audio/Library/ThrowingKnife` |
| Advanced Stealth | 소리만 보관. 잠입·분대 AI는 별도 계획이 필요하다 | `Art/Audio/Library/AdvancedStealth` |
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
| 올라서기 | 렛지에 매달린 뒤에만 올라선다(`Ability.mantleFromGround`로 되돌릴 수 있다) | 넘기를 원할 때 올라서는 경우가 생겼다 |
| 월 런 | 넣지 않았다 | 벽 달리기 클립 4개도 변환에서 뺐다 |

## 아직 남은 일

- **총과 손만 세상 위에 그리기**: 총이 벽을 뚫어 보이지 않게 한다. 손이 몸 메시의 일부라서 메시를 나눠야 한다. 몸 전체를 위에 그리면 발이 바닥 위로 비치고, 별도 무기 카메라는 쓰지 않기로 했다.
- **몸 클립 펀치**: 주먹이 지금은 손 IK로만 뻗는다. `punchleft`·`punchright`·`punchmid`·발차기 클립을 섞는다.
- **렛지에서 뒤돌아보기**: 매달린 채 시선을 오른쪽으로 크게 돌리면 왼손은 잡고 오른손과 머리만 돌아야 한다. 반대쪽도 같다.
- **총 든 동작 클립**: `Body.armedClips`를 켜서 확인한다. 손 IK가 생겨서 가능해졌지만 아직 시험하지 않았다.
- **사다리·줄의 손 IK**: 지금은 클립과 `PlayerBody.Grip`의 위치 맞춤뿐이다. 클립의 손이 어긋나 보이면 추가한다.
- **총 모델**: 지금 플레이어 프리팹의 총은 재질을 비워 보이지 않고, 권총 모델은 없다.
- **중력**: 24다. 20도 시험해 볼 만하다.
- **소음을 듣는 적**: `Noise` 이벤트는 나가지만 듣는 AI가 없다.
- **멀티플레이**: 혼자일 때만 Esc 메뉴가 게임을 멈추게 한다.
