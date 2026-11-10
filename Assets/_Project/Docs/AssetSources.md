# 외부 에셋 출처

[프로젝트 안내](../../../README.md) · [애드온 이식 기록](AddonPortNotes.md) · [설정·유지보수](MaintenanceGuide.md)

프로젝트에 들어온 외부 에셋의 출처를 모은 목록이다. 나중에 무엇을 빼거나 바꿀지 확인하는 용도이고, 크레딧이 아니다. 지금 들어간 다른 게임과 애드온의 에셋은 분위기를 잡기 위한 임시 자료이며, 직접 만든 에셋이 생기면 바꾼다.

**새 외부 에셋을 넣으면 같은 작업에서 이 문서에 한 줄을 추가한다.** 워크숍 에셋은 워크숍 번호를 함께 적는다. 경로는 `Assets/_Project` 기준이고, `Assets/`로 시작하는 경로만 프로젝트 밖의 팩이다. 파일 수와 크기는 2026-11-01에 잰 값이다.

## 나중에 교체할 것

다른 게임과 워크숍 애드온에서 가져온 에셋이다.

| 위치 | 파일 | 출처 |
| --- | --- | --- |
| `Art/Animations/Beatrun` | FBX 2개, 169MB | Beatrun Actual Thirdperson animations(워크숍 3762345523)의 여성 세트를 Hiro 뼈대에 구운 클립 |
| `Art/Audio/Movement` (`PushHard` 제외) | 약 1490개, 63MB | Beatrun Community Edition(워크숍 3467179024, 미러스 엣지 원본). `LandHard`·`LandHeavy`·`Roll`·`Push`는 Dynamic Landing(3823914367), 표면별 `Land`·`Slide`, `Prone`, `Ledge/Reach`, `Lean`, `Cloth/SprintRustle`은 FEAR-like SFX(3586060527) |
| `Art/Audio/Voice/Hiro` | 126개, 5MB | Beatrun의 Faith 숨소리와 기합(미러스 엣지 원본) |
| `Art/Audio/Library/Beatrun` | 197개, 16MB | Beatrun Community Edition. 아직 연결하지 않은 보관본 |
| `Art/Audio/Library/FEAR` | 52개, 3MB | FEAR-like SFX. 아직 연결하지 않은 보관본 |
| `Art/Audio/Library/ThrowingKnife` | 43개, 6MB | [MW] Quick Throwing Knife(3650480912). 폴더 이름으로 보아 Far Cry 4 추출본으로 추정 |
| `Art/Audio/Library/AdvancedStealth` | 2개, 1MB | Advanced Stealth(3827056860) |
| `Art/Audio/Combat/Shove` | 7개, 0.4MB | 파일 이름(`rifle_swing_hit_infected`)으로 보아 L4D2 게임 소리로 추정 |
| `Art/Audio/Bullets/Ricochet` | 5개, 1MB | [ArcCW] Arctic's Customizable Weapons (Base)(워크숍 2131057232)의 `ricochet01`~`05` 도탄 소리. 원래 출처는 기록이 없다(2026-11-04 추가) |
| `Art/Audio/Guns` (`Reverb`, `LowAmmoClick`, `<총>/LowAmmo`, `<총>/LastRound` 제외) | 16개, 1.4MB | Hi-Octane Weapon Sounds: Re-Charged!(워크숍 3810863466)의 권총·소총(M16)·펌프 샷건 발사, 탄창 빼기·넣기, 슬라이드·장전손잡이·펌프, 샷건 탄 넣기, 빈 총 소리. 설명에 Magmacow·Navaro·yoru의 소리 라이브러리에서 가져왔다고 적혀 있다(2026-11-05 추가) |
| `Art/Audio/Guns/Reverb` | 15개, 7.7MB | Accurate Gun Reverb(워크숍 3629393613)의 총 잔향 꼬리(권총·매그넘·기관단총·소총·샷건과 저격 × 좁은 실내·넓은 실내·실외). 원래 출처는 설명에 없다(2026-11-05 추가) |
| `Art/Audio/Guns/LowAmmoClick` | 1개, 11KB | Counter-Strike 2 게임 파일의 잔탄 경고음 `sounds/weapons/lowammo_01`(사운드 이벤트 `Default.NearlyEmpty`)을 WAV로 그대로 꺼낸 것(2026-11-06 추가). 같은 날 넣었던 Low Ammo Sound Cue(워크숍 3198155663)의 `lasc/click.mp3`는 이 소리를 +10dB 키운 것이라 빼고 원본으로 바꿨다 |
| `Art/Audio/Guns/<총>/LowAmmo`, `Art/Audio/Guns/<총>/LastRound` | 6개, 0.7MB | TFA Base(GMod 워크숍 2840031720)의 총 종류별 잔탄 경고음 `lowammo_indicator_ar`·`handgun`·`shotgun`과 마지막 한 발 소리 `lowammo_dry_ar`·`handgun`·`shotgun` 그대로(2026-11-06 추가) |
| `Art/Audio/Enemies/Zombie` | 38개, 2MB | 좀비가 공격을 시작할 때 내는 소리(0.8.15, 2026-11-11). L4D2 워크숍 3675704366 "RE6 Common Infected Sound"(제작자 표기 MR.GDYMH, 소리의 원래 출처는 바이오하자드 6)에서 꺼냈다. 사용자가 애드온 표에서 좀비 목소리용으로 고른 모드다. 365개 가운데 공격 음성 `action/rageat`의 22개(`Attack`, `Attack/Male`, `Attack/Female`)와 분노 비명 `action/rage`의 16개(`Leap/Male`, `Leap/Female`: 러너의 도약)만 넣었다. 길이 1.25초 이하이고 앞부분이 가장 큰 것을 골라 앞뒤 무음을 자르고 크기를 서로 맞췄다. 들어 보지 못하고 길이와 파형 수치로 고른 것이라, 들어 보고 어색한 파일은 폴더에서 지우면 된다. 원본 전체는 넣지 않았고 이 PC의 L4D2 워크숍 폴더에 있다. 쓰는 곳은 [ZombieVoice](../Scripts/Enemies/ZombieVoice.cs), 크기는 Sounds.csv의 `Zombie/…` 행 |
| `Art/Models/Characters/KazusaBand` | 91개, 108MB | 블루 아카이브의 카즈사. `KazusaBand.kkbp.json`으로 보아 코이카츠 모델 변환본으로 추정. 어디서 받았는지 기록이 없다 |
| `Tools/Blender/Source/Hiro` | 저장소 안, Assets 밖 | 클립을 구울 때 쓰는 Hiro 뼈대 MDL(워크숍 3744034369) |

Hiro 모델(`Art/Models/Characters/Hiro`)은 허락을 받은 캐릭터라 교체 대상이 아니다.

## 라이선스 안에서 유지

구매했거나 무료로 공개된 에셋이다.

| 위치 | 파일 | 출처 | 조건과 메모 |
| --- | --- | --- | --- |
| `Art/Audio/Combat/BOOM`, `Art/Audio/Movement/PushHard` | 88개, 13MB | BOOM Library Violent Combat(구매) | 1인 라이선스, 음원 공유 형태의 재배포 금지 |
| `Art/Audio/Movement/Dodge` | 18개, 1.9MB | BOOM Library Violent Combat(구매)의 WHOOSH Dash·Kimono·Phantom Wind를 변형마다 잘라 낸 것(2026-11-05 추가). 11-06에 Dash·Kimono는 앞쪽에 부풀어 오르는 부분을 줄여 다시 잘랐다 | 위와 같다 |
| `Art/Animations/MocapOnline` | 21개, 36MB | MoCap Online(구매) | AI 학습이나 새 에셋 생성에 쓰지 않는다 |
| `Art/Animations/Mixamo` | 28개, 13MB | mixamo.com, X Bot | |
| `Assets/ThirdParty/Vefects/Combat Flipbook VFX URP` | 688개, 328MB | Vefects(구매) | 재색 사본이 `Prefabs/Effects/Parry`, `Art/Materials/Parry`에 있다 |
| `Assets/ThirdParty/DAVFX/Realistic Impacts Pack` | 266개, 473MB | DAVFX(구매) | VFX Graph 패키지가 있어야 재생된다. `Resources/Combat/ImpactLibrary`가 프리팹을, `Art/Materials/Impacts`가 총알 구멍 텍스처를 직접 가리킨다 |
| `Assets/ThirdParty/JMO Assets/Cartoon FX Remaster` | 483개, 35MB | Cartoon FX Remaster Free | |
| `Resources/Combat/Parry`의 `ParryFlare` | 1개 | Kenney Particle Pack(CC0) | |
| `Art/Audio/HitConfirm/Body` | 1개 | Kenney Interface Sounds(CC0, OpenGameArt)의 `tick_002` | 2026-11-04 추가 |
| `Art/Audio/HitConfirm/Head`, `Art/Audio/HitConfirm/Kill`의 `wood_hit_08` | 3개 | rubberduck의 100 CC0 metal and wood SFX(CC0, OpenGameArt): `hammer_03`, `metal_hit_05`, `wood_hit_08` | 2026-11-04 추가 |
| `Art/Audio/HitConfirm/Kill`의 `bfh1_hit_08` | 1개 | rubberduck의 75 CC0 breaking / falling / hit sfx(CC0, OpenGameArt) | 2026-11-04 추가 |
| `Art/Audio/HitConfirm/Kill`의 `qubodupImpactStone` | 1개 | qubodup의 Impact(CC0, OpenGameArt) | 2026-11-04 추가 |
| `Assets/ThirdParty/MagicaCloth2` | 328개, 146MB | Magica Cloth 2 | `Example (Can be deleted)` 143MB는 예제라 git에서 제외했고 지워도 된다. `Resources/Cloth`의 프리셋은 `Res/Preset`의 사본이다 |
| `Art/Models/Characters/Sil` | 33개, 56MB | VRChat 아바타 シルエット; エッセンシャル(イドクロイ, BOOTH items/3361632, 구매) 1_2 패키지의 색 세트 01~04(2026-11-05에 02~04 추가) | 모델 데이터 재배포 금지. 모델은 [sil_model_to_fbx.py](../../../Tools/Blender/sil_model_to_fbx.py)로 변환했다. 텍스처와 껍질 재질 `sil_01`~`sil_04`는 패키지 원본이다. 셰이더 `WES_soft_mod3`은 GrabPass만 URP의 Opaque Texture로 바꿨다. 뼈 재질 `ess01`~`ess04`는 원본(Poiyomi 7.2.41)의 값을 Poiyomi 10 URP에 옮겨 다시 만들었고, 원본에 없던 값은 블룸이 번지도록 올린 발광 세기 2.5(원본 1)뿐이다. 02는 원본에서 뼈 발광이 꺼져 있고 반짝이(Glitter)가 켜져 있다. 대응: 그림자 방식 Toon + Ramp Type 2(셰이드 맵) → `_LightingMode` 4, 그림자 마스크 → `_LightingShadowMasks`, 밝기 제한 없음 → `_LightingCapEnabled` 0(점광원도 `_LightingAdditiveLimited` 0), Indirect Color Mode 1 → `_LightingIndirectUsesNormals` 1, 다른 물체의 그림자는 원본처럼 받지 않음, 림 색 섞기 0.72 → Replace 0.72(Blur 1), 림의 Shadow Mix 0.808 → 폭 0.2763·Shrink In Shadow 0.08(밝은 쪽 폭과 어두운 쪽에서 사라지는 자리를 맞춤), 피부 밑 산란 Power 5·Scale 0.25 → Spread 5·Strength 0.25(빛 색 섞기 1, 그림자 0, 두께 보정 -1), 금속 반사 Metallic 0.58·Glossiness 0.53 → Mochie 0.58·0.53. 원본은 금속 맵(R 금속)과 반사 마스크(`_BRDFMetallicMap` = Rough 맵)를 따로 읽어서, 두 장을 픽셀 그대로 합친 `Mat0N_PackedMaps.png`(R 금속, G 255, B 반사 마스크, A 255, 원본처럼 sRGB)를 새로 만들었다. 원본의 반사율 0.66은 Poiyomi 10에 해당 값이 없어 옮기지 못했다. 원본이 가리키는 `_ToonRamp`(GUID 7b5d5a38…)는 패키지에 없지만, 원본은 셰이드 맵 방식이라 이 텍스처를 쓰지 않았다 |
| `Art/Models/Environment/MountedFluorescentLights` | 7개, 8MB | Poly Haven Mounted Fluorescent Lights(polyhaven.com/a/mounted_fluorescent_lights, Ulan Cabanilla, CC0) 2K. 2026-11-05 추가 | 받은 일곱 종류 중 a만 [fluorescent_light_to_fbx.py](../../../Tools/Blender/fluorescent_light_to_fbx.py)로 내보냈다. 색·법선·발광 텍스처는 받은 그대로이고, URP Lit용으로 glTF판 ARM 텍스처를 `mask_2k.png`(R 금속, G AO, A 매끄러움 = 1 - 거칠기)로 바꿨다. 조명이 달린 프리팹은 `Prefabs/Props/MountedFluorescentLight` |
| `Assets/_PoiyomiShaders` | 1201개, 118MB | Poiyomi Toon Shader 10.0.28 URP판(github.com/poiyomi/PoiyomiToonShader) | 실의 뼈대 재질(`ess01`~`ess04`)이 쓴다. Poiyomi가 정한 경로라 `ThirdParty` 밖에 둔다. 잠근 재질의 셰이더는 `Assets/_LockedShaderCache`에 생기며, 쓰지 않게 된 것은 Thry → ThryEditor → Optimized Shader Cache → Remove Unused로 지운다 |
| `Assets/ThirdParty/TextMeshPro` | 37개, 4MB | Unity TextMeshPro 기본 리소스 | |
| `Assets/ThirdParty/KoreanFonts` | 54개, 37MB | Pretendard, 에스코어 드림, 페이퍼로지, KOTRA 볼드, 사천항공체 | 무료 배포 폰트로 보이며 라이선스 파일은 없다 |
| `Assets/ThirdParty/Hivemind/GunFX/URP` | 565개, 473MB | Hivemind Realistic Gun VFX(Muzzle Flash, Bullet Impact, Ejections) 1.0(구매, Asset Store 360154). 2026-11-06 추가 | 패키지 안의 `GunFX_URP.unitypackage`에서 프리팹(착탄 32표면×3단계, 탄흔 데칼 22표면×3, 탄피 배출, 예광탄, 섬광, 과열 연기, 파티클 데칼, 총구 화염)과 그것들이 쓰는 파일만 넣었다. HDRP 기본판, Built-in판, 쇼케이스는 뺐다. URP판에 든 `Assets/Settings/DefaultVolumeProfile`과 TextMesh Pro 기본 파일 5개는 우리 파일과 GUID가 같아 덮어쓰므로 `TutorialInfo`와 함께 뺐다. 데칼은 PC_Renderer의 Decal 기능으로 그린다 |
| `Assets/ThirdParty/Synty` | 105개, 34MB | POLYGON - City Zombies Pack 1.7.2(Synty Studios, 구매, Asset Store 131930). 2026-11-06 추가 | 좀비 캐릭터(FBX 하나, 휴머노이드), 잘린 조각, 부착 무기, 대체 색 재질과 그것들이 쓰는 PolygonGeneric 셰이더 그래프 2개만 넣었다. PolygonGeneric의 환경·소품, 씬, 열 때마다 로그를 남기는 SyntyPackageHelper는 뺐다 |
| `Assets/ThirdParty/RVFX/BloodEffectsPack` | 129개, 114MB | Blood Effects Pack 5.3.1(RVFX, 구매, Asset Store 295450). 2026-11-06 추가 | URP판(`1_URP`)의 피 데칼(메시·투영)과 내장, 공용 텍스처·스크립트, 설명서만 넣었다. Built-in판(`0_BuiltIn`), 피 튀김, 화면 피 UI, 데모 씬은 뺐다. 트레일 데칼 두 개(`BloodDecal_01/02_Trail_URP`)에는 원본대로 데모용 `CircularMovement`가 붙어 있다 |
| `Assets/ThirdParty/IronSamiStudio/ProceduralHitReactionSystem` | 7개, 0.1MB | Procedural Hit Reactions System 1.1.0(IronSamiStudio, 구매, Asset Store 318532). 2026-11-06 추가 | 스크립트와 설명서만 넣었다. 데모 씬, `Resources`에 든 데모 로봇(빌드에 섞인다), 파이프라인 설치 패키지는 뺐다. 좀비 피격 반응이 쓴다(0.8.14, 2026-11-11): 좀비마다 `HitReactionController` 하나와 부위별 `BodyPartReactor` 여섯을 실행 중에 달고, `ReactionProfile`은 에셋 파일 대신 `Tables/ZombieJolt.csv`로 채운다(`Scripts/Enemies/ZombieJolt.cs`). `HitReactionTrigger`는 쓰지 않는다(피격은 우리 판정이 알린다). 에셋의 파일은 고치지 않았다 |
| `Assets/ThirdParty/LowPolyWeaponBundlePackV` | 1132개, 32MB | Low Poly Weapon Bundle Pack V 1.0(CASTLE BRAVO, 구매, Asset Store 423530). 2026-11-06 추가 | 패키지 안의 URP판(`Low Poly Weapon Bundle Pack V_URP.unitypackage`)에서 프리팹(총 약 40자루, 부착물, 하부 샷건 MasterKey, 유탄발사기, 수류탄, 칼·삽, 아이템)과 그것들이 쓰는 모델·재질·텍스처만 넣었다. Built-in 기본판, HDRP판, 데모 씬은 뺐다. 재질은 URP Lit 그대로다 |
| `Assets/ThirdParty/Layer Lab` | 3668개, 23MB | LAYERLAB 2D Icons 4종(구매): Game Picto Pack 1.0(Asset Store 420994), Picto Icon Pack 01 1.0(365290), Space Rank 1.0.3(72220), Casual Flag Icons 1.0.0(365526). 2026-11-06 추가 | PNG만 넣었다(같은 아이콘의 크기별 사본 포함). SVG 사본, PSD·AI 원본, 데모 씬과 데모 스크립트는 뺐다. Space Rank의 Simple 256 그림 24장은 클래스 등급 휘장으로 `Resources/UI/Ranks/rank_01~24.png`에 복사해 쓴다(6E, 2026-11-09) |
| `Assets/ThirdParty/Piloto Studio` | 304개, 231MB | Realistic Gunslinger VFX 1.0(Piloto Studio, 구매, Asset Store 362716). 2026-11-06 추가 | 프리팹 25개(총구·명중, 수류탄, 공습, 로켓, 폭발, 하이눈, 표식, 버프, 제압 사격)와 그것들이 쓰는 파일만 넣었다. 데모 씬, 쇼케이스·데모 스크립트, PDF 설명서는 뺐다. 프리팹이 가리키지만 팩에 없는 것은 꺼진 렌더러의 재질, 셰이더가 쓰지 않는 옛 `_diff` 텍스처, HDRP 재질 버전 정보뿐이라 화면에는 영향이 없다 |
| `Assets/RVFX/RVFXDestructionSystem` | 76개, 19MB | RVFX Destruction – Mesh Fracture, Debris & GPU Playback 1.0.0(RVFX, 구매, Asset Store 416476). 2026-11-06 추가 | 편집기 DLL이 `Assets/RVFX/RVFXDestructionSystem`과 `Assets/RVFX/Generated` 경로를 코드에 고정해 쓰므로 `ThirdParty`로 옮기지 않고 원래 위치에 두었다. 기본 패키지(DLL, Burst 어댑터, 셰이더, 설명서)와 URP 지원 패키지(`Pipeline Support/URP.unitypackage`)의 내용을 넣었고, Built-in·HDRP 지원과 예제 씬은 뺐다. 굽기 도구가 만드는 파일은 `Assets/RVFX/Generated`에 생긴다 |
| `Assets/ThirdParty/AdvancedZombieAIPro` | 80개, 9MB | Advanced Zombie AI Pro 1.0.1(Finite Machine Studio, 구매, Asset Store 391558). 2026-11-06 추가 | Unity 6.4(6000.4.11f1)에서 만든 팩이다. `ZombieSpawner.cs` 160번 줄의 6.4 전용 `FindObjectsByType` 호출에 `FindObjectsSortMode.None`을 넣어 6.3에서 컴파일되게 고쳤다(주석으로 표시). 스크립트, 기본 컨트롤러, 휴머노이드 데모 클립(Representational)만 넣고, 데모 씬, StarterAssets 모델·소리, 데모 총은 뺐다. 컨트롤러가 6.4 형식(AnimatorController v6)이라 6.3에서 제대로 열리는지는 아직 확인하지 않았다. 게임에는 멀티 4단계에서 코드를 옮겨 쓴다 |
| `Assets/ThirdParty/Nephilite Studios/Zombie Animation Set` | 188개, 33MB | Zombie Animation Set 1.1(Nephilite Studios, 구매, Asset Store 268209). 2026-11-09 추가 | 휴머노이드 좀비 클립 88가지(Root Motion·In Place 두 벌)와 아바타 기준 모델 `Md_Char_Low_Poly_Man`. Unity의 Import 대신 Asset Store 캐시의 패키지를 같은 GUID로 풀어 넣었고, `_Demo (You Can Delete)`(스크립트, 씬, 조명, 재질)는 뺐다. [ZombieLooks.asset](../Resources/Enemies/ZombieLooks.asset)에서 넘어진 좀비의 일어나기(`zombie_getup_FD_01`·`zombie_getup_UP_01`), 조용함·경계 대기(`zombie_unalert_stand_idle_01`·`zombie_alert_stand_idle_01`), 알아챔·으르렁(`zombie_unalert_to_alert_01`·`zombie_alert_to_aggro_01`), 셤블러 잡기(`zombie_grapple_start_01`·`zombie_grapple_hold_01`), 기어 오기(`zombie_FD_*`), 한 팔 공격(`zombie_swipe_attack_01/02`·`zombie_light_attack_01/02`·`zombie_aggro_swipe_attack_01`, 모두 In Place)과 수색 걸음(`zombie_walk_02`, Root Motion)을 쓴다 |
| `Assets/ThirdParty/KINEMATION` | 1545개, 2.4GB | KINEMATION(구매, 2026-11-06 추가): Character Animation System 2.4.2(Asset Store 320309), FPS Addon for CAS 1.4.1(321490, 구매자 무료), FPS Animation Ultimate 3.4.0(240060), Retarget Pro V5 5.3.0(379204), FPS Animation Framework 4.9.3(238641), Tactical FPS Animations 4.1.0(`TacticalShooterPack`, `KShooterCore`; 935개, 1.8GB). 여기에 KINEMATION Discord(구매 인증 채널 `#cas-fpspack-integration`)의 CAS × FPS Animation Pack 통합 패키지, KINEMATION GitHub(github.com/kinemation/demoes, 릴리스 `cas`)의 `CAS_Demo`·`CAS_Demo_FPS_Addon` 중 통합 패키지가 쓰는 7개(`CAS Demo` 폴더)를 더했다 | 공용 코어(KAnimationCore 1.2.2, ScriptableWidget 1.1.0, PropertyBindings 1.1.0, FbxExporter 1.2.1)는 팩마다 판이 달라 가장 새 판 하나만 넣었고, 반동 시스템은 FPS Addon에 든 새 판을 쓴다. 무기는 MX16A4·M1911 두 자루와 공용 동작만 넣었다. 나머지 18자루, 데모 씬, HDRP·Built-in판, PDF 설명서는 뺐다. 통합 패키지가 가리키는 Ultimate의 `.anim`은 FBX 클립의 압축 없는 사본이라 크므로(한 파일 최대 24MB) `.gitattributes`로 이 폴더의 `.anim`만 LFS에 둔다. FPS Animation Framework는 몸이 총을 드는 데 쓴다(FPSBody, 2026-11-06). 그 설정 중 KINEMATION GitHub의 FPSAF 데모(github.com/kinemation/scriptable-animation-system)에서 가져온 휴머노이드 프로필 둘, 리그, 입력 설정, 상체 마스크, IK 모션 7개는 `Assets/_Project/Data/Combat/FPSBody`에 옮겨 두었다(텍스트 에셋만, 데모의 클립·모델은 가져오지 않았다). Tactical은 데모 씬과 공용 라이브러리(우리 것이 더 새 판)를 뺀 전부를 넣었다(2026-11-06, 사용자 선택 A). Tactical의 4K TGA 195개는 무손실 PNG로 바꿨다(6.1GB → 1.1GB, 파일마다 ffmpeg framemd5로 픽셀이 같은지 확인, GUID는 .meta를 옮겨 유지). Tactical이 가리키는데 패키지에 원래 없는 참조 14개(R08 무기 오버라이드 컨트롤러, 캐릭터 컨트롤러 둘, 재질 넷)는 비어 있다 |

## 우리가 고친 곳

서드파티 팩의 파일을 직접 고친 곳이다. 팩을 업데이트하거나 다시 가져오면 덮어쓰이므로, 그 뒤에 다시 적용한다. 코드에는 `ENFILADE:`로 시작하는 주석을 달아 두었다.

| 파일 | 고친 것 | 이유 |
| --- | --- | --- |
| `Assets/ThirdParty/AdvancedZombieAIPro/ZombieSpawner/ZombieSpawner.cs` 160번 줄 | `FindObjectsByType<ZombieHealth>(FindObjectsInactive.Exclude)`에 `FindObjectsSortMode.None`을 더했다 | Unity 6.4에서 만든 팩이라 6.3에 없는 오버로드를 쓴다. 고치지 않으면 프로젝트 전체가 컴파일되지 않는다(2026-11-06) |
| `Assets/ThirdParty/RVFX/BloodEffectsPack/1_URP/Script/Trail/Editor/TrailProjectorSpawnerSystemEditor_URP.cs` 437번 줄, `.../1_URP/Script/Projector/Editor/ProjectorSpawnerSystemEditor_URP.cs` 416번 줄 | Unity 6 이상에서는 `pipeline.renderingLayerMaskNames` 대신 `RenderingLayerMask.GetDefinedRenderingLayerNames()`를 쓴다. 그보다 오래된 Unity에서는 원래 줄을 쓴다 | 옛 API 경고 CS0618. URP 17.3의 `renderingLayerMaskNames`가 바로 이 호출을 돌려주므로 동작은 같다(2026-11-06) |
| `Assets/ThirdParty/KINEMATION/CharacterAnimationSystem/Examples/Scripts/Editor/BasicCasPreset.cs` 24·25번 줄, `.../RetargetPro/Editor/Scripts/Window/RetargetPreviewEnvironment.cs` 25번 줄 | `Assets/KINEMATION/...`, `Assets/CAS Demo/` 경로를 `Assets/ThirdParty/KINEMATION/...`로 바꿨다 | KINEMATION 편집기 코드가 원래 위치를 고정해 쓴다. 바꾸지 않으면 CAS 설정 마법사의 예제 경로와 Retarget Pro 미리보기 바닥 재질을 찾지 못한다(2026-11-06) |
| `Assets/ThirdParty/KINEMATION/CharacterAnimationSystem/Integrations/FPSAnimationPack/Prefabs/Player/FPSAnimationPack_Player_Fullbody.prefab`, `..._Player_Arms.prefab` | 마네킹·팔 뼈에 자식으로 붙어 있던 무기 프리팹 20개 중 넣지 않은 18개(MX16A4·M1911 외)를 지웠다. 카메라의 HDRP 카메라 데이터(HDAdditionalCameraData, 스크립트 GUID `23c1ce4f…`) 컴포넌트 하나도 지웠다. 다른 줄은 그대로다 | 넣지 않은 무기가 깨진 프리팹으로 남지 않게 하려고(2026-11-06). HDRP 컴포넌트는 URP인 이 프로젝트에 스크립트가 없어서, KINEMATION 설정이 이 프리팹을 미리보기 캐릭터로 불러올 때마다 "The referenced script (Unknown) on this Behaviour is missing!"이 떴다. 무기를 더 넣으면 이 두 예제 프리팹은 통합 패키지 원본으로 다시 덮고 두 가지를 다시 적용한다 |
| `Assets/ThirdParty/KINEMATION/CharacterAnimationSystem/Integrations/FPSAnimationPack/Prefabs/Weapons/CAS_FPSPack_MX16A4.prefab` | `m_RemovedComponents`의 둘째 항목 fileID를 `5869339407474300519`에서 `5125579401999066630`으로 바꿨다 | 이 CAS용 변형은 Ultimate 원본의 `FPSWeapon`과 `FPSWeaponSound`를 빼야 하는데, 둘째 항목이 원본에 없는 옛 ID라 `FPSWeaponSound`가 남았다. 그 Awake가 빠진 `FPSWeapon`을 찾다가 NullReferenceException을 냈다. M1911 변형은 원래 맞다. FPSBody도 KINEMATION 총에 남은 두 스크립트를 깨우기 전에 지운다(2026-11-06) |
| `Assets/ThirdParty/KINEMATION/CharacterAnimationSystem/Integrations/FPSAnimationPack/Procedural/PA_CAS_FPSAnimationPack.asset` | 반동 바인딩 두 줄(`OutLoc`, `OutRot`)의 어셈블리 이름을 `Assembly-CSharp`에서 `RecoilAnimation.Runtime`으로, Trigger Discipline 가중치 바인딩의 형식 이름을 `...FPSAnimationPack.CasFPSPlayerController`에서 `...FPSAnimationPack.Scripts.CasFPSPlayerController`로 바꿨다 | 통합 패키지는 반동 시스템이 Assembly-CSharp에 있던 판으로 만들어졌다. 여기서는 FPS Addon 판이 자기 어셈블리에 있으므로 원래 이름으로는 아무것도 찾지 못해, 예제 씬에서 쏴도 총이 튀지 않는다. 형식 이름 하나는 KINEMATION 쪽의 옛 이름이다. 이 설정은 예전 숨은 팔 리그(GunRig, 2026-11-07 삭제) 때 쓰였고 지금 게임은 쓰지 않는다(2026-11-06) |
| `Assets/ThirdParty/KINEMATION/FPSAnimationPack/Animations/MX16A4/Weapon/A_W_MX16A4_Pose.FBX.meta`, `.../M1911/Weapon/A_W_M1911_Idle.FBX.meta` | 가져오기 설정 `isReadable`(Read/Write)을 켰다 | KINEMATION 총에는 총구 뼈가 없어서 GunMuzzle(FPSBody)이 총 메시의 앞면에서 총구 위치를 잰다. 빌드에서 메시를 읽으려면 이 설정이 있어야 한다(2026-11-06) |
| `Assets/ThirdParty/KINEMATION/KShooterCore/Runtime/IKMotion.cs.meta`, `.../TacticalShooterPack/Animations/Procedural/IKMotion_Aiming.asset`, `IKMotion_FireMode.asset` | 스크립트 GUID를 `c31466bf…`에서 `3fa14a61c6864e6b8cc9fad1c7116bfd`로 바꾸고, 그 스크립트를 쓰는 Tactical 에셋 둘의 `m_Script`도 같이 바꿨다 | Ultimate의 `FPSAnimationPack/Scripts/Player/IKMotion.cs`가 같은 GUID를 다른 클래스로 쓴다. 바꾸지 않으면 두 팩을 함께 넣을 수 없다(2026-11-06). Ultimate 쪽은 그대로다 |
| `Assets/ThirdParty/KINEMATION/FPSAnimationPack/Scripts/Player/FPSProceduralAnimation.cs` 16번 줄 | `ikWeight` 필드를 `#pragma warning disable/restore 0414`로 감쌌다. 동작은 그대로다 | KINEMATION이 값을 넣기만 하고 읽지 않는 필드라 스크립트를 다시 컴파일할 때마다 CS0414 경고가 콘솔에 떴다. 이 스크립트는 팩의 팔 리그용이고 게임은 쓰지 않는다(2026-11-07) |
| `Assets/ThirdParty/KINEMATION/KShooterCore/Runtime/KShooterCore.Runtime.asmdef`, `.../TacticalShooterPack/Scripts/TacticalShooterPack.Runtime.asmdef`, `.../Scripts/Editor/TacticalShooterPack.Editor.asmdef` | 새로 만들었다. KAnimationCore, RecoilAnimation, Input System, Collections, TextMeshPro를 참조한다 | Tactical 스크립트는 asmdef 없이 와서 Assembly-CSharp에 들어가는데, 그러면 그때 있던 우리 전역 `WeaponSway`(2026-11-07 삭제)가 Tactical 쪽 이름을 가려 오류 14개가 난다(2026-11-06) |

## 출처 확인이 필요한 것

프로젝트에 기록이 없어서 이름과 커밋만으로는 판단할 수 없다. 출처를 알게 되면 위 표로 옮긴다.

| 위치 | 파일 | 아는 것 |
| --- | --- | --- |
| `Art/Audio/Combat/Hitfullness`, `Art/Audio/HitConfirm/Break` | 23개, 3MB | 총기 명중·처치에 겹치는 타격 소리. 커밋 0.6.0에 추가. 2026-11-04에 `bone_crack` 3개를 체간 붕괴 소리로 `HitConfirm/Break`에 옮겼다 |
| `Art/Audio/Combat/Defense` | 16개, 1MB | 가드·패링·공격 예고 소리. 커밋 0.5.0에 추가 |
| `Art/Audio/Combat/Player` | 28개, 2MB | BOOM으로 바꾸기 전의 주먹·피격 소리. 현재 연결하지 않은 클립이 있다 |
| `Art/Audio/UI/MissionObjective.wav` | 1개 | 임무 알림 소리 |
| `Art/Sprites/Weapons` | 7개, 5MB | 피, 총구 섬광, 벽 착탄 텍스처. 커밋 0.5.0에 추가. 벽 착탄은 DAVFX로 바꾸어 지금은 연결하지 않았다 |
| `Art/Sprites/UI/HUD` | 56개, 2MB | HUD 이미지. 직접 만든 것인지 기록이 없다 |
| `Art/Models/Props/TetoPear` | FBX 1개 | 물리 프롭 시험용. 커밋 0.6.3에 추가 |

## 아직 들어오지 않은 후보

설치된 워크숍 애드온 121개의 평가와 출처는 [워크숍 애드온 이식표](https://claude.ai/artifact/TjkB8PvDvQ1dnhJr27FEbz)에 있다. 여기서 프로젝트로 가져오는 에셋은 위 첫 번째 표에 추가한다.

아직 Asset Store 보관함에만 있는 KINEMATION 팩: FPS Animation Ultimate의 나머지 무기 18자루. 넣을 때 Tactical과 GUID가 겹치는 파일(Ultimate의 AK.asset과 Tactical의 Recoil_AK105.asset 등)을 먼저 확인한다.
