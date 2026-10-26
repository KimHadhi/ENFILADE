# 검증 안내

[프로젝트 안내](../../../README.md) · [설정·유지보수](MaintenanceGuide.md) · [최근 정리·검증 기록](MaintenanceReport.md)

## PowerShell에서 실행

프로젝트 루트에서 실행한다. 경로는 스크립트 위치에서 계산하므로 프로젝트 폴더 이름이 바뀌어도 사용할 수 있다.

```powershell
# 문서 상대 링크, Assets의 누락·고아 .meta와 중복 GUID
& ./Tools/Test-ProjectStructure.ps1

# 이동·전투·HUD의 Edit Mode 검사 묶음
& ./Tools/Validate-Project.ps1

# Edit Mode + 물리·렌더링·어셈블리 재로드 전체 검사
& ./Tools/Validate-Project.ps1 -All

# 수정한 영역만 선택
& ./Tools/Validate-Project.ps1 -Checks Ranged,GunFeel
```

Unity가 기본 Hub 설치 위치에 없으면 `-UnityEditor 'D:/Unity/6000.3.12f1/Editor/Unity.exe'`를 추가한다. [ProjectVersion.txt](../../../ProjectSettings/ProjectVersion.txt)와 같은 버전을 지정한다. 기본 제한 시간은 **검사별 1,200초**이며 `-TimeoutSeconds 1800`처럼 변경할 수 있다. Windows PowerShell 5.1과 PowerShell 7에서 사용할 수 있다. 로컬 스크립트 실행이 정책으로 막히면 해당 실행에만 `powershell -NoProfile -ExecutionPolicy Bypass -File ./Tools/Validate-Project.ps1`을 사용한다.

검사기는 현재 작업 파일의 `Assets`, `Packages`, `ProjectSettings`를 `.validation/<실행 ID>/Project`로 복사한다. 원본·기존 검사 폴더를 덮어쓰거나 삭제하지 않는다. 저장되지 않은 Unity 편집 내용은 포함되지 않으므로 실행 전에 저장한다. 첫 실행은 패키지 복원과 전체 임포트가 필요하다. 한 실행에서 선택한 검사들은 같은 복사본에서 순서대로 실행된다.

| 선택 이름 | 진입점 | 주요 범위 |
| --- | --- | --- |
| `EditMode` | [ProjectChecks.RunEditMode](../Editor/Checks/ProjectChecks.cs) | 이동 구조·기능, 무기 기본·주먹 0.2초 후딜·벽 접촉 연타/소리·근접 벽 무시·슬롯·원거리, 목표·궁극기·불꽃·체력·채팅 |
| `Slide` | [SlideCombatPlayChecks.RunBatch](../Editor/Checks/Combat/SlideCombatPlayChecks.cs) | 30/60/120Hz 슬라이딩 접촉·적 크기·저스트 창·벽·통과·레그돌 |
| `Defense` | [PerfectDefensePlayChecks.RunBatch](../Editor/Checks/Combat/PerfectDefensePlayChecks.cs) | 주먹·밀치기·벽 너머 반격, 완벽 방어·회피·적 통과·정리 |
| `Execution` | [ExecutionCrowdPlayChecks.RunBatch](../Editor/Checks/Combat/ExecutionCrowdPlayChecks.cs) | 처형 중 군중 공격 중단·NavMesh 후퇴·벽·밀침·복구 |
| `Ranged` | [RangedCombatPlayChecks.RunBatch](../Editor/Checks/Combat/RangedCombatPlayChecks.cs) | 실제 씬 원거리 전투·표시 |
| `GunFeel` | [GunFeelPlayChecks.RunBatch](../Editor/Checks/Combat/GunFeelPlayChecks.cs) | 실제 소총 발사·피격·처치·카메라 후처리 |
| `DamageFeedback` | [PlayerDamageFeedbackPlayChecks.RunBatch](../Editor/Checks/Combat/PlayerDamageFeedbackPlayChecks.cs) | 피격 방향 월드 추적·다중 피격·혈흔 비네트 셰이더 렌더링·쉐이크·빈사 상시 비네트·정리·화면 PNG |
| `Ammo` | [AmmoHUDChecks.RunBatch](../Editor/Checks/Combat/AmmoHUDChecks.cs) | 탄약·장전 HUD |
| `Console` | [ConsoleSpawnPlayChecks.RunBatch](../Editor/Checks/Combat/ConsoleSpawnPlayChecks.cs) | 테스트 적 기본 1마리·수량 지정 소환·공간 부족과 일부 소환·겹침 방지·웨이브 분리·정리 |
| `Ultimate` | [PlayerConsoleChecks.RunBatch](../Editor/Checks/Debug/PlayerConsoleChecks.cs) | 궁극기 스탯·최대 보유량 절대 지정·예비 충전량·실제 씬의 HUD 폭 상한·사용 기준 아이콘·총기 전체 탄약 보충·장전 중 보충·콘솔·Inspector 반영·HUD 프리팹 |
| `Reload` | [RuntimeReloadChecks.RunBatch](../Editor/Checks/Movement/RuntimeReloadChecks.cs) | 플레이 중 실제 어셈블리 재로드 후 이동·자세·HP·무기 복구 |

## Unity 메뉴에서 실행

**Edit Mode 검사는 재생을 종료하고 씬을 저장한 뒤** 실행한다. 전체 묶음은 `Tools/Validation/Run Edit Mode Checks`다. 검사 중 예외나 오류가 생기면 완료로 취급하지 않는다.

| 영역 | 개별 메뉴 |
| --- | --- |
| 이동 구조 | `Tools/Movement/Run Refactor Checks` |
| 이동 기능 | `Tools/Movement/Run Ability Checks` |
| 회피 반응 | `Tools/Movement/Run Dodge Response Checks` |
| 무기 기본 | `Tools/Combat/Run Foundation Checks` |
| 슬롯·주먹·가드·밀치기·처형 | `Tools/Combat/Run Four Slots and Fists Checks` |
| ADS·예비탄·헤드샷·반동·총구 추적 | `Tools/Combat/Run Ranged Combat Checks` |
| 목표 데이터 | `Tools/HUD/Run Mission Checks` |
| 궁극기 / 불꽃 / 체력 / 채팅 | `Tools/HUD/Run Ultimate HUD Checks` / `Run Ultimate Fire Checks` / `Run Player Health Checks` / `Run Chat Checks` |

**채팅·목표판 입력 충돌 검사는 Play Mode 전용**이다. CombatFoundation을 Play하고 Game 창에 포커스를 준 뒤 `Tools/HUD/Check Mission and Chat Input`을 실행한다. 이 검사는 입력 액션이 실제 동작하는 상태가 필요하며 위 EditMode 묶음에는 포함하지 않는다.

`Install`, `Connect`, `Export`, `Create`, `Repair` 메뉴는 씬·프리팹·에셋을 생성하거나 저장하는 제작 도구다. 검사 메뉴와 구분한다.

## 배치 진입점을 직접 사용할 때

복사본의 절대 경로를 `-projectPath`로 지정한다. 프레임 기반 `RunBatch`는 완료 후 에디터를 직접 종료하므로 **`-quit`를 붙이지 않는다.** 렌더링 검사가 포함되어 있어 공통 실행에 `-nographics`도 쓰지 않는다. 이 차이는 PowerShell 검사기가 자동 처리한다.

`ProjectChecks.RunEditMode`, `MovementAbilityChecks.RunAll`, `WeaponSlotsMeleeChecks.RunBatch`, `RangedCombatChecks.RunBatch`는 동기 메서드이므로 배치 명령에 `-quit`를 붙인다. 메서드 이름에 RunBatch가 있다는 이유만으로 종료 방식을 판단하지 않는다. 일부 검사는 임시 호스트 씬을 저장하므로 배치는 원본 대신 복사본에서 실행한다.

## 결과와 실제 플레이 확인

- 실행 폴더의 `results.json`에 소스 경로·기준 커밋·작업 변경 목록·Unity 버전·검사별 종료 코드·소요 시간·로그 경로가 기록된다.
- **종료 코드 0 + 해당 완료 문구**가 있어야 통과다. 실패·시간 초과는 결과에 남기고 나머지 선택 검사도 실행한다. 하나라도 실패하면 스크립트 전체가 실패한다.
- `Logs/<검사 이름>.log`의 예외·컴파일·패키지·셰이더 오류를 함께 확인한다. 초기 임포트나 편집기 자체 오류도 로그에서 구분해 기록한다.
- 이 문서는 검사 실행법이다. 실제 통과 기록은 [정리·검증 기록](MaintenanceReport.md)과 해당 실행 로그를 확인한다. 소스가 바뀌면 이전 기록이 새 상태를 보증하지 않는다.
- 현재 씬에서 슬롯 선택·공격·장전·총기 벽 차단과 근접 벽 무시, 주먹 벽 소리·벽 적중 연타·허공 0.2초 후딜, 이동·경사·낮은 천장, 적 크기별 슬라이딩·레그돌 복구, HUD 가독성·채팅 입력을 직접 확인한다. 자동 검사로 조작감과 모든 화면 구성을 대신할 수 없다.
- 검사 복사본의 `Library`는 재생성 가능하다. 필요한 로그를 보관하고 프로세스 종료 후 실행 폴더를 삭제할 수 있다.
