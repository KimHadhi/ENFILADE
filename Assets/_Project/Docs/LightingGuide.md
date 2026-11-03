# 조명 안내

[프로젝트 안내](../../../README.md) · [설정·유지보수](MaintenanceGuide.md) · [외부 에셋 출처](AssetSources.md)

맵의 하늘, 해와 달, 조명 기구, 안개, 반사, 간접광을 맞추고 굽는 순서다. 모든 맵에 공통인 설정은 이미 들어가 있다. 맵마다 할 일은 [맵 하나 꾸미기](#맵-하나-꾸미기)에, 시험 맵 두 곳의 값은 [시험 맵 두 곳](#시험-맵-두-곳-밤-kloppenheim-02)에 있다.

## 공통 설정

| 위치 | 값 | 이유 |
| --- | --- | --- |
| [PC_RPAsset](../Settings/Rendering/PC_RPAsset.asset) | MSAA 4x | 물체 모서리의 계단 현상 |
| | 그림자 150 m, 4단계, 4096 | 가까운 그림자를 선명하게 |
| | Light Probe System: Adaptive Probe Volumes | 구운 간접광을 캐릭터와 좀비가 서 있는 자리에서 받는다. lilToon과 Poiyomi가 지원한다 |
| | Volume Profile: [GameLook](../Settings/Rendering/GameLook.asset) | 톤매핑 끔, Bloom 0.25(임계값 1), 비네트 0.2 |
| [PC_Renderer](../Settings/Rendering/PC_Renderer.asset) | SSAO 세기 0.7, 반경 0.4 | 구석과 접촉면을 어둡게 |
| [Player](../Resources/Characters/Player.prefab) 카메라 | SMAA High, 디더링 | 머리카락과 반짝이는 부분의 계단, 하늘과 안개의 띠 줄무늬 |

- 톤매핑을 꺼서 밝기 1을 넘는 부분은 하얗게 날아간다. 빛을 받은 흰 벽이 날아가면 그 빛의 세기를 낮춘다.
- 블룸은 밝기 1을 넘는 부분만 번진다. 빛나야 하는 것(전구, 형광등, 간판)은 재질의 발광 세기를 1보다 크게 준다.
- 처치 연출과 저체력 화면이 덮어쓰는 값(노출, 대비, 채도, 색 필터, 화이트 밸런스, 블룸, 비네트)은 맵 볼륨에서 바꾸지 않는다. 분위기는 빛과 안개의 색으로 잡는다. 이유는 [화면 후처리](MaintenanceGuide.md#화면-후처리)에 있다.

## 맵 하나 꾸미기

시작하기 전에 씬을 저장한다. 순서대로 한 뒤 마지막에 굽는다. Unity 6는 하늘을 바꿔도 굽기 전까지 환경광과 반사가 예전 그대로다.

### 1. 하늘

1. Poly Haven에서 HDRI를 `.hdr`로 받아 `Art/Sky/<이름>/`에 넣는다. 하늘만 있는 'Pure Sky' 종류가 맵 바닥과 겹치지 않아 좋다.
2. 텍스처 Inspector에서 Texture Shape `2D`, Max Size(4K는 4096, 8K는 8192), Compression `High Quality`로 두고 Apply.
3. 같은 폴더에 Material을 만든다. 이름은 `Sky_<이름>`, Shader는 `Skybox/Panoramic`이다. Spherical (HDR)에 텍스처를 넣고 Mapping `Latitude Longitude Layout`, Image Type `360 Degrees`, Rotation `0`으로 둔다. Exposure로 하늘 밝기를 맞춘다.
4. Window → Rendering → Lighting → **Environment** 탭에서 다음처럼 둔다.
   - Skybox Material: 3의 재질
   - Sun Source: 해나 달 조명
   - Environment Lighting Source: `Skybox`
   - Environment Reflections Source: `Skybox`, Resolution: `256`

### 2. 해나 달

씬의 Directional Light(`Sun`)를 다음처럼 둔다.

- Mode: `Mixed`. 직사광과 그림자는 실시간이고, 벽에 튕긴 빛만 굽는다.
- Light Appearance: `Filter and Temperature`. Temperature로 색을 정한다.
- Shadow Type: `Soft Shadows`
- Rotation: HDRI 속 해나 달의 방향에 맞춘다. 맞지 않으면 그림자가 하늘과 반대로 진다.

### 3. 안개

Environment 탭 → Other Settings에서 Fog를 켜고 Mode는 `Exponential`로 둔다. Color는 하늘의 지평선 색으로 한다(색 선택 창의 스포이트로 Game 창의 지평선을 찍어도 된다). Density는 0.005~0.02 사이에서 맞춘다. 안개는 하늘에는 칠해지지 않으므로 색이 다르면 지평선에 경계가 보인다.

### 4. 조명 설정 에셋

Lighting 창 **Scene** 탭의 Lighting Settings에서 New를 누르고, 만들어진 에셋을 `Settings/Rendering/Lighting/<맵 이름>.lighting`으로 옮긴다.

- Realtime Global Illumination: 끔
- Baked Global Illumination: 켬
- Lighting Mode: `Baked Indirect`
- Lightmapper: `Progressive GPU`

### 5. 굽기 대상 표시

움직이지 않는 벽, 바닥, 천장, 계단, 장애물을 고르고 Inspector 오른쪽 위 Static 목록에서 `Contribute GI`와 `Reflection Probe Static`을 켠다. 그다음 Mesh Renderer → Lighting → Receive Global Illumination을 `Light Probes`로 바꾼다. 라이트맵 대신 프로브에서 간접광을 받으므로, 메시에 라이트맵용 UV가 없어도 된다.

움직이거나 부서지는 것(`BreakableDoor`, 과녁, `Rigidbody`가 있는 물체)은 표시하지 않는다. 표시하지 않은 물체도 간접광은 받는다.

### 6. 조명 기구

1. 모델을 `Art/Models/Environment/<이름>/`에 넣는다. [3D 모델 추가하기](MaintenanceGuide.md#3d-모델-추가하기)의 규칙이 URP Lit 재질을 만든다.
2. Poly Haven의 텍스처 이름(`_diff`, `_nor_gl`, `_rough`)은 그 규칙이 알아보지 못한다. 모델 Inspector → Materials → Extract Materials로 재질을 꺼내서 직접 넣는다.
   - Base Map: `_diff`
   - Normal Map: `_nor_gl`. 텍스처 Inspector에서 Texture Type을 `Normal map`으로 바꾼다.
   - Emission: `_emission`이 있으면 Emission을 켜고 그 텍스처를 넣는다. 세기는 2 정도로 한다. 없으면 전구 재질에만 Emission을 켜고 색으로 준다.
   - 거칠기: URP Lit은 금속도와 매끄러움을 한 장(빨강 채널은 금속도, 알파 채널은 매끄러움)으로 받는다. Poly Haven은 `_metal`과 `_rough`를 따로 주므로 합친 텍스처가 필요하다. 합치기 전까지는 Smoothness 값만 조절한다.
3. 씬에 놓고, 빛이 나오는 자리에 자식으로 Light를 단다.
   - Type: `Spot`, Mode: `Mixed`, Rotation X: `90`(아래를 향함)
   - Shadow Type: `Soft Shadows`
   - 그림자를 켠 조명이 많으면 무거워진다. 프레임이 떨어지면 먼 조명부터 그림자를 끈다.
4. 하나를 다 맞추면 프리팹으로 만들어 복제한다.

### 7. 간접광 볼륨과 반사 프로브

- GameObject → Light → **Adaptive Probe Volume**을 만들고 Mode를 `Global`로 둔다. Lighting 창 **Adaptive Probe Volumes** 탭에서 Baking Mode를 `Single Scene`으로 둔다.
- GameObject → Light → **Reflection Probe**를 방이나 구역마다 하나씩 만든다. Type `Baked`, Resolution `256`으로 두고, Box Size를 방 크기에 맞춘다. 실내는 Box Projection을 켜고, 야외는 끈다.

### 8. 굽기

Lighting 창 오른쪽 아래의 **Generate Lighting**을 누른다. 씬 옆에 씬 이름의 폴더가 생기고 `LightingData.asset`, `ReflectionProbe-*.exr`, 프로브 볼륨 데이터가 들어간다. 모두 커밋한다. `LightingData.asset`은 바이너리라서 `.gitattributes`가 줄바꿈을 바꾸지 않게 이미 처리되어 있다.

구운 뒤 Console에 오류가 없는지, 벽 모서리에 얼룩이나 빛이 새는 곳이 없는지 본다. 얼룩이 있으면 그 자리에 Probe Adjustment Volume을 놓고 프로브를 무효로 만든다. 다시 구워야 하는 때는 하늘, 해나 달, `Mixed` 조명, 굽기 대상 물체가 바뀌었을 때다.

## 시험 맵 두 곳 (밤, Kloppenheim 02)

[전투 시험장](../Scenes/Development/CombatFoundation.unity)과 [이동 시험장](../Scenes/Development/MovementAbilityPlayground.unity)에 같은 밤하늘을 쓴다. 아래 숫자는 시작값이며, Play로 보면서 맞춘다.

### 재료 (모두 CC0)

| 무엇 | 받을 파일 | 크기 | 둘 곳 |
| --- | --- | --- | --- |
| 밤하늘 | [Kloppenheim 02 (Pure Sky)](https://polyhaven.com/a/kloppenheim_02_puresky) 4K `.hdr` | 21 MB | `Art/Sky/Kloppenheim02/` |
| 형광등 (전투 시험장) | [Mounted Fluorescent Lights](https://polyhaven.com/a/mounted_fluorescent_lights) FBX 2K | 7 MB | `Art/Models/Environment/MountedFluorescentLights/` |
| 가로등 (이동 시험장) | [Street Lamp 01](https://polyhaven.com/a/street_lamp_01) FBX 2K | 10 MB | `Art/Models/Environment/StreetLamp01/` |

받은 것은 같은 작업에서 [외부 에셋 출처](AssetSources.md)에 한 줄씩 적는다.

### 공통 값

| 항목 | 값 |
| --- | --- |
| 하늘 재질 Exposure | 0.2. 원본 밝기 그대로면 밤이 아니라 해 질 녘처럼 밝다 |
| 달(`Sun`) Rotation | X 17, Y -54.4, Z 0. HDRI 속 달의 위치(고도 17°)다 |
| 달 Temperature, Intensity | 9000 K, 0.3 |
| 달 그림자 Strength | 0.8 |

### 이동 시험장 (야외)

- **안개**: 켬, Color `#4B5158`, Exponential, Density 0.01
- **가로등 6개**: 바닥(가로 -20~20, 세로 -10~30) 가장자리 (±19.5, 0, -5), (±19.5, 0, 10), (±19.5, 0, 25)에 놓고 등이 안쪽을 향하게 돌린다.
  - 전구 재질 `street_lamp_01_bulb`: Emission 켬, 색 `#FFC27A`, 세기 3
  - Spot Light: 높이 3.6 m(등 안), Spot Angle 150, Range 15, Intensity 6, 3000 K
- **굽기 대상**: `★Map` 아래의 Floor, Stairs, Cube, Cube (2), Cube (3), Step 0.15·0.30·0.45, Vault rail, Mantle ledge, Wall climb lane, Wall climb finish, Roll drop platform, Swing launch platform, Surface pads 전부
- **빼는 것**: Door(`BreakableDoor`), Shove and tackle dummy(`Rigidbody`). Swing Bar, Ladder, Zipline은 프리팹이라 표시하지 않는다
- **반사 프로브 1개**: 위치 (0, 6, 10), Box Size (44, 12, 44), Box Projection 끔

### 전투 시험장 (실내)

천장과 벽으로 막혀 있어서 달빛은 거의 들어오지 않는다. 형광등이 주 조명이다.

- **안개**: 끔
- **형광등**: FBX를 놓으면 형광등 7개(a~g)가 나란히 나온다. `mounted_fluorescent_lights_a` 하나만 남겨 프리팹으로 만들고 복제한다.
  - FirstRoom 12개: x = -10, 0, 10과 z = -15, -5, 5, 15가 만나는 자리, 높이 7.1 m(천장 아래면 7.125 m 바로 밑)
  - 출발 방과 적 출현 방에 하나씩: (0, 6.7, -25), (0, 6.7, 24.6)
  - 재질: `_emission` 텍스처로 Emission 켬, 세기 2
  - Spot Light: Spot Angle 150, Range 12, Intensity 25, 6000 K
- **굽기 대상**: `★Map` 아래 FirstRoom의 Floor, Ceiling, Walls 전부, Wall, Wall (1), Cover - bullets must stop here, 그리고 PlayerSpawnRoom, EnemySpawnRoom(Stairs 포함), 장식 전부
- **빼는 것**: Door와 Door (1)(`BreakableDoor`), Targets(과녁), Chat Test Stations(버튼)
- **반사 프로브 3개**, 모두 Box Projection 켬:

  | 방 | 위치 | Box Size |
  | --- | --- | --- |
  | FirstRoom | (0, 3.6, 0) | (30, 7.25, 40) |
  | PlayerSpawnRoom | (0, 3.4, -25) | (10, 6.75, 10) |
  | EnemySpawnRoom | (0, 3.4, 24.6) | (10, 6.75, 10) |
