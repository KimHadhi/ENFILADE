# World — 맵 조각

문, 사다리, 집라인, 철봉, 물리 소품, 표면 종류, 맵 목록과 색감·컬링.

전체 지도는 [스크립트 길잡이](../README.md), 프로젝트 구조는 [구조 안내](../../../../Docs/Architecture.md)에 있다.

## 무엇을 고치려면 어디를

| 변경할 내용 | 시작할 파일·설정 |
| --- | --- |
| 씬에 놓는 사다리·집라인 / 맵 빌더의 사다리 높이(난간 끝을 오르는 바닥에 맞춰 지을 때 잰다) | [Ladder](Ladder.cs), [Zipline](Zipline.cs), [사용 안내](../../../../Docs/Guides/MovementGuide.md#사다리집라인) / [LadderFit](../../Editor/Maps/LadderFit.cs) |
| 맵마다 색감 (11-13 사용자: "후처리 진행". 씬이 열릴 때 그 씬 줄로 맵 전체에 깔리는 볼륨을 만들어서 맵을 다시 짓지 않아도 되고, 에디터에서는 표를 저장하면 플레이 중에도 바뀐다. 처치 연출·저체력·사망·낙하 화면이 덮어쓰는 노출·대비·채도·색 필터·화이트 밸런스·블룸·비네트·렌즈 왜곡·색수차는 쓰지 않고 그림자와 밝은 곳에 섞는 색(스플릿 토닝), 18% 회색을 축으로 한 대비 곡선, 어두운 곳의 채도만 쓴다. 줄이 없는 허브·메뉴는 그대로) | [MapGrade](MapGrade.cs) (우선순위 -10 전역 볼륨, GameTables.Reloaded로 다시 만듦), 표: [MapGrade.csv](../../Data/Resources/Tables/Maps/MapGrade.csv), 문서: [조명 안내](../../../../Docs/Guides/LightingGuide.md) |
| 부서지는 문 (모든 PC에서 함께 부서짐, 서 있는 동안만 적의 길을 막음) | [BreakableDoor](BreakableDoor.cs), [DoorBreaks](DoorBreaks.cs), 길찾기 다시 굽기: ENFILADE > 맵 > 문 길찾기 다시 굽기 ([DoorNavMeshBaker](../../Editor/Maps/DoorNavMeshBaker.cs)) |
| 적이 듣는 소음 이벤트 | [Noise](Noise.cs) (좀비가 듣는다: [ZombieMind](../Enemies/ZombieMind.cs)) |
| 컬링 (메모 40: 캠페인 맵의 움직이지 않는 상자는 정적 배칭·가림(오클루전) 대상이고, 맵을 지을 때 가림 컬링을 굽는다. 좀비·플레이어·줍기 같은 움직이는 것도 벽과 건물 뒤면 그리지 않는다. 트인 곳은 레이어별 거리로 자른다: 좀비(Enemy) 160 m 밖은 그리지 않고 그림자도 없음, 카메라를 둘러싼 구로 재서 돌아봐도 튀지 않음, 조준경처럼 텍스처에 그리는 카메라는 그대로. 그림자·데칼 거리는 [조명 안내](../../../../Docs/Guides/LightingGuide.md#공통-설정)) | 굽기 [GreyboxBuilder](../../Editor/Maps/GreyboxBuilder.cs) MarkStatic·BakeOcclusion, 레이어 거리 [CameraCulling](CameraCulling.cs), 표: [Culling.csv](../../Data/Resources/Tables/Maps/Culling.csv) |
