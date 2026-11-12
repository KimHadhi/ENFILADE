# ENFILADE 프로젝트 폴더

게임 그 자체가 이 폴더에 있다. 시작 안내는 프로젝트 맨 위의 **[README](../../README.md)**, 구조는 **[구조 안내](../../Docs/Architecture.md)**에 있다.

| 폴더 | 무엇 |
| --- | --- |
| [Scripts](Scripts/README.md) | 게임 코드 (기능별 폴더, 폴더마다 README) |
| [Editor](Editor/README.md) | Unity 편집기 도구 (ENFILADE 메뉴) |
| Art | 보고 듣는 것 전부: 그림, 소리, 모델, 동작, 셰이더, 재질 |
| Data | 숫자와 장부: [표](Data/Resources/Tables)(분야별 CSV), 목록 에셋, 총 데이터, 구운 클립 |
| Prefabs | 조립해 둔 물건 (GameHUD, 플레이어, 효과) |
| Scenes | 씬 |
| Input, Settings | 입력 액션, 렌더링 설정 |

## 무엇이 어디에 있나 (정의)

1. **종류가 폴더를 정한다.** 그림·소리·모델은 `Art`에만, 숫자와 목록은 `Data`에만, 조립한 것은 `Prefabs`에만 있다.
2. **`Resources`는 종류가 아니라 서랍이다.** Unity는 `Resources`라는 이름의 폴더 안의 것을 이름으로 불러올 수 있다.
   게임이 이름으로 부르는 것만 그 서랍에 넣고, 서랍은 종류마다 하나씩 딱 셋이다: `Art/Resources`, `Data/Resources`,
   `Prefabs/Resources`. 게임이 부르는 이름은 서랍부터의 경로다(`UI/Icons/Fist`). 맨 위의 `Resources` 폴더는 없다.
3. **서랍 밖의 것은 연결해서 쓴다.** 프리팹·에셋·씬이 직접 가리키거나, 편집기 도구가 폴더를 읽어 장부(소리 묶음 등)를 만든다.
4. **게임이 쓰지 않는 것은 여기 두지 않는다.** 재료·후보·예전 것은 Unity 밖의 [ArtSource/Library](../../ArtSource/Library/README.md)에 둔다.

서랍 규칙은 `Tools/Test-ProjectStructure.ps1`이 검사하고, 아무도 쓰지 않는 에셋은 `Tools/Find-UnusedAssets.py`가 찾아 준다.
편집기 도구가 서랍의 자리를 쓸 때는 [ProjectFolders](Scripts/Core/ProjectFolders.cs)의 이름을 쓴다.

문서는 모두 [Docs](../../Docs/README.md)에, 모드는 [Mods](../../Mods/README.md)에 있다.
