# 스크립트 길잡이

게임 기능을 수정할 때는 아래 표에서 시작한다. 전체 이동 기능과 키 조작은 [이동 기능 안내](Docs/MovementGuide.md), 상태 전환 내부 구조는 [이동 상태 구조](Player/Movement/README.md)에 정리했다.

무기 설정·추가·디버깅은 [전투 제작 안내](Docs/CombatFoundationGuide.md)에 정리했다. 배열 할당과 EntityReference 제안에 대한 확인 결과는 [성능 지적 검토](Docs/PerformanceReview.md)에 정리했다.

## 어디를 수정할까?

| 수정할 내용 | 시작할 파일 |
| --- | --- |
| 플레이어 공격·무기·콤보를 확장할 위치 | **Player/Combat/**. 현재 밀치기·태클은 [PlayerCombatActions.cs](Player/Combat/PlayerCombatActions.cs) |
| 체력, 피격, 사망, 피해 중복 방지 | [PlayerDamageReceiver.cs](Player/Combat/PlayerDamageReceiver.cs) |
| 총의 대미지·연사력·탄창·장전 시간 | **Assets/Combat/Weapons/**의 무기 asset. 필드 추가는 [WeaponData.cs](Combat/Weapons/Data/WeaponData.cs) |
| 단발/연사 규칙·탄약 소모·장전 타이머 | [WeaponRuntime.cs](Combat/Weapons/Core/WeaponRuntime.cs) |
| 실제 발사·조준 방향·발사 이벤트 | [WeaponController.cs](Player/Combat/Weapons/WeaponController.cs) |
| 플레이어 사격/장전 입력 읽기 | [PlayerWeaponInput.cs](Player/Combat/Input/PlayerWeaponInput.cs), 키 배치는 Player.inputactions의 Combat 맵 |
| 칼·주먹 등 근접 공격의 접촉 판정 | [AttackHitbox.cs](Combat/HitDetection/AttackHitbox.cs) |
| 총알의 즉시 명중 판정과 회피 경로 검사 | [AttackTrace.cs](Combat/HitDetection/AttackTrace.cs) |
| 공격 ID·피해 종류·피해 받는 대상의 공통 규약 | [DamageHit.cs](Combat/Damage/DamageHit.cs) |
| 이동 시작점과 전체 프레임 순서 | [PlayerMovement.cs](Player/Movement/PlayerMovement.cs) |
| 걷기·슬라이드·다이브 수치 | [PlayerMovement.Configuration.cs](Player/Movement/PlayerMovement.Configuration.cs), 실제 씬의 PlayerMovement 인스펙터 |
| 순간 회피용 다이브의 추진·감속·회복 | [MovementDive.cs](Player/Movement/Actions/MovementDive.cs). 방향별 자세와 착지는 States/Diving에서 연결 |
| 다이브에서 먼저 솟았다가 엎드리는 몸/눈높이 | 인스펙터의 **Dive Pose Delay / Duration**, [MovementPostureController.cs](Player/Movement/Core/MovementPostureController.cs)의 BeginDive 및 높이 처리 |
| 닷지·기울이기·파쿠르·낙하·밀치기 수치 | [MovementAbilitySettings.cs](Player/Movement/MovementAbilitySettings.cs), PlayerMovement의 Ability Settings |
| 닷지 성공, 무적, 쿨타임 | [DodgeController.cs](Player/Movement/Dodge/DodgeController.cs) |
| 스텝 닷지 기본 쿨타임·기울이기 폭/각도 | PlayerMovement의 Ability Settings에서 **Step Dodge Cooldown / Lean Offset / Lean Angle** |
| Shift 유지 중 회피 후 달리기 연결 | [MovementActions.cs](Player/Movement/Core/MovementActions.cs)의 ProcessSprintRampUp, [GroundBurstState.cs](Player/Movement/States/Abilities/GroundBurstState.cs)의 Exit. 바인딩 유지 판정은 MovementInput.SprintButtonHeld |
| 스텝 경로를 스치는 공격도 회피로 인정 | [StepDodgeTrail.cs](Player/Movement/Dodge/StepDodgeTrail.cs) |
| Q/E 기울이기와 성공 후 횡이동 | [MovementLean.cs](Player/Movement/Dodge/MovementLean.cs) |
| 바닥 접지·중력·충돌·경사면 밀착 | [MovementMotor.cs](Player/Movement/Core/MovementMotor.cs) |
| 앉기·포복 높이·천장·환풍구 | [MovementPostureController.cs](Player/Movement/Core/MovementPostureController.cs) |
| Vault/맨틀/렛지/벽/봉의 진입 판정 | [ParkourTraversal.cs](Player/Movement/Parkour/ParkourTraversal.cs), [ParkourDetector.cs](Player/Movement/Parkour/ParkourDetector.cs) |
| Vault 이동 경로와 끝부분 연결 | [ReferenceTraversal.cs](Player/Movement/Parkour/ReferenceTraversal.cs) |
| 낙하 피해·착지 롤링 | [MovementLanding.cs](Player/Movement/Actions/MovementLanding.cs) |
| 마우스 시점·감도 | [PlayerLook.cs](Player/Camera/PlayerLook.cs) |
| 카메라 흔들림·피치·롤·FOV | [PlayerCameraEffects.cs](Player/Camera/PlayerCameraEffects.cs) |
| 입력 키 변경 | **Assets/Actions/Player.inputactions** |

## 폴더 역할

```text
Scripts/
├─ Player/
│  ├─ Movement/       이동의 진입점, 설정, 상태와 물리
│  │  ├─ Core/       상태 머신, 입력, 모터, 자세
│  │  ├─ Actions/    행동 우선순위, 착지
│  │  ├─ Dodge/      회피, 성공 판정, 기울이기
│  │  ├─ Parkour/    지형 탐지와 이동 경로
│  │  └─ States/     행동별 실제 동작과 상태 전환
│  ├─ Combat/         플레이어 밀치기·태클, 체력·피격
│  │  ├─ Input/      마우스/키보드를 사격 의도로 변환
│  │  └─ Weapons/    조준과 발사 실행, 이벤트 발행
│  └─ Camera/         조준 시점과 시각 효과
├─ Combat/             플레이어·적이 함께 쓰는 전투 규칙
│  ├─ Damage/         피해 데이터와 수신 인터페이스
│  ├─ HitDetection/   Raycast/근접 접촉과 회피 판정
│  └─ Weapons/
│     ├─ Data/        무기 설정의 형식 (WeaponData)
│     └─ Core/        장탄·타이머, 입력 의도의 규약
├─ World/              문, 스윙 바, 순간이동 장치
├─ Debug/
│  ├─ Movement/       이동 HUD, 연습용 공격기·표적
│  └─ Combat/         탄창 HUD, 사격 표적
└─ Docs/               기능·조작 설명

Assets/Editor/Movement/ 이동 테스트와 연습 씬 생성 도구
Assets/Editor/Combat/   전투 테스트, 테스트 씬 생성, 플레이어 설치 도구
Assets/Combat/Weapons/  개별 무기의 실제 수치 asset (스크립트 아님)
```

## 전투를 추가할 때

1. 플레이어의 공격 입력·공격 단계·무기 선택은 **Player/Combat/**에 둔다. 새 공격 상태를 PlayerMovement에 계속 추가하지 않는다.
2. 피해량을 실제로 적용할 때는 **Combat/**의 공격 판정과 `IDamageReceiver`를 사용한다. 같은 공격의 여러 히트박스에는 하나의 공격 ID를 공유한다.
3. 태클처럼 실제 몸을 이동시키는 동작만 이동 상태와 연결한다. 현재 `PlayerCombatActions`가 공격을 결정하고, `GroundBurstState`가 태클 이동을 담당한다.
4. 몸의 위치는 `MovementMotor`, 마우스 조준은 `PlayerLook`, 화면 연출은 `PlayerCameraEffects`가 담당한다. 공격 연출에서 플레이어 위치나 조준 피치를 덮어쓰지 않는다.
5. 체력·사망·회피 피드백은 `PlayerDamageReceiver.HitResolved` / `Died` 이벤트로 연결한다. 전투 연결 예시는 [이동 안내의 적 공격 연결](Docs/MovementGuide.md#적-공격-연결)을 참고한다.

현재 PlayerCombatActions는 일반 C# 서비스이며, MovementAbilities가 생성·갱신한다. 총의 사격·재장전은 별도 WeaponController가 관리하므로 PlayerMovement에 무기 상태를 추가하지 않는다. 콤보·장비 교체·반동 연출은 다음 단계다.

## 파일을 늘릴 때의 기준

- **설정만 변경**: 씬 인스펙터나 무기 asset을 먼저 수정한다. 스크립트 기본값을 바꿔도 이미 저장된 씬/asset 값은 자동으로 바뀌지 않는다.
- **플레이어의 입력·행동**: Player 아래 해당 기능에 둔다. 이동 가능한 상태와 발사 가능한 상태를 무조건 하나의 상태 머신으로 합치지 않는다.
- **플레이어와 적이 공유하는 규칙**: Combat에 둔다. 여기서 마우스·키보드를 직접 읽지 않는다.
- **표시/효과**: 카메라 또는 별도 표시 컴포넌트가 이벤트를 받게 한다. HUD에서 탄약을 빼거나 피해를 주지 않는다.
- **검사용 기능**: Debug 또는 Editor에 둔다. 실제 게임 규칙이 테스트 HUD에 의존하지 않게 한다.

새 기능을 만들 때는 입력 → 규칙 → 실행 → 표시 중 어느 책임인지 먼저 정한다. 한 파일이 둘 이상의 큰 책임을 맡기 시작하면 책임별로 분리한다. 파일이 짧다는 이유만으로 무조건 더 쪼개지는 않는다.

## 유지한 연결

이동한 기존 스크립트의 `.meta` GUID를 보존했다. 기존 씬/프리팹의 컴포넌트 연결과 사용하는 설정값, 상태 숫자는 유지한다. 읽는 코드가 전혀 없는 다이브 설정 3개는 씬 데이터에서도 제거했다. 공용 타입을 대규모로 namespace/asmdef로 나누지는 않았다. 이동·카메라·전투 간 호출 흐름은 그대로 두면서 파일의 책임과 위치를 정리했다.

## 인스펙터 참조와 삭제한 코드

`PlayerMovement.Configuration.cs`는 별도 컴포넌트가 아니다. `partial class PlayerMovement`의 수치 필드를 파일로 나눈 것으로, 인스펙터 설정과 같은 데이터다. 삭제하면 이동 코드가 사용하는 설정도 없어진다.

`Crouch Pivot`은 비워두면 자식의 `CrouchPivot`을 찾는다. 카메라의 이동·시점 참조도 초기화 중 자동 탐색하며, 직접 할당은 다른 계층 구조를 쓸 때 필요하다. 이런 선택 참조에는 인스펙터 설명을 추가했다. `PlayerDamageReceiver` 컴포넌트가 없다면 PlayerMovement가 자동으로 붙인다. 자동 연결이 있다고 해당 기능이나 스크립트가 불필요한 것은 아니다.

씬·프리팹 및 코드 참조를 확인하고 다음 항목을 삭제했다.

- `PlayerSupineMotion`: 다리 들기 값을 계산하지만 모델·본·Animator에 적용하지 않던 미완성 코드. 누운 이동 자체는 이동 상태에서 계속 처리한다.
- `TimeManager`: 현재 씬 연결과 호출이 없는 시간 배율 보조 코드.
- `airDiveMinDuration`, `airDiveVerticalPop`, `minDiveSpeed`: 실제 이동에서 읽지 않는 설정 및 대응 공개 속성.

연습용 HUD·공격기·표적과 Editor 검사 도구는 실제 테스트에 사용하므로 유지했다.
