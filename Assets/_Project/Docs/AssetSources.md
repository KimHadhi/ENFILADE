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
| `Art/Models/Characters/KazusaBand` | 91개, 108MB | 블루 아카이브의 카즈사. `KazusaBand.kkbp.json`으로 보아 코이카츠 모델 변환본으로 추정. 어디서 받았는지 기록이 없다 |
| `Tools/Blender/Source/Hiro` | 저장소 안, Assets 밖 | 클립을 구울 때 쓰는 Hiro 뼈대 MDL(워크숍 3744034369) |

Hiro 모델(`Art/Models/Characters/Hiro`)은 허락을 받은 캐릭터라 교체 대상이 아니다.

## 라이선스 안에서 유지

구매했거나 무료로 공개된 에셋이다.

| 위치 | 파일 | 출처 | 조건과 메모 |
| --- | --- | --- | --- |
| `Art/Audio/Combat/BOOM`, `Art/Audio/Movement/PushHard` | 88개, 13MB | BOOM Library Violent Combat(구매) | 1인 라이선스, 음원 공유 형태의 재배포 금지 |
| `Art/Animations/MocapOnline` | 21개, 36MB | MoCap Online(구매) | AI 학습이나 새 에셋 생성에 쓰지 않는다 |
| `Art/Animations/Mixamo` | 28개, 13MB | mixamo.com, X Bot | |
| `Assets/Vefects/Combat Flipbook VFX URP` | 688개, 328MB | Vefects(구매) | 재색 사본이 `Prefabs/Effects/Parry`, `Art/Materials/Parry`에 있다 |
| `Assets/JMO Assets/Cartoon FX Remaster` | 483개, 35MB | Cartoon FX Remaster Free | |
| `Resources/Combat/Parry`의 `ParryFlare` | 1개 | Kenney Particle Pack(CC0) | |
| `Assets/MagicaCloth2` | 328개, 146MB | Magica Cloth 2 | `Example (Can be deleted)` 143MB는 예제라 git에서 제외했고 지워도 된다 |
| `Assets/ThirdParty/TextMeshPro` | 37개, 4MB | Unity TextMeshPro 기본 리소스 | |
| `Assets/ThirdParty/KoreanFonts` | 54개, 37MB | Pretendard, 에스코어 드림, 페이퍼로지, KOTRA 볼드, 사천항공체 | 무료 배포 폰트로 보이며 라이선스 파일은 없다 |

## 출처 확인이 필요한 것

프로젝트에 기록이 없어서 이름과 커밋만으로는 판단할 수 없다. 출처를 알게 되면 위 표로 옮긴다.

| 위치 | 파일 | 아는 것 |
| --- | --- | --- |
| `Art/Audio/Combat/Hitfullness` | 23개, 3MB | 총기 명중·처치에 겹치는 타격 소리. 커밋 0.6.0에 추가 |
| `Art/Audio/Combat/Defense` | 16개, 1MB | 가드·패링·공격 예고 소리. 커밋 0.5.0에 추가 |
| `Art/Audio/Combat/Player` | 28개, 2MB | BOOM으로 바꾸기 전의 주먹·피격 소리. 현재 연결하지 않은 클립이 있다 |
| `Art/Audio/UI/MissionObjective.wav` | 1개 | 임무 알림 소리 |
| `Art/Sprites/Weapons` | 7개, 5MB | 피, 총구 섬광, 벽 착탄 텍스처. 커밋 0.5.0에 추가 |
| `Art/Sprites/UI/HUD` | 56개, 2MB | HUD 이미지. 직접 만든 것인지 기록이 없다 |
| `Art/Models/Props/TetoPear` | FBX 1개 | 물리 프롭 시험용. 커밋 0.6.3에 추가 |
| `Assets/ThirdParty/InfimaGames/LowPolyShooterPack` | 16개, 1MB | 씬의 소총 `SK_AR_01`. 구매 기록이 없다 |
| `Assets/DAVFX/Realistic Impacts Pack` | 266개, 473MB | 착탄 이펙트 팩. 구매 기록이 없다 |

## 아직 들어오지 않은 후보

설치된 워크숍 애드온 121개의 평가와 출처는 [워크숍 애드온 이식표](https://claude.ai/artifact/TjkB8PvDvQ1dnhJr27FEbz)에 있다. 여기서 프로젝트로 가져오는 에셋은 위 첫 번째 표에 추가한다.
