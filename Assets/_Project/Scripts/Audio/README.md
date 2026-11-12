# Audio — 소리

볼륨 채널, 소리 묶음과 큐, 환경음과 잔향, 캐릭터가 내는 소리.

전체 지도는 [스크립트 길잡이](../README.md), 프로젝트 구조는 [구조 안내](../../../../Docs/Architecture.md)에 있다.

## 무엇을 고치려면 어디를

| 변경할 내용 | 시작할 파일·설정 |
| --- | --- |
| 소리 볼륨 채널(전투·이동·목소리·UI) / 연타가 동시 재생 한도를 넘지 않게 | [GameAudio](GameAudio.cs) / [VoiceRing](VoiceRing.cs) |
| 동작마다 나는 소리의 구성 / 소리 묶음·표면·재생 | [SoundCues.csv](../../Data/Resources/Tables/Audio/SoundCues.csv) / [SoundBank](SoundBank.cs), [Surfaces](../World/Surfaces.cs), [SoundCues](SoundCues.cs), [CharacterSounds](CharacterSounds.cs), [사용 안내](../../../../Docs/Guides/MaintenanceGuide.md#이동목소리-소리-폴더가-곧-목록) |
| 맵에 놓는 구역 리버브(귀가 들어간 상자의 프리셋) | [ReverbArea](ReverbArea.cs) (놓는 법은 [사용 안내](../../../../Docs/Guides/MaintenanceGuide.md)의 구역 리버브) |
| 몸이 부서지는 소리 (워크숍 애드온 이식 2026-11-11: 처형의 마지막 일격이 등 뒤면 목 꺾기·앞이면 두개골·거구처럼 버티면 팔 부러짐, 머리가 터질 때 피 뿜기, 산 좀비의 팔다리가 떨어질 때 피·뼈, 주먹이 좀비에 맞을 때 BOOM 밑에 깔리는 살 소리. 모든 PC에서 그 자리에서 3D로, 좀비를 깨우는 소음은 내지 않음) / V 밀치기 헛스윙 / 소리 미리 듣기 `/sound <뱅크>/<키>`·`/sound cue <큐>`·`/sound list <뱅크>` | [BodyBreakSounds](BodyBreakSounds.cs) (`CombatDamageEvents.Damaged`·`ZombieGore.Crippled`를 들음, SoundCues `Execution/*`·`Gore/*`·`ZombieHit/Fist`, 거구의 다리와 약점 머리 `Brute/*`는 ZombieBody.Legs가 같은 Play로 부름, 소리 `Art/Audio/Enemies/Zombie/Bone`·`Gore`·`Hit`) / [PlayerShoveAudio](../Melee/Shove/PlayerShoveAudio.cs) (SoundCues `ShoveSwing`) / [SoundPreview](SoundPreview.cs) |
