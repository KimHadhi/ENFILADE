# 검증 안내

변경한 기능에 해당하는 검사부터 실행한다. 이 문서는 **실행 방법과 검사 범위**를 설명하며, 현재 작업본에서 모든 검사를 통과했다는 기록은 아니다. 플레이 방법은 [프로젝트 안내](../README.md)를 참고한다.

## 편집기에서 실행

Play Mode를 종료하고 작업 중인 씬을 저장한다. 아래 메뉴는 Unity 상단의 `Tools`에서 찾을 수 있다. 이동·전투 검사는 주로 임시 씬을 만들어 검사하고 닫는다.

| 변경한 기능 | 메뉴 | 검사 코드 |
| --- | --- | --- |
| 이동 상태·자세·초기화 | `Tools/Movement/Run Refactor Checks` | [MovementRefactorChecks](../Editor/Checks/Movement/MovementRefactorChecks.cs) |
| 회피·파쿠르·다이브 등 이동 기능 | `Tools/Movement/Run Ability Checks` | [MovementAbilityChecks](../Editor/Checks/Movement/MovementAbilityChecks.cs) |
| 회피 감속·판정 연결 | `Tools/Movement/Run Dodge Response Checks` | [DodgeResponse](../Editor/Checks/Movement/MovementAbilityChecks.DodgeResponse.cs) |
| 총 발사·장전·탄약·차폐 | `Tools/Combat/Run Foundation Checks` | [WeaponFoundationChecks](../Editor/Checks/Combat/WeaponFoundationChecks.cs) |
| 슬롯·주먹·가드·밀치기·처형 | `Tools/Combat/Run Four Slots and Fists Checks` | [WeaponSlotsMeleeChecks](../Editor/Checks/Combat/WeaponSlotsMeleeChecks.cs) |
| ADS·예비탄·헤드샷·반동·총구 추적 | `Tools/Combat/Run Ranged Combat Checks` | [RangedCombatChecks](../Editor/Checks/Combat/RangedCombatChecks.cs) |
| 목표 데이터·진행 표시 | `Tools/HUD/Run Mission Checks` | [MissionHUDChecks](../Editor/Checks/UI/MissionHUDChecks.cs) |
| 채팅과 목표판의 입력 충돌 | `Tools/HUD/Check Mission and Chat Input` | [MissionInputChecks](../Editor/Checks/UI/MissionInputChecks.cs) |
| 궁극기 HUD / 불꽃 프레임 | `Tools/HUD/Run Ultimate HUD Checks` / `Run Ultimate Fire Checks` | [UltimateHUDChecks](../Editor/Checks/UI/UltimateHUDChecks.cs), [UltimateFireSetup.Check](../Editor/Setup/UI/UltimateFireSetup.cs) |

체력과 채팅의 개별 검사는 `Tools/HUD/Run Player Health Checks`, `Tools/HUD/Run Chat Checks`도 제공한다. `Install`, `Connect`, `Export`, `Create`, `Repair` 메뉴는 에셋이나 씬을 생성·수정할 수 있는 제작 도구이므로 검사와 구분해서 사용한다.

## Play Mode 검사

다음 검사는 실행 중인 프레임·물리·NavMesh·렌더링 상태를 사용한다. **별도 검증 프로젝트에서 배치 실행**한다. `Assets`, `Packages`, `ProjectSettings`를 복사하고 같은 Unity 버전을 사용한다. 검증 복사본의 초기 임포트가 필요하며, 원본 프로젝트를 `-projectPath`로 넘기지 않는다.

| 검사 진입점 | 주요 범위 |
| --- | --- |
| [SlideCombatPlayChecks.RunBatch](../Editor/Checks/Combat/SlideCombatPlayChecks.cs) | 30/60/120Hz의 적 크기별 반응, 0.18초 창, 벽 차폐·통과·정리. 레그돌 검사도 함께 실행 |
| [PerfectDefensePlayChecks.RunBatch](../Editor/Checks/Combat/PerfectDefensePlayChecks.cs) | 완벽 방어·반격, 적 몸 통과와 종료 처리 |
| [ExecutionCrowdPlayChecks.RunBatch](../Editor/Checks/Combat/ExecutionCrowdPlayChecks.cs) | 처형 중 군중 공격 중단, NavMesh 후퇴·벽 차단·복구 |
| [RangedCombatPlayChecks.RunBatch](../Editor/Checks/Combat/RangedCombatPlayChecks.cs) | 실제 씬의 원거리 전투와 표시 |
| [GunFeelPlayChecks.RunBatch](../Editor/Checks/Combat/GunFeelPlayChecks.cs) | 실제 소총의 발사·명중·처치 피드백, 피격 반응, 카메라 후처리 유지 |
| [AmmoHUDChecks.RunBatch](../Editor/Checks/Combat/AmmoHUDChecks.cs) | 탄약·장전 HUD |
| [ConsoleSpawnPlayChecks.RunBatch](../Editor/Checks/Combat/ConsoleSpawnPlayChecks.cs) | 테스트 적 소환·설정·정리 |
| [RuntimeReloadChecks.RunBatch](../Editor/Checks/Movement/RuntimeReloadChecks.cs) | 플레이 중 어셈블리 재로드 후 이동·자세·체력·무기 복구 |

이 진입점들은 완료 시 에디터를 직접 종료한다. 실행 명령에 **`-quit`를 붙이지 않는다.** 프레임 기반 검사가 완료되기 전에 종료될 수 있다. 렌더링과 셰이더를 확인하는 검사를 포함하므로 공통 예제에서는 `-nographics`도 사용하지 않는다.

Windows PowerShell 예제다. `$validationProject`는 준비한 **검증 복사본의 절대 경로**로 바꾼다.

```powershell
$unityEditor = 'C:\Program Files\Unity\Hub\Editor\6000.3.12f1\Editor\Unity.exe'
$validationProject = 'F:\GameProject\MorphangValidation'
$logPath = Join-Path $validationProject 'slide-checks.log'
$unityArguments = '-batchmode -projectPath "{0}" -executeMethod SlideCombatPlayChecks.RunBatch -logFile "{1}"' -f $validationProject, $logPath
$checkProcess = Start-Process -FilePath $unityEditor -ArgumentList $unityArguments -WindowStyle Hidden -PassThru -Wait
$checkProcess.ExitCode
```

다른 검사는 `-executeMethod` 값을 표의 진입점으로 바꿔 실행한다.

## 동기 검사를 배치로 실행

`RunBatch`라는 이름만으로 종료 방식을 판단하지 않는다. 다음 메서드는 호출 중 검사를 마치고 직접 에디터를 종료하지 않으므로, 배치 실행에는 `-quit`를 붙인다.

| 진입점 | 묶어서 실행하는 범위 |
| --- | --- |
| `MovementAbilityChecks.RunAll` | 이동 구조 + 이동 기능 |
| `WeaponSlotsMeleeChecks.RunBatch` | 주먹·슬롯 + 무기 기본 + 이동 구조 |
| `RangedCombatChecks.RunBatch` | 위 전투 묶음 + 원거리 전투 |

예제 명령의 `-executeMethod` 값을 선택한 진입점으로 바꾸고 `-batchmode` 뒤에 `-quit`를 추가한다. 이 검사도 임시 호스트 씬을 저장할 수 있으므로 검증 복사본에서 실행한다.

## 결과 읽기

- 종료 코드와 로그를 함께 확인한다. 예외·컴파일 오류·실패 메시지가 있으면 마지막 성공 문구만 보고 통과로 판단하지 않는다.
- Play Mode 검사는 `SLIDE_COMBAT_PLAY_CHECKS_PASSED`, `GUN_FEEL_PLAY_CHECKS_PASSED`처럼 해당 검사명이 포함된 완료 메시지를 남긴다.
- 실제 씬에서 바꾼 인스펙터 값과 조작 느낌은 Play로 추가 확인한다. 자동 검사 결과만으로 화면 구성과 모든 튜닝 값을 확인할 수는 없다.
- 결과를 남길 때는 코드 버전, 실행 진입점, 종료 코드, 로그 위치를 함께 기록한다. 재생성 가능한 검증 프로젝트는 `Assets` 안에 보관하지 않는다.

[프로젝트 안내](../README.md) · [스크립트 길잡이](../Scripts/README.md)
