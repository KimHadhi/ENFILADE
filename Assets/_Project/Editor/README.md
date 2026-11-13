# Editor — 편집기 도구

Unity 편집기에서만 도는 도구다. 메뉴는 모두 메뉴 막대의 **ENFILADE** 아래에 있다. 게임 코드는 [스크립트 길잡이](../Scripts/README.md), 프로젝트 구조는 [구조 안내](../../../Docs/Architecture.md)에 있다.

| 폴더 | 메뉴 | 하는 일 |
| --- | --- | --- |
| `Maps` | ENFILADE → 맵 | 허브 로비 짓기, 캠페인 1~4장과 모든 장 다시 짓기, 엔필레이드 타워 짓기, 맵 사진 찍기, 문 길찾기 다시 굽기. 맵은 [GreyboxBuilder](Maps/GreyboxBuilder.cs)가 표(`Tables/Maps`)를 읽어 짓는다 |
| `Characters` | ENFILADE → 캐릭터 | BOOTH 다시 가져오기([Booth](Characters/Booth)), 옷 목록 다시 만들기, 옷장 사진 모두 굽기, 몸 프로필 자동 생성, 승리 포즈 가져오기 |
| `Guns` | ENFILADE → 총 | 데스페라도 만들기, FPS Animation Pack·Tactical Shooter Pack 총 만들기, KINEMATION 총 클립 다시 굽기, Grip Tuner |
| `Tables` | ENFILADE → 표 | 표 폴더 열기, 표 다시 읽기. 표를 저장하면 실행 중인 게임에 바로 넣고([GameTablesWatcher](Tables/GameTablesWatcher.cs)), 도구가 표를 쓸 때는 [TableFile](Tables/TableFile.cs)로 쓰고, 표의 자리는 이름으로 찾는다([TablePaths](Tables/TablePaths.cs)) |
| `Texts` | ENFILADE → 글자 | 글자 편집기, 하드코딩 검사, 콜아웃 목소리 상태 |
| `Fonts` | ENFILADE → 글꼴 | 글꼴 검사, 역할 맞추기, 동적 글꼴 비우고 저장 |
| `Hud` | ENFILADE → HUD | 현재 씬에 HUD 넣기·갱신, HUD 프리팹 열기, 겹침 검사 |
| `Models` | ENFILADE → 모델 | 임포트 규칙 다시 적용([ModelImportRules](Models/ModelImportRules.cs): 폴더에 따라 모델·클립 임포트 설정을 자동으로), 처형 클립 가져오기 메시지, 물리 소품 프리팹 만들기 |
| `Models` | ENFILADE → 좀비 | 클립 폴더 다시 읽기 |
| `Audio` | (자동) | `Art/Audio`의 폴더를 소리 묶음으로 만든다 |
| `Build` | (자동) | 빌드하면 프로젝트의 `Mods` 폴더를 실행 파일 옆에 복사한다 |
| `Dev` | (자동) | `Temp/enfilade-refresh.request` 파일이 생기면 열려 있는 Unity가 스스로 새로 고친다(창을 누르지 않아도 됨. 실행·빌드 중에는 기다린다). 밖의 도구가 고친 코드를 바로 컴파일시킬 때 쓴다 |

새 도구를 더할 때: 맞는 폴더에 두고 메뉴는 `ENFILADE/<분야>/<하는 일>`로 단다. 표를 읽고 쓰는 도구는 경로를 적지 말고 `TablePaths.Of("표 이름")`을 쓴다.
