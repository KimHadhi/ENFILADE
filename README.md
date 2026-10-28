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
| 체력·무기·적·웨이브 밸런스 숫자 변경 (CSV 표, 컴파일 불필요) | [밸런스 표](Assets/_Project/Docs/MaintenanceGuide.md#밸런스-표-data-tables), [Resources/Tables](Assets/_Project/Resources/Tables) |
| 설정 수치 변경·저장, 폴더 이름 변경 후 복구 | [설정·유지보수 안내](Assets/_Project/Docs/MaintenanceGuide.md) |
| 3D 모델·텍스처 추가 (폴더 규칙, 자동 임포트 설정) | [3D 모델 추가하기](Assets/_Project/Docs/MaintenanceGuide.md#3d-모델-추가하기) |
| 플레이어 몸(진짜 1인칭)·캐릭터 교체·애니메이션 추가 | [플레이어 몸](Assets/_Project/Docs/MaintenanceGuide.md#플레이어-몸-진짜-1인칭) |
| 사운드 추가·교체, 공격·피격·벽 소리의 폴더와 연결 위치 | [사운드 관리](Assets/_Project/Docs/MaintenanceGuide.md#사운드를-추가하거나-교체하기) |
| 수정할 코드 찾기 | [스크립트 길잡이](Assets/_Project/Scripts/README.md) |
| 전투 또는 이동 코드 확장 | [전투 코드 구조](Assets/_Project/Scripts/Combat/README.md), [이동 코드 구조](Assets/_Project/Scripts/Player/Movement/README.md) |
| 변경 사항 검사 | [검증 안내](Assets/_Project/Docs/ValidationGuide.md) |

## 폴더 구성

| 위치 | 내용 |
| --- | --- |
| `Assets/_Project/Scripts` | 게임 로직·UI·디버그 코드 |
| `Assets/_Project/Editor` | 편집 전용 도구: 표 다시 읽기, 모델 임포트 규칙·물리 프랍, HUD 편집 미리보기, 폰트 저장 |
| `Assets/_Project/Data` | 무기 ScriptableObject와 UI 프로필 |
| `Assets/_Project/Resources` | 실행 중 키로 로드하는 설정·셰이더, `Tables`의 밸런스 CSV 표 |
| `Assets/_Project/Scenes`, `Prefabs` | 씬·NavMesh / 적·UI·이펙트 프리팹 |
| `Assets/_Project/Art`, `Input`, `Settings` | 프로젝트 3D 모델(`Art/Models`)·애니메이션(`Art/Animations`)·그래픽·오디오 / 입력 / 렌더링 설정 |
| `Assets/_Project/Docs` | 조작·제작·구조·검증 문서 |
| `Assets/ThirdParty` | 외부 소총 모델·폰트·TextMesh Pro 원본 |
| `Assets/ProBuilder Data`, `Assets/ProjectSettings` | 도구가 관리하는 데이터 |
| `Packages`, `ProjectSettings` | 패키지 버전·잠금 정보 / Unity 프로젝트 설정 |
| `Tools` | Unity 없이 문서 링크·메타데이터 구조 확인, `Blender`의 Source 모델·애니메이션 변환 스크립트 |

## 변경 후 확인하기

CombatFoundation을 Play해 바꾼 기능을 직접 조작한다. 확인 항목과 선택적인 Unity 메뉴 검사는 [검증 안내](Assets/_Project/Docs/ValidationGuide.md)에 있다. 에셋을 옮기거나 문서를 고쳤다면 프로젝트 루트에서 링크·메타데이터를 확인한다.

```powershell
& ./Tools/Test-ProjectStructure.ps1
```
