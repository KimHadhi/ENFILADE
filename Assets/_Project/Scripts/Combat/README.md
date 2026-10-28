# 전투 코드 구조

[스크립트 길잡이](../README.md) · [전투 제작 안내](../../Docs/CombatFoundationGuide.md) · [규칙과 확장 계획](../../Docs/WeaponSystemDesign.md) · [검증 안내](../../Docs/ValidationGuide.md)

`Combat`에는 플레이어와 적이 공유하는 피해·판정·무기 상태·전투 효과를 둔다. 플레이어 입력과 행동은 `Player/Combat`, 적 체력과 AI는 `Enemies`에서 연결한다.

## 폴더별 책임

| 위치 | 맡는 일 | 시작할 파일 |
| --- | --- | --- |
| `Damage/` | 공격 정보, 피해 수신 계약, 체간·기절·넘어짐, 피해 이벤트와 공통 피드백 | [DamageHit](Damage/DamageHit.cs), [MeleeCombatTarget](Damage/MeleeCombatTarget.cs), [CombatDamageEvents](Damage/CombatDamageEvents.cs) |
| `HitDetection/` | 총기 레이·벽 차단, 벽을 무시하는 근접 스윕·부위 판정 | [AttackTrace](HitDetection/AttackTrace.cs), [MeleeAttackSweep](HitDetection/MeleeAttackSweep.cs), [ShoveSweep](HitDetection/ShoveSweep.cs) |
| `Weapons/Data/` | 무기 설정의 형식. 숫자는 `Tables/Weapons.csv`가 덮어씀 | [WeaponData](Weapons/Data/WeaponData.cs) |
| `Weapons/Core/` | 무기 한 자루의 탄약·발사·장전, 총구 조준 계산과 반동 스프링 | [WeaponRuntime](Weapons/Core/WeaponRuntime.cs), [WeaponAimSolver](Weapons/Core/WeaponAimSolver.cs), [RecoilSpring](Weapons/Core/RecoilSpring.cs) |
| `Weapons/Effect/` | 프리팹 기반 총구 화염·궤적(총구 오브젝트), 탄착·혈흔(Player의 `Weapon Impact Effects`). 각자 `ShotFired`를 구독 | [MuzzleEffects](Weapons/Effect/MuzzleEffects.cs), [WeaponImpactEffects](Weapons/Effect/WeaponImpactEffects.cs), [EnemyBloodEffect](Weapons/Effect/EnemyBloodEffect.cs) |
| `Feel/` | 화면 킥·총성·히트마커·혈흔·점수·처치 효과, 총알 피격 반응 | [WeaponFeedback](Feel/WeaponFeedback.cs), [GunFeelDirector](Feel/GunFeelDirector.cs), [EnemyHitReaction](Feel/EnemyHitReaction.cs) |
| `Waves/` | 웨이브 생성·처치 집계·진행 | [WaveManager](Waves/WaveManager.cs) |
| `../Player/Combat/` | 플레이어 입력, 슬롯, 공격, 방어·반격·처형·슬라이드 접촉 | [PlayerWeaponSlots](../Player/Combat/Weapons/PlayerWeaponSlots.cs), [PlayerMeleeCombat](../Player/Combat/PlayerMeleeCombat.cs), [SlideCombat](../Player/Combat/SlideCombat.cs) |
| `../Enemies/` | 실제 적 HP·사망·추적·공격 | [EnemyHealth](../Enemies/EnemyHealth.cs), [EnemyCombatAI](../Enemies/EnemyCombatAI.cs) |
| `../UI/` | 탄약·슬롯·체력·체간·처형 안내 | [AmmoHUD](../UI/AmmoHUD.cs), [EnemyVitalsHUD](../UI/EnemyVitalsHUD.cs), [ExecutionHUD](../UI/ExecutionHUD.cs) |
| `../Debug/Combat/` | 연습 표적·진단 HUD·시험용 소환 | [WeaponTrainingTarget](../Debug/Combat/WeaponTrainingTarget.cs), [ConsoleEnemySpawner](../Debug/Combat/ConsoleEnemySpawner.cs) |

`Feel`의 대부분은 표시·소리지만 `EnemyHitReaction`은 적 이동과 공격을 바꾸는 게임 규칙이다. 이 컴포넌트를 화면 효과로만 취급해 제거하지 않는다.

## 발사에서 피격까지

```text
PlayerWeaponInput → WeaponController
                       ├─ WeaponRuntime: 발사 허용·탄약·장전
                       ├─ WeaponAimSolver / AttackTrace: 총구·엄폐·명중
                       │    └─ IDamageReceiver.ReceiveHit
                       │         ├─ HP·사망 / 적 피격 반응
                       │         └─ CombatDamageEvents → UI·소리·GunFeelDirector
                       └─ ShotFired 등 이벤트 → 모델·총구·총성 효과
```

슬롯은 `PlayerWeaponSlots`가 `WeaponRuntime`을 보관하고, 선택할 때 `WeaponController.Equip`으로 연결한다. 컨트롤러가 탄약·발사 결과를 결정하며 HUD와 애니메이션은 결과를 표시한다.

## 확장할 때 유지할 계약

1. **공격 ID**: 총알 한 발·근접 공격 한 번마다 새 ID를 만들고, 같은 공격의 모든 히트박스·스윕 프레임은 ID를 공유한다. 대상 수 제한은 공격의 적중 목록에서, 수신 중복은 피해 수신자에서 처리한다.
2. **피해 정보**: `Source`, `Method`, `IsCritical`, `IsCounter`, `IsComboFinisher`, `CanBeGuarded`를 보존한다. 방어·마지막 타격 패링·점수·소리는 이 정보에 의존한다. `IsCritical`은 이미 배율이 적용된 피해를 표시하는 정보이며 추가 피해 배율을 뜻하지 않는다.
3. **사망 순서**: HP 감소와 피해 보고를 먼저 처리한 뒤 기존 `Died` 경로로 사망시킨다. 효과에서 적을 직접 제거하면 웨이브 집계와 처형 처리가 어긋난다.
4. **적 행동 잠금**: 새 AI는 기절·흐트러짐·넘어짐과 `PlayerExecution.Suppresses`를 확인한다. 이동·공격 준비·NavMesh 복구가 각각 이 상태를 존중해야 한다.
5. **몸 이동과 화면 표현**: 실제 이동은 이동 서비스가 담당한다. `WeaponPresentation`과 `CameraViewPunch`의 시각 보간을 히트스캔·벽 차단에 섞지 않는다.
6. **시간 효과**: 히트스톱·반격 슬로모션은 [CombatTimeEffects](../Player/Combat/CombatTimeEffects.cs)로 요청·해제한다. 일시정지나 다른 효과의 시간 배율을 덮어쓰지 않는다.
7. **무기 상태와 설정**: `WeaponData`는 공유 설정, `WeaponRuntime`은 개별 무기 상태다. 현재 장탄수·예비탄을 asset에 저장하지 않는다.

살아 있는 적이 넘어지는 [EnemyKnockdownPresentation](Damage/EnemyKnockdownPresentation.cs)은 실제 Rigidbody·뼈대 레그돌과 복구를 처리한다. 처치 연출의 [CorpseTopple](Feel/CorpseTopple.cs)은 제거된 적을 대신하는 충돌체 없는 시각 사본이다.

## 데이터와 도구 위치

실행 중 자동 생성하는 전투 피드백은 전용 `EFFECTS` 분류로 모은다.

| 분류 | 오브젝트와 역할 |
| --- | --- |
| `EFFECTS → Audio` | `Feel Audio`(총성·장전·확인음), `Combat Hit Audio`(피격·처치음) |
| `EFFECTS → World` | `Weapon Feel FX`(총구 연기·불꽃·탄피), `Impact VFX`(혈액·먼지·바닥 혈흔), 처치 후 사라지는 시체 연출 |
| `EFFECTS → Screen` | `Damage Popups`(피해 숫자), `Hit Markers`(명중 표시), `Combat Feed`(점수·처치 피드), `Kill Confirm Effect`(처치 화면: 일반 처치는 GTA V식 밝아짐·블룸·옅은 푸른 틴트·약한 색수차 0.5초 유지 후 0.25초 페이드, 처형·반격은 특수 펄스) |
| `ENEMIES → Console` / `Waves` | 콘솔에서 소환한 적 / 웨이브가 관리하는 적 |

공통 분류는 `RuntimeSceneHierarchy.GetGroup`/`Group`, 효과 생성은 `FeelAssets.CreateRuntimeRoot`/`GroupRuntimeRoot`를 사용한다. 분류가 없는 씬에는 장식형 제목으로 필요한 분류를 새로 만든다. 무기 효과와 적은 소유 무기·스포너의 씬에 생성하며 월드 좌표·크기를 유지한다. Screen 폴더에는 Canvas가 없어 UI 각각의 Overlay Canvas, 표시 순서와 기준 해상도가 유지된다. 콘솔 적과 웨이브 적이 같은 상위 분류를 써도 웨이브의 집계·일시정지·정리는 기존 추적 목록만 대상으로 한다.

`Impact VFX → Blood Decals (Pool)`에는 최대 32개 혈흔을 재사용하는 풀이 있다. 수명(`GunFeel → Blood Decal Seconds`)과 1.2초 페이드가 끝나면 비활성화하고, 다음 타격에서 같은 오브젝트를 다시 사용한다. 32개를 넘으면 오래된 슬롯을 덮어쓰므로 무한히 증가하지 않는다. 반복 생성·삭제 비용을 줄이는 기존 동작을 유지하며, 비활성 혈흔도 이 폴더 안에 보관한다.

| 목적 | 위치 |
| --- | --- |
| 무기·적·플레이어·웨이브 밸런스 숫자 | [Resources/Tables](../../Resources/Tables) ([사용법](../../Docs/MaintenanceGuide.md#밸런스-표-data-tables)) |
| 무기 에셋(아이콘·사운드·반동 연출) | [Data/Combat/Weapons](../../Data/Combat/Weapons) |
| 방어·반격·체간·피해 UI 설정 | [Resources/Combat/Feedback.asset](../../Resources/Combat/Feedback.asset) |
| 총기 타격감·점수·피격 반응 설정 | [Resources/Combat/GunFeel.asset](../../Resources/Combat/GunFeel.asset) |
| 물리 프랍 만들기 | [PhysicsPropBuilder](../../Editor/Import/PhysicsPropBuilder.cs) (`Tools → Models`) |

설정 로딩 키는 `Combat/Feedback`, `Combat/GunFeel`, 표는 `Tables/<이름>`이다. 에셋 경로를 정리할 때도 `Resources` 기준 키를 유지한다. 바꾼 뒤 확인할 항목은 [검증 안내](../../Docs/ValidationGuide.md)를 따른다.
