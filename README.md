# ENFILADE

1~4명이 함께 하는 1인칭 좀비 액션 게임이다. 발까지 보이는 진짜 1인칭 몸으로 달리고, 넘고, 미끄러지고, 총과 주먹과 칼로 싸운다. **Unity 6000.3.27f1**, URP를 쓴다. 정확한 버전은 [ProjectVersion.txt](ProjectSettings/ProjectVersion.txt), 패키지는 [manifest.json](Packages/manifest.json)이 기준이다.

**0.9.0**은 프로젝트 구조를 처음부터 다시 정리한 판이다. 예전 자리에서 무엇이 어디로 갔는지는 [구조 안내의 표](Docs/Architecture.md#090에서-바뀐-자리)에 있다.

## 바로 실행하기

1. Unity Hub에서 **이 README가 있는 폴더**를 프로젝트로 추가하고, 지정된 Unity 버전으로 연다.
2. [MainMenu](Assets/_Project/Scenes/MainMenu.unity) 씬을 열고 Play한다. 빌드의 첫 씬도 메인 메뉴다.
3. 메인 메뉴에서 혼자 하기나 방 만들기를 고르면 허브(은신처)로 간다. 허브의 지도 탁자에서 장을 고르고 준비 버튼을 눌러 출발한다.

| 씬 | 용도 |
| --- | --- |
| [MainMenu](Assets/_Project/Scenes/MainMenu.unity) | 메인 메뉴: 플레이 방식, 클래스, 무기, 캐릭터와 옷장, 기록 |
| [Hub](Assets/_Project/Scenes/Hub/Hub.unity) | 걸어 다니는 은신처: 장 고르기, 클래스 벽, 총 진열대, 사격장, 거울, 장 결과 연출 |
| [Campaign](Assets/_Project/Scenes/Campaign) Ch1~Ch4 | 캠페인: 옥상 탈출, 막힌 고가도로, 지하철, 공항 |
| [EnfiladeTower](Assets/_Project/Scenes/Campaign/EnfiladeTower.unity) | 엔필레이드 타워: 1~4명 시련의 탑 |
| [Development](Assets/_Project/Scenes/Development) | 테스팅 룸(전투·이동 시험장). 메뉴에는 없고 콘솔 `/testroom`으로 간다 |

허브와 캠페인·타워 씬은 코드로 짓는다. 맵을 고쳤으면 Unity의 **ENFILADE → 맵** 메뉴에서 다시 짓는다.

## 기본 조작

키는 게임의 **설정 → 조작 → 키 설정**에서 보고 바꾼다. 처음 값은 다음과 같다.

| 하는 일 | 키 |
| --- | --- |
| 이동 · 달리기 · 점프 | WASD · Shift · Space |
| 앉기·슬라이드 · 다이브·포복 · 회피 · 기울이기 | Ctrl · Z · Alt · Q/E |
| 공격 · 조준/가드 · 장전 | 좌클릭 · 우클릭 · R |
| 무기 칸 | 1~4 (같은 키를 다시 누르면 내린다) |
| 밀치기(달리는 중이면 돌진) · 투척 · 돌 던지기 · 노크 | V · G · T · H |
| 상호작용·처형 (길게 누르면 가리키기) | F |
| 능력 카드 · 총 꾸미기 · 점수판 | X · C · Tab |
| 콜아웃 휠 | 마우스 뒤로 버튼 |
| 뒤에서 몸 보기 · 일시정지·설정 · 개발자 콘솔 | F5 · Esc · ` |

## 필요한 안내 찾기

| 하려는 일 | 안내 |
| --- | --- |
| 프로젝트가 어떻게 짜여 있는지, 새 기능을 어디에 두는지 | [구조 안내](Docs/Architecture.md) |
| 모드 만들기 (표만 고치는 모드, Unity로 만드는 모드) | [모드 만들기](Docs/Modding.md), [Mods 폴더](Mods/README.md) |
| 수정할 코드 찾기 | [스크립트 길잡이](Assets/_Project/Scripts/README.md) (폴더마다 README가 있다) |
| 체력·무기·적·이동 같은 숫자 바꾸기 (CSV 표, 컴파일 불필요) | [표 안내](Docs/Guides/MaintenanceGuide.md#밸런스-표-data-tables), [Resources/Tables](Assets/_Project/Resources/Tables) |
| 화면 글자 고치기, 번역 | Unity의 ENFILADE → 글자 → 글자 편집기, [화면 문구](Docs/Guides/MaintenanceGuide.md#화면-문구-textscsv) |
| 이동·파쿠르 조작과 수치 | [이동 안내](Docs/Guides/MovementGuide.md) |
| 무기·적·전투 UI 만들기 | [전투 제작 안내](Docs/Guides/CombatFoundationGuide.md), [전투 규칙](Docs/Guides/WeaponSystemDesign.md) |
| 플레이어 몸, 캐릭터와 옷, 애니메이션 | [플레이어 몸](Docs/Guides/PlayerBodyGuide.md) |
| 맵의 하늘·조명·반사·간접광 | [조명 안내](Docs/Guides/LightingGuide.md) |
| 소리·모델 추가, 설정, 글꼴, HUD 겹침 검사 | [설정·유지보수 안내](Docs/Guides/MaintenanceGuide.md) |
| 기능별 설명 (클래스 카드, 타워, 처형, 투척물, 좀비 엘리트 등) | [Docs/Features](Docs/Features) |
| 외부 에셋의 출처, 워크숍 애드온에서 옮긴 것 | [외부 에셋 출처](Docs/Records/AssetSources.md), [애드온 이식 기록](Docs/Records/AddonPortNotes.md) |
| 모든 문서의 목록 | [문서 목차](Docs/README.md) |

## 폴더 구성

| 위치 | 내용 |
| --- | --- |
| `Assets/_Project` | 게임 그 자체. 안의 구성은 [구조 안내](Docs/Architecture.md#assets_project-안) |
| `Assets/_Project/Scripts` | 게임 코드, 기능별 폴더 16개 |
| `Assets/_Project/Editor` | Unity 편집기 도구 (메뉴 막대의 **ENFILADE** 메뉴) |
| `Assets/_Project/Resources/Tables` | 게임의 모든 숫자와 글자: 분야별 폴더의 CSV 표 |
| `Assets/ThirdParty` | 사거나 받은 에셋 팩 |
| `Assets/_PoiyomiShaders`, `_LockedShaderCache`, `ProBuilder Data` | 도구가 자리를 정해 둔 폴더 (Poiyomi 셰이더, ProBuilder) |
| `Mods` | 모드 폴더. 게임을 켤 때 읽는다 ([안내](Mods/README.md)) |
| `Docs` | 문서 |
| `ArtSource` | 블렌더 작업실 (애니메이션을 손보고 저장하면 게임에 들어간다) |
| `Tools` | Unity 밖에서 쓰는 도구: 블렌더 스크립트, 아이콘·소리 만들기, 구조 검사 |
| `Packages`, `ProjectSettings` | 패키지 목록, Unity 프로젝트 설정 |

## 변경 후 확인하기

바꾼 기능은 직접 플레이해서 확인한다([검증 안내](Docs/Guides/ValidationGuide.md)). 파일을 옮기거나 문서를 고쳤다면 프로젝트 맨 위에서 링크와 메타 파일을 검사한다. Unity 없이 몇 초면 끝난다.

```powershell
& ./Tools/Test-ProjectStructure.ps1
```
