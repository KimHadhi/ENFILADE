# 전투 제작 안내

[프로젝트 안내](../README.md) · [전투 규칙과 확장 계획](WeaponSystemDesign.md) · [전투 코드 구조](../Scripts/Combat/README.md) · [검증 안내](ValidationGuide.md)

이 문서는 현재 전투 씬을 실행하고, 무기 수치와 표시를 수정하는 작업 순서다. 행동 간 우선순위와 이후 기획은 **전투 규칙과 확장 계획**에서 확인한다.

## 바로 실행하기

1. [CombatFoundation 씬](../Scenes/Development/CombatFoundation.unity)을 열고 Play한다.
2. Game 창을 클릭해 마우스를 잠근다. 잠금을 잡는 첫 클릭은 공격하지 않으므로 버튼을 놓고 다시 누른다.
3. **1**로 소총, **2**로 피스톨을 선택한다. 현재 씬은 슬롯을 해제한 상태로 시작한다.
4. **좌클릭**으로 발사하고 **우클릭 유지**로 정조준(ADS), **R**로 재장전한다.
5. **3 또는 4**의 빈 슬롯을 선택하면 주먹을 사용한다. 선택한 숫자를 다시 누르면 무기와 주먹을 모두 내린다.
6. **Esc**로 마우스를 해제한다. 이동·슬라이딩 조작은 [이동 안내](MovementGuide.md)를 참고한다.

현재 씬에는 총 모델과 임시 절차적 자세·이펙트가 연결되어 있다. 손·탄창 교체와 무기별 정식 애니메이션은 후속 작업이다.

## 조작과 화면 확인

| 입력 | 총을 든 상태 | 빈 슬롯을 선택한 상태 |
| --- | --- | --- |
| 좌클릭 | 단발·점사·연사 등 무기 설정에 따라 발사 | 누르면 차징, 놓으면 주먹 공격 |
| 우클릭 | 정조준 유지 | 가드, 새 입력 직후 패링 판정 |
| R | 수동 재장전 | 사용하지 않음 |
| V | 밀치기, 전방 달리기 조건을 만족하면 별도 태클 | 짧게 눌렀다 놓으면 밀치기, 길게 누르면 한 명에게 가드 브레이크 |
| 1 / 2 / 3 / 4 | 원거리 / 원거리 / 근접 / 특수 슬롯 선택·해제 | 동일 |
| F | 가까운 흐트러진 적에게 처형 표시가 뜨면 처형 | 동일. 슬롯을 해제한 상태에서도 가능 |
| Q / E | 기울이기·마이크로 회피, ADS와 함께 사용 가능 | 기울이기·마이크로 회피 |

- **탄약 HUD**: 장탄수, 탄창 용량, 예비탄을 표시한다. 하단 막대는 평소 탄창 잔량, 장전 중 실제 장전 진행률이다.
- **탄약 경고**: `RELOAD`는 재장전 권장, `NO RESERVE`는 다음 장전용 예비탄 없음, `OUT OF AMMO`는 장탄·예비탄 모두 없음이다.
- **적 표시**: HP와 체간이 줄어들며, 체간이 바닥나면 흐트러짐 표시와 조건에 따른 `[F] 처형하기` 안내가 나타난다.
- **완벽 방어**: 완벽 회피 또는 연속기 마지막 타격 패링 후 `[LMB] 반격`이 뜬다. 이때 새 좌클릭은 일반 공격보다 반격을 먼저 실행한다.
- **시험용 적**: 채팅에 `/spawn`, 콘솔 채널에 `spawn`을 입력한다. 소환한 적은 웨이브 진행에 포함되지 않는다. 명령 목록은 `/help`로 확인한다.

## 무엇을 바꿀지에 따른 수정 위치

수치는 **코드의 기본값보다 씬·프리팹·설정 asset에 저장된 값이 우선**한다. 영구적으로 조정할 때는 Play를 종료한 뒤 수정한다.

| 바꾸려는 것 | 수정 위치 |
| --- | --- |
| 피해량, 발사 방식, RPM, 탄창·예비탄, 장전 시간 | [무기 데이터 폴더](../Data/Combat/Weapons) → 해당 `WeaponData` |
| 탄퍼짐, 조준 반동, ADS, 총 모델 추적·high-ready 반응 | `WeaponData`의 Accuracy / Recoil Feel / Aim Down Sights / Aim Tracking Feel / High-ready 항목 |
| 무기별 총성, 화면 킥, 총구·탄피, 총알 체간 피해 | `WeaponData`의 Feel 및 Feel → Target Reaction 항목 |
| 전체 타격감 켜기·끄기, 히트마커, 점수, 피격·처치 효과 | [GunFeel.asset](../Resources/Combat/GunFeel.asset) |
| 방어·밀치기 소리, 반격, 흐트러짐 표시, 피해 팝업·HP 손실 표시 | [Feedback.asset](../Resources/Combat/Feedback.asset) |
| 시작 무기와 시작 슬롯 | 플레이어의 `PlayerWeaponSlots` → Slot 1~4 / Starting Slot |
| 주먹·가드·패링·V 홀드 | 플레이어의 `PlayerMeleeCombat` |
| 밀치기·태클·슬라이딩 수치 | 플레이어의 `PlayerMovement` → Ability Settings |
| 적 체력·AI·체간·몸 크기 | [적 프리팹](../Prefabs/Enemies)의 `EnemyHealth`, `EnemyCombatAI`, `MeleeCombatTarget` |
| 사격·장전·가드 키 | [Player.inputactions](../Input/Player.inputactions)의 Combat 맵 |
| 슬롯 숫자 키 | [PlayerWeaponSlots.Update](../Scripts/Player/Combat/Weapons/PlayerWeaponSlots.cs)의 직접 키보드 입력 |
| 탄약 UI의 위치·크기·색 | 씬의 `AmmoHUD` → Screen Margin / Hud Scale / Colors |

슬롯 키는 현재 Input Actions의 Slot 액션을 사용하지 않고 숫자열과 숫자패드 1~4를 직접 읽는다. Slot 액션 바인딩만 수정해서는 슬롯 키가 바뀌지 않는다.

## 새 총 데이터 연결하기

1. [무기 데이터 폴더](../Data/Combat/Weapons)에서 기존 asset을 복제하거나 **Create → Morphang → Combat → Weapon Data**를 선택한다.
2. `Category`를 **Ranged**로 두고 Display Name, Fire Mode, Damage, Rounds Per Minute, Magazine Size를 설정한다.
3. `Initial Reserve Ammo`, `Reload Duration`, `Empty Reload Duration`을 조절한다. RPM 600은 초당 10발이며 시간 단위는 초다.
4. 현재 CombatFoundation에서는 **PlayerWeaponSlots의 Slot 1 또는 Slot 2**에 할당한다. Starting Slot은 0이면 해제, 1~4이면 해당 슬롯으로 시작한다.
5. 모델을 바꾸면 `WeaponController`의 Weapon Root / Muzzle Point를 확인한다. 실제 총구의 로컬 **+Z**가 발사 방향이며 Sight Height는 모델별로 조절한다.
6. 단발·점사·연사, 장전 취소, 슬롯 전환 후 탄약 보존, 벽 앞 사격 차단을 확인한다.

`WeaponController`의 Weapon Data를 직접 할당하는 방식은 슬롯 관리자가 없는 단일 무기 테스트용이다. 슬롯 관리자가 있으면 선택한 슬롯의 데이터와 런타임이 컨트롤러에 연결된다. 새 근접·특수 무기는 Category만 바꿔 완성되지 않으며, 유형별 공격 구현과 모델 연결이 추가로 필요하다.

현재 저장된 예제 데이터는 다음과 같다. 이 표는 코드 기본값이 아니라 해당 asset의 설정이다.

| 데이터 | 방식 | HP 피해 | RPM | 탄창 / 예비탄 | 잔탄 있는 장전 / 빈 탄창 장전 |
| --- | --- | ---: | ---: | ---: | ---: |
| [FoundationRifle](../Data/Combat/Weapons/FoundationRifle.asset) | 연사 | 20 | 600 | 30 / 90 | 1.5초 / 2초 |
| [FoundationPistol](../Data/Combat/Weapons/FoundationPistol.asset) | 단발 | 35 | 300 | 12 / 48 | 1.2초 / 1.65초 |

## 다른 씬에 연결하기

- **기본 총기만 설치**: Play를 종료하고 `PlayerMovement` 오브젝트를 선택한 뒤 **Tools → Combat → Add Foundation Weapon To Selected Player**를 실행한다. 입력·총기 컴포넌트와 기본 데이터 연결을 추가하고 씬을 저장한다.
- **4슬롯과 HUD 설치**: 플레이어와 기존 채팅 Canvas가 있는 씬에서 **Tools → HUD → Install Weapon Slots**를 실행한다. 이 도구는 씬과 재사용 HUD 프리팹을 저장하므로 설정 변경으로 취급한다.
- **별도 기본 시험장 생성**: **Tools → Combat → Create Foundation Test Scene**은 Development 폴더에 새 이름의 간단한 테스트 씬을 만든다. 현재 CombatFoundation의 전체 UI·적·모델 구성을 복제하는 도구는 아니다.

`WeaponController`의 선택 참조는 비워두면 기존 계층에서 찾는다. Input Source는 같은 오브젝트의 `IWeaponInput`, Aim Origin은 플레이어 카메라, Weapon Root와 Muzzle Point는 기존 모델·트레이서 연결을 사용한다. 다른 계층의 모델이나 카메라를 쓰면 직접 할당한다. Hit Layers에는 적과 벽을 모두 포함해야 한다.

## 문제를 찾는 순서

| 증상 | 먼저 확인할 것 |
| --- | --- |
| 공격이 전혀 안 됨 | Console 오류 → 슬롯 선택 → 입력·플레이어 생존 상태 → Game 창 포커스와 마우스 잠금 → 채팅·처형 등 행동 잠금 |
| 총이 안 나감 | 실제 선택 슬롯의 데이터 → 장탄·장전 상태 → 총구 high-ready 여부 → Combat/Fire 입력 |
| 벽 근처에서 총이 올라감 | 정상적인 총구 안전 장치인지 확인. 모델의 총구 위치·+Z 방향과 Hit Layers 검사 |
| 발사했지만 HP가 안 줄어듦 | `Log Shots`의 hit / result 확인. 엄폐, 사거리, 수신자, 회피, 사망 상태 순서로 추적 |
| 재장전이 안 됨 | 탄창이 이미 가득 찼는지, 예비탄이 있는지, 회피·밀치기·슬롯 전환이 장전을 취소했는지 확인 |
| 설정을 바꿨는데 반영되지 않음 | 수정한 asset이 실제 슬롯에 연결됐는지, 씬·프리팹에 다른 값이 저장됐는지 확인 |
| 효과만 약하거나 없음 | GunFeel의 Master Switches와 해당 무기의 Feel 설정, Feedback의 음량·참조 확인 |

`Log Shots`와 `Draw Rays`는 `WeaponController`의 진단 옵션이다. 빨간 레이는 고체에 명중했다는 뜻이며 피해 성공을 보장하지 않는다. 피해 결과의 `Evaded`는 회피, `Duplicate`는 같은 공격 ID의 중복 수신이다.

변경 후 실행할 검사와 실제 플레이 확인 항목은 [검증 안내](ValidationGuide.md)를 따른다. 자동 검사가 통과해도 조작감·모델 자세·화면 가독성은 실제 씬에서 확인한다.
