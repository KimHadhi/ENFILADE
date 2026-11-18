# Editor — 편집기 도구

Unity 편집기에서만 도는 도구다. 메뉴는 모두 메뉴 막대의 **ENFILADE** 아래에 있다. 게임 코드는 [스크립트 길잡이](../Scripts/README.md), 프로젝트 구조는 [구조 안내](../../../Docs/Architecture.md)에 있다.

| 폴더 | 메뉴 | 하는 일 |
| --- | --- | --- |
| `Maps` | ENFILADE → 맵 | 허브 로비 짓기, 캠페인 1~4장과 모든 장 다시 짓기, 엔필레이드 타워 짓기, 맵 사진 찍기, 문 길찾기 다시 굽기. 맵은 [GreyboxBuilder](Maps/GreyboxBuilder.cs)가 표(`Tables/Maps`)를 읽어 짓는다. 단단한 소품이 설 자리는 빌더가 정한다([GreyboxBuilder.Standing](Maps/GreyboxBuilder.Standing.cs)): 배치 코드는 종류와 자리(`PropSolid`)나 방과 벽(`Furnish`)만 말하고, 빌더가 모델을 재서 벽·바닥·천장·다른 소품·비워 둘 길(`KeepClear`: 문, 바닥 구멍)과 겹치지 않게 세우거나 세우지 않는다. 다시 지을 때마다 옮긴 것·세우지 않은 것·아직 겹치는 것이 `Logs/MapAudit/<씬>.txt`와 닫는 창에 나온다. 같은 파일에 맵 검사가 모인다([GreyboxBuilder.Audit](Maps/GreyboxBuilder.Audit.cs)): 도착 자리가 무엇 속에 묻혔는지, 메시 없는 메시 콜라이더, 산 장면(팩) 건물에서 보이는데 막지 않는 벽·유리·받침, 뒤에서 지나가는 벽, 총알이 모양에 맞는 소품(MapArt `shots` = `mesh`)의 상자에서 모델을 비껴가는 광선의 몫. 플레이 중에 움직이는 연출의 부품은 `MovingPart`로 만든다(키네마틱 몸 + 제 공간에 입힌 재질): 크기를 바꾸지 않고 옮기기만 하며, 움직임은 빌더가 궤적으로 적어 둔다(SetPieceTrack). 산 장면에서 지으며 만든 메시(자른 벽, 뒤집은 면)는 다 지은 뒤 한 번, 메시마다 제 파일로 저장한다([GreyboxBuilder.PackScene](Maps/GreyboxBuilder.PackScene.cs) `WritePackMeshes`) |
| `Characters` | ENFILADE → 캐릭터 | BOOTH 다시 가져오기([Booth](Characters/Booth)), 옷 목록 다시 만들기, 옷장 사진 모두 굽기, 몸 프로필 자동 생성, 승리 포즈 가져오기. 같은 메뉴의 옷장 검사대는 게임의 클래스로 입혀 보기 때문에 스크립트 쪽에 있다(`Scripts/Player/Outfits`의 WardrobeBench: 모든 캐릭터에게 모든 옷을 입혀 보고 `Logs/WardrobeBench`에 적는다) |
| `Guns` | ENFILADE → 총 | 데스페라도 만들기, FPS Animation Pack·Tactical Shooter Pack 총 만들기, KINEMATION 총 클립 다시 굽기, Grip Tuner. 같은 메뉴의 쥐기 점검, 총 동작 굽기, 검사대는 게임의 클래스로 재기 때문에 스크립트 쪽에 있다(`Scripts/Weapons/FPSBody`의 GunHoldProbe, GunTrackBake, GunBench) |
| `Tables` | ENFILADE → 표 | 표 폴더 열기, 표 다시 읽기. 표를 저장하면 실행 중인 게임에 바로 넣고([GameTablesWatcher](Tables/GameTablesWatcher.cs)), 도구가 표를 쓸 때는 [TableFile](Tables/TableFile.cs)로 쓰고, 표의 자리는 이름으로 찾는다([TablePaths](Tables/TablePaths.cs)) |
| `Texts` | ENFILADE → 글자 | 글자 편집기, 하드코딩 검사, 콜아웃 목소리 상태 |
| `Fonts` | ENFILADE → 글꼴 | 글꼴 검사, 역할 맞추기, 동적 글꼴 비우고 저장 |
| `Hud` | ENFILADE → HUD | 현재 씬에 HUD 넣기·갱신, HUD 프리팹 열기, 겹침 검사 |
| `Models` | ENFILADE → 모델 | 임포트 규칙 다시 적용([ModelImportRules](Models/ModelImportRules.cs): 폴더에 따라 모델·클립 임포트 설정을 자동으로), 처형 클립 가져오기 메시지, 물리 소품 프리팹 만들기 |
| `Models` | ENFILADE → 좀비 | 클립 폴더 다시 읽기 |
| `Melee` | ENFILADE → 근접 | 암살·처형 규칙 보고서([MeleeReportFiles](Melee/MeleeReportFiles.cs): 컴파일할 때와 표를 저장할 때마다 `Logs/execution-rules.txt`를 스스로 다시 쓴다. 게임이 묻는 판정 함수 그대로 만든 것이라 플레이하지 않고 읽어서 확인한다), 칼 휘두르기 보고서(`Logs/knife-swings.txt`: 칼마다 스탯, 클립의 칼날 구간·다음 시작점, 탭·홀드·연타가 그 칼의 타임라인에서 무엇이 되는지) |
| `Audio` | (자동) | `Art/Audio`의 폴더를 소리 묶음으로 만든다 |
| `Build` | (자동) | 빌드하면 프로젝트의 `Mods` 폴더를 실행 파일 옆에 복사하고, 아바타 내보내기 도구를 `Mods/_SDK`에 넣는다 |
| `ModSdk` | ENFILADE → 모드 | 아바타를 모드로 내보내기·옷을 모드로 내보내기([ModAvatarExporter](ModSdk/ModAvatarExporter.cs), 창 하나의 두 탭): 휴머노이드 캐릭터(VRChat 아바타)나 그 캐릭터용 옷을 번들과 `.avatar.json`으로 싸서 `Mods/<id>/Avatars`에 낸다. 메시를 옷장 칸으로 나누는 규칙([ModPieces](ModSdk/ModPieces.cs))도 여기 있고, 게임의 BOOTH 가져오기(`Characters/Booth`)가 같은 것을 쓴다. 이 폴더와 `Scripts/Core/Mods/ModAvatarFile.cs`가 제작자에게 주는 SDK의 전부라, **게임의 다른 코드를 쓰면 안 된다**(VRChat용 Unity 2022 프로젝트에서도 컴파일돼야 한다). 옷 칸 목록(`ModPieces.Slots`)은 OutfitSlots.csv와 같게 둔다(SDK 폴더를 만들 때 다르면 알려 준다). 설명은 [모드 만들기](../../../Docs/Modding.md)의 "아바타 모드" |
| `Mods` | ENFILADE → 모드 | 모드 SDK 폴더 만들기([ModSdkPackage](Mods/ModSdkPackage.cs): `ModSdk`의 스크립트와, 게임과 같이 쓰는 두 파일 ModAvatarFile.cs(파일 형식)·WornParts.cs(입는 것이 지닐 수 있는 부품)를 담는다), 모드용 쉐이더 목록 다시 만들기([ModShaderLibraryBuilder](Mods/ModShaderLibraryBuilder.cs): lilToon의 모든 변형을 빌드에 넣는 목록. 스크립트를 불러올 때 스스로 맞춘다) |
| `Dev` | (자동) | `Temp/enfilade-refresh.request` 파일이 생기면 열려 있는 Unity가 스스로 새로 고친다(창을 누르지 않아도 됨. 실행·빌드 중에는 기다린다). 밖의 도구가 고친 코드를 바로 컴파일시킬 때 쓴다. 컴파일과 스크립트 다시 읽기가 끝날 때마다, 디스크에는 있는데 어느 어셈블리에도 들지 않은 우리 스크립트(`Assets/_Project`)를 찾아 한 번 다시 가져오고 콘솔에 알린다([MissedScripts](Dev/MissedScripts.cs)). 편집기가 컴파일하는 사이에 생긴 새 파일을 놓치면, 코드는 멀쩡한데 그 파일에 있는 이름마다 CS0103이 나고 편집기를 다시 켤 때까지 그대로였다 |

새 도구를 더할 때: 맞는 폴더에 두고 메뉴는 `ENFILADE/<분야>/<하는 일>`로 단다. 표를 읽고 쓰는 도구는 경로를 적지 말고 `TablePaths.Of("표 이름")`을 쓴다.
