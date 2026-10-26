# Morphang 프로젝트 안내

플레이어 이동·파쿠르, 원거리·근접 전투, 적 AI와 HUD를 개발하는 Unity 프로젝트다. 직접 관리하는 게임 에셋은 `_Project`, 외부 원본은 `Assets/ThirdParty`에 둔다.

## 시작하기

1. Unity **6000.3.12f1**로 프로젝트를 연다. 버전의 기준은 [ProjectVersion.txt](../../ProjectSettings/ProjectVersion.txt)다.
2. [CombatFoundation.unity](Scenes/Development/CombatFoundation.unity)를 열고 Play한다. Game 창에 포커스를 두고 플레이한다.
3. 기본 조작은 [이동 안내](Docs/MovementGuide.md), 무기·적·전투 설정은 [전투 제작 안내](Docs/CombatFoundationGuide.md)를 확인한다.

| 씬 | 용도 |
| --- | --- |
| [CombatFoundation](Scenes/Development/CombatFoundation.unity) | 플레이어·무기·적·HUD를 함께 확인하는 개발 씬. 현재 빌드 등록 씬 |
| [MovementAbilityPlayground](Scenes/Development/MovementAbilityPlayground.unity) | 이동·파쿠르 연습 씬 |
| [Mainmenu](Scenes/Menu/Mainmenu.unity) | 메뉴·설정 UI. 현재 빌드 목록에는 포함되지 않음 |

## 필요한 문서 찾기

| 하고 싶은 일 | 읽을 문서 |
| --- | --- |
| 조작·자세·회피·저스트 슬라이드 확인 | [이동 안내](Docs/MovementGuide.md) |
| 무기 수치 변경, 적 추가, 전투 문제 확인 | [전투 제작 안내](Docs/CombatFoundationGuide.md) |
| 전투 규칙과 아직 구현하지 않은 기획 구분 | [무기·전투 설계](Docs/WeaponSystemDesign.md) |
| 수정할 코드와 기능 담당 파일 찾기 | [스크립트 길잡이](Scripts/README.md) |
| 전투 코드 확장 | [전투 코드 구조](Scripts/Combat/README.md) |
| 이동 상태·초기화·프레임 순서 수정 | [이동 코드 구조](Scripts/Player/Movement/README.md) |
| 변경한 기능 검사 | [검증 안내](Docs/ValidationGuide.md) |

## 폴더 지도

| 위치 | 담는 내용 |
| --- | --- |
| `Art/` | 오디오·재질·셰이더·이미지와 프로젝트 UI용 폰트 |
| `Data/` | 무기 데이터와 UI 프로필 |
| `Input/` | 입력 액션과 입력 설정 |
| `Prefabs/` | 적·UI·무기 이펙트 프리팹 |
| `Scenes/` | 개발 씬과 메뉴 씬, 씬에 연결된 NavMesh |
| `Scripts/` | 게임 로직, 런타임 UI, 디버그 기능 |
| `Editor/Setup/` | 씬·컴포넌트·HUD를 생성하거나 연결하는 도구 |
| `Editor/Checks/` | 이동·전투·UI 검사 |
| `Editor/Utilities/` | 미리보기와 편집 보조 도구 |
| `Resources/` | 문자열 키로 불러오는 설정·셰이더 등. 사용 중인 로드 키를 유지해야 함 |
| `Settings/Rendering/` | 렌더 파이프라인과 볼륨 설정 |
| `Docs/` | 사용·제작·설계·검증 안내 |

외부 소총 자료는 `Assets/ThirdParty/InfimaGames`, 원본 폰트·기본 SDF는 `Assets/ThirdParty/KoreanFonts`, TextMesh Pro 자료는 `Assets/ThirdParty/TextMeshPro`에 있다. 아직 쓰지 않는 폰트도 보관한다. `Assets/ProBuilder Data`와 `Assets/ProjectSettings`는 해당 도구가 사용하는 위치다.

## 수정할 때의 기준

- **수치 조정은 씬·프리팹·데이터 에셋의 Inspector부터 확인한다.** 코드 기본값을 바꿔도 이미 저장된 값은 자동으로 바뀌지 않는다.
- 에셋 이동·이름 변경은 Unity Project 창에서 처리해 `.meta`의 GUID를 유지한다. 코드에 직접 적힌 경로와 생성 도구의 저장 경로도 함께 확인한다.
- `Resources.Load`의 키는 `Resources` 폴더 아래의 상대 경로다. 예를 들어 [Feedback.asset](Resources/Combat/Feedback.asset)은 `Combat/Feedback`, [GunFeel.asset](Resources/Combat/GunFeel.asset)은 `Combat/GunFeel`로 로드한다.
- `Editor/Setup` 도구는 씬이나 프리팹을 실제로 저장할 수 있다. 용도와 대상 씬을 확인한 뒤 사용한다. 검사 도구의 실행 방법은 [검증 안내](Docs/ValidationGuide.md)에 있다.
- 새 에디터 도구가 폴더를 만들 때는 [ProjectAssetPaths](Editor/Utilities/ProjectAssetPaths.cs)를 사용한다. 생성 파일도 이 폴더 구조에 맞춘다.
