# 직접 관리하는 전투 제작 안내

## 먼저 실행하기

1. Unity에서 `Assets/_Project/Scenes/Development/CombatFoundation.unity`를 열고 Play한다.
2. 화면을 클릭해 마우스를 잡는다. 이 클릭은 발사하지 않는다. 버튼을 한 번 놓은 뒤 좌클릭하면 사격한다.
3. 기본 소총은 좌클릭 유지로 연사, R로 재장전한다. 오른쪽 위에서 잔탄과 장전 시간을 확인한다. Esc는 마우스를 해제한다.
4. 정면/오른쪽 표적에 맞히면 표적 HP가 줄어든다. 왼쪽 표적은 엄폐물에 막힌다. 총 모델이나 발사 애니메이션은 아직 없다.
5. Console에서 `[Weapon]` 로그의 탄약·명중 대상·피해 결과를 본다. Scene 뷰의 Gizmos를 켜면 발사 선을 볼 수 있다. 빨강은 고체에 명중, 청록은 빗나감이며 빨강이라고 반드시 대미지가 들어간 것은 아니다.

기존 씬에서도 쓰려면 Play를 종료하고 **PlayerMovement가 있는 오브젝트**를 선택한 뒤 `Tools → Combat → Add Foundation Weapon To Selected Player`를 실행하고 씬을 저장한다. 입력/무기 컴포넌트와 기본 소총만 설치한다. 탄창 표시가 필요하면 같은 오브젝트에 `WeaponDebugHUD`를 추가한다.

## 구조를 읽는 순서

```text
Player.inputactions / Combat 맵
    ↓ 좌클릭·R
PlayerWeaponInput                   장치 입력, 커서/포커스 검사
    ↓ WeaponInputFrame              이번 프레임 눌림 / 누르는 중 / 장전
WeaponController.Update
    ├─ WeaponRuntime.Tick           발사 가능 여부, 탄약 소모, 장전 타이머
    │    ↑ WeaponData               무기별 고정 설정
    ├─ AttackTrace.Trace            조준 방향 Raycast, 첫 벽에서 멈춤
    │    ├─ IDamageReceiver.ReceiveHit → HP 감소 또는 회피
    │    └─ StepDodgeTrail          실제 명중점까지의 회피 경로 인정
    └─ ShotFired 이벤트             나중에 소리·이펙트·반동이 구독할 위치
```

`WeaponData`는 설계도, `WeaponRuntime`은 현재 들고 있는 한 자루의 상태다. 두 플레이어가 같은 WeaponData를 사용해도 탄약은 따로 갖는다. 현재 잔탄을 ScriptableObject에 저장하지 않는다.

씬에 붙이는 것은 `PlayerWeaponInput`, `WeaponController`, 필요하면 `WeaponDebugHUD`다. `WeaponRuntime`, `WeaponInputFrame`, `AttackTrace`는 인스펙터에 붙이는 컴포넌트가 아니다. `WeaponData`는 Project 창에서 만드는 asset이다.

## 내가 바꾸려는 것 → 수정 위치

| 원하는 변경 | 수정 위치 | 확인 방법 |
| --- | --- | --- |
| 대미지, RPM, 탄창, 장전 시간, 사거리 | `Assets/_Project/Data/Combat/Weapons`의 해당 asset | Play 종료 후 값 변경, 다시 Play하여 HP/탄창 확인 |
| 새 총 종류 | WeaponData asset 복제 또는 Create 메뉴 | WeaponController의 Weapon Data에 할당 |
| 단발/연사 | asset의 Fire Mode | SemiAutomatic은 누를 때 1발, Automatic은 유지 연사 |
| 사격·재장전 키 | `Assets/_Project/Input/Player.inputactions` → Combat → Fire/Reload bindings | 저장 후 재실행. 맵/액션 이름은 유지 |
| 빈 탄창 자동 장전, 예비 탄약 규칙 | `Combat/Weapons/Core/WeaponRuntime.cs` | 탄창 바닥·장전 중 사격·장전 취소 검증 |
| 총알 시작점·판정 레이어·발사 로그 | `Player/Combat/Weapons/WeaponController.cs` 또는 해당 인스펙터 | 엄폐물과 자기 몸 충돌 확인 |
| 관통·명중 부위 판정 | `Combat/HitDetection/AttackTrace.cs` | 벽 차폐와 회피 검사도 함께 실행 |
| 적 체력·피격 반응 | 적의 `IDamageReceiver` 구현 | 자식 Collider에 맞혀도 부모 수신자로 연결되는지 확인 |
| 플레이어 HP·사망·피격 회피 | `Player/Combat/PlayerDamageReceiver.cs` | 기존 이동/회피 검사 실행 |
| 탄창 표시 | `Debug/Combat/WeaponDebugHUD.cs` | 표시 코드에서 탄약 자체를 변경하지 않음 |
| 사격 표적의 체력 | `Debug/Combat/WeaponTrainingTarget.cs`의 인스펙터 | 다시 Play하거나 Reset Target 컨텍스트 메뉴 |

## 코드 없이 새 무기 만들기

1. Play를 종료한다. Project 창에서 `Assets/_Project/Data/Combat/Weapons`를 연다.
2. 우클릭 → **Create → Morphang → Combat → Weapon Data**. 또는 기존 asset을 복제한다.
3. 파일 이름과 Display Name을 정하고 Damage, Rounds Per Minute, Magazine Size, Reload Duration, Range를 설정한다. RPM 600은 초당 10발이다. 시간 단위는 초, 사거리는 Unity 월드 단위다.
4. 씬 플레이어의 WeaponController → **Weapon Data**에 새 asset을 넣고 저장한다. 플레이 중 asset을 바꾸는 방식의 무기 교체는 아직 지원하지 않는다.
5. Play에서 탄창을 비우고 재장전한다. 단발이면 버튼을 유지해도 한 발만 나오는지 확인한다.

준비된 예제는 `FoundationRifle`(자동, 20 대미지, 600 RPM, 30발, 1.5초)과 `FoundationPistol`(단발, 35 대미지, 300 RPM, 12발, 1.2초)이다. Play 중 ScriptableObject 수치를 바꾸면 변경이 남을 수 있으므로 조정은 Play를 종료하고 하는 편이 관리하기 쉽다.

## 인스펙터에서 꼭 필요한 연결

- **Weapon Data**: 필수. 자동으로 임의의 무기를 선택하지 않는다.
- **Input Source**: 선택. 비워두면 같은 오브젝트의 `IWeaponInput` 구현을 찾는다. 기본 설치에서는 PlayerWeaponInput이다.
- **Aim Origin**: 선택. 비워두면 플레이어 자식 Camera를 찾는다. 현재는 화면 중앙에서 쏘는 방식이다.
- **Input Actions**: 선택. 비워두면 프로젝트의 InputSystem.actions를 사용한다. `Combat/Fire`, `Combat/Reload`가 있어야 한다.
- **Hit Layers**: 탄환이 부딪칠 레이어. 표적뿐 아니라 벽도 포함해야 벽 뒤의 적을 맞히지 않는다. Trigger는 무시한다.
- **Log Shots / Draw Rays**: 검사용. 평상시 로그가 많으면 끈다. `WeaponDebugHUD`는 테스트용이며 게임용 탄약 표시는 `AmmoHUD`가 담당한다.

자동 연결되는 선택 필드를 비워도 정상인 것은 의도된 동작이다. 그 컴포넌트의 역할이 없어졌다는 뜻은 아니다. 다른 카메라 계층이나 입력을 쓰려면 직접 연결하면 된다.

## 문제를 찾는 순서

**발사 자체가 안 됨**: Console의 빨간 오류 → Weapon Data 연결 → 입력/무기 컴포넌트 활성화 → Combat 액션 이름 → Play 창 포커스와 마우스 잠금 → 잔탄/장전 상태 순으로 확인한다. 잠금 해제 상태에서는 발사하지 않으며 다시 잠글 때 누른 클릭도 발사에서 제외한다.

**발사 로그는 나오지만 HP가 안 줄어듦**: 로그의 hit와 result를 본다. `miss`면 방향/거리/레이어를 확인한다. 벽 이름이면 엄폐물에 막혔다. `Ignored`면 수신자 존재·사망 여부·피해 조건을 본다. `Evaded`는 정상 회피다. `Duplicate`는 같은 공격 ID를 다시 받았다는 뜻이다. 각 총알은 새 ID를 갖고, 하나의 근접 공격에 달린 여러 히트박스는 같은 ID를 공유해야 한다.

**총의 연사력이 프레임마다 달라 보임**: 애니메이션 이벤트에서 발사하지 않는다. WeaponRuntime의 Update 시간 누적을 유지한다. 지속 연사만 소수 시간 오차를 이월하고, 긴 멈춤 후 한 프레임에 몰아 쏘는 양은 최대 4발로 제한한다. 일부러 게임이 장시간 멈춘 경우 정확한 RPM보다 과도한 몰아 쏘기 방지를 우선한다.

## 직접 검증하고 저장하기

1. 작은 변경 하나를 한다. 수치 변경과 로직 변경을 한꺼번에 섞지 않는다.
2. Play를 종료한 상태에서 **Tools → Combat → Run Foundation Checks**를 실행한다. 성공하면 Console에 `WEAPON_FOUNDATION_CHECKS_PASSED: 11`이 나온다.
3. 공용 피격/회피/이동을 수정했다면 **Tools → Movement → Run Ability Checks**도 실행한다. 상태 머신 자체를 수정했다면 Run Refactor Checks도 확인한다.
4. CombatFoundation에서 실제로 조준·발사·장전한다. 자동 검사는 손으로 느끼는 조작감이나 모든 씬 배치를 대신하지 않는다.
5. Console 오류와 변경 목록을 확인한 뒤 Git으로 저장한다. 파일을 이동/이름 변경할 때는 Unity Project 창을 사용하거나 `.meta`를 함께 이동한다. `.meta`를 잃으면 씬/asset 연결이 끊길 수 있다.

검사와 테스트 씬 생성은 현재 씬과 분리된 임시 additive 씬에서 작업한다. `Tools → Combat → Create Foundation Test Scene`은 기존 테스트 씬을 덮어쓰지 않고 새 씬을 만든다. 테스트 도구는 실행 전 Play를 종료해야 한다.

## 지금 되는 것과 다음 단계

현재는 4슬롯, 단발/점사/연사, 슬롯별 탄창과 유한 예비 탄약, 수동 R 재장전, 총구 기준 히트스캔과 헤드샷, ADS, 반동·탄퍼짐, 근접 벽 high-ready를 지원한다. 전술/빈 탄창 재장전 시간은 다르며 회피·밀치기·슬롯 전환·사망은 진행 중 장전을 취소한다. 완료 시에만 예비 탄약을 탄창으로 옮긴다. 시간 배율 0에서는 발사·장전 시간이 진행하지 않는다. 자동 재장전은 하지 않는다.

Q/E 기울이기의 Micro 회피 판정은 ADS와 재장전을 유지하며 일반 Step 회피의 FOV/회전 충격을 추가하지 않는다. 일반 Step 회피는 기존처럼 조준과 장전을 끊는다. 하이 레디 상태와 총을 내리는 전환 중에는 ADS를 차단하고 기본 시야·크로스헤어로 복귀한다. 우클릭을 유지한 채 장애물에서 벗어나면 총을 내린 뒤 다시 조준한다. 카메라의 기울이기 적용 후 총구 장애물을 검사하고 같은 프레임의 최종 ADS 상태로 FOV를 반영한다.

총구의 조준점 추적은 카메라 회전이나 Q/E를 늦추지 않고 가까운 표적↔먼 표적 전환에서 생기는 수렴 각도만 부드럽게 바꾼다. 무기 데이터의 **Aim Tracking Feel → Aim Follow Half Life**는 지향 사격, **Ads Aim Follow Half Life**는 정조준 반응을 조절한다. 값이 클수록 천천히 따라가며 0이면 기존 즉시 추적이다. 라이플 기본 0.055/0.025초(약 0.18/0.08초에 90% 수렴), 피스톨 0.045/0.02초다. 실제 히트스캔·벽 차단·하이 레디 판정은 보간하지 않는다. 무기 교체와 하이 레디 진입 시 과거 추적 상태를 버린다.

오른쪽 아래 `AmmoHUD`는 큰 장탄수, 작은 `/ 탄창 용량`, 별도의 `RESERVE` 예비탄을 표시한다. 평상시 하단 막대는 탄창 잔량, 재장전 중에는 실제 장전 진행률이다. 잔량 25% 이하는 주황색과 `[R] RELOAD`, 빈 탄창은 빨간색, 예비탄까지 바닥나면 `OUT OF AMMO`로 표시한다. `NO RESERVE`는 탄창 안의 탄은 사용할 수 있지만 다음 재장전용 예비탄이 없다는 뜻이다. 맨손·슬롯 해제·사망에는 숨긴다. 기존 AmmoHUD 컴포넌트의 Screen Margin, Hud Scale, Colors로 위치·크기·색상·경고 기준을 조절한다. 별도 캔버스를 만들지 않고 기존 HUD 캔버스 안에서 표시하며 탄약 규칙은 변경하지 않는다. 실제 씬 표시와 재장전/소진 검사는 격리 프로젝트에서 `AmmoHUDChecks.RunBatch`를 `-quit` 없이 실행한다.

`WeaponData`의 반동 패턴은 실제 시점에 적용되며 `ShotFired` 이벤트도 유지한다. `WeaponPresentation`은 기존 모델에 견착·총구 수렴·high-ready·전술/빈 탄창 재장전의 임시 자세를 적용한다. 탄약과 발사 타이밍은 `WeaponRuntime`이 결정한다. 모델·리그별 본격적인 손/탄창 애니메이션은 이 결과에 연결한다. 상세 수치와 연결 방법은 [무기 시스템 설계](WeaponSystemDesign.md)의 원거리 항목을 참고한다.

슬롯 관리는 `PlayerWeaponSlots`, 조준 안전성은 `WeaponAimSolver`, 시각 자세는 `WeaponPresentation`, 적 행동은 `EnemyCombatAI`에 분리되어 있다. `Tools → Combat → Run Ranged Combat Checks`로 새 원거리 규칙을 검사할 수 있다. 실제 씬의 총구 연결·FOV·반동·회피 취소 검사는 격리 프로젝트에서 `RangedCombatPlayChecks.RunBatch`를 `-quit` 없이 실행한다.
