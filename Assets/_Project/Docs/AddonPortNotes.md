# 애드온 이식 기록

[프로젝트 안내](../../../README.md) · [이동 안내](MovementGuide.md) · [플레이어 몸](PlayerBodyGuide.md) · [설정·유지보수](MaintenanceGuide.md)

GMod 애드온(`F:\Addons`, 저장소 밖)의 이동·애니메이션·소리를 ENFILADE로 옮긴 기록이다. Lua 코드를 그대로 가져오지 않고 규칙과 수치를 Unity 구조에 맞춰 다시 만들었다. 애니메이션과 소리는 변환해서 넣었다.

## 어디서 무엇을 옮겼나

| 애드온 | 옮긴 것 | 지금 위치 |
| --- | --- | --- |
| Beatrun Community Edition | 넘기·올라서기 판정과 이동식, 사다리·집라인, 시점 펀치 값, 세게 착지, 숨소리 모델, 동작 소리와 재생 순서 | [VaultFinder](../Scripts/Player/Movement/Parkour/VaultFinder.cs), [VaultMotion](../Scripts/Player/Movement/Parkour/VaultMotion.cs), [Parkour](../Scripts/Player/Movement/Parkour), [PlayerMovementPunch](../Scripts/Player/Camera/PlayerMovementPunch.cs), [MovementLanding](../Scripts/Player/Movement/Actions/MovementLanding.cs), [PlayerBreathing](../Scripts/Player/Audio/PlayerBreathing.cs), [SoundCues.csv](../Resources/Tables/SoundCues.csv) |
| Beatrun Actual Thirdperson animations | 여성 3인칭 세트(`tp_climbanim_f`)를 Hiro 뼈대에 구운 클립 | [Art/Animations/Beatrun](../Art/Animations/Beatrun) |
| Eye View Attachment for Beatrun | 카메라를 몸의 눈에 붙이기 | [PlayerBody.Look](../Scripts/Player/Body/PlayerBody.Look.cs) |
| 2026 IK Foot (High FPS Version) | 발 IK. 2026-11-07 CAS Foot IK로 바꿨다가 무릎이 비틀려 되돌렸고, CAS에서 자기 충돌체를 땅으로 치지 않는 점만 가져왔다 | [FootPlanting](../Scripts/Player/Body/FootPlanting.cs) |
| Anim-Based Footsteps Timing | 발이 실제로 닿는 순간의 발소리 | [PlayerFootsteps](../Scripts/Player/Audio/PlayerFootsteps.cs) |
| Dynamic Landing | 크게 착지해 쓰러지기, 돌진 밀치기, 착지·구르기·밀치기 소리 | `MovementLanding`, [PlayerCombatActions](../Scripts/Player/Combat/PlayerCombatActions.cs), [Art/Audio/Movement](../Art/Audio/Movement) |
| FEAR-like SFX for Various VManip Addons | 일부 표면의 착지·슬라이드, 포복, 렛지, 기울이기, 옷 소리 | `Art/Audio/Movement`, 나머지는 [Art/Audio/Library](../Art/Audio/Library) |
| [wOS] DynaBase, Mixamo 확장 | 착지 클립을 시험했다. 지금은 mixamo.com에서 직접 받은 클립으로 바꿔서 남아 있지 않다 | [Art/Animations/Mixamo](../Art/Animations/Mixamo) |
| [MW] Quick Throwing Knife | 소리만 보관. 기능은 이후 전투 작업에서 다룬다 | `Art/Audio/Library/ThrowingKnife` |
| Advanced Stealth | 소리만 보관. 좀비의 시야·의심·소음 듣기·수색·등 뒤 암살은 따로 만들었다([ZombieMind](../Scripts/Enemies/ZombieMind.cs), [ZombieSenses.csv](../Resources/Tables/ZombieSenses.csv)). 이 애드온의 수식을 옮겼다는 기록은 없다. 분대 AI는 옮기지 않았다 | `Art/Audio/Library/AdvancedStealth` |
| Horde : Wave Survival (GMod 워크숍 2401598805) | 돈 떨구기(G로 묶음을 떨궈 동료에게 주기), 웨이브 막바지에 남은 적을 벽 너머로 보이기. 총의 관통력 수치는 위 ArcCW 행 | [MoneyDrops](../Scripts/Economy/MoneyDrops.cs), [EnemyReveal](../Scripts/Combat/Waves/EnemyReveal.cs) |
| [ArcCW] Arctic's Customizable Weapons (Base) (워크숍 2131057232, Horde가 쓰는 무기 베이스) | 총알 관통과 도탄(`DoPenetration`, `GetRicochetChance`, `PenTable`), 총마다 관통력은 Horde의 ArcCW 무기 수치(M4A1 10, G17 4), 도탄 소리, 앉아 쏘기 배율 규칙(`arccw_mult_crouchdisp`·`arccw_mult_crouchrecoil`, 값은 TFA 베이스의 0.5·0.65), 자동 장전(`arccw_automaticreload`), 뷰모델 흔들림(`cl_viewmodel.lua`의 coolsway 중 움직임·걸음·숨쉬기. 숨쉬기는 총구가 들리게 바로 세웠고 질주에서 커지는 출렁임은 뺐다. 시선 흔들림은 아래 TFA 것). 몸만 ArcCW와 다르다(2026-11-06 사용자 요청): 두께와 상관없이 몸 하나에 `pierceCost` 2mm만 깎고, 들어간 자리에서 다시 나가 붙어 선 몸도 맞힌다. `Flesh`의 재질 계수는 ArcCW의 0.1로 되돌렸고, `pierceCost`를 비울 때만 쓴다 | [BulletPenetration](../Scripts/Combat/HitDetection/BulletPenetration.cs), [AttackTrace](../Scripts/Combat/HitDetection/AttackTrace.cs), [Weapons.csv](../Resources/Tables/Weapons.csv), [Surfaces.csv](../Resources/Tables/Surfaces.csv), `Art/Audio/Bullets/Ricochet` |
| TFA Base (GMod 워크숍 2840031720) | 시선 흔들림(Sway)은 2026-11-07부터 KINEMATION FPS Animation Framework의 Sway 레이어로 바꿔 지웠다. 앉아 쏘기 배율 값 0.5·0.65. 잔탄 경고음 규칙(`shared.lua`의 `EmitLowAmmoSound`: 쏜 뒤 탄창 3분의 1 미만이면 총마다의 경고음, 비어 갈수록 크게(`RequiredVolume`), 마지막 발은 마지막 발 소리, `autodetection.lua`대로 4발 이하 탄창은 없음). 경고음과 함께 CS2 딸깍을 내는 것은 사용자가 고른 조합이다(2026-11-06) | [Weapons.csv](../Resources/Tables/Weapons.csv), `WeaponFeedback.AmmoCue`, GunFeel `Low Ammo Fraction`·`Low Ammo Small Magazine` |
| Accurate Gun Reverb (L4D2 워크숍 3629393613) | 쏠 때마다 발밑에서 9방향 광선으로 방 크기를 재고(양끝 2개씩 버린 합 ÷ 7, 위가 500유닛 넘게 트이면 실외) 총과 방에 맞는 잔향 꼬리를 겹치기. 꼬리 15개. 유니티 때문에 다른 점: 길찾기 영역 대신 2m 칸마다 기억, 광선이 사람을 지나감, 동시 꼬리 수 제한 | [GunReverb](../Scripts/Audio/GunReverb.cs), `Art/Audio/Guns/Reverb` |
| Hi-Octane Weapon Sounds: Re-Charged! (L4D2 워크숍 3810863466) | 총소리만: 권총·소총·샷건 발사, 장전, 빈 총 소리 | `Art/Audio/Guns` |
| 소음 무기 모드 5개 (L4D2 워크숍 1280137683·1324155748·790038322·1154171177·3354970327, 사용자가 2026-11-10에 고름) | 소음기를 단 발사음만. 모델과 재질은 쓰지 않았다 | `Art/Audio/Weapons/Suppressed`, 총마다의 연결은 `Data/Combat/Weapons/<총>.asset`의 Suppressed Fire Sounds |
| Low Ammo Sound Cue (L4D2 워크숍 3198155663) | 한때 잔탄 경고음 규칙이었다(쏜 뒤 남은 탄이 탄창의 20%(올림)이자 적어도 3발 이하면 매 발 같은 딸깍). 2026-11-06에 사용자가 TFA 베이스의 규칙과 소리를 골라 바꿨다(위 TFA 행). 이 애드온의 `click.mp3`는 CS2의 `lowammo_01`을 키워 찌그러뜨린 것이어서 CS2 원본으로 바꿨다 | 없음 |
| mighty foot engaged (GMod 워크숍 1987521428) | 장전 중 V 발차기의 시간표와 판정: 0.2초에 시점 펀치(-10, 좌우 ±2)와 휘두르는 소리, 0.3초에 몸 가운데에서 시선이 머무는 곳 쪽으로 키 높이만큼 선 판정 후 4유닛 훑기, 피해 20~30, 맞히면 차는 사람 100유닛/초 뒤로(공중 두 배), 재사용 0.75+0.5초. 문 열기·부수기는 옮기지 않았다 | [ShoveMoves](../Scripts/Player/Combat/ShoveMoves.cs), Player.csv `Shove` |
| ArcCW 근접 도약 (위 ArcCW의 `sh_bash.lua` DoLunge) | 서서 밀칠 때 사거리 너머 64유닛 안의 조준한 적에게 거리 × 5(공중 2.5)의 속도로 도약, 앉아 있으면 안 함 | [ShoveMoves](../Scripts/Player/Combat/ShoveMoves.cs) |
| Beatrun addon compatibility layer, Landing Anim-Fix | 옮기지 않았다. GMod 안에서 동작하는 Lua 패치다 | 없음 |
| Meaty Melee SFX (L4D2 워크숍 3660295000) | 처형의 마지막 일격(등 뒤면 목 꺾기, 앞이면 두개골, 거구처럼 버티면 팔 부러짐), 머리가 터질 때 목의 피 뿜기(2초를 1초로 줄임), 산 좀비의 팔다리가 떨어질 때 피 뿜기와 뼈. 나머지 근접 무기 소리 161개(카타나·칼·도끼·마체테·삽 등의 휘두르기·살·벽 타격, 전기톱)는 보관(2026-11-11) | `Art/Audio/Enemies/Zombie/Bone`·`Gore`, [BodyBreakSounds](../Scripts/Audio/BodyBreakSounds.cs), SoundCues `Execution/…`·`Gore/…`, `Art/Audio/Library/MeatyMelee_3660295000` |
| Fleshy CI Hit Sounds (L4D2 워크숍 3725589072) | 주먹이 좀비에 맞을 때 BOOM 타격음 밑에 깔리는 살 소리 13개(감염자 피격음). 생존자용 3개는 보관 | `Art/Audio/Enemies/Zombie/Hit/Fist`, SoundCues `ZombieHit/Fist` |
| RE4 standard pick up sound (2432848231), [COD:MWII] Shove and Pick-up Sounds (2907759388) | 필드 줍기 소리(탄약 = RE4, 총 = MWII, 실드 = MWII 레이저 켜짐의 앞 무음을 뺀 1초)와 V 밀치기 헛스윙(MWII 휘두름 2개). 오케스트라 히트는 보관 | `Art/Audio/UI/PickAmmo`·`PickGun`·`PickShield`, `Art/Audio/Movement/Shove/Swing`, SoundCues `ShoveSwing` |
| A.V.A Headshot hitsound (2981494447) | 헤드샷 확인음 후보만(쓰이지 않음). 처치 종류별 겹침 구조는 이미 CombatHitAudio에 있다 | `Art/Audio/HitConfirm/HeadAVA` |
| Biblically Accurate L4D1 Weapon Audio (3785893356), Partisan's Alternate Sounds Tweaked (3798403978) | 연결된 Hi-Octane과 비교: 셋 다 L4D2 총 이름의 같은 자리 소리이고, L4D1 팩과 Hi-Octane은 같은 Valve 원음을 다듬은 것(길이·크기가 거의 같다)이라 들어 보지 않고 바꿀 근거가 없어 Hi-Octane을 그대로 둔다. 대신 키가 없어 합성음이던 자리를 채웠다: 탄창을 쓰는 Heavy 총(SRM-12·Mk14 EBR·MX68)의 탄창 빼기·넣기·장전손잡이를 L4D1 팩의 군용 저격총 소리로(샷건 탄 한 발 소리이던 Heavy 탄창 넣기도 바꿈), 구급상자 줍기를 L4D1 팩의 아이템 줍기로. 나머지(총·근접·아이템 250개, Partisan 124개)는 보관 | `Art/Audio/Guns/Heavy/MagOut`·`MagIn`·`SlideBack`·`SlideForward`, `Art/Audio/UI/PickHeal`, `Art/Audio/Library/L4D1WeaponAudio_3785893356`·`PartisanWeapons_3798403978`·`HiOctane_3810863466`(옮긴 샷건 탄 소리) |
| Cry of Fear Blood decals (L4D2 워크숍 3612824343) | 피 데칼 9장(같은 그림 2장은 뺌). 원본은 회색 바탕에 곱하는 mod2x 데칼이라 알파 데칼로 바꿨다: 회색에서 초록·파랑이 얼마나 내려갔는지로 덮임을, 회색 벽에 찍혔을 때의 색으로 피 색을 읽고 가장자리 6텍셀을 흐린다. 매 피격의 혈흔(흰 무늬 + 알파, 색은 GunFeel이 정함)과 고어의 투영 데칼(피 색 + 덮임에서 만든 노멀, RVFX 재질을 본뜸) 두 가지로 넣었다(2026-11-11) | [BloodSplatTextures](../Scripts/Combat/Feel/BloodSplatTextures.cs)·`Resources/Combat/BloodSplats`, `Art/Materials/BloodDecals`·[GoreLibrary](../Resources/Combat/GoreLibrary.asset) `Splats`, 변환 [addon_blood_decals.py](../../../Tools/Blender/addon_blood_decals.py) |
| Improved gore textures and models (3828922073), Melee Blood Remaster (3678772125) | 데칼이 아니라 L4D2 감염자 모델용 상처·절단면·내장 텍스처와 무기에 묻는 피 무늬(`hunter_01_detail`)라 데칼로 쓰지 않고 1K 이하 JPG로 보관했다. 잘린 자리 단면이나 칼날의 피를 만들 때 쓴다. 살점 모델 11개는 L4D2 감염자 체형용이라 들이지 않았다 | `Art/Textures/Addons/GoreTextures_3828922073`·`MeleeBlood_3678772125` |
| 칼 모드 3개: [绝区零] 讨骸刀·无尾 (3802606110), DMC5: Yamato (3532432838), Heavy Cavalry Sword (3661236204) / COD23:MW4 KRAIT P68 (3803570564) | 칼 꺼내기·기병도 휘두르기·타격 소리와 권총·쌍권총 소리 22개를 보관. 모델은 아래 [워크숍 모델](#워크숍-모델-2026-11-11) | `Art/Audio/Library/ZZZKatana_3802606110`·`Yamato_3532432838`·`CavalrySword_3661236204`·`KraitP68_3803570564` |

소리의 표면별 출처는 [사운드 관리](MaintenanceGuide.md#이동목소리-소리-폴더가-곧-목록)에, 프로젝트 전체의 에셋 출처는 [외부 에셋 출처](AssetSources.md)에 있다.

## 워크숍 에셋을 들여올 때

1. 어떤 애드온을 쓸지는 [워크숍 애드온 이식표](https://claude.ai/artifact/TjkB8PvDvQ1dnhJr27FEbz)에서 고른다.
2. [Tools/Workshop/workshop.py](../../../Tools/Workshop/workshop.py)의 `extract`로 애드온을 프로젝트 밖 `F:\GameProject\_WorkshopImport\<워크숍 번호>`에 푼다. 받은 파일의 실행 파일은 실행하지 않는다.
3. 필요한 파일만 변환해서 아래 표의 위치에 넣는다. Source 모델과 애니메이션은 [Tools/Blender](../../../Tools/Blender)의 변환 스크립트를 쓴다.
4. 같은 작업에서 [외부 에셋 출처](AssetSources.md)에 워크숍 번호와 함께 한 줄을 추가한다.

| 종류 | 위치 |
| --- | --- |
| 캐릭터 모델 | `Art/Models/Characters/<이름>` |
| 적 모델 | `Art/Models/Enemies/<이름>` |
| 무기 모델 | `Art/Models/Weapons/<이름>` |
| 애니메이션 | `Art/Animations/<묶음>`. 폴더 표는 `<묶음>.clips.csv`. 좀비는 `Zombie` |
| 총 소리 | `Art/Audio/Weapons/<무기>` |
| 적 소리 | `Art/Audio/Enemies/<적>/<상태>` |
| 근접·타격 소리 | `Art/Audio/Combat/<용도>` |
| 이펙트 | `Prefabs/Effects/<용도>`, 재질은 `Art/Materials/<용도>` |
| 스토어 팩처럼 통째로 받는 에셋 | `Assets/ThirdParty/<팩 이름>` |

## 워크숍 모델 (2026-11-11)

워크숍 무기 모델은 [addon_weapon_to_fbx.py](../../../Tools/Blender/addon_weapon_to_fbx.py)가 무기마다의 레시피([Tools/Blender/Addons](../../../Tools/Blender/Addons)의 `<이름>.json`)대로 바꾼다. L4D2 월드 모델은 원점과 축이 제작자마다 제각각이라(야마토는 코등이에 거꾸로 매달려 있고 KRAIT은 -Y를 겨눈다) 레시피가 손잡이 자리, 칼끝·총구 방향, 위쪽을 적고, 스크립트가 모두 같은 틀로 맞춘다. 무기 로직은 아직 없고, 다음 작업(나이트 근접 무기, 클래스 무기, 쌍권총)이 이 FBX를 그대로 쓴다.

- **틀**: 원점 = 쥐는 자리(칼은 손잡이 가운데, 권총은 L4D2의 `ValveBiped.weapon_bone` 손 붙는 점), 유니티 +Z = 칼끝·총구, +Y = 칼등·가늠자(칼날이 휜 쪽의 반대가 칼등이라 날은 -Y), 실제 크기(SourceIO의 1단위 = 0.01905m가 무기에는 실물 크기다. 캐릭터만 이 프로젝트의 0.025m를 쓴다).
- **표식**: FBX 안의 빈 오브젝트. 칼은 `Guard`(코등이)·`Tip`(칼끝)·`Pommel`(칼자루 끝), 권총은 `Muzzle`(총구)·`SuppressorMuzzle`(소음기 끝)·`LightPoint`(라이트).
- **부품**: 따로 숨기거나 떨굴 수 있게 메시를 나눴다. 칼집에 꽂힌 월드 모델은 칼과 칼집으로 나뉘어 칼만 켜면 뽑은 칼이다.
- **재질**: URP Lit을 모델 안에 두고 [ModelImportRules](../Editor/Import/ModelImportRules.cs)가 `Textures`의 같은 이름 텍스처를 붙인다. 텍스처는 VMT에서 읽어 `$color2` 색조를 구워 넣었고(기본 알파가 가리는 곳만 칠하는 `$blendtintbybasealpha`도 따름) 노멀은 초록을 뒤집었다(Source는 DirectX식). 색 맵은 JPG, 노멀은 PNG, 2K 이하. 매끄러움·금속은 아직 기본값이다(VMT의 퐁 지수 텍스처는 쓰지 않음).
- **가져오기 설정**: GUID를 고정하려고 `.meta`를 함께 넣었다(리그 없음, 읽기 끔). 그래서 처음 가져오기 규칙 대신 `.meta`가 설정을 정한다.

| 모델 | 워크숍 | 부품(삼각형) | 길이 | 텍스처 |
| --- | --- | --- | --- | --- |
| [ZZZKatana](../Art/Models/Addons/Weapons/ZZZKatana) | [绝区零] 讨骸刀·无尾(武士刀) (L4D2 3802606110) | `Katana` 1,037, `Scabbard` 1,608 | 1.01m | 2K 색 + 1K 노멀. 타격 시간표는 원본 `katana.txt`: 재공격 0.5초, 피해 70, 가로 베기1 1.0초(판정 0.10~0.25초), 가로 베기2 1.3초(0.0~0.2초), 강공격 1.0초(0.46~0.78초), 밀치기 0.73초(0.10~0.26초) |
| [YamatoKatana](../Art/Models/Addons/Weapons/YamatoKatana) | DMC5: Yamato (L4D2 3532432838) | `Katana` 18,081, `Sheath` 3,234 | 0.92m | 원본 2048×4096 세 장(132MB)을 1024×2048로 |
| [CavalrySword](../Art/Models/Addons/Weapons/CavalrySword) | Heavy Cavalry Sword (L4D2 3661236204) | `Sword` 23,464 | 1.02m | 칼날·코등이·손잡이 1K, 노멀 2K까지 |
| [KraitP68](../Art/Models/Addons/Weapons/KraitP68) | COD23:MW4 KRAIT P68 (L4D2 3803570564) | `Pistol` 53,010, `Suppressor` 5,717, `Light` 6,371, `Magazine` 5,760 | 0.32m(소음기 포함) | 2K까지 14장. 쌍권총은 같은 모델 두 자루로 쓴다(원본의 쌍권총 월드 모델도 같은 메시) |

**좀비**: [addon_zombie_to_fbx.py](../../../Tools/Blender/addon_zombie_to_fbx.py)가 ValveBiped 뼈대의 감염자 MDL을 세운다(발→머리가 위, 발뒤꿈치→발끝이 앞, 발을 바닥에, 골반을 원점 위에, 0.025m 단위로 키 1.7~1.9m). 이름에 `_Male_`·`_Female_`을 넣어 [ZombieVoice](../Scripts/Enemies/ZombieVoice.cs)가 성별 목소리를 고른다. `.meta`가 휴머노이드(모델에서 만듦, 유니티가 ValveBiped 뼈를 스스로 맞춤)와 읽기 켬(ZombieBody가 메시로 히트박스를 맞추고 팔다리를 자른다)을 정한다. [ZombieLooks](../Resources/Enemies/ZombieLooks.asset)의 `Characters`에는 12종만 더했다(Synty 50종에 섞이는 비율을 낮게 두려고): 저폴리 경찰·CEDA 방호복·폭동 진압·광대, GMod 셤블러 셋(slow1·4·7)·러너 하나(fast1)·Zombie Panic! 넷(jessica·marcus·vanessa·paul). 나머지 61종은 폴더에 준비만 했으니 `Characters`에 끌어 넣으면 바로 섞인다. 모습이 어색하면 그 줄만 빼면 된다.

| 묶음 | 워크숍 | 들인 것 | 크기 |
| --- | --- | --- | --- |
| LowPolyCI_3386016169 (뺌) | Low Poly Common Infected (L4D2 3386016169) | 2026-11-12에 뺐다: 사용자가 저폴리 모습이 별로라고 했다("그거 별로던데"). 0.8.102 eaf923ff의 기록에는 남아 있어 되살릴 수 있다 | 0 |
| [ZombieSNPCs_152529683](../Art/Models/Addons/Zombies/ZombieSNPCs_152529683) | Zombie SNPCs (GMod 152529683) | 21종(2026-11-12에 fast3·gal_boss_mini는 손·아래팔 뼈가 휴머노이드로 잡히지 않아 Unity가 리그 오류를 내서 뺐다). 처음 들인 23종: 셤블러 slow1~12, 러너 fast1·3, Zombie Panic! Source의 일곱(carrier, eugene, jessica, lea, marcus, paul, vanessa), gal_boss와 작은 판(0.4~2.6만 삼각형, 텍스처 1K JPG). 뼈대가 ValveBiped가 아닌 9종(burnzie, draggy, fast2·4, hulk, stalker, zombine 등)은 휴머노이드가 될 수 없어 뺐다 | 19MB |

Walkers [XDR](3353266656)와 [xdR] nmrih zombies(3709350917)는 모델이 아니라 L4D2 감염자 뼈대용 걷기 애니메이션이라 이번에는 들이지 않았다. 좀비 걷기 클립으로 쓰려면 MDL의 시퀀스를 FBX로 구워 `Art/Animations/Zombies/<슬롯>`에 넣으면 된다(그 폴더는 넣는 즉시 좀비 목록에 섞인다).

칼 소리는 `Art/Audio/Library`에 있다: 휘두르기 `MeatyMelee_3660295000/weapons/katana/katana_swing_miss1·2`, 살 타격 `melee_katana_01~03`, 벽 타격 `katana_impact_world1·2`, 꺼내기 `ZZZKatana_3802606110/weapons/katana/katana_deploy_01`·`katana_deploy_1`. 기병도는 `CavalrySword_3661236204/weapons/tonfa`, 권총은 `KraitP68_3803570564`(발사·탄창·슬라이드·쌍권총·빈 총 살펴보기).

## 이식표 묶음별 현황 (2026-11-10 재점검)

[워크숍 애드온 이식표](https://claude.ai/artifact/TjkB8PvDvQ1dnhJr27FEbz)는 애드온 121개(L4D2 70, GMod 51)를 적었고, 그중 35개를 묶음 10개로 골랐다. 표는 좀비가 캡슐 하나뿐이던 때 쓴 것이라 "없음"으로 적힌 칸 여럿이 낡았다. 아래는 GitHub에 올라온 0.8.1의 코드·표·폴더를 보고 다시 매긴 것이다. PC 폴더에만 있는 작업(거구 좀비, 음악, 던지는 캔, 필드 줍기, 구역 환경음)은 보지 못했으므로, 그 작업이 올라오면 "안 함" 칸을 다시 본다.

| 묶음 | 지금 | 남은 것 |
| --- | --- | --- |
| 1. 좀비 감각과 상태 | 핵심은 끝남. 조용함→경계→수색→깸, 시야 광선 3점, 소음 듣기, 등 뒤 암살([ZombieMind](../Scripts/Enemies/ZombieMind.cs), [PlayerExecution](../Scripts/Player/Combat/PlayerExecution.cs)) | 빛·손전등 반영, 벽 재질별 소리 손실, 시체 발견 |
| 2. 좀비 몸과 소리 | 몸·애니메이션·절단은 구매 팩과 자체 구현으로 대체(Synty, Nephilite, [ZombieGore](../Scripts/Enemies/Gore/ZombieGore.cs)). 2026-11-11에 맞을 때의 살 소리(Fleshy CI Hit Sounds), 처형·머리 터짐·팔다리 절단 소리(Meaty Melee SFX)가 들어왔다([BodyBreakSounds](../Scripts/Audio/BodyBreakSounds.cs)). 같은 날 워크숍 좀비 73종(Low Poly CI 50, GMod Zombie SNPCs 23)이 휴머노이드 FBX로 들어왔고 그중 12종이 ZombieLooks에 섞였다([워크숍 모델](#워크숍-모델-2026-11-11)) | 좀비 목소리는 공격할 때의 음성 37개만 들어왔다(0.8.15, [ZombieVoice](../Scripts/Enemies/ZombieVoice.cs), `Art/Audio/Enemies/Zombie`). **평소 신음, 알아챘을 때, 맞을 때, 죽을 때 소리**(RE6 Zombie Sound의 나머지)는 아직이다 |
| 3. 맵 진행과 디렉터 | 뼈대는 L4D식으로 따로 만듦([CampaignDirector](../Scripts/Campaign/CampaignDirector.cs), [Campaign.csv](../Resources/Tables/Campaign.csv)). Horde의 숫자는 쓰지 않음 | 인원수에 비례하는 동시 상한, 엘리트·보스 편성, 엔드리스 |
| 4. 클래스, 퍽, 상점 | 돈, 돈 떨구기, 상점, 능력 카드([Perks.csv](../Resources/Tables/Perks.csv))는 있음. 부착물 메뉴는 ArcCW 배치 | 클래스, 무게 제한, 퍽 트리, 부착물 능력치 겹치기 규칙 |
| 5. 카타나 | 모델·소리만 준비됨(2026-11-11): 칼 세 자루(젠존제 칼, 야마토, 기병도)를 손잡이 원점·칼끝 +Z의 FBX로, 칼 소리는 보관 폴더에([워크숍 모델](#워크숍-모델-2026-11-11)). 근접 무기 로직은 없고 주먹뿐 | 칼 로직(나이트 근접 무기): 젠존제 칼의 `katana.txt` 타격 시간표, TFA의 근접 공격 표·막기 수치. 칼·삽 모델은 `Assets/ThirdParty/LowPolyWeaponBundlePackV`와 Synty에도 있다 |
| 6. 던지는 칼 | 안 함. 소리 43개만 `Art/Audio/Library/ThrowingKnife`에 보관 | 전부 |
| 7. 총소리·잔향 | 끝남([GunReverb](../Scripts/Audio/GunReverb.cs), `Art/Audio/Guns`). 2026-11-11에 L4D1 팩으로 Heavy 탄창 총의 합성음 자리를 채우고 L4D1·Partisan 팩을 보관했다 | 보관본(`Art/Audio/Library/L4D1WeaponAudio_3785893356`·`PartisanWeapons_3798403978`)에 기관단총·저격총·매그넘·M60 등 총 종류별 소리가 있다. 새 총 종류를 더할 때 쓴다 |
| 8. 권총·쌍권총 | 권총은 구매 팩(M1911)으로 대체. KRAIT P68 모델(소음기·라이트·탄창 부품)과 소리 22개가 준비됨(2026-11-11) | 쌍권총 로직. KRAIT의 1인칭 장전 동작(원본 v_ 모델의 시퀀스)은 아직 바꾸지 않았다 |
| 9. 발차기·근접 보조 | 핵심 끝남: 장전 중 V 발차기, 도약 밀치기, 밀치기 카운터([ShoveMoves](../Scripts/Player/Combat/ShoveMoves.cs)) | 독립 발차기 키, 공중 드롭킥 2배, 문 차기 |
| 10. 무기 구조 정리 | 셸 단위 장전과 사격으로 끊기는 있음([Weapons.csv](../Resources/Tables/Weapons.csv)의 `shellReload`) | 작업 우선순위 상태기계, 숨 참기, 무기 살펴보기. 급하지 않다 |

표가 "없음"이라 했지만 그 뒤에 생긴 것: 샷건과 한 발씩 장전, 소음기와 소리를 듣는 좀비, 쓰러짐·부활과 누운 플레이어 뜯어먹기, 래그돌 시체, 머리·팔다리 절단, 상점 구급상자, 디렉터의 긴장도, 죽은 뒤 관전 카메라.

## 단위

Source 단위는 **1 = 0.025m**로 환산했다. Beatrun의 수치를 옮길 때 이 값을 쓴다. 예를 들어 사다리 한 칸은 0.39m, 사다리 미끄럼은 8.5m/s, 집라인 최고 속도는 18.75m/s, 집라인 매달림은 1.875m다.

## Beatrun과 다르게 만든 것

| 부분 | ENFILADE | 이유 |
| --- | --- | --- |
| 시간 | 넘기·올라서기·구르기는 애니메이션이 자연스러운 시간을 쓴다 | Dying Light처럼 묵직한 파쿠르를 원한다 |
| 눈 시점 | 정조준·사다리를 빼고 카메라가 눈을 따른다. 사다리는 상하 전환 중 기준 눈높이를 유지한다 | 사다리의 클립 전환이 화면을 밀지 않게 했다 |
| 손 | 넘기·높은 올라서기 때 손을 실제 모서리에 짚고, 몸은 그 손이 닿는 범위에 머물다 캡슐을 따라잡는다 | 원본은 손 고정이 없다. 판단은 캐릭터마다 실제 팔 길이로 해서 수치를 따로 넣지 않는다 |
| 매달린 뒤 올라가기의 몸 | 캡슐의 경로와 시간은 원본이지만 보이는 몸은 `hangheaveup` 대신 이동을 남긴 `mx_ledge_climb_up`을 쓴다. 손을 짚은 동안 몸은 손에 붙어 올라간다 | `hangheaveup`은 몸이 캡슐보다 최대 0.66m 벽 반대쪽으로 처지고 손도 0.3초쯤 윗면에서 떠서, 1인칭으로 내려다보면 벽 앞 허공에 떠서 허공을 짚는다 |
| 엄지 | 잡지 않은 손의 엄지는 클립 대신 캐릭터의 손에서 잰 편한 자세를 쓴다 | 리타깃을 거친 엄지가 손에서 벌어져 나온다 |
| 몸 크기 | 원본의 77·64 같은 거리를 `HeightUnit`으로 캡슐 키에 맞춘다. 캐릭터별 높이 설정은 없다 | Actual Thirdperson은 모델마다 높이 프리셋을 저장하지만, 여기서는 손 위치를 재서 몸을 맞춘다 |
| 장애물 지정 | `ParkourObstacle`로 물체별 넘기·올라타기를 고정할 수 있다 | 자동 판단이 맵 의도와 다를 때를 위한 추가 기능이다 |
| 적 위로 넘기 | 넘을 수 있다 | 원본은 NPC를 넘지 않지만 기존 동작을 유지했다 |
| 렛지 매달림 | 판정 범위, 위치, 벽을 볼 때의 클립은 기존 것을 쓰고 손을 `Grip`으로 맞춘다. 돌아보기와 올라가기는 원본이다. 돌아볼 때는 몸이 자리를 지키고 남은 손을 IK로 모서리에 놓는다 | 원본은 손이 모서리 77단위 위치에 맞도록 클립과 높이를 맞춰 두었다. 캐릭터마다 다른 팔을 맞추려고 손을 재서 맞춘다. 돌아보기 자세의 손은 몸에 가까워서 그 손에 몸을 맞추면 벽을 뚫는다 |
| 끝 지점 정리 | 끝점에 끼면 0.12초에 걸쳐 빠져나온다 | 원본은 순간이동한다. 눈 시점에서 화면이 튀지 않게 했다 |
| 일찍 끝내기 | 몸이 아무것도 안 닿아도 끝점까지 남은 길이 비어 있어야 끝낸다 | 원본 규칙은 폭 넓은 몸에서만 맞는다. 좁은 캡슐은 장애물 바로 앞에서 끝나 넘지 못했다 |
| 넘기 끝의 착지 | 넘기 경로가 장애물 너머 공중에서 끝나도 발이 땅에 닿을 때까지 넘기 상태로 내려온다(아래 땅이 60단위 안일 때). 내려오는 동안 낙하는 보통 낙하처럼 재고, 착지는 발 한가운데 아래의 바닥으로 판단한다 | 원본은 경로가 끝나면 바로 걷기 상태로 돌린다. 공중에서 상태가 바뀌어 넘기가 급하게 끊기는 느낌이라고 사용자가 지적했다(2026-11-06). 이어서 받은 영상의 '착지 뒤 0.7m 낙하'는 레일 모서리에 섰다가 떨어진 것이었다(아래 줄) |
| 넘기 끝점과 일찍 끝내기 검사 | 넘는 물체에 대해서는 몸 전체를 바닥까지 검사한다(`ParkourDetector.BodyClearOf`). 끝점과, 몸이 다 빠졌는지 보고 일찍 끝낼 때 둘 다 | 원본과 같게 되돌린 것이다. 원본은 vaultpos와 끝낼 때 몸통 전체(바닥 포함)를 검사한다. 이쪽 검사는 캡슐을 바닥에서 0.04m 띄워 해서, 넘기 끝 발 높이(35단위, 0.86m)가 0.9m 레일 윗면 바로 아래라 몸 뒷부분이 레일 위에 걸친 채 끝날 수 있었다. 그러면 레일 모서리에 잠깐 섰다가 떨어져 넘기가 일찍 끊겨 보였다(2026-11-06 세 번째 영상) |
| 벽 오르기와 넘기 | 이 점프로 곧 넘거나 올라설 수 있는 장애물이면 벽 오르기를 시작하지 않고, 벽을 오르는 중에도 넘기를 시도한다. 곧 넘을 수 있는지와 렛지를 잡은 순간의 높은 동작은 벽에 붙었을 때의 자리에서 윗면을 찾는다 | 원본은 벽 오르기가 먼저 시작될 수 있어 윗면이 높은 장애물을 넘기 어렵다. 원본처럼 지금 자리에서 찾으면 벽에서 0.5m쯤 떨어진 순간 얇은 윗면을 놓쳐, 넘어야 할 얇은 벽을 오르고 매달린 뒤 올라섰다 |
| 맨틀 중 시선 | 올라서는 동안 올라가는 쪽에서 좌우 45°까지만 돌아본다 | 원본은 매달린 데서 올라갈 때 시선을 묶지 않지만 자기 머리를 숨긴다. 여기서는 머리가 보여서 돌아보면 목 안이 보인다 |
| 높은 넘기 뒤의 손 | 윗면을 밀고 뗀 손은 넘기와 그 클립이 끝날 때까지 뗀 높이보다 올라가지 않는다 | 원본 `vaultoverhigh`는 몸이 윗면을 넘은 뒤 그 손을 얼굴 앞 머리 높이로 들어 올린다. 얇은 벽 너머엔 짚을 데가 없어 허공을 누르는 것처럼 보였다(2026-11-06 사용자 영상) |
| 착지 구르기 속도 | 수평 2m/s부터 구르고 그보다 느리면 제자리 착지 | 원본 200단위/초(약 4.9m/s)는 걷기 속도와 같아서 걷다가 떨어지면 구르지 못했다(2026-11-06 사용자 요청) |
| 점프 직후 낮은 넘기 | 점프가 오르는 동안 낮은 넘기는 원본 점프(230단위/초, 중력 600)가 지금쯤 발을 둘 높이에서 찾는다. 첫 공중 프레임은 원본의 첫 틱으로 친다 | 이 게임의 점프는 더 높고 빨리 떠서, 60fps에서는 첫 공중 프레임에 이미 0.9m 레일을 찾는 높이(가슴 상자 아래, 발 위 32단위)를 지났다. 그래서 달리며 점프하면 레일을 넘지 않고 그냥 뛰어넘었다 |
| 일찍 누른 점프 | 달리며 낮은 장애물 앞에서 일찍 누른 점프는 넘기가 걸리는 자리까지 최대 3m를 땅에서 빠르게(최소 11m/s) 달려가 바로 넘는다. 달려가기 시작할 때 카메라가 앞으로 살짝 숙고 시야각이 넓어진다 | 원본은 장애물 앞면 약 1.2m 안에서 눌러야 넘는다. 사용자가 대기 말고, 순간이동 없이 속도를 올려 빠르고 시원하게 넘기를 원했다(2026-11-06) |
| 얕은 윗면 | 몸 너비(캡슐 지름)보다 얕은 윗면에는 디뎌 오르기·올라타기를 하지 않는다. `ParkourObstacle`이 Climb Onto면 예외 | 원본은 레일 위로 올라서기도 한다. 달리며 레일을 넘으려다 레일 위에 올라서는 것을 사용자가 불쾌해했다(2026-11-06) |
| 높은 동작의 손 뻗기 | 높은 넘기·높은 올라서기를 시작하면 손이 클립 대신 짚을 자리로 `Hands.edgeReach`(0.2초)에 걸쳐 뻗어 가고, 닿은 뒤에야 손가락을 쥔다 | 두 클립 모두 손을 머리 위에서 시작해 늦게 윗면으로 내리고, 캡슐은 그동안 벽 앞으로 다가간다. 그래서 손이 윗면 위·너머 허공으로 뻗었다(2026-11-06 사용자 지적) |
| 높은 올라서기의 윗면 찾기 | 70단위 앞에 윗면이 없으면 몸이 올라설 수 있는 곳까지 당겨서 찾는다 | 원본은 깊이 약 1.3m보다 얕은 윗면을 놓치고 매달리기로 넘어간다 |
| 높은 동작의 출발 | 캡슐이 0.2초 동안 벽 앞 자리로 간다 | 원본은 캡슐을 순간이동하고 몸만 보간한다. 바닥이 막으면 높이는 유지하고 거리만 좁힌다 |
| 숨소리 | 질주(`Breathing.exertSpeed` 6m/s 초과)·파쿠르·큰 착지에서만 숨이 차고, 숨참이 `audibleStress` 이하면 조용하며, 힘쓰기를 멈추면 숨 간격이 1.25초에서 2.2초로 느려지다 멎는다 | 원본은 1.9m/s만 넘어도 숨이 차서 걷기(4.9m/s)에도 0.5~0.75초마다 숨을 쉬었다. 2026-11-05 사용자 요청(백로그 612) |
| 월 런 | 넣지 않았다 | 벽 달리기 클립 4개도 변환에서 뺐다 |
| 집라인 잡기 | 뛰어오른 공중(Space, 넘기 끝 도약, 렛지 점프, 벽 차기, 봉에서 도약)이나 Space를 누른 채일 때만 손이 줄을 잡는다 | 공중이면 무엇 때문이든 잡게 두니, 사다리 꼭대기에서 넘어가 바닥 0.1m 위에 내려선 몇 프레임 동안 바로 옆 집라인을 잡아 허브 캣워크에 올라설 수 없었다(2026-11-12 사용자) |
| 사다리 높이 | 난간 끝이 오르는 바닥과 같은 높이. 맵 빌더는 지을 때 그 바닥을 잰다([LadderFit](../Editor/World/LadderFit.cs)) | 지붕 위로 1m 넘게 솟은 난간이 필요 이상으로 높다는 사용자 말(2026-11-12). 넘어가기는 2026-11-08부터 지붕에서 끝나는 사다리에 맞춰져 있다 |
| 사다리 꼭대기에서 타기 | 사다리 쪽으로 걸어 나가면 저절로 탄다. 레포데처럼 몸이 사다리에 닿고(몸 앞이 가로대 면에서 0.05m 안, 사다리 폭 안) 이동 키가 가장자리 너머를 향할 때만이다(뒷걸음질도). F는 0.9m 뒤에서 가장자리 너머를 보고 누르면 탄다 | 백로그 141에서는 F로만 탔다. 걸어서도 타게 했을 때 범위가 넓어 걷다가 사다리로 끌려갔기 때문이다. 사용자는 레포데만큼 좁게 알아서 내려가는 것을 원했다(2026-11-12) |

## 아직 남은 일

- **총과 손만 세상 위에 그리기**: 총이 벽을 뚫어 보이지 않게 한다. 손이 몸 메시의 일부라서 메시를 나눠야 한다. 몸 전체를 위에 그리면 발이 바닥 위로 비치고, 별도 무기 카메라는 쓰지 않기로 했다.
- **몸 클립 펀치**: 주먹이 지금은 손 IK로만 뻗는다. `punchleft`·`punchright`·`punchmid`·발차기 클립을 섞는다.
- **총 든 동작 클립**: `Body.armedClips`를 켜서 확인한다. 손 IK가 생겨서 가능해졌지만 아직 시험하지 않았다.
- **사다리·줄의 손 IK**: 했다(백로그 142). 사다리는 `PlayerBodyHands.Ladder`가 손을 가로대에, `PlayerBody.LadderFeet`가 발을 가로대에 올리고, 집라인은 `PlayerBodyHands.Zipline`이 클립의 앞뒤 손 위치 그대로 각 손을 줄 위에 감아 쥔다. 모두 캐릭터마다 손바닥 치수(ArmIK)를 쓴다.
- **총 모델**: 했다. KINEMATION 팩의 총 11자루(권총 M1911 포함)를 쓴다([외부 에셋 출처](AssetSources.md)). 쌍권총은 없다.
- **중력**: 24다. 20도 시험해 볼 만하다.
- **소음을 듣는 적**: 했다. 좀비가 `Noise`를 듣는다([ZombieMind](../Scripts/Enemies/ZombieMind.cs), [ZombieSenses.csv](../Resources/Tables/ZombieSenses.csv)).
- **멀티플레이**: 혼자일 때만 Esc 메뉴가 게임을 멈추게 한다.
