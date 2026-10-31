# 이동 코드 구조

[프로젝트 안내](../../../../../README.md) · [스크립트 길잡이](../../README.md) · [이동 조작·설정](../../../Docs/MovementGuide.md) · [플레이어 몸](../../../Docs/PlayerBodyGuide.md) · [검증 안내](../../../Docs/ValidationGuide.md)

이 문서는 이동 시스템을 수정할 때의 진입점과 유지해야 할 규칙을 설명합니다. 조작법과 기본 수치는 이동 안내에서 관리합니다.

## 책임과 수정 위치

`PlayerMovement`는 Unity 생명주기와 외부 API를 연결합니다. 입력·물리·자세·행동 데이터는 각 서비스가 관리하고, 상태 객체는 현재 행동의 정책을 결정합니다.

| 파일·폴더 | 책임 |
| --- | --- |
| [PlayerMovement.cs](PlayerMovement.cs) | 초기화, 프레임 처리 순서, 외부 속성·메서드 |
| [PlayerMovement.Configuration.cs](PlayerMovement.Configuration.cs) | 이동·자세·슬라이드·다이브 설정 선언. 값은 `Player.csv`의 `Move`·`Posture` 행 |
| [MovementAbilitySettings.cs](MovementAbilitySettings.cs) | 회피·파쿠르·착지·슬라이딩 공격·밀치기·사다리·집라인·평균대 설정 선언. 값은 `Player.csv`의 `Ability` 행 |
| [Core/MovementInput.cs](Core/MovementInput.cs) | Input System 액션 샘플링 |
| [Core/MovementStateMachine.cs](Core/MovementStateMachine.cs) | 상태 등록, 활성 경로, 공통 조상 기준 전환 |
| [Core/MovementMotor.cs](Core/MovementMotor.cs) | 속도·접지·경사·중력·CharacterController 이동 |
| [Core/MovementPostureController.cs](Core/MovementPostureController.cs) | 몸·눈높이, 공중 웅크리기, 천장·통로 공간 검사 |
| [Core/MovementActions.cs](Core/MovementActions.cs) | 슬라이드·다이브 시작, 달리기·점프 버퍼·코요테 타이머 |
| [Actions/MovementAbilities.cs](Actions/MovementAbilities.cs) | 독점 행동의 입력 우선순위, 회피·파쿠르·전투 서비스 연결 |
| [Actions/MovementDive.cs](Actions/MovementDive.cs) / [MovementLanding.cs](Actions/MovementLanding.cs) | 다이브 비행 / 착지 단계·구르기·낙하 피해·큰 낙하·넉백 |
| [Actions/MovementMomentum.cs](Actions/MovementMomentum.cs) | 달리며 쌓이는 속도 |
| [Dodge/](Dodge/) | 공통 회피 창, 경로 성공 판정, 기울이기, 적 통과 |
| [Parkour/](Parkour/) | 장애물·벽·렛지·봉 탐지와 이동 경로, 사다리·집라인·평균대 잡기([MovementFixtures](Parkour/MovementFixtures.cs))와 평균대 판정([BalanceRhythm](Parkour/BalanceRhythm.cs), [BeamRun](Parkour/BeamRun.cs)) |
| [States/](States/) | 자세·행동별 입력, 전환, 물리 정책 |
| [../Combat/SlideCombat.cs](../Combat/SlideCombat.cs) | 슬라이딩의 적 접촉·가속·저스트 판정 |
| [../Combat/PlayerCombatActions.cs](../Combat/PlayerCombatActions.cs) | 몸의 V: 돌진 밀치기(총·주먹)와 총을 든 군중 밀치기. 돌진의 몸 이동은 States/Abilities에서 처리 |
| [../Camera/PlayerCameraEffects.cs](../Camera/PlayerCameraEffects.cs) | 이동 상태에 따른 카메라 연출 |

Configuration은 같은 `PlayerMovement` 컴포넌트의 partial 선언입니다. 별도 컴포넌트나 제거 가능한 설정 사본이 아닙니다. HUD·카메라·텔레포트 등은 `CurrentState`, `Velocity`, `IsInitialized`, `ResetVelocity()` 같은 외부 API를 사용합니다.

## 밖에서 듣는 이벤트

몸·소리·시점 펀치는 이동 코드가 직접 부르지 않습니다. 아래 이벤트를 구독하거나 공개 속성을 읽어 따라갑니다. 새 연출도 이 방식으로 붙입니다.

| 이벤트 | 뜻 | 듣는 쪽 |
| --- | --- | --- |
| `StateChanged(from, to)` | 상태 전환 | 동작 소리, 사다리류 소리, 시점 펀치, 속도 쌓기 |
| `Landed(impact)` | 착지 | 동작 소리, 시점 펀치, 속도 쌓기, 착지 단계 |
| `Jumped` | 점프 | 동작 소리, 시점 펀치 |
| `DashPushHit(point)` | 돌진 밀치기가 적에게 멈춤 | 동작 소리 |
| `LadderStepped(leftHand)` | 사다리 한 칸 | 사다리류 소리, 시점 펀치 |
| `LadderSlideStopped` | 사다리를 미끄러지다 멈춤 | 시점 펀치 |
| `BalanceKeyPressed(side)` | 평균대 키 입력 | 판정 원 |
| `BalanceBeat(judgement)` | 평균대 판정 결과 | 판정 원, 사다리류 소리 |
| `BalanceStep` | 평균대 위 실제 발 접지 | 사다리류 소리 |
| `PlayerFootsteps.Stepped(step)` | 몸의 발이 땅에 닿음 | 시점 펀치 |

## 프레임 흐름

`Update()`는 서비스를 준비한 뒤 입력 샘플링 → 기능·행동·물리 타이머 → 접지와 착지 → 달리기 입력·속도 쌓기 → 독점 행동/상태 입력 → 상태 로직 → 점프 → 기울이기·공중 자세 → 수평 물리 → 중력 → 몸·눈높이 → 실제 이동 순서로 진행합니다.

각 단계는 그 시점의 활성 상태를 조회합니다. 입력이나 착지 중 상태가 바뀌면 같은 프레임의 다음 단계부터 새 상태가 처리합니다. 처형·가드 브레이크로 입력이 잠긴 경우에는 입력과 수평 이동을 멈추고 중력·자세·실제 이동만 처리하는 별도 경로를 사용합니다. 크게 착지하거나 넉백으로 날아가는 동안(`ControlLocked`)에는 입력만 비우고 상태 처리는 그대로 진행합니다.

- 실제 몸 이동은 `MovementMotor`를 통해 적용합니다. 경로 이동은 해당 파쿠르 구현의 충돌 검사와 함께 다룹니다.
- 마우스 조준은 `PlayerLook`, 추가 화면 연출은 `PlayerCameraEffects`가 맡습니다.
- 공격 데이터와 피해 전달은 Combat 계층을 사용합니다. 이동 상태가 무기 잔탄·피해 수신 규칙을 소유하지 않습니다.
- 채팅·포커스·커서 잠금·일시정지·사망에 따른 입력 차단은 `InputAvailable`과 전투 잠금 경로를 유지합니다.

## 상태 계층

아래 부모·말단은 `MovementStateMachine.Create()`에서 연결되는 실제 상태 객체입니다.

```text
Movement
├─ Locomotion
│  ├─ Standing
│  ├─ Crouching
│  └─ Prone
│     ├─ Proning
│     └─ Supine
├─ Slide
│  ├─ Sliding
│  ├─ HeadFirstSliding
│  └─ SupineSliding
├─ Dive
│  ├─ Diving
│  └─ SupineDiving
├─ Evasion → StepDodge
├─ Recovery → LandingRoll, HeavyLanding
├─ Assault → DashPush
└─ Parkour
   ├─ Obstacle → Vault, Mantle, LedgeHang, LedgeJump
   ├─ Wall → WallClimb, WallKick
   ├─ Swing → BarGrab, BarSwing, LacheRelease
   └─ Fixture → Ladder, Zipline, Balance, BalanceFall
```

전환 시 공통 조상 아래의 기존 경로만 종료하고 새 경로를 진입합니다. Standing → Crouching은 Locomotion을 유지하고, Diving → HeadFirstSliding은 Dive를 종료한 뒤 Slide에 진입합니다.

처리하지 않은 입력·로직·물리 이벤트는 `MovementBaseState`의 기본 구현을 통해 부모로 전달됩니다. 재정의한 말단에서 부모 동작도 필요하면 명시적으로 기본 구현을 호출해야 합니다. `Enter/Exit`는 상태 머신만 호출합니다.

같은 상태로의 전환은 재진입이나 지속 시간 초기화를 하지 않습니다. `Enter/Exit` 안에서 다시 전환하면 예외를 발생시킵니다. 성공 후 연속 스텝은 `GroundBurstState.RestartStep()`처럼 필요한 행동 데이터만 명시적으로 다시 시작합니다.

접지는 상태 계층과 별도의 물리 관측값입니다. 점프 중에도 Standing일 수 있고, 슬라이딩 중 턱을 벗어나 공중에 있을 수도 있습니다. 상태 이름만으로 접지 여부를 추론하지 않습니다.

## 슬라이딩 공격과 회피의 연결

슬라이딩의 공통 마찰·조향·제동·접촉은 `SlidingGroupState`가 처리하고, 말단은 마찰값과 자세별 벽 반응을 정합니다. 일반 벽 반응과 돌진 밀치기는 저스트 슬라이딩과 별도로 유지합니다.

1. Slide 부모 진입에서 `SlideCombat.Begin()`이 공격 ID와 접촉 기록을 새로 만듭니다.
2. `MovementAbilities.ProcessInput()`은 슬라이딩 중 새 Alt 입력을 가속으로 먼저 처리합니다.
3. `MovementMotor`의 이동 전 검사와 실제 컨트롤러 접촉에서 `SlideCombat`으로 적 접촉을 전달합니다.
4. `SlideCombat`이 적 크기·저스트 판정으로 피해, 넘어짐/밀침, 통과 또는 말단의 벽 반응을 선택합니다.
5. `DodgeEnemyPassThrough`가 통과할 적과의 충돌 쌍을 관리하고 안전하게 복구합니다. Slide 종료에서 저스트 창을 닫습니다.

Slide 내부의 자세 전환은 부모를 다시 진입하지 않으므로 같은 적의 중복 타격을 막는 기록이 유지됩니다. 여러 콜라이더도 같은 `MeleeCombatTarget` 기준으로 묶습니다. 이동 전 검사는 가까운 순서로 처리하고 고정 장애물을 만나면 멈춰 벽 뒤의 적을 때리지 않도록 합니다.

넘어짐의 상태·타이머는 [MeleeCombatTarget](../../Combat/Damage/MeleeCombatTarget.cs), 물리 전환과 복구는 [EnemyKnockdownPresentation](../../Combat/Damage/EnemyKnockdownPresentation.cs)이 맡습니다. 뼈대가 있는 적은 Rigidbody/Joint를, 없는 적은 몸통 Rigidbody를 사용합니다. 새 적 AI는 `IsStunned`/`IsKnockedDown`을 존중하고, 기립 공간·NavMesh가 확보되기 전 임의로 이동을 재개하지 않아야 합니다.

일반 회피는 `DodgeController`의 공통 무적·재사용 시간과 성공 이벤트를 사용합니다. `StepDodgeTrail`은 실제 이동 경로의 성공 판정만 보조하고, `MovementLean`은 확인된 마이크로 회피 성공 뒤의 이동을 담당합니다. 저스트 슬라이드의 타이머는 이 일반 회피 창과 독립적입니다.

## 파쿠르 지지면

벽·렛지의 지지면은 `WallDetector.CanUseSurface`에서 판별합니다. 적의 `MeleeCombatTarget`·`EnemyHealth` 또는 상위 오브젝트의 Enemy 태그가 있거나, 움직이는 물체(비키네마틱 Rigidbody)이면 잡지 않으며, 진행 중인 상태도 이 규칙을 확인합니다. 적 머리 통과를 유지하기 위해 일반 장애물·지면 검사와 이 지지면 판정을 구분합니다.

땅이나 공중에서 찾은 장애물 가운데 넘기가 아닌 것은 `Ability.mantleFromGround`가 켜져 있을 때만 시작합니다. 기본은 렛지에 매달린 뒤 `MantleFromLedge`로 올라서는 경로입니다.

## 평균대 판정

`MovementFixtures.TryBeam`이 올라서는 방식을 고릅니다. 걷기는 `BalanceState`(Fixture 상태), 달리기는 `BeamRun`(Standing 상태를 유지하는 서비스)이 맡고, 둘 다 `BalanceRhythm`을 `BalanceDifficulty` 수치만 달리해 씁니다. 앉아서 가는 것은 일반 Crouching에 `LimitOnBeam` 속도 제한만 겁니다.

판정은 별도의 노트 시계 없이 몸의 실제 발 디딤으로 만듭니다.

1. [PlayerBody.Fixtures](../Body/PlayerBody.Fixtures.cs)가 재생 중인 걷기 클립의 위상을 `ObserveBalanceCycle`로 보냅니다.
2. [PlayerBody.Feet](../Body/PlayerBody.Feet.cs)가 발을 빔 윗면에 놓고, IK 뒤 [PlayerFootsteps](../Audio/PlayerFootsteps.cs)가 접지를 감지해 `BalanceFootDown`을 부릅니다.
3. `BalanceRhythm.Contact`가 그 순간을 판정 시각으로 삼고, 같은 발의 다음 접지를 한 주기 뒤로 예고합니다. 링(`BalanceLeftRing`, `BalanceRightRing`)은 이 예고까지 남은 위상에서 계산합니다.
4. 입력은 `Press`가 발별로 저장합니다. `Resolve`는 판정 시각 앞뒤 `GoodWindow` 안에서 가장 가까운 입력을 고릅니다. 저장 시간은 `balanceHitRange`이며, 그 안의 맞는 입력이 끝내 없을 때만 Miss입니다.
5. 결과는 `BalanceBeat`로 나갑니다. Miss 수가 `SlipLimit`에 닿으면 `BalanceFall`로 전환하고, 실패한 발 쪽으로 떨어집니다.

유지할 규칙은 다음과 같습니다.

- 입력은 링·몸 위상·카메라를 바꾸지 않습니다. 키 입력은 `BalanceKeyPressed`로 원의 색만 바꾸고, 판정 시 문구와 색만 바뀝니다. 이미 보여 준 예고를 짧게 만들거나 늦추지 않습니다.
- 충분히 예고되지 않은 접지(유예 중이거나 링이 0.2초 미만 보인 경우)는 판정하지 않습니다.
- 걷기 속도와 애니메이션은 같은 `BalanceBeatTime`을 쓰며, `balanceTempoResponse`로 목표 간격을 부드럽게 따라갑니다. `Balance` 값은 몸의 작은 흔들림일 뿐이고 입력이나 판정에 따라 변하지 않습니다.
- 달리기는 진입 때의 전방 달리기·전력질주 동작 하나를 유지하고 재생률만 실제 속도에 맞춥니다. 시선이나 달리기 의도로 동작이 바뀌면 예고한 접지 시점이 밀리기 때문입니다.
- A/D는 판정 입력입니다. 평균대 위에서는 일반 조향이나 카메라 기울기에 쓰지 않습니다.

## 재초기화와 상태 보존

`EnsureInitialized()`는 Awake·OnEnable과 Update에서 필요한 서비스를 준비합니다. 새 서비스를 Awake에서만 생성하면 플레이 중 어셈블리 재로드 이후 복구되지 않을 수 있습니다. 외부 컴포넌트는 이동 데이터에 접근하기 전에 `IsInitialized`를 확인합니다.

재로드 시에는 웅크린 현재 캡슐을 원래 서 있는 크기로 오인하지 않도록 `runtimeBodyDefaults`를 유지합니다. 현재 자세에 맞는 상태로 다시 구성하고, 체력·무기 잔탄의 보존은 해당 전투 컴포넌트의 복구 경로와 함께 확인합니다.

`ResetVelocity()`는 속도만 지우는 함수가 아닙니다. 처형·방어·근접 동작·적 통과를 정리하고, 이동 타이머·버퍼·회피·파쿠르 상태를 초기화한 뒤 현재 공간에 맞는 자세로 돌아갑니다. 텔레포트·비활성화처럼 기존 움직임을 끊어야 하는 경우 이 경로를 사용합니다.

## 상태나 기능을 추가할 때

1. `MovementBaseState`를 상속한 상태를 만들고 필요한 이벤트와 정책만 재정의합니다.
2. `MovementState` enum 끝에 항목을 추가하고 `MovementStateMachine.Create()`에 부모와 함께 등록합니다.
3. 공통 동작은 부모·서비스에, 행동별 차이는 말단에 둡니다.
4. 입력 우선순위와 점프 버퍼 소비, 종료·취소·리셋·재초기화를 함께 확인합니다.
5. 상태별 캡슐·눈높이, 벽·천장·경사 접촉, 카메라/HUD의 새 상태 처리를 검증합니다.

기존 enum 숫자를 재배열하지 않습니다. 삭제된 WallRun의 값 15는 예약되어 있으며 이후 값은 유지해야 합니다. 직렬화된 필드의 이름·타입을 바꿀 때는 씬·프리팹 값을 어떻게 이전할지 먼저 정합니다. 파일 이동에는 해당 `.meta`를 함께 유지합니다.

## 검증

확인 항목은 [검증 안내](../../../Docs/ValidationGuide.md)를 따릅니다. 문서에 적힌 기본값만으로 실제 씬 설정을 대신하지 않으므로, 수치를 바꾼 뒤에는 실제 플레이로 확인합니다.
