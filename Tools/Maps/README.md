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
| `hull_vs_shape.py <모델.fbx> …` | 모델의 충돌 상자 가운데 총알에게 허공인 몫: 상자의 네 옆면과 윗면으로 0.25 m 간격의 광선을 쏴서 모델의 삼각형을 하나도 못 만나는 비율. 게임의 판정([ShotShape](../../Assets/_Project/Scripts/World/ShotShape.cs))과 같은 삼각형 검사·잎 묶음을 옮긴 것이고, 모든 삼각형을 다 보는 검사와 맞는지도 함께 확인한다 |
| `find_boxes.py <씬> 낱말 …` | 이름에 낱말이 든 상자의 자리·크기·방향 |
| `navmesh.py` | 구운 걷기 메시(`Scenes/Campaign/<씬>/NavMesh.asset`, 바이너리)를 읽는 모듈: 타일의 폴리곤, 타일 사이 이음, 낙하·건너뛰기 링크(한쪽 방향), 그 위의 길찾기(폴리곤 통로 + 깔때기). 엔진의 길찾기와 같은 것은 아니라서 길이가 몇 m 다를 수 있다. 혼자 돌리면 그 파일의 타일·폴리곤·링크 수를 적는다 |
| `flow.py [씬 이름 …]` | 좀비가 나오는 자리의 모델([Spawns.md](../../Docs/Features/Spawns.md))을 맵마다 읽는다: 임무 경로(Start에서 각 단계의 자리까지)를 걷기 메시 위에 놓고, 구간마다 흐름과 걷기 메시가 잇지 못하는 곳, 스폰 지점마다 경로의 어디에 붙는지를 `Logs/spawn-flow.txt`에 적는다. 볼 것은 `FINDING` 줄. 게임의 SpawnRoute(Lay·Place)를 줄대로 옮긴 것이라 그쪽을 바꾸면 여기도 같이 바꾼다. 게임이 스스로 쓰는 기록은 `Logs/spawns.txt`(편집기에서 장을 시작하면) |
| `sim.py [씬 이름 …]` | 단계마다 분대를 그 구간의 8 m마다 세워 보고, 표(SpawnRules.csv)의 규칙대로 쓸 수 있는 스폰 지점이 몇 개인지 `Logs/spawn-sim.txt`에 적는다(`NOTHING` = 그 자리에서는 떼가 기다린다). 시야는 읽지 않으므로 실제로는 이보다 적다. 뒤쪽 구간은 모두 소탕된 것으로 친다(가장 나쁜 경우) |
| `walk_joins.py [씬 이름 …]` | 임무 경로와 구운 걷기 메시: 단계마다 앞 자리에서 그 자리까지 걸어서 이어지는지, 링크(내려뛰기·건너뛰기)로만 이어지는지, 끊겼는지를 적고, 끊긴 곳마다 메시의 두 쪽이 가장 가까운 자리와 그 사이에 선 것을 적는다(열려 있는데 메시만 끊김: 몸이 지나갈 폭 / 넘어야 하는 낮은 것: 높이 / 높이 차 / 벽). 스폰 지점마다 걷기 메시 위인지, 단단한 것 속인지, 경로의 땅으로 갈 수 있는지도 적는다. `Logs/walk-joins.txt`. 자리와 지점은 공간에서 가장 가까운 메시에 세우고(`navmesh.locate`는 바로 위·아래 층의 폴리곤을 먼저 고른다), 높이는 메시의 디테일 삼각형에서 읽는다. `--radius 0.28 --voxel 0.1`을 붙이면 그 반지름으로 다시 구웠을 때를 예측한다(`Logs/walk-joins-predicted.txt`): 열려 있는데 끊긴 곳마다 양쪽 면을 굽기의 복셀 눈금에 놓고 반지름만큼 깎아 남는 칸을 센다(둘 이상이면 이어짐). `--hop 이름`은 그 이름의 상자마다 넘는 링크를 놓고, `--without 이름`은 그 이름의 상자를 빼고 본다(다시 지으면 빌더 규칙이 문 앞에서 치우는 소품). 메시 콜라이더는 읽지 않는다(4장 터미널 안) |
| `cage_crowd.py [씬 이름 …] [--held N]` | 철창에 갇힌 무리(GimmickCage)가 다 설 수 있는지: 씬에서 철창의 칸·갇힌 수·놓이는 구간을 읽고, 게임과 같은 규칙으로 무리의 가운데와 반경을 구해(칸 가운데, 창살에서 0.9 m 안쪽), 구운 걷기 메시 위에 게임이 세우는 방식 그대로(수 × 8번 시도, 2 m 안의 걷기 메시, 1.1 m 간격) 2,000번 세워 본다. 씬마다 지워진 엎드린 무리 자리(`SleeperNest`, '잠든 무리 N')가 몇 개 남았는지도 센다(다시 지으면 0). 모두 맞으면 마지막 줄이 `OK`. `--held N`은 철창이 갇힌 수를 적기 전에 지은 씬을 칸마다 N으로 보고 잰다. 몸이 들어갈 틈(물리)과 플레이어와의 거리는 재지 않는다 |
| `fbx_size.py <파일.fbx> …` | 바이너리 FBX(6.x, 7.x)의 메시 크기를 임포트 배율(.meta)까지 곱해 적는다. 블렌더가 못 여는 FBX 6.1도 읽는다. 모델을 표에 넣기 전에 방에 들어가는지 볼 때 쓴다 |

## 돌리는 법

```
python Tools/Maps/prop_crossings.py Assets/_Project/Scenes/Campaign/Ch1_Rooftops.unity
python Tools/Maps/settle_check.py Assets/_Project/Data/Resources/Tables/Maps/MapArt.csv Assets/_Project/Scenes/Campaign/Ch1_Rooftops.unity
python Tools/Maps/walk_probe.py Assets/_Project/Scenes/Campaign/Ch1_Rooftops.unity 98,0,0 91.6,0,0 91.6,0,-3 88.55,0,-5.6 88.55,0,-12.3 88.2,-3,-13
python Tools/Maps/fbx_size.py Assets/ThirdParty/Airport_Terminal_Scene/3dobjects_props/kuehlschrank.fbx
python Tools/Maps/flow.py
python Tools/Maps/sim.py Ch1_Rooftops
python Tools/Maps/cage_crowd.py
python Tools/Maps/walk_joins.py
python Tools/Maps/walk_joins.py --radius 0.28 --voxel 0.1 Ch1_Rooftops
```

한글이 깨지면 `PYTHONIOENCODING=utf-8`을 앞에 붙인다.

## 2026-11-18에 잰 것 (11-16에 지은 씬)

- 1장: 단단한 소품 134개 중 55개가 벽·바닥·천장과 겹쳤다. 다시 지으면 0이어야 한다(`prop_crossings.py`의 마지막 줄 `crossing: 0`).
- 1장 지하 계단: 복도 → 104호 문 → 계단 → 지하 배전반까지 캡슐이 막힘 없이 지나간다(`walk_probe.py`, 위 예시의 길).
- 4장(11-15에 지은 씬): 터미널 벽 충돌 메시(colmesh)의 MeshCollider에 메시가 없었다(씬 파일의 `m_Mesh` 덮어쓰기가 fileID 0). 다시 지으면 `Logs/MapAudit/Ch4_Airport.txt`의 '메시 없는 것 0'이어야 한다. 팩의 충돌 메시는 걷는 높이의 벽 2,534 m²를 모두 덮고, 유리 한 장(fenster_05, 30 m²)만 덮지 않는다(`shell_cover.py`).
- 차 상자의 허공: 소형 세단 34%, 경찰 세단 32%, 승합차 22%, 픽업 28%(`hull_vs_shape.py`, 빌더가 올린 상자 기준).
- 철창 무리(3장, 11-15에 지은 씬의 걷기 메시, `cage_crowd.py Ch3_Subway --held 4`): 두 칸 모두 무리의 원 안이 전부 걷기 메시 위(0.1 m 격자 2,053점·1,513점), 2,000번 세워 2,000번 모두 넷이 다 섰고 칸 밖에 선 놈 0. 걷기 메시는 벽·창살에서 0.53 m 안쪽부터 있다. 엎드린 무리 자리는 1장 21·2장 12·3장 16(갇힌 무리 둘 포함)·4장 5개가 씬에 남아 있다: 다시 지으면 0이어야 한다.
- 경로와 걷기 메시(11-15·16에 구운 메시, 반지름 0.5 m, `walk_joins.py`): 열려 있는데 메시만 끊긴 구간이 1장 Ch1.13(아파트 계단참, 폭 1.2 m), 2장 Ch2.04·09(휴게소 건물 문)·Ch2.06·07(검문소 초소 문)·Ch2.11(톨게이트 사무실: 문 안쪽에 사물함이 서 있어 0.52 m만 남음). 3장 Ch3.05는 강 밑 터널의 배관 셋(바닥에서 0.92 m, 좀비 걸음 0.75 m보다 높음)에서 끊김. 열려 있는데 끊긴 곳은 모두 1장 22·2장 5·3장 5곳. 예측(`--radius 0.28 --voxel 0.1`): 모두 이어짐. 같은 계산을 구운 반지름 0.5 m에 대면 0곳이 이어져 지금의 굽기와 맞는다.
- 모델 크기: 냉장 진열대 kuehlregal_4er 4.00 × 2.50 × 1.23 m, 냉장고 kuehlschrank 1.20 × 2.50 × 0.86 m, 조명탑 발전기 3.97 × 4.17 × 1.58 m(씬의 상자와 FBX가 같은 값).
