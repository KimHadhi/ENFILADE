# 설정·유지보수 안내

[프로젝트 안내](../../../README.md) · [전투 제작](CombatFoundationGuide.md) · [이동 안내](MovementGuide.md) · [플레이어 몸](PlayerBodyGuide.md) · [검증 안내](ValidationGuide.md)

## 밸런스 표 (Data Tables)

전투·이동·몸·소리의 숫자는 [Resources/Tables](../Resources/Tables)의 CSV 표에 있다. **표를 저장하면 컴파일 없이 바로 적용되고, Play 중이면 실행 중인 게임에도 즉시 반영된다.** Excel, Google Sheets(파일 → 가져오기/CSV로 다운로드), VS Code 등 CSV를 여는 프로그램이면 무엇이든 된다.

| 표 | 내용 | 행(row) |
| --- | --- | --- |
| [Player.csv](../Resources/Tables/Player.csv) | 자세별 캡슐·눈높이, 이동·속도 쌓기·점프·슬라이드·다이브, 회피·파쿠르·착지·밀치기·사다리·집라인, 최대 체력, 궁극기 보유량·소비량·충전량, 주먹·패링·가드 브레이크, 처형, 몸 애니메이션·손, 이동 펀치·큰 낙하 화면, 발소리·동작 소리·숨소리·바람·큰 낙하 소리 | `Posture` `Move` `Ability` `Vitals` `Stats` `Ultimate` `Fists` `Defense` `Parry` `Execution` `Body` `Hands` `Punch` `FallScreen` `Sounds` `Footsteps` `MovementAudio` `Breathing` `Wind` `FallAudio` `FixtureAudio` |
| [SoundCues.csv](../Resources/Tables/SoundCues.csv) | 동작마다 나는 소리 묶음(볼트·착지·발소리…)과 적에게 들리는 거리 | 큐 이름 (`Vault`, `Land`, `StepRun` …) |
| [Sounds.csv](../Resources/Tables/Sounds.csv) | 소리 묶음 폴더별 볼륨·피치 | `<묶음>/<폴더>` (`Movement/Bar/Grab` …) |
| [Reverb.csv](../Resources/Tables/Reverb.csv) | 총소리 잔향: 방을 재는 광선 길이, 실내·실외와 방 크기 기준, 동시에 나는 꼬리 수. 구역 리버브가 바뀌는 시간 | `Gun.필드`, `Area.필드` |
| [Surfaces.csv](../Resources/Tables/Surfaces.csv) | 표면 종류의 부모와 찾을 키워드 | 표면 이름 (`Concrete` …) |
| [Weapons.csv](../Resources/Tables/Weapons.csv) | 총 피해·RPM·장탄·예비탄·장전·자동 장전·탄퍼짐·앉아 쏘기·헤드샷·ADS·흔들림, 몸의 손이 잡는 위치 | `WeaponData` 에셋 이름 (`MX16A4` …) |
| [FPSBody.csv](../Resources/Tables/FPSBody.csv) | 총을 드는 KINEMATION FPS Animation Framework: Q/E 때 몸이 기우는 각도, 질주 때 상체가 시선을 따르는 정도와 질주로 보는 지점, 끊긴 장전이 풀리는 시간, 총구를 재는 깊이, 총을 꺼내고 넣을 때 상체를 넘겨받는 시간 | `Rig` |
| [Enemies.csv](../Resources/Tables/Enemies.csv) | 적 체력·공격·자세(체간)·이동 속도 | 적 프리팹 이름 (`Enemy_Test`) |
| [Waves.csv](../Resources/Tables/Waves.csv) | 웨이브 적 수·증가량·대기·등장 간격 | `WaveManager`의 `Table Id` (기본 `Default`) |
| [Maps.csv](../Resources/Tables/Maps.csv) | 메인 메뉴에서 고를 맵과 보이는 이름. 빌드 설정에 등록된 씬만 나온다 | 씬 이름 (`CombatFoundation` …) |
| [Hub.csv](../Resources/Tables/Hub.csv) | 허브(은신처): [F]가 닿는 거리와 각도, 모두 준비한 뒤 출발까지 세는 시간(혼자면 따로), 사격장 좀비 더미·강철판의 체력, 넘어진 과녁이 일어나기까지, 움직이는 과녁 속도, 결과 연출의 칭호 간격·마지막 한 장이 닫히기까지. Maps.csv의 `Hub` 줄은 허브의 이름(메뉴의 맵 고르기·기록·클래스 성장에는 안 나온다) | `Default` |
| [Results.csv](../Resources/Tables/Results.csv) | 장 결과의 칭호: 어느 숫자로 주는지, 순서, 받을 최소값, 단위. 칭호 이름과 한 줄 농담은 Texts.csv `results.award.<id>` | 칭호 (`kills`, `downs` …) |

- **열 이름 = 인스펙터 필드 이름**이다(인스펙터 표시 이름이 `Attack Range`면 열은 `attackRange`). 표에 열을 추가하면 그 필드도 표가 관리한다. 적 표의 `speed`처럼 `NavMeshAgent`의 공개 속성도 쓸 수 있다.
- 표에 있는 칸이 인스펙터 값보다 우선한다. **빈칸이거나 열이 없으면 인스펙터 값**을 그대로 쓴다. 표에 행이 없는 오브젝트(테스트 표적 등)는 영향을 받지 않는다.
- `#`로 시작하는 행·열은 설명용이라 무시된다. `Player.csv`는 `key,value,note` 형식이고 key는 `행.필드`다.
- 목록과 벡터는 `;`로 구분한다: 패링 단계 `0.35;0.2;0.1;0.05`, 주먹 피해 `10;28`(최소;최대 충전). 소수점은 `.`을 쓴다.
- Excel에서는 **CSV UTF-8**로 저장해야 한글 설명이 깨지지 않는다.
- 열 이름 오타나 읽을 수 없는 값은 Console에 `[Tables]` 경고로 한 번만 표시된다. Play 중 변경이 바로 안 보이면 `Tools → Data Tables → Reload Tables`를 누르거나 Preferences → Asset Pipeline → Auto Refresh가 켜져 있는지 확인한다.
- 플레이 중 최대 체력·자세를 바꾸면 살아 있는 적과 플레이어는 현재 비율을 유지한다.
- **편집기 도구가 표를 쓸 때**(BOOTH 가져오기의 Cloth.csv 흔들림 줄, Grip Tuner의 GunGrips.csv)는 [TableFile](../Editor/Data/TableFile.cs)을 거친다. 파일을 비우고 새로 쓰지 않고 제자리에 덮어쓰므로, 유니티·백신·git·검색 도구가 파일을 메모리에 매핑해 두어도 써진다(윈도는 매핑된 파일을 줄이지 못하게 해서, 파일을 먼저 0으로 자르는 예전 `File.WriteAllText`는 "Win32 IO returned 1224"로 막혔다. 2026-11-12). 새 내용이 더 짧은데 줄이기가 막히면 남는 자리를 빈 줄로 채우고(표를 읽을 때 빈 줄은 건너뛴다) 다음에 쓸 때 줄인다. 엑셀처럼 쓰기를 막고 파일을 연 프로그램이 있으면 쓰기가 기다렸다가 유니티로 돌아올 때와 최대 30초마다 다시 해 보고, 1분 넘게 막히면 잡고 있는 프로그램 이름(윈도 재시작 관리자로 찾는다)이나 할 일을 `[Tables]` 경고로 한 번만 알린다. 그 프로그램을 닫으면 따로 누를 것 없이 저절로 써진다. 기다리는 동안 그 표가 다른 곳에서 저장되면 그 내용을 덮지 않고 그만두며 다시 할 방법을 알린다. 기다리는 쓰기가 남은 채 유니티를 끄려 하면 한 번 묻는다. 새 편집기 도구가 표를 쓸 때도 `File.WriteAllText` 대신 `TableFile.Read`로 읽고 `TableFile.Write`로 쓴다.

### 화면 문구 (Texts.csv)

플레이어가 읽는 글자(메뉴·설정·HUD·안내·알림·처치 목록·좀비 이름)는 [Texts.csv](../Resources/Tables/Texts.csv)에 있다(2026-11-11 플레이 테스트 의견: 번역판을 위해 표로 정리). 코드는 id만 부르고(`Texts.Get`, `Texts.Format`) 글자는 표가 가진다.

- 열은 `id`, `ko`(한국어), `en`(번역판용, 비어 있으면 한국어가 나온다), `#어디`(어디에 나오는지 적은 메모. 게임은 읽지 않는다). 다른 언어는 열을 하나 더 만들고 `Texts.Language`를 그 열 이름으로 바꾼다.
- `{0}`, `{1}`은 코드가 채우는 자리라 지우면 안 된다(순서는 바꿔도 된다). 글자로 중괄호를 쓰려면 `{{` `}}`. `\n`은 줄바꿈이고 `<color=#...>`, `<b>` 같은 리치 텍스트 태그도 쓸 수 있다. 칸 앞뒤 공백은 지워지므로 띄어쓰기가 필요한 자리는 코드에 있다.
- id를 잘못 지우거나 바꾸면 화면에 `[id]`가 그대로 보인다. `#---`로 시작하는 줄은 구역 제목이다.
- 저장하고 표를 다시 읽으면(Play 중이면 바로) 새로 그려지는 글자부터 바뀐다. 이미 떠 있는 글자는 다음에 다시 쓸 때 바뀐다.
- 개발자 콘솔(`/` 명령)의 도움말과 대답, /net 같은 개발용 표시, 디버그 로그는 코드에 남겨 두었다. 이야기 카드(CampaignChapters.csv), 능력 카드(Perks.csv), 상점 품목(Shop.csv), 메뉴 소식(MenuTicker.csv), 맵 이름(Maps.csv)과 무기 이름(무기 데이터)은 원래 각자의 표·에셋에 있다.

### 클래스 (Classes.csv, ClassAbilities.csv)

클래스는 [Classes.csv](../Resources/Tables/Classes.csv)의 줄이다(2026-11-11 사용자의 설계: KF2·GMod Horde 느낌의 7개). 열은 `title`(이름), `role`(한 줄 소개), `primary`·`secondary`(장이 시작될 때 1·2번 칸에 주는 총. 무기 에셋 이름), `classWeapons`(;로 구분한 클래스 총. 이 총을 들면 `gunBonus`% 피해가 세진다), `ready`(1이면 능력이 만들어진 클래스. 0이면 메뉴에 "준비 중"이지만 고를 수는 있다).

- 고른 클래스는 ClassGrowth의 classes.json에 저장되고 클래스마다 등급이 따로 쌓인다. 아직 안 골랐으면 `ready` 1인 첫 클래스(어썰트)다.
- 플레이 중 등급(11-11): 초상화 아래에 이번 판 성장까지 더한 휘장과 다음 휘장까지의 얇은 막대가 있다([RankStrip](../Scripts/UI/Hud/RankStrip.cs)). 흰색은 판 시작 때 쌓인 몫, 분홍은 이번 판 처치, 연분홍은 지금 가진 돈 중 판이 끝나면 성장으로 치는 절반이라 돈을 쓰면 그만큼 줄어든다. 처치 때 막대 위에 작은 +XP가 뜨고, 이번 판으로 오를 휘장이 생기면 분홍 화살표와 수가 붙는다. Tab 화면에는 같은 막대가 크게, 이번 판 경험치와 그 내역, 처치 하나의 경험치, 다음 휘장까지가 나온다. 둘 다 [RankProgress](../Scripts/UI/Hud/RankProgress.cs)가 ClassGrowth와 같은 규칙으로 계산하며 저장은 하지 않는다(휘장은 여전히 장이 끝날 때 오른다). 크기는 Hud.csv의 Rank 줄.
- 능력 수치는 [ClassAbilities.csv](../Resources/Tables/ClassAbilities.csv): `Assault.*`(스택 최대·스택당 피해와 이동·스택 유지와 감소·CHARGE 시간·반경·피해·이동), `Point.reloadShare`(들지 않은 총이 저절로 장전되는 시간 배수), `Ghost.*`(헤드 헌터 최대·스택당 피해(곱연산)·바디샷에 잃는 스택, 데드아이 피해, 엘리트 휘청임 시간과 다시 휘청일 때까지), `Knight.*`(받는 피해 배수, 패리 판정·패리 자세 피해, 패리 반격 켜기, 반격·주먹 피해, 깔려도 싸우기, 깔렸을 때 걷어차기 재사용, 도발 반경·시간·재사용·도발 중 받는 피해), `Ping.*`(F 핑 시간·거리·간격, 모든 클래스), `Schiesshund.*`(표식 시간·피해, 현상금, 데스페라도 탄 점수·최대·시작·피해, 거구에게 데스페라도가 빼는 몫, 리턴 백), `Support.*`(보급 가방 용량·시작·초당 충전, 보급에 드는 양·탄창 수·풀리 로디드 %·데탕트 주사기·거리·각도, 바리케이드에 드는 양·최대 개수·놓는 거리·체력·막은 공격 배수·밀어대는 좀비의 초당 피해).
- 클래스 기술 키는 입력 액션 `Player/ClassAbility`(기본 마우스 휠 클릭)이고 설정 → 조작 → 키 설정 창의 "클래스 기술" 줄에서 바꾼다(아래 **키 설정**). 바꾼 키는 GameSettings의 `keyBindings`에 저장되고 그 줄의 "기본"이나 "모두 기본값"으로 돌아간다.
- 고스트(2단계): 헤드샷마다 헤드 헌터 1스택, 바디샷에 하나 잃고 피해를 입으면 모두 잃는다. 스택마다 총 피해가 곱으로 오른다. 장전한 뒤 첫 발은 데드아이(기본 두 배). 클래스 총으로 엘리트(거구·돌진·자폭과 엘리트·네임드 등급)를 맞히면 휘청인다(MeleeCombatTarget.Interrupt: 공격과 돌진이 끊긴다).
- 나이트(2단계): 적에게 받는 피해가 줄고, 패리 판정이 길고 자세 피해가 크며, 패리하면 완벽 회피처럼 반격 창이 열린다. 반격과 주먹이 세고 러너에게 깔려도 싸우며, 깔리면 V로 실드 없이 걷어차 떨쳐낸다(Knight.breakFree초마다, PlayerHold.BreakFreeEvery). 클래스 기술 키는 도발: 반경 안에서 이미 싸우던 좀비가 몇 초 동안 나이트를 노린다(EnemyTargeting.Taunt). 좀비를 움직이는 건 호스트라서 친구 PC의 도발은 PlayerNetworkCombat.TauntRpc로 호스트에 간다. 조용한 좀비는 깨우지 않는다.
- F 핑(3단계, 모든 클래스): F에 처형·암살·줍기·부활·사다리·미션 버튼·문·상점이 걸려 있지 않으면 조준한 적을 찍는다(PlayerPing). 찍힌 적은 팀 모두에게 테두리(HitFlashRenderer.SetPing)와 벽 너머 실루엣, 머리 위 마름모와 거리로 보인다(PingMarks). 다른 PC에는 PlayerNetworkCombat.PingRpc로 간다. 왼손으로 가리키는 동작은 클립이 필요해 지금은 총이 살짝 끄덕인다(FPSBody.Point).
- 시순트(3단계): F 핑이 청록 현상금 표식(한 번에 하나)이고 내 총알이 그 적에게 더 세다. 자기 표식 대상을 처치하면 현상금 돈(처치 점수 줄 '현상금'), 리턴 백(이동·피해 스택), 데스페라도 탄 점수를 받는다. 데스페라도는 시순트의 4번 칸 R08(2번 칸은 보통 권총 M1911): PlayerWeaponSlots.SpecialHoldsGun이 특수 칸에 총을 허락하고 KeepsOwnRounds로 탄약 줍기·리필·상점·처형 보상이 그 칸을 건너뛴다. 탄은 현상금으로만 찬다(WeaponRuntime.LoadOwnRounds·LoadRound, 예비탄 0이라 재장전도 없다). 탄 한 발은 피해 두 배이고, 표식 대상의 머리에 맞으면 엘리트도 즉사(PlayerClass.Finish, 거구는 bossShare만큼)하며 화면에는 청록 BOUNTY 배너(CounterBanner를 다른 글자와 색으로), 세계에는 모두에게 보이는 BountyBurst(총구에서 머리까지 청록 광선, 섬광과 불꽃, 쏟아지는 금화, 하늘로 솟는 빛기둥, 바닥 충격파 두 겹, 공중에 떠오르는 BOUNTY 글자; PlayerNetworkCombat.BountyRpc)가 터진다. 보통 표식 처치는 금화만 쏟아진다.
- 서포트(4단계, SupportKit): 저절로 차는 보급 가방 하나로 두 일을 한다. F로 조준한 동료(서 있고 3.5m 안)에게 보급하면 그 동료의 PC에서 총마다 탄창 몇 개가 예비탄에 더해지고 처음 예비탄보다 25% 더까지 찬다(풀리 로디드, 줍기·상점은 처음 예비탄까지), 데탕트면 주사기도 찬다(PlayerNetworkCombat.ResupplyRpc). 휠 클릭은 앞 바닥에 허리 높이 판자 바리케이드를 세운다(Barricade: 최대 개수를 넘으면 가장 오래된 것이 무너진다). 바리케이드는 내비 메시를 깎아 좀비가 못 지나가고, 너머의 플레이어를 노린 좀비 공격을 대신 맞으며(IDamageReceiver, 아군의 총알·주먹은 피해 없음), 붙어서 밀어대는 좀비에게도 깎인다. 체력은 호스트가 정하고(클라이언트의 좀비 공격은 BarricadeHitRpc로 호스트에) 모두에게 알린다(BarricadeStateRpc). 세우기·치우기는 BarricadeRpc·CollapseRpc. 나중에 들어온 사람에게는 이미 선 바리케이드가 안 보인다.
- 클래스 전용 퍼크 카드(헤드 헌터 최대 같은 수치를 키운다), 근접 무기·저격총 에셋, 핑의 왼손 가리키기 클립은 다음 단계다.

### 잠입 (Stealth.csv, ZombieSenses.csv)

2026-11-11 잠입 개편(사용자의 Advanced Stealth 애드온과 시야 참고 영상을 바탕으로). 화면에 보이는 것과 보상은 [Stealth.csv](../Resources/Tables/Stealth.csv), 좀비의 눈·귀는 [ZombieSenses.csv](../Resources/Tables/ZombieSenses.csv)에 있다.

- `Markers.*`: 머리 위 의심 표시와 조준점 둘레 쐐기. 크기(px), 높이(m), 나오는 최소 의심, !가 남는 시간, 효과음 크기.
- `View.*`: 바닥의 시야 콘(`coneMode` 0 끔 · 1 앉았을 때 · 2 항상, 진하기, 최대 개수, 거리)과 소리 파동(최소 거리, 시간, 진하기). 콘은 셰이더가 깊이 텍스처로 바닥 위치를 찾아 칠하므로 URP 에셋의 Depth Texture가 켜져 있어야 한다(PC_RPAsset은 켜져 있다).
- `Throw.*`: T 돌 던지기(누르고 있으면 궤적과 떨어질 곳의 소리 범위가 보이고 떼면 던진다)의 쿨다운, 던지는 빠르기, 소리가 닿는 거리(`noise`), 돌 크기와 남는 시간. 돌에 반응하는 방식은 ZombieSenses의 `lure*` 열(지켜보는 시간 `lureGlance`, 곁눈질 `lureFocus`, 다가가는 빠르기와 둘러보는 시간). 돌 소리는 [StealthSounds](../Resources/Stealth/StealthSounds.asset)의 `stoneHits`에서 고르고 비면 코드가 만든 소리를 쓴다.
- `Score.takedown`(암살 추가 점수), `Score.unseen`(조용한 구간을 아무도 안 들키고 지나간 보상, 모두에게).
- 더 쉽게 하려면: ZombieSenses의 `nearRate`를 낮추거나 `forget`을 올리고 `crouchShare`를 낮춘다. 덜 두리번거리게 하려면 `lookAroundEvery`를 늘리거나 0으로 끈다. 돌이 더 멀리 들리게 하려면 `Throw.noise`.

### 총 꾸미기 저장 (gun_setups.json)

C 메뉴에서 바꾼 조준경·총구·하단·손잡이, L 플래시, B 사격 모드는 바꾸는 즉시 이 PC의 `gun_setups.json`(`Application.persistentDataPath`, 보통 `%USERPROFILE%\AppData\LocalLow\<회사>\<제품>`)에 총 에셋 이름별로 적히고, 그 총을 꺼낼 때마다(다음 판 포함) 그대로 붙는다(2026-11-11 플레이 테스트: 판마다 다시 꾸며야 했음). 처음 상태로 돌리려면 게임을 끄고 그 파일을 지운다. 표에 없는 사격 모드가 적혀 있으면 그것만 무시한다.

### 허브 로비와 결과 화면 (Hub.csv, Results.csv)

허브(은신처)는 `Tools → World → 허브 로비 짓기`가 코드로 짓는다([HubBuilder](../Editor/World/HubBuilder.cs), `Scenes/Hub/Hub.unity`, 빌드 목록에도 넣는다). 다시 누르면 처음부터 다시 지으니 씬을 손으로 고치지 말고 빌더의 배치를 고친다. 허브 씬이 빌드에 있으면 메인 메뉴의 혼자 하기·방 만들기가 허브로 들어가고, 장을 마치면 이야기 카드 뒤에 모두 허브로 돌아온다. 허브가 없으면 예전처럼 고른 장으로 바로 가고 장 끝에 다음 장이 열린다.

- 지도 탁자의 장 카드, 클래스 벽의 사물함(클래스마다 하나, 등급 휘장), 총 진열대(FPSBody의 총 전부)는 게임이 시작할 때 표에서 채우므로 장·클래스·총이 늘어도 다시 지을 필요가 없다. 장 카드 사진은 메인 메뉴와 같은 `Resources/UI/Maps/<씬>.png`.
- 장은 방장(혼자면 본인)만 고른다. 고른 장은 이 PC의 마지막 맵(`GameSettings.lastMap`)으로 남아 메인 메뉴의 맵 카드와 같다. 장을 마치고 돌아오면 다음 장이 골라져 있고, 마지막 장 뒤에는 첫 장으로 돌아가며 능력 카드가 비워진다.
- 준비 버튼 [F]: 방이면 모두 준비했을 때 `departSeconds`초를 센 뒤(그동안 출발 셔터가 올라간다) 방장이 방 전체를 그 장으로 데려가고, 누구 하나 준비를 풀면 멈춘다. 혼자면 `soloDepartSeconds` 뒤 바로 출발.
- 화면 위 가운데 준비 판([HubReadyBoard](../Scripts/Hub/HubReadyBoard.cs), TF2 MvM처럼): 방에 있는 사람마다 프로필 사진 한 칸(내 것은 메인 메뉴 프로필 사진, 친구 것은 그 PC가 보낸 작은 사진, 없으면 이름 첫 글자를 분홍 위에), 자리 순서(방장 먼저)라 모든 PC에서 같은 줄이다. 내 칸 밑 이름은 분홍 "나". 준비하면 그 칸 모서리에 흰 체크가 든 분홍 판이 크게 쾅 박히고 칸이 하얗게 번쩍인다(0.14초, 남지 않음). 준비 전에는 사진이 조금 어둡다. 오른쪽 끝 "준비 n/m"은 모두 준비하면 분홍이 되고 판이 한 번 번쩍인다. 혼자면 한 칸. 결과 연출 중에는 숨는다. 문구는 Texts `hub.ready.*`.
- 사격장 과녁은 보통 좀비와 같은 체력(`dummyHealth`, Enemies.csv `Enemy_Test`와 맞출 것)이라 넘어질 때까지 맞힌 수가 그 총으로 좀비 하나를 잡는 데 드는 수다. 각 PC가 자기 사격만 판정한다.
- 과녁은 연습용 적이다(`ICombatFoe.IsPractice`, [CombatFoe](../Scripts/Combat/Damage/CombatFoe.cs)): 넘어뜨린 한 발이 좀비와 똑같은 처치라 클래스가 처치로 하는 일이 모두 된다(어썰트 아드레날린과 CHARGE, 고스트 헤드 헌터·화이트 타이거, 시순트 현상금·리턴 백·데스페라도 탄·BOUNTY, 처치 표시와 소리). 맞힐 때마다 궁극기도 적처럼 찬다. F로 과녁을 찍을 수 있고 시순트면 청록 현상금 표식이 된다(방이면 다른 PC도 같은 자리의 자기 과녁에 표식, `RangeTarget.Home`). 대신 판에는 아무것도 남지 않는다: 킬 피드 줄·돈·점수·경험치·연속 처치·기록·결과·총 처치 수에 안 들어간다([GunFeelDirector](../Scripts/Combat/Feel/GunFeelDirector.cs)가 연습 처치를 건너뛰고, 기록·통계는 좀비만 센다). 능력 카드는 장 내내 남으므로 허브에서는 카드를 나눠 주지 않고, 가득 찬 게이지의 X는 바로 그 클래스의 궁극기다([PerkCardScreen](../Scripts/UI/PerkCardScreen.cs)). 허브에서 찬 게이지·스택은 장에 들어가면 플레이어가 새로 만들어지며 사라진다.
- 결과: 장이 끝날 때 각 PC가 모두의 숫자(처치·입힌 피해·치료·헤드샷 처치·구조·최장 연속 처치·최장 헤드샷·암살·처형·쓰러짐·번 돈)를 저장해 허브로 가져가고([MatchResults](../Scripts/Hub/MatchResults.cs)), 허브의 무대에 선 플레이어들 위로 칭호가 하나씩 쾅 붙은 뒤(혼자면 셋) KF2 같은 한 장으로 끝난다. 클래스 등급이 올랐으면 그 장에서 휘장이 박힌다. Space·Enter·클릭으로 건너뛴다. 콘솔 `results`로 마지막 장의 결과를 다시 볼 수 있다.

### 기록 저장 (records.json)

메인 메뉴 기록 탭의 숫자는 같은 폴더의 `records.json`([PlayerRecords](../Scripts/Player/PlayerRecords.cs))이다. 판이 끝나거나 게임을 끌 때 적힌다. 판 하나는 맵을 불러와서 떠날 때까지이고, 20초 안에 아무것도 안 하고 나간 것은 판으로 치지 않는다(시간만 더한다).

- 합계: 처치·헤드샷(총알 치명타로 죽인 것)·쓰러짐·죽음·살려 준 동료·전멸·판·탈출·플레이 시간.
- 장마다(CampaignChapters.csv에 있는 씬만): 탈출 수, 최고 기록과 그 날, 최소 죽음, 판 수, 그 장에 있던 시간, 처치·헤드샷·쓰러짐·죽음, 한 판 최다 처치, 첫 탈출과 마지막 판의 날짜. 기록 탭에서 장 줄을 누르면 오른쪽에 이것이 펼쳐진다.
- 최근 판은 40개까지 남긴다. 기록 탭 첫 화면은 8개, 장 기록은 그 장의 판 5개를 보여 준다.
- 0.8.34 전의 파일에는 장마다 탈출 기록만 있었다. 처음 읽을 때 남아 있던 최근 판(최대 8개)으로 장 기록을 한 번 채우고, 그보다 오래된 탈출은 판 수에만 더한다. 그래서 오래된 판의 처치 수 등은 장 기록에 빠져 있다.
- 기록을 지우려면 게임을 끄고 그 파일을 지운다.

새 무기는 `WeaponData` 에셋을 만들고 `Weapons.csv`에 같은 이름의 행을 추가한다. 새 적 프리팹도 같은 방식이며, 이름과 다른 행을 쓰려면 `EnemyHealth → Table Id`를 적는다.

## 그 밖의 수치를 바꾸고 저장하기

표에 없는 값은 **Play를 종료한 뒤 실제 사용 중인 씬·프리팹·데이터를 수정한다.** C# 필드에 적힌 초기값은 새 컴포넌트의 시작값이다. 이미 저장된 씬·프리팹의 수치를 덮어쓰지 않는다.

| 바꿀 내용 | 실제 편집 위치 | 적용 범위 |
| --- | --- | --- |
| 바닥으로 인정할 레이어 | 씬의 `PlayerMovement → Ground Layers`, `Ability Settings → Interaction Layers` | 해당 플레이어 |
| 무기 반동·조준 추적·화면 흔들림·사운드·아이콘 | [Data/Combat/Weapons](../Data/Combat/Weapons)의 해당 `WeaponData` | 그 데이터를 사용하는 모든 무기 |
| 시작 무기·선택 슬롯 | 플레이어의 `PlayerWeaponSlots` | 해당 플레이어 |
| 적 크기·밀치기 연쇄 | [적 프리팹](../Resources/Enemies)의 `MeleeCombatTarget` | 프리팹 인스턴스. 씬의 Override 확인 |
| 총성·히트마커·피격·처치·점수 효과 | [GunFeel.asset](../Resources/Combat/GunFeel.asset), 무기별 `Feel`. 히트마커 '깔끔'(기본, 페이데이 3식 얇은 선) 모양은 [HitMarkers.csv](../Resources/Tables/HitMarkers.csv) | 공통 효과 / 개별 무기 |
| 주먹·벽 타격음·방어·반격·처형·피해 팝업 | [Feedback.asset](../Resources/Combat/Feedback.asset) | 공통 전투 피드백 |
| 총구 화염·궤적, 적 몸의 상처 자국 | Player의 `Gun/…/MuzzlePoint → MuzzleEffects`, `Enemy Blood Effect` | 해당 플레이어 |
| 피 스플래시, 표면별 착탄, 총알 구멍(표면별 크기 `holeSize`, 크기 흔들림 `holeVary`, 비스듬히 맞았을 때 늘어나는 한도 `holeStretch`) | [ImpactEffects.csv](../Resources/Tables/ImpactEffects.csv), 구멍 유지 시간은 [GunFeel.asset](../Resources/Combat/GunFeel.asset)의 `Bullet Hole Seconds` | 공통 전투 피드백 |
| 이동·공격·UI 입력 | [Player.inputactions](../Input/Player.inputactions) | 입력 액션 사용 기능 |
| 슬롯 1~4 입력 | [PlayerWeaponSlots](../Scripts/Player/Combat/Weapons/PlayerWeaponSlots.cs)의 직접 키보드 처리 | 숫자열·숫자패드 슬롯 선택 |

1. 사용할 씬을 열고 실제 연결된 컴포넌트·데이터를 선택한다.
2. 값 하나 또는 관련된 작은 묶음을 수정한다. 씬 인스턴스의 Override와 프리팹 원본을 구분한다.
3. 씬과 프로젝트 에셋을 저장한다. 데이터 에셋은 Play 중에도 변경이 남을 수 있으므로 Play를 종료한 상태에서 편집한다.
4. 다시 Play해 효과를 확인하고 [해당 검사](ValidationGuide.md)를 실행한다.

예제 무기의 비교표는 [전투 제작 안내](CombatFoundationGuide.md)에 있다. 장탄·예비탄의 현재 값은 `WeaponRuntime`이 관리하며 공유 데이터에 저장하지 않는다.

플레이어의 `PlayerDamageReceiver`, `PlayerStats`, `UltimateCharge`, `PlayerMeleeCombat`, `PlayerExecution`은 씬에 두지 않고 `PlayerMovement`·`PlayerWeaponSlots`가 Play 시작 때 붙인다. 이 컴포넌트의 수치는 `Player.csv`에서 바꾼다. HUD 프리팹의 `PlayerStats`/`UltimateCharge`는 편집 화면 미리보기용이다.

플레이어는 여러 명일 수 있다. 플레이어가 필요한 코드는 씬을 검색하지 말고 [PlayerRoster](../Scripts/Player/PlayerRoster.cs)를 쓴다. 화면과 입력은 `Local`, 적은 `Nearest`다. `PlayerMovement.IsLocal`이 거짓인 플레이어는 이 PC가 조종하지 않는 캐릭터로, 입력·카메라·청취자·화면 효과 없이 몸과 주변에 들리는 소리만 남는다.

플레이어는 씬에 놓지 않는다. 씬의 [PlayerSpawnPoint](../Scripts/Player/PlayerSpawnPoint.cs)가 게임 시작 때 `Resources/Characters/Player.prefab`을 만들고, 더미도 같은 프리팹을 쓴다. 플레이어의 컴포넌트나 총 위치를 고칠 때는 이 프리팹을 연다. HUD는 모든 씬이 `Prefabs/UI/HUD/GameHUD.prefab` 하나를 쓴다. 프리팹은 씬의 물건을 가리킬 수 없으므로 플레이어와 HUD는 실행 중에 서로를 찾는다.

방은 [GameSession](../Scripts/Network/GameSession.cs)이 맡는다. 만든 사람의 PC가 호스트이고, 친구는 서버 찾기의 목록이나 주소로 접속한다. 방은 만들 때 고른 대로 인터넷이나 LAN 중 하나로만 열린다. LAN 방은 게임 포트 7777을 쓰고, 호스트의 [LanRoomBeacon](../Scripts/Network/LanRoomBeacon.cs)이 UDP 7778에서 묻는 쪽의 [LanRoomFinder](../Scripts/Network/LanRoomFinder.cs)에게 방 정보를 답한다. 인터넷 방은 [InternetRoom](../Scripts/Network/InternetRoom.cs)이 Unity Lobby에 올리고, [InternetRoomFinder](../Scripts/Network/InternetRoomFinder.cs)가 목록을 읽는다. 인터넷 방은 모두 가까운 Relay에 자리를 잡고, 그 위에 호스트와 친구가 서로 바로 붙는 길(직접 연결)을 먼저 시도한다. 되면 그 친구의 게임 데이터는 Relay를 거치지 않고, 안 되면 그 친구만 전처럼 Relay로 들어온다. 한 방에 두 가지 친구가 섞여도 된다. 순서는 이렇다. 호스트는 방을 만들 때 [PunchSocket](../Scripts/Network/PunchSocket.cs)으로 UDP 포트(7777, 차 있으면 7780까지, 그래도 없으면 아무 포트)를 열고, 그 소켓이 인터넷에서 어떤 주소와 포트로 보이는지 알아낸다. [RouterPortMap](../Scripts/Network/RouterPortMap.cs)이 공유기에 UPnP로 그 포트를 열어 달라고 해서 되면 공유기의 바깥 주소를 쓰고(누구나 바로 들어올 수 있다), 안 되면 공개 STUN 서버 두 곳([Network.csv](../Resources/Tables/Network.csv)의 `stunServers`)에 물어본다. 두 서버가 서로 다른 포트를 봤다면 공유기가 상대마다 포트를 바꾸는 종류라 밖에서 겨냥할 수 없으므로 바깥 주소는 알리지 않는다. 알아낸 주소와 방의 토큰, 집 안 주소는 방 정보 `direct`에 방 사람만 읽을 수 있게 적는다. 친구도 들어갈 때 PunchSocket을 열어 자기 바깥 주소를 알아내고, 그것을 방에 들어가는 요청에 붙여 보낸다(`ep`). 호스트는 새로 들어온 사람의 주소를 보고 그쪽으로 5초 동안 패킷을 보낸다. 집 공유기는 이쪽에서 방금 패킷을 보낸 상대에게서 오는 패킷만 들여보내기 때문에, 양쪽이 서로에게 동시에 보내야 길이 뚫린다(홀 펀칭). 친구는 호스트의 주소들에 `punchSeconds`(2.5초) 동안 탐침을 보내고, 방의 토큰이 맞는 답이 오면 그 길이 양방향으로 통한다는 뜻이다. 같은 공유기 아래 있는 두 PC는 바깥 주소로는 서로 못 닿는 일이 많아서, 바깥 주소가 같고 집 안 주소가 내가 붙어 있는 네트워크에 속할 때는 집 안 주소도 함께 시도한다. 답이 오면 친구는 소켓을 닫고 게임의 전송 계층이 바로 그 포트로 호스트에 접속한다(`ClientBindPort`). 공유기는 소켓이 아니라 포트를 기억하므로 뚫린 길이 그대로 쓰인다. 호스트 쪽은 방이 열려 있는 내내 새로 오는 친구마다 패킷을 보내 줘야 하는데 게임의 전송 계층은 그걸 못 하므로, PunchSocket이 계속 바깥을 맡고 게임의 직접 연결용 전송은 이 PC 안(127.0.0.1)에서만 듣는다. PunchSocket이 친구마다 이 PC 안 소켓을 하나씩 두고 패킷을 양쪽으로 넘겨서, 전송 계층은 친구를 서로 다른 주소로 본다. 이 경유는 왕복 0.1ms쯤이다. 호스트의 Netcode 전송은 [DualTransport](../Scripts/Network/DualTransport.cs)로, Relay용과 직접 연결용 전송 둘을 하나처럼 묶어 친구를 어느 쪽으로든 받는다. 답이 안 오면 친구는 그대로 Relay로 접속하므로, 실패가 치르는 값은 그 2.5초뿐이다. Relay 자리는 받고 10초 안에 써야 해서 `punchSeconds`를 길게 잡으면 안 된다. 탐침은 통했는데 게임 접속이 4초 안에 안 되는 경우에는 GameSession이 그 방을 Relay로 한 번 더 자동으로 들어가고, 그 방은 게임을 끌 때까지 Relay로만 들어간다. 직접 붙은 친구도 Relay 자리는 [RelayKeeper](../Scripts/Network/RelayKeeper.cs)가 붙잡아 둔다. 자리가 10초 동안 조용하면 로비가 그 사람을 방에서 나간 것으로 쳐서 방 목록의 인원이 틀어지기 때문이다. 호스트는 15초마다 STUN 서버에 다시 물어 공유기가 주소를 잊지 않게 하고, 주소가 바뀌면 방 정보를 고친다. UPnP 포트는 1시간 임대로 열고 방이 열려 있는 동안 30분마다 다시 연다. 방을 닫으면 돌려주고, 게임이 비정상으로 꺼져도 임대가 끝나면 닫힌다. 빌드한 exe로 처음 방을 열면 Windows 방화벽 창이 뜨는데, 허용하지 않아도 홀 펀칭은 이쪽에서 먼저 보내는 방식이라 통한다. `direct`를 0으로 두면 이 전부가 꺼지고 모든 방이 Relay로만 열린다. 대칭형 공유기나 통신사 NAT 뒤, UDP가 막힌 곳에서는 직접 연결이 안 되고 Relay로 간다. 플레이어는 [OnlineServices](../Scripts/Network/OnlineServices.cs)의 익명 로그인을 쓰므로 Unity 계정이 없어도 된다. Relay는 호스트를 가리므로 Relay 방의 핑은 내 PC와 호스트가 각각 Relay 지역까지 잰 왕복 시간을 더한 추정값이다. 직접 연결이 될지는 들어가 봐야 알므로 목록의 핑은 언제나 Relay 기준이다. 화면의 핑은 실제 값이다. [PingDisplay](../Scripts/Network/PingDisplay.cs)가 [Network.csv](../Resources/Tables/Network.csv)의 `pingDivisor`로 나눠 보일 수 있지만 지금은 1이다. 모든 방이 도쿄 Relay를 거치던 때는 3으로 나눠 일부러 작게 보였다. 지연을 측정할 때는 어느 쪽이든 나누기 전 값을 쓴다. 콘솔의 `/net`이 방의 실제 핑, 초당 전송량, 프레임을 [NetworkStatsHUD](../Scripts/Network/NetworkStatsHUD.cs)로 보여 주고, 다시 입력하면 끄면서 전체 결과를 출력한다. 어느 길로 붙었는지도 여기 나온다. 친구 쪽은 "직접 연결(홀 펀칭)", "직접 연결(같은 공유기)", 또는 "릴레이(지역) 경유"와 직접 연결이 안 된 까닭을, 호스트 쪽은 직접과 Relay로 온 사람 수와 사람마다 어느 쪽인지를 보여 준다. 전송량은 [CountingTransport](../Scripts/Network/CountingTransport.cs)가 세는 게임 데이터로, 패킷 헤더는 포함하지 않는다. 남의 캐릭터와 좀비는 다음 위치가 늘 먼저 와 있도록 조금 과거로 그린다(보간 버퍼). Netcode는 이 버퍼를 핑에 비례해 잡는데, 그러면 친구 PC에서 방장 쪽(좀비 포함)은 쓸데없이 늦고 방장 PC에서 친구는 핑이 33ms만 넘어도 모자라 끊긴다. 그래서 [NetSmoothing](../Scripts/Network/NetSmoothing.cs)이 `PlayerPose.Tick`(보낸 쪽이 찍은 틱)으로 업데이트가 실제로 몇 틱 늦게 오는지 방장 쪽과 참가자 쪽을 따로 재서, 둘 다 제때 오는 만큼만 버퍼를 둔다(늦은 쪽 `bufferPercentile`까지 덮고 `bufferMargin`을 더함, 늘릴 때는 바로 줄일 때는 `bufferHold`초 뒤 1틱씩). `/net`의 Buffer 줄에 지금 몇 틱(ms)인지와 필요한 양이 나온다. `smoothing`을 0으로 하면 `bufferSeconds` 고정이다. 좀비 위치는 플레이어처럼 매 틱 보내지 않고 가장 가까운 플레이어와의 거리에 따라 `zombieNearRate`(15m 안 60번)에서 `zombieFarRate`(45m 밖 20번)까지 보낸다([EnemyNetwork](../Scripts/Network/EnemyNetwork.cs)가 차례가 아닌 틱에는 NetworkTransform의 문턱값을 아주 크게 둬서 막는다). 목록 화면은 `IRoomList`만 보므로 다른 방 찾기 방식을 더해도 화면은 그대로다. 혼자 하기에서는 GameSession이 만들어지지 않으므로 네트워크가 전혀 돌지 않는다. 내 이동은 내 PC가 계산해 보내고, 남의 캐릭터는 `PlayerMovement.IsNetworkDriven`으로 표시되어 받은 위치를 따른다. 몸과 소리, 시선은 [PlayerNetworkPose](../Scripts/Network/PlayerNetworkPose.cs)가 네트워크 틱마다(`tickRate`, 초당 120번) 보내는 [PlayerPose](../Scripts/Network/PlayerPose.cs)로 맞춘다. 좌우 시선을 `NetworkTransform`에 맡기면 보간 버퍼 때문에 돌리는 동작이 늦게 오고 꼬리가 끌리므로, 플레이어 프리팹의 `NetworkTransform`은 위치만 보낸다. 받는 쪽의 [MovementMirror](../Scripts/Player/Movement/Core/MovementMirror.cs)가 이 값을 이동 상태에 그대로 써 넣으므로, 상태 코드는 돌지 않아도 `PlayerBody`, 손 IK, 발소리가 평소처럼 읽는다. 몸이 새 이동 값을 읽게 되면 `PlayerPose`와 `MovementMirror`에도 더해야 상대에게 보인다. 스킨은 같은 컴포넌트가 따로 보낸다.

적은 호스트가 돌린다. 적 프리팹은 [Resources/Enemies](../Resources/Enemies)에 두며, 이 폴더의 네트워크 프리팹은 모든 PC에 자동으로 등록된다. 다른 PC의 적은 [EnemyNetwork](../Scripts/Network/EnemyNetwork.cs)가 AI와 NavMesh를 끈 복사본이고, 호스트의 위치와 체력만 보여 준다. 쏜 사람의 PC가 자기 복사본에서 명중을 판정해 효과를 바로 보여 주고 호스트에 알리면, 호스트가 진짜 체력을 깎고 다른 PC에 알린다. 웨이브는 호스트만 진행하고, 씬의 `WaveManager`가 네트워크 오브젝트가 아니므로 표시는 [WaveSync](../Scripts/Network/WaveSync.cs)가 메시지로 보낸다. `CombatDamageReport.ByOtherPlayer`인 타격은 세상에는 보이지만 히트 마커, 점수, 킬 화면은 쏜 사람에게만 나온다.

적의 공격은 호스트가 시작을 알리고 맞을 사람의 PC가 판정한다. `EnemyCombatAI.ShowWindup`이 그 PC에서 예고 시간을 직접 세므로 예고음과 타격의 간격이 혼자 할 때와 같고, 패리와 회피도 입력 지연 없이 판정된다. 패리나 주먹이 적에게 주는 체간 피해, 경직, 밀림은 `MeleeCombatTarget`이 복사본에 바로 적용하면서 호스트에도 보낸다. 체간, 경직, 잡힘, 쓰러짐의 기준은 호스트이고 `EnemyNetwork`가 0.1초 간격으로 복사본에 알린다. 복사본은 방금 자기가 한 일을 잠깐 유지해서, 호스트가 듣기 전에 보낸 값에 되돌려지지 않게 한다. 처형은 내 PC에서 바로 시작하고 호스트가 예약을 확인하며, 다른 사람이 먼저 잡았으면 늦은 쪽이 취소된다. 호스트는 처형 중인 사람의 복사본을 `ShowExecuting`으로 표시해 주변 적을 물러나게 한다. 넘어짐은 호스트의 물리가 위치를 만들고, 복사본은 `EnemyParryRecoil.Lie`로 쓰러진 모습만 입힌다. 플레이어 체력은 각자의 PC가 진짜이고, 다른 PC에는 `PlayerNetworkPose`로 알린다.

다른 플레이어의 복사본에도 `WeaponController`와 손 IK가 그대로 있다. [PlayerNetworkCombat](../Scripts/Network/PlayerNetworkCombat.cs)이 고른 무기, 조준, 재장전, 가드를 보내고 사격, 주먹, 주먹 소리는 일어날 때마다 알린다. 복사본은 `ShowShot` 등으로 총구 화염, 궤적, 탄착, 소리를 자기 화면에 재현할 뿐 데미지는 주지 않는다. 내 무기와 주먹 소리는 2D, 남의 것은 3D로 난다.

체력이 0이 되면 [PlayerLife](../Scripts/Player/Combat/PlayerLife.cs)가 정한다. 서 있는 동료가 있으면 쓰러져서 권총만 들고 기어 다니고, 쓰러짐 체력이 시간과 피격으로 줄어든다. 동료가 옆에서 F를 누르고 있으면 일어난다([PlayerRevive](../Scripts/Player/Combat/PlayerRevive.cs)). 살리는 동안은 출혈이 멈추고, 양쪽 화면에 누가 살리는지와 남은 시간이 나온다. 살리는 사람의 진행 상황은 그 사람의 `Stance`로 전달되므로 끊기거나 나가면 바로 출혈이 다시 시작된다. 적에게 맞으면 살리는 중에도 쓰러짐 체력이 줄어든다. 다 줄면 죽고 스폰 지점에서 돌아온다. 웨이브가 진행 중이면 다음 웨이브에, 아니면 `respawnSeconds` 뒤에 돌아온다. 서 있는 사람이 없으면 호스트가 전멸을 알리고, 모두 스폰 지점으로 돌아와 1웨이브부터 다시 한다. 혼자 하면 체력 0이 곧 전멸이다. 수치는 [Downed.csv](../Resources/Tables/Downed.csv)에 있다. 즉사 공격(`DamageHit.IsFatal`, 적은 [Enemies.csv](../Resources/Tables/Enemies.csv)의 `fatalHit`)은 남은 체력과 쉴드, 쓰러짐을 건너뛰고 바로 죽인다. 회피와 패링은 그대로 통한다. [PlayerRagdoll](../Scripts/Player/Body/PlayerRagdoll.cs)은 몸이 만들어질 때 물리 인형 [RagdollCorpse](../Scripts/Combat/Feel/RagdollCorpse.cs)를 미리 만들어 꺼 둔다. 싸움 도중 만들면 끊기기 때문이다. 휴머노이드가 뼈를 찾지 못하는 몸에는 인형을 만들지 않고, 그 몸에는 다시 시도하지 않는다. 예전에는 매 프레임 뼈대를 복사하다 실패하며 복사본을 남겨 게임이 점점 느려졌다(2026-11-12, 옷의 뼈 이름이 겹쳐 아바타가 깨졌을 때). 인형은 주요 뼈마다 물리 부품 하나씩뿐이고 메시도 뼈대 복제본도 없다. 죽으면 진짜 몸이 그대로 쓰러진다. 클립, IK, 시선은 복귀할 때까지 멈추고(`PlayerBody.GoLimp`) 인형을 몸의 자세에 맞춘 뒤, 매 프레임 인형의 회전을 진짜 뼈에 복사한다. 물리가 진짜 뼈를 직접 건드리지 않으므로 복귀할 때는 클립만 다시 틀면 된다. 줄이 끊긴 꼭두각시처럼 한 번에 무너지지 않도록 네 가지를 더한다. 팔다리는 죽기 직전 프레임의 움직임을 이어 가고(`Launch`), 근육 역할만 하는 두 번째 관절이 죽은 순간의 자세를 붙잡았다가 잠깐 사이에 힘이 빠진다(`Brace`). 그래서 무릎부터 꺾이며 쓰러지고, 힘이 빠진 뒤에도 관절 마찰이 남아 떨지 않고 멈춘다. 팔다리는 몸을 뚫지 않는다. 처음부터 겹쳐 있던 부위끼리는 떨어질 때까지 통과시켜 튀지 않게 한다. 손과 발에도 충돌 모양이 있어 바닥에 박히지 않고, 가슴뼈가 있는 리그는 허리에서도 꺾인다. 관절은 사람 몸이 도는 방향으로 다 돈다. 처음 만든 인형은 Ragdoll Wizard처럼 팔다리가 자기 길이 방향으로 도는 회전(Swing 2)을 0으로 막아서, 굽은 팔꿈치나 무릎이 옆으로 쓰러지지 못하고 허공에 뻣뻣하게 서 있었다. 지금은 팔뚝, 정강이, 고개, 허리도 돌고, 어깨와 골반은 팔다리를 움직이는 범위의 한가운데로 돌려 놓은 자세에서 만든 구관절(`Socket`)이라 원뿔 범위가 몸이 실제로 닿는 범위를 덮는다. 관절은 만들 때의 자세를 기준으로 제한을 재기 때문이다. 인형에 없는 뼈(쇄골, 목, 손, 손가락, 발)도 근육과 함께 힘이 풀려서, 총을 쥔 손 모양이나 움츠린 어깨가 그대로 남지 않는다. 죽은 자리에 남는 캡슐은 다른 PC에서 아예 꺼 둔다. `detectCollisions`만 꺼서는 걷는 캐릭터가 부딪치기 때문이다. 시체 물리는 죽은 사람의 PC가 계산하고 [PlayerNetworkCorpse](../Scripts/Network/PlayerNetworkCorpse.cs)가 자세를 보내, 모든 PC에서 같은 모습이 된다. 그 사람의 카메라는 시체의 눈에 붙는다. 시체는 `IDamageReceiver`라서 총알, 주먹, 밀치기가 모두 같은 경로로 시체를 민다. 수치는 [Ragdoll.csv](../Resources/Tables/Ragdoll.csv)에 있다.

슬로 모션은 혼자 하면 `Time.timeScale`로 세상 전체를 늦춘다. 방에서는 호스트의 시간을 늦추면 적 전체가 느려지므로, 고스트 오브 쓰시마 전설 모드처럼 그 플레이어와 상대 적만 느려진다. 플레이어 쪽은 [CombatTimeEffects](../Scripts/Player/Combat/CombatTimeEffects.cs)의 `Pace`가 `PlayerMovement.DeltaTime`과 몸 애니메이션 속도를 정하고, 적 쪽은 `EnemyNetwork.Pace`가 모든 PC에서 공격, 이동, 체간 시간을 늦춘다. 짧은 히트스톱은 네트워크 지연보다 짧아서 본인에게만 걸린다.

치마, 겉옷, 머리카락은 그 캐릭터를 플레이하는 PC가 [PlayerBodyCloth](../Scripts/Player/Body/PlayerBodyCloth.cs)에서 계산하고, [PlayerNetworkCloth](../Scripts/Network/PlayerNetworkCloth.cs)가 움직이는 뼈의 위치를 [ClothPose](../Scripts/Network/ClothPose.cs)로 보낸다. 다른 PC의 복사본은 Magica Cloth를 돌리지 않고 받은 위치를 따라가며(`ClothBones.Follow`), 회전은 위치로 다시 계산하므로 뼈 하나에 3바이트만 쓴다. 신뢰성 없는 전송이라 잃어버린 자세는 다시 보내지 않고, 천이 멈춰 있으면 1초에 한 번만 보낸다. 통신량은 `/net`으로 본다. 치마는 주인 PC의 다리에 맞춰 계산되므로 다른 PC의 다리도 같은 박자여야 뚫리지 않는다. 그래서 걷기·달리기 박자를 `PlayerPose.Step`으로 함께 맞춘다. 팔이 치마를 누르는 것([SkirtPress](../Scripts/Player/Body/SkirtPress.cs))은 보내지 않는다. 계산하는 PC는 누르기 전의 자세를 보내고(`PlayerBodyCloth.Simulated`), PC마다 자기 화면의 팔로 누른다. 누르기에 쓰는 몸 피부와 치마 겹 두께는 PC마다 같은 메시에서 재므로 똑같다. 천과 부딪히는 콜라이더는 계산하는 PC에서만 쓴다. 복사본은 모델의 Read/Write가 꺼져 메시를 읽을 수 없을 때만 다리 콜라이더를 만들어, 눌린 치마가 다리 안으로 들어가지 않게 거리를 재는 데 쓴다. 시체는 진짜 몸이 쓰러진 것이라 천도 그대로 계속 움직인다.

쓰러진 동료는 [DownedAura](../Scripts/Combat/Feel/DownedAura.cs)가 벽 너머로도 보이게 빛내고, [TeammateMarkerHUD](../Scripts/UI/TeammateMarkerHUD.cs)가 이름과 거리, 남은 시간을 표시한다. 동료가 쓰러지거나 죽는 순간, 거구가 나타나는 순간, 내 실드가 깨지는 순간은 화면 위 알림 띠([AlertHUD](../Scripts/UI/Hud/AlertHUD.cs))가 일이 난 쪽에서 들어오며 알린다. 띠의 크기와 시간은 그 파일 위쪽 상수에 있다. 수치는 [DownedAura.csv](../Resources/Tables/DownedAura.csv)에 있다. 킬 피드의 줄은 [KillFeedRow](../Scripts/UI/KillFeedRow.cs)이고, 내 처치는 금빛으로 구별된다. 왼쪽의 [StreakBadge](../Scripts/UI/StreakBadge.cs)는 연속 처치를 로마 숫자와 불꽃으로 보여 주고, XXV를 넘으면 파란 눈 해골로 바뀐다. 연속 처치 판정은 점수 팝업의 `multiKillWindow`에 `streakGrace`를 더한 시간 안에 다음 처치를 해야 이어진다. 방에서는 호스트가 적의 실제 죽음을 한 번만 세어 그 숫자와 줄을 모든 PC에 보내므로(`PlayerNetworkScore.AnnounceKill`) 어느 PC에서나 같게 보인다. 불꽃은 `FeedFlame` 셰이더가 그리며 수치는 [KillFeed.csv](../Resources/Tables/KillFeed.csv)에 있다. 숫자는 TMP의 Bold 대신 재질의 `FaceDilate`로 굵기를 내고 그림자를 좁게 잡는다. Bold와 넓은 그림자를 함께 쓰면 글자 상자가 폰트 아틀라스 속 글자 자리 밖까지 늘어나, 옆에 놓인 글자 조각이 숫자 주위에 점과 선으로 묻어난다. Tab 점수판은 [ScoreboardHUD](../Scripts/UI/ScoreboardHUD.cs)이고, 숫자는 [PlayerTally](../Scripts/Player/Combat/PlayerTally.cs)에 모이며 [PlayerNetworkScore](../Scripts/Network/PlayerNetworkScore.cs)가 다른 PC로 보낸다.

채팅은 [PlayerChat](../Scripts/Network/PlayerChat.cs)이 호스트를 거쳐 다른 PC로 보낸다. `/` 명령어와 콘솔 채널은 공유되지 않는다. 표시 이름은 메인 메뉴 프로필에서 정하는 `GameSettings.DisplayName`이며, 비어 있으면 PC 사용자 이름을 쓴다.

궁극기 스탯의 기본값은 **최대 보유량 120%, 1회 소비량 100%**다. 120%에서 사용하면 20%가 남고, 소비량을 70%로 바꾸면 50%가 남는다. 현재 충전량은 `UltimateCharge`, 최대 보유량과 소비량은 `PlayerStats`가 관리한다. 최대 보유량 변경은 현재 충전량을 유지하며, 최대치가 줄어들 때만 초과분을 잘라낸다. 소비량이 최대 보유량보다 크면 사용할 수 없다.

궁극기는 초상화 왼쪽 테두리의 탄창([UltimateMagazine](../Scripts/UI/HP/UltimateMagazine.cs), D 4차 시안 메모: B의 자리, C의 움직임, A의 탄창 교체)으로 보인다. 한 발은 1회 소비량의 10분의 1이라 열 발이면 준비이고, 최대 보유량까지 남는 칸은 분홍 선 위의 여분 칸이다(기본 120%면 12칸). 탄은 아래부터 쌓이고, 다음 탄은 충전되는 만큼 테두리가 그려지다 다 차면 왼쪽에서 밀려 들어온다. 열 발이 차면 탄창 위 약실 덮개가 분홍 판으로 닫히고, 능력 카드로 쓰면(`TryConsume`) 판이 열리며 쓴 탄이 위로 빠져 탄피가 왼쪽으로 튀고, 빈 탄창이 떨어진 뒤 남은 탄을 담은 새 탄창이 올라와 꽂힌다. [HudPortrait](../Scripts/UI/Hud/HudPortrait.cs)가 Play 시작 때 탄창을 만들어 사진 재질의 크기(`_Half`, `_MatWidth`)에 맞춰 붙이고, 수치는 [UltMagazine.csv](../Resources/Tables/UltMagazine.csv)에 있다. 예전 흰 궁극기 막대(`UltimateHUD`, `UltIcon`·`UltBar`·`UltReadyThresholdIcon`), 불꽃(`UltimateFireEffect`), 사진 테두리 발광(`UltimatePortraitGlow`)은 GameHUD에서 꺼 두었다. `/ultmax 200`은 최대 보유량을 200%로, `/ultcost 70`은 1회 소비량을 70%로 지정한다. `/ultmax 20`은 20%를 더하는 것이 아니라 최대 보유량을 20%로 설정한다. 이 명령은 Play 중 테스트 상태만 바꾼다.

체력바는 D 4차 시안 A다([HealthBarFlair](../Scripts/UI/HP/HealthBarFlair.cs), 막대 그림은 [SlantedHealthGraphic](../Scripts/UI/HP/SlantedHealthGraphic.cs)). 맞으면 잃은 만큼이 칸에서 조각으로 떨어져 나가고 남은 칸이 잠깐 하얗게 번쩍이며 숫자가 새 값으로 굴러 내려간다. 실드는 체력 칸 위에 같은 양·같은 간격의 칸으로 나뉘고, 깨지지 않은 피격은 잃은 만큼 하늘색 조각이 떨어진다(다 깨질 때는 `ShieldShatter`). 체력이 최대의 25% 이하면 칸에 어두운 빗금이 흐르고 숫자가 분홍으로 뛰며 바 끝에 빗금 표시와 "위험"이 뜬다. 회복하면 채움 끝이 빛나고 바 끝에 +N이 뜬다(실드는 하늘색). 수치는 [HealthBar.csv](../Resources/Tables/HealthBar.csv)에 있다. 체력바 오른쪽 끝 옆, 바 높이에는 지금 자세가 작은 흰 그림으로 선다([StanceFigure](../Scripts/UI/HP/StanceFigure.cs)): 총 들고 서기, 한쪽 무릎 앉기, 엎드리기, 슬라이딩, 등 대고 눕기, 다운(분홍), 사다리·매달리기. 자세가 바뀌면 새 그림이 분홍으로 가운데에서 커졌다가 0.14초 만에 흰색 제 크기로 돌아온다. 처음(11-11)에는 바 위, 체력 숫자 줄 안에 서 있어서 실드 띠·실드 +N·회복 +N·위험 표시와 겹쳤다(11-12 사용자: "너무 겹침. 위치 좀 여유 있게"). 지금은 제 자리가 있다: 바 끝(실드 띠가 더 길면 그 끝)에서 `Stance.gap`만큼 떨어지고(커진 순간에도 그만큼, 쉴 때는 약 15px), 바 끝의 위험 표시와 회복 +N은 그림 다음에 뜬다(`StanceFigure.End` → `HealthBarFlair.Place`). 그림은 바의 자식이라 바가 기울고 밀리고 흔들려도 바 끝을 따라가고, 그 위에 제 각도로 더 기운다(`Stance.roll`·`yaw`, 설정의 HUD 기울기가 곱해진다). HudTilt.csv는 여전히 바의 겹침 판정에서 그림을 빼므로 체력 숫자 줄과 바 사이 띠(HudBridge)와 간격은 바만 보고 정해진다. 숫자 줄 글자는 매 프레임 재서 `Stance.clear`보다 가까이 오면 그림이 그 오른쪽으로 비켜 서지만, 숫자 줄이 바보다 높아 보통은 그럴 일이 없다. 16:9·16:10, HUD 원근 각도 0/20/40°, HUD 기울기 0/1/2, Q/E 기울이기, 실드 +N 길이, 최대 체력 100/150/300을 원근 투영으로 재어 보니 쉴 때 이웃과 가장 가까운 곳이 6px, 커진 순간 0.1px로 어디서도 겹치지 않는다(예전 자리는 최대 26px 겹쳤다). 그림은 `Tools/Icons/stance_icons.py`로 다시 그리고(파이썬 표준 라이브러리만), 크기·간격은 [Hud.csv](../Resources/Tables/Hud.csv)의 Stance 줄이다.

## 폴더 이름·저장 위치를 바꿨을 때

1. Unity와 코드 편집기를 닫고 폴더를 이동하거나 이름을 바꾼다.
2. Unity Hub에서 새 폴더를 다시 추가한다. 프로젝트는 루트의 `Assets`, `Packages`, `ProjectSettings`를 기준으로 연다.
3. 지정 버전으로 열고 임포트가 끝날 때까지 기다린다. 검사 스크립트는 자신의 위치에서 루트를 찾으므로 절대 경로를 고칠 필요가 없다.
4. 이전 이름의 `.slnx`, `.sln`, `.csproj`는 Unity가 재생성하는 파일이다. External Tools의 프로젝트 파일 재생성을 사용한다.
5. 캐시 문제가 지속되면 Unity를 종료한 상태에서 `Library`를 재생성할 수 있다. 첫 임포트가 다시 필요하다. `Assets`, `Packages`, `ProjectSettings`와 `.meta`는 보존한다.

폴더명, Player Settings의 Product Name, 기존 셰이더·편집기 세션 키는 서로 다른 값이다. 이번 정리에서 문서·무기 생성 메뉴는 ENFILADE로 맞췄다. 기존 `Hidden/Morphang/...` 셰이더 식별자와 세션 키는 연결된 코드와 자료가 사용하므로 유지한다. 출시용 제품명·플랫폼 식별자는 Player Settings에서 별도로 결정한다.

## 에셋과 파일 정리

- 에셋 이동은 Unity Project 창에서 한다. 파일 도구로 옮긴다면 원래 `.meta`를 함께 옮겨 GUID를 보존한다. 씬·프리팹 연결은 GUID에 의존한다.
- `Resources/Combat/Feedback.asset`과 `GunFeel.asset`의 로드 키는 각각 `Combat/Feedback`, `Combat/GunFeel`이다. 경로를 바꿀 때는 코드의 키도 함께 바꾼다.
- 외부 폰트·모델은 씬·재질·프리팹뿐 아니라 문자열 로드에서도 사용한다. 참조와 라이선스를 확인한 뒤 정리한다.
- 다른 파이프라인에서 URP로 옮겨 온 에셋의 재질에는 HDRP의 재질 버전 블록(스크립트 GUID `da692e001514ec24dbc4cca1949ff7e8`)이 남아 있곤 하다. 이 프로젝트에는 그 스크립트가 없어서, 가져오거나 불러올 때마다 "The referenced script (Unknown) on this Behaviour is missing!"가 재질 수만큼 뜬다. 아무것도 그 블록을 가리키지 않으므로 `--- !u!114`부터 다음 `---` 앞까지 지우면 된다. 셰이더(URP/Lit)는 그대로다. 2026-11-12에 ThirdParty 재질 186개(공항 터미널 18, Piloto 102, KINEMATION 47, Magica 예제 16, DAVFX 2, Vefects 1)에서 지웠다. 새 에셋을 들이면 같은 블록이 있는지 본다. BOOTH 원본 프리팹의 VRChat 스크립트는 일부러 남겨 둔다. BOOTH 가져오기가 읽은 뒤 사본에서 지운다.
- TMP 설정(`ThirdParty/TextMeshPro/Resources/TMP Settings.asset`)의 전역 대체 폰트는 Pretendard SemiBold다. 영어 폰트(TMP 기본 LiberationSans, KINEMATION의 Roboto)로 만든 글자에 한글이 들어와도 네모(□)와 경고 없이 이 폰트로 그린다. 2026-11-12에 탄약 HUD 상태 줄의 총 칭호("갓 뜯은 · 처치 · 헤드샷")가 LiberationSans로 그려지며 프레임마다 글자 수만큼 경고를 냈다.
- `Tools` 메뉴에는 `Data Tables`(표 폴더 열기·다시 읽기), `Models`(임포트 규칙 재적용·물리 프랍 만들기), `TextMeshPro`(Dynamic 폰트 정리·저장), `HUD`(현재 씬에 HUD 넣기·갱신, HUD 프리팹 열기)만 둔다. 전투 HUD는 [GameHUD](../Prefabs/UI/HUD/GameHUD.prefab) 프리팹 하나를 모든 플레이 씬이 같이 쓴다. 새 씬에 HUD를 넣거나 씬의 HUD를 프리팹과 똑같이 맞출 때는 `Tools → HUD → 현재 씬에 HUD 넣기·갱신`을 누른다. 이때 미션 바 모양도 Hud.csv 값으로 프리팹에 반영되어, Play 전 편집 화면에서도 Play 때와 같은 모양이 보인다. 킬로그·점수 팝업·동료 체력·Tab 화면·설정 창은 어느 씬에서나 코드가 스스로 만든다. HUD는 프리팹에서 직접 편집하며, 숨겨진 새 목표 알림은 `MissionHUD` Inspector의 편집 미리보기 버튼으로 펼친다. 목표 목록은 Tab 화면의 [ObjectiveLogPanel](../Scripts/UI/ObjectiveLogPanel.cs)이 코드로 만든다.
- 공유할 소스는 `Assets`와 `.meta`, `Packages`, `ProjectSettings`, `Tools`, 문서다. `Library`, `Temp`, `.vs`, 생성 솔루션·업그레이드 로그는 재생성 가능한 로컬 파일이다.

문서 링크·메타데이터를 확인하려면 프로젝트 루트에서 `& ./Tools/Test-ProjectStructure.ps1`을 실행한다. 게임 기능 변경은 실제 씬 조작으로 확인한다.

## 플레이어 설정 (Esc 메뉴)

**Esc**를 누르면 게임이 멈추고 설정 메뉴가 열린다. Esc를 다시 누르거나 "돌아가기"를 누르면 이어서 하고, 이때 설정이 저장된다. 채팅이나 개발자 콘솔을 닫는 Esc로는 메뉴가 열리지 않는다. "이 탭 기본값"은 보고 있는 탭의 설정만 기본값으로 돌린다. 닉네임·마지막 맵·방 설정은 어느 탭에서도 돌리지 않는다.

| 분류 | 항목 |
| --- | --- |
| 소리 | 전체, 전투, 이동, 목소리, UI 볼륨 |
| 조작 | 키 설정(맨 위 줄의 "모든 키 바꾸기"가 따로 창을 연다. 아래 **키 설정**), 마우스 감도(×0.2~3, `PlayerLook`의 기본 감도에 곱함), 상하 반전, 앉기·기울이기를 "누르는 동안" 또는 "눌러서 전환", 자동 장전, 콜아웃(놓으면 말하기·동료 말풍선·저절로 외치기) |
| 화면 | 디스플레이: 모니터, 화면 모드(전체 화면·테두리 없는 창·창 모드), 해상도(첫 칸 "모니터 해상도"는 그 모니터 자체 해상도. 다른 모니터로 옮겨도 맞는다). 빌드한 게임에서만 적용되고 에디터에서는 저장만 된다([DisplaySettings](../Scripts/Settings/DisplaySettings.cs)). 모니터를 바꾸면 창을 먼저 옮기고, 옮긴 뒤 그 모니터의 해상도 목록을 다시 보여 준다. 프레임: 수직 동기화, 프레임 제한. 시야: 시야각(16:9 화면 기준 가로 70~120°, 기본 90°. 세로 약 59°로 이전 기본 60°와 거의 같다. 더 넓은 화면은 옆이 더 보인다. 달릴 때의 시야 넓힘과 ADS 줌은 이 값에서 계산한다), 화면 흔들림: 걸음 흔들림, 착지, 충격, 볼트 펀치, 이동·다이브 기울임에 곱한다. Q/E 기울이기와 총 반동은 영향을 받지 않는다. 내 머리카락 보기 |
| 카메라 | 파쿠르 3인칭: 애송이 모드(기본 꺼짐. 켜면 동작별 줄이 보인다: 넘기·오르기, 사다리 오르내리기, 착지 구르기, 회피, 짚라인, 슬라이딩, 철봉 돌기. 그 동작 동안 카메라가 몸 뒤로 빠졌다가 돌아온다). 연출: 처형 시네마틱(기본 켜짐. 처형·암살 동안 몸 밖에서 각도를 바꿔 보여 준다). 자세한 것은 [몸 안내](PlayerBodyGuide.md)의 **몸 밖 카메라** |
| HUD | HUD 흔들림(시선을 돌릴 때 HUD가 뒤처지는 정도), HUD 기울기(요소마다 따로 기운 각도의 배율 0~200%: HudTilt.csv 행, 동료 카드, 점수 팝업. 0%면 면 위에 평평하게 눕는다. HudTilt 조각들은 제각각 기울되, 매 프레임 그리는 영역을 재서 기울인 탓에 평평할 때보다 가까워지면 그만큼 서로 밀어내므로 배율을 올려도 겹치지 않는다. 최소 간격은 HudTilt.csv `gap`, 불꽃·파편처럼 조각 밖으로 그려지는 효과는 `ignore`에 이름을 적어 판정에서 뺀다. `bridge`에 바로 아래 조각 이름을 적으면 두 조각 사이 틈 가운데로 `bridgeColor` 얇은 띠(HudBridge)가 지나간다. 띠는 최대 `bridgeWidth` 두께이고, 두 조각에서 최소 `bridgeGap`만큼 떨어져 있으며, 틈이 좁은 쪽으로 가늘어져 뾰족하게 끝난다. 넓은 쪽 끝에는 `bridgeAccent` 색 작은 삼각형이 붙고, 그 바로 바깥에 바 칸처럼 기운 같은 색 작은 조각이 하나 더 있다. 지금은 HP 숫자와 HP 바 사이에 분홍기가 아주 살짝 도는 흰 띠, 끝 포인트는 바와 같은 분홍), HUD 원근 각도(좌우 HUD 면이 화면 가운데 쪽으로 멀어지는 각도 0~40°, 기본 20°. 0°면 평평한 HUD), 기울이기 따라가기(Q/E로 화면이 기울 때 좌우 HUD 면도 세상이 도는 쪽으로 같이 돈다. 도는 몫은 Hud.csv `Lean.share`, 각 면의 바깥 모서리 가운데가 축이라 모서리가 화면 밖으로 나가지 않는다), HUD 그리드 배경, 데미지 숫자 |
| 캐릭터 | 캐릭터 카드(카즈사(밴드)·히로·실)와 고른 캐릭터의 색 카드에서 즉시 바꾼다. 오른쪽 큰 사진이 고른 모습이다. 메인 메뉴의 `캐릭터` 버튼도 같은 화면을 연다. 선택은 메뉴를 닫을 때 저장되며 기본 캐릭터는 카즈사다. 화면 분류의 `내 머리카락 보기`는 선택한 스킨에도 적용된다. 자세한 것은 [몸 안내](PlayerBodyGuide.md) |

- **숫자 칸**: 슬라이더 줄 끝의 값(80%, 1.00×, 90° 같은 것)은 숫자 칸이다. 누르면 숫자만 남고 통째로 선택되니 바로 쳐서 바꾼다. Enter를 누르거나 다른 곳을 누르면 적용되고, Esc는 원래 값으로 돌린다(그 Esc로는 메뉴가 닫히지 않고, 한 번 더 누르면 닫힌다). 보이는 단위로 친다(퍼센트는 80, 크로스헤어 점 크기는 px). 범위 밖 숫자는 끝 값이 된다. 프레임 제한 줄 끝 칸에는 목록에 없는 값도 넣는다(20~1000, 0은 무제한). 위젯은 `SettingsWidgets.NumberBox`, 줄은 `SettingsRows.Slider`·`NumberChoice`. 치는 동안은 그 티가 난다(사용자 11-12: "숫자 입력 중인지 잘 모르겠음"): 칸이 검게 바뀌고 분홍 테두리와 굵은 분홍 밑줄이 서며, 숫자는 흰 굵은 글씨로 분홍 선택 위에 보이고 그 뒤에 분홍 커서가 깜박이고, 그 줄 이름이 분홍이 된다. 끝나면 바로 원래대로 돌아가고 남는 효과는 없다([TypingLook](../Scripts/UI/Settings/TypingLook.cs)). 숫자는 오른쪽 정렬이라 TMP가 마지막 글자 바로 뒤에 그리는 커서가 칸의 마스크 밖으로 나가 잘려 있었다: 글자 칸 오른쪽을 6px 비워 커서가 보인다. 크로스헤어 색의 #RRGGBB 칸도 같은 모양이다.
- **키 설정**: 조작 탭 맨 위 "키 설정" 줄의 "모든 키 바꾸기"가 따로 창을 연다([KeyBindingsView](../Scripts/UI/Settings/KeyBindingsView.cs), 사용자 11-12: "키 설정 창을 아예 따로 만들었으면 좋겠음. 그래서 키들 싹 다 커스텀할 수 있게끔"). 게임이 읽는 키가 모두 있다: 이동·파쿠르(앞·뒤·왼쪽·오른쪽, 달리기, 점프, 앉기, 다이브, 회피, 기울이기, 상호작용), 전투(사격, 조준·막기, 재장전, 밀치기, 사격 모드, 총 살펴보기, 라이트, 총 꾸미기, 돌 던지기, 벽 두드리기), 무기 칸·소지품(1~4번 칸, 돈 떨구기), 클래스·콜아웃(클래스 기술, 능력 카드·궁극, 콜아웃 휠), 화면·채팅(목표·점수판, 채팅 열기, 채팅 채널, 3인칭 보기, 개발자 콘솔). 줄마다 주 키와 보조 키 두 칸이고 어느 쪽 키로도 된다(회피 기본은 왼쪽·오른쪽 Alt, 1~4번 칸의 보조는 숫자패드, 채팅 열기의 보조는 숫자패드 Enter, 나머지 보조 칸은 비어 있다). 칸을 누르면 분홍으로 바뀌고 다음 키나 마우스 버튼(왼쪽·오른쪽·휠·옆 버튼)을 받는다. Esc는 그대로 두고, 우클릭은 칸을 비운다. 다른 줄이 쓰던 키를 넣으면 두 줄의 키를 맞바꾸고(그 줄은 이 칸의 예전 키를 받는다. 예전 키가 없던 칸이면 그 줄은 키를 잃는다) 아래 줄에 분홍으로 알려 주며, 그 칸이 잠깐 번쩍인다. 같은 줄의 다른 칸에 이미 있는 키는 아무것도 바꾸지 않는다. 왼쪽 Shift와 Shift(양쪽)처럼 같은 키를 품는 것도 겹침이다. 채팅 채널 키는 채팅 칸에 쓰는 동안만 읽어서 다른 줄과 겹쳐도 된다(기본 Tab = 점수판과 같다). 기본 키와 다른 칸은 왼쪽에 분홍 막대가 서고 그 줄 끝에 "기본" 버튼이 생긴다. "기본"은 그 줄만, "모두 기본값"(3초 안에 두 번 누른다)은 모든 키를 되돌린다. 조작 탭의 "이 탭 기본값"은 키를 건드리지 않는다. 바꾸는 즉시 settings.json의 `keyBindings`(Input System의 바인딩 덮어쓰기 JSON, 바인딩 id로 찾는다)에 저장된다. 기본 키는 [Player.inputactions](../Input/Player.inputactions)에 있고(엑셀 표가 아니라 유니티의 Input Actions 편집기에서 고친다), 보조 칸은 그 에셋에 경로가 빈 바인딩으로, 이동의 보조 칸은 비어 있는 두 번째 WASD 묶음으로 들어 있다.
- **키를 읽는 코드**: 모두 액션을 거친다([KeyBindings](../Scripts/Settings/KeyBindings.cs)). `KeyBindings.Pressed`/`Held("Player/Throw")`는 상점·능력 카드·총 꾸미기처럼 메뉴가 액션 맵을 꺼 둔 동안에도 그 키의 컨트롤을 직접 읽어, 예전의 직접 키 읽기와 같은 때에 같은 답을 준다. 자기 맵 사본을 쓰는 곳(PlayerWeaponInput, PlayerSpectate)은 `KeyBindings.Copy`로 만들어 나중에 바꾼 키도 따라간다. 화면 글자의 "[F] 문 열기", "[좌클릭] 던지기", "[WASD] 이동"처럼 대괄호 안의 기본 키는 플레이어가 고른 키로 읽힌다(`KeyBindings.Relabel`, Texts.Get이 부른다. 기본 키 글자와 액션의 짝은 `KeyBindings.Labels`). 줍기 안내의 키 칸, 처형 안내, 능력 카드 안내, 사다리·파쿠르 안내, 목표 바의 TAB 칸도 고른 키를 보여 준다.
- **바꿀 수 없는 키**(창 맨 아래에도 적혀 있다): Esc(메뉴·닫기·키 받기 취소), 마우스 움직임(시점), 휠 굴리기(목록), 콜아웃 휠 안의 좌클릭(말하기)·우클릭(닫기), 능력 카드 고르기 1·2·3, 메뉴의 Enter(출격·방 참가), 결과 화면 넘기기(Space·Enter·좌클릭), 채팅·콘솔 안의 Enter·위아래 화살표, 콘솔 자동 완성 Tab. 글자 키를 개발자 콘솔이나 채팅 채널에 쓰면 그 창에 글을 칠 때도 그 키가 먹힌다.
- **새 키 더하기**: Player.inputactions에 Button 액션과 기본 키, 경로가 빈 두 번째 바인딩을 넣고, `KeyBindings.Entries`에 줄을, Texts.csv에 `settings.keys.<id>` 이름을 더한 뒤, 코드는 `KeyBindings.Pressed("맵/액션")`으로 읽는다. 화면 글자에 대괄호로 쓸 키면 `KeyBindings.Labels`에 기본 키 글자를 더한다.
- **알트탭**: 빌드에서 게임이 포커스를 다시 받으면 0.5초 뒤와 2초 뒤에 두 번, 창이 마지막 변경 뒤 자리 잡은 모습(화면 모드·크기, 전체 화면이면 모니터까지)과 같은지 보고 다르면 설정대로 다시 적용한다([DisplayGuard](../Scripts/Settings/DisplayGuard.cs), `DisplaySettings.Restore`). 요청한 값이 아니라 실제로 받은 값과 비교하므로, 모니터가 못 하는 크기를 계속 다시 청하지 않는다. 커서를 가두고 있었다면 바로 다시 가둔다. 창 모드에서는 플레이어가 창을 다른 모니터로 끌어 옮길 수 있으니 모니터는 되돌리지 않는다. Alt+Enter 전환은 Player Settings(Allow Fullscreen Switch)에서 껐다. 화면 모드는 설정 메뉴에서만 바뀌고, 그 밖의 변화는 되돌릴 대상이다. 포커스가 바뀔 때마다 Player.log(`%USERPROFILE%\AppData\LocalLow\AlaskanGyaru\ENFILADE\Player.log`)에 `[Display]` 줄이 하나씩 남는다.
- **저장 위치**: 사용자별 `Application.persistentDataPath/settings.json`이다. Windows에서는 보통 `%USERPROFILE%\AppData\LocalLow\<회사>\<제품>\settings.json`이다. 파일을 지우면 기본값으로 돌아간다. 밸런스 숫자는 CSV 표, 플레이어 취향은 이 파일에 둔다.
- **기본값**: 처음 하는 플레이어의 설정과 "이 탭 기본값"이 돌아가는 값은 [SettingsDefaults.csv](../Resources/Tables/SettingsDefaults.csv)에서 바꾼다(아래 **설정 기본값·화면 글자 고치는 곳**). 층은 `GameSettings` 코드 값 → [DefaultSettings.asset](../Resources/Settings/DefaultSettings.asset)(Inspector에서 고친 적이 있을 때만) → 표 순서이고 뒤가 이긴다([GameSettingsPreset](../Scripts/Settings/GameSettingsPreset.cs)). 플레이어가 고른 값은 settings.json이 이긴다. settings.json은 모든 설정을 적어 두므로, 이미 저장한 PC에서는 새로 생긴 설정만 기본값을 따른다.
- **앉기 전환**: 한 번 누르면 앉고, 다시 누르면 선다. Shift나 점프를 누르거나, 다이브·포복·파쿠르로 앉기·슬라이드 상태를 벗어나면 자동으로 풀린다. 키를 놓은 것과 같다. 그래서 전환 모드의 점프는 서서 하는 점프이고, 몸을 접는 앉아 점프는 Ctrl을 누른 채 점프할 때만 나온다. 점프 중에 누르면 공중에서 몸을 접고, 앉은 채로 착지한다. 달리며 누르면 슬라이드하고, 끝나면 앉은 채로 남는다. 전환 모드에서 슬라이드 감속은 S 키로 한다.
- **기울이기 전환**: Q나 E를 한 번 누르면 그쪽으로 기울고, 같은 키를 다시 누르면 돌아온다. 반대 키를 누르면 반대로 기운다. Shift를 누르거나 서기·앉기 외의 상태가 되면 풀린다.
- **일시정지**: 싱글 플레이 기준이다. 멀티 플레이로 바꿀 때는 메뉴만 열고 게임은 멈추지 않게 한다(`SettingsMenu.Open`의 `GamePause.Set(true)`). [GamePause](../Scripts/Settings/GamePause.cs)만 게임 속도를 0으로 만든다. 멈출 때의 속도(반격 슬로 포함)를 그대로 되돌리므로, `CombatTimeEffects`의 슬로는 메뉴를 닫은 뒤 이어진다. UI 채널 소리만 일시정지 중에도 재생된다.
- 메뉴는 코드로 만든 화면이다([UI/Settings](../Scripts/UI/Settings)). 항목은 `SettingsMenuView`, 모양은 `SettingsWidgets`에서 바꾼다. 탭 순서는 `SettingsGroup` 순서와 같아야 "이 탭 기본값"이 맞는 묶음을 돌린다. EventSystem이 없는 씬에서는 메뉴를 처음 열 때 하나 만든다.

### 설정 기본값·화면 글자 고치는 곳

- **기본값**: [SettingsDefaults.csv](../Resources/Tables/SettingsDefaults.csv)를 엑셀로 연다. `Default` 한 줄에 설정마다 한 칸이고, 둘째 줄 `#설명`에 어느 탭의 무엇인지와 값의 범위가 적혀 있다. 고친 뒤 CSV UTF-8로 저장한다(편집기에서는 `Tools → Data Tables`의 다시 읽기나 다음 플레이에 반영). 빈 칸은 코드 값을 쓴다. 음량·화면 흔들림·HUD 흔들림은 0~1(0.8이 80%), 켬/끔은 true/false, 화면 모드는 Fullscreen·Borderless·Windowed, 데미지 숫자는 Off·Stacked·Each, 프레임 제한 0은 무제한이다. `gunCrosshairCode`·`meleeCrosshairCode`에 크로스헤어 공유 코드(ENF-…)를 넣으면 새 플레이어가 그 직접 꾸민 크로스헤어로 시작한다(비우면 기본 모양). 표에 없는 설정도 `GameSettings`의 필드 이름으로 열을 더하면 읽는다.
- **내 PC에서 확인**: 이미 저장된 settings.json이 이기므로, 바꾼 기본값은 그 탭의 "이 탭 기본값"을 누르거나 `%USERPROFILE%\AppData\LocalLow\AlaskanGyaru\ENFILADE\settings.json`을 지운 뒤에 보인다.
- **화면 글자**: [Texts.csv](../Resources/Tables/Texts.csv)의 `ko` 열을 고친다. `id`는 코드가 찾는 이름이라 그대로 두고, `#어디` 열에 화면 어디에 나오는지 적혀 있다. 설정 창은 `settings.`(탭·줄·선택지·안내), 크로스헤어 탭은 `crosshair.`로 시작한다. `{0}`은 코드가 채우는 자리라 남겨 둔다. 설정 창은 열 때마다 새로 만들므로 저장하고 다시 열면 바뀐 글자가 보인다.

## 개발자 콘솔 (` 키)

숫자 1 왼쪽의 **`** 키(키 설정 창의 "개발자 콘솔" 줄에서 바꾼다)로 화면 위쪽에 콘솔을 내리고 올린다([DevConsole](../Scripts/UI/Chatting/DevConsole.cs)). 메인 메뉴를 포함한 어느 씬에서나 열리고, 채팅이나 다른 글 입력 칸에 쓰는 중이면 열리지 않는다.

- **로그**: 게임을 켠 때부터의 Unity 로그·경고·오류가 쌓인다(최근 600줄, 화면에는 200줄). 같은 줄이 이어지면 `×횟수`로 묶고, 오류에는 스택의 첫 두 줄을 붙인다. `log all | warn | error | off`로 보일 로그를 거른다. 명령과 결과는 늘 보인다.
- **명령**: 채팅 콘솔 채널과 같은 명령([GameCommands](../Scripts/UI/Chatting/GameCommands.cs))을 `/` 없이 쓴다. 이름을 치는 동안 맞는 명령과 설명이 아래에 뜨고, Tab이 고른 것을 채운다. 위/아래는 목록이 떠 있으면 그 안에서 고르고, 빈 줄에서는 전에 친 명령을 불러온다. `clear`는 콘솔 기록을, `close`는 콘솔을 닫는다.
- 열려 있는 동안 커서가 풀리고 플레이어 조작이 꺼지지만 게임은 멈추지 않는다(방에서도 같게). Esc나 `로 닫는다. 새 명령은 `GameCommands.Register`에 넣으면 채팅과 콘솔 둘 다 쓴다.
- **허브와 결과**: `hub`는 허브로 간다(혼자, 또는 방장이면 방 전체). `results`는 마지막으로 마친 장의 결과 연출을 다시 보여 준다(허브에서는 무대에서, 다른 곳에서는 마지막 한 장만).
- **소리 미리 듣기**: `sound <묶음>/<키>`(예: `sound Zombie/Bone/NeckSnap`)는 그 폴더의 소리 하나를, `sound cue <큐>`(예: `sound cue Execution/Behind`)는 SoundCues.csv 한 줄을 시간차까지 그대로, `sound list <묶음>`은 묶음의 키 목록을 보여 준다. Sounds.csv의 볼륨·피치를 따른다([SoundPreview](../Scripts/Audio/SoundPreview.cs)). 처형·팔다리 절단처럼 상황을 만들기 어려운 소리를 넣거나 바꾼 뒤 바로 들어 본다.

## 사운드를 추가하거나 교체하기

기준 폴더는 [Art/Audio/Combat](../Art/Audio/Combat)이다. 클립을 폴더에 넣은 뒤 [Feedback.asset](../Resources/Combat/Feedback.asset)의 해당 배열에도 연결한다. 폴더 안의 모든 파일이 자동으로 재생 목록에 들어가는 구조는 아니다.

| 폴더 | 재생 상황 | Feedback 설정 |
| --- | --- | --- |
| `BOOM/Swing` | 주먹을 휘두를 때 | `Punch Swing Sounds` |
| `BOOM/Hit` | 내 주먹이 적에게 피해를 줬을 때 | `Punch Hit Sounds` |
| `BOOM/HeavyHit` | 충전 주먹·반격·가드 브레이크 타격 | `Punch Heavy Hit Sounds` |
| `Player/Attack/Punch/Wall` | 주먹이 벽·지형에 닿았을 때 | `Punch Wall Sounds`, `Punch Wall Volume` |
| `BOOM/Hurt` | 플레이어가 낙하 이외의 피해를 받았을 때 | `Player Damage Sounds` |
| `BOOM/Shove` | 밀치기·슬라이딩의 적 접촉 | `Shove Enemy Sounds` |
| `Shove` | 밀치기의 월드 접촉 | `Shove World Sound` |
| `BOOM/Kill` | 주먹·밀치기·처형 처치 | `Close Kill Sounds` |
| `BOOM/Fall` | 플레이어 낙하 피해, 적이 쓰러질 때 | `Tackle Sounds`, `Knockdown Sounds` |
| `BOOM/Grab` | 가드 브레이크로 다가갈 때, 처형 시작 | `Grab Sounds` |
| `BOOM/Block` | 가드 성공 | `Block Sounds` |
| `BOOM/Parry`, `BOOM/ParryBreak` | 주먹 패링, 체간을 무너뜨린 패링 | `Parry Sounds`, `Parry Break Sounds` |
| `Hitfullness` | 총기 명중·처치 등에 겹치는 타격 레이어 | `Impact Bullet Sounds` 등 각 레이어 |
| `Defense/Parry` | 금속 패링음. 카타나용 보관 클립 | 현재 연결하지 않음 |
| `Defense/ReservedStrongParries` | 향후 강공격용 보관 클립 | 현재 연결하지 않음 |
| `Player/Attack/Punch/Hit`·`Swing`, `Player/Damage/Hit`, `Defense/Block` | BOOM으로 바꾸기 전의 클립 | 현재 연결하지 않음 |

`BOOM` 폴더와 돌진 밀치기의 `Movement/PushHard`는 유료 BOOM Library Violent Combat의 소리를 타격 지점부터 잘라 낸 것이다. 한 번에 두 번 맞는 듯한 클립은 뺐고, 폴더 안의 음량은 약 3dB 안으로 맞췄다. 회피의 `Movement/Dodge`(`Wind` 바람은 WHOOSH Dash, `Cloth` 옷은 Kimono, `Perfect` 완벽 회피는 Phantom Wind)는 같은 팩의 WHOOSH 파일에 든 변형 6개씩을 하나씩 잘라 낸 것이고, 바람과 옷은 앞쪽에 천천히 부풀어 오르는 부분(0.1~0.2초)을 잘라 0.14초짜리 회피 안에 가장 커지게 했다(가장 큰 곳의 −12dB 지점 20ms 앞에서 자른다), SoundCues.csv의 `Dodge`·`DodgePerfect`가 낸다. 1인 라이선스이고 음원 공유 형태의 재배포가 금지되므로, 저장소는 비공개로 유지한다. `Resources/Combat/Parry`의 `ParryFlare`는 [Kenney Particle Pack](https://kenney.nl/assets/particle-pack)이고 CC0다.

패링 이펙트는 유료 Vefects Combat Flipbook VFX URP로 만든다. `Assets/Vefects` 원본은 고치지 않고, `Prefabs/Effects/Parry`의 프리팹과 `Art/Materials/Parry`의 머티리얼을 복사본으로 둔다. 색은 이 머티리얼의 `R`(밝은 몸통), `G`(어두운 속살), `B`, `Outline`에서 고친다. 복사본은 팩의 텍스처와 셰이더를 쓰므로 `Assets/Vefects`의 `VFX/Hit/Textures`와 `Shared`는 지우면 안 된다. 원본 프리팹에는 반복 재생되는 `AudioSource`가 붙어 있어서 복사본에서 뺐고, `ParryVfx`도 슬롯에 넣은 프리팹의 소리를 끈다. 주황 별인 `VFX_Parry_Sparkle`은 Cartoon FX Remaster의 `CFXR3 Hit Light B (Air)`에서 고리 두 층을 뺀 복사본이다. 머티리얼과 스크립트는 팩의 것을 쓰므로 `Assets/JMO Assets`도 지우면 안 된다.

기존 클립을 이동하거나 이름을 바꿀 때는 `.meta`도 함께 이동해 GUID를 유지한다. 새 클립은 Unity 임포트가 끝난 뒤 배열에 추가하며, `Missing` 항목이나 남은 옛 경로의 `.meta`가 없는지 확인한다. 각 배열은 직전에 재생한 클립을 피해서 고르므로 원하는 변형들을 같은 배열에 넣으면 된다.

### 이동·목소리 소리 (폴더가 곧 목록)

이동과 목소리 소리는 배열에 연결하지 않는다. **폴더에 넣으면 자동으로 들어간다.** 폴더 구조가 소리 이름이고, 파일을 추가·이동·삭제하면 편집기가 `Resources/Audio/<묶음>` 소리 묶음(SoundBank)을 다시 만든다([SoundBankBuilder](../Editor/Import/SoundBankBuilder.cs)).

| 폴더 | 묶음 | 내용 |
| --- | --- | --- |
| [Art/Audio/Movement](../Art/Audio/Movement) | `Movement` | `<행동>/<표면>/`: `Run`·`Walk`·`Sneak`·`Release`(발 떼기)·`Land`·`Slide`·`SlideLoop`·`WallStep`·`HandSoft`·`HandHard`·`HandRelease`·`Ringout` 아래에 `Concrete`·`Metal`·`Duct`·`Wood`·`Glass`… 표면이 없는 소리는 `<행동>/` 또는 `<행동>/<종류>/`: `Cloth/VaultSwish`, `Roll/Start`, `Ladder/Step`, `Bar/Grab`, `Fall/Heartbeat`, `LandHard`, `Push`, `Prone/Down`, `Lean`… |
| [Art/Audio/Voice/Hiro](../Art/Audio/Voice/Hiro) | `Voice` | 숨소리 `Breath/SoftShortIn`…, 힘쓰는 소리 `StrainSoft/Medium/Hard`, 충격음 `ImpactSoft/Impact/ImpactHard`, 콜아웃 대사 `Callout/<줄 id>`([Callouts.csv](../Resources/Tables/Callouts.csv)의 줄마다 한 폴더. 지금은 Windows TTS로 만든 임시 목소리 `<id>_tts.wav`이고, 녹음 파일을 그 폴더에 넣고 `_tts.wav`를 지우면 그 목소리가 된다. 대사를 고친 뒤 `python Tools/Audio/callout_voices.py`를 돌리면 녹음이 없는 폴더의 TTS만 다시 만든다). 캐릭터 목소리를 바꾸면 이 폴더 내용만 갈아 끼운다 |
| [Art/Audio/HitConfirm](../Art/Audio/HitConfirm) | `HitConfirm` | 히트마커와 함께 나는 확인음. `Body`(명중마다), `Head`(헤드샷 때 겹침), `Kill`(처치), `Break`(체간 붕괴). 폴더 안 파일 중 하나를 무작위로 고른다. `HeadAVA`는 쓰이지 않는 헤드샷음 후보(A.V.A, 워크숍 2981494447)라 `Head`와 폴더 이름을 맞바꾸면 그 소리로 바뀐다 |
| [Art/Audio/Guns](../Art/Audio/Guns) | `Guns` | 총소리. `<총>/<소리>`이고 총은 무기 데이터의 Sound Profile(`Pistol`·`Rifle`·`Heavy`), 소리는 `Shot`·`MagOut`·`MagIn`·`SlideBack`·`SlideForward`·`Bolt`·`DryFire`·`LowAmmo`·`LastRound`·`DrawBack`·`DrawForward`다. 총 이름 없이 둔 `DryFire`는 그 소리가 없는 총이 같이 쓴다. 잔탄 경고는 세 겹이다. 탄창이 3분의 1 아래로 남은 발마다 그 총의 `LowAmmo`가 빌수록 커지며 나고, 탄창을 비우는 발에는 그 대신 `LastRound`가 난다(TFA Base 규칙, WeaponFeedback.AmmoCue). 모든 총이 같이 쓰는 딸깍 `LowAmmoClick`은 SoundCues.csv의 `GunLowAmmoClick/<총>` 행이 CS2처럼 총소리 0.05초 뒤에 클립의 2.5배로 낸다(표의 볼륨이 1을 넘으면 FeelAudio가 클립보다 크게 낸다. 최대 4). 장전 끝의 노리쇠는 SoundCues.csv의 `GunBolt/<총>` 행이 당기고 놓는 소리를 시간차로 낸다. 총을 꺼낼 때는 `GunDraw/<총>` 행이 같은 식으로 낸다(지금은 소총만: `DrawBack`, 0.3초 뒤 `DrawForward`. 행이 없는 권총·샷건은 꺼낼 때 소리가 없다. WeaponFeedback.Drawn). KINEMATION 총(FPSBody)을 들었을 때는 장전 소리를 그 장전 동작에서 잰 순간에 낸다. 탄창이 빠지기 시작할 때 `MagOut`, 끼워질 때 `MagIn`, 노리쇠나 슬라이드가 걸쇠에서 풀려 앞으로 갈 때 `SlideForward`다(당겼다 놓는 동작이면 `SlideBack`도 낸다). 부딪는 소리는 클립에서 가장 큰 순간이 동작의 순간에 오도록 일찍 시작한다([ReloadMoments](../Scripts/Audio/ReloadMoments.cs)). 무기 데이터나 GunFeel의 소리 칸에 클립을 넣으면 그쪽이 먼저고, 둘 다 비면 합성음이다([FeelAudio](../Scripts/Combat/Feel/FeelAudio.cs)). `Reverb/<총>/<방>`은 쏜 자리의 방에 맞춰 겹치는 잔향 꼬리다([GunReverb](../Scripts/Audio/GunReverb.cs), 수치는 Reverb.csv). `Heavy`의 `MagOut`·`MagIn`·`SlideBack`·`SlideForward`는 탄창을 쓰는 Heavy 총(SRM-12·Mk14 EBR·MX68)의 소리다(L4D1 Weapon Audio의 군용 저격총 소리. 한 발씩 넣는 헤링턴은 무기 데이터의 제 소리를 쓴다) |
| [Art/Audio/UI](../Art/Audio/UI) | `UI` | 메뉴 소리([KitSound](../Scripts/UI/Kit/KitSound.cs)). `Buy`(상점에서 샀을 때), `Refuse`(살 수 없을 때), `Alert`(동료가 쓰러지거나 죽었다는 알림), 주울 때의 `PickAmmo`·`PickHeal`·`PickShield`·`PickMoney`·`PickGun`, 장을 탈출했을 때의 `Escape` 폴더에 파일을 넣으면 그 파일을 쓰고, 폴더가 없으면 코드로 만든 소리가 난다. 폴더 바로 아래에 둔 파일(`MissionObjective.wav`)은 묶음에 들어가지 않는다. 줍기 소리 중 `PickAmmo`(RE4)·`PickGun`(MWII)·`PickHeal`(L4D1)·`PickShield`(MWII 레이저 켜짐)는 워크숍 소리가 들어 있고 `PickMoney`는 아직 합성음이다 |
| [Art/Audio/Enemies/Zombie](../Art/Audio/Enemies/Zombie) | `Zombie` | 좀비 음성([ZombieVoice](../Scripts/Enemies/ZombieVoice.cs)). `Attack`(공격을 시작할 때의 으르렁), `Leap`(러너가 도약할 때의 비명). 그 아래 `Male`·`Female` 폴더는 이름에 그 말이 든 몸이 쓴다(Synty 좀비 프리팹 이름). 성별 폴더가 없으면 바로 아래 파일을, 그것도 없으면 있는 쪽을 쓴다. 얼마나 자주, 얼마나 멀리 나는지는 [ZombieVoice.csv](../Resources/Tables/ZombieVoice.csv). 몸이 부서지는 소리도 이 묶음이다([BodyBreakSounds](../Scripts/Audio/BodyBreakSounds.cs)): `Bone/NeckSnap`·`SkullBreak`·`ArmBreak`(처형), `Gore/HeadBurst`·`BloodSpray`(머리 터짐, 산 좀비의 팔다리 절단), `Hit/Fist`(주먹이 좀비에 맞을 때의 살 소리). 무엇을 언제 겹치는지는 SoundCues.csv의 `Execution/…`·`Gore/…`·`ZombieHit/Fist` |
| [Art/Audio/Library](../Art/Audio/Library) | 없음 | 아직 쓰지 않는 보관 소리(Beatrun 나머지, FEAR 손전등·문, 투척 칼, 잠입, 그리고 2026-11-11 워크숍 이식의 `<애드온>_<워크숍 번호>` 폴더들: 근접 무기·칼·쌍권총·총 소리 팩). 워크숍 보관본은 원래 경로를 지킨 OGG(Vorbis q6)라 원본 WAV의 약 7분의 1 크기다. 어디에서도 참조하지 않으므로 빌드 크기에 들어가지 않는다. 쓰려면 위 폴더로 옮긴다 |

- **볼륨·피치**: [Sounds.csv](../Resources/Tables/Sounds.csv)의 `<묶음>/<폴더>` 행(예: `Movement/Walk/Concrete`). Beatrun 원본 값으로 채워져 있다. 행이 없으면 볼륨 1, 피치 1이다.
- **가져오기 설정**: 클립마다 Preload Audio Data와 Load In Background를 켠다. 맵을 불러올 때 [CombatWarmup](../Scripts/Combat/Feel/CombatWarmup.cs)이 묶음을 불러오면 그 데이터를 뒤에서 미리 읽어 둔다. 꺼 두면 처음 재생할 때 메인 스레드에서 읽느라 프레임이 7~89ms 멈췄다. 2026-11-06에 꺼져 있던 1,634개를 켰고, 메모리는 약 79MB 더 쓴다. 새 클립도 둘 다 켠다.
- **도플러**: 쓰지 않는다. [GameAudio.Bind](../Scripts/Audio/GameAudio.cs)가 모든 소리에서 끈다. 유니티는 소리의 속도를 게임 시간으로 재므로, 슬로 모션 중에 실제 시간으로 움직이는 것(완벽 회피 중 적을 지나가는 회피)은 엄청나게 빠르다고 보고 둘레 3D 소리의 높이를 튀게 했다.
- **표면**: 표면은 발소리, 충돌 이펙트, 총알이 뚫고 나가는 정도(`penetration`)를 정한다. 물체에 [SurfaceType](../Scripts/World/SurfaceType.cs) 컴포넌트를 붙이고 Inspector의 목록에서 고르면 그 값을 쓴다. 목록은 Surfaces.csv의 줄과 설명(`#note`)이라, 표에 줄을 더하면 바로 목록에 생긴다([SurfaceNameDrawer](../Editor/Data/SurfaceNameDrawer.cs)). 없으면 [Surfaces.csv](../Resources/Tables/Surfaces.csv)의 키워드를 물리 재질·렌더 재질·오브젝트 이름에서 찾는다(가장 긴 키워드가 이김). 아무것도 맞지 않으면 `Concrete`다.
- **표면이 없는 소리**: 표마다 `parent`가 있어서 소리가 없는 표면은 부모로 넘어간다. `Duct` → `Metal` → `Concrete` 순서다. 예를 들어 `Grass`는 착지 소리만 따로 있고, 발소리는 `Dirt` → `Gravel` 소리를 쓴다.
- 출처: Beatrun(미러스 엣지 원본), Dynamic Landing(`LandHard`·`LandHeavy`·`Roll`·`Push`), FEAR-like SFX(`Land/Dirt·Grass·Stone·Tile·Carpet`, `Slide/Dirt·Sand`, `Prone`, `Ledge/Reach`, `Lean`, `Cloth/SprintRustle`). 공개 빌드 전에는 교체가 필요할 수 있다.
- **언제 무슨 소리가 나는지**: 코드는 큐 이름만 부르고, 그 큐가 무엇을 재생할지는 [SoundCues.csv](../Resources/Tables/SoundCues.csv)가 정한다. `sounds` 칸은 `;`로 나눈 `묶음/키` 목록이다. `{surface}`로 끝나면 발밑·장애물·렛지·벽의 표면 소리, `*0.5`는 볼륨, `+0.1`은 0.1초 뒤다(예: `Movement/HandHard/{surface}; Voice/StrainSoft +0.1`). `Vault/Gantry`처럼 표면 이름을 붙인 행이 있으면 그 표면에서는 그 행을 쓴다. `noise`는 적에게 들리는 거리(m)이고 [Noise](../Scripts/World/Noise.cs) 이벤트로 나간다(아직 듣는 적은 없다).
- **발소리**는 타이머가 아니라 몸의 발이 땅에 닿는 순간에 난다([PlayerFootsteps](../Scripts/Player/Audio/PlayerFootsteps.cs), GMod Anim-Based Footsteps 방식). 발마다 직전 접지 높이를 추적하며 새로 들어 올린 발만 다음 걸음으로 센다. 양발이 함께 올라가는 골반 움직임은 제외하고, 낮은 프레임 속도로 발의 최저점을 건너뛰어도 하강 후 반등을 인식한다. 가만히 서 있으면 양발의 기준을 각각 다시 잰다. 시험장은 [MovementAbilityPlayground](../Scenes/Development/MovementAbilityPlayground.unity) 시작 위치 뒤의 `Surface pads`(표면별 판 16개)이고, 디버그 HUD의 `Surface:`가 마지막 걸음의 표면이다.
- **이동 소리의 비중**: 일반 이동 속도 5.2m/s는 몸의 달리기 클립과 `StepRun`을 쓴다. 반복되는 옷 소리는 이 큐에서만 `*0.22`, 발 떼기 마찰음은 `StepRelease`에서 `*0.3`으로 줄여 발 디딤이 먼저 들리게 한다. 볼트·구르기·착지의 옷 소리는 각 동작 큐에서 따로 조절한다. 몸 소리가 설정된 동시 재생 수를 넘으면 가장 먼저 끝날 소리를 멈추고 새 소리로 교체한다.
- **넘기 뒤 발소리 복귀**: 볼트의 남은 애니메이션을 재생하는 동안만 발 접지를 잠시 읽지 않는다. 마무리가 끝나거나 다른 자세로 중단되면 `playingOut`을 해제해 보행 접지와 발소리가 다시 이어진다.

**볼륨 채널**: 모든 소리는 `전투`·`이동`·`목소리`·`UI` 채널 중 하나에 속하고, Esc 메뉴의 볼륨을 따른다. 새 `AudioSource`를 만들면 [GameAudio](../Scripts/Audio/GameAudio.cs)`.Bind(source, AudioChannel.…)`로 한 번 연결한다. 소리마다 크기를 바꿀 때는 `source.volume`을 쓰지 않고 `PlayOneShot(clip, 볼륨)`의 볼륨 값을 쓴다. 커졌다 작아지는 반복 소리(슬라이드 긁는 소리)는 `GameAudio.SetGain`으로 크기를 정한다. 월드 한 지점에서 한 번 나는 소리는 `AudioSource.PlayClipAtPoint` 대신 `GameAudio.PlayAt`을 쓴다. 전체 볼륨은 `AudioListener.volume`이다.

### 구역 리버브 (맵마다 놓기)

방·복도·터널처럼 소리가 울리는 곳에는 [ReverbArea](../Scripts/Audio/ReverbArea.cs)를 놓는다. 내 귀(카메라)가 상자 안에 있는 동안 들리는 소리(발소리·숨·적·재장전·도탄)에 Unity의 리버브 프리셋이 걸린다. 총소리와 그 잔향 꼬리, 히트마커·처치 확인음, UI에는 걸리지 않는다. 총은 [GunReverb](../Scripts/Audio/GunReverb.cs)가 쏜 자리의 방을 재서 따로 울린다. 상자 밖에는 리버브가 없다.

1. Hierarchy에서 `GameObject → Audio → Reverb Area`를 고른다. 씬 화면 가운데에 파란 상자가 생긴다.
2. 오브젝트를 옮기고 돌려 공간에 맞춘 뒤, 씬 화면에서 상자 면의 점을 끌어 크기를 맞춘다. Inspector의 `Center`·`Size`로도 된다.
3. `Preset`을 고른다. 작은 방 `Room`, 타일 방 `Bathroom`, 돌로 된 방 `Stoneroom`, 복도 `Hallway`, 돌 복도 `StoneCorridor`, 하수관 `SewerPipe`, 동굴 `Cave`, 큰 홀 `Hangar`·`Arena`, 주차장 `ParkingLot`, 골목 `Alley`, 도시 거리 `City`, 들판 `Plain`. `Off`는 큰 구역 안의 한 곳(작은 부스 등)을 울리지 않게 한다.
4. 상자가 겹치면 `Priority`가 높은 쪽, 같으면 작은 상자가 이긴다. 그래서 맵 전체를 덮는 큰 상자(`City` 등)를 깔고 그 안에 방 상자를 놓으면 방 안에서는 방이 이긴다.
5. `Strength`를 낮추면 프리셋이 덜 들린다. 구역을 드나들 때 바뀌는 시간은 [Reverb.csv](../Resources/Tables/Reverb.csv)의 `Area.fadeTime`이다.
6. 씬을 저장한다.

## 3D 모델 추가하기

모델 하나당 폴더 하나를 [Art/Models](../Art/Models) 아래 분류 폴더에 만든다. 이름에는 공백 대신 PascalCase를 쓴다.

```text
Art/Models/
  Characters/  Weapons/  Props/  Environment/
    Props/TetoPear/
      TetoPear.fbx
      Textures/tetopear.png      ← 머티리얼 이름(tetopear)과 같은 이름
```

폴더에 넣기만 하면 [ModelImportRules](../Editor/Import/ModelImportRules.cs)가 처음 임포트할 때 다음을 맞춘다.

| 항목 | 처리 |
| --- | --- |
| 루트 | 씬에 놓은 오브젝트의 루트는 회전 0·크기 1. Blender의 -90°·×100은 안쪽 자식에 남는다 |
| 축·불필요 요소 | 축 변환을 메시에 굽고, 카메라·조명은 가져오지 않는다 |
| `Props`·`Environment` | 리그·애니메이션을 끈다. `Characters`·`Weapons`는 기본값(Generic)을 유지한다 |
| 머티리얼 | URP Lit으로 만들고, 모델 옆이나 `Textures/`에서 이름이 맞는 텍스처를 자동 연결한다 |
| 텍스처 | 최대 2048. `_Normal`·`_N`은 노멀맵, `_Mask`·`_Metallic`·`_Roughness`·`_AO`·`_ORM`·`_Height`는 sRGB 끔 |

텍스처 이름 규칙: 머티리얼 이름 또는 모델 이름 뒤에 아무것도 없거나 `_BaseColor`·`_Albedo`·`_Diffuse`·`_Color`이면 기본 색, `_Normal`이면 노멀맵이다. 대소문자·공백·밑줄은 구분하지 않는다. 폴더에 텍스처가 하나뿐이면 이름과 상관없이 기본 색으로 쓴다. 텍스처를 나중에 넣어도 모델이 다시 임포트되어 연결된다.

- 규칙은 **처음 임포트할 때만** 적용되므로 이후 Inspector에서 바꾼 설정은 유지된다. 규칙을 다시 적용하려면 모델이나 폴더를 선택하고 `Tools → Models → Reapply Import Rules`를 누른다.
- 머티리얼은 모델 안에 포함되어 자동으로 갱신된다. 매끄러움 등을 직접 조정하려면 모델 Inspector → Materials → **Extract Materials**로 꺼낸다. 꺼낸 뒤에는 텍스처가 자동으로 바뀌지 않는다.
- 크기는 `Model → Scale Factor`로 조정한다. 플레이어 키는 약 1.75m다.
- 게임에 쓰는 오브젝트(충돌체·스크립트 포함)는 모델을 씬에 놓고 [Prefabs](../Prefabs) 아래 프리팹으로 만든다. 굴러다니는 물체는 아래 물리 프랍 도구를 쓴다.
- 원본 `.blend`는 `Assets` 밖에 둔다. `Assets` 안에 두면 Unity가 Blender를 실행해 변환하려고 한다. `.blend1`은 Blender 백업이므로 넣지 않는다. `.fbx`·`.png`·`.blend`는 Git LFS로 저장된다.
- Blender에서 내보낼 때 Apply Scalings `FBX All`, Forward `-Z`, Up `Y`, Apply Transform을 켜면 안쪽 자식의 회전·크기도 깔끔해진다.
- 워크숍 애드온의 Source 무기(MDL)는 [addon_weapon_to_fbx.py](../../../Tools/Blender/addon_weapon_to_fbx.py)와 무기마다의 레시피([Tools/Blender/Addons](../../../Tools/Blender/Addons))로 `Art/Models/Addons/Weapons/<이름>`에 바꿔 넣는다(손잡이 원점, +Z 칼끝·총구, 실제 크기, 부품과 표식. [애드온 이식 기록](AddonPortNotes.md#워크숍-모델-2026-11-11)). `blender -b --factory-startup --python Tools/Blender/addon_weapon_to_fbx.py -- <레시피.json> <workshop.py로 푼 애드온 폴더> <프로젝트 폴더>`

### 충돌과 물리 (물리 프랍)

모델을 Project 창에서 선택하고 우클릭 → `ENFILADE → Create Physics Prop Prefab`(또는 `Tools → Models → Create Physics Prop Prefab`)을 누르면 `Prefabs/<분류>/<이름>.prefab`이 만들어진다. 씬에는 이 프리팹을 놓는다.

| 구성 | 내용 |
| --- | --- |
| 루트 | `Rigidbody`(질량 1kg, 보간, 빠른 충돌 감지) + [PhysicsProp](../Scripts/World/PhysicsProp.cs) |
| 자식 | 모델 그대로. 메시마다 **convex `MeshCollider`** |
| 반응 | **질량이 결과를 정한다.** 모든 밀기는 운동량이라 같은 힘이면 가벼울수록 멀리 간다. 벽 타기·매달리기·넘기 대상에서는 빠진다 |

| 밀기 | 세기 (설정 위치) | 최고 속도 | 1kg 물체 | 20kg 물체 |
| --- | --- | --- | --- | --- |
| 걷기 | 300N으로 기댐 (`Player.csv`의 `Ability.propPushForce`) | 걷는 속도 | 바로 걷는 속도 | 서서히 밀림 |
| V 밀치기 | 60 N·s (`Ability.propShoveImpulse`) | 8m/s (`Ability.shovePushSpeed`) | 8m/s | 3m/s |
| 총알 1발 | 소총 3·권총 4 N·s ([Weapons.csv](../Resources/Tables/Weapons.csv) `propImpulse`) | 12m/s | 3·4m/s | 0.15·0.2m/s |
| 슬라이딩·다이브·돌진 밀치기로 부딪힘 | 몸무게 70kg과 함께 밀려 나감 (`Ability.propBodyMass`) | 플레이어 속도 | 플레이어 속도로 밀려 나가고 플레이어는 거의 그대로 | 둘 다 플레이어 속도의 78%로 진행 |

슬라이딩·다이브·돌진 밀치기로 부딪히면 물체와 플레이어가 한 속도(몸무게 ÷ 둘의 질량 합 × 플레이어 속도)로 함께 나아간다. 물체가 튕겨 나가며 더 빨라지지는 않는다. 그 속도가 원래의 30% 아래가 되는 물체(플레이어 몸무게의 약 2.3배, 160kg 이상)는 지금처럼 벽으로 취급해 튕기거나 멈춘다.

움직이는 물체에는 오목한 메시 충돌체를 쓸 수 없어서 볼록 껍질(convex, 최대 255면)로 감싼다. 배·공·상자처럼 거의 볼록한 모양은 이것으로 충분하다. 도넛·의자처럼 크게 오목한 모양은 자식 오브젝트에 Box·Sphere·Capsule 충돌체를 여러 개 나눠 붙이고 `MeshCollider`를 지운다. 움직이지 않는 벽·지형은 `Rigidbody` 없이 convex를 끈 `MeshCollider`를 쓰면 모양 그대로 충돌한다.

물체마다 바꿀 값은 루트 `Rigidbody → Mass` 하나다. 밀기 세기는 모든 물체에 공통이며 위 표의 위치에서 바꾼다. 프리팹이 이미 있으면 도구는 덮어쓰지 않는다.

## 화면 후처리

플레이어 카메라는 URP 후처리를 켠다([PlayerCameraEffects](../Scripts/Player/Camera/PlayerCameraEffects.cs)가 시작할 때 켬). 그래서 PC 파이프라인 볼륨 프로필 [GameLook](../Settings/Rendering/GameLook.asset)(톤매핑 끔, Bloom 0.25, 비네트 0.2)이 항상 적용된다. 톤매핑은 VRChat 기본 화면처럼 꺼 두어 캐릭터가 제작자가 정한 색 그대로 나온다. 처치 연출([KillConfirmEffect](../Scripts/Combat/Feel/KillConfirmEffect.cs))은 노출·대비·채도·색 필터·화이트 밸런스·블룸·비네트를, 저체력 화면([LowHealthGrade](../Scripts/Player/Combat/LowHealthGrade.cs))은 채도·색 필터를 절댓값으로 덮어쓴다. 저체력 화면이 켜져 있을 때 처치는 색을 돌려주지 않는다. 색이 돌아오면 회복한 것처럼 보이기 때문이다. 대신 번쩍임, 렌즈 펀치, 색 번짐, 대비, 블룸으로 처치를 보인다(GunFeel의 `Kill screen: ordinary kills at low health`). 그래서 이 값들을 프로필이나 맵 볼륨에서 올리면 처치할 때 화면이 오히려 어두워지거나 밋밋해진다. 밝기와 색감은 조명으로 맞춘다([조명 안내](LightingGuide.md)). [SampleSceneProfile](../Settings/Rendering/SampleSceneProfile.asset)은 모바일 설정만 쓴다. 큰 낙하 효과는 [Resources/Rendering/FallWarp](../Resources/Rendering/FallWarp.asset) 볼륨 프로필을 실행 중에 복사해 우선순위 100의 전역 볼륨으로 쓴다. 이 에셋이 프로젝트에 있어야 빌드에서 렌즈 왜곡·색수차 셰이더가 빠지지 않는다(URP의 쓰지 않는 후처리 변형 제거). 세기는 에셋이 아니라 `FallScreen` 행에서 바꾼다.

## 실행 중 생성되는 분류와 혈흔 풀

Play 중 효과가 필요하면 `EFFECTS → Audio / World / Screen`을 자동으로 만들고 소리·월드 효과·화면 표시를 모은다. 콘솔 소환 적은 `ENEMIES → Console`, 웨이브 적은 `ENEMIES → Waves`에 생성된다. 수동으로 빈 오브젝트를 미리 만들 필요는 없다. 스크립트 변경 후에는 Play를 종료하고 다시 시작해 생성 위치를 확인한다.

`EFFECTS → World → Impact VFX → Blood Decals (Pool)`의 회색 혈흔은 다음 타격에 재사용할 대기 오브젝트다. 풀은 **최대 32개**로 제한되며 무한히 증가하지 않는다. 비활성 혈흔은 표시하지 않고 수명 업데이트도 건너뛴다. 반복 생성·삭제 부하를 줄이기 위해 풀을 유지한다. 화면에 남는 시간은 [GunFeel.asset](../Resources/Combat/GunFeel.asset)의 `Blood Decal Seconds`로 조절한다. 이 시간이 지나면 1.2초 동안 흐려진 뒤 풀로 돌아간다. 혈흔 모양은 코드로 만든 얼룩 하나와 [Resources/Combat/BloodSplats](../Resources/Combat/BloodSplats)의 Cry of Fear 혈흔 9장 중에서 매번 다른 것을 고른다([BloodSplatTextures](../Scripts/Combat/Feel/BloodSplatTextures.cs)). 이 폴더의 텍스처는 흰 무늬 + 알파라서 색은 GunFeel의 `Blood Color`가 정하고, 같은 꼴의 텍스처를 넣으면 바로 섞인다([addon_blood_decals.py](../../../Tools/Blender/addon_blood_decals.py)가 회색 바탕 곱하기 데칼을 이 꼴로 바꾼다). 머리가 터진 뒤 벽, 떨어진 살점, 잘린 자리 웅덩이의 투영 데칼은 [GoreLibrary](../Resources/Combat/GoreLibrary.asset)의 `Splats` 13개(RVFX 4 + Cry of Fear 9, `Art/Materials/BloodDecals`) 중에서 고른다.

`EFFECTS → World → Impact Pool`은 DAVFX 이펙트를 미리 만들어 둔 대기열이다. 전투 중 생성이 없도록 맵을 불러올 때 [CombatWarmup](../Scripts/Combat/Feel/CombatWarmup.cs)에서 한 번 만들고, 복사본은 땅속 5000m에서 켜진 채 기다린다. VFX Graph는 처음 그릴 때 약 0.5초 멈추므로, 맵이 뜨는 동안 모든 복사본을 시야 끝에서 한 번씩 재생해 그 멈춤을 맵 시작으로 옮긴다. 첫 타격에 처음 만들어지는 시스템을 새로 넣으면 `CombatWarmup`에도 추가한다. 킬 화면, 낮은 체력, 슬로 모션이 쓰는 후처리 효과 조합도 플레이어 카메라가 생기자마자 눈에 안 보이는 세기로 한 프레임씩 그려 셰이더를 미리 만든다([PostProcessWarmup](../Scripts/Combat/Feel/PostProcessWarmup.cs)). 효과를 새로 섞는 화면 효과를 만들면 그 조합을 여기에 더한다. 손의 엄지 피부 보정이 쓰는 몸 메시 복사본도 첫 렛지가 아니라 몸을 만들 때 만들어 입혀 두고, 자세와 상관없는 계산(엄지 정점, 이음새, 이웃)도 그때 끝낸다(`HandSkinCorrection.CopyAhead`). 그래서 첫 매달리기 때는 엄지 둘레 정점만 계산한다. 2026-11-06 기록에서 첫 매달리기 프레임은 97 ms였고, 그중 66 ms가 이 보정이었다. 소리 묶음(`Resources/Audio`의 Movement, Voice, HitConfirm, Bullets)은 처음 쓰일 때 담긴 클립을 한꺼번에 불러오므로 `CombatWarmup`에서 미리 불러 둔다. 숨소리가 힘쓸 때만 나게 된 뒤로는 Voice의 첫 사용이 첫 회피나 반격이었다. 새 소리 묶음을 만들면 `CombatWarmup`의 `SoundBanks` 목록에 더한다. 어떤 프리셋을 어떤 크기와 밝기로 쓸지는 [ImpactEffects.csv](../Resources/Tables/ImpactEffects.csv)에서 고치며, Play 중에 저장해도 바로 반영된다. 복사본을 다 쓰면 가장 오래된 것을 다시 쓰므로, 끊기거나 이펙트가 튀어 보이면 `copies`를 늘린다. 표에 적는 프리셋은 [ImpactLibrary](../Resources/Combat/ImpactLibrary.asset)에 있어야 한다. 프리셋 생김새는 DAVFX의 `URP/Demo Scene`에서 볼 수 있다. 이펙트 재생에는 `com.unity.visualeffectgraph` 패키지가 필요하다.

벽 착탄은 맞은 표면의 행을 쓰고, 그 표면의 행이 없으면 [Surfaces.csv](../Resources/Tables/Surfaces.csv)의 부모를 따라 올라가 마지막에 `Concrete`를 쓴다. 풀의 복사본은 다음 착탄으로 옮겨 가므로, 팩 이펙트에 포함된 데칼과 빛 폭발(`VFXLightBurstCompanion`)은 끈다. 총알 구멍은 `EFFECTS → World → Bullet Holes`의 데칼 64개를 돌려 쓰며, PC_Renderer의 Decal 기능이 있어야 보인다. 구멍은 표면마다 크기(`holeSize`)가 다르고 맞을 때마다 `holeVary`만큼 크기가 흔들린다. 비스듬히 맞으면 총알이 지나간 쪽으로 1/cos(각도)배 길어지되 `holeStretch`에서 멈추고(금속 3, 나무 2.5, 콘크리트 2, 유리·타일 1 = 늘 둥글다), 맞은 점이 구멍의 앞쪽 끝이 된다([BulletHoles](../Scripts/Combat/Feel/BulletHoles.cs)). 약 25°까지는 둥글게 두고 무작위로 돌린다. 투영은 늘 벽 안쪽이다. 구멍 모양은 Hivemind 텍스처 6계열 24장이 전부이고 콘크리트·아스팔트·벽돌·석고·돌·대리석·나무·자갈 머티리얼이 모두 Concrete 계열 9장을 색만 바꿔 쓰므로, 색이 같은 머티리얼끼리 한 행에 섞어 모양 수를 늘렸다(콘크리트 = Concrete + Ceramic, 돌·나무 판 = Rocks + Plank, 석고·대리석 = DryWall + Marble). `hole`에 `none`을 적으면 그 표면은 구멍을 남기지 않는다(물). Decal은 Screen Space 방식이라 다른 머티리얼에 셰이더 변형을 더하지 않는다. `Prefabs/Effects/Weapons`의 `Impact_*` 프리팹과 `Art/Sprites/Weapons`의 `Impact_Wall_*`은 연결을 끊은 옛 착탄 자료다.

적의 분류와 웨이브 집계는 별개다. 웨이브 일시정지·중단은 해당 WaveManager가 추적하는 적에게만 적용하며 콘솔 적은 그대로 유지한다.
