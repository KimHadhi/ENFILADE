# ENFILADE

이동·파쿠르와 원거리·근접 전투를 개발하는 Unity 1인칭 액션 프로젝트다. **Unity 6000.3.12f1**을 사용한다. 실제 버전은 [ProjectVersion.txt](ProjectSettings/ProjectVersion.txt), 패키지는 [manifest.json](Packages/manifest.json)과 [packages-lock.json](Packages/packages-lock.json)이 기준이다.

## 바로 실행하기

1. Unity Hub에서 **이 README가 있는 폴더**를 프로젝트로 추가한다. 폴더 이름이나 저장 위치를 바꿨다면 새 경로로 다시 추가한다.
2. 지정된 Unity 버전으로 열고 패키지 복원·임포트·컴파일을 기다린다.
3. [CombatFoundation](Assets/_Project/Scenes/Development/CombatFoundation.unity)을 열고 Play한다. Game 창을 클릭해 마우스를 잠근 뒤 버튼을 놓았다 다시 누른다.
4. **1**은 소총, **2**는 피스톨, **3/4**의 빈 슬롯은 주먹이다. 같은 슬롯 키를 다시 누르면 장비를 내린다. 시작 시에는 장비를 들고 있지 않다.
5. WASD 이동, Shift 달리기, Space 점프, Ctrl 앉기·슬라이드, Z 다이브·포복, Alt 회피, Q/E 기울이기, V 밀치기, F 처형. 좌클릭 공격, 우클릭 ADS·가드, R 장전, Esc 마우스 해제.

| 씬 | 용도 |
| --- | --- |
| [CombatFoundation](Assets/_Project/Scenes/Development/CombatFoundation.unity) | 무기·적·HUD 통합 개발 씬. 현재 빌드에 등록된 유일한 씬 |
| [MovementAbilityPlayground](Assets/_Project/Scenes/Development/MovementAbilityPlayground.unity) | 이동·파쿠르 시험장 |
| [Mainmenu](Assets/_Project/Scenes/Menu/Mainmenu.unity) | 메뉴·설정 UI. 현재 빌드에는 미등록 |

## 필요한 안내 찾기

| 하려는 일 | 안내 |
| --- | --- |
| 이동·슬라이드·다이브·회피·파쿠르 조작 | [이동 안내](Assets/_Project/Docs/MovementGuide.md) |
| 무기·적·전투 UI를 만들거나 수정 | [전투 제작 안내](Assets/_Project/Docs/CombatFoundationGuide.md) |
| 현재 전투 규칙과 이후 기획 확인 | [전투 규칙과 확장 계획](Assets/_Project/Docs/WeaponSystemDesign.md) |
| 설정 수치 변경·저장, 폴더 이름 변경 후 복구 | [설정·유지보수 안내](Assets/_Project/Docs/MaintenanceGuide.md) |
| 사운드 추가·교체, 공격·피격·벽 소리의 폴더와 연결 위치 | [사운드 관리](Assets/_Project/Docs/MaintenanceGuide.md#사운드를-추가하거나-교체하기) |
| 수정할 코드 찾기 | [스크립트 길잡이](Assets/_Project/Scripts/README.md) |
| 전투 또는 이동 코드 확장 | [전투 코드 구조](Assets/_Project/Scripts/Combat/README.md), [이동 코드 구조](Assets/_Project/Scripts/Player/Movement/README.md) |
| 변경 사항 검사 | [검증 안내](Assets/_Project/Docs/ValidationGuide.md) |
| 이번 정리 내용과 실제 검사 결과 확인 | [정리·검증 기록](Assets/_Project/Docs/MaintenanceReport.md) |

## 폴더 구성

| 위치 | 내용 |
| --- | --- |
| `Assets/_Project/Scripts` | 게임 로직·UI·디버그 코드 |
| `Assets/_Project/Editor` | 편집 전용 설치·생성·검사 도구 |
| `Assets/_Project/Data` | 무기 ScriptableObject와 UI 프로필 |
| `Assets/_Project/Resources` | 실행 중 키로 로드하는 설정·셰이더 |
| `Assets/_Project/Scenes`, `Prefabs` | 씬·NavMesh / 적·UI·이펙트 프리팹 |
| `Assets/_Project/Art`, `Input`, `Settings` | 프로젝트 그래픽·오디오 / 입력 / 렌더링 설정 |
| `Assets/_Project/Docs` | 조작·제작·구조·검증 문서 |
| `Assets/ThirdParty` | 외부 소총 모델·폰트·TextMesh Pro 원본 |
| `Assets/ProBuilder Data`, `Assets/ProjectSettings` | 도구가 관리하는 데이터 |
| `Packages`, `ProjectSettings` | 패키지 버전·잠금 정보 / Unity 프로젝트 설정 |
| `Tools` | Unity 없이 구조 확인, 별도 복사본에서 Unity 검사 실행 |

## 변경 후 확인하기

프로젝트 루트에서 PowerShell로 실행한다. 첫 번째 명령은 문서 링크와 에셋 메타데이터를, 두 번째는 이동·전투·HUD 회귀 검사를 확인한다.

```powershell
& ./Tools/Test-ProjectStructure.ps1
& ./Tools/Validate-Project.ps1
```

물리·렌더링·재컴파일을 포함한 전체 검사는 `& ./Tools/Validate-Project.ps1 -All`이다. 검사기는 현재 파일을 `.validation` 아래 새 프로젝트로 복사하고, 원본에서 Unity를 실행하지 않는다. 결과와 로그는 각 실행 폴더에 남긴다. 초기 임포트에는 시간이 걸린다. 실행 옵션과 결과 해석은 [검증 안내](Assets/_Project/Docs/ValidationGuide.md)를 따른다.
