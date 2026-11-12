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

임시 폴더의 `Assets/...`를 프로젝트로 옮길 때는 파일마다 `.meta`를 먼저, 바로 뒤에 그 파일을 복사한다(유니티가 `.meta` 없는 파일을 먼저 보면 GUID를 둘 만든다). 새 폴더의 `.meta`도 유니티가 만들기 전에 쓴다.

유니티 패키지(Dekogon 등)는 고른 프리팹과 그것이 쓰는 것만 들이는 `import_unitypackage.py`(GUID·가져오기 설정 그대로, 텍스처 2048 제한, LODGroup의 남의 렌더러 뺌)로 들인 뒤 `tga_to_png.py`로 줄인다.
