# 맵 검사 (Unity 없이)

지은 맵의 씬 파일(`Assets/_Project/Scenes/**.unity`)만 읽어서 맵을 검사하는 파이썬 스크립트들. Unity를 켜지 않고, 플레이하지 않고도 '다시 지은 결과'를 확인하려고 2026-11-18에 만들었다(사용자: "망가진 맵 오브젝트 배치 복구"). 씬은 읽기만 한다.

빌더가 다시 지을 때 스스로 쓰는 보고서는 `Logs/MapAudit/<씬>.txt`다([GreyboxBuilder.Standing](../../Assets/_Project/Editor/Maps/GreyboxBuilder.Standing.cs)). 여기 스크립트는 그 결과를 씬 파일에서 따로 다시 재는 것이다.

| 스크립트 | 하는 일 |
|---|---|
| `unity_scene.py` | 씬 YAML을 읽는 모듈: 게임 오브젝트, 월드 좌표의 트랜스폼, 컴포넌트. 씬에 직접 적힌 오브젝트만 자리를 안다(프리팹 사본의 안쪽은 프리팹에 있다) |
| `prop_crossings.py <씬>` | 단단한 소품(이름이 `<종류> (충돌)`인 안 보이는 상자)마다 자리·크기·방향을 적고, 벽·바닥·천장 상자와 겹치면 `CROSSES 무엇(깊이)`를 붙인다. 뒤에 `x0 x1 y0 y1 z0 z1`을 주면 그 범위 안의 모든 단단한 상자를 적는다 |
| `standing.py` | 빌더의 '단단한 소품이 설 자리' 규칙(GreyboxBuilder.Standing.cs의 `Overlap`·`Crosses`·`Settle`·`Furnish`)을 줄대로 옮긴 모듈. 빌더의 규칙을 바꾸면 여기도 같이 바꾼다 |
| `settle_check.py <MapArt.csv> <씬>` | 그 씬에 이미 서 있는 소품에 빌더의 규칙을 적용하면 무엇이 옮겨지고 거절되는지 미리 계산한다(다시 짓기 전에 무슨 일이 일어날지). `PropSolid`로 세운 것만 맞다: 허브의 기둥처럼 다른 길로 세운 상자도 같은 이름이면 섞여 나온다 |
| `walk_probe.py <씬> x,y,z x,y,z …` | 플레이어 캡슐(반지름 0.3 m, 키 1.75 m)을 그 길로 걸려 보고, 바닥이 없거나 상자 속에 들어가거나 턱이 0.3 m보다 높은 곳을 `TROUBLE`로 적는다. 상자와 경사판만 읽는다(메시 콜라이더는 안 읽으므로 4장 터미널 안은 못 본다) |
| `reach_check.py <CampaignSteps.csv> <씬> …` | 그 장의 도착(Reach) 단계 자리마다: 자리의 가슴 높이가 상자 속에 묻혔는지, 반경 안에 설 수 있는 자리 중 몇 곳에서 그 자리가 트여 보이는지(게임의 도착 규칙 [CampaignRun.Reach](../../Assets/_Project/Scripts/Modes/Campaign/CampaignRun.Reach.cs)와 같은 잣대), 가리는 것의 이름. 상자만 읽는다 |
| `unity_mesh.py` | 글로 저장된 Unity 메시(.asset)의 꼭짓점과 삼각형을 읽는 모듈. 빌더가 씬 옆에 두는 메시(`Scenes/Campaign/<씬>/Pack`)를 읽는다 |
| `shell_cover.py <Pack 폴더> --collision … --seen …` | 산 장면(팩) 건물의 보이는 벽·유리 중 충돌 메시가 덮지 않는 곳을 걷는 높이에서 m²로 잰다(자리별로도). 빌더가 지을 때 물리로 재는 것(`Logs/MapAudit`의 '보이는데 막지 않는 껍데기')을 저장된 메시에서 따로 재는 것 |
| `find_boxes.py <씬> 낱말 …` | 이름에 낱말이 든 상자의 자리·크기·방향 |
| `fbx_size.py <파일.fbx> …` | 바이너리 FBX(6.x, 7.x)의 메시 크기를 임포트 배율(.meta)까지 곱해 적는다. 블렌더가 못 여는 FBX 6.1도 읽는다. 모델을 표에 넣기 전에 방에 들어가는지 볼 때 쓴다 |

## 돌리는 법

```
python Tools/Maps/prop_crossings.py Assets/_Project/Scenes/Campaign/Ch1_Rooftops.unity
python Tools/Maps/settle_check.py Assets/_Project/Data/Resources/Tables/Maps/MapArt.csv Assets/_Project/Scenes/Campaign/Ch1_Rooftops.unity
python Tools/Maps/walk_probe.py Assets/_Project/Scenes/Campaign/Ch1_Rooftops.unity 98,0,0 91.6,0,0 91.6,0,-3 88.55,0,-5.6 88.55,0,-12.3 88.2,-3,-13
python Tools/Maps/fbx_size.py Assets/ThirdParty/Airport_Terminal_Scene/3dobjects_props/kuehlschrank.fbx
```

한글이 깨지면 `PYTHONIOENCODING=utf-8`을 앞에 붙인다.

## 2026-11-18에 잰 것 (11-16에 지은 씬)

- 1장: 단단한 소품 134개 중 55개가 벽·바닥·천장과 겹쳤다. 다시 지으면 0이어야 한다(`prop_crossings.py`의 마지막 줄 `crossing: 0`).
- 1장 지하 계단: 복도 → 104호 문 → 계단 → 지하 배전반까지 캡슐이 막힘 없이 지나간다(`walk_probe.py`, 위 예시의 길).
- 4장(11-15에 지은 씬): 터미널 벽 충돌 메시(colmesh)의 MeshCollider에 메시가 없었다(씬 파일의 `m_Mesh` 덮어쓰기가 fileID 0). 다시 지으면 `Logs/MapAudit/Ch4_Airport.txt`의 '메시 없는 것 0'이어야 한다. 팩의 충돌 메시는 걷는 높이의 벽 2,534 m²를 모두 덮고, 유리 한 장(fenster_05, 30 m²)만 덮지 않는다(`shell_cover.py`).
- 모델 크기: 냉장 진열대 kuehlregal_4er 4.00 × 2.50 × 1.23 m, 냉장고 kuehlschrank 1.20 × 2.50 × 0.86 m, 조명탑 발전기 3.97 × 4.17 × 1.58 m(씬의 상자와 FBX가 같은 값).
