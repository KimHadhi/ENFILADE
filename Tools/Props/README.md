# 소품 들이기 (맵 꾸미기 3단계)

Fab·언리얼·블렌더에서 받은 소품을 유니티 URP 에셋으로 바꾸는 스크립트들. 2026-11-12에 맵 꾸미기 소품 179개(종류 89, Tables/MapArt)를 이걸로 들였다.
받은 원본은 저장소 밖 `F:\FabAssets`에 두고, 언리얼에서 꺼낸 것은 `F:\FabAssets\FromUnreal`에 쌓인다.

| 스크립트 | 어디서 | 하는 일 |
|---|---|---|
| `export_for_unity.py` | 언리얼 | 팩의 메시(FBX, LOD·정점 색 포함, 충돌 메시 뺌), 텍스처(PNG), 재질·블루프린트(T3D 글)와 `manifest.json`(재질 칸, 삼각형 수, 나나이트, 텍스처·숫자 파라미터, 압축·sRGB)을 꺼낸다. 이펙트 폴더는 건너뛴다. 언리얼이 죽어도 다시 돌리면 이어서 하고, 죽게 만든 에셋은 `crashed.txt`에 적고 건너뛴다. |
| `inspect_blend.py` | 블렌더 | .blend 안의 메시(모디파이어 적용 후 삼각형 수), 그림(파일 안에 들었는지, 빠졌는지), 재질별로 어느 그림이 어느 입력에 들어가는지 적는다. |
| `fbx_stats.py`, `catalog.py` | 파이썬 | 압축을 풀지 않고 FBX의 삼각형 수·재질 이름을 읽고, 모든 후보를 한 목록으로 모은다. |
| `thumbs.py` | 블렌더 | 이름 없이 ID만 있는 Quixel 팩을 눈으로 고르려고 ID가 박힌 썸네일 모아보기를 만든다. |
| `plan_props.py` | 파이썬 | 고른 소품 목록. 각 모델의 파일과 텍스처 역할(색·노멀·거칠기·광택·금속도·AO·투명도)을 찾아 `job.json`을 만든다. 언리얼 재질은 파라미터 → 부모 재질 → T3D에 적힌 텍스처 이름(`_BC`·`_N`·`_ORM`·`_RMA`·`_ARM`·`_AOMR`·`_OP`) 순으로 찾는다. |
| `plan_unreal.py` | 파이썬 | 언리얼에서 꺼낸 팩 하나를 따로 들일 때(나중에 받은 팩). 종류 파일(`{"종류": ["SM_이름", ...]}`)에 적힌 메시만, 팩 안 폴더대로 `<dest>/<FiringRange 같은 묶음>/<메시>/<메시>.fbx`에, 재질은 `<dest>/Materials`에 두는 `job.json`과 MapArt에 넣을 경로 `kinds.json`을 만든다. Dekogon 마스터 재질의 겉모습도 옮긴다: 정적 스위치(인스턴스 T3D의 `StaticSwitchParameters`)와 벡터를 부모부터 읽어 색 틴트(`Albedo Color Tint (Base)`, 마스크 하나 `Tint By Mask?` 또는 셋 `Use Base Color Tint (Multiple)?`), 발광(`Emissive?`: 텍스처·색·세기·거듭제곱·채도 빼기), 색의 알파로 오리기(`Opacity?`), 유리(`MM_Glass_01a`·`MM_Reinforced_Glass_01a`). 2026-11-12 Military Training Facility 57개를 이걸로 들였다. |
| `build_props.py` | 블렌더 | `job.json`대로 텍스처를 유니티 순서로 바꾸고(언리얼 노멀은 초록 뒤집기, 거칠기·광택 → 금속도+매끈함 한 장, AO 따로, 2048까지), URP Lit 재질과 모든 `.meta`(경로로 정한 GUID, 모델 속 재질은 이름으로 우리 재질에 연결)를 쓴다. `.blend` 물체는 FBX로, 언리얼 스켈레탈 메시는 정적 메시로 구워 내보낸다. 재질에 `tint`가 있으면 마스터 그래프처럼 선형 색에서 틴트를 섞어 색 텍스처에 굽고, `emission`은 발광 텍스처를 언리얼처럼 셰이딩해 가장 밝은 점으로 나눠 저장하고 세기는 제곱근으로 줄여 `_EmissionColor`(0.6~4)에, `opacityAlpha`는 색의 알파로 오리고, `glass`는 반투명(알파 섞기, 깊이 안 씀, 그림자 없음) 재질로 쓴다. |
| `plan_decals.py` | 파이썬 | 맵 데칼(맵 꾸미기 6단계)로 쓸 Megascans 데칼 세트 목록. 압축에서 색·투명도·노멀만 새 폴더에 풀고, `build_decals.py`의 `job.json`과 Tables/MapDecals 줄(`MapDecals.rows.csv`)을 쓴다. 세트마다 쓰임(group, 놓는 크기, 벽에서 자리, 그리는 거리, 진하기)과 가장자리 처리(누수는 좌우·아래를 흐리게, 낙엽은 불규칙한 둥근 더미로)를 적는다. 낱장 낙엽 모음표·네모가 보이는 얼룩·거의 투명한 것·투명도 맵이 없는 타이어 자국은 뺐다(목록 위 주석). |
| `build_decals.py` | 블렌더 | `job.json`대로 데칼마다 색과 투명도를 한 장(`_Decal.png`, 알파가 투명도)에, 노멀(`_Normal.png`, Megascans는 OpenGL)을 따로, 가장자리에서 늘어나지 않게 Clamp로, 1024(2.4 m 넘는 것은 2048)까지 줄여 쓰고, URP의 `Shader Graphs/Decal`(총알 자국과 같은 셰이더라 새로 컴파일할 셰이더가 없음) 재질과 모든 `.meta`를 쓴다. |
| `tga_to_png.py` | 블렌더 | TGA를 같은 픽셀의 PNG로 바꾸고 `.meta`를 옮겨 GUID를 지킨다(재질 연결은 그대로, 크기는 3~4분의 1). |
| `scene_stats.py` | 파이썬 | 지은 맵이 얼마나 무거운지 씬 파일만으로 잰다(유니티 없이): 렌더러·그리기·삼각형(LOD 첫 단계)·재질·텍스처 수와 그래픽 메모리 추정·그림자 그리는 것(1 m보다 작은 것 따로)·LOD 안의 렌더러·조명(실시간·섞음·구움)·데칼·반사 프로브·충돌체. 프리팹 복사본은 그 프리팹이나 모델(FBX 7·6.1)에서 무엇을 가져오는지 따라가 센다. 다시 짓기 전과 뒤를 같은 방법으로 비교하려고 만들었다(11-14 맵 최적화). `--json`으로 숫자를 파일에 남긴다 |
| `fit_texture_sizes.py` | 파이썬 | 맵 소품 텍스처의 가져오기 크기를 그것을 입는 가장 큰 소품에 맞춘다: 소품의 가장 긴 변 1 m당 2048 px, 2의 거듭제곱 512~2048(UV가 겉면 전체를 감싸서 텍스처가 긴 변보다 넓게 덮인다). 줄이는 것만 `.meta`의 maxTextureSize에 쓰고 이미지는 그대로라 되돌릴 수 있다. 맵의 벽·바닥 재질(Art/Textures/Surfaces), Heist 팩(손에 드는 도구), 맵 밖(프로젝트의 프리팹·에셋)에서 쓰는 재질은 손대지 않는다. `--write` 없이 돌리면 얼마나 줄지 보여 준다(11-14: 131장, 약 392 MB) |
| `render_layout.py`, `render_scene.py`, `contact_sheet.py` | 파이썬, 블렌더 | 지은 맵을 유니티 없이 그림으로 본다(맵 꾸미기를 그림으로 판단하려고, 11-14). `render_layout.py`가 씬 파일에서 모델 복사본(FBX·자리·회전·재질의 텍스처와 색)·회색 상자(자리·크기·벽면 재질의 옆·윗면 텍스처)·조명을 JSON으로 꺼내고, `render_scene.py`가 블렌더에서 그것을 세워 정한 시점(눈·보는 곳·렌즈, 또는 위에서 본 평면)마다 그리고, `contact_sheet.py`가 여러 장을 한 장에 모은다. `render_scene.py`의 네 번째 인자로 목업 스크립트를 주면 빌더에 넣을 배치를 짓기 전에 같은 규칙으로 얹어 그린다(허브 작전 준비실 A안이 이렇게 사용자에게 먼저 보였다). 블렌더는 FBX 6.1을 못 여니 그런 모델(공항 팩의 게시 케이스·컴퓨터 세트 등)은 빠진다 |
| `fix_lod_renderers.py` | 파이썬 | 프리팹의 LODGroup에서 다른 에셋(원본 FBX) 안의 렌더러를 뺀다. Dekogon 프리팹은 FBX의 UCX_ 충돌 메시 렌더러를 LOD0에 넣어 두어서, 놓을 때마다 "registered with more than one LODGroup" 경고가 났다(11-12, 32개 고침). 인스턴스는 그 렌더러를 그리지 않으니 빼도 보이는 것은 같다. `import_unitypackage.py`도 들일 때 같은 일을 한다. 팩의 LOD 단계는 손대지 않는다: 맵 빌더가 놓을 때 사라지는 마지막 단계만 Tables/MapLods 값으로 맞추고, LOD가 없는 모델에는 사라지기만 하는 한 단계 LODGroup을 단다(GreyboxBuilder.Lods, 11-13). |

## 돌리는 법

언리얼(파이썬 플러그인은 명령줄로 켠다. `-NullRHI`로 돌리면 스켈레탈 메시 FBX 내보내기가 "Assertion failed: MeshObject"로 죽는다):

```
UnrealEditor-Cmd.exe <프로젝트>.uproject -run=pythonscript -script=export_for_unity.py -EnablePlugins=PythonScriptPlugin -AllowCommandletRendering -unattended -nop4 -nosplash -nosound
```

블렌더(남이 만든 파일이라 늘 `--factory-startup --disable-autoexec`):

```
blender -b --factory-startup --disable-autoexec --python build_props.py -- copy <job.json> <프로젝트 폴더>
blender -b --factory-startup --disable-autoexec --python build_props.py -- skeletal <job.json> <프로젝트 폴더>
blender -b <파일.blend> --factory-startup --disable-autoexec --python build_props.py -- blend <job.json> <프로젝트 폴더> <번호>
```

나중에 받은 언리얼 팩 하나(유니티가 켜져 있어도 되게, 저장소 밖에서 지은 뒤 `.meta`를 먼저 놓는다):

```
python -I plan_unreal.py <팩> Assets/ThirdParty/<회사>/<팩> <작업 폴더> <종류.json>
blender -b --factory-startup --disable-autoexec --python build_props.py -- copy <작업 폴더>/job.json <저장소 밖 임시 폴더>
```

맵 데칼(같은 방식으로 저장소 밖에서 지은 뒤 옮긴다):

```
python -I plan_decals.py F:/FabAssets <작업 폴더>
blender -b --factory-startup --disable-autoexec --python build_decals.py -- <작업 폴더>/job.json <저장소 밖 임시 폴더>
```

지은 맵을 그림으로 보기(출력 폴더는 절대 경로로. 블렌더는 상대 경로를 드라이브 맨 위에 푼다):

```
python -I render_layout.py <프로젝트 폴더> Assets/_Project/Scenes/Hub/Hub.unity <작업 폴더>/hub.json
blender -b --factory-startup --disable-autoexec --python render_scene.py -- <작업 폴더>/hub.json <시점.json> <작업 폴더>/그림 [목업.py]
blender -b --factory-startup --disable-autoexec --python contact_sheet.py -- <모음.png> <열 수> <배율> <그림.png> ...
```

임시 폴더의 `Assets/...`를 프로젝트로 옮길 때는 파일마다 `.meta`를 먼저, 바로 뒤에 그 파일을 복사한다(유니티가 `.meta` 없는 파일을 먼저 보면 GUID를 둘 만든다). 새 폴더의 `.meta`도 유니티가 만들기 전에 쓴다.

유니티 패키지(Dekogon 등)는 고른 프리팹과 그것이 쓰는 것만 들이는 `import_unitypackage.py`(GUID·가져오기 설정 그대로, 텍스처 2048 제한, LODGroup의 남의 렌더러 뺌)로 들인 뒤 `tga_to_png.py`로 줄인다. 11-14부터: 고르는 표에 `.unity`도 넣으면 장면과 그것이 쓰는 것 전부가 들어오고(4장 터미널), `--exclude`(정규식 JSON)로 팩 씬의 라이트맵·LightingData 같은 것을 빼고, `--keep-existing`은 프로젝트에 이미 있는 GUID를 다시 쓰지 않으며(먼저 들인 것·고친 것을 덮지 않음), `--strip-scripts`는 들이지 않는 스크립트의 컴포넌트를 프리팹과 장면에서 뗀다. `--cap-rule "Textures_Props/=1024"`처럼 경로마다 텍스처 크기 제한을 따로 줄 수 있다. 쓸 때는 GUID마다 `.meta`를 먼저 쓰고, 같은 경로에 GUID가 다른 파일이 있으면 덮지 않고 유니티처럼 `이름 1.확장자`로 둔다. `--dry`는 들어올 파일 수·크기·새 텍스처의 그래픽 메모리를 미리 보여 준다(4장 터미널: 장면 하나에서 2,044개, 새것 1,429개).
