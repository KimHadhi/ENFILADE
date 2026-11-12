# 조명 안내

[프로젝트 안내](../../../README.md) · [설정·유지보수](MaintenanceGuide.md) · [외부 에셋 출처](AssetSources.md)

맵의 하늘, 해와 달, 조명 기구, 안개, 반사, 간접광을 맞추고 굽는 순서다. 모든 맵에 공통인 설정은 이미 들어가 있다. 맵마다 할 일은 [맵 하나 꾸미기](#맵-하나-꾸미기)에, 시험 맵 두 곳의 값은 [시험 맵 두 곳](#시험-맵-두-곳-밤-kloppenheim-02)에 있다.

## 공통 설정

| 위치 | 값 | 이유 |
| --- | --- | --- |
| [PC_RPAsset](../Settings/Rendering/PC_RPAsset.asset) | MSAA 4x | 물체 모서리의 계단 현상 |
| | 그림자 100 m, 4단계(10·30·70·100 m), 4096, 부드러운 그림자 Medium | 가까운 그림자를 선명하게. 2026-11-11 성능(메모 40, 측정 없이): 150 m → 100 m로 줄이면서 앞의 세 경계(10·30·70 m)는 그대로 두어 가까운 그림자는 같고, 마지막 단계만 70~100 m로 좁아져 더 선명하다. 100 m 밖의 물체는 그림자 지도에 그리지 않는다. 부드러운 그림자 High(7×7) → Medium은 경계를 거르는 비용을 줄이고 아래 천장 빛 샘(필터 번짐)도 줄인다. 되돌리려면 Shadow Distance 150, Cascade 4 Split 0.067·0.2·0.467, Soft Shadow Quality High |
| | Light Probe System: Adaptive Probe Volumes | 구운 간접광을 캐릭터와 좀비가 서 있는 자리에서 받는다. lilToon과 Poiyomi가 지원한다 |
| | Volume Profile: [GameLook](../Settings/Rendering/GameLook.asset) | 톤매핑 끔, Bloom 0.25(임계값 1), 비네트 0.2 |
| [PC_Renderer](../Settings/Rendering/PC_Renderer.asset) | SSAO 세기 0.7, 반경 0.4, Method: Interleaved Gradient | 구석과 접촉면을 어둡게. 노이즈는 고정 패턴을 쓴다. Blue Noise는 매 프레임 바뀌는 노이즈라 TAA로 섞어야 하는데, 카메라가 SMAA라 가까운 벽(렛지에 매달릴 때)이 지글거렸다. TAA로 바꾸면 Blue Noise로 돌린다 |
| | 데칼 Max Draw Distance 120 m | 2026-11-11 성능: 200 → 120 m. 120 m 밖의 총알 자국은 한 점도 안 된다 |
| [Player](../Resources/Characters/Player.prefab) 카메라 | SMAA High, 디더링 | 머리카락과 반짝이는 부분의 계단, 하늘과 안개의 띠 줄무늬 |
| [QualitySettings](../../../ProjectSettings/QualitySettings.asset) PC | Async Upload Time Slice 4 ms, Buffer 64 MB | 2026-11-11 성능: 2 ms·16 MB에서 늘렸다. 장을 불러올 때 큰 텍스처·메시를 GPU로 올리는 일을 덜 쪼개 로딩이 빨라지고 첫 사용 끊김이 줄기를 바란 것(측정 없음). 되돌리려면 2·16 |
| 캠페인 맵 (회색 맵 빌더) | 하늘·해·안개는 [MapLook.csv](../Resources/Tables/MapLook.csv) | 2026-11-12 맵 꾸미기 1단계(월드 워 Z 느낌, 사용자가 고른 시안 A안): 장마다 Poly Haven HDRI 하나(`Art/Sky`), 해의 방향·색·세기는 그 HDRI에서 잰 값(해가 너무 낮으면 올림), 안개 색은 그 하늘의 지평선 색. 맵을 지을 때 하늘 재질(`Scenes/Campaign/<장>/Sky.mat`)과 조명 설정(`Lighting.lighting`, 구운 GI 없음)을 만들고 저장한 뒤 환경광·반사만 굽는다(몇 초). 하늘 텍스처는 4096, 밉맵 없음(이음매 줄 방지) |
| | 벽·바닥 재질은 [MapSurfaces.csv](../Resources/Tables/MapSurfaces.csv) | 2026-11-12 맵 꾸미기 4단계(사용자: "슬슬 그레이박스 말고 좀 더 퀄리티 있게"): 빌더의 재질 칸(Concrete·Road·Metal·Wood, 타워는 Wall·Floor·Trim·Steel)마다 셰이더 `ENFILADE/Surface`([Art/Shaders/Surface](../Art/Shaders/Surface/Surface.shader)) 재질을 지을 때 `Art/Materials/Surfaces/<줄 id>.mat`로 쓴다. 텍스처는 `Art/Textures/Surfaces`의 ambientCG 세트(색·NormalGL·거칠기·AO)를 따로 읽어 월드에서 m 단위로 입힌다(박스 매핑: 면마다 가장 많이 보는 축 하나, 위로 보는 면은 top 세트). 그래서 상자에 UV가 없어도 되고, 붙은 상자는 이음매 없이 한 무늬로 이어진다. 긴 벽에서 같은 무늬가 되풀이되는 티는 몇 m 넓이의 밝기 얼룩(variation)으로 지운다. 문·셔터·스캐너·밀려나는 여객기처럼 움직이는 것은 `.Moving` 재질(물체 자신의 공간)이라 텍스처가 미끄러지지 않는다. 줄이 없거나 텍스처가 없거나 셰이더가 컴파일되지 않으면 그 칸은 예전 회색 재질로 짓는다(콘솔에 [Greybox] 경고). 빛·그림자·안개·프로브·데칼·Forward+는 URP Lit의 코드 그대로라 옆의 Lit 물체와 똑같이 밝혀진다 |
| | 건물마다 벽, 창, 맵 밖 도시는 MapSurfaces `boxes`·`windows`, [MapWindows.csv](../Resources/Tables/MapWindows.csv), [MapSkyline.csv](../Resources/Tables/MapSkyline.csv) | 2026-11-12 맵 꾸미기 5단계(사용자: "1장은 어딜 가든 벽 텍스쳐가 똑같아서 아쉽더라. 그리고 맵에 밀도가 없어"): 재질을 따로 받지 않은 상자는 이름의 앞부분으로 MapSurfaces의 그 맵 줄을 찾아 입는다(긴 앞부분이 먼저). 건물의 벽·난간·옥상 방이 모두 건물 이름으로 시작해서 건물마다 제 벽이 된다. `windows`가 있는 모습은 셰이더가 옆면에 창(유리·창틀, 창마다 조금 다른 밝기)을 그린다. 빌더가 그 모습을 입은 상자들의 범위에 칸을 맞추고(모서리에서 시작하는 온전한 칸 수), 가장 큰 상자의 지붕에서 아래로 층을 세어 난간과 옥상 위에는 창이 없다. 맵 밖 도시는 보이기만 하는 상자(충돌·좀비 길 없음)를 맵 둘레 밖에 겹겹이 세운다. 크기와 높이를 모습의 창 칸과 층에 맞춰 반올림하니 창이 모서리·지붕과 맞는다. 바깥 땅도 함께 깐다. 길찾기 굽기 범위는 충돌이 있는 상자만으로 잰다(맵 밖 도시가 굽기를 넓히지 않게). 2026-11-13 2~4장에도 같은 방식(사용자: "2~4장 진행"): 장마다 모습 줄, 4장 터미널 바깥벽의 커튼월 창(`Curtain`: 2.4 × 3.5 m 유리, 층 4 m), MapSkyline 2·3·4장 줄(3장은 강 건너 광장에서 보이게 30 m 밖, 4장은 50 m 밖에 낮은 격납고), 장마다 Dense 소품 줄 |
| | 벽·바닥의 때(데칼)는 MapSurfaces `wallDecals`·`topDecals`, [MapDecals.csv](../Resources/Tables/MapDecals.csv) | 2026-11-13 맵 꾸미기 6단계(사용자: "데칼 진행"): 렌더러의 데칼(Screen Space, 총알 자국과 같은 URP 데칼 셰이더)로 벽 윗가장자리에서 흘러내린 누수 자국, 벽 밑동의 진흙 튐, 벽·바닥의 금, 길의 기름, 바닥의 때·진흙·낙엽을 붙인다. 모습(MapSurfaces 줄)마다 group과 밀도(벽은 1 m당, 윗면은 1㎡당)를 적으면 그 모습을 입은 상자가 저절로 받는다. 자리는 상자의 자리로 정해져 다시 지어도, 다른 PC에서도 같다. 데칼 상자(두께 0.2 m)가 면 위에 온전히 있고 앞에 다른 것이 없을 때만 놓아(광선과 겹침 검사) 모서리·턱·바닥·벽에 늘어져 묻지 않는다. 줄마다 그리는 거리(35~70 m)와 진하기. 한 맵 1600개, 한 면·한 윗면에 group마다 8·40개까지 |
| | 정적 배칭·가림 컬링, 레이어 거리 컬링 | 맵을 지을 때 움직이지 않는 상자를 static으로 표시하고 가림 컬링을 굽는다(작은 가림체 4 m). 벽과 건물 뒤의 좀비는 그리지 않는다. 트인 곳은 [Culling.csv](../Resources/Tables/Culling.csv)의 레이어 거리(좀비 160 m)로 자른다. 조명은 그림자 없는 점 조명만 쓴다(Forward+라 개수 제한 없음) |

- 톤매핑을 꺼서 밝기 1을 넘는 부분은 하얗게 날아간다. 빛을 받은 흰 벽이 날아가면 그 빛의 세기를 낮춘다.
- 블룸은 밝기 1을 넘는 부분만 번진다. 빛나야 하는 것(전구, 형광등, 간판)은 재질의 발광 세기를 1보다 크게 준다.
- **lilToon 패치**: lilToon 2.4.1은 구운 프로브 볼륨이 있으면 캐릭터를 프로브 빛만으로 밝힌다(lilToon issue #419, MR #420). 해의 직사광까지 프로브에 굽는 Subtractive 모드에서만 맞는 방식이다. 이 프로젝트는 Baked Indirect로 구워서 프로브에는 간접광만 들어가므로, 그대로 두면 캐릭터가 해 아래에서도 푸르고 어둡다. 그래서 [lil_common_macro.hlsl](../../../Packages/jp.lilxyzw.liltoon/Shader/Includes/lil_common_macro.hlsl)의 `LIL_ADD_REALTIME_MAINLIGHT_APV`가 해의 실시간 빛을 해 그림자만큼 다시 더한다. lilToon을 업데이트하면 이 패치를 다시 넣는다. Lighting Mode를 Subtractive로 바꾸면 해가 두 번 더해지므로 패치를 뺀다. Poiyomi(Sil)는 해를 그대로 더하므로 해당하지 않는다. 다만 Sil의 뼈 재질은 원본 설정대로 다른 물체가 드리우는 그림자를 받지 않아서(Poiyomi의 Receive Casted Shadows 0), 해가 비치는 맵에서는 막힌 방 안에서도 해 쪽이 밝다. 껍질(`WES_soft_mod3`)은 원래 빛을 받지 않는 셰이더다.
- 처치 연출과 저체력 화면이 덮어쓰는 값(노출, 대비, 채도, 색 필터, 화이트 밸런스, 블룸, 비네트)은 맵 볼륨에서 바꾸지 않는다. 분위기는 빛과 안개의 색으로 잡는다. 이유는 [화면 후처리](MaintenanceGuide.md#화면-후처리)에 있다.
- 시야각이 바뀌는 동안(달리다 멈출 때 속도 시야각이 돌아오는 동안, 조준) 가까운 해 그림자의 경계가 잠깐 떤다. URP는 그림자 단계의 크기를 카메라 시야각으로 정하기 때문이다(Unity가 의도한 동작). 거슬리면 Player의 [PlayerCameraEffects](../Scripts/Player/Camera/PlayerCameraEffects.cs) `maxFovBonus`를 줄인다. 넘기·오르기 중에는 진입 속도로 시야각을 고정하므로 그동안은 떨지 않는다.

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

이미 들어간 것: 해의 부드러운 그림자와 `Mixed`, 아래 굽기 대상 36개(Contribute GI, Reflection Probe Static, Receive GI `Light Probes`), `Lights` 아래 전역 Adaptive Probe Volume, `★Map/DarkRoom` 안의 반사 프로브(위치 (-30, 5, 0), Box Size (15, 9, 14.5), Box Projection 켬), 조명 설정 에셋 [MovementAbilityPlayground.lighting](../Settings/Rendering/Lighting/MovementAbilityPlayground.lighting)(Baked Indirect). 하늘은 아직 기본 낮하늘이다. Generate Lighting으로 구우면 문 하나만 열린 DarkRoom 안이 어두워져 총구 불빛(`MuzzleLight`)을 시험할 수 있다. 남은 것은 아래의 밤하늘, 달, 안개, 가로등이다.

- **안개**: 켬, Color `#4B5158`, Exponential, Density 0.01
- **가로등 6개**: 바닥(가로 -20~20, 세로 -10~30) 가장자리 (±19.5, 0, -5), (±19.5, 0, 10), (±19.5, 0, 25)에 놓고 등이 안쪽을 향하게 돌린다.
  - 전구 재질 `street_lamp_01_bulb`: Emission 켬, 색 `#FFC27A`, 세기 3
  - Spot Light: 높이 3.6 m(등 안), Spot Angle 150, Range 15, Intensity 6, 3000 K
- **굽기 대상**: `★Map` 아래의 Floor, Stairs, Cube, Cube (2), Cube (3), Step 0.15·0.30·0.45, Vault rail, Mantle ledge, Wall climb lane, Wall climb finish, Roll drop platform, Swing launch platform, Surface pads 전부, DarkRoom의 바닥·천장·벽 6조각
- **빼는 것**: Door(`BreakableDoor`), Shove and tackle dummy(`Rigidbody`). Swing Bar, Ladder, Zipline은 프리팹이라 표시하지 않는다
- **반사 프로브 1개**: 위치 (0, 6, 10), Box Size (44, 12, 44), Box Projection 끔

### 전투 시험장 (실내)

천장과 벽으로 막혀 있어서 달빛은 거의 들어오지 않는다. 형광등이 주 조명이다.

이미 들어간 것(2026-11-05): 아래의 형광등 14개, 굽기 대상 26개(Contribute GI, Reflection Probe Static, Receive GI `Light Probes`), 해의 부드러운 그림자와 `Mixed`, 해 옆의 전역 Adaptive Probe Volume, 반사 프로브 3개, 조명 설정 에셋 [CombatFoundation.lighting](../Settings/Rendering/Lighting/CombatFoundation.lighting)(Baked Indirect). Generate Lighting으로 구우면 된다(2026-11-05에 구웠다).

**해는 꺼 두었다(2026-11-06).** 이 맵은 사방이 막혀 해가 비출 곳이 없는데, 해 그림자 때문에 천장 바로 아래 벽에 햇빛이 반원이나 띠 모양으로 샜다. 줌하면서 천장 쪽을 올려다볼 때만 보였다. 원인은 그림자 설정이다. URP는 부드러운 그림자의 편향을 그림자 텍셀의 2.5배로 잡고(`ShadowUtils.GetShadowBias`, 조명이 파이프라인 설정을 따를 때), 이 프로젝트의 High 품질은 그 경계를 7×7로 거른다. 그림자 거리 150 m를 4구간으로 나누면 30~70 m 구간의 텍셀이 약 8 cm라 편향이 20 cm쯤 되고, 두께 25 cm인 이 천장은 거기에 필터 번짐까지 더한 만큼을 막지 못한다. 반원의 둥근 테두리는 텍셀이 작은 가까운 구간(10~30 m)의 경계다. 해를 끄면 새는 빛이 없어지고, 매 프레임 그리던 해 그림자 4구간 비용도 사라진다. 실내 조명은 형광등과 구운 간접광이 그대로 맡는다. 창문을 내서 달빛을 들이려면 해를 다시 켜고, 천장을 50 cm 이상으로 두껍게 하거나 지붕 위에 그림자만 드리우는 두꺼운 상자(Cast Shadows `Shadows Only`)를 얹는다. 이동 시험장 DarkRoom의 천장은 1 m라 괜찮다. 남은 것은 위 공통 값의 밤하늘이다.

- **안개**: 끔
- **형광등**: [MountedFluorescentLight](../Prefabs/Props/MountedFluorescentLight.prefab) 프리팹. 받은 FBX의 일곱 종류 중 `mounted_fluorescent_lights_a`만 쓴다. 원점이 천장에 붙는 면이고 관이 3.5 cm 아래로 내려오므로, 천장 아래면에서 1 mm 아래에 놓았다. 밝기와 각도는 프리팹 하나만 고치면 14개가 같이 바뀐다.
  - FirstRoom 12개(`Ceiling Lights` 아래): x = -10, 0, 10과 z = -15, -5, 5, 15가 만나는 자리, 높이 7.124 m(천장 아래면 7.125 m)
  - 출발 방과 적 출현 방에 하나씩: (0, 6.749, -25), (0, 6.749, 24.625)(천장 아래면 6.75 m)
  - 재질: 관(`mounted_fluorescent_lights_glass`)만 `_emission` 텍스처로 Emission 켬, 세기 2
  - Spot Light: Spot Angle 150(안쪽 110), Range 12, Intensity 25, 6000 K, `Mixed`, 부드러운 그림자, 그림자 해상도 Medium(512). 그림자를 끄면 첫 방의 빛이 벽을 지나 옆방 바닥까지 닿는다. 14개가 한 화면에 들어오므로 `PC_RPAsset`의 Additional Lights Shadow Atlas를 4096으로 늘렸다(2048이면 "Reduced additional punctual light shadows resolution" 경고와 함께 해상도가 깎인다). 실시간 그림자 14개가 무거우면 방 가장자리 등부터 그림자를 끈다
- **굽기 대상**: `★Map` 아래 FirstRoom의 Floor, Ceiling, Walls 전부, Wall, Wall (1), Cover - bullets must stop here, 그리고 PlayerSpawnRoom, EnemySpawnRoom(Stairs 포함), 장식 전부
- **빼는 것**: Door와 Door (1)(`BreakableDoor`), Targets(과녁), Chat Test Stations(버튼), TetoPear(물리 프롭), 형광등 프리팹(빛은 스포트 조명이 내고, 기구는 프로브 빛을 받는다)
- **반사 프로브 3개**, 모두 Box Projection 켬:

  | 방 | 위치 | Box Size |
  | --- | --- | --- |
  | FirstRoom | (0, 3.6, 0) | (30, 7.25, 40) |
  | PlayerSpawnRoom | (0, 3.4, -25) | (10, 6.75, 10) |
  | EnemySpawnRoom | (0, 3.4, 24.6) | (10, 6.75, 10) |
