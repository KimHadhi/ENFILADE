# 스크립트 길잡이

수정하려는 기능의 담당 파일부터 찾는다. 프로젝트 실행과 에셋 위치는 [프로젝트 안내](../../../README.md), 조작법은 [이동 안내](../Docs/MovementGuide.md), 검사 실행은 [검증 안내](../Docs/ValidationGuide.md)에 있다.

## 어디를 수정할까?

| 변경할 내용 | 시작할 파일·설정 |
| --- | --- |
| **체력·무기·주먹·처형·적·웨이브·이동·몸·소리 숫자** | [Resources/Tables](../Resources/Tables)의 CSV 표 (컴파일 불필요, [사용법](../Docs/MaintenanceGuide.md#밸런스-표-data-tables)) |
| 표 읽기·적용 방식 | [GameTables](Data/GameTables.cs), [CsvTable](Data/CsvTable.cs), [TableFieldWriter](Data/TableFieldWriter.cs) |
| 걷기·달리기·점프·자세·슬라이드·다이브 수치 | [Player.csv](../Resources/Tables/Player.csv)의 `Move`·`Posture` 행, 필드 선언은 [Configuration](Player/Movement/PlayerMovement.Configuration.cs) |
| 회피·파쿠르·착지·저스트 슬라이드·밀치기·사다리·집라인 수치 | `Player.csv`의 `Ability` 행, 필드 선언은 [MovementAbilitySettings](Player/Movement/MovementAbilitySettings.cs) |
| 이동 상태 전환·처리 순서 | [PlayerMovement](Player/Movement/PlayerMovement.cs), [이동 코드 구조](Player/Movement/README.md) |
| 접지·경사·중력·실제 몸 이동 / 다른 플레이어 위에 서면 그 사람과 함께 움직이기(`Ride`) | [MovementMotor](Player/Movement/Core/MovementMotor.cs) |
| 자세 높이·천장·환풍구·눈높이 | [MovementPostureController](Player/Movement/Core/MovementPostureController.cs) |
| 달리며 쌓이는 속도 | [MovementMomentum](Player/Movement/Actions/MovementMomentum.cs) |
| 착지 단계·구르기·큰 낙하·넉백 | [MovementLanding](Player/Movement/Actions/MovementLanding.cs), [HeavyLanding 상태](Player/Movement/States/) |
| 스텝 회피·기울이기·적 통과 | [Dodge](Player/Movement/Dodge/), [MovementAbilities](Player/Movement/Actions/MovementAbilities.cs) |
| Vault·맨틀·렛지·벽·봉·사다리·집라인 / 동작마다 총을 든 손(구르기, 크게 착지, 장전 중에 시작한 넘기는 총을 넣음) | [Parkour](Player/Movement/Parkour/), [States/Parkour](Player/Movement/States/Parkour/) / [TraversalHands](Player/Movement/Parkour/TraversalHands.cs) |
| 씬에 놓는 사다리·집라인 | [Ladder](World/Ladder.cs), [Zipline](World/Zipline.cs), [사용 안내](../Docs/MovementGuide.md#사다리집라인) |
| 부서지는 문 (모든 PC에서 함께 부서짐, 서 있는 동안만 적의 길을 막음) | [BreakableDoor](World/BreakableDoor.cs), [DoorBreaks](World/DoorBreaks.cs), 길찾기 다시 굽기: Tools > World > 문 길찾기 다시 굽기 ([DoorNavMeshBaker](../Editor/Utilities/DoorNavMeshBaker.cs)) |
| 슬라이딩의 적 접촉·저스트 판정 | [SlideCombat](Player/Combat/SlideCombat.cs) |
| 적의 넘어짐·레그돌·복구 | [EnemyKnockdownPresentation](Combat/Damage/EnemyKnockdownPresentation.cs) |
| 밀치기·돌진 밀치기 / 서서 도약 밀치기(ArcCW)·장전 중 발차기(Mighty Foot Engaged) / 밀치기 피로 | [PlayerCombatActions](Player/Combat/PlayerCombatActions.cs) / [ShoveMoves](Player/Combat/ShoveMoves.cs) / [ShoveFatigue](Player/Combat/ShoveFatigue.cs), [ShoveFatigueHUD](UI/ShoveFatigueHUD.cs) |
| 주먹·가드(드는 동안 느린 걸음)·세키로식 패링(성공 때 발 묶임)·가드 브레이크 | [PlayerMeleeCombat](Player/Combat/PlayerMeleeCombat.cs) (걷는 속도는 `LimitMoveSpeed`로 이동이 묻는다) |
| 적 머리 위에 선 플레이어: 밟힌 적이 털어 떨어뜨리고 주변 적은 위로 친다 | [HeadStandShakeOff](Player/Combat/HeadStandShakeOff.cs), [EnemyHead](Enemies/EnemyHead.cs), Enemies.csv `reachAbove` |
| 완벽 방어·달려들어 치는 반격 / 처형(F, 주먹 상태의 좌클릭 인살) / 달려들 때 재는 몸 사이 거리 | [PlayerPerfectDefense](Player/Combat/PlayerPerfectDefense.cs), [PlayerExecution](Player/Combat/PlayerExecution.cs) / [BodyGap](Player/Combat/BodyGap.cs) |
| Q/E 회피가 성공했을 때의 슬립과 시선 고정(비켜 돌기) / 전투가 카메라에 요청하는 짧은 머리 움직임(숙이기, 돌아보기, 시선 회전) / 화면 가장자리 빛 | [MicroSlip](Player/Movement/Dodge/MicroSlip.cs), [MovementLean](Player/Movement/Dodge/MovementLean.cs) / [CombatHeadMotion](Player/Combat/CombatHeadMotion.cs) / [CombatEdgeGlint](Player/Combat/CombatEdgeGlint.cs) |
| 보이는 몸(진짜 1인칭)·상태별 애니메이션·F5 뒤에서 보기 / 연속 점프의 착지 | [PlayerBody](Player/Body/PlayerBody.cs), [PlayerBodyProfile](Player/Body/PlayerBodyProfile.cs), [사용 안내](../Docs/PlayerBodyGuide.md) / [PlayerBody.Hop](Player/Body/PlayerBody.Hop.cs) |
| 눈 시점·시선 따라 허리·머리 굽히기·Q/E 몸 기울이기(골반 옮기기·옆으로 휘기)·공중에서 등을 대고 나는 몸·카메라 벽 판정 / F5에서도 총·주먹이 따르는 눈 시점 | [PlayerBody.Look](Player/Body/PlayerBody.Look.cs), [PlayerBody.Clearance](Player/Body/PlayerBody.Clearance.cs), [CameraClearance](Player/Camera/CameraClearance.cs) / [PlayerView](Player/Camera/PlayerView.cs) |
| 손으로 총·주먹·난간 잡기 / 봉에 매달려 몸째 흔들리기·봉 잡는 손 / 발 IK·발목 제한 | [PlayerBodyHands](Player/Body/PlayerBodyHands.cs), [ArmIK](Player/Body/ArmIK.cs) / [PlayerBody.Bar](Player/Body/PlayerBody.Bar.cs), [PlayerBodyHands.Bar](Player/Body/PlayerBodyHands.Bar.cs) / [PlayerBody.Feet](Player/Body/PlayerBody.Feet.cs), [AnkleLimit](Player/Body/AnkleLimit.cs) |
| 적·웨이브 공유 (호스트가 진행) | [EnemyNetwork](Network/EnemyNetwork.cs), [EnemyHitMessage](Network/EnemyHitMessage.cs), [WaveSync](Network/WaveSync.cs) |
| 친구의 무기·사격·주먹 표시 | [PlayerNetworkCombat](Network/PlayerNetworkCombat.cs) |
| 방 채팅 / 닉네임 | [PlayerChat](Network/PlayerChat.cs) / [ProfileView](UI/Menu/ProfileView.cs) |
| 실제 핑·전송량 측정 (`/net`) | [NetworkStatsHUD](Network/NetworkStatsHUD.cs), [CountingTransport](Network/CountingTransport.cs) |
| 상대 캐릭터의 몸·소리·스킨 동기화 | [PlayerNetworkPose](Network/PlayerNetworkPose.cs), [PlayerPose](Network/PlayerPose.cs), [MovementMirror](Player/Movement/Core/MovementMirror.cs) |
| 방 만들기·참가·네트워크 스폰 / 서버 목록 | [GameSession](Network/GameSession.cs) / [ServerBrowserView](UI/Menu/ServerBrowserView.cs), [BusySpinner](UI/Menu/BusySpinner.cs), [IRoomList·RoomInfo](Network/RoomInfo.cs), [CombinedRoomList](Network/CombinedRoomList.cs), [LanRoomFinder](Network/LanRoomFinder.cs), [LanRoomBeacon](Network/LanRoomBeacon.cs), [InternetRoomFinder](Network/InternetRoomFinder.cs), [InternetRoom](Network/InternetRoom.cs) (공유기가 UPnP로 포트를 열면 직접 연결, 아니면 Relay: [RouterPortMap](Network/RouterPortMap.cs)), [OnlineServices](Network/OnlineServices.cs), 화면에 보이는 핑 [PingDisplay](Network/PingDisplay.cs) ([Network.csv](../Resources/Tables/Network.csv)) |
| 플레이어 생성 / 시작 위치 / 목록·내 캐릭터 찾기 / 멀티 구조 시험용 더미 | [PlayerFactory](Player/PlayerFactory.cs) / [PlayerSpawnPoint](Player/PlayerSpawnPoint.cs) / [PlayerRoster](Player/PlayerRoster.cs) / [ConsoleDummyPlayers](Debug/Combat/ConsoleDummyPlayers.cs) |
| 플레이어 HP·실드·피격·사망 | [PlayerDamageReceiver](Player/Combat/PlayerDamageReceiver.cs) |
| 쓰러짐·부활·복귀·전멸 | [PlayerLife](Player/Combat/PlayerLife.cs), [PlayerRevive](Player/Combat/PlayerRevive.cs), [PlayerLifeHUD](UI/PlayerLifeHUD.cs), [Downed.csv](../Resources/Tables/Downed.csv) |
| 킬 피드 (내 처치 강조, 연속 처치) | [CombatFeedHUD](Combat/Feel/CombatFeedHUD.cs), [KillFeedRow](UI/KillFeedRow.cs), [StreakBadge](UI/StreakBadge.cs), [KillFeed.csv](../Resources/Tables/KillFeed.csv) |
| 슬로 모션 (혼자: 세상 전체, 방: 본인과 상대만) / 연속 처치가 처음 해골이 될 때 방 전체 슬로 모션과 그동안의 화면(색, 가장자리, 모션 블러)과 소리(낮은 음, 먹먹함) / 느려져도 VFX Graph 이펙트가 매 프레임 움직이게 | [CombatTimeEffects](Player/Combat/CombatTimeEffects.cs), `EnemyNetwork.Pace` / [SkullSlowMotion](Combat/Feel/SkullSlowMotion.cs), [SlowMotionLook](Combat/Feel/SlowMotionLook.cs), [SlowMotionSound](Combat/Feel/SlowMotionSound.cs), [SkullSlowMotion.csv](../Resources/Tables/SkullSlowMotion.csv) / [VfxTimeStep](Combat/Feel/VfxTimeStep.cs) |
| 쓰러진 동료 표시·동료 체력 | [DownedAura](Combat/Feel/DownedAura.cs), [TeammateMarkerHUD](UI/TeammateMarkerHUD.cs), [TeamHealthPanel](UI/TeamHealthPanel.cs), [HealthSegments](UI/HealthSegments.cs), [DownedAura.csv](../Resources/Tables/DownedAura.csv) |
| Tab 화면 (점수판과 목표 목록) | [ScoreboardHUD](UI/ScoreboardHUD.cs), [ObjectiveLogPanel](UI/ObjectiveLogPanel.cs), [PlayerTally](Player/Combat/PlayerTally.cs), [PlayerNetworkScore](Network/PlayerNetworkScore.cs) |
| 죽은 몸의 레그돌 | [PlayerRagdoll](Player/Body/PlayerRagdoll.cs), [RagdollCorpse](Combat/Feel/RagdollCorpse.cs), [PlayerBody.Limp](Player/Body/PlayerBody.Limp.cs), [PlayerNetworkCorpse](Network/PlayerNetworkCorpse.cs), [Ragdoll.csv](../Resources/Tables/Ragdoll.csv) |
| 치마·겉옷·머리카락의 천 시뮬레이션과 공유 | [PlayerBodyCloth](Player/Body/PlayerBodyCloth.cs), [ClothPart](Player/Body/ClothPart.cs), [ClothBones](Player/Body/ClothBones.cs), [PlayerBodyClothColliders](Player/Body/PlayerBodyClothColliders.cs), [SkirtPress](Player/Body/SkirtPress.cs), [BodySurface](Player/Body/BodySurface.cs), [SkinSample](Player/Body/SkinSample.cs), [PlayerNetworkCloth](Network/PlayerNetworkCloth.cs), [ClothPose](Network/ClothPose.cs), [Cloth.csv](../Resources/Tables/Cloth.csv), [ClothBody.csv](../Resources/Tables/ClothBody.csv) |
| 플레이어 궁극기 최대 보유량·소비량 스탯 / 현재 충전량 | [PlayerStats](Player/PlayerStats.cs), [UltimateCharge](UI/HP/UltimateCharge.cs) (Play 시작 때 `PlayerMovement`가 붙임) |
| 피격 방향 아크·혈흔·쉐이크·빈사 박동·저체력 흑백 | [PlayerDamageFeedback](Player/Combat/PlayerDamageFeedback.cs), [LowHealthGrade](Player/Combat/LowHealthGrade.cs) ([LowHealth.csv](../Resources/Tables/LowHealth.csv)), [설정](../Resources/Combat/PlayerDamageFeedback.asset), [사용 안내](../Docs/PlayerDamageFeedbackGuide.md) |
| 무기별 연출·사운드·반동 | [Data/Combat/Weapons](../Data/Combat/Weapons/), 형식은 [WeaponData](Combat/Weapons/Data/WeaponData.cs) |
| 총알 궤적 / 탄착·혈흔 프리팹 효과 (관통한 벽 뒷면의 큰 파편과 탄흔, 몸을 뚫은 뒤 등 뒤로 뿜는 피, 그 뒤에 맞은 곳, 관통·도탄 소리 포함) / 총구 조명 | [MuzzleEffects](Combat/Weapons/Effect/MuzzleEffects.cs), [MuzzleLight](Combat/Weapons/Effect/MuzzleLight.cs), [WeaponImpactEffects](Combat/Weapons/Effect/WeaponImpactEffects.cs), [EnemyBloodEffect](Combat/Weapons/Effect/EnemyBloodEffect.cs) |
| 총구 화염 (CS2 1인칭을 프레임 단위로 잰 구름 불덩이: 3프레임 밝고 절반은 잔광 1프레임, 발마다 다른 모양 / F5·동료가 보면 1.6배와 엷은 연기) | [MuzzleFlash](Combat/Weapons/Effect/MuzzleFlash.cs) (총마다 하나, 쏠 때마다 총구에서 터뜨림, 내 총과 상대 사본 모두), [MuzzleFlame](Combat/Weapons/Effect/MuzzleFlame.cs) (화염의 모양과 시간, [MuzzleFlame.prefab](../Resources/Combat/MuzzleFlame.prefab)에서 조절), [MuzzleFlame.shader](../Resources/Combat/Shaders/MuzzleFlame.shader) |
| 발사 간격·탄약·장전 타이머 | [WeaponRuntime](Combat/Weapons/Core/WeaponRuntime.cs) |
| 발사·명중·조준 연결 / 총 모델 표현 / 이동·숨쉬기 흔들림(ArcCW)과 시선 흔들림(TFA) | [WeaponController](Player/Combat/Weapons/WeaponController.cs), [WeaponPresentation](Player/Combat/Weapons/WeaponPresentation.cs) / [WeaponSway](Player/Combat/Weapons/WeaponSway.cs) |
| 장비 슬롯 / 사격 입력 | [PlayerWeaponSlots](Player/Combat/Weapons/PlayerWeaponSlots.cs), [PlayerWeaponInput](Player/Combat/Input/PlayerWeaponInput.cs) |
| 피해 공통 규약·중복 타격 / 레이 판정 / 총알 관통과 도탄 (ArcCW) | [DamageHit](Combat/Damage/DamageHit.cs), [AttackTrace](Combat/HitDetection/AttackTrace.cs), [AttackHitbox](Combat/HitDetection/AttackHitbox.cs) / [BulletPenetration](Combat/HitDetection/BulletPenetration.cs), `AttackTrace.Bullet`, [Weapons.csv](../Resources/Tables/Weapons.csv)·[Surfaces.csv](../Resources/Tables/Surfaces.csv)의 `penetration` |
| 적 체력·추적·공격·노릴 플레이어 고르기 / 웨이브 / 웨이브 끝 무렵 남은 적 벽 너머 표시 | [Enemies](Enemies/), [EnemyTargeting](Enemies/EnemyTargeting.cs), [WaveManager](Combat/Waves/WaveManager.cs) / [EnemyReveal](Combat/Waves/EnemyReveal.cs) ([Waves.csv](../Resources/Tables/Waves.csv)의 `revealRemaining`·`revealColor`) |
| 총성·피격·히트마커·처치 피드백 / 후처리 빌려 쓰기 | [Combat/Feel](Combat/Feel/), [PostProcessLoan](Combat/Feel/PostProcessLoan.cs), [GunFeel.asset](../Resources/Combat/GunFeel.asset), 무기 데이터의 `Feel` |
| 히트마커 모양과 확인음(틱·헤드샷·처치·체간 붕괴) | [HitMarkerHUD](Combat/Feel/HitMarkerHUD.cs), [HitConfirm](Combat/Feel/HitConfirm.cs), [Art/Audio/HitConfirm](../Art/Audio/HitConfirm), [Sounds.csv](../Resources/Tables/Sounds.csv)의 `HitConfirm/…` 행 |
| 완벽 방어·흐트러짐·처형의 표시·사운드 | [Feedback.asset](../Resources/Combat/Feedback.asset) |
| 시점 입력 / 이동 카메라 효과 / 시점 펀치 / 큰 낙하 화면 | [PlayerLook](Player/Camera/PlayerLook.cs), [PlayerCameraEffects](Player/Camera/PlayerCameraEffects.cs), [PlayerMovementPunch](Player/Camera/PlayerMovementPunch.cs), [PlayerFallScreen](Player/Camera/PlayerFallScreen.cs) |
| 탄약·무기 슬롯·적 체력 UI | [AmmoHUD](UI/AmmoHUD.cs), [WeaponSlotsHUD](UI/WeaponSlotsHUD.cs), [EnemyVitalsHUD](UI/EnemyVitalsHUD.cs) |
| 데미지 숫자 (적 몸 옆에 서는 합산·낱개 숫자, 설정 → 화면 → 데미지 숫자) | [DamagePopupManager](UI/DamagePopupManager.cs) (받기·묶기·쌓기), [DamageNumber](UI/DamageNumber.cs) (숫자 하나의 모양과 움직임), [DamageNumberAnchor](UI/DamageNumberAnchor.cs) (몸 옆 자리와 방향), [DamageNumberLayout](UI/DamageNumberLayout.cs) ([Hud.csv](../Resources/Tables/Hud.csv) Damage), [Feedback.asset](../Resources/Combat/Feedback.asset)의 Damage popups (색·글꼴·움직임) |
| 체력·궁극기 UI / 목표·웨이브 UI | [UI/HP](UI/HP/), [MissionHUD](UI/MissionHUD.cs) (내용·알림), [MissionBarLook](UI/MissionBarLook.cs) (미션 바 모양, Hud.csv Mission), [MissionLineGraphic](UI/MissionLineGraphic.cs) (진행 선), [WaveMissionSource](UI/WaveMissionSource.cs) |
| 3D HUD (UI 카메라, 좌우로 기운 면, 요소별 기울기, 시선 흔들림) | [HudCamera](UI/Hud/HudCamera.cs), [HudPlane](UI/Hud/HudPlane.cs), [HudTilt](UI/Hud/HudTilt.cs), [HudRig](UI/Hud/HudRig.cs) (GameHUD에 붙음), [HudBoard](UI/Hud/HudBoard.cs) (좌우 면이 서 있는 유리판, Hud.csv Board), [HudInstaller](../Editor/Utilities/HudInstaller.cs) (Tools → HUD), [Hud.csv](../Resources/Tables/Hud.csv), [HudTilt.csv](../Resources/Tables/HudTilt.csv) |
| 채팅·테스트 명령 | [UI/Chatting](UI/Chatting/) |
| 메인 메뉴·캐릭터 미리보기 | [MainMenuScreen](UI/Menu/MainMenuScreen.cs), [MainMenuPreview](UI/Menu/MainMenuPreview.cs) |
| Esc 설정 메뉴·일시정지 / 저장되는 플레이어 설정 | [UI/Settings](UI/Settings/), [GamePause](Settings/GamePause.cs) / [GameSettings](Settings/GameSettings.cs) |
| 크로스헤어 꾸미기·프리셋·공유 코드 | [CrosshairSettingsView](UI/Settings/CrosshairSettingsView.cs), [CrosshairPresetGallery](UI/Settings/CrosshairPresetGallery.cs), [CrosshairCode](Settings/CrosshairCode.cs), [CrosshairPresets.csv](../Resources/Tables/CrosshairPresets.csv) |
| 소리 볼륨 채널(전투·이동·목소리·UI) / 연타가 동시 재생 한도를 넘지 않게 | [GameAudio](Audio/GameAudio.cs) / [VoiceRing](Audio/VoiceRing.cs) |
| 발소리 / 동작 소리 / 숨·바람·큰 낙하·사다리류 소리 | [PlayerFootsteps](Player/Audio/PlayerFootsteps.cs), [PlayerMovementAudio](Player/Audio/PlayerMovementAudio.cs), [Player/Audio](Player/Audio/) |
| 동작마다 나는 소리의 구성 / 소리 묶음·표면·재생 | [SoundCues.csv](../Resources/Tables/SoundCues.csv) / [SoundBank](Audio/SoundBank.cs), [Surfaces](Audio/Surfaces.cs), [SoundCues](Audio/SoundCues.cs), [CharacterSounds](Audio/CharacterSounds.cs), [사용 안내](../Docs/MaintenanceGuide.md#이동목소리-소리-폴더가-곧-목록) |
| 총소리 고르기(총에 넣은 클립 → Guns 묶음 → 합성음) / 쏜 자리의 방에 맞춘 총 잔향 (Accurate Gun Reverb) | [FeelAudio](Combat/Feel/FeelAudio.cs), [Art/Audio/Guns](../Art/Audio/Guns) / [GunReverb](Audio/GunReverb.cs) ([Reverb.csv](../Resources/Tables/Reverb.csv)) |
| 맵에 놓는 구역 리버브(귀가 들어간 상자의 프리셋) | [ReverbArea](Audio/ReverbArea.cs) (놓는 법은 [사용 안내](../Docs/MaintenanceGuide.md)의 구역 리버브) |
| 적이 들을 소음 이벤트 | [Noise](World/Noise.cs) (아직 듣는 적은 없다) |

## 코드의 경계

| 위치 | 책임 |
| --- | --- |
| `Player/Movement` | 입력을 이동 의도로 해석하고 상태·자세·물리 이동을 실행 |
| `Player/Combat` | 플레이어의 공격·방어·장비 선택과 행동 간 취소·잠금 |
| `Player/Camera` | 시점 입력과 이동에 따른 카메라 효과, 시점 펀치, 큰 낙하 화면, 카메라 벽 판정 |
| `Player/Body` | 보이는 몸: 클립 재생, 눈 시점, 손·발 IK. 이동 상태를 읽어 표현한다 |
| `Player/Audio` | 이동 이벤트와 발 접지를 듣고 소리를 낸다 |
| `Combat/Damage`, `HitDetection`, `Weapons` | 플레이어·적이 공유하는 피해 규약, 접촉 판정, 무기 데이터·시간 규칙 |
| `Combat/Feel` | 전투 결과에 대한 표시·소리와 적의 총격 피격 반응 |
| `Enemies`, `Combat/Waves` | 적의 생명주기·AI와 웨이브 진행 |
| `UI` | 게임 상태를 읽어 표시하고 UI 입력을 전달 |
| `World` | 파괴 가능한 문, 스윙 바, 사다리, 집라인, 텔레포트, 물리 프랍([PhysicsProp](World/PhysicsProp.cs)), 표면 종류, 소음 등 월드 상호작용 |
| `Data` | 밸런스 CSV 표를 읽어 컴포넌트·에셋 필드에 적용하고, 편집기에서 표 저장 시 다시 적용 |
| `Settings` | 사용자별로 저장되는 설정(`settings.json`)과 일시정지 |
| `Audio` | 소리 볼륨 채널, 소리 묶음·큐·표면, 캐릭터의 몸과 입에서 나는 소리 |
| `Debug` | 연습 표적·공격기·테스트 명령·디버그 HUD |
| `../Editor` | 편집기 전용 도구: 표 다시 읽기, 모델 임포트 규칙·물리 프랍, 소리 묶음·몸 프로필 자동 생성, HUD 편집 미리보기, 폰트 저장 |

이동의 진입점은 `PlayerMovement`다. 프레임마다 입력·상태·물리 서비스를 연결하고, 외부 기능에는 `CurrentState`, `Velocity`, `ResetVelocity()` 같은 API를 제공한다. 내부 상태 전환과 재컴파일 복구는 [이동 코드 구조](Player/Movement/README.md)를 참고한다.

전투는 **입력 → 공격 규칙 → 접촉·피해 → 반응·표시** 순서로 읽으면 된다. 예를 들어 총은 `PlayerWeaponInput → WeaponController / WeaponRuntime → AttackTrace → IDamageReceiver`로 이어진다. 피해를 받은 대상의 처리에서 발생한 전투 이벤트는 `GunFeelDirector`가 표시·소리로 연결한다. 새 무기나 적을 추가하는 순서는 [전투 코드 구조](Combat/README.md)에 있다.

## 플레이어에 붙는 것과 실행 순서

씬의 플레이어에는 이동·시점·입력·무기·슬롯만 둔다. 나머지는 Play 시작 때 `PlayerMovement.EnsureInitialized`가 붙이고 `Player.csv`의 같은 이름 행과 연결한다. 몸은 `Resources/Characters/PlayerBody` 프로필에 캐릭터가 있을 때만 붙는다.

| 행 | 컴포넌트 |
| --- | --- |
| `Body`, `Hands`, `Footsteps` | [PlayerBody](Player/Body/PlayerBody.cs), [PlayerBodyHands](Player/Body/PlayerBodyHands.cs), [PlayerFootsteps](Player/Audio/PlayerFootsteps.cs) |
| `Sounds`, `MovementAudio`, `Breathing`, `Wind`, `FallAudio`, `FixtureAudio` | [CharacterSounds](Audio/CharacterSounds.cs), [PlayerMovementAudio](Player/Audio/PlayerMovementAudio.cs), [PlayerBreathing](Player/Audio/PlayerBreathing.cs), [PlayerWindAudio](Player/Audio/PlayerWindAudio.cs), [PlayerFallAudio](Player/Audio/PlayerFallAudio.cs), [PlayerFixtureAudio](Player/Audio/PlayerFixtureAudio.cs) |
| `Punch`, `FallScreen` | [PlayerMovementPunch](Player/Camera/PlayerMovementPunch.cs), [PlayerFallScreen](Player/Camera/PlayerFallScreen.cs) |

한 프레임 안의 순서는 `DefaultExecutionOrder`로 고정되어 있다. 앞 단계의 결과를 뒤 단계가 읽으므로 순서를 바꾸면 카메라·손이 한 프레임 늦는다.

| 순서 | 컴포넌트 | 하는 일 |
| ---: | --- | --- |
| -100 | `PlayerMovement` | 입력·상태·실제 이동 |
| 0 | `PlayerLook` | 시선 |
| 200 | `PlayerCameraEffects` | 흔들림·기울이기와 카메라 기준 위치 |
| 250 | `PlayerBody` | 몸 자세, 발 IK, 눈 시점으로 카메라 최종 위치 확정(F5면 그 뒤 카메라만 뒤로) |
| 260 | `PlayerFootsteps` | IK가 끝난 발로 접지 판단 |
| 300 | `WeaponController` | 확정된 눈 시점(`PlayerView`)으로 총 자세, 카메라 조준선으로 발사 방향 |
| 320 | `PlayerBodyHands` | 확정된 총·주먹 위치로 손 놓기 |

## 입력과 설정

대부분의 키 바인딩은 [Player.inputactions](../Input/Player.inputactions)에 있다. 이동은 `MovementInput`, 사격은 `PlayerWeaponInput`, UI는 해당 UI 컴포넌트가 읽는다. 앉기는 상태 코드에서 키를 직접 읽지 않고 `MovementInput.CrouchHeld`/`CrouchPressed`를 쓴다. 그래야 Esc 메뉴의 "눌러서 전환" 설정이 모든 상태에 적용된다. **슬롯 1~4와 숫자패드 키는 현재 `PlayerWeaponSlots.Update()`에서 직접 읽으므로** 입력 에셋만 바꿔서는 재지정되지 않는다.

`PlayerMovement.Configuration.cs`는 별도 컴포넌트가 아니라 같은 `PlayerMovement` 클래스의 직렬화 필드다. `MovementAbilitySettings`는 그 안의 설정 데이터다. 수치는 `Player.csv`(`Posture`·`Move`·`Ability` 행)가 필드 이름으로 덮어쓰므로, 표에 있는 값은 인스펙터에서 바꿔도 Play 때 표 값으로 돌아간다. 필드 이름을 바꾸면 표의 키도 같이 바꾼다. 컴포넌트 안에 든 설정 객체는 `GameTables.BindNested`로 연결한다.

## 기능을 추가할 때

1. 플레이어 전용 입력·선택은 `Player`, 공통 피해·무기 규칙은 `Combat`에 둔다.
2. 실제 위치 변경은 이동 서비스에 연결하고, HUD·표시 코드에서 HP·탄약·플레이어 위치를 직접 바꾸지 않는다.
3. 한 공격의 여러 콜라이더는 같은 `DamageHit.Id`를 사용한다. 새 공격에는 새 ID를 발급한다.
4. 비활성화·사망·상태 취소·스크립트 재로드에서 입력, 충돌 무시, 시간 효과, 임시 표시가 정리되는지 확인한다.
5. 변경한 기능을 [검증 안내](../Docs/ValidationGuide.md)의 항목대로 실제 플레이로 확인한다.

## 성능을 살필 때

Profiler에서 대표 상황의 CPU 시간과 `GC.Alloc`을 먼저 확인한다. API 이름만 보고 전체 공격이 무할당이라고 판단하지 않는다.

- `AttackTrace`는 보통 단일 Raycast를 사용하고, 제외할 충돌체를 만나면 다중 결과를 검사한다. 버퍼가 꽉 차면 누락된 가까운 벽을 놓치지 않도록 전체 결과를 다시 조회한다. 최적화할 때 이 포화 상황과 공격자 제외 규칙을 유지한다.
- `ShoveSweep`의 밀치기는 여러 적을 스칠 수 있다. `PlayerCombatActions`의 돌진 밀치기는 벽에 가리지 않은 가장 가까운 적 하나만 처리한다. 단일 타격 질의로 바꾸면 군중 밀치기와 벽 차폐가 달라질 수 있으므로, 배열 할당 개선과 타격 규칙 변경을 구분한다.
- `WeaponAimSolver`의 총구 주변 검사는 재사용 버퍼를 사용한다. 버퍼가 가득 찼고 자기 콜라이더만 발견됐다면 전체 조회로 엄폐 누락을 막는다. 일반 경로의 배열 할당은 줄였지만 전체 발사의 무할당이나 프레임 성능을 보장하는 수치는 아니다.
- 수신자 캐시·풀링을 도입한다면 부모 콜라이더의 대상 식별, 재사용 시 참조 갱신, 중복 타격 방지를 함께 검사한다.
