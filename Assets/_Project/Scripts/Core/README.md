# Core — 바탕

게임 어디서나 쓰는 바탕이다. 프레임 순서, 표, 글자, 모드, 설정, 로딩, 씬 폴더 정리.

전체 지도는 [스크립트 길잡이](../README.md), 프로젝트 구조는 [구조 안내](../../../../Docs/Architecture.md)에 있다.

## 폴더

| 폴더 | 스크립트 | 무엇 |
| --- | ---: | --- |
| [Common](Common/) | 13 | 어디서나 쓰는 작은 도우미: 이징 곡선([Ease](Common/Ease.cs)), 트랜스폼 이름으로 찾기([Hierarchy](Common/Hierarchy.cs)), 메시 읽기([MeshTools](Common/MeshTools.cs)), 재질이 그리는 모습 읽기([MaterialLook](Common/MaterialLook.cs)), Unity 오브젝트의 진짜 null([UnityNull](Common/UnityNull.cs)), 플레이어 파일 쓰기·읽기([SaveFile](Common/SaveFile.cs)), 숫자 표기 고정([GameCulture](Common/GameCulture.cs)), 레이어를 한곳에서 말하기([WorldLayers](Common/WorldLayers.cs): 세상의 레이어 마스크 `Built`, 플레이어와 시체·흩어진 것이 놓이는 레이어 `Passed`), 기능이 스스로 남기는 플레이 기록([PlayRecord](Common/PlayRecord.cs): `Logs/<이름>.txt`, 에디터와 개발 빌드), 색을 글로 쓰고 읽기([ColourCode](Common/ColourCode.cs): `#RRGGBB` 여섯 자리만), 색이 눈에 얼마나 밝은지와 어두운 바탕에서 읽히는 색([ColourWeight](Common/ColourWeight.cs): 플레이어 색과 색상 판이 같이 쓴다), 플레이 없이 도는 규칙 점검의 보고서([CheckReport](Common/CheckReport.cs): 첫 줄 `ALL KEPT` 또는 `N BROKEN`, 규칙마다 ok·FAIL 줄. 에디터 전용) |
| [Loading](Loading/) | 6 | 로딩 화면과 첫 사용 준비 |
| [Mods](Mods/) | 7 | Mods 폴더 읽기([GameMods](Mods/GameMods.cs)), 모드 캐릭터 불러오기([ModAvatars](Mods/ModAvatars.cs), 파일 형식 [ModAvatarFile](Mods/ModAvatarFile.cs)), 캐릭터와 옷의 모델이 지닐 수 있는 부품의 한 목록([WornParts](Mods/WornParts.cs): 뼈·Animator·메시·LOD·컨스트레인트만 남기고 콜라이더·스크립트·조명·소리는 뗀다. 게임이 입힐 때와 SDK가 내보낼 때 같이 쓴다), 모드 옷을 옷장에 올리기([ModOutfits](Mods/ModOutfits.cs)), 그 재질([ModMaterials](Mods/ModMaterials.cs), [ModShaderLibrary](Mods/ModShaderLibrary.cs)). 설명은 [모드 만들기](../../../../Docs/Modding.md) |
| [Scene](Scene/) | 4 | 실행 중 하이어라키 폴더, 대기 카메라, 씬에 하나뿐인 것 찾기([SceneOne](Scene/SceneOne.cs)), 구운 프로브 빛이 안 읽힐 때 매 프레임 예외 대신 끄기([ProbeLightGuard](Scene/ProbeLightGuard.cs)) |
| [Settings](Settings/) | 14 | 저장되는 설정, 키 설정(주제별 파일), 화면 |
| [Time](Time/) | 2 | 게임 속도의 주인([WorldTime](Time/WorldTime.cs))과 일시정지([GamePause](Time/GamePause.cs)) |
| [Tables](Tables/) | 4 | CSV 표 읽기와 적용 (GameTables, CsvTable, TableCells, TableFieldWriter) |
| [Texts](Texts/) | 2 | 화면 글자 (Texts, TextKey) |

## 무엇을 고치려면 어디를

| 변경할 내용 | 시작할 파일·설정 |
| --- | --- |
| 표 읽기·적용 방식 / 편집기 도구(BOOTH 가져오기·Grip Tuner)가 표 쓰기(파일을 자르지 않고 제자리에 덮어써서 다른 프로그램이 매핑해 둬도 써짐, 막히면 기다렸다가 저절로, 1분 넘게 막히면 잡고 있는 프로그램을 한 번 알림) | [GameTables](Tables/GameTables.cs), [CsvTable](Tables/CsvTable.cs), [TableFieldWriter](Tables/TableFieldWriter.cs) / [TableFile](../../Editor/Tables/TableFile.cs) |
| 화면 문구 (메뉴·설정·HUD·안내·알림·처치 목록·좀비 이름 등 플레이어가 읽는 글자는 모두 id로 표에서 읽는다. 열 하나가 언어 하나라 번역판은 `en` 열만 채우면 되고, 비어 있으면 한국어가 나온다. `{0}`·`{1}`은 코드가 채우는 자리, `\n`은 줄바꿈, 리치 텍스트 태그 사용 가능. 표에 없는 id는 `[id]`로 보인다. 개발자 콘솔(`/` 명령) 도움말·디버그 로그·/net 같은 개발용 글자는 코드에 남긴다. 2026-11-11 플레이 테스트 의견. 2026-11-12부터 표의 글자 칸(이야기 카드·캠페인 목표·맵·메뉴 소식·클래스·능력 카드·킬 피드 도장·아이템·상점·옷·크로스헤어 프리셋·콜아웃 대사·특수 순간 글자)도 Texts id를 담아 모든 글자가 한 파일에 있다. 칸에 글자를 쓰면 그대로 나온다(모드). 고치는 곳은 ENFILADE → 글자 → 글자 편집기(찾기·구역·ko/en 나란히·쓰는 곳·고친 칸만 제자리 저장·Enter 줄바꿈·`{0}`과 체형 고르기 칸 `|` 경고·Play 중 떠 있는 글자 바로 바뀜·콜아웃 목소리 다시 만들기. 이야기 카드는 줄마다 한 줄씩, 줄 수 자유(`Texts.Lines`), 제목·이야기·결과는 글자 높이만큼 쌓여 겹치지 않는다(CampaignScreen)), 코드·표·프리팹에 바로 쓴 글자는 ENFILADE → 글자 → 하드코딩 검사) | [Texts](Texts/Texts.cs) (`Texts.Get`·`Texts.Format`, 표 칸은 `Texts.Cell`), [Texts.csv](../../Data/Resources/Tables/Core/Texts.csv) (id·ko·en·#어디), 프리팹 글자 [TextKey](Texts/TextKey.cs), 편집기 [TextEditorWindow](../../Editor/Texts/TextEditorWindow.cs)·[TextSheet](../../Editor/Texts/TextSheet.cs)·[CsvSpans](../../Editor/Tables/CsvSpans.cs)·[TextLiveSwap](../../Editor/Texts/TextLiveSwap.cs)·[CalloutVoices](../../Editor/Texts/CalloutVoices.cs), 검사 [TextScan](../../Editor/Texts/TextScan.cs) |
| 창이 뒤에 있을 때의 기록 (2026-11-18, 사용자: "방장(호스트) 알트탭 시 허브 및 맵 전환 진행이 멈추는 현상". 게임 코드는 포커스를 잃어도 아무것도 멈추지 않는다(Run In Background 켜짐). 방의 씬 로딩은 Netcode가 유니티 기본 우선순위(프레임마다 몇 ms)로 읽고 있어서, 창이 다른 창 뒤에서 프레임이 줄면 로딩이 거의 멈춘다(프레임이 준다는 것은 추론). 그래서 방의 로딩도 혼자 할 때와 같은 우선순위(`Loading.csv` `Load.priority`)로 읽게 했고(`SceneWarmup`), 창이 뒤에 있는 동안 5초마다 `[Focus] behind another window: 12 frames in 5.0 s (2 a second), longest 800 ms, scene 'Hub', hosting` 한 줄을 남겨 다음 플레이에서 실제로 프레임이 주는지 읽을 수 있게 했다. 플레이어의 몸이 포커스를 잃으면 통째로 멈추는 것(`PlayerMovement`)은 이동·입력 쪽 일이다) | [FocusPulse](Settings/FocusPulse.cs), [SceneWarmup](Loading/SceneWarmup.cs) |
| 로딩 화면과 첫 사용 준비 (씬을 바꿀 때마다 화면이 덮고, 씬을 뒤에서 읽고, 전투가 처음 그릴 것을 카메라 앞에서 한 번씩 그린 뒤 걷힌다. 씬 이동은 `SceneLoad.Go`, 준비 완료는 `SceneWarmup.Ready`·`IsReady`·`WhenReady`, 빌드의 그래픽 상태 기록은 콘솔 `pso`) | [Loading](Loading) 폴더: [LoadingScreen](Loading/LoadingScreen.cs)·[SceneLoad](Loading/SceneLoad.cs)·[SceneWarmup](Loading/SceneWarmup.cs)·[CombatRehearsal](Loading/CombatRehearsal.cs)·[PsoWarmup](Loading/PsoWarmup.cs)·[ZombieLookTemplates](Loading/ZombieLookTemplates.cs)(좀비가 복사할 모습 원본, 팩 통째가 아니라 입은 캐릭터만), 수치는 [Loading.csv](../../Data/Resources/Tables/Core/Loading.csv), [사용법](../../../../Docs/Guides/MaintenanceGuide.md#로딩-화면과-첫-사용-준비-loadingcsv) |
| **체력·무기·주먹·처형·적·웨이브·이동·몸·소리 숫자** | [Data/Resources/Tables](../../Data/Resources/Tables)의 CSV 표 (컴파일 불필요, [사용법](../../../../Docs/Guides/MaintenanceGuide.md#밸런스-표-data-tables)) |
| 완벽 방어·흐트러짐·처형의 표시·사운드 | [Feedback.asset](../../Data/Resources/Combat/Feedback.asset) |
