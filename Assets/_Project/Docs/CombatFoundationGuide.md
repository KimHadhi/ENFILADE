# 전투 제작 안내

[프로젝트 안내](../../../README.md) · [전투 규칙과 확장 계획](WeaponSystemDesign.md) · [전투 코드 구조](../Scripts/Combat/README.md) · [검증 안내](ValidationGuide.md)

이 문서는 현재 전투 씬을 실행하고, 무기 수치와 표시를 수정하는 작업 순서다. 행동 간 우선순위와 이후 기획은 **전투 규칙과 확장 계획**에서 확인한다.

## 바로 실행하기

1. [CombatFoundation 씬](../Scenes/Development/CombatFoundation.unity)을 열고 Play한다.
2. Game 창을 클릭해 마우스를 잠근다. 잠금을 잡는 첫 클릭은 공격하지 않으므로 버튼을 놓고 다시 누른다.
3. **1**로 소총, **2**로 피스톨을 선택한다. 현재 씬은 슬롯을 해제한 상태로 시작한다.
4. **좌클릭**으로 발사하고 **우클릭 유지**로 정조준(ADS), **R**로 재장전한다.
5. **3 또는 4**의 빈 슬롯을 선택하면 주먹을 사용한다. 선택한 숫자를 다시 누르면 무기와 주먹을 모두 내린다.
6. **Esc**로 일시정지하고 설정 메뉴를 연다(마우스도 풀린다). Esc나 "돌아가기"로 돌아온다. 이동·슬라이딩 조작은 [이동 안내](MovementGuide.md)를 참고한다.

총의 자세는 절차적으로 정하고 [몸의 손](PlayerBodyGuide.md#몸의-손)이 그 총을 잡는다. 지금 씬의 총 모델은 재질을 비워 보이지 않는다. 탄창 교체와 무기별 정식 애니메이션은 후속 작업이다.

## 조작과 화면 확인

| 입력 | 총을 든 상태 | 빈 슬롯을 선택한 상태 |
| --- | --- | --- |
| 좌클릭 | 단발·점사·연사 등 무기 설정에 따라 발사 | 누르면 차징, 놓으면 주먹 공격 |
| 우클릭 | 정조준 유지 | 가드, 새 입력 직후 패링 판정 |
| R | 수동 재장전 | 사용하지 않음 |
| V | 밀치기. 시선 쪽으로 달리며 누르면 돌진 밀치기 | 짧게 눌렀다 놓으면 밀치기, 길게 누르면 한 명에게 가드 브레이크. 시선 쪽으로 달리며 누르면 돌진 밀치기 |
| 1 / 2 / 3 / 4 | 원거리 / 원거리 / 근접 / 특수 슬롯 선택·해제 | 동일 |
| F | 가까운 흐트러진 적에게 처형 표시가 뜨면 처형 | 동일. 슬롯을 해제한 상태에서도 가능 |
| Q / E | 기울이기·마이크로 회피, ADS와 함께 사용 가능 | 기울이기·마이크로 회피 |

- **탄약 HUD**: 장탄수, 탄창 용량, 예비탄을 표시한다. 하단 막대는 평소 탄창 잔량, 장전 중 실제 장전 진행률이다.
- **탄약 경고**: `RELOAD`는 재장전 권장, `NO RESERVE`는 다음 장전용 예비탄 없음, `OUT OF AMMO`는 장탄·예비탄 모두 없음이다.
- **적 표시**: HP와 체간이 줄어들며, 체간이 바닥나면 흐트러짐 표시와 조건에 따른 `[F] 처형하기` 안내가 나타난다.
- **완벽 방어**: 완벽 회피 또는 연속기 마지막 타격 패링 후 `[LMB] 반격`이 뜬다. 이때 새 좌클릭은 일반 공격보다 반격을 먼저 실행한다.
- **처치 점수**: 처치하면 크로스헤어 오른쪽 아래에 XP와 처치 사유가 뜬다. 처형으로 처치하면 `처형` 줄에 전술 단검과 베기 자국 아이콘(빨간색), 반격으로 처치하면 `반격` 줄에 되돌아오는 화살표 아이콘(반격 색)이 붙는다. 일반 처치·헤드샷 줄은 기존 해골·폭발 아이콘 그대로다.
- **시험용 적**: 채팅에 `/spawn`, 콘솔 채널에 `spawn`을 입력한다. 소환한 적은 웨이브 진행에 포함되지 않는다. 명령 목록은 `/help`로 확인한다.

### 채팅·목표판과 테스트 명령

**Enter**로 채팅을 열고 메시지나 명령을 입력한 뒤 Enter로 제출한다. 채팅 중 **Tab**은 채널 전환, **Esc**는 닫기, 위·아래 화살표는 입력 기록이다. 채팅이 닫혀 있을 때 Tab은 목표판을 연다. 채팅 중에는 이동·전투 입력을 멈춘다.

| 명령 | 용도 |
| --- | --- |
| `/help` | 현재 등록된 명령과 인자 확인 |
| `/map 씬이름` | 빌드에 등록된 씬으로 이동. 예: `/map MovementAbilityPlayground` |
| `/spawn`, `/spawn 5` | 생략하면 1마리, 수량을 붙이면 해당 수만큼 테스트 적 소환. 빈 공간이 부족하면 실제 소환 수를 알림. 웨이브 집계 제외 |
| `/ammo` | 보유한 모든 총기의 탄창을 채우고 기본 예비탄까지 보충. 기본보다 많은 예비탄은 유지 |
| `/wave start` | 대기 중인 웨이브 시작 |
| `/wave stop`, `/wave resume` | 웨이브 일시정지 / 재개 |
| `/wave abort`, `/wave status` | 웨이브 종료 / 상태 확인 |
| `/stats`, `/heal`, `/revive` | 플레이어 상태 확인 / HP 회복 / 사망 복구·실드 초기화 |
| `/hurt 20`, `/shield 50`, `/maxhp 200` | 피격·실드·최대 HP와 HUD 확인 |
| `/hit 10 left`, `/hit 10 back` | 방향성 플레이어 피격·혈흔 비네트·쉐이크 시험. front/right/back/left 지원 |
| `/ult 100`, `/ultuse` | 궁극기 게이지 충전·자원 소비 테스트. 무기 진화나 스킬은 실행하지 않음 |
| `/ultmax 200`, `/ultcost 70` | 궁극기 최대 보유량을 200%, 1회 소비량을 70%로 각각 지정. 증가량이 아닌 지정한 값으로 설정 |

위 명령은 실행 중인 테스트 상태를 바꾼다. 영구적인 밸런스 설정은 [설정·유지보수 안내](MaintenanceGuide.md)의 표와 Inspector 위치에서 수정한다.

## 무엇을 바꿀지에 따른 수정 위치

숫자는 **[밸런스 표](MaintenanceGuide.md#밸런스-표-data-tables)가 가장 우선**한다. 표는 저장하면 Play 중에도 바로 반영된다. 표에 없는 값은 코드의 기본값보다 씬·프리팹·설정 asset에 저장된 값이 우선하므로, Play를 종료한 뒤 수정한다.

| 바꾸려는 것 | 수정 위치 |
| --- | --- |
| 피해량, 발사 방식, RPM, 탄창·예비탄, 장전 시간, 탄퍼짐, ADS 배율, 몸의 손이 잡는 위치 | [Weapons.csv](../Resources/Tables/Weapons.csv)의 해당 무기 행 |
| 조준 반동, 총 모델 추적·high-ready 반응, 무기 사운드·아이콘 | [무기 데이터 폴더](../Data/Combat/Weapons) → 해당 `WeaponData`의 Recoil Feel / Aim Tracking Feel / High-ready 항목 |
| 무기별 총성, 화면 킥, 총구·탄피, 총알 체간 피해 | `WeaponData`의 Feel 및 Feel → Target Reaction 항목 |
| 전체 타격감 켜기·끄기, 히트마커, 점수, 피격·처치 효과 | [GunFeel.asset](../Resources/Combat/GunFeel.asset) |
| 플레이어 피격 아크·혈흔·쉐이크·25% 빈사 박동 | [PlayerDamageFeedback.asset](../Resources/Combat/PlayerDamageFeedback.asset), [피격 효과 안내](PlayerDamageFeedbackGuide.md) |
| 방어·밀치기 소리, 반격, 흐트러짐 표시, 피해 팝업·HP 손실 표시 | [Feedback.asset](../Resources/Combat/Feedback.asset) |
| 시작 무기와 시작 슬롯 | 플레이어의 `PlayerWeaponSlots` → Slot 1~4 / Starting Slot |
| 주먹·가드·패링·V 홀드 | [Player.csv](../Resources/Tables/Player.csv)의 `Fists` 행 |
| 밀치기·돌진 밀치기·슬라이딩 수치 | `Player.csv`의 `Ability` 행 |
| 적 체력·공격·체간·이동 속도 | [Enemies.csv](../Resources/Tables/Enemies.csv) |
| 적의 몸 크기·밀치기 연쇄 | [적 프리팹](../Prefabs/Enemies)의 `MeleeCombatTarget` |
| 사격·장전·가드 키 | [Player.inputactions](../Input/Player.inputactions)의 Combat 맵 |
| 슬롯 숫자 키 | [PlayerWeaponSlots.Update](../Scripts/Player/Combat/Weapons/PlayerWeaponSlots.cs)의 직접 키보드 입력 |
| 탄약 UI의 위치·크기·색 | 씬의 `AmmoHUD` → Screen Margin / Hud Scale / Colors |

슬롯 키는 현재 Input Actions의 Slot 액션을 사용하지 않고 숫자열과 숫자패드 1~4를 직접 읽는다. Slot 액션 바인딩만 수정해서는 슬롯 키가 바뀌지 않는다.

## 새 총 데이터 연결하기

1. [무기 데이터 폴더](../Data/Combat/Weapons)에서 기존 asset을 복제하거나 **Create → ENFILADE → Combat → Weapon Data**를 선택한다. `Category`는 **Ranged**로 둔다.
2. [Weapons.csv](../Resources/Tables/Weapons.csv)에 asset과 같은 이름의 행을 추가하고 `displayName`, `fireMode`, `damage`, `roundsPerMinute`, `magazineSize`를 적는다.
3. `initialReserveAmmo`, `reloadDuration`, `emptyReloadDuration`을 조절한다. RPM 600은 초당 10발이며 시간 단위는 초다. 몸의 손이 잡을 위치(`rightGrip*`, `leftGrip*`, `holdOffset`)도 모델에 맞춘다.
4. 현재 CombatFoundation에서는 **PlayerWeaponSlots의 Slot 1 또는 Slot 2**에 할당한다. Starting Slot은 0이면 해제, 1~4이면 해당 슬롯으로 시작한다.
5. 모델을 바꾸면 `WeaponController`의 Weapon Root / Muzzle Point를 확인한다. 실제 총구의 로컬 **+Z**가 발사 방향이며 Sight Height는 모델별로 조절한다.
6. 단발·점사·연사, 장전 취소, 슬롯 전환 후 탄약 보존, 벽 앞 사격 차단을 확인한다.

`WeaponController`의 Weapon Data를 직접 할당하는 방식은 슬롯 관리자가 없는 단일 무기 테스트용이다. 슬롯 관리자가 있으면 선택한 슬롯의 데이터와 런타임이 컨트롤러에 연결된다. 새 근접·특수 무기는 Category만 바꿔 완성되지 않으며, 유형별 공격 구현과 모델 연결이 추가로 필요하다.

현재 예제 무기는 다음과 같다. 값은 `Weapons.csv` 기준이다.

| 데이터 | 방식 | HP 피해 | RPM | 탄창 / 예비탄 | 잔탄 있는 장전 / 빈 탄창 장전 |
| --- | --- | ---: | ---: | ---: | ---: |
| [FoundationRifle](../Data/Combat/Weapons/FoundationRifle.asset) | 연사 | 20 | 600 | 30 / 90 | 1.5초 / 2초 |
| [FoundationPistol](../Data/Combat/Weapons/FoundationPistol.asset) | 단발 | 35 | 300 | 12 / 48 | 1.2초 / 1.65초 |

## 다른 씬에 연결하기

CombatFoundation의 `★Player` 오브젝트와 HUD Canvas를 복사해 붙여 넣거나 프리팹으로 만들어 쓴다. 전투·체력·궁극기 컴포넌트는 Play 시작 때 `PlayerMovement`가 붙이고 수치는 [밸런스 표](MaintenanceGuide.md#밸런스-표-data-tables)에서 가져오므로, 플레이어에 직접 둘 컴포넌트는 이동·시점·입력·무기·슬롯뿐이다.

`WeaponController`의 선택 참조는 비워두면 기존 계층에서 찾는다. Input Source는 같은 오브젝트의 `IWeaponInput`, Aim Origin은 플레이어 카메라, Muzzle Point는 하위의 `MuzzleEffects` 위치를 쓴다. Weapon Root(총 모델)는 직접 할당하며, 장비가 없거나 근접일 때 이 오브젝트를 숨긴다. Hit Layers에는 적과 벽을 모두 포함해야 한다.

## 문제를 찾는 순서

| 증상 | 먼저 확인할 것 |
| --- | --- |
| 공격이 전혀 안 됨 | Console 오류 → 슬롯 선택 → 입력·플레이어 생존 상태 → Game 창 포커스와 마우스 잠금 → 채팅·처형 등 행동 잠금 |
| 총이 안 나감 | 실제 선택 슬롯의 데이터 → 장탄·장전 상태 → 총구 high-ready 여부 → Combat/Fire 입력 |
| 벽 근처에서 총이 올라감 | 정상적인 총구 안전 장치인지 확인. 모델의 총구 위치·+Z 방향과 Hit Layers 검사 |
| 발사했지만 HP가 안 줄어듦 | `Log Shots`의 hit / result 확인. 엄폐, 사거리, 수신자, 회피, 사망 상태 순서로 추적 |
| 재장전이 안 됨 | 탄창이 이미 가득 찼는지, 예비탄이 있는지, 회피·밀치기·슬롯 전환이 장전을 취소했는지 확인 |
| 설정을 바꿨는데 반영되지 않음 | 표에 같은 값이 있으면 표가 이긴다. 표에 없다면 수정한 asset이 실제 슬롯에 연결됐는지, 씬·프리팹에 다른 값이 저장됐는지 확인 |
| 효과만 약하거나 없음 | GunFeel의 Master Switches와 해당 무기의 Feel 설정, Feedback의 음량·참조 확인 |

`Log Shots`와 `Draw Rays`는 `WeaponController`의 진단 옵션이다. 빨간 레이는 고체에 명중했다는 뜻이며 피해 성공을 보장하지 않는다. 피해 결과의 `Evaded`는 회피, `Duplicate`는 같은 공격 ID의 중복 수신이다.

변경 후 확인할 항목은 [검증 안내](ValidationGuide.md)를 따른다. 조작감·모델 자세·화면 가독성은 실제 씬에서 확인한다.
