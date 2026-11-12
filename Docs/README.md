# ENFILADE 문서 목차

처음이면 [프로젝트 안내](../README.md)와 [구조 안내](Architecture.md)부터 읽는다.

## 구조와 모드

| 문서 | 내용 |
| --- | --- |
| [구조 안내](Architecture.md) | 폴더 구성, 게임이 켜지는 순서, 숫자와 글자가 흐르는 길, 새 기능을 두는 자리, 0.9.0에서 바뀐 자리 |
| [모드 만들기](Modding.md) | 표만 고치는 모드(Unity 불필요)와 Unity 프로젝트에서 만드는 모드, 표 목록 |
| [스크립트 길잡이](../Assets/_Project/Scripts/README.md) | 코드 폴더 지도. 폴더마다 "무엇을 고치려면 어느 파일" README가 있다 |
| [편집기 도구](../Assets/_Project/Editor/README.md) | Unity의 ENFILADE 메뉴에 있는 도구들 |

## 안내서 (Guides)

| 문서 | 내용 |
| --- | --- |
| [이동 안내](Guides/MovementGuide.md) | 이동·슬라이드·다이브·회피·파쿠르 조작, 사다리와 집라인 |
| [플레이어 몸](Guides/PlayerBodyGuide.md) | 진짜 1인칭 몸, 캐릭터와 옷, BOOTH 가져오기, 표정, 애니메이션 추가 |
| [전투 제작 안내](Guides/CombatFoundationGuide.md) | 무기·적·전투 UI를 만들고 고치는 순서, 새 총 연결하기 |
| [전투 규칙과 확장 계획](Guides/WeaponSystemDesign.md) | 지금의 전투 규칙과 그 이유 |
| [플레이어 피해 표시](Guides/PlayerDamageFeedbackGuide.md) | 맞았을 때 내 화면에 나오는 것 |
| [조명 안내](Guides/LightingGuide.md) | 맵의 하늘, 해와 달, 조명 기구, 안개, 반사, 간접광 굽기 |
| [설정·유지보수 안내](Guides/MaintenanceGuide.md) | 표 쓰는 법, 화면 글자, 설정, 글꼴, 무기 아이콘, HUD 겹침 검사, 소리와 모델 추가 |
| [검증 안내](Guides/ValidationGuide.md) | 바꾼 뒤 무엇을 확인하는지 |

## 기능별 설명 (Features)

| 문서 | 내용 |
| --- | --- |
| [클래스 카드](Features/ClassCards.md) | 클래스마다의 능력 카드 |
| [엔필레이드 타워](Features/Tower.md) | 시련의 탑 |
| [처형·암살](Features/Executions.md) | 자세별 짝 애니메이션 |
| [투척물](Features/Grenades.md) | G 투척과 클래스 투척물 |
| [좀비 엘리트](Features/ZombieElites.md) | 특수 좀비 |
| [데스 스크린](Features/DeathScreen.md) | 죽었을 때의 화면 |
| [자연스러운 달리기](Features/NaturalRun.md) | 걸음과 달리기 다리 |
| [손가락질과 찌르기](Features/Poke.md) | F 길게 눌러 가리키기 |

## 기록 (Records)

| 문서 | 내용 |
| --- | --- |
| [외부 에셋 출처](Records/AssetSources.md) | 어떤 팩을 어디서 얻었고 무엇에 쓰는지 |
| [애드온 이식 기록](Records/AddonPortNotes.md) | GMod·L4D2 워크숍 애드온에서 옮긴 것 |

## 작업실과 도구의 안내

| 문서 | 내용 |
| --- | --- |
| [블렌더 원클릭 작업실](../Tools/Blender/OneClick/README.md) | 작업실을 열면 바로 일하고 저장하면 게임에 들어가는 방식 |
| [처형 작업실](../Tools/Blender/Executions/README.md), [좀비 작업실](../Tools/Blender/Zombies/README.md), [True FPS 작업실](../Tools/Blender/TrueFPS/README.md) | 블렌더 작업실별 안내 |
| [소품 가져오기](../Tools/Props/README.md) | 맵 소품과 데칼을 팩에서 들이는 도구 |
| `ArtSource/*/START_HERE.md` | 각 블렌더 작업실을 여는 법 |
