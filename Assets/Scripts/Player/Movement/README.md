# Movement 구조

전체 파일 위치와 전투 확장 시작점은 [스크립트 길잡이](../../README.md), 구현된 조작은 [이동 기능 안내](../../Docs/MovementGuide.md)를 먼저 본다. 이 문서는 상태 머신 내부를 수정할 때 참고한다.

`PlayerMovement`는 Unity 생명주기와 외부 API를 연결하는 진입점이다. 카메라, HUD,
텔레포트는 기존 `PlayerMovement.MovementState`, 속도 속성 및 `ResetVelocity()`를 그대로 사용한다.

## 런타임 상태 계층

```text
MovementRootState
├─ LocomotionState
│  ├─ StandingState
│  ├─ CrouchingState
│  └─ ProneState
│     ├─ ProningState
│     └─ SupineState
├─ SlidingGroupState
│  ├─ SlidingState
│  ├─ HeadFirstSlidingState
│  └─ SupineSlidingState
├─ DivingGroupState
│  ├─ DivingState
│  └─ SupineDivingState
├─ Evasion → StepDodge
├─ Recovery → LandingRoll
├─ Assault → Tackle
└─ Parkour
   ├─ Obstacle → Vault, Mantle, LedgeHang, LedgeJump
   ├─ Wall → WallClimb, WallKick
   └─ Swing → BarGrab, BarSwing, LacheRelease
```

각 부모는 실제로 생성되는 상태 객체다. 단순한 C# 상속 계층이 아니다.
`MovementStateMachine`은 활성 말단 상태부터 부모까지의 경로를 유지한다.
전환 시 공통 조상 아래만 종료/진입한다. 예를 들어 Standing → Crouching에서는
Locomotion이 유지되고, Diving → HeadFirstSliding에서는 Dive가 종료되고 Slide가 진입한다.

말단에서 처리하지 않는 이벤트는 `MovementBaseState`를 통해 부모로 전달된다.
말단이 이벤트를 처리했다면 기본적으로 부모 처리는 실행하지 않는다.
`Enter/Exit`는 머신만 호출하며 부모에게 전달하지 않는다.
진입/종료 콜백 안에서 재전환하면 예외를 발생시켜 재귀 전환을 차단한다.
동일 상태로의 전환은 기존처럼 지속 시간을 초기화하지 않는다.
예외적으로 성공 후 연속 스텝 닷지는 `GroundBurstState.RestartStep()`이 해당 동작의 거리/시간만 명시적으로 재시작한다. 월 런은 삭제했고 옛 enum 값 15는 비워 두었다.

- Locomotion: 일반 이동/공중 조향, 일반 점프, 웅크린 착지 슬라이드.
- Prone: 포복 자세의 기상, 환풍구 진입/탈출, 낙하 전환.
- Slide: 공통 마찰·조향·브레이크·충돌. 각 말단은 마찰값과 정면 충돌 반응을 지정한다.
- Dive: 공중 조향·파괴 충돌·착지. 각 말단은 자세와 정지 조건을 지정한다.

접지 여부는 상태 계층과 별도인 물리 관측값이다. 일반 점프 중에도 Standing이 유지되고,
슬라이딩도 턱을 지나 공중에 있을 수 있으므로 최상위를 Grounded/Airborne으로 강제로 나누지 않는다.
기존 동작과 상태 enum을 유지하면서 행동별 공통 규칙을 부모로 모았다.

## 책임 분리

| 파일 | 책임 |
| --- | --- |
| `PlayerMovement.cs` | 초기화, 프레임 처리 순서, 기존 외부 API 위임 |
| `PlayerMovement.Configuration.cs` | 걷기·슬라이드·다이브·자세 등의 인스펙터 설정 |
| `MovementAbilitySettings.cs` | 회피·파쿠르·낙하·밀치기 설정 |
| `Core/MovementStateMachine.cs` | 상태 생성, 활성 경로, 공통 조상 기준 전환, 지속 시간 |
| `Core/MovementMotor.cs` | 속도, 접지/경사 탐지, 계단 밀착, CharacterController 이동, 중력 |
| `Core/MovementPostureController.cs` | 자세별 캡슐 높이, 공중 다리 접기, 천장/환풍구 검사, 카메라 높이 |
| `Core/MovementActions.cs` | 슬라이드·다이브 발사 실행, 스프린트/점프 버퍼/코요테 타이머 |
| `Core/MovementInput.cs` | Input System 액션 연결 |
| `Actions/MovementAbilities.cs` | 독점 행동의 입력 우선순위, 회피/파쿠르/전투 서비스 연결 |
| `Dodge/` | 공통 회피 무적·쿨타임, 기울이기, 스텝 경로 판정 |
| `Parkour/` | 지형 탐지, 벽/렛지/봉 데이터, 통과 경로 |
| `../Combat/PlayerCombatActions.cs` | 밀치기·태클 공격 판정. 태클의 몸 이동은 States/Abilities에서 실행 |
| `../Camera/PlayerCameraEffects.cs` | 위치/조준을 바꾸지 않는 카메라 효과. CameraEffectsPivot에 회전 적용 |

Configuration은 설정값 보존을 위한 partial 선언이다. 이동 구현을 partial 파일 여러 개로
나눈 구조가 아니라, Motor/Posture/Actions가 각각 런타임 데이터를 소유한다.
유지한 스크립트의 GUID, 사용하는 설정 필드 이름/타입/기본값, enum 순서를 보존했다.
읽는 코드가 없던 다이브 설정 3개와 미사용 공개 속성은 제거했다. 삭제 내역은 [스크립트 길잡이](../../README.md)에 기록했다.
삭제된 월 런의 값은 재사용하지 않고 이후 상태 숫자를 유지한다. 파일의 폴더 이동은 `.meta`와 함께 수행한다.
속도·슬라이드 방향 같은 런타임 값은 내부 객체로 이동했고 기존 이름의 속성으로 접근한다.
`ResetVelocity()`는 버퍼에 남은 점프·코요테 시간·접지 정보도 초기화한다.

프레임 순서는 타이머 → 접지/착지 → 입력 → 상태 로직 → 점프 → 공중 자세 → 수평 물리 → 중력
→ 높이 → 실제 이동 순서를 유지한다. 각 단계는 그 시점의 활성 상태를 조회하므로 입력이나
착지 중 전환된 상태가 같은 프레임의 다음 단계를 처리한다.

## 상태 추가

1. `MovementBaseState`를 상속하는 말단 상태를 작성한다.
2. 필요한 이벤트만 재정의하고, `Posture`, `LocomotionSpeed`, `SlideFriction` 등 필요한 정책을 지정한다.
3. `MovementState` enum 끝에 항목을 추가하고 `MovementStateMachine.Create()`에서 부모와 연결한다.
4. 새 상태의 고유 규칙 및 전환을 검증한다. 외부 카메라 연출에 필요하면 해당 enum 처리도 추가한다.

공통 동작을 추가할 때는 개별 말단보다 해당 부모에 배치한다.
HUD는 `CurrentStatePath`로 실제 부모 경로를 표시한다.

## 검증

Unity 메뉴 **Tools → Movement → Run Refactor Checks**에서 실행한다.
Play Mode를 종료하고, 이름 없는 새 씬이라면 먼저 저장해야 한다.
검사는 별도의 임시 additive 씬을 열고 완료/실패 시 닫는다. 사용자의 씬은 저장하지 않는다.

Unity 6000.3.12f1의 별도 배치 프로젝트에서 아래 13개 검사를 통과했다.

- 부모/말단 진입·종료 순서, 형제/교차 전환, 동일/잘못된 전환, 이벤트 전달
- 기존 설정 필드 역직렬화
- 9개 말단 상태의 캡슐 높이와 발 위치
- 서기/앉기 점프
- 포복/누운 자세에서 점프 입력으로 기상
- 슬라이드 점프 최소 시간과 운동량 유지
- 다이브/엎드린 슬라이드 점프 차단
- 고속 다이브 착지의 슬라이드 전환
- 저속 다이브 착지의 포복 전환
- 슬라이드 부모의 마찰이 한 번만 적용되는지
- 낮은 천장의 점프 차단
- CharacterController 접지와 단일 착지 이벤트
- 리셋 후 이동 및 점프 버퍼 초기화

게임 스크립트 전체를 프로젝트의 Unity 참조 어셈블리로 컴파일하여 오류 0개를 확인했다.
실제 게임 씬에서의 키보드 조작감, 계단/환풍구의 모든 배치, 카메라 연출까지 자동 검증한 것은 아니다.
