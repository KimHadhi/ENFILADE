# 전투 코드 구조

[스크립트 길잡이](../README.md) · [전투 제작 안내](../../../../Docs/Guides/CombatFoundationGuide.md) · [규칙과 확장 계획](../../../../Docs/Guides/WeaponSystemDesign.md) · [검증 안내](../../../../Docs/Guides/ValidationGuide.md)

`Combat`에는 플레이어와 적이 공유하는 피해·판정·무기 상태·전투 효과를 둔다. 플레이어 입력과 행동은 `Player/Combat`, 적 체력과 AI는 `Enemies`에서 연결한다.

## 폴더별 책임

| 위치 | 맡는 일 | 시작할 파일 |
| --- | --- | --- |
| `Damage/` | 공격 정보, 피해 수신 계약, 체간·기절·넘어짐, 피해 이벤트와 공통 피드백 | [DamageHit](Damage/DamageHit.cs), [MeleeCombatTarget](Damage/MeleeCombatTarget.cs), [CombatDamageEvents](Damage/CombatDamageEvents.cs) |
| `HitDetection/` | 총기 레이·벽 차단, 벽을 무시하는 근접 스윕·부위 판정 | [AttackTrace](HitDetection/AttackTrace.cs), [MeleeAttackSweep](HitDetection/MeleeAttackSweep.cs), [ShoveSweep](HitDetection/ShoveSweep.cs) |
| `Weapons/Data/` | 무기 설정의 형식. 숫자는 `Tables/Weapons.csv`가 덮어씀 | [WeaponData](../Weapons/Core/WeaponData.cs) |
| `Weapons/Core/` | 무기 한 자루의 탄약·발사·장전, 총구 조준 계산과 반동 스프링 | [WeaponRuntime](../Weapons/Core/WeaponRuntime.cs), [WeaponAimSolver](../Weapons/Core/WeaponAimSolver.cs), [RecoilSpring](../Weapons/Core/RecoilSpring.cs) |
| `Weapons/Effect/` | 총알 궤적(총구 오브젝트), 총구 화염(`MuzzleFlash`)과 조명(`MuzzleLight`, 둘 다 WeaponController가 붙임), 탄착·혈흔(Player의 `Weapon Impact Effects`). 각자 `ShotFired`를 구독 | [MuzzleEffects](../Weapons/Effects/MuzzleEffects.cs), [WeaponImpactEffects](../Weapons/Effects/WeaponImpactEffects.cs), [EnemyBloodEffect](../Weapons/Effects/EnemyBloodEffect.cs) |
| `Feel/` | 화면 킥·총성·히트마커·혈흔·점수·처치 효과, 총알 피격 반응 | [WeaponFeedback](../Weapons/Effects/WeaponFeedback.cs), [GunFeelDirector](Feedback/GunFeelDirector.cs), [EnemyHitReaction](Feedback/EnemyHitReaction.cs) |
| `Waves/` | 웨이브 생성·처치 집계·진행 | [WaveManager](../Modes/Waves/WaveManager.cs) |
| `../Player/Combat/` | 플레이어 입력, 슬롯, 공격, 방어·반격·처형·슬라이드 접촉 | [PlayerWeaponSlots](../Weapons/PlayerWeaponSlots.cs), [PlayerMeleeCombat](../Melee/PlayerMeleeCombat.cs), [SlideCombat](../Melee/SlideCombat.cs) |
| `../Enemies/` | 실제 적 HP·사망·추적·공격 | [EnemyHealth](../Enemies/EnemyHealth.cs), [EnemyCombatAI](../Enemies/EnemyCombatAI.cs) |
| `../UI/` | 탄약·슬롯·체력·체간·처형 안내 | [AmmoHUD](../UI/Hud/Weapons/AmmoHUD.cs), [EnemyVitalsHUD](../UI/Hud/Damage/EnemyVitalsHUD.cs), [ExecutionHUD](../UI/Hud/Prompts/ExecutionHUD.cs) |
| `../Debug/Combat/` | 연습 표적·진단 HUD·시험용 소환 | [WeaponTrainingTarget](../Dev/WeaponTrainingTarget.cs), [ConsoleEnemySpawner](../Dev/ConsoleEnemySpawner.cs) |

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
5. **몸 이동과 화면 표현**: 실제 이동은 이동 서비스가 담당한다. 총 모델 표현(FPSBody, KINEMATION FPS Animation Framework)과 `CameraViewPunch`의 시각 보간을 히트스캔·벽 차단에 섞지 않는다.
6. **시간 효과**: 히트스톱·반격 슬로모션은 [CombatTimeEffects](../Melee/CombatTimeEffects.cs)로 요청·해제한다. 일시정지나 다른 효과의 시간 배율을 덮어쓰지 않는다.
7. **무기 상태와 설정**: `WeaponData`는 공유 설정, `WeaponRuntime`은 개별 무기 상태다. 현재 장탄수·예비탄을 asset에 저장하지 않는다.

살아 있는 적이 넘어지는 [EnemyKnockdownPresentation](Damage/EnemyKnockdownPresentation.cs)은 실제 Rigidbody·뼈대 레그돌과 복구를 처리한다. 처치 연출의 [CorpseTopple](Corpses/CorpseTopple.cs)은 제거된 적을 대신하는 충돌체 없는 시각 사본이다.

## 데이터와 도구 위치

실행 중 자동 생성하는 전투 피드백은 전용 `EFFECTS` 분류로 모은다.

| 분류 | 오브젝트와 역할 |
| --- | --- |
| `EFFECTS → Audio` | `Feel Audio`(총성·장전·확인음), `Combat Hit Audio`(피격·처치음) |
| `EFFECTS → World` | `Weapon Feel FX`(탄피), `Impact VFX`(혈액·먼지·바닥 혈흔), `Parry VFX`(패링 불꽃·섬광), 처치 후 사라지는 시체 연출 |
| `EFFECTS → Screen` | `Damage Popups`(피해 숫자), `Hit Markers`(명중 표시), `Combat Feed`(점수·처치 피드), `Kill Confirm Effect`(처치 화면: 일반 처치는 GTA V식 밝아짐·블룸·옅은 푸른 틴트·약한 색수차 0.5초 유지 후 0.25초 페이드, 처형·반격은 특수 펄스) |
| `ENEMIES → Console` / `Waves` | 콘솔에서 소환한 적 / 웨이브가 관리하는 적 |

공통 분류는 `RuntimeSceneHierarchy.GetGroup`/`Group`, 효과 생성은 `FeelAssets.CreateRuntimeRoot`/`GroupRuntimeRoot`를 사용한다. 분류가 없는 씬에는 장식형 제목으로 필요한 분류를 새로 만든다. 무기 효과와 적은 소유 무기·스포너의 씬에 생성하며 월드 좌표·크기를 유지한다. Screen 폴더에는 Canvas가 없어 UI 각각의 Overlay Canvas, 표시 순서와 기준 해상도가 유지된다. 콘솔 적과 웨이브 적이 같은 상위 분류를 써도 웨이브의 집계·일시정지·정리는 기존 추적 목록만 대상으로 한다.

`Impact VFX → Blood Decals (Pool)`에는 최대 32개 혈흔을 재사용하는 풀이 있다. 수명(`GunFeel → Blood Decal Seconds`)과 1.2초 페이드가 끝나면 비활성화하고, 다음 타격에서 같은 오브젝트를 다시 사용한다. 32개를 넘으면 오래된 슬롯을 덮어쓰므로 무한히 증가하지 않는다. 반복 생성·삭제 비용을 줄이는 기존 동작을 유지하며, 비활성 혈흔도 이 폴더 안에 보관한다.

| 목적 | 위치 |
| --- | --- |
| 무기·적·플레이어·웨이브 밸런스 숫자 | [Data/Resources/Tables](../../Data/Resources/Tables) ([사용법](../../../../Docs/Guides/MaintenanceGuide.md#밸런스-표-data-tables)) |
| 무기 에셋(아이콘·사운드·반동 연출) | [Data/Guns/Weapons](../../Data/Guns/Weapons) |
| 방어·반격·체간·피해 UI 설정 | [Data/Resources/Combat/Feedback.asset](../../Data/Resources/Combat/Feedback.asset) |
| 총기 타격감·점수·피격 반응 설정 | [Data/Resources/Combat/GunFeel.asset](../../Data/Resources/Combat/GunFeel.asset) |
| 물리 프랍 만들기 | [PhysicsPropBuilder](../../Editor/Models/PhysicsPropBuilder.cs) (`ENFILADE → 모델`) |

설정 로딩 키는 `Combat/Feedback`, `Combat/GunFeel`, 표는 `Tables/<이름>`이다. 에셋 경로를 정리할 때도 `Resources` 기준 키를 유지한다. 바꾼 뒤 확인할 항목은 [검증 안내](../../../../Docs/Guides/ValidationGuide.md)를 따른다.

## 폴더

| 폴더 | 스크립트 | 무엇 |
| --- | ---: | --- |
| [Corpses](Corpses/) | 4 | 시체 인형 |
| [Damage](Damage/) | 9 | 피해 규약과 받는 쪽 |
| [Feedback](Feedback/) | 21 | 맞았을 때의 표시·소리·화면 흔들림 |
| [HitDetection](HitDetection/) | 6 | 레이·스윕 판정, 관통 |
| [Impacts](Impacts/) | 6 | 탄착 효과와 총알 자국 |
| [Marks](Marks/) | 3 | 벽 너머로 보이는 표식 |
| [Moments](Moments/) | 10 | 특수 순간 효과, 슬로 모션 |

## 무엇을 고치려면 어디를

| 변경할 내용 | 시작할 파일·설정 |
| --- | --- |
| 특수 순간 VFX (궁극기·현상금·결투·원호 방어·되치기·건 카타·카드. 사용자가 11-12 시안에서 순간마다 고른 대로. 규칙: 조준점 가운데는 비우고 화면 반전·하얗게 하기 없음, 한 순간에 주인공 하나, 1초 안에 끝, 클래스마다 제 색과 문장, 남이 보는 세계와 내가 보는 세계는 같다. 궁극기를 쓰면 모든 PC 왼쪽 아래(아군 카드 오른쪽)에 누가 썼는지 기운 띠(클래스마다 띠 끝 모양과 기울기가 다르고, 칸에 클래스 문장이 그려져 찍힘)와 발밑 클래스 문장. 나이트 분신은 내 캐릭터 그대로 보랏빛 검은색 잔상, 포인트는 두 총(손·등)이 총구부터 달아오르고 불티, 고스트는 흰 호랑이 외곽선, 데탕트는 흰 십자가 바닥으로 퍼짐, 어썰트는 금빛 쐐기 판. 데스페라도 현상금은 머리에 겹 마름모·봉인 고리·몸 둘레로 터지는 빛·하늘 선, 보통 현상금은 CLAIMED 판. CHARGE는 버프 받은 모두(내 팔도) 노랗게 빛나고 바닥 고리, 결투는 빛이 흐르는 두 겹 줄과 상대 괄호, 원호 방어는 회피 같은 내 캐릭터 잔상과 막은 자리의 튕김(COVER 배너는 그대로), 되치기는 분홍 호, 카타는 꺼낼 때마다 주황 호, 카드 발톱은 휘어진 상처 셋·건틀렛 섬광은 별과 범위 부채꼴(화면 안 하얘짐)·포탄은 과녁 문장. 작은 이름표 CHARGE·DUEL·REVERSAL·GUN KATA는 클래스 칸 위. 소리는 클래스마다 합성음, Resources/Audio/Moments/<키>로 바꿈. COUNTER 배너는 이제 COUNTER와 COVER만) | [MomentLook](Moments/MomentLook.cs), [MomentFx](Moments/MomentFx.cs), [FxShapes](Feedback/FxShapes.cs), [MomentHUD](../UI/Hud/Classes/MomentHUD.cs)·[BandGraphic](../UI/Hud/Classes/BandGraphic.cs)·[SigilGraphic](../UI/Hud/Classes/SigilGraphic.cs), [BodyBake](Corpses/BodyBake.cs)(BodyShell 빛 껍질 포함), [BodyDouble](../Classes/Knight/BodyDouble.cs), [GunHeat](../Classes/Point/GunHeat.cs), [MomentSound](Moments/MomentSound.cs), 셰이더 [MomentGlow](../../Art/Resources/Combat/Shaders/MomentGlow.shader)·[GunHeat](../../Art/Resources/Combat/Shaders/GunHeat.shader), CardFx·CardNet(보낸 사람 From), [MomentFx.csv](../../Data/Resources/Tables/Hud/MomentFx.csv), [사용법](../../../../Docs/Guides/MaintenanceGuide.md#특수-순간-vfx-momentfxcsv) |
| 산 팩 효과 (Realistic Gunslinger 팩을 게임 순간에 쓴다. 사용자 11-13: "Realistic Gunslingers VFX에 시순트용 이펙트 꽤 있는데?". 결투에서 이기면 상대 시체에서 이마에 총알 구멍이 난 해골이 떠오른다(Mark Debuff). 데스페라도를 쏘면 총구 고리, 날아가는 탄두, 맞은 자리 폭발이 나오고(Piercing Shot Muzzle·Heavy Shot·Piercing Hit), 데스페라도 현상금은 머리에서 터진다. 현상금 표식이 붙은 적의 머리 위에 작은 청록 해골(Mark Debuff), 브로큰 애로우 포탄마다 집속탄 폭발(Cluster Granades), 건 카타 동안 몸 둘레를 도는 금빛 탄(Bullet buff). 다리가 부러진 거구가 쓰러져 바닥에 닿을 때와 거구가 바리케이드 판자를 칠 때는 짧은 충격파와 돌 조각이 튄다(Big Pointblank를 작게, `bruteFall`·`bruteSmash`). 다른 총도 갈래마다(PackShots.ClassOf: Weapons.csv 숫자로 저격·지정사수는 Marksman = Piercing Shot, 권총·기관단총은 Light, 산탄총은 Shotgun = 한 발에 한 번 뿌리는 알갱이, 소총·기관총은 그대로) 총구·탄·맞은 자리가 총의 원래 불꽃과 궤적 대신 나온다(겹치지 않게, 소음기면 총구는 없음, 적 하나당 맞은 자리 한 번). 새 총도 저절로 갈래를 탄다. 자주 쏘는 쓰임은 복사본을 다시 쓴다(`pool`). 모든 PC에 같게. 프리팹은 그대로 두고 복사본만 표대로 줄인다: 크기, 빠르기, 화면을 덮는 검은 퍼짐과 큰 연기 빼기, 주인공 바로 터뜨리기. 수치 Tables/PackFx.csv) | [PackFx](Moments/PackFx.cs), [PackShots](../Weapons/Effects/PackShots.cs), [BountySkulls](../Classes/Hound/BountySkulls.cs), [PackFxLibrary](Moments/PackFxLibrary.cs)(Data/Resources/Combat/PackFxLibrary.asset) |
| 적의 넘어짐·레그돌·복구 | [EnemyKnockdownPresentation](Damage/EnemyKnockdownPresentation.cs) |
| 쓰러진 동료 표시·동료 체력 (좀비에게 잡히거나 깔리거나 뜯어먹히는 동료도 벽 너머로 분홍빛으로 빠르게 깜빡인다: DownedAura.csv `heldColor`·`heldBeats`, 상태는 `PlayerNetworkCombat` Stance.Held → `PlayerMovement.InZombieGrip`) | [DownedAura](Marks/DownedAura.cs), [TeammateMarkerHUD](../UI/Hud/Team/TeammateMarkerHUD.cs), [TeamHealthPanel](../UI/Hud/Team/TeamHealthPanel.cs), [HealthSegments](../UI/Hud/Health/HealthSegments.cs), [DownedAura.csv](../../Data/Resources/Tables/Player/DownedAura.csv) |
| 피해 공통 규약·중복 타격 / 레이 판정 / 총알 관통과 도탄 (ArcCW) | [DamageHit](Damage/DamageHit.cs), [AttackTrace](HitDetection/AttackTrace.cs) / [BulletPenetration](HitDetection/BulletPenetration.cs), `AttackTrace.Bullet`, [Weapons.csv](../../Data/Resources/Tables/Weapons/Weapons.csv)·[Surfaces.csv](../../Data/Resources/Tables/Maps/Surfaces.csv)의 `penetration`, 총마다 몸을 통째로 뚫는 규칙 `pierceBodies`·`pierceKeep`(BulletPierce: 데스페라도의 피어싱 탄) |
| 셤블러 잡기 (G&B 레슬: 셤블러는 멀어지는 중이 아니면 할퀴는 대신 잡음, 패링은 안 되고 회피만, 잡히면 5초 동안 못 움직이고 총도 못 씀, 밀치기·주먹으로 떼어냄, 그 셤블러가 누구에게든 맞거나 기절하면 풀림, 2마리 이상이면 마리당 초당 10씩 물고 4마리면 즉사. 엎드리거나 누워 있으면 어떤 좀비든 무릎 꿇고 뜯어먹음: 같은 방식으로 붙잡고 처음부터 묾. 누운·쓰러진 플레이어는 낮게 노려서 맞힘. 잡기와 뜯어먹기는 서로 발 높이 차가 0.6m 안일 때만 하고 턱 위·아래의 플레이어는 할퀴기만 함(Enemies.csv `holdHeight`). 무엇이든 플레이어를 잡고 있는 좀비는 모든 PC에서 외곽선이 뜨고 가려진 곳에서는 벽 너머 실루엣으로 두근거림: [HoldReveal](Marks/HoldReveal.cs), [HoldReveal.csv](../../Data/Resources/Tables/Player/HoldReveal.csv)) | [PlayerHold](../Player/Life/PlayerHold.cs) (잡히거나 깔린 플레이어 자기 화면의 반응: 가장자리가 붉게 조여 오고, 무엇에 잡혔는지와 벗어나는 법 안내, 깔림 막대, 잡기 소리 [HoldFeedback](../Player/Life/HoldFeedback.cs), Player.csv `HoldFeedback.*`. 잡은 좀비의 외곽선 `HitFlashRenderer.SetHolder`(Feedback의 Holder 항목)는 이제 HoldReveal이 모든 PC에서 켠다. 엎드리거나 누운 플레이어를 뜯어먹으려 무릎 꿇은 좀비는 그사이 0.8m까지 기어간 플레이어도 잡는다: EnemyCombatAI의 feedReach), [ZombieGrip](../Enemies/ZombieGrip.cs), `EnemyCombatAI` AttackMove.Grab, `PlayerMovement.IsHeld`, `PlayerNetworkCombat.ShareHold`, Player.csv `Hold.*`, [ZombieKinds.csv](../../Data/Resources/Tables/Enemies/ZombieKinds.csv)의 `grabs` |
| 총성·피격·히트마커·처치 피드백 / 후처리 빌려 쓰기 | [Combat/Feel](Feedback/), [PostProcessLoan](Moments/PostProcessLoan.cs), [GunFeel.asset](../../Data/Resources/Combat/GunFeel.asset), 무기 데이터의 `Feel` |
| 총소리 고르기(총에 넣은 클립 → Guns 묶음 → 합성음) / 쏜 자리의 방에 맞춘 총 잔향 (Accurate Gun Reverb) | [FeelAudio](Feedback/FeelAudio.cs), [Art/Audio/Guns](../../Art/Audio/Guns) / [GunReverb](../Audio/GunReverb.cs) ([Reverb.csv](../../Data/Resources/Tables/Audio/Reverb.csv)) |
| 매 피격 혈흔의 모양 고르기(코드로 만든 얼룩 + Cry of Fear 혈흔 9장, 같은 모양을 연달아 찍지 않음) / 고어 투영 데칼(GoreLibrary의 Splats 전부에서 고름) | [BloodSplatTextures](Impacts/BloodSplatTextures.cs) (`ImpactVfx.Splat`이 부름, 텍스처 Resources/Combat/BloodSplats) / [GoreFx](../Enemies/Gore/GoreFx.cs) |
