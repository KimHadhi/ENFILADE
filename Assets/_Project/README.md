# 프로젝트 에셋 안내

게임에서 직접 관리하는 에셋은 `_Project`, 외부 원본은 `Assets/ThirdParty`에 둡니다.

| 폴더 | 내용 |
| --- | --- |
| `Art/` | 오디오, 재질, 셰이더, 이미지와 프로젝트 UI용 폰트 |
| `Data/` | 무기 수치와 UI 프로필 |
| `Input/` | 입력 액션과 입력 설정 |
| `Prefabs/` | 적, UI, 무기 이펙트 프리팹 |
| `Scenes/Development/` | CombatFoundation 및 MovementAbilityPlayground 씬과 NavMesh |
| `Scenes/Menu/` | 메인 메뉴 씬 |
| `Scripts/` | 플레이어, 공용 전투, 적 AI, UI, 월드와 디버그 코드 |
| `Editor/Setup/` | 씬·HUD 구성 도구 |
| `Editor/Checks/` | 전투·이동·UI 검증 도구 |
| `Editor/Utilities/` | 편집기 보조 도구 |
| `Resources/Combat/` | `Resources.Load("Combat/Feedback")`로 불러오는 피드백 설정 |
| `Settings/Rendering/` | 렌더 파이프라인 및 볼륨 설정 |
| `Docs/` | 기능·조작·무기 제작 안내 |

시작점은 [스크립트 길잡이](Scripts/README.md), [전투 제작 안내](Docs/CombatFoundationGuide.md), [이동 기능 안내](Docs/MovementGuide.md)입니다.

원본 폰트와 기본 SDF는 `Assets/ThirdParty/KoreanFonts`, 외부 소총 모델은 `Assets/ThirdParty/InfimaGames`, TextMesh Pro 자료는 `Assets/ThirdParty/TextMeshPro`에 있습니다. 아직 사용하지 않는 폰트도 모두 보관합니다. `Assets/ProBuilder Data`와 `Assets/ProjectSettings`는 도구가 사용하는 위치를 유지합니다.

에셋을 이동할 때는 Unity Project 창을 사용해 `.meta`와 GUID를 함께 유지합니다. 새 코드에서 경로를 직접 지정하거나 에셋을 생성할 때는 이 구조에 맞추고, 에디터 도구의 부모 폴더는 `ProjectAssetPaths.EnsureFolder`로 준비합니다.
