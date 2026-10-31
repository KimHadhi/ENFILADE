# 스크립트 길잡이

수정하려는 기능의 담당 파일부터 찾는다. 프로젝트 실행과 에셋 위치는 [프로젝트 안내](../../../README.md), 조작법은 [이동 안내](../Docs/MovementGuide.md), 검사 실행은 [검증 안내](../Docs/ValidationGuide.md)에 있다.

## 어디를 수정할까?

| 변경할 내용 | 시작할 파일·설정 |
| --- | --- |
| **체력·무기·주먹·처형·적·웨이브·이동·몸·소리 숫자** | [Resources/Tables](../Resources/Tables)의 CSV 표 (컴파일 불필요, [사용법](../Docs/MaintenanceGuide.md#밸런스-표-data-tables)) |
| 표 읽기·적용 방식 | [GameTables](Data/GameTables.cs), [CsvTable](Data/CsvTable.cs), [TableFieldWriter](Data/TableFieldWriter.cs) |
| 걷기·달리기·점프·자세·슬라이드·다이브 수치 | [Player.csv](../Resources/Tables/Player.csv)의 `Move`·`Posture` 행, 필드 선언은 [Configuration](Player/Movement/PlayerMovement.Configuration.cs) |
| 회피·파쿠르·착지·저스트 슬라이드·밀치기·사다리·집라인·평균대 수치 | `Player.csv`의 `Ability` 행, 필드 선언은 [MovementAbilitySettings](Player/Movement/MovementAbilitySettings.cs) |
| 이동 상태 전환·처리 순서 | [PlayerMovement](Player/Movement/PlayerMovement.cs), [이동 코드 구조](Player/Movement/README.md) |
| 접지·경사·중력·실제 몸 이동 | [MovementMotor](Player/Movement/Core/MovementMotor.cs) |
| 자세 높이·천장·환풍구·눈높이 | [MovementPostureController](Player/Movement/Core/MovementPostureController.cs) |
| 달리며 쌓이는 속도 | [MovementMomentum](Player/Movement/Actions/MovementMomentum.cs) |
| 착지 단계·구르기·큰 낙하·넉백 | [MovementLanding](Player/Movement/Actions/MovementLanding.cs), [HeavyLanding 상태](Player/Movement/States/) |
| 스텝 회피·기울이기·적 통과 | [Dodge](Player/Movement/Dodge/), [MovementAbilities](Player/Movement/Actions/MovementAbilities.cs) |
| Vault·맨틀·렛지·벽·봉·사다리·집라인·평균대 | [Parkour](Player/Movement/Parkour/), [States/Parkour](Player/Movement/States/Parkour/) |
| 씬에 놓는 사다리·집라인·평균대 | [Ladder](World/Ladder.cs), [Zipline](World/Zipline.cs), [BalanceBeam](World/BalanceBeam.cs), [사용 안내](../Docs/MovementGuide.md#사다리집라인평균대) |
| 슬라이딩의 적 접촉·저스트 판정 | [SlideCombat](Player/Combat/SlideCombat.cs) |
| 적의 넘어짐·레그돌·복구 | [EnemyKnockdownPresentation](Combat/Damage/EnemyKnockdownPresentation.cs) |
| 밀치기·돌진 밀치기 | [PlayerCombatActions](Player/Combat/PlayerCombatActions.cs) |
| 주먹·가드·패링·가드 브레이크 | [PlayerMeleeCombat](Player/Combat/PlayerMeleeCombat.cs) |
| 완벽 방어·반격 / 처형 | [PlayerPerfectDefense](Player/Combat/PlayerPerfectDefense.cs), [PlayerExecution](Player/Combat/PlayerExecution.cs) |
| 보이는 몸(진짜 1인칭)·상태별 애니메이션·F5 뒤에서 보기 | [PlayerBody](Player/Body/PlayerBody.cs), [PlayerBodyProfile](Player/Body/PlayerBodyProfile.cs), [사용 안내](../Docs/PlayerBodyGuide.md) |
| 눈 시점·시선 따라 허리·머리 굽히기·카메라 벽 판정 | [PlayerBody.Look](Player/Body/PlayerBody.Look.cs), [PlayerBody.Clearance](Player/Body/PlayerBody.Clearance.cs), [CameraClearance](Player/Camera/CameraClearance.cs) |
| 손으로 총·주먹·난간 잡기 / 발 IK·발목 제한 | [PlayerBodyHands](Player/Body/PlayerBodyHands.cs), [ArmIK](Player/Body/ArmIK.cs) / [PlayerBody.Feet](Player/Body/PlayerBody.Feet.cs), [AnkleLimit](Player/Body/AnkleLimit.cs) |
| 플레이어 HP·실드·피격·사망 | [PlayerDamageReceiver](Player/Combat/PlayerDamageReceiver.cs) |
| 플레이어 궁극기 최대 보유량·소비량 스탯 / 현재 충전량 | [PlayerStats](Player/PlayerStats.cs), [UltimateCharge](UI/HP/UltimateCharge.cs) (Play 시작 때 `PlayerMovement`가 붙임) |
| 피격 방향 아크·혈흔·쉐이크·빈사 박동 | [PlayerDamageFeedback](Player/Combat/PlayerDamageFeedback.cs), [설정](../Resources/Combat/PlayerDamageFeedback.asset), [사용 안내](../Docs/PlayerDamageFeedbackGuide.md) |
| 무기별 연출·사운드·반동 | [Data/Combat/Weapons](../Data/Combat/Weapons/), 형식은 [WeaponData](Combat/Weapons/Data/WeaponData.cs) |
| 총구 화염·궤적 / 탄착·혈흔 프리팹 효과 | [MuzzleEffects](Combat/Weapons/Effect/MuzzleEffects.cs), [WeaponImpactEffects](Combat/Weapons/Effect/WeaponImpactEffects.cs), [EnemyBloodEffect](Combat/Weapons/Effect/EnemyBloodEffect.cs) |
| 발사 간격·탄약·장전 타이머 | [WeaponRuntime](Combat/Weapons/Core/WeaponRuntime.cs) |
| 발사·명중·조준 연결 / 총 모델 표현 | [WeaponController](Player/Combat/Weapons/WeaponController.cs), [WeaponPresentation](Player/Combat/Weapons/WeaponPresentation.cs) |
| 장비 슬롯 / 사격 입력 | [PlayerWeaponSlots](Player/Combat/Weapons/PlayerWeaponSlots.cs), [PlayerWeaponInput](Player/Combat/Input/PlayerWeaponInput.cs) |
| 피해 공통 규약·중복 타격 / 레이 판정 | [DamageHit](Combat/Damage/DamageHit.cs), [AttackTrace](Combat/HitDetection/AttackTrace.cs), [AttackHitbox](Combat/HitDetection/AttackHitbox.cs) |
| 적 체력·추적·공격 / 웨이브 | [Enemies](Enemies/), [WaveManager](Combat/Waves/WaveManager.cs) |
| 총성·피격·히트마커·처치 피드백 | [Combat/Feel](Combat/Feel/), [GunFeel.asset](../Resources/Combat/GunFeel.asset), 무기 데이터의 `Feel` |
| 완벽 방어·흐트러짐·처형의 표시·사운드 | [Feedback.asset](../Resources/Combat/Feedback.asset) |
| 시점 입력 / 이동 카메라 효과 / 시점 펀치 / 큰 낙하 화면 | [PlayerLook](Player/Camera/PlayerLook.cs), [PlayerCameraEffects](Player/Camera/PlayerCameraEffects.cs), [PlayerMovementPunch](Player/Camera/PlayerMovementPunch.cs), [PlayerFallScreen](Player/Camera/PlayerFallScreen.cs) |
| 탄약·무기 슬롯·적 체력 UI | [AmmoHUD](UI/AmmoHUD.cs), [WeaponSlotsHUD](UI/WeaponSlotsHUD.cs), [EnemyVitalsHUD](UI/EnemyVitalsHUD.cs) |
| 평균대 판정 원 | [BalanceGaugeHUD](UI/BalanceGaugeHUD.cs) |
| 체력·궁극기 UI / 목표·웨이브 UI | [UI/HP](UI/HP/), [MissionHUD](UI/MissionHUD.cs), [WaveMissionSource](UI/WaveMissionSource.cs) |
| 채팅·테스트 명령 | [UI/Chatting](UI/Chatting/) |
| Esc 설정 메뉴·일시정지 / 저장되는 플레이어 설정 | [UI/Settings](UI/Settings/), [GamePause](Settings/GamePause.cs) / [GameSettings](Settings/GameSettings.cs) |
| 소리 볼륨 채널(전투·이동·목소리·UI) | [GameAudio](Audio/GameAudio.cs) |
| 발소리 / 동작 소리 / 숨·바람·큰 낙하·사다리류 소리 | [PlayerFootsteps](Player/Audio/PlayerFootsteps.cs), [PlayerMovementAudio](Player/Audio/PlayerMovementAudio.cs), [Player/Audio](Player/Audio/) |
| 동작마다 나는 소리의 구성 / 소리 묶음·표면·재생 | [SoundCues.csv](../Resources/Tables/SoundCues.csv) / [SoundBank](Audio/SoundBank.cs), [Surfaces](Audio/Surfaces.cs), [SoundCues](Audio/SoundCues.cs), [CharacterSounds](Audio/CharacterSounds.cs), [사용 안내](../Docs/MaintenanceGuide.md#이동목소리-소리-폴더가-곧-목록) |
| 적이 들을 소음 이벤트 | [Noise](World/Noise.cs) (아직 듣는 적은 없다) |

## 코드의 경계

| 위치 | 책임 |
| --- | --- |
| `Player/Movement` | 입력을 이동 의도로 해석하고 상태·자세·물리 이동을 실행 |
| `Player/Combat` | 플레이어의 공격·방어·장비 선택과 행동 간 취소·잠금 |
| `Player/Camera` | 시점 입력과 이동에 따른 카메라 효과, 시점 펀치, 큰 낙하 화면, 카메라 벽 판정 |
| `Player/Body` | 보이는 몸: 클립 재생, 눈 시점, 손·발 IK. 이동 상태를 읽어 표현한다 |
| `Player/Audio` | 이동 이벤트와 발 접지를 듣고 소리를 낸다 |
| `Combat/Damage`, `HitDetection`, `Weapons` | 플레이어·적이 공유하는 피해 규약, 접촉 판정, 무기 데이터·시간 규칙 |
| `Combat/Feel` | 전투 결과에 대한 표시·소리와 적의 총격 피격 반응 |
| `Enemies`, `Combat/Waves` | 적의 생명주기·AI와 웨이브 진행 |
| `UI` | 게임 상태를 읽어 표시하고 UI 입력을 전달 |
| `World` | 파괴 가능한 문, 스윙 바, 사다리, 집라인, 평균대, 텔레포트, 물리 프랍([PhysicsProp](World/PhysicsProp.cs)), 표면 종류, 소음 등 월드 상호작용 |
| `Data` | 밸런스 CSV 표를 읽어 컴포넌트·에셋 필드에 적용하고, 편집기에서 표 저장 시 다시 적용 |
| `Settings` | 사용자별로 저장되는 설정(`settings.json`)과 일시정지 |
| `Audio` | 소리 볼륨 채널, 소리 묶음·큐·표면, 캐릭터의 몸과 입에서 나는 소리 |
| `Debug` | 연습 표적·공격기·테스트 명령·디버그 HUD |
| `../Editor` | 편집기 전용 도구: 표 다시 읽기, 모델 임포트 규칙·물리 프랍, 소리 묶음·몸 프로필 자동 생성, HUD 편집 미리보기, 폰트 저장 |

이동의 진입점은 `PlayerMovement`다. 프레임마다 입력·상태·물리 서비스를 연결하고, 외부 기능에는 `CurrentState`, `Velocity`, `ResetVelocity()` 같은 API를 제공한다. 내부 상태 전환과 재컴파일 복구는 [이동 코드 구조](Player/Movement/README.md)를 참고한다.

전투는 **입력 → 공격 규칙 → 접촉·피해 → 반응·표시** 순서로 읽으면 된다. 예를 들어 총은 `PlayerWeaponInput → WeaponController / WeaponRuntime → AttackTrace → IDamageReceiver`로 이어진다. 피해를 받은 대상의 처리에서 발생한 전투 이벤트는 `GunFeelDirector`가 표시·소리로 연결한다. 새 무기나 적을 추가하는 순서는 [전투 코드 구조](Combat/README.md)에 있다.

## 플레이어에 붙는 것과 실행 순서

씬의 플레이어에는 이동·시점·입력·무기·슬롯만 둔다. 나머지는 Play 시작 때 `PlayerMovement.EnsureInitialized`가 붙이고 `Player.csv`의 같은 이름 행과 연결한다. 몸은 `Resources/Characters/PlayerBody` 프로필에 캐릭터가 있을 때만 붙는다.

| 행 | 컴포넌트 |
| --- | --- |
| `Body`, `Hands`, `Footsteps` | [PlayerBody](Player/Body/PlayerBody.cs), [PlayerBodyHands](Player/Body/PlayerBodyHands.cs), [PlayerFootsteps](Player/Audio/PlayerFootsteps.cs) |
| `Sounds`, `MovementAudio`, `Breathing`, `Wind`, `FallAudio`, `FixtureAudio` | [CharacterSounds](Audio/CharacterSounds.cs), [PlayerMovementAudio](Player/Audio/PlayerMovementAudio.cs), [PlayerBreathing](Player/Audio/PlayerBreathing.cs), [PlayerWindAudio](Player/Audio/PlayerWindAudio.cs), [PlayerFallAudio](Player/Audio/PlayerFallAudio.cs), [PlayerFixtureAudio](Player/Audio/PlayerFixtureAudio.cs) |
| `Punch`, `FallScreen`, `BalanceGauge` | [PlayerMovementPunch](Player/Camera/PlayerMovementPunch.cs), [PlayerFallScreen](Player/Camera/PlayerFallScreen.cs), [BalanceGaugeHUD](UI/BalanceGaugeHUD.cs) |

한 프레임 안의 순서는 `DefaultExecutionOrder`로 고정되어 있다. 앞 단계의 결과를 뒤 단계가 읽으므로 순서를 바꾸면 카메라·손이 한 프레임 늦는다.

| 순서 | 컴포넌트 | 하는 일 |
| ---: | --- | --- |
| -100 | `PlayerMovement` | 입력·상태·실제 이동 |
| 0 | `PlayerLook` | 시선 |
| 200 | `PlayerCameraEffects` | 흔들림·기울이기와 카메라 기준 위치 |
| 250 | `PlayerBody` | 몸 자세, 발 IK, 눈 시점으로 카메라 최종 위치 확정 |
| 260 | `PlayerFootsteps` | IK가 끝난 발로 접지 판단 |
| 270 | `BalanceGaugeHUD` | 그 프레임의 평균대 판정 표시 |
| 300 | `WeaponController` | 확정된 카메라로 조준·발사 |
| 320 | `PlayerBodyHands` | 확정된 총·주먹 위치로 손 놓기 |

## 입력과 설정

대부분의 키 바인딩은 [Player.inputactions](../Input/Player.inputactions)에 있다. 이동은 `MovementInput`, 사격은 `PlayerWeaponInput`, UI는 해당 UI 컴포넌트가 읽는다. 앉기는 상태 코드에서 키를 직접 읽지 않고 `MovementInput.CrouchHeld`/`CrouchPressed`를 쓴다. 그래야 Esc 메뉴의 "눌러서 전환" 설정이 모든 상태에 적용된다. **슬롯 1~4와 숫자패드 키는 현재 `PlayerWeaponSlots.Update()`에서 직접 읽으므로** 입력 에셋만 바꿔서는 재지정되지 않는다.

`PlayerMovement.Configuration.cs`는 별도 컴포넌트가 아니라 같은 `PlayerMovement` 클래스의 직렬화 필드다. `MovementAbilitySettings`는 그 안의 설정 데이터다. 수치는 `Player.csv`(`Posture`·`Move`·`Ability` 행)가 필드 이름으로 덮어쓰므로, 표에 있는 값은 인스펙터에서 바꿔도 Play 때 표 값으로 돌아간다. 필드 이름을 바꾸면 표의 키도 같이 바꾼다. 컴포넌트 안에 든 설정 객체는 `GameTables.BindNested`로 연결한다.

## 기능을 추가할 때

1. 플레이어 전용 입력·선택은 `Player`, 공통 피해·무기 규칙은 `Combat`에 둔다.
2. 실제 위치 변경은 이동 서비스에 연결하고, HUD·표시 코드에서 HP·탄약·플레이어 위치를 직접 바꾸지 않는다.
3. 한 공격의 여러 콜라이더는 같은 `DamageHit.Id`를 사용한다. 새 공격에는 새 ID를 발급한다.
4. 비활성화·사망·상태 취소·스크립트 재로드에서 입력, 충돌 무시, 시간 효과, 임시 표시가 정리되는지 확인한다.
5. 변경한 기능을 [검증 안내](../Docs/ValidationGuide.md)의 항목대로 실제 플레이로 확인한다.

## 성능을 살필 때

Profiler에서 대표 상황의 CPU 시간과 `GC.Alloc`을 먼저 확인한다. API 이름만 보고 전체 공격이 무할당이라고 판단하지 않는다.

- `AttackTrace`는 보통 단일 Raycast를 사용하고, 제외할 충돌체를 만나면 다중 결과를 검사한다. 버퍼가 꽉 차면 누락된 가까운 벽을 놓치지 않도록 전체 결과를 다시 조회한다. 최적화할 때 이 포화 상황과 공격자 제외 규칙을 유지한다.
- `ShoveSweep`의 밀치기는 여러 적을 스칠 수 있다. `PlayerCombatActions`의 돌진 밀치기는 벽에 가리지 않은 가장 가까운 적 하나만 처리한다. 단일 타격 질의로 바꾸면 군중 밀치기와 벽 차폐가 달라질 수 있으므로, 배열 할당 개선과 타격 규칙 변경을 구분한다.
- `WeaponAimSolver`의 총구 주변 검사는 재사용 버퍼를 사용한다. 버퍼가 가득 찼고 자기 콜라이더만 발견됐다면 전체 조회로 엄폐 누락을 막는다. 일반 경로의 배열 할당은 줄였지만 전체 발사의 무할당이나 프레임 성능을 보장하는 수치는 아니다.
- 수신자 캐시·풀링을 도입한다면 부모 콜라이더의 대상 식별, 재사용 시 참조 갱신, 중복 타격 방지를 함께 검사한다.
