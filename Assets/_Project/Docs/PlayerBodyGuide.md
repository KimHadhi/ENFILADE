# 플레이어 몸 (진짜 1인칭)

[프로젝트 안내](../../../README.md) · [이동 안내](MovementGuide.md) · [설정·유지보수](MaintenanceGuide.md) · [검증 안내](ValidationGuide.md)

플레이어는 보이는 몸을 가진다. 몸은 이동 상태에 맞는 애니메이션을 재생하고, 카메라는 몸의 눈을 따른다(Beatrun Eye View처럼, 아래 **눈 시점**). 사다리에서는 상하 전환 때 시점이 튀지 않도록 기준 눈높이를 유지한다. 회전·기울기·충격 흔들림은 코드가 더한다. 내려다보면 허리·목이 같이 굽어 몸통·다리가 보인다. 머리·머리카락은 Esc 설정 **내 머리카락 보기**를 켜면 보이고, 끄면 그림자만 남는다. **F5**는 뒤에서 몸을 보는 확인용 시점이다.

## 구성

| 구성 | 위치 |
| --- | --- |
| 캐릭터 | 기본은 [KazusaBand](../Art/Models/Characters/KazusaBand)의 카즈사(밴드). 메인 메뉴의 **캐릭터** 화면이나 Esc → **캐릭터** 탭에서 [Hiro](../Art/Models/Characters/Hiro), [Sil](../Art/Models/Characters/Sil)과 그 색 세트로 바꾼다. 모두 휴머노이드이며 서 있는 눈높이가 카메라(1.6m)에 맞게 자동으로 크기가 정해진다 |
| 애니메이션 | [Art/Animations/Mixamo](../Art/Animations/Mixamo)의 26개 클립(서기·걷기·달리기 8방향, 넘기, 난간, 낙하·착지·구르기, 일어나기). [Art/Animations/Beatrun](../Art/Animations/Beatrun)의 173개 + 웅크려 옆걸음 2개(슬라이딩·다이브·웅크리기·벽·봉 등, 소총·권총 버전 포함). [Art/Animations/MocapOnline](../Art/Animations/MocapOnline)의 19개(총 든 이동, 사다리, 주먹). 휴머노이드라 어떤 캐릭터에도 옮겨 붙는다 |
| 연결 | `Resources/Characters/PlayerBody` 프로필(모델 + 클립 목록). 임포트할 때 자동으로 만들어지고 클립 목록도 자동 갱신된다 |
| 상태별 클립·연출 수치 | [Player.csv](../Resources/Tables/Player.csv)의 `Body` 행 (예: `Body.slide,slideloop`) |
| 자세별 캡슐·눈높이 | `Player.csv`의 `Posture` 행. 웅크림 0.8m, 슬라이딩 0.62m, 엎드림 0.37m, 누움 0.35m. 눈이 캡슐 꼭대기를 넘지 않게 맞췄다 |
| 동작 코드 | [PlayerBody](../Scripts/Player/Body/PlayerBody.cs). Play 시작 때 `PlayerMovement`가 붙인다 |
| KINEMATION | 몸의 클립은 Animator의 그래프 첫 출력으로 돈다. Animator는 빈 컨트롤러([BodyHost](../Resources/Characters/BodyHost.controller), FPSAF가 읽는 파라미터만 있다)를 받는다. 그 위에 FPS Animation Framework(총, [FPSBody](../Scripts/Player/FPSBody/FPSBody.cs))가 붙는다. 애니메이션 단계가 끝난 뒤의 일은 모두 우리 코드이고, [PlayerBody](../Scripts/Player/Body/PlayerBody.cs)의 `LateUpdate`에 순서대로 적혀 있다. 컴포넌트 사이의 순서(총, 무기, 손, 옷감)는 [FrameOrder](../Scripts/FrameOrder.cs) 한 파일에 있다. 맨 처음이 시선 굽힘([SpineLook](../Scripts/Player/Body/SpineLook.cs))이다. 2026-11-09에 Character Animation System(CAS)을 몸에서 뺐다. 0.7.32까지 우리 코드가 하던 일을 KINEMATION 내부 필드를 건드려 가며 셋째 시스템으로 돌리던 것이라, 같은 비율·축·순서로 우리 코드에 옮겼다. CAS 패키지는 총(MX16A4·M1911)과 총 부품 클립이 그 통합 폴더에 있어서 지우지 않는다. FPSAF가 몸의 클립을 넘겨받으면 Animator의 첫 출력은 가중치 0으로 물러난다(둘 다 기본 자세에서 시작해서 나중 것이 앞 것을 지운다). 발은 우리 발 IK다(아래 **발 IK**). 총 클립은 Retarget Pro가 스킨마다 다시 만든다([GunClipRetarget](../Editor/Import/GunClipRetarget.cs)) |

몸의 수치는 모두 표에 있고 Play 중에도 바로 반영된다. 클립 이름을 바꾸면 몸이 다시 만들어진다.

## 캐릭터 바꾸기

메인 메뉴의 **캐릭터** 버튼이나 Esc → **캐릭터** 탭에서 고른다. 위에 캐릭터 카드(카즈사(밴드)·히로·실), 아래에 고른 캐릭터의 색 카드가 있고, 메인 메뉴에서는 오른쪽 3D 미리보기가, Esc 탭에서는 오른쪽의 큰 사진이 고른 모습을 보여 준다([CharacterSkinView](../Scripts/UI/Settings/CharacterSkinView.cs)). 다른 캐릭터를 고르면 그 캐릭터의 첫 색 세트로 시작한다. 캐릭터를 바꾸면 몸과 손발 IK를 다시 연결하고, 같은 캐릭터의 색만 바꾸면 몸은 그대로 두고 재질만 갈아입힌다. 메인 메뉴에서는 고르는 즉시, Esc 메뉴에서는 메뉴를 닫을 때 저장한다. 기본값으로 초기화하면 카즈사가 선택된다. F5 시점은 스킨을 바꿔도 유지된다. 멀티에서는 "스킨#색" 문자열로 다른 PC에 전해지므로, 색 세트가 들어간 빌드끼리 해야 서로의 색이 보인다.

카드 사진은 메인 메뉴가 열릴 때 미리보기 조명 아래에서 모습마다 한 장씩 찍어 두고([SkinPortraits](../Scripts/UI/Menu/SkinPortraits.cs)), Esc 탭도 그 사진을 쓴다. 레벨에서 바로 Play를 누르면 메인 메뉴를 거치지 않아 카드에 이름만 나온다. 편집기는 셰이더를 뒤에서 컴파일하는 동안 그 물체를 청록색으로 그리므로, 컴파일 중에 찍힌 사진은 버리고 컴파일이 끝난 뒤 다시 찍는다. 그동안 카드에는 이름만 보인다.

새 모델을 `Art/Models/Characters/<이름>/`에 넣고, `Resources/Characters/PlayerBody` 프로필의 `Skins`에 고유 `Id`·표시 이름·모델을 추가한다. `Model`은 저장된 선택이 없거나 해당 스킨이 사라졌을 때 쓰는 기본 모델이다. 1인칭에서 숨길 부분은 프로필의 `First Person Shadow Only`에 렌더러 이름으로 적는다(기본 `Head`, `Hair`, `HeadAccessories`).

색 세트는 스킨의 `Variants`에 넣는다([SkinVariant](../Scripts/Player/Body/SkinVariant.cs)). 줄마다 `Id`(저장과 네트워크에 쓰므로 한 번 정하면 바꾸지 않는다), 표시 이름(첫 띄어쓰기 앞부분이 색 카드에 나온다. 예: `02 흰색 · 주황` → `02`), 바꿀 재질 쌍 `Swaps`(모델이 가져올 때 쓰는 재질 → 이 색 세트의 재질)를 적는다. 첫 줄이 기본이며, 모델 그대로 입는 색이면 `Swaps`를 비운다. 스킨과 색 세트의 `Id`에는 `#`을 쓰지 않는다. 실의 01~04가 예시다.

캐릭터는 [lilToon](https://github.com/lilxyzw/lilToon) 2.4.1(`Packages/jp.lilxyzw.liltoon`, MIT) 셰이더를 쓴다. 재질은 각 모델의 `Materials`에 따로 있어 Inspector의 lilToon 화면에서 그림자 색·림라이트·광택을 바로 조정한다. 새 캐릭터는 모델 Inspector → Materials → **Extract Materials** 후 셰이더를 `lilToon`으로 바꾼다. 실은 예외로 원본 재질을 그대로 쓴다(아래).

카즈사는 KKBP FK `KazusaSFW.blend`(SFW 버전)를 [kkbp_model_to_fbx.py](../../../Tools/Blender/kkbp_model_to_fbx.py)로 변환했다. 얼굴·몸·머리카락·머리 장식을 나누고 표정 셰이프키와 실제로 가중치가 있는 보조 뼈를 보존했다. 사용하지 않는 뼈 가지를 제거해 1,067개에서 371개가 되었으며, 휴머노이드의 필수 뼈와 손가락·눈 54개를 명시적으로 연결한다. 눈 뼈로 크기를 맞춰 귀·헤일로가 눈높이 추정에 영향을 주지 않는다. `GagEyes`, `Tears`, `RiggedTongue`는 블렌더 드라이버에 의존하는 추가 효과여서 프리팹에서 꺼 두었다. 일반 표정 셰이프키는 얼굴 메시에 남아 있다. SFW 원본에서 제거한 몸의 노출 메시를 반영한다. 추가로 [KazusaBand.coverage.json](../../../Tools/Blender/KazusaBand.coverage.json)의 셔츠 안쪽 몸통 범위를 수출할 때 제외한다. 고개를 숙일 때 다른 가중치로 휘는 몸 표면이 셔츠를 뚫지 않도록 가려진 면 2,616개를 제거하며, 목·팔·허리 경계와 다리, 옷 자체는 유지한다. 모든 셰이프키와 UV를 보존하고 기존 프리팹·재질·스킨 선택·손 보정 연결도 유지한다.

KKBP 모델은 뼈 연결 뒤 **Rig → Configure → Pose → Enforce T-Pose → Apply**까지 해야 한다. 원본 FK 바인드 자세를 그대로 휴머노이드 기준 자세로 저장하면, 아바타가 유효하다고 표시돼도 카메라 반대 방향을 보고 팔을 내리는 동작에서 팔을 올릴 수 있다. 카즈사의 `.fbx.meta`에는 Unity의 자세 정렬을 적용한 기준 자세와 뼈의 부모 관계를 저장했다. 재수출할 때 이 메타데이터를 함께 보존하고, 모델별 문제를 고치려고 공유 애니메이션의 `yawOffset`을 바꾸지 않는다.

실은 VRChat 아바타 `sil_ess_1_2.blend`를 [sil_model_to_fbx.py](../../../Tools/Blender/sil_model_to_fbx.py)로 변환했다. 원본 치마에는 뼈가 없어 모든 정점이 골반에 붙어 있었으므로, 허리에서 밑단까지 치마 뼈(`Skirt_<줄>_<관절>`, 12줄 × 4관절)를 만들어 가중치를 옮겼다. 얼굴, 뼈대의 두개골, 눈은 `HeadAccessories` 하나로 합쳐 1인칭에서 숨긴다. 이 모델은 머리 뼈 이름이 `Head`라서 메시에 같은 이름을 쓰면 휴머노이드 아바타를 만들지 못한다. 휴머노이드 연결은 Unity가 가져올 때 자동으로 한다. 재질은 패키지 원본이다. 겉의 실루엣(`sil_01`)은 패키지의 `WES_soft_mod3` 셰이더이고, 뼈대, 눈, 목걸이(`ess01`)는 Poiyomi Toon URP(`Assets/_PoiyomiShaders`)다. 뼈 재질 `ess01`~`ess04`는 원작자가 쓴 Poiyomi 7.2.41의 값을 Poiyomi 10의 이름으로 옮겼다(셰이드 맵 그림자, 림, 피부 밑 산란, 금속 반사, 발광. 대응표는 [외부 에셋 출처](AssetSources.md)). 금속 반사는 원본에서 금속 맵과 반사 마스크(Rough 맵)를 따로 읽었는데 Poiyomi 10은 한 장으로 읽으므로, 두 장을 그대로 합친 `Mat0N_PackedMaps`를 쓴다. 이 재질들은 잠금 해제 상태라, 빌드 전에 Thry 메뉴로 잠가야 빌드에서 분홍색이 되지 않는다. `WES_soft_mod3`은 원래 Built-in 전용인 GrabPass로 뒤 배경을 가져왔다. 이를 URP의 Opaque Texture(`_CameraOpaqueTexture`)를 읽게 바꾸었고, 나머지 계산은 원본 그대로다. 그래서 `PC_RPAsset`의 Opaque Texture와 Depth Texture가 켜져 있어야 하며, GrabPass처럼 원래 해상도로 찍도록 Opaque Downsampling은 None으로 둔다. 치마 누르기가 몸을 만들 때 메시를 재므로 모델의 Read/Write는 켜 둔다.

## 치마·겉옷·머리카락

카즈사의 치마, 허리에 묶은 겉옷, 머리카락은 [Magica Cloth 2](../../ThirdParty/MagicaCloth2)의 BoneCloth로 움직인다. [PlayerBodyCloth](../Scripts/Player/Body/PlayerBodyCloth.cs)가 몸이 만들어질 때 천을 만들고, 스킨을 바꾸면 몸과 함께 다시 만든다. 멀티에서는 플레이하는 PC가 계산하고 다른 PC는 그 결과를 따라간다([설정·유지보수](MaintenanceGuide.md)). 실은 새로 만든 치마 뼈, 머리카락 4가닥, 원작자가 흔들림을 설정해 둔 가슴이 움직인다. 가슴은 매달린 천이 아니라 제자리로 돌아오는 출렁임이라 `spring`(Magica의 BoneSpring)으로 둔다.

겉옷은 전용 뼈가 없다. 소매와 뒤판이 속치마와 함께 치마 뼈 `cf_j_sk_00~07`에 가중치로 묶여 있어 치마 한 줄로 같이 움직이고, 허리에 말린 부분과 매듭은 허리 뼈에 붙어 있어 움직이지 않는다. 겉옷만 따로 펄럭이게 하려면 Blender에서 소매 뼈를 새로 넣어야 한다. 머리카락은 옆머리 3가닥만 2~3마디라 휜다. 뒷머리 8가닥은 1마디씩이라 코드가 끝점(`Cloth tip`)을 붙여 가닥마다 따로 매달고, 끝점 길이를 다르게 해서 흔들리는 주기가 어긋나게 한다. 모든 가닥이 같은 쪽으로 쏠리면 `worldInertia`를 낮추고, 더 제각각이길 원하면 `tipSpread`, `windPower`, `centrifugal`을 올린다. 앞머리 3가닥은 카메라 앞을 가리지 않도록 뺐다. 내 머리카락은 **내 머리카락 보기**를 켜야 보이고, 끄면 그림자로만 보인다.

| 무엇을 바꾸나 | 어디서 |
| --- | --- |
| 스킨별 천 뼈와 움직임 | [Cloth.csv](../Resources/Tables/Cloth.csv). 줄마다 스킨 Id, 뼈, 프리셋, 닿는 몸을 적는다. 숫자 열은 Play 중 저장하면 바로 반영되고, 뼈와 프리셋과 닿는 몸은 다음에 몸을 만들 때 반영된다 |
| 콜라이더 크기, 치마를 누르는 팔의 굵기와 누르는 정도, 다른 PC로 보내는 간격 | [ClothBody.csv](../Resources/Tables/ClothBody.csv). 다리와 팔은 뼈 길이, 손은 손목에서 가운뎃손가락 첫 마디까지의 거리, 머리와 가슴은 어깨 너비의 비율이라 모델 크기를 따라간다. 콜라이더 크기와 팔 굵기는 다음에 몸을 만들 때 반영된다 |
| 표에 없는 세부 값 | [Resources/Cloth](../Resources/Cloth)의 프리셋. Play 중 몸 아래의 `Cloth (…)` 오브젝트를 Inspector에서 조정한 뒤 프리셋으로 저장해 이 파일을 덮어쓴다 |

콜라이더는 천만 막고 전투 판정과는 상관없다. Magica Cloth는 Unity 콜라이더를 보지 않으므로 벽과 좀비를 통과한다. 바닥은 캡슐 밑의 평면으로 막고, 웅크리기·슬라이딩·엎드리기·구르기처럼 땅에 낮게 있을 때만 켠다. 넘기 도중에는 캡슐 밑이 공중이라 평면이 치마를 밀어 올리기 때문이다. 죽으면 평면은 캡슐이 아니라 몸의 골반 밑 땅으로 옮겨 가 땅의 기울기를 따르므로, 시체가 밀리거나 쏴서 옮겨져도 천이 죽은 자리의 높이에 남지 않는다. 팔은 콜라이더가 아니다(아래).

콜라이더는 천이 몸의 어느 쪽에 있는지 모른다. 무릎이 크게 올라가며 치마 앞판이 다리 뒤로 넘어가면 다리가 앞판의 복귀를 막는다. 그래서 치마에는 뼈가 원래 자리보다 몸 안쪽으로 못 들어가게 하는 `backstop`과, 원래 자리에서 너무 멀어지지 않게 하는 `reach`를 둔다. 치마가 무릎에 눌려 뚫리면 `backstop`을 키우고, 넘기 뒤 앞판이 다시 끼면 줄인다.

Magica는 뼈를 잇는 선에서만 충돌한다. 보이는 치마는 그 선 앞뒤로 두께가 있어서, 충돌 두께(입자 반지름)가 얇으면 다리가 겉의 겹을 뚫고 들어간 뒤에야 닿는다. 실의 튀튀는 정점의 80%가 뼈 선에서 0.068 안에, 카즈사의 치마와 겉옷은 0.067 안에 있는데 프리셋 두께는 0.02다. 그래서 `Cloth.csv`의 `thickness`로 두께를 정한다. 다만 몸이 만들어질 때 깊이마다 다리와 골반까지의 여유를 재서, 서 있기만 해도 치마가 밀리지 않도록 그 여유의 90%까지만 쓰고 프리셋 두께보다는 얇게 하지 않는다.

애니메이션 클립은 치마 없는 몸 기준이라, 팔을 내리면 손이 치마 부피 안에 놓인다. 팔을 Magica 콜라이더로 두면 콜라이더가 천을 사방으로 밀어내므로, 치마 속의 손이 안에서 천을 밀어 주먹만 한 혹을 만든다. 손을 치마 겉까지 통째로 옮기면 손이 클립과 크게 달라져서 쓰지 않는다. 그래서 팔은 천과 부딪히지 않고, 천이 그 프레임의 자리를 정한 뒤 [SkirtPress](../Scripts/Player/Body/SkirtPress.cs)가 팔 자리의 치마 뼈를 치마 가운데 선 쪽으로 곧게 누른다. 팔과 같은 높이에서 치마 바깥 겹이 팔 안쪽 면 뒤로 갈 때까지 누르고, 그 아래 뼈도 같은 만큼 들어와 눌린 자리에서 천이 늘어진다. 뼈 길이는 그대로라 치마는 관절에서 안으로 꺾인다. 누르는 자리는 팔 둘레 밖으로 이웃한 뼈 줄 간격(`ClothBody.csv`의 `pressSpread`, 기본 1배)만큼 둘레를 따라 완만하게 번져서, 손이 두 줄 사이에 있어도 둘 다 눌린다. 허리 위나 밑단 아래, 몸 반대편에 있는 팔은 누르지 않는다. 팔은 `handSink`(기본 0.3)만큼 바깥 겹에 묻힌다. 손이 치마에서 떠 보이면 `handSink`를 올리고, 많이 파묻히면 내린다.

치마의 안쪽 겹과 바깥 겹이 뼈 선에서 얼마나 떨어져 있는지는 몸을 만들 때 메시에서 뼈마다 잰다([SkinSample](../Scripts/Player/Body/SkinSample.cs)). 카즈사의 옆줄 바깥 겹은 2cm 안팎이고 소매가 덮는 앞줄은 5~8cm라서, 숫자 하나(`thickness`)로는 맞지 않는다. 한 줄만 양옆 줄보다 훨씬 두꺼우면 리본이나 정점 몇 개가 튀어나온 것으로 보고 깎는다. 실의 등 뒤 밑단이 그렇다. 치마가 몸에서 멈추는 선도 실제 피부다. [BodySurface](../Scripts/Player/Body/BodySurface.cs)가 몸을 만들 때 골반과 다리 메시에서 뼈 둘레의 방향과 높이마다 피부까지 거리를 재고, 그 지도는 뼈를 따라 움직인다. Magica 콜라이더는 둥근 관만 되어서 넓적한 허벅지에 맞출 수 없고, 서 있을 때 치마가 밀려 부풀지 않도록 가늘게 둔다. 그래서 이 지도는 누르기에만 쓴다. 눌린 치마는 `ClothBody.csv`의 `squash`(기본 0.8)만큼 몸에 닿아 납작해진다. 0이면 안쪽 겹이 피부에 닿을 때 멈추고, 1이면 바깥 겹이 피부에 닿을 때까지 들어간다. 허벅지가 비치면 낮추고, 덜 눌려 보이면 올린다. 피부와 치마 사이에는 3mm를 남긴다. `Cloth.csv`의 `press`가 있으면 그 깊이(천 길이의 배수)까지만 누른다. 클립은 손을 허벅지에 거의 붙여 두는데 치마 메시는 두께가 줄지 않으므로, 치마를 갈 수 있는 데까지 누르고도 손이 묻히는 때가 있다. 그 남은 깊이만큼 팔이 어깨에서 바깥으로 벌어져 손이 치마 위에 얹힌다. 팔꿈치와 손목은 클립 그대로이고, 벌어지는 거리는 `ClothBody.csv`의 `armClearance`(팔 길이의 0.04배, 카즈사는 약 2cm)까지이며 부드럽게 따라간다. 총이나 난간처럼 무언가 잡은 손은 움직이지 않고, 쓰러진 몸에는 하지 않는다. 그보다 깊은 손은 치마에 묻힌다. 메시를 읽으려면 모델 가져오기 설정의 Read/Write가 켜져 있어야 한다. 꺼져 있으면 Console에 한 번 알리고, 두께는 `thickness`로, 멈추는 선은 다리 콜라이더로 대신한다. 치마가 많이 꺼져 보이면 `press`를 0.3 정도로 줄이고, 0이면 누르지 않고 팔이 치마를 통과한다. 그려지는 모습만 누르고, 다음 프레임은 Magica가 뼈를 되돌린 뒤 천 자신의 상태에서 계산한다. 그래서 시뮬레이션은 팔을 모르고, 휘두른 손이 치마를 쳐서 흔들지는 않는다. 멀티에서는 누르기 전 자세를 보내고 PC마다 자기 화면의 팔로 누른다. 손은 치마 때문에 움직이지 않는다.

새 스킨에 천을 넣으려면 `Cloth.csv`에 줄을 더한다. 뼈 이름은 모델의 `.fbx.meta`나 Hierarchy에서 찾는다. 줄이 없는 스킨(히로)의 치마와 머리카락은 지금처럼 움직이지 않는다.

## 애니메이션 추가하기

휴머노이드 FBX를 `Art/Animations/<묶음>/`에 넣고 옆에 `<파일이름>.clips.csv`를 둔다. 클립 하나짜리 파일이 많은 폴더(Mixamo)는 폴더 이름의 표 하나(`Mixamo/Mixamo.clips.csv`)에 모은다. Mixamo는 모든 클립 이름이 `mixamo.com`이라 파일 이름이 클립 이름이 된다. 수정 후 반영되지 않으면 해당 FBX를 우클릭해 **Reimport**한다.

| 열 | 뜻 |
| --- | --- |
| `loop` | 1이면 반복 |
| `inPlace` | 1이면 클립 안의 앞뒤·옆 이동을 버리고 제자리에서 재생. 기본은 원래 위치 그대로다 |
| `inPlaceY` | 1이면 오르내림도 버린다. 넘기·올라가기처럼 플레이어 경로가 몸을 옮기는 클립에 쓴다 |
| `inPlaceYaw` | 1이면 클립 안의 회전을 버린다. 몸이 직접 도는 제자리 돌기에 쓴다 |
| `cycleOffset` | 반복 클립이 시작하는 지점(0~1). 섞이는 이동 클립들이 같은 순간에 왼발을 딛게 맞춘다 |
| `from`·`to` | 첫 프레임부터 센 프레임 번호. 그 구간만 쓴다(착지는 발이 닿는 프레임부터) |
| `yawOffset` | 클립의 앞 방향 보정 각도(도, 기본 0). 아래 설명 참고 |

`yawOffset`은 클립이 어디를 보는지에 따라 정한다. Source 클립을 그대로 변환하면 옆을 보므로 **90**이다. Hiro 뼈대에 구운 Beatrun 클립과 Mixamo 클립은 0이면 카메라 반대쪽을 보므로 **180**이다(변환 스크립트가 알맞은 값을 써 준다). 구르기·좌우 회피처럼 애니메이션에 들어 있는 회전은 그대로 남는다.

### Mixamo에서 받을 때

스킨 없이 받은 파일에는 T자 자세가 없어서, 파일마다 첫 프레임을 기본 자세로 삼은 아바타가 만들어지고 클립이 그만큼 틀어져 리타깃된다(Mixamo는 발이 반 바퀴 뒤집혀 나왔다). 그래서 같은 캐릭터를 T자 자세·스킨 포함으로 받아 폴더 이름으로 `<폴더>.tpose.fbx`(`Mixamo/Mixamo.tpose.fbx`)로 두면, 그 폴더의 다른 파일이 모두 그 아바타를 복사해 쓴다(Copy From Other Avatar). 넣으면 자동으로 다시 임포트된다.

새 클립은 지금과 같은 캐릭터(X Bot), **FBX Binary**로 받는다.

### MoCap Online에서 받을 때

UE4용 ZIP의 `Source/FBX`(Punches는 `Content/Boxing_01/Source/FBX`)에 클립마다 FBX가 하나씩 있다. UE4 마네킹 뼈대라 Unreal 없이 그대로 쓴다. 제자리 버전(`IP`·`IPC`, 없으면 `Root_Motion`)을 [MocapOnline](../Art/Animations/MocapOnline)에 넣고 `MocapOnline.clips.csv`에 행을 더한다. 클립 이름은 파일 이름이고, `yawOffset`은 180이다. 폴더의 `MocapOnline.tpose.fbx`는 팩의 `SKMannequin_T_Pose/SK_Mannequin.fbx`다. A자 자세 파일을 쓰면 안 된다.

- **Rifle 팩**: FBX가 없고 `.uasset`뿐이라 Unreal 에디터로 FBX를 내보내야 한다. 그 전까지 `Body.rifleMoves`는 권총 클립을 쓴다. 손은 IK가 총에 놓으므로 달라지는 것은 다리와 상체 자세다. Unreal에서 내보낸 FBX는 take 이름이 파일 이름과 다를 수 있으니 임포트 뒤 확인한다.
- **눈높이**: 웅크린 클립은 눈이 약 1.2m라 웅크린 카메라(0.8m)와 맞지 않아서 쓰지 않는다. 받아 둔 팩의 원본은 프로젝트 밖 `F:\GameProject\_MocapOnline`에 있고, 파일별 받기 횟수가 정해져 있다.
- **라이선스**: Standard License는 게임에 쓸 수 있지만 원본 파일을 따로 배포하면 안 되므로 저장소는 비공개로 둔다. 이 폴더의 FBX에 든 마네킹 메시는 Epic 것이다. 몸은 클립만 참조해서 메시는 빌드에 들어가지 않는다.

### Source(GMod·L4D2) 모델·애니메이션 변환

[Tools/Blender](../../../Tools/Blender)의 스크립트를 SourceIO 애드온이 있는 Blender로 실행한다. 모델은 `source_model_to_fbx.py`, 애니메이션은 `source_anims_to_fbx.py`. 애니메이션은 마지막 인자로 플레이어 모델의 MDL을 주면 그 모델의 뼈대(뼈 길이·기본자세)에 직접 구워 낸다. 서로 다른 뼈대 사이의 휴머노이드 리타깃이 없어져 발이 꺾이거나 몸이 뒤로 빠지지 않는다. 예: `blender -b --factory-startup --python source_anims_to_fbx.py -- new_climbanim.mdl BeatrunBody.fbx BeatrunBody * - 180 survivor_gambler.mdl`. Hiro의 뼈대 MDL은 [Tools/Blender/Source/Hiro](../../../Tools/Blender/Source/Hiro)에 있다. 맨 끝에 접미사(`_tp`)를 주면 클립 이름 뒤에 붙는다. 같은 이름의 클립 묶음을 나란히 두고 비교할 때 쓴다. 몸은 같은 이름이 여러 개면 경로 순서상 첫 번째를 쓴다.

- **블렌드 시퀀스**: 3인칭 세트의 이동 클립(`runfwd`, `walkfwd`, `crouchfwd`, 회피 등)은 이동 방향 3×3 블렌드이고, 가운데 칸이 서 있기다. 칸마다 몸 전체가 이동 방향으로 돈다. 변환 스크립트는 클립 이름에 맞는 칸을 고른다. 앞 이동은 앞 칸, `runbwd`·`walkback`·`crouchbwd`는 뒤 칸, `dodgejumpleft/right`는 옆 칸이고, 나머지 블렌드(매달리기·흔들기)는 모든 값이 0인 칸이다. 다른 칸을 따로 굽으려면 이름을 `새이름=시퀀스@옆:앞`으로 준다. 웅크려 옆걸음(`BeatrunCrouch.fbx`)은 `crouchleft=crouchfwd@-1:0,crouchright=crouchfwd@1:0`으로 구웠다. `sprintfwd` 원본은 2초에 같은 보행 주기가 네 번 들어 있다. 다른 이동 클립과 한 주기로 섞도록 `BeatrunBody.clips.csv`에서 `from=0`, `to=15`, `cycleOffset=0`으로 잘라 0.5초 한 주기만 쓴다(30fps). 전체를 다시 구우면 표가 새로 써지므로 이 세 값을 복원한다.
- **Spine4**: Source 뼈대에는 척추가 4개(`Spine`, `Spine1`, `Spine2`, `Spine4`)지만 Unity 휴머노이드에는 3자리뿐이다. 그래서 변환할 때 `Spine4`의 회전을 `Spine2`에 합쳐 굽는다. 임포트 때 "in between humanoid transforms … discarded" 경고가 나면 이 처리가 빠진 것이다.
- **손가락**: Hiro의 손가락 뼈 축은 Beatrun 뼈대와 최대 167° 달라서, 회전을 그대로 옮기면 손가락이 옆·뒤로 꺾인다. 변환할 때 클립 손가락이 자기 기본 자세에서 움직인 만큼을 Hiro 손가락 축으로 옮겨서 Hiro 기본 자세에 더한다. 클립이 기본 자세면 Hiro도 정확히 기본 자세다.
- 모든 뼈를 매 프레임 키로 굽는다(173개, 약 170MB). 움직이지 않는 뼈를 빼거나 키를 줄이면 일정한 자세를 유지하는 뼈가 기본 자세로 튀거나 중간에 꺾여서 쓰지 않는다. 한 프레임짜리 포즈는 Unity에서 빈 클립이 되므로 넣지 않는다. 필요한 포즈는 `이름=시퀀스@값:값`으로 이름을 주면 같은 자세로 두 번 키를 찍어 굽는다(`BeatrunHang.fbx`).

## 들어 있는 애니메이션

[Beatrun](../Art/Animations/Beatrun)은 Beatrun 3인칭 여성 세트(`tp_climbanim_f.mdl`)를 Hiro 뼈대에 구운 173개 클립이다. 벽 달리기 4개는 쓰지 않아 뺐다. 소총·권총 버전은 `_ar2`·`_pistol`이 붙는다(누워 미끄러지기, 다이브, 머리부터 미끄러지기, 공중 코일 등). [Mixamo](../Art/Animations/Mixamo)는 mixamo.com에서 X Bot 기준으로 받은 클립(스킨 없음, 30fps)이다. 리타깃 기준 자세는 같은 X Bot을 T자 자세·스킨 포함·FBX Binary로 받은 `Mixamo.tpose.fbx`다(클립 파일과 같은 FBX 2019 형식, 같은 `mixamorig:` 뼈 66개). 걷기·달리기·옆걸음·뒷걸음은 제자리 버전이다. 클립마다 실제 속도·길이·발 딛는 시점은 `Mixamo.clips.csv`의 메모에 있다. 웅크리기는 Beatrun을 쓴다. Mixamo 웅크려 걷기는 눈높이가 약 1.2m라 웅크린 카메라(0.8m)와 맞지 않는다. Mixamo 클립 가운데 쓰지 않는 것은 `mx_stand_up_stomach` 하나다(11초로 너무 길어서 엎어진 착지에는 `mx_get_up`을 쓴다). `Player.csv`의 `Body` 행에서 클립 이름을 바꾸면 Play 중에도 몸이 다시 만들어진다.

## 이동

- **8방향 이동**([PlayerBody.Locomotion](../Scripts/Player/Body/PlayerBody.Locomotion.cs)): 걷기·달리기·웅크려 걷기는 앞·오른쪽·뒤·왼쪽 클립을 이동 방향으로 섞는다(대각선은 양옆 두 개). 몸은 항상 카메라 쪽을 본다. 모든 반복 클립은 한 보행 주기를 공유해서 섞여도 발이 엇갈리지 않는다. 전력질주 클립은 일부러 앞으로 달릴 때(`RunIntent`: Shift, 또는 W로 속도가 쌓이는 중) 그 속도에 이르면 쓴다. 일반 이동·뒷걸음·옆걸음에는 전력질주 클립을 섞지 않는다.
- **총 든 이동**: 총이 보이는 동안 서기·걷기·달리기·전력질주는 `Body.pistolMoves`·`Body.rifleMoves`의 클립을 쓴다. 순서는 서기, 걷기 앞·오른쪽·뒤·왼쪽, 달리기 앞·오른쪽·뒤·왼쪽, 전력질주다. 프로필에 없는 클립이 하나라도 있으면 맨손 클립을 쓴다. 웅크리기는 총을 들어도 그대로다. 클립은 왼발을 딛는 순간이 맞도록 `cycleOffset`을 넣어 맨손 클립과 같은 박자로 섞인다.
- **이동 재생 속도**: `Body.runClipSpeed=5.2`, `Body.sprintClipSpeed=8.4`, `Body.crouchClipSpeed=3`은 각각 `Move.walkSpeed`, `Move.sprintSpeed`, `Move.crouchSpeed`에 맞춘 기준값(m/s)이다. 빠른 기본 이동 때문에 평소에도 질주 자세가 섞이거나 웅크리기가 1.5배로 재생되지 않게 한다. 이동 애니메이션의 재생 배율은 0.65~1.1로 제한한다. 이동 속도를 바꾸면 이 기준값도 함께 조정한다.
- **제자리 돌기**: 서 있을 때 시선만 돌리면 다리는 그 자리에 있고 상체만 비튼다. `turnStartAngle`(60°)을 넘으면 다리가 선 자세 그대로 제자리에서 시선 쪽으로 돈다(`turnSpeed`, 시작과 끝은 `turnEaseTime`만큼 부드럽게). `turnSteps`를 1로 하면 `turnLeft`/`turnRight` 클립으로 발을 옮기며 돈다. 이 클립은 `inPlaceYaw`로 회전을 빼고 다리에만 거는데, 걸음은 클립 자신의 회전에 맞춰져 있고 엉덩이는 선 자세 그대로라서 걸음 중간에 다리가 옆으로 갔다가 클립이 끝날 때 돌아온다. 그래서 기본은 끈다(2026-11-09 사용자 요청). 이 임계각에 따른 다리 돌기는 슬라이딩 중에는 실행하지 않는다. 총을 들어도 같다. FPSAF 프로필의 IK 층(`IkLayerSettings`)에서 `offsetFeetTargets`는 끈다. FPSAF 데모는 시선과 함께 몸 전체를 돌리고 발만 그만큼 되돌리지만, 우리 몸은 이미 다리 쪽을 보고 있어서 발이 한 번 더 돌아 미끄러졌다(2026-11-09 사용자 요청: 총 들고 제자리 돌 때 다리가 흐느적거리며 미끄러짐).

## 파쿠르 시간

- 넘기·올라서기: Beatrun의 이동식을 그대로 쓴다([VaultMotion](../Scripts/Player/Movement/Parkour/VaultMotion.cs)). 동작마다 시작점에서 끝점까지 직선으로 가는 빠르기와 손을 놓는 시점이 다르며, `Ability.vaultRate`·`mountRate`·`heaveRate`로 배율을 바꾼다. 매달린 뒤 올라가기는 약 0.93초다.
- 매달린 뒤 올라가기의 몸([PlayerBody.Heave](../Scripts/Player/Body/PlayerBody.Heave.cs)): 캡슐은 Beatrun의 직선으로 먼저 가고, 보이는 몸은 `mx_ledge_climb_up`이 움직인다. 이 클립만 이동과 높이를 남겨 임포트하고 진행률로 재생한다. 양손의 팔이 가장 길게 펴졌다가 다시 굽기 시작할 때까지는 매달릴 때와 같은 `Grip`이 몸을 손에 맞추므로 손은 움직이지 않고 어깨가 손 위로 올라온다. 그 뒤로는 손을 뗀 자리에서 클립대로 이어 가고, 남은 구간의 뒤쪽 70%에 걸쳐 발은 캡슐 바닥에, 골반은 캡슐 위에 맞춘다. 끝나면 `Body.heaveStandTime`(0.35초) 동안 일어선다.
- 올라서는 동안(`Mantle`: 매달린 데서 올라가기, 높은·낮은 올라서기, 바·사다리 꼭대기) 시선은 올라가는 쪽에서 좌우 `Body.mantleTurnLimit`(45°, Beatrun의 높은 올라서기와 같음)까지만 돈다. 카메라가 머리를 따라가므로 더 돌면 자기 목을 뚫어 본다. Beatrun은 이때 자기 머리를 숨겨서 매달린 데서 올라가기에는 제한이 없다.
- 클립은 이동 진행도가 아니라 자기 속도로 재생된다(`Body.vaultClipSpeed`, 낮은 올라타기만 `ontoClipSpeed`). 몸이 먼저 도착하면 발이 땅에 닿아 움직이기 시작할 때까지 이어진다. 이 동안 몸은 GMod처럼 캡슐 위에 그대로 있고, 시작한 방향을 유지한다.
- 바에서 올라서기, 사다리 꼭대기, 천장이 낮은 난간은 따로 만든 경로를 쓴다. `Ability.mantleDuration` + 높이 1m마다 `mantleDurationPerMeter`가 걸리고 클립이 그 진행도에 맞춰 재생되며, 진행의 80%부터 이동으로 돌아갈 수 있다.
- 구르기는 `Ability.rollDuration` 동안 `Body.roll`(mx_sprint_roll) 전체가 재생된다. 끝까지 발을 내디디는 클립이라 구른 뒤 달리기로 그대로 이어진다. 세게·크게 착지하기 전 `rollInputWindow`(0.5초) 안에 Ctrl을 누르면 구르고, 착지할 때의 속도를 그대로 가져간다.
- 제자리 착지: 수평 속도가 `Ability.rollLandSpeed`(2m/s, 걷기 출발 속도보다 낮아 걸어서 떨어져도 구른다)보다 느리거나 S를 누르고 있으면 구르지 않고 `Ability.rollLandTime`(0.6초) 동안 멈춰 `Body.rollLand`(land)를 재생한다. 조작이 돌아온 뒤에도 클립은 끝까지 이어진다. 머리 위가 낮아 설 수 없으면 `Body.landCrouch`를 쓴다.
- 크게 착지해 쓰러져 있는 동안(`HeavyLanding`)은 쓰러지는 방향에 맞는 클립이 일어날 때까지의 시간에 맞춰 재생된다. 몸은 쓰러진 방향을 유지하고, 시선은 그 방향에서 좌우 `Body.heavyLandingTurnLimit`(90°)까지만 돈다(카메라가 눈에 붙어 있어서 더 돌면 자기 머리를 뚫어 본다).

## 상황별 클립

같은 상태라도 상황에 맞는 클립을 고른다. 모두 `Body` 행에서 이름과 기준값을 바꾼다.

- 점프: 도움닫기 속도에 따라 제자리(`jumpStill`), 걷기(`jumpSlow`), 달리기(`jump`).
- 공중에서 Ctrl: 무릎을 접는 `airTuck`(Beatrun 코일). 충돌 캡슐의 바닥이 올라가므로 몸도 그 자세의 발바닥이 캡슐 바닥에 닿는 만큼만 올라간다([PlayerBody.Tuck](../Scripts/Player/Body/PlayerBody.Tuck.cs), `Body.tuckFollowSpeed`). 그래서 발이 바닥을 뚫지 않고, 착지할 때 몸과 눈 시점이 튀지 않는다.
- 큰 낙하(`Ability.bigFallHeight`): 팔다리를 허우적거리는 `fallFlail`(Beatrun `fallinguncontrolled`). 큰 낙하 중 Ctrl은 착지 구르기만 준비하고 무릎을 접지 않는다. 이미 접고 있었으면 다리를 편다.
- 착지(단계는 [착지 단계](MovementGuide.md#착지-단계)): 세게 착지하면 무릎을 깊게 굽히는 `land`, 웅크린 채면 서지 않는 `landCrouch`. 크게 착지하면 손과 무릎을 짚었다 일어나는 `heavyLand`(mx_hard_landing), 아주 크게면 엎어졌다 일어나는 `heavyLandFaceDown`(mx_get_up), 뒤로 움직이던 중이거나 넉백이면 등으로 떨어졌다 일어나는 `heavyLandBack`(mx_stand_up_back). 가볍게 착지할 때 공중에서 무릎을 접었으면, 움직이는 중에는 달려 나가는 `coilLand`/`coilLandCrouch`를 쓰고 제자리면 발을 디딘 채 잠깐 웅크렸다 일어난다. 무릎을 접지 않았으면 착지 동작 없이 바로 이어진다. 빠른 낙하 애니메이션은 `fallClipSpeed`(7.2m/s)부터.
- 슬라이드 끝: 웅크림·엎드림으로 이어지는 끝 동작.
- 넘기: 판정이 고른 동작에 따라 보통 넘기 `vault`, 긴 넘기 `vaultKong`, 얇은 벽 넘기 `vaultHigh`. `vaultChainTime`(1.5초) 안에 같은 클립이 다시 걸리면 다른 넘기 클립을 쓴다. 같은 클립은 자기 자신과 섞일 수 없어서 첫 프레임으로 튀기 때문이다. 끝날 때 Space를 누르고 있으면 `springboard`로 튀어 오른다.
- 올라서기: 판정이 고른 동작에 따라 디뎌 오르기 `stepUp`, 올라타기 `mantleLow`, 높은 올라서기 `mantleMid`, 매달린 데서 올라가기 `ledgeClimbUp`. 바에서 올라설 때만 높이로 고른다(`mantleStepHeight`·`mantleLowHeight`·`mantleMidHeight`).
- 난간: 잡는 순간(`ledgeCatch`)과 좌우 이동(`ledgeShimmyLeft/Right`). 벽에서 `Ability.ledgeJumpLookAngle`(42°)보다 크게 돌아보면 Beatrun `hangpose`의 좌우 4칸·위아래 3칸 자세를 섞어 몸이 시선을 따라 돈다([PlayerBody.Hang](../Scripts/Player/Body/PlayerBody.Hang.cs), [BeatrunHang](../Art/Animations/Beatrun/BeatrunHang.fbx)). 왼쪽을 보면 오른손, 오른쪽을 보면 왼손이 남는다. 돌아보는 동안 몸은 벽을 볼 때의 자리를 지키고, 남은 손은 팔을 뻗어 모서리를 잡는다. 벽에서 좌우 `Body.hangTurnLimit`(175°)까지만 돌아볼 수 있다. 매달린 동안과 올라가며 손을 짚은 동안에는 무릎·발목·발끝이 윗면보다 낮으면서 벽면을 넘으면 그 관절을 벽 밖으로 돌린다. 클립을 다른 체형에 입히면 다리가 벽을 뚫기 때문이다.
- 벽: 벽 오르기 시작(`wallClimbStart`), 튀는 방향별 벽 차기(`wallKick`/`wallKickRight`).
- 총: `armedClips`를 켜면 소총·권총을 들었을 때 해당 클립의 총 든 버전(`rifleSuffix`·`pistolSuffix`)을 쓴다. 이 클립들은 Source가 손을 총에 고정해 주는 것을 전제로 만들었다. 지금은 손을 몸의 손 IK가 총에 놓으므로 켜 볼 수 있지만, 아직 확인하지 않아 꺼 두었다.

## 머리 맞추기와 누운 자세

자세마다 클립 속 머리 위치가 다르므로(누워 미끄러지면 머리가 발 뒤쪽에 있다) 몸은 머리가 카메라 기준점 바로 뒤에 오도록 천천히 따라 움직인다(`Body.headFollowTime`). 매달릴 때는 손이 몸을 지지하고, 일반 슬라이딩 중에는 진행 방향 기준 위치를 유지한다.

누운 자세는 Beatrun 점프 턴 계열을 쓴다. 조준을 누른 채 W 없이 슬라이드가 끝나면 `meslideendprone`으로 다리를 들었다가 눕고, 누워서는 `jumpturnlandidle`, 일어설 때는 Beatrun의 `DoJumpTurnStand`처럼 셋 중 하나를 고른다. 맨손이면 `jumpturnlandstand`(어깨 위로 뒤로 굴렀다가 일어선다), 총을 들었으면 `jumpturnlandstandgun`(구르지 않고 총을 무릎 앞에 든 채 앉았다가 일어선다), 낮은 천장 아래는 `jumpturnlandcrouch`다. 일어나는 동안은 `Ability.supineRiseTime`(1초, Beatrun의 JumpTurnRecovery와 같음) 동안 걷기·점프·회피·다이브·기울이기가 되지 않아 일어나는 동작을 끝까지 보여 준다. 손은 자유라서 사격·조준·밀치기·주먹은 된다. 등을 대고 하는 다이브는 공중에서 `jumpturnfly`로 뒤로 누운 뒤 `jumpturnflyidle`로 날아가고, 몸은 날아가는 방향의 반대쪽을 본다(Beatrun 점프 턴과 같다). 앞 다이브에서 돌아눕거나 누운 채 떨어지면 시작 동작 없이 바로 공중 자세가 된다. 내려앉으면 `jumpturnland`로 눕고, 빠르게 내려앉아 미끄러져도 같은 동작으로 눕는다. 벽에 튕겨 누워 미끄러질 때는 `meslide` 계열을 쓴다.

Beatrun의 `meslideendprone`·`jumpturnland`는 몸 전체를 앞으로 옮긴다(`meslideendprone`은 약 0.9m이고, 우리 슬라이드 클립보다 그만큼 뒤에서 시작한다). Beatrun은 카메라가 머리에 붙지 않아 괜찮지만 우리는 붙어 있으므로, 이 두 동작 동안에는 머리가 바닥 위 제자리를 지키고 다리만 앞으로 뻗는다(`PlayerBody.HoldHeadPlace`). 1인칭 화면은 아래로만 내려간다. 또 미끄러지기·다이브·엎드림·누운 자세와 눕고 일어나는 동작 동안에는 조준 중에도 카메라가 눈에 붙어 있다. 조준할 때 쓰는 고정 위치는 서 있는 캡슐 기준이라서, 그대로 두면 바닥에 누운 몸을 뚫고 지나가기 때문이다.

엎드림은 전용 애니메이션이 없어 머리부터 슬라이딩 자세를 임시로 쓴다.

## 눈 시점과 시선

시선 방향과 머리가 시선을 따라가는 일은 [BodyGaze](../Scripts/Player/Body/BodyGaze.cs)(어느 클립이 시선을 이끄는지와 수치는 [PlayerBody.Gaze](../Scripts/Player/Body/PlayerBody.Gaze.cs)), 상체 자세와 눈 위치는 [PlayerBody.Look](../Scripts/Player/Body/PlayerBody.Look.cs)가 맡는다. 맨손일 때 허리·목·머리를 시선대로 굽히는 일은 [SpineLook](../Scripts/Player/Body/SpineLook.cs)이 애니메이션 단계 바로 뒤, `LateUpdate`의 맨 처음에 한다. 총을 들면 FPS Animation Framework(FPSAF)의 Look 레이어가 넘겨받는다([FPSBody](../Scripts/Player/FPSBody/FPSBody.cs)).

- 평소에는 마우스가 정한 `LookPivot`의 방향을 SpineLook이 허리·목·머리에 나눠 준다. 마우스 입력에는 별도 보간을 넣지 않으며, 1인칭 카메라와 F5·다른 플레이어에게 보이는 몸이 같은 시선을 쓴다. 머리를 카메라 방향에 정확히 묶던 예전 보정은 없다. 머리는 SpineLook이 나눈 각도만큼 돌아간다.
- 넘기·올라서기·구르기 등 선택된 행동에서는 카메라가 그 클립의 머리 움직임을 편안한 범위로 줄여 반영한다. 이때 머리는 시선으로 굽히기 전 클립의 머리(SpineLook이 굽히기 직전에 적어 둔 `ClipHeadPosition`·`ClipHeadRotation`)를 쓴다. `Body.animationViewLimits`로 먼저 위아래 18°·좌우 12°·기울기 10° 안으로 제한한 뒤 `Body.animationViewStrength`(기본 0.65)와 클립 비율을 곱하고, `Body.animationViewTime`(기본 0.1초)으로 애니메이션 회전만 부드럽게 잇는다. 정조준할수록 카메라의 영향이 줄며, 사다리 반복 등반과 매달린 채 유지하는 자세는 마우스가 이끈다.
- 카메라 회전은 머리의 앞·위 방향을 투영해 뒤집힌 동작을 작은 각도로 접어 계산한다. 구르기가 90°를 넘을 때 오일러 각 분해로 생기는 불필요한 좌우 회전·기울기를 막으며, 화면 전체가 머리와 함께 뒤집히지는 않는다.
- 카메라를 눈에 붙이고, 벽에서 빼고, F5로 뒤에 두고, 근거리 평면을 정하는 일은 따로 떨어진 클래스 [BodyCamera](../Scripts/Player/Body/BodyCamera.cs)가 한다(2026-11-09 분리). `PlayerBody.Look`이 매 프레임 눈 위치, 기울이기가 나른 거리, 시선을 넘긴다.
- 카메라는 KINEMATION 1인칭 카메라처럼 눈(FPSAF가 조준경을 맞추는 머리의 카메라 소켓)에 붙는다. 머리의 눈에서 `eyeViewAhead`만큼 앞이다. 정조준할 때도 눈에 그대로 있다. 걸음의 흔들림과 착지·구르기의 꺼짐은 애니메이션에서 온다. Q/E로는 몸이 기운 만큼만 옆으로 간다. 회전은 마우스 시선에 이동 효과(Beatrun 펀치 등, [PlayerCameraEffects](../Scripts/Player/Camera/PlayerCameraEffects.cs))를 더하고, 마지막으로 KINEMATION 총의 사격 흔들림과 클립 카메라 뼈의 움직임을 더한다([FPSBodyCamera](../Scripts/Player/FPSBody/FPSBodyCamera.cs)). `eyeViewTime`은 사다리·주먹질 때 기준 위치로 옮겨 가는 시간이며 마우스 회전을 늦추지 않는다.
- 사다리에서는 눈 시점을 기준 카메라 위치로 부드럽게 옮겨 고정한다. 상승·하강 클립의 머리 높이 차이가 상하 반복 입력 때 화면을 밀지 않게 하며, 사다리 하강 종료 끄덕임은 `Punch.ladderSlideStopNod` 기본 0으로 둔다.
- 눈 위치는 아바타에 연결된 LeftEye·RightEye의 중점이다. 눈 사이 간격은 캐릭터 크기에 비례해 검사하므로 Hiro처럼 눈이 넓게 배치된 캐릭터도 실제 눈 뼈(`pd.013`·`pd.014`)를 쓴다. 모델 기본 자세의 앞 방향은 양팔 뼈로 구한다. Hiro의 기본 자세가 모델 -Z를 향해도 눈과 `eyeViewAhead`·`eyeAdjust`를 해부학적 앞 방향으로 계산해 머리 로컬 좌표에 저장한다. 눈 뼈가 없거나 적합하지 않으면 같은 앞 방향과 카메라 높이로 추정한다. 위치를 조정하려면 Play 중 `Body.eyeAdjust`(위;앞)를 고친다.
- 벽 근처에서 내려다보면 SpineLook이 굽힌 허리 때문에 얼굴이 벽에 닿을 수 있다. 그때는 얼굴이 닿지 않을 만큼만 허리 굽힘을 되돌리고(반씩 좁혀 찾는다), 머리는 보던 곳을 그대로 본다([PlayerBody.Clearance](../Scripts/Player/Body/PlayerBody.Clearance.cs)의 `ClearFace`). 그 뒤에도 남는 충돌은 플레이어 캡슐 안의 일정한 기준점에서 눈까지 카메라 부피를 쓸어 검사한다([CameraClearance](../Scripts/Player/Camera/CameraClearance.cs)). 이 부피는 시야각·화면 비율·근거리 평면의 모서리까지 고려한다.
- 몸은 머리가 카메라 기준점 바로 뒤에 오도록 계속 따라오므로(`headFollowTime`), 카메라는 플레이어 위치에서 옆으로 멀리 벗어나지 않는다. 이때 머리는 시선으로 굽히기 전 클립의 머리를 쓴다. 내려다본다고 몸이 뒤로 밀리지 않는다.
- 평소 위아래를 보면 허리(`spineBendDown`·`spineBendUp` 비율)·목(나머지의 0.3)·머리(0.7)가 나눠 굽는다. 허리 몫은 척추 뼈(Spine·Chest·UpperChest)가 똑같이 나눈다. 좌우로 비틀 때는 허리 60%·목 20%·머리 20%, Q/E 기울기는 허리 뼈만 나눈다. 순서는 기울기, 비틀기, 위아래 순이고, 위아래는 방금 비튼 만큼 돌아간 오른쪽 축으로 굽힌다(CAS Look과 같은 순서라 CAS를 뺀 뒤에도 몸이 같게 움직인다). 총을 들어 올린 자세(하이 레디)에서도 똑같이 굽는다. 예전에는 들어 올리면 허리를 폈는데, 그러면 눈과 총이 바닥에서 떨어져 총이 다시 내려가고 허리가 다시 굽기를 반복했다(소총을 들고 앉아 바닥을 볼 때). 들어 올린 총은 4cm 여유가 생기고 0.5초가 지나야 내려간다. 걸을 때 머리가 흔들리며 경계를 넘나들어도 걸음마다 들썩이지 않게 하려는 것이다. 이 0.5초는 경계 근처에서만 기다린다. 12cm 넘게 비켜났으면 바로 내려가고, 4cm 여유가 생긴 뒤 쏘기나 정조준을 누르고 있으면 기다리지 않고 약 0.1초 만에 내려온다(`WeaponController.WideClearance`, `FPSBody.QuickLowerHalfLife`). 내려오는 동안 누른 한 발은 0.3초 안에 총이 다 내려오면 그대로 나간다. 반자동 총에서 총을 내리려고 누른 클릭이 사라지지 않게 하려는 것이다(2026-11-09 사용자 요청: 다시 쏠 수 있는데도 하이레디가 너무 늦게 풀림).
- Q/E 기울이기는 손에 든 것에 따라 세 가지다([BodyLean](../Scripts/Player/Body/BodyLean.cs), 2026-11-08 사용자 결정). 2026-11-09부터 기울이기는 따로 떨어진 클래스(`BodyLean`)이고, 수치는 그대로 `Player.csv`의 `Body.leanSpineBend`·`Body.leanHipShift`·`Body.leanHipReach`다([PlayerBody.Lean](../Scripts/Player/Body/PlayerBody.Lean.cs)이 매 프레임 넘긴다). 아무것도 안 들면 0.7.34 방식이다. SpineLook이 척추를 허리부터 옆으로 `Body.leanSpineBend`(20°)만큼 휘고, 골반이 `Body.leanHipShift`(0.08m) 옮겨 가며, 엿보는 거리의 나머지는 카메라가 메운다. 주먹이면 같은 20°에 골반이 최대 `Body.leanHipReach`(0.2m)까지 옮겨 눈이 엿보는 거리에 닿고, 카메라는 눈에 붙는다. 총이면 FPSAF가 눈이 엿보는 거리에 닿을 만큼 척추를 기울인다(KINEMATION 방식). 다른 사람 화면에서도 똑같이 기운다.
- 골반이 시선과 다른 쪽을 보면 가슴·목·머리가 시선 쪽으로 비틀린다. 제자리에서 다리를 디딘 채일 때와 전력질주로 골반이 돌 때도 적용한다.
- 일반 슬라이딩 중 하체는 플레이어 캡슐에 대한 위치와 월드 진행 방향(`SlideDirection`)을 유지한다. 시선만 돌려서는 다리 위치·방향이 달라지지 않으며, 실제 이동·조향을 따라간다. 상체는 부드럽게 따라가고 머리의 최종 시선은 입력에 맞춘다. 좌우는 `Body.slideTurnLimit`(기본 각 75°), 위아래는 `Body.slidePitchLimits`(기본 위 40°·아래 55°)로 제한하며, 상체와 카메라가 같은 범위를 쓴다. 미끄러지는 방향이 시선의 반대쪽(벽에 튕겨 뒤로 밀림, 뒤로 슬라이딩 시작)이면 시선과 다리는 그대로이고 몸이 뒤로 미끄러진다. 옆으로 튕겨 기존 시선이 새 범위를 벗어나면 그 범위 안으로 서서히 돌아온다.
- 순서: 카메라 효과(200)가 회전 효과와 시야각을 정하고, 몸(250)이 눈 위치에 카메라를 놓는다. 그다음 FPSAF(330)가 KINEMATION 흔들림과 카메라 뼈 움직임을 더한다. 시선 입력은 계속 `LookPivot`에서 읽으며, 완성된 카메라를 다시 머리 입력에 넣지 않는다.
- Esc로 일시정지하면 멈춘 순간의 휴머노이드 뼈 자세를 기억했다가 매 프레임 다시 씌운다([PlayerBody.Pause](../Scripts/Player/Body/PlayerBody.Pause.cs)). KINEMATION 프레임워크가 Animator 그래프 안에서 돌기 때문에 Animator는 끄지 않는다. 카메라 효과와 눈 시점도 멈춘 채 유지된다.
- **내 머리카락 보기**(Esc 설정, 기본 켬): 켜면 머리·머리카락(`First Person Shadow Only`에 적힌 렌더러)을 그린다. 카메라가 그 안에 있는 부분은 근거리 클리핑(`Body.headNearClip`, 0.05m)으로 잘린다.

## 몸의 손

코드는 [PlayerBodyHands](../Scripts/Player/Body/PlayerBodyHands.cs)와 [ArmIK](../Scripts/Player/Body/ArmIK.cs), 수치는 `Player.csv`의 `Hands` 행이다.

클립이 팔 자세를 잡은 뒤, 팔을 굽혀 손목을 목표에 놓는다. 팔꿈치는 자연스러운 쪽으로 향하고, 손이 닿지 않으면 어깨가 그쪽으로 돈다(`shoulderReach`). 손 방향은 캐릭터의 손가락 뼈로 잰 "손가락 방향·손바닥 방향"으로 정하므로 캐릭터를 바꿔도 맞는다.

카즈사와 히로의 손은 손목 1개와 엄지·검지·중지·약지·새끼손가락 각 3마디로, 한 손 16개·양손 32개 뼈가 모두 Humanoid에 연결되어 있다. 손끝 뼈가 없어도 스킨의 끝을 측정한다. [HandGrip](../Scripts/Player/Body/HandGrip.cs)은 클립을 재생하기 전 기본 자세에서 각 마디 길이와 손가락 두께, 스킨의 손끝과 손바닥 중심·두께를 잰다. 그 뒤 손바닥 중심을 접촉 위치에 놓고 다섯 손가락을 각각 표면에 닿는 범위까지만 굽힌다. 봉에서는 엄지를 다른 손가락 반대쪽으로 모으며, 렛지에서는 엄지의 벌림과 마디 굽힘을 모델의 편안한 기본 자세로 유지한다. 실제 지지면에 닿을 때만 밑마디를 작은 범위에서 보정한다. 손가락의 캡슐 근사뿐 아니라 실제 스킨 웨이트를 가진 마디별 표본 정점도 움직여 관통을 검사한다. 표본 사이 삼각형에는 작은 여유를 둔다. 봉 단면이나 손에 대한 표면 위치가 같으면 계산한 자세를 재사용하므로, 잡은 채로 있는 동안 정밀 탐색을 매 프레임 반복하지 않는다. 피부 전체를 변형하는 물리 시뮬레이션은 아니다.

- 총: 무기 모델 기준의 손목 위치·손가락 방향·손바닥 방향을 [Weapons.csv](../Resources/Tables/Weapons.csv)의 `rightGrip*`(방아쇠 손)·`leftGrip*`(받치는 손)에서 읽는다. 총 자세(지향·정조준·벽에 막혀 든 자세·반동·장전)는 지금처럼 카메라 기준 코드가 정하고 손이 따라간다. 지향 자세는 `holdOffset`만큼 몸 쪽으로 당겨서 팔이 닿게 했다(Hiro의 팔 길이는 약 0.51m). 벽에 막혀 총을 든 자세(하이레디)는 시선의 좌우만 따른다. 그래서 내려다보면 고개만 숙이고 총과 상체는 든 채로 있다.
- 손은 무기 코드 다음(실행 순서 320)에 놓이므로 총보다 늦게 따라오지 않는다. 넘기·올라서기·매달리기·벽 동작·구르기·봉 동작 중에는 손이 총을 놓는다.
- 주먹: [FistsView](../Scripts/Player/Combat/FistsView.cs)가 카메라 아래에 막기·충전·주먹 위치만 정하고, 몸의 손이 그 자리로 간다. 예전 상자 장갑은 없앴다. 주먹을 올린 동안 손가락은 `Body.fistClench` 클립을 따라 주먹을 쥔다. 빈손으로 앞으로 달리는 동안(`Hands.fistsRunSpeed` 3m/s 이상)은 손이 달리기 클립대로 흔들리고, 달리지 않는 상태가 `Hands.fistsRaiseDelay`(0.3초) 이어져야 주먹이 올라온다. 이 PC의 플레이어는 W 입력과 달리려는 속도로 판단하고, 다른 PC의 복제본만 실제 움직임으로 판단한다. 실제 움직임 방향은 화면을 돌릴 때 뒤처지고 다음 장애물에 막히면 멈춰서, 넘기 직후마다 손바닥을 편 손이 얼굴 앞으로 올라왔다 내려갔다(2026-11-06 사용자 영상).
- 주먹질: [PlayerBody.Layers](../Scripts/Player/Body/PlayerBody.Layers.cs)가 이동 클립 위의 층에서 주먹 클립을 재생한다. 골반과 다리는 이동 클립 그대로라 걸으면서 칠 수 있다. 치는 팔은 클립을 따르고(`Body.punchBlendIn`·`punchBlendOut`) 반대 손은 주먹 위치에 남아 화면 구도를 지킨다. 왼손·오른손이 번갈아 나가고, 차징이 `Body.punchHardCharge`를 넘으면 `punchLeftHard`·`punchRightHard`를 쓴다. 판정은 버튼을 뗀 순간에 시작되므로, 클립은 다 뻗기 4~7프레임 전부터 잘라(`clips.csv`의 `from`·`to`) `Body.punchClipSpeed`로 재생한다. 클립의 상체가 머리를 앞으로 데려가므로 그동안 카메라는 `Body.punchEyeSteady` 비율만큼 고정 위치 쪽으로 옮겨 간다. 가드나 패링을 시작하면 클립은 바로 팔을 돌려준다. 클립이 없으면 예전처럼 주먹 위치만 움직인다.
- 난간: [PlayerBodyHands.Ledge](../Scripts/Player/Body/PlayerBodyHands.Ledge.cs)가 두 손의 실제 윗면을 따로 찾고(`ledgeHandSpacing`), 그 면의 높이·기울기에 손바닥을 놓는다. 손바닥 중심은 모서리 바로 안쪽에 두며(`Hands.ledgePalmInset`, 손바닥 길이의 기본 15%), 엄지는 아래 손가락 항목의 편한 자세를 쓰고, 표면에 닿으면 닿지 않을 만큼만 모델의 기본 자세 쪽으로 편다. `PlayerBody` 프로필의 각 `Skins` 항목에서 `Ledge Hand Spacing Scale`과 `Ledge Palm Inset Scale`을 조절하면 캐릭터별 손 간격과 걸치는 깊이를 바꿀 수 있다(둘 다 기본 1). 실제 손바닥·손가락 치수는 해당 모델에서 다시 측정한다. 기본 자세로도 지지면에 닿으면 밑마디만 최대 12도 안에서 여유를 만든다. 모서리 밖의 빈 공간은 실제 지지면과 구분한다. 다른 네 손가락과 사다리의 기존 잡기 자세는 유지한다. [HandSkinCorrection](../Scripts/Player/Body/HandSkinCorrection.cs)은 엄지 밑부분의 접힌 피부를 최대 2.5mm 범위에서 완화하고 표면 방향을 접촉 자세에 맞춰 보정한다. UV 경계의 같은 위치를 묶어 틈이 생기지 않게 하며 지지면 안으로 밀어 넣지 않는다. 다른 네 손가락에는 적용하지 않는다. 실제 모델 메시의 복사본에 보정을 만들고 원본 모델과 기존 표정은 유지한다. 놓을 때는 접촉 가중치에 따라 해제한다. 끝부분에서 손이 허공을 짚으려 하면 안쪽의 윗면을 찾는다. 손가락은 지지 콜라이더를 뚫지 않는 범위까지만 굽힌다. 상자 지지는 선분과 상자의 거리로 계산하고, 다른 지지는 표면을 검사한다.
- 난간의 손가락 자세는 잡기로 정한 손바닥·손목 목표 좌표에서 계산한다. 실제 손목의 미세한 추적 오차나 시선 회전은 잡기 자세를 다시 풀게 하지 않는다. 모든 손가락을 기본 자세로 돌린 뒤 한 번에 계산해 이웃 손가락의 클립 동작이 해에 섞이지 않도록 했다. 실제 접촉 모양이 바뀔 때의 손가락 전환도 부드럽게 이어진다. 이 계산을 위해 잠시 사용한 목표 좌표는 복구하며 실제 손목 뼈를 이동하거나 늘리지 않는다.
- 사다리: [PlayerBodyHands.Ladder](../Scripts/Player/Body/PlayerBodyHands.Ladder.cs)가 실제 가로대에 잡는 위치를 기억한다. 마지막으로 올라간 손은 한 칸 위를 잡고, 다음 동작 때 반대 손만 그 위의 가로대로 옮긴다. 지지하는 손은 클립이나 몸의 이동을 따라 밀리지 않는다. 이동하는 손은 앞쪽으로 살짝 떼었다가 다시 잡고, [HandGrip](../Scripts/Player/Body/HandGrip.cs)이 손가락을 가로대 둘레로 굽힌다. 간격·손 이동 궤적·손바닥 여유는 `Hands.ladderHandSpacing`·`ladderReachArc`·`ladderPalmClearance`다. 빠르게 내려갈 때는 양옆 세로대를 잡는다. 손바닥은 세로대의 앞쪽·바깥쪽 모서리를 비스듬히 잡아 엄지는 안쪽에서, 다른 네 손가락은 바깥쪽에서 감싸도록 한다. 하강 중 손은 어깨보다 팔 길이의 25% 아래를 잡고 팔꿈치는 아래·바깥쪽으로 내려, 좁은 사다리에서도 팔이 눈 시점 안으로 들어오지 않게 한다. 손가락 접촉도 실제 사각 단면의 방향을 유지해 모서리에 맞춰 계산한다. 이 방향은 사다리의 회전을 따른다. 가로대·세로대 사이에서는 손 위치와 회전을 이어 주고 손가락을 잠깐 풀었다가 다시 감싼다. 하강 후 가로대를 다시 잡는 양손의 진행률은 따로 갱신하므로, 새 상승을 시작해도 반대 손의 잡기가 중간에 멈추지 않는다.
- 편한 엄지: 잡지 않은 손의 엄지는 클립을 따르지 않는다. 리타깃을 거친 엄지는 캐릭터마다 기본 자세가 달라 손에서 벌어져 나오기 때문이다. 대신 그 캐릭터의 기본 손에서 재서, 손가락 축을 중심으로 손바닥 면 아래 `Hands.thumbUnderPalm`(55°)까지 돌리고 바깥 두 마디를 `Hands.thumbBend`(20°)씩 손바닥 쪽으로 굽힌다. 모델이 이미 더 아래로 둔 엄지는 그대로 둔다. 주먹과 총에서는 클립을 따른다.
- 엄지는 사다리에서도 모델의 기본 마디 방향과 굽힘을 보존한다. 세 마디를 일렬로 정렬하지 않으며, 밑마디·가운데·끝마디를 각각 최대 20·30·40도 범위에서 접촉까지 굽힌다. 피부가 닿을 때 밑마디를 드는 보정도 최대 20도다.
- 가로대는 실제 사각 단면과 모서리에 맞춰 손가락을 굽힌다. 손바닥의 피부 두께를 먼저 반영하고 `Hands.ladderPalmClearance`(기본 1.5mm)를 더한다. 손가락 피부의 표면 여유는 사다리·난간 공통 `Hands.fingerSurfaceClearance`(기본 1mm)다. 작은 손이나 굵은 봉은 손이 닿는 범위만 감싸며, 뼈를 늘리거나 손가락을 표면 안으로 밀어 넣지 않는다. 손을 옮길 때는 먼저 펴고, 다음 가로대에 도착하며 다시 감싼다.
- 봉([PlayerBody.Bar](../Scripts/Player/Body/PlayerBody.Bar.cs), [PlayerBodyHands.Bar](../Scripts/Player/Body/PlayerBodyHands.Bar.cs)): Beatrun은 곧게 매달린 `swingstraight` 자세를 흔들리는 위치에 맞춰 휘는데, 우리 클립은 그 곧은 자세뿐이다. 그래서 몸 전체가 발을 중심으로 진자 각도만큼 돈다. 봉의 매달림 길이는 손에서 발까지라서 발은 캡슐 자리에 남는다. 그다음 손이 봉에 닿도록 몸을 옮기고(`PlayerBody.Grip`), 두 손은 어깨너비 간격으로 봉을 위에서 감싼다. 손가락은 봉 충돌체에서 잰 실제 굵기로 굽힌다. 몸은 흔들리는 방향 중 시선에 가까운 쪽을 보고, 시선은 그 방향에서 좌우 `Body.barTurnLimit`(45°, Beatrun과 같음)까지 돌아간다. 놓으면 `Body.barReleaseTime`(0.3초)에 걸쳐 발을 중심으로 다시 똑바로 선다. 다른 사람 화면에서는 같은 자리의 봉을 찾아 똑같이 잡는다.
- 사다리 꼭대기: 올라서기의 `ObstacleTarget.ExitLadder`로 출발한 사다리를 이어받는다. 높은 손은 고정하고 낮은 손을 같은 가로대로 모은 뒤, 몸을 당기는 동안 양손이 계속 가로대를 잡는다. 팔이 닿지 않으면 눈 시점을 놓기 전에 몸도 필요한 만큼 옮긴다. 이후 높은 손·지지 손 순서로 부드럽게 놓는다(`Hands.ladderExitRelease`, 동작 진행률 기준). 놓은 손은 올라서기 클립과 장애물 윗면 보정을 따르며, 다른 곳의 일반 올라서기는 사다리 잡기를 사용하지 않는다.
- 넘기·올라서기([PlayerBodyHands.Edge](../Scripts/Player/Body/PlayerBodyHands.Edge.cs)): 매달린 뒤 올라가기를 뺀 동작의 손이다. 클립별 시간표나 캐릭터별 수치 없이 그 캐릭터의 팔 길이로 판단한다. 클립의 손이 모서리 자리에 가장 가까워졌을 때 팔 길이의 `Hands.edgeCatch`(1)배 안이면 `Hands.edgeEase`(0.1초)에 걸쳐 짚는다. 높은 넘기(`OverHigh`)와 높은 올라서기(`OntoHigh`)는 시작부터 손이 클립 대신 짚을 자리로 `Hands.edgeReach`(0.2초)에 걸쳐 뻗어 가고, 팔이 닿는 거리가 되면 짚는다. 손가락은 손이 닿은 뒤에야 쥔다. 두 클립 모두 손을 머리 위에서 시작해 늦게 윗면으로 내리는데 캡슐은 그동안 벽 앞으로 다가가서, 손이 윗면 위·너머 허공으로 뻗었기 때문이다(2026-11-06 사용자 지적). 뻗는 동안의 손은 몸을 뒤처지게 하지 않는다. 짚은 뒤에는 어깨가 팔 길이의 `Hands.edgeArmUse`(0.95)배 안에 머물도록 몸이 캡슐보다 뒤처진다(`EdgeHoldBack`). Beatrun 클립은 몸이 캡슐보다 크게 뒤로 처져서, 손을 윗면에 붙이려면 팔만으로는 닿지 않기 때문이다. 필요한 만큼만 뒤처지므로 팔이 닿으면 몸은 움직이지 않는다. 뒤처짐이 팔 길이의 `Hands.edgeHoldBack`(0.6;1.8)배 구간에 들어서면 조금씩 놓아주고, 그래서 팔이 다 펴지면 손을 떼며 다시 짚지는 않는다. 그 뒤 몸은 `Hands.edgeCatchUp`(초당 팔 길이 3배)으로 캡슐을 따라잡는다. 디뎌 오르기에서는 손이 닿지 않고, 한 손 넘기는 한 손만 짚는다. 낮은 넘기(`Over`, `Kong`)는 발이 윗면을 넘는 궤적을 지켜야 해서 몸을 뒤처지게 하지 않는다. 두 손 간격은 어깨 너비에 `Hands.ledgeHandSpacing`을 곱한 값이다. 손이 윗면 아래로 들어가면 실제 손바닥 두께만큼 윗면 위로 천천히 올린다. 높은 넘기(`OverHigh`)에서 윗면을 밀고 뗀 손은 넘기와 그 클립이 끝까지 재생될 때까지 뗀 자리의 높이보다 올라가지 않는다(`KeepPushedHandsDown`). 클립이 들어 올리는 만큼만 눌러 두어 튀지 않고, 끝나면 천천히 놓는다. `vaultoverhigh`는 몸이 윗면을 한참 넘은 뒤 그 손(재 보면 오른손, 동작의 3분의 1쯤에서 뗀다)을 얼굴 앞 머리 높이로 들어 올리는데, 얇은 벽 너머에는 짚을 데가 없어 1인칭에서 허공을 누르는 것처럼 보였다(2026-11-06 사용자 영상). 총·주먹·가로대·봉을 쥔 손은 건드리지 않는다.
- 치마: 클립이 손을 치마 자리에 두면 치마가 손 뒤로 눌린다. 치마를 다 누르고도 손이 묻히면 그만큼만 팔이 어깨에서 살짝 벌어진다(위의 **치마·겉옷·머리카락**). 무언가 잡은 손은 그대로다.
- 총과 주먹은 카메라가 아니라 눈 시점([PlayerView](../Scripts/Player/Camera/PlayerView.cs))에 붙는다. 1인칭에서는 눈 시점이 카메라와 같은 자리라 달라지는 것이 없다. F5에서는 카메라만 뒤로 빠지고 총·주먹·근접 판정은 몸의 눈에 남는다. 총알은 손의 총구에서 화면 가운데 조준점이 가리키는 곳으로 나간다.
- 동작마다 총을 든 손은 [TraversalHands](../Scripts/Player/Movement/Parkour/TraversalHands.cs)가 정한다. 넘기와 허리 높이 올라타기는 오른손에 총을 든 채 왼손만 짚고, 디뎌 오르기는 양손이 총에 남는다. 높은 올라서기·매달리기·벽·봉·사다리·집라인은 총을 넣고 맨손으로 하며, 끝난 뒤 0.25초 지나 다시 꺼낸다(Beatrun은 오르는 동작마다 무기를 숨긴다). 착지 구르기와 크게 착지하기도 두 손이 땅을 짚으므로 총을 넣는다. 장전은 두 손을 다 쓰므로, 장전 중에 시작한 넘기와 올라타기는 구르기처럼 총을 넣고 맨손으로 하며 장전은 끊긴다. 손이 필요 없는 디뎌 오르기는 장전을 그대로 이어 간다. Beatrun은 이때 총을 손에 쥔 채 구르는 전용 동작(`merollgun`, 제자리 착지는 `landgun`)을 쓰고, 이 클립들은 [Art/Animations/Beatrun](../Art/Animations/Beatrun)에도 들어 있다.
- 손에 보이는 총은 KINEMATION 총(MX16A4, M1911)이고, 몸이 KINEMATION FPS Animation Framework로 든다([FPSBody](../Scripts/Player/FPSBody/FPSBody.cs)). 총 클립은 KINEMATION 팔 뼈대용 원본을 KINEMATION Retarget Pro가 스킨마다 몸에 맞게 다시 만든 것이다([GunClipRetarget](../Editor/Import/GunClipRetarget.cs), [GunBakes](../Scripts/Player/FPSBody/GunBakes.cs)). FPSAF가 상체를 굽히고 비틀고 기울이며 두 손을 총에 두므로, 총을 든 동안에는 SpineLook의 시선 굽힘(`UpperBodyFromFramework`)과 PlayerBodyHands의 총 손 IK가 쉰다. 매달리기·사다리처럼 손이 총을 놓으면 총은 오른손이 잡은 모양 그대로 오른손을 따라간다. 플레이어 프리팹의 `Gun`은 모델 없는 뿌리라서 총구(`MuzzlePoint`)만 달려 있다.
- 총을 든 채 옆 회피(`StepDodge`)를 하면 회피 클립은 다리에만 돈다([PlayerBody.Dodge](../Scripts/Player/Body/PlayerBody.Dodge.cs), 발차기와 같은 다리 마스크). 골반·상체·머리는 서 있는 클립 그대로이고 그 위에서 FPSAF가 총을 든다. 클립 전체를 돌리면 회피의 상체 동작과 총을 잡는 손이 같은 뼈를 두고 겹쳤다(2026-11-09 사용자 요청). 회피도 FPSAF가 총을 쥐는 자세로 친다(`PlayerBodyHands.ShootingStance`). 서기·앉기만 칠 때는 회피하는 동안 FPSAF가 총을 놓았다 다시 잡아서, 손이 총에서 떨어졌다 붙었다. 맨손이나 주먹이면 예전처럼 클립 전체가 돈다.

### 난간 잡기

매달려 있는 동안 몸은 어디를 보든 벽을 향한다. 클립이 자세를 잡은 뒤에는 양손의 측정된 손바닥 접촉 위치에서 손목 목표를 계산하고, 그 목표에 맞춰 몸 전체를 옮긴다. 손과 몸이 같은 기준을 쓰며 눈 시점도 그 뒤에 놓는다. 손 접촉을 계산할 수 없을 때는 기존 `Body.ledgeGrip` 위치를 몸 정렬의 기준으로 쓴다. 좌우 이동 클립은 실제 이동 속도에 맞춰 빨라진다(`shimmyClipSpeed`).

## 발 IK

코드는 [FootPlanting](../Scripts/Player/Body/FootPlanting.cs)이고 [PlayerBody.Feet](../Scripts/Player/Body/PlayerBody.Feet.cs)가 표 수치를 넘겨 켠다. GMod IK Foot 애드온의 방식을 따랐다. 2026-11-07에 KINEMATION CAS Foot IK로 바꿨다가 무릎이 걸음마다 옆·뒤로 비틀려 이 방식으로 되돌렸다. CAS에서 자기 캐릭터 충돌체를 땅으로 치지 않는 점만 가져왔다(플레이어의 캡슐·히트박스·든 총은 땅이 아니다).

서 있거나 웅크려 땅에 있을 때(착지 동작 포함, 넘기 마무리 동작 제외) 발마다 뒤꿈치·발목·발끝 아래로 땅을 찾아(`Body.footReach`) 가장 높은 곳에 선다. 그래서 발 일부가 계단 속으로 들어가지 않는다. 한 발의 땅이 캡슐 바닥보다 낮으면(내려가는 계단·경사) 그 발이 닿도록 골반을 내리고(최대 `maxHipDrop`), 높은 쪽 발은 무릎을 굽혀 땅에 맞춘다. 무릎은 클립의 무릎 축으로만 굽고, 다리가 제 길이 축으로 돌지 않아서 발목이 꼬이지 않는다(GMod IK Foot도 허벅지·종아리를 앞뒤로만 굽힌다). 거의 편 다리는 무릎 축이 분명하지 않아서 골반 좌우 방향을 축으로 앞으로 굽는다. 발은 그 아래 땅 높이만큼만 옮기므로 클립의 걸음과 발 들기는 그대로 남는다. 땅을 디딘 발은 경사에 맞춰 기운다(`maxFootTilt`). 골반이 내려가면 눈과 카메라도 같이 내려간다.

- 땅과 골반 높이는 월드 기준으로 기억한다. 그래서 캡슐이 계단을 한 칸씩 툭 오르내려도 발이 계단을 뚫지 않는다. 높아진 땅에는 바로 올라서고, 낮아진 땅과 골반은 `footSettleTime`에 걸쳐 내려간다.
- 발바닥은 어느 부분도 땅 아래로 들어가지 않는다. 다른 비율에 맞춘 클립은 발을 바닥에 박을 수 있다. 깊이 앉으면 발목이 너무 낮고, 발이 긴 캐릭터(예: Sil, 발목에서 발끝까지 약 18cm)는 발끝을 내리는 순간 앞코가 바닥을 파고든다. 그래서 뒤꿈치와 발끝 아래 발바닥이 땅 위에 오도록 발목을 그만큼 올리고, 무릎은 그만큼 더 굽힌다. 발끝은 발가락 뼈 끝(발끝 뼈가 있으면 그것)까지 잰다. 모두 그 캐릭터의 기본 자세에서 잰다.
- 발의 수평 위치는 애니메이션을 따른다. 발밑을 놓쳤을 때 마지막 높이·기울기 보정은 `footSettleTime`에 걸쳐 풀고, 다시 지지를 찾으면 같은 시간에 보정의 영향을 늘린다. 실제 지지가 없는 발은 발소리를 내지 않는다. 이 표현은 물리 접지를 결정하지 않는다.
- 실행 중 Scene 뷰에서 플레이어를 선택하면 발마다 찾은 땅(초록 선)과 발목 목표(노란 구)가 보인다.
- 팔·발 IK는 같은 두 뼈 계산([TwoBoneIK](../Scripts/Player/Body/TwoBoneIK.cs))을 쓴다.
- **발목 제한**([AnkleLimit](../Scripts/Player/Body/AnkleLimit.cs)): 모든 상태에서 발이 정강이 축으로 돌아간 각도(`Body.maxAnkleTurn`)와 옆으로 꺾인 각도(`Body.maxAnkleRoll`)를 Hiro 기본 자세 기준으로 자른다. 발끝을 위아래로 젖히는 것은 자르지 않는다. 리타깃이 잘못된 클립이나 섞인 클립, 다리 IK 때문에 발목이 꼬여도 실제 발목이 가능한 범위 안에 머문다.

## 사다리·집라인의 몸

클립은 [PlayerBody.Fixtures](../Scripts/Player/Body/PlayerBody.Fixtures.cs)가 고른다. 사다리에서는 [PlayerBody.Grip](../Scripts/Player/Body/PlayerBody.Grip.cs)이 어깨를 사다리 중앙의 등반 거리로 맞추고, [PlayerBodyHands.Ladder](../Scripts/Player/Body/PlayerBodyHands.Ladder.cs)가 양손을 각각 실제 가로대에 고정한다. 오르는 손은 `LadderLeftHand`와 실제 한 칸 이동 진행률(`LadderStepProgress`)을 따른다. 한 칸 오르기와 멈춤은 MoCap Online의 가로대 간격 400mm 클립(`Ability.ladderRungHeight` 0.39m와 맞다)이고, 잡기·꼭대기 넘기·미끄러져 내려오기는 게임의 짧은 시간에 맞는 Beatrun 클립이다. 원래 두 칸을 오르는 반복 클립을 `clips.csv`의 `to`로 절반만 쓰고, `Body.ladderClipSpeed`로 `Ability.ladderRungInterval`에 맞춘다. `ladderRungInterval`을 바꾸면 이 값도 함께 바꾼다. 클립 이름의 `LU`는 왼발이 위라는 뜻이라 손은 반대로 오른손이 위다. 집라인에서는 몸 전체를 옮겨 손을 줄에 맞추며 개별 손 IK는 아직 없다. 몸이 향할 수 있는 범위는 `Body.fixtureTurnLimit`이다.
