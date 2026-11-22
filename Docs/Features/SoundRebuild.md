# 소리 다시 세우기 (설계)

[문서 목차](../README.md) · [소리 코드 폴더](../../Assets/_Project/Scripts/Audio/README.md) · [설정·유지보수 안내](../Guides/MaintenanceGuide.md) · 맵 기믹 · [좀비 엘리트](ZombieElites.md)

게임의 소리를 두 갈래로 다시 세우는 설계다. 하나는 **코드가 계산해 만드는 소리를 소리 파일로 바꾸는 일**이고, 다른 하나는 **소리를 내는 일을 한 곳(Sonity)이 맡게 하는 일**이다. 2026-11-20에 썼다.

사용자(2026-11-20): "아니 근데 저 소리 시스템이 더 우월한 거 아님? 애초에 소리를 코드로 만들면 안 되잖아." 이어서: "코드로 만든 소리 27곳을 무엇으로 바꿀지 목록으로 정리해 주시고요. 사운드 시스템을 Sonity로 리팩토링하는 게 나을까요? 필요하면 개조까지 해서요."

| 무엇 | 상태 |
| --- | --- |
| 코드로 만든 소리 조사 (2장, 5장) | 끝. 코드에서 읽었고, 소리는 귀로 듣지 않았다 |
| Sonity로 옮길지 (3장) | 옮긴다. 사용자가 네 가지에 답했다(2026-11-21, 4장): **숫자는 표에 남고**, 표와 폴더가 Sonity를 움직인다(3.7) |
| 짓기 (7장) | 0.9.237: 5.1의 합성 코드를 지웠고, 사용자가 준 소리 12개를 넣었고(5.10), 벽 두드리기를 기능째 지웠다. 0.9.249: 1단계(바닥)를 지었다(7.1). 2단계(있는 소리 옮기기)는 아직이다 |

## 1. 한눈에

- **27개 파일이 120개의 소리를 코드로 계산해서 만든다**(`AudioClip.Create` + `SetData`).
  - **92개는 지금 플레이어가 듣는 소리다.** 바꿔야 할 대상은 이것이다(5.3~5.9).
  - 24개는 이미 녹음된 파일이 먼저 울리고, 합성 코드는 파일이 없을 때만 도는 대체 코드였다. 파일은 그대로 두고 합성 코드만 지웠다(5.1, 0.9.237).
  - 3개는 아무도 부르지 않는다. 1개는 일부러 넣은 무음이다(5.2).
- **소리 파일로 바꾸지 못할 소리는 없다.** 모든 합성 소리는 한 번 계산해서 저장해 둔 것이다. 게임 값을 따라 실시간으로 다시 만드는 소리는 하나도 없다. 게임이 바꾸는 부분은 재생할 때의 피치와 볼륨이고, 녹음 파일에도 똑같이 걸린다. 예를 들어 전기톱 피치는 감는 정도를, 리퍼의 바람은 차지 정도를 따른다.
- **소리를 내는 일은 Sonity 하나가 맡고, 우리 코드에는 얇은 층 하나만 남기는 것을 권한다**(3장). 표면별 소리 대체, 좀비가 듣는 소음, 설정 볼륨, 슬로모션은 그 얇은 층이 맡는다.
- **사용자가 넷 다 답했다**(4장, 2026-11-21). 가장 큰 것은 소리 숫자를 어디에 두느냐였고, 답은 "내 철학 지켜": 숫자는 표에 남고 Sonity 이벤트는 표와 폴더에서 실행 중에 만든다(3.7).

## 2. 지금의 소리

0.9.235에서 `Assets/_Project/Scripts`를 세었다.

| 무엇 | 지금 |
| --- | --- |
| 소리를 코드로 만드는 파일 | 27개, 소리 120개 (5장) |
| `AudioSource`를 직접 만드는 파일 | 28개 (`AddComponent<AudioSource>` 30곳) |
| `AudioSource`를 다루는 파일 | 46개 |
| 채널 볼륨에 묶는 곳 | `GameAudio.Bind` 27개 파일 29곳 |
| 한 번만 울리는 3D 소리 | `GameAudio.PlayAt`이 소리마다 새 GameObject를 만들고 끝나면 지운다. 재사용(풀)이 없다 |
| 동시 재생 한도 | 한 곳에서 다루지 않는다. 기능마다 `VoiceRing`으로 막는다. 이 링은 Unity의 실제 음성 32개를 넘으면 새 소리가 들리지 않던 문제를 기능별로 막은 것이다 |
| 믹서 | 없다. `GameAudio`가 소스마다 볼륨, 덕킹, 슬로모션 피치를 직접 쓴다. 주석에 "나중에 AudioMixer로 옮길 자리"라고 적혀 있다 |
| 표 (`Tables/Audio`) | `Sounds.csv` 269줄(키마다 볼륨·피치), `SoundCues.csv` 80줄(동작마다 울릴 것, 표면별 버전, 좀비가 듣는 거리), `Ambience.csv` 29줄, `Reverb.csv` 12줄, `Music.csv` 3줄 |
| 소리 묶음 (`SoundBank`) | 폴더가 곧 목록이다. `Art/Audio/Movement/Run/Concrete/*.wav`는 Movement 묶음의 `Run/Concrete` 키가 된다 |
| 모드 | 소리를 싣지 않는다. 실행 중에 소리 파일을 읽는 코드가 없고, `Editor/ModSdk`에도 소리가 없다 |

## 3. Sonity로 옮기나

### 3.1 출시된 게임은 어떻게 하나

출시된 게임은 대부분 Wwise나 FMOD 같은 소리 도구를 쓴다. 역할은 이렇게 나뉜다.

- **코드**는 *언제*와 *게임의 값*만 말한다. 예: "발소리, 표면은 철판, 속도 0.7".
- **소리 도구**는 *무엇이 어떻게* 울리는지 정한다. 어느 파일인지, 무작위로 고르는지, 볼륨과 피치, 거리에 따른 감쇠, 동시 재생 한도, 버스, 덕킹 같은 것이다.
- 사운드 디자이너는 도구 안에서 **들으면서** 맞춘다.
- 게임 쪽에는 도구를 감싸는 얇은 층이 하나 있다.

우리 `SoundCues`도 "코드는 큐 이름만 부르고, 표가 무엇을 울릴지 정한다"는 같은 모양이다. 하지만 세 가지가 다르다.
- 편집이 CSV 글자라서 들으면서 맞출 수 없다.
- 소리 92개는 이 길을 타지 않고 코드에서 만들어진다.
- 음성(재생 중인 소리)을 한 곳이 관리하지 않는다.

### 3.2 Sonity가 주는 것

Sonity 문서(v1.2.0.1, 2026-09-14)에서 읽은 것이다. 써 보지는 않았다.

- **바탕은 Unity 소리다.** 안에서 `AudioSource`를 쓰고, 파일은 그대로 `AudioClip`이며, 출력은 `AudioMixerGroup`으로 보낸다. FMOD나 Wwise처럼 소리 엔진을 따로 두지 않는다.
- **이벤트와 컨테이너.** 이벤트(`SoundEvent`) 안에 여러 클립을 넣는다. 무작위로 고르되 최근에 나온 것은 피하고, 볼륨과 피치도 무작위로 줄 수 있으며, 클립을 겹칠 수 있다.
- **음성 관리.**
  - 전체 음성 한도가 있다. 한도가 차면 가장 조용한 것(볼륨 × 우선순위)을 끊는다.
  - 이벤트마다 동시 재생 수를 정한다. 주인(owner)마다 따로 셀 수도, 전체로 셀 수도 있다.
  - 여러 이벤트가 한도 하나를 함께 쓰게 할 수 있다(`SoundPolyGroup`).
  - 다시 울리기까지의 쿨다운과 울릴 확률을 정할 수 있다.
  - 소스를 재사용한다(풀).
- **파라미터.** 재생할 때 넘기는 값이 소리 도중에도 피치, 볼륨, 필터, 거리를 바꾼다. 값 하나(`Intensity`)로 세게·중간·약하게 층 사이를 넘나들 수도 있다.
- **태그(`SoundTag`).** 재생할 때 넘기거나 전역으로 걸어서, 울릴 이벤트나 수정자를 고른다.
- **3D 소리.** 감쇠 곡선, 퍼짐, 거리 배율, 리버브 존 섞임을 정할 수 있다. Steam Audio를 연결하면 벽에 막히는 소리도 된다.
- **멈춤.** 전역 일시정지가 있고, UI 소리처럼 멈춤을 무시하게 할 수도 있다.
- **편집기.**
  - 인스펙터에서 바로 들어 볼 수 있다(Ctrl+Q).
  - 플레이 중에 고친 값이 그 자리에서 반영된다.
  - 씬 뷰와 게임 뷰에 소리 위치를 그려 주고, 음성 수 통계를 보여 준다.
- **음악.** 끝나기 조금 전에 다음 곡을 시작하는 전환이 있다. 플레이리스트와 스템은 소개 문구에만 있고 문서에는 설명이 없다.
- **소스 코드가 들어 있다**(FAQ: "The source code is included"). 체험판만 DLL이다.

### 3.3 Sonity에 없는 것: 우리 층이 맡는다

| 지금 하는 일 | Sonity | 옮긴 뒤 맡는 곳 |
| --- | --- | --- |
| 표면 대체 사슬 (덕트 → 철판 → 콘크리트 → 표면 없음, `Surfaces.Fallbacks`) | 태그는 있지만 대체 사슬이 없다 | 우리 층이 사슬을 따라 고른 뒤 이벤트(또는 태그)로 넘긴다 |
| 슬로모션 때 세계 소리의 피치 (`GameAudio.FollowWorldPitch`가 매 프레임 소스마다 바꾼다) | 전역 피치가 없다. 시간 배율은 지연·페이드·쿨다운에만 걸린다 | 믹서의 세계 그룹들(전투·이동·목소리·환경음)에 노출한 Pitch 값 하나. UI와 음악은 지금처럼 빠진다 |
| 설정의 채널 볼륨 6개와 덕킹 (`GameAudio.Volume`, `SetDuck`) | 믹서 그룹을 쓰라고 한다 | 믹서 그룹 6개(전투·이동·목소리·UI·음악·환경음)에 노출한 볼륨 값 |
| 좀비가 듣는 소음 거리 (`SoundCues.csv`의 `noise`) | 해당 없음. 소리가 아니라 게임 규칙이다 | 표에 남는다. 우리 층이 소리와 소음을 함께 낸다 |
| 여러 PC에 같은 소리 | 해당 없음. 로컬에서만 울린다 | 지금처럼 각 PC가 사건을 받아 자기 소리를 낸다. 바뀌지 않는다 |
| 도플러 끄기, UI 소리가 일시정지·필터·리버브를 무시 (`GameAudio.Bind`) | 이벤트 설정에 있다 | 이벤트 설정과 믹서 |

### 3.4 개조

- 소스가 들어 있으니 고칠 수는 있다. 스토어 약관(Standard EULA, Extension Asset)은 게임에 넣기 위해 고치는 것을 허락한다. 하지만 Sonity의 소스나 고친 판을 따로 배포하는 것은 막는다. 1석 라이선스이고 컴퓨터 두 대까지 쓸 수 있다. 모드 SDK(`Editor/ModSdk`)는 원래 게임 코드를 담지 않으므로 문제없다.
- **업그레이드 방법이 "Sonity 폴더를 통째로 지우고 다시 넣기"다**(1.1.0~1.2.0 변경 기록). 고친 부분은 업그레이드할 때마다 날아간다. 그래서 원칙은 이렇다.
  - 먼저 감싼다(3.3의 얇은 층).
  - 고치는 것은 감싸서 안 될 때만 한다.
  - 고친 곳은 이 문서의 표에 한 줄씩 적어 업그레이드 뒤 다시 넣는다.
- 지금 보이는 개조 후보는 하나다. 파라미터를 넘기는 재생 함수가 `params` 배열을 받아서 부를 때마다 메모리를 잡는다(문서의 함수 모양을 보고 한 추측). 측정해서 문제가 되면 배열 없는 판을 더한다.

### 3.5 위험

- 개발자 한 사람이 만드는 제품이다(Sonigon, Victor Engström).
- 문서는 Unity 6.0~6.7(6.3 포함)을 지원한다고 한다. 그러나 스토어 호환 표에는 6.3이 없다. 개발자는 스토어 표가 틀린다고 한다.
- 문서끼리 어긋나는 곳이 있다. 소리 속도 기준값이 430과 340으로 다르고, 압축 볼륨을 URP가 지원하는지도 다르게 적혀 있다.
- 스토어 리뷰는 읽지 못했다.
- 옮기는 일이 크다. 소리를 다루는 파일이 46개다. 6장처럼 단계마다 게임이 돌아가는 상태를 남긴다.

### 3.6 권장

**옮긴다.** 재생과 음성 관리는 Sonity 하나가 맡고, 게임만의 규칙은 얇은 층 하나가 맡는다. 이유는 셋이다.

1. **출시된 게임의 모양이다.** 소리 도구가 소리를 정하고, 코드는 언제와 값만 말한다. 우리 표를 계속 키우면 결국 소리 도구를 직접 만들게 된다.
2. **주인이 하나가 된다.** 지금은 28개 기능이 저마다 소스를 만들고 저마다 한도를 막는다. 손(`HandOwner`), F 키(`InteractKey`), 플레이어 색(`PlayerColours`)을 한 주인에게 모은 것과 같은 일이다.
3. **들으면서 맞출 수 있다.** 소리 92개를 새 파일로 바꾸는 일은 귀로 하는 일이다. CSV 숫자를 고치고 게임을 다시 켜서 듣는 것보다, 인스펙터에서 듣고 플레이 중에 바로 고치는 편이 맞다.

FMOD와 Wwise를 고르지 않는 이유도 있다. 둘 다 별도 프로그램과 뱅크 빌드가 따라오고, Unity 소리와 음성을 따로 관리한다. Sonity는 Unity 소리 위에서 돌아서 지금의 파일, 믹서, 리버브 존을 그대로 쓴다.

### 3.7 표가 Sonity를 움직인다 (사용자 결정, 2026-11-21)

사용자: "내 철학 지켜. 모딩, 유지 보수, 유동성 챙기고 안정성 좋은 걸로." 그래서 3.6의 셋째 이유(인스펙터에서 듣고 고치기)는 버리고, 나머지 둘(출시 게임의 모양, 주인이 하나)을 지킨다.

- **집은 그대로다.** 폴더가 곧 목록이고(`SoundBank`), 소리 숫자는 `Sounds.csv`, 무엇을 겹쳐 울릴지는 `SoundCues.csv`에 있다. Sonity 이벤트 에셋은 손으로 만들지 않는다. 한 숫자에 집이 하나라서 어느 쪽이 참인지 흔들리지 않는다.
- **Sonity 이벤트는 실행 중에 만든다.** 우리 층이 묶음의 키마다 `SoundContainer`(클립들)와 `SoundEvent`(볼륨, 피치, 동시 재생 수, 믹서 그룹)를 `ScriptableObject.CreateInstance`로 만들고 표의 숫자를 쓴다. Sonity 소스에서 확인한 것: 이벤트는 `internals.sonityGuid`로 구분되고(`SoundManagerInternals`의 사전 열쇠), 이 값과 `cachedName`은 실행 중에도 쓸 수 있는 공개 필드다. 편집기의 `AssetPostprocessor`가 GUID를 매기는 것은 에셋 파일에만 하는 일이므로, 만든 이벤트마다 새 GUID를 준다. Sonity를 고치지 않아도 된다.
- **표를 다시 읽으면**(`GameTables.Reloaded`) 같은 이벤트에 숫자만 다시 쓴다. 플레이 중에 표를 고쳐 바로 듣는 길이 남는다(`/sound` 미리 듣기).
- **모드**: 표만 고치는 모드가 소리 크기와 피치를 바꿀 수 있다. 나중에 모드가 소리 파일을 싣게 되어도, 실행 중에 읽은 클립이 같은 길로 이벤트가 된다.
- **Sonity가 맡는 것**은 3.2 그대로다: 음성 한도와 끊을 소리 고르기, 동시 재생 수, 쿨다운, 소스 재사용(풀), 일시정지, 디버그 그림과 통계.
- **Sonity 폴더**는 받은 그대로 `Assets/Plugins/Sonity`에 둔다(예제 57MB 포함). 업그레이드가 "폴더를 지우고 다시 넣기"이고 패키지는 이 자리로 들어오므로, 옮기거나 고치지 않는 편이 안정적이다. 들여온 뒤 Unity가 Sonity의 어셈블리 여덟 개를 에러 없이 컴파일했다(PlayMaker 연동도 PlayMaker 없이 컴파일된다). 1단계 커밋(0.9.249)에서 git에 넣었다. 예제(`Examples`, 57MB)만 MagicaCloth 예제처럼 `.gitignore`로 뺐다. 예제를 보려면 패키지를 다시 들여온다.

## 4. 사용자가 정한 것 (2026-11-21)

1. **소리 숫자를 어디에 두나: 표에 남는다.** 사용자: "내 철학 지켜. 모딩, 유지 보수, 유동성 챙기고 안정성 좋은 걸로." 내 권장안(Sonity 이벤트 에셋으로 옮기기)은 고르지 않았다. 어떻게 하는지는 3.7.
2. **Sonity 구입: 샀다.** 사용자가 사서 기본 위치(`Assets/Plugins/Sonity`)에 손대지 않고 들여왔다. "필요하면 위치 옮기거나 수정해"라고 했고, 옮기지 않기로 했다(3.7).
3. **새 소리 파일: 오는 대로 넣는다.** 사용자: "새 소리를 내가 한 번에 다 구할 순 없으니까 일단 5.1 이미 파일로 울리는 것부터 하자." 5.1을 먼저 했고, 같은 날 사용자가 준 12개를 넣었다(5.10). 나머지 "찾을 말"(5.3~5.9)은 사용자가 고르는 대로 넣는다.
   - 유료 효과음 팩을 사거나, 상업적으로 써도 되는 무료 묶음(예: 해마다 나오는 Sonniss GDC 번들)을 쓴다. 라이선스는 받을 때 확인한다.
   - 이미 있는 후보 일부는 `ArtSource`의 GMod·L4D 워크숍 파일이다(전기톱 루프, `mega_mob_incoming`). 이 파일들은 지금 게임에 있는 다른 워크숍 소리와 같은 라이선스 문제를 안는다.
4. **사이렌: 넘어간다.** 사용자: "사이렌은 넘어가죠." 1장 골목 셔터의 사이렌(5.7)은 지금의 합성 소리 그대로 둔다.
5. **벽 두드리기: 지웠다.** 사용자: "벽 두드리기 기능은 쓸모가 없으니까 삭제하자." `PlayerKnock`, 그 RPC, H 키(입력 에셋과 키 설정 창), `Stealth.csv`의 `Knock.*`, `StealthSounds` 에셋, `knock.wav`를 지웠다(0.9.237).

## 5. 바꿀 목록

열의 뜻:
- **지금**: 코드가 만드는 소리가 어떻게 들리는지.
- **언제**: 게임에서 언제 울리는지.
- **바꿀 것**: **굵은 글씨**는 이미 프로젝트에 있는 파일이고, 따옴표 안은 효과음 라이브러리에서 찾을 말이다.
- **높음**: 700 Hz를 넘는 높은 소리가 들어 있다는 뜻이다. 높은 소리는 사용자의 귀를 아프게 한다. UI 소리는 700 Hz 아래, 2.2 kHz 로우패스가 규칙이다. 바꿀 파일도 이 규칙에 맞춰 낮게 고른다.

경로 줄임: `S/` = `Assets/_Project/Scripts/`, `A/` = `Assets/_Project/Art/Audio/`, `AS/` = `ArtSource/Library/Audio/`(게임 밖, 아직 가져오지 않음).

### 5.1 이미 파일로 울리는 것: 합성 코드만 지운다 (24개) — 지웠다 (0.9.237)

녹음 파일이 먼저 울리고, 합성은 파일이 없을 때만 돌던 것이다. 이제 파일이 없으면 소리가 없다(폴더를 비우면 조용해진다).

| 파일 | 소리 | 0.9.237 |
| --- | --- | --- |
| `S/Combat/Feedback/FeelAudio.cs` | 소총·권총·중화기 사격, 빈 탄창 딸깍, 탄 적음 둘, 마지막 탄, 탄창 빼기, 탄창 넣기, 노리쇠, 슬라이드 뒤로·앞으로 (12), 처치, 헤드샷 처치, 몸 쓰러짐 (3) | 지웠다. 쓰이지 않던 `HitMarker`의 합성(5.2)도 함께 갔다. 남은 합성은 보호막 둘(5.4)뿐이고, 로딩 때(`FeelAudio.Prepare`) 19개 대신 그 둘만 만든다. 0.9.238에 보호막 깨짐도 사용자가 준 파일(`breakarmorself`)로 바뀌어 합성을 지웠고, 이제 보호막 맞음 하나만 남았다 |
| `S/Enemies/Elites/ZombieReaper.cs` | 리퍼 낫 휘두름 (`tonfa_swing_miss1/2.ogg`) | 합성 휙을 지웠고, 휘두름과 차지 소리는 사용자가 준 소리로 바뀌었다(5.10) |
| `S/Player/Stealth/PlayerKnock.cs` | 노크 (`A/Stealth/knock.wav`) | 기능째 지웠다(4장 5번) |
| `S/Player/Stealth/PlayerThrow.cs` | 돌 부딪힘 (`qubodupImpactStone.ogg`) | 합성을 지웠고, 사용자가 준 `Stone hit`로 바뀌었다(5.10) |
| `S/UI/Kit/KitSound.cs` | 탄약·회복·보호막·총 줍기, 의심, 발각 (6) | 지웠다 |

### 5.2 안 쓰이는 것

| 파일 | 소리 | 처리 |
| --- | --- | --- |
| `FeelAudio.cs` | `HitMarker`: 부르는 곳이 없다. 맞힘 소리는 `HitConfirm/Body`가 낸다 | 합성은 0.9.237에 5.1과 함께 지웠다. 열거형 값은 저장된 데이터의 번호를 지키려고 남겼다 |
| `S/Combat/Moments/MomentSound.cs` | `duel`: 부르는 곳이 없다 | 지운다 |
| `S/Classes/CardFx.cs` | 건틀릿 `Blast` 길: 부르는 곳이 없다 | 지운다 |
| `FeelAudio.cs` | `Draw`: 권총·중화기를 꺼낼 때의 일부러 넣은 무음 | 진짜 소리를 넣을 수 있다: **KINEMATION `TacticalShooterPack/SFX/General/S_DrawPistolWithHolster.WAV`, `S_Equip.WAV`** |

### 5.3 좀비 (13개)

| 소리 | 지금 | 언제 | 바꿀 것 |
| --- | --- | --- | --- |
| 거구 포효 (`ZombieBody.Brute.cs:112`) | 낮게 떨어지는 긴 포효, 1.8초 | 거구가 처음 일어설 때. 모든 PC, 3D 120 m | "giant monster roar, falling, raspy, 1.8 s". 부분 후보: **`A/Enemies/Zombie/Attack/rage_at_victim*.wav`** 피치를 낮춰서. 코드는 이미 `Art/Resources/Audio/Boss/BruteRoar` 파일을 먼저 찾는다 |
| 거구 등장 울림 (`:139`) | 서브 드롭 쿵, 1.4초 | 같은 때. 모든 PC, 2D | "cinematic boss braam, sub drop, 1.5 s". 후보: `AS/L4D1WeaponAudio_3785893356/npc/mega_mob/mega_mob_incoming.ogg`. 경로 `Audio/Boss/BruteSting` |
| 전기톱 공회전 (`ZombieBreadbag.cs:222`) | 2행정 엔진 공회전, 루프 | 브레드백이 살아 있는 동안. 모든 PC, 3D 34 m | **`AS/.../weapons/chainsaw/chainsaw_idle_lp_01.ogg`**(가져와야 함). 이미 있는 칸: `EliteLooks.chainsawIdle` |
| 전기톱 회전 (`:222`) | 엔진을 감아올림, 루프. 피치가 감는 정도를 따라 0.85→1.15 | 감기·베기 | **`chainsaw_high_speed_lp_01.ogg`**, 감는 순간에 `chainsaw_start_01/02.ogg`. 칸: `EliteLooks.chainsawRev` |
| 캔디맨 벌 (`ZombieCandyman.cs:238`) | 벌 여섯 마리의 웅웅거림, 루프 | 캔디맨이 살아 있는 동안. 끌 때 1.4배 | "bee swarm buzz loop, seamless". 칸: `EliteLooks.candymanBuzz` |
| 차저 울부짖음 (`ZombieCharger.cs:341`) | 거친 낮은 목소리가 올랐다 내림, 1.1초 | 돌진 준비 | "large creature bellow, bull roar, 1 s". 부분 후보: `A/Enemies/Zombie/Leap/Male/rage_*.wav` 피치를 낮춰서 |
| 폭발 좀비 터짐 (`ZombieExploder.cs:328`) | 젖은 무거운 폭발, 0.9초 | 죽을 때(처형 제외). 3D 80 m | **Vefects `SFX_Vefects_Explosion_02_One_Shot_01/02.wav`**에 **`A/Enemies/Zombie/Gore/HeadBurst/headless_*.wav`**를 겹쳐서 |
| 폭발 좀비 해제 (`:358`) | 부드러운 쿵 뒤에 가스가 빠지는 쉿, 0.45초 | 처형으로 해제할 때 | "gas leak hiss, short deflate, 0.5 s" |
| 닌자 사라짐 (`ZombieNinja.cs:331`) | 천과 바람의 휙, 0.45초 | 연기 속으로 사라질 때 | "cloth whoosh vanish, 0.5 s". `A/Movement/Dodge/Wind/Dash*`도 맞지만 회피 소리라서 둘이 같게 들린다. 칸: `EliteLooks.ninjaVanish` |
| 닌자 나타남 (`:331`) | 칼집에서 칼 뽑는 울림, 0.9초. **높음**(2.2~6 kHz) | 누군가의 등 뒤에 나타날 때(경고) | **`A/Enemies/Elites/katana_deploy_01.ogg`**(리퍼 돌진도 씀) 또는 **`A/Grenades/KnifeDraw/pull_out_blade_1-3.wav`**. 칸: `EliteLooks.ninjaAppear` |
| 리퍼 바람 (`ZombieReaper.cs:340`) | 낮은 바람과 70 Hz 웅, 루프. 피치와 볼륨이 차지를 따른다 | 낫 차지 | "eerie wind loop, low howl, seamless". 칸이 없으므로 `EliteLooks`에 새로 만든다 |
| 좀비 부르기 들숨 (`ZombieCall.cs:49`) | 목으로 숨을 들이켜다 신음으로 올라감, 1.2초 | 스스로 깬 좀비가 동료를 부르기 전 | "zombie inhale into growl, rising, 1.2 s" |
| 좀비 부르기 포효 (`:73`) | 쉰 포효가 떨어짐, 1.5초 | 부를 때. 3D 70 m | **`A/Enemies/Zombie/Attack/rage_at_victim*.wav`**(남녀 묶음, 약 1초)가 잘 맞는다 |

### 5.4 전투 느낌 (4개)

| 소리 | 지금 | 언제 | 바꿀 것 |
| --- | --- | --- | --- |
| 보호막 깨짐 (`FeelAudio.cs`) | 밝은 깨짐, 떨어지는 윙, 유리 핑 | 내 보호막이 다할 때 | **했다(0.9.238)**: 사용자가 준 `breakarmorself.ogg`를 `A/HitConfirm/ShieldBreak`에 넣고 합성을 지웠다 |
| 보호막 맞음 (`FeelAudio.cs`) | 전기 딱, 떨어지는 지직 | 내 보호막이 맞을 때, 친구 보호막이 맞을 때 | **Vefects `SFX_Vefects_Electric_Impact_01/02_One_Shot_01.wav`**. 폴더 `A/HitConfirm/ShieldHit`. 친구 쪽 길은 고쳐야 한다(6.1의 버그 2) |
| 죽음 스팅 (예전 `S/Audio/DeathSting.cs`) | 쿵, 먹먹해지는 소리, 낮은 울림, 귀울림, 느려지는 심장 두 번, 2.9초. **높음**(귀울림 2.35 kHz) | 내가 죽을 때. 2D, UI 채널 | **했다(0.9.308)**: 사용자가 고른 MW3 Death Screen 애드온 화면(2026-11-22)과 함께 애드온 소리를 내고 합성과 DeathSting을 지웠다. 2026-11-22부터 그 휘슬은 데스 스크린 애드온 모듈의 것(`Art/Audio/Addons/DeathScreen/Whistle`, HudAddons 묶음 `DeathScreen/Whistle`)이고 UI 묶음 `Death`와 그 파일은 지웠다. 약 1.8 kHz 울림이 있다 |
| 특수 처치 팅 (`S/UI/Hud/KillFeed/SpecialKillStamp.cs:263`) | 딱 뒤에 밝은 쇳소리 둘(1975·2637 Hz). **높음** | 미트샷, 레일건, 스키트 등 특수 처치 | "low UI stinger, soft impact + muted low chord, 0.3 s". 낮게 새로 고른다 |

### 5.5 클래스·카드·순간 연출 (26개)

순간 연출 소리(`MomentSound.cs`)는 코드가 이미 `Resources/Audio/Moments/<키>` 파일을 먼저 찾는다. 하지만 그 폴더가 없어서 지금은 전부 합성 소리가 울린다.

| 소리 | 지금 | 언제 | 바꿀 것 |
| --- | --- | --- | --- |
| 기사 궁극기 `knight` | 낮은 종, 칼 뽑기 | 궁극기 | "low bell toll + sword unsheathe". 칼 층: **`katana_deploy_01.ogg`**, **`pull_out_blade_1-3.wav`** |
| 돌격 궁극기 `assault` | 나팔 세 음 + 북 | 궁극기 | **11-20의 돌격 나팔 시안**(`ArtSource/Audio/FieldBugle`, `SoftTrumpet/04_three_notes.wav`, `ShortCharge`). 이 시안이 궁극기용인지 CHARGE 키(5.9)용인지는 사용자가 정한다 |
| 고스트 궁극기 `ghost` | 높은 쇳소리 한 번, 숨. **높음**(2.4 kHz) | 궁극기 | "metallic shimmer, short, low" |
| 포인트 궁극기 `point` | 리볼버 실린더 돌림, 공이치기 | 궁극기 | **KINEMATION `TacticalShooterPack/SFX/R08/S_R08_OpenCylinder.WAV`, `S_R08_CloseCylinder.WAV`**, **`A/Weapons/Fatebringer/Fatebringer_CraneClose.wav`** |
| 데탕트 궁극기 `detente` | 심장 두 번, 맑은 종 | 궁극기 | **`heartbeat_beat_*.wav`**에 "soft clear chime"(낮게)을 더해서 |
| 슈스훈트 궁극기 `schiesshund` | 딸깍 여섯, 금전등록기. **높음**(1.76 kHz) | 궁극기 | "cash register" + **`A/Guns/LowAmmoClick/lowammo_01.wav`** |
| 서포트 궁극기 `support` | 무전 딸깍, 상자 내려놓기 | 궁극기 | "radio squelch click + wooden crate drop" |
| 현상금 `bounty` | 종 둘, 동전 반짝, 낮은 떨어짐 | 데스페라도가 현상금을 받을 때(`BountyBurst.cs:34`) | "reward chime, coins, low sting" |
| 반격 던지기 `reversal` | 올라가는 바람 뒤 쿵 | 기사 반격(`CardFx.cs:96`) | "whoosh riser into impact". `A/Movement/Dodge/Perfect/Phantom Wind*`도 맞지만 완벽 회피 소리라서 둘이 같게 들린다 |
| 엄호 막기 `cover` | 쇠 울림, 잡음, 쿵 | 기사 엄호로 막을 때(`CardFx.cs:100`) | "metal deflect clang". **`A/Combat/BOOM/Parry/Iron Fist*`**는 패리 소리라서 둘이 같게 들린다 |
| 건카타 `kata` | 빠른 딸깍 다섯, 울림 | 포인트 건카타 꺼내기(`CardFx.cs:104`) | **KINEMATION R08 실린더 소리** |
| 결투 승리 `duelwon` | 도탄 윙, 낮은 종 한 번 | 결투에 이겼을 때(`HoundDuel.cs:152`) | **`A/Bullets/Ricochet/ricochet01-05.wav`** + "single low bell toll" |
| 카드 포격 (`CardFx.cs:368`) | 떨어지는 쿵, 먹먹한 잡음 | 돌격 포격 포탄이 떨어질 때. 3D 90 m | **Vefects `SFX_Vefects_Explosion_01_One_Shot_01/02.wav`** |
| 건런 착탄 (`S/Classes/Assault/GunRunSound.cs:75`) | 30 mm 탄 터짐, 흙 튐 | 착탄 몇 발마다 | "30mm cannon impact explosion dirt, 0.5 s". 후보: **Vefects Explosion_01**. 파일 경로 `Audio/Moments/gunrun_hit` |
| 건런 BRRRT (`:96`) | 먼 곳의 GAU-8 연사 | 마지막 탄 뒤 | "A-10 BRRRT GAU-8 distant burst, 2 s". 경로 `gunrun_brrrt` |
| 건런 제트 (`:141`) | 제트기 저공 통과, 1.3초에 가장 큼. **높음**(1.9 kHz 윙) | 통과 | "jet fighter low flyby, 3 s". `GunRunFx.cs:26`의 `JetPeak = 1.3`이 지금 합성 소리의 정점에 묶여 있다(6.1의 버그 3) |
| 데탕트 가스 (`S/Classes/Detente/DetenteGas.cs:241`) | 소독 스프레이를 한 번에 비우는 쉿. **높음**(1.8~7 kHz) | 데탕트 궁극기, 구름마다 | "aerosol / medical spray long hiss, 3–4 s". 길이가 `ultGasSeconds`(3초) + 0.35초를 따르므로, 3.35초 이상의 파일이거나 루프를 페이드로 끊는다 |
| 고스트 돌진 (`S/Classes/Ghost/GhostFx.cs:111`) | 부드러운 바람 휙 | 체인 순간이동 | "teleport wind gust whoosh, 0.4 s" |
| 쇼다운 끌기 (`S/Classes/Hound/ArenaSound.cs`) | 올라가는 바람 뒤 낮은 쿵 | 쇼다운 시작 | "reverse whoosh into impact, 0.7 s". 후보: **Vefects `SFX_Vefects_Radial_Charge_Skill_Ready_One_Shot_01`**. 경로 `Audio/Moments/showdown` |
| 쇼다운 복귀 | 낮은 쿵, 바람 빠짐 | 돌아올 때 | 후보: **Vefects `SFX_Vefects_Radial_Burst_One_Shot_01/02`**. 경로 `showdown_back` |
| 관중 환호 | 군중 소리, 함성, 박수, 휘파람. **높음**(휘파람) | 환호 속 복귀 | "crowd cheer stadium swell, 3 s". 경로 `cheer` |
| 피닉스 부활 (`S/Classes/PhoenixFx.cs:276`) | 불길 휙과 쿵, 올라가는 부드러운 음 | 피닉스 카드가 혼자 남은 플레이어를 일으킬 때 | "fire ignite burst whoosh + soft rising tone, 1 s". 음은 700 Hz 아래로. 경로 `Audio/Moments/phoenix` |
| 용접 토치 (`S/Classes/Support/RepairLooks.cs:250`) | 부드러운 쉿, 루프 | 서포트가 토치를 들고 있는 동안 | "blowtorch gas flame loop". 볼륨 버그가 있다(6.1의 버그 1) |
| 망치 두드림 (`:270`) | 판자 쿵, 망치 머리 울림. **높음**(2350 Hz) | 망치질 한 번마다 | **`A/HitConfirm/HeadOld/hammer_03.ogg`**(CC0, 11-12부터 쓰지 않음) |
| 보급 가방 휙 (`S/Classes/Support/SupplyPack.cs:249`) | 짧은 바람 | 던질 때 | "bag toss whoosh soft". `grenade_throw.wav`, `Cloth/VaultSwish`도 맞지만 다른 동작의 소리다 |
| 보급 가방 툭 (`:227`) | 부드러운 쿵, 천 바스락 | 친구 가슴에 닿을 때 | "bag catch thud canvas". 후보: **`A/Movement/Push/push1-4.wav`** |

### 5.6 플레이어 (3개)

| 소리 | 지금 | 언제 | 바꿀 것 |
| --- | --- | --- | --- |
| 찌르기 누름 (`S/Player/Poke/SkinPoke.cs:367`) | 둥근 짹(780→330 Hz) + 뽁. 부위마다 피치가 달라서 정수리는 약 1.1 kHz | 손끝이 친구를 누를 때 | "soft cartoon boop, finger poke squish, 0.1 s", 낮게. 경로 `Audio/Poke/Press` |
| 찌르기 뗌 (`:367`) | 위로 가는 가벼운 짹 | 손을 뗄 때 | "soft pop release". 경로 `Audio/Poke/Lift` |
| 돌 던짐 휙 (`S/Player/Stealth/PlayerThrow.cs:295`) | 돌이 지나가는 바람 | T 던지기 | "small stone throw whoosh, 0.3 s" |

### 5.7 캠페인 (2개)

| 소리 | 지금 | 언제 | 바꿀 것 |
| --- | --- | --- | --- |

### 5.8 맵 기믹 (18개)

`GimmickSounds.cs`는 파일을 찾는 길이 없으므로 길을 더해야 한다. 큐 순서는 맵이 번호로 저장하므로 바꾸지 않는다.

| 큐 | 지금 | 쓰는 곳 | 바꿀 것 |
| --- | --- | --- | --- |
| `Static` | 꺼진 채널의 TV 잡음, 루프 | 1·2장 미끼 | "TV static noise loop". `fallstatic.wav`는 큰 낙하 소리라서 쓰지 않는다 |
| `Clunk` | 차단기 딸깍, 쿵, 전기 웅 | 문, 차단기, 화물, 연료 밸브, 탑승교, 트레이 | "circuit breaker switch clunk, power hum" |
| `Ding` | 엘리베이터 종 두 음. **높음** | 엘리베이터 | "elevator arrival ding", 낮게 |
| `Smash` | 깨짐, 깊은 쿵, 덜컹 | 우리, 자물쇠 | **`A/Movement/Door/Barge/me_door_generic_barge1-4.wav`** |
| `Hiss` | 가스 쉿(벤트에선 루프) | 연료 함정, 밸브, 착지, 벤트 | "gas leak hiss loop" |
| `Unlock` | 딸깍 둘, 빗장, 철컥. **높음**(2.4 kHz) | 탑승교, 자물쇠, 트레이 | "key turn lock unlock bolt" |
| `Jingle` | 쇠 짤랑 넷. **높음**(3.1~4.2 kHz) | 열쇠 줍기 | "keys jingle pickup", 낮게 |
| `Snap` | 줄 튕김, 딱 | 떨어뜨리기 | "rope snap cable break" |
| `Crash` | 거대한 낮은 충돌, 쇠 울림 | 떨어뜨리기, 연료 함정 | "shipping container drop metal crash" |
| `CarAlarm` | 차 경보 와우, 경적, 루프. **높음** | 경보 차 | "car alarm loop", 낮게 |
| `Fire` | 낮은 불길, 탁탁, 루프 | 불 | "fire burning loop crackle" |
| `Rumble` | 길고 깊은 우르릉 | 무너짐 | "concrete collapse rumble debris" |
| `Beep` | 삐 두 번(1760 Hz). **높음** | 금속 탐지기 | "metal detector alarm", 낮게 |
| `Chime` | 안내 방송 종 세 음, 미끼에선 루프 | 3장 지하철, 4장 공항 미끼 | **`Assets/ThirdParty/Airport_Terminal_Scene/Sounds/bingbong.wav`**, 루프 미끼엔 **`terminal_voice_01-21.wav`**(공항 안내 방송) |
| `FireBell` | 화재 벨, 초당 18번, 루프. **높음** | 화재 경보 | "fire alarm bell ringing loop", 낮게 |
| `Lever` | 라쳇 넷, 걸쇠 쿵 | 화물 문, 탑승교, 두 손 레버 | "heavy lever pull ratchet latch" |
| `Shutter` | 덜컹거리며 올라감, 쿵 | 화물 문 닫기, 두 손 레버 | "roller shutter door rattle" |
| `Generator` | 발전기 털털, 루프 | 차단기(엔진) | "portable generator running loop" |

### 5.9 UI (26개)

모두 내 PC에서만, 2D, UI 채널로 울린다. 코드는 이미 `A/UI/<큐>` 폴더를 먼저 찾으므로 폴더에 파일을 넣으면 쓰인다(5.1의 여섯이 그렇게 됐다). **16개가 낮은 소리 규칙을 어긴다**(5.4의 특수 처치 팅을 더한 수).

| 큐 | 지금 | 쓰는 곳 | 바꿀 것 |
| --- | --- | --- | --- |
| `Buy` | 서랍 철컥, 종 두 음. **높음** | 상점, 로비, 옷장 확정 | "purchase confirm, low soft thunk" |
| `Refuse` | 둔한 삐 둘(165 Hz) | 모든 거절 | "UI error low double buzz" |
| `Alert` | 880→659 Hz 내려가는 두 음. **높음** | 동료 다운·사망, 카드 경고 | "low two-note warning, falling" |
| `PickMoney` | 종 둘(2637·3520 Hz). **높음** | 돈 줍기 | "coins pickup, muffled pouch, low" |
| `Escape` | 올라가는 네 음(523~1047 Hz). **높음** | 챕터 끝, 결과 | "mission complete stinger, low brass" |
| `Unseen` | 종 세 음. **높음** | 잠입 성공 | "stealth success, soft low marimba" |
| `Charge` | 북 + 뿔피리 두 음 | 돌격 CHARGE, 여러 클래스 키 | "short war horn + drum hit". 돌격 나팔 시안 후보(5.5) |
| `Taunt` | 칼로 방패 두 번, 낮은 뿔피리. **높음**(울림 성분) | 기사 도발 | "sword on shield clang + low horn". 후보: `A/Combat/BOOM/Block` |
| `Hunter` | 딸깍 + 1760 Hz. **높음** | 고스트 사냥 표시 | "low soft UI notch tick" |
| `HunterLost` | 쿵, 내려가는 두 음 | 고스트 사냥 놓침 | "low falling two-note loss" |
| `Ping` | 1319·1976 Hz 두 음. **높음**. 가장 많이 쓰임 | 핑, 상점, 옷장, 바리케이드 | "soft low UI ping marker" |
| `Mark` | 쿵, 종 둘. **높음** | 현상금 표시, 결투 | "wanted mark stamp, low" |
| `Bounty` | 공이치기, 쿵, 종. **높음** | 현상금 | "revolver hammer + low cash payout" |
| `Supply` | 지퍼, 탄창 두 번 | 보급 | "bag zipper + mags into pouch". 후보: **`A/Guns/Rifle/MagIn`** |
| `Hammer` | 망치 세 번 | 바리케이드 짓기 | **`hammer_03.ogg`** 세 번, 또는 "hammer nailing wood" |
| `Splinter` | 쿵 + 쪼개짐 | 바리케이드 맞음, 좀비 돌파 | "wood crack splinter impact" |
| `Collapse` | 쿵, 판자 다섯 개 덜컹 | 바리케이드 무너짐 | "wooden planks collapse clatter" |
| `TowerRise` | 모터, 라쳇, 잠김 | 탑 올라감 | "mechanical servo rise, ratchet lock" |
| `TowerTick` | 1318 Hz 삐 한 번. **높음** | 탑 초읽기(매초) | "low clock tick, wood block" |
| `TowerScan` | 삐 세 음. **높음** | 손바닥 스캐너 | "hand scanner read, low beep" |
| `TowerOpen` | 쿵, 철컹, 올라가는 음. **높음** | 탑 문 열림 | "security door unlock, low" |
| `TowerClear` | 찌르는 두 음, 종. **높음** | 층 클리어 | "level clear stinger, low" |
| `Enfiladed` | 쿵, 쇠 울림, 금관 화음, 종. **높음** | 탑 마지막 | "cinematic boom + brass swell, 2.5 s". 쿵 층: Vefects Explosion |
| `CardPick` | 낮은 쿵, 미끄러짐, 화음 | 카드 고르기 | "card pick, low thud" (지금도 낮다) |
| `CardPickGrand` | 같은 것 + 낮은 5도 | 큰 카드 고르기 | 위와 같은 결로 더 크게 |
| `CardBurn` | 낮은 쉭, 둔한 펑 | 카드 태우기 | "paper catching fire, low" |

### 5.10 사용자가 준 소리 (2026-11-21, 0.9.237)

MP3 그대로 폴더에 넣었다. 크기와 피치는 `Sounds.csv`의 줄이고 모두 1로 시작했다. 나는 귀로 들어 보지 않았다.

사용자가 들어 본 뒤(2026-11-21) 고친 것:
- **리퍼가 전반적으로 너무 작다** → 볼륨 1.5배(`PlayOneShot`이라 1을 넘는다). 온전히 들리는 거리를 3~4m에서 8m로 늘렸다. 예전에는 링 끝(9m)에서 절반 아래로 들렸다. 이 거리는 `ZombieReaper.csv`의 `soundNear`·`soundFar`로 뺐다.
- **줍기 공통 소리를 살짝 키워야** → `UI/Pick` 1.3배. 바닥 줍기는 0.8을 곱해 1에서 멈춘다.
- **T를 꾹 눌러도 돌을 왼손에 드는 동작이 없어서 Ready가 안 난다** → 돌을 수류탄과 같은 왼손이 꺼내 들고 던지게 했다(`Throwable.Stone`, [투척물](Grenades.md)). Ready는 손이 돌을 쥐는 순간(꺼내기 클립의 핀 순간)에 난다. 원본이 아주 작아(최고 -29dB) 3배로 했다.

| 파일 | 언제 | 묶음/키 |
| --- | --- | --- |
| `Reaper Attack` (5.6초, 0.8초쯤 가장 큼) | 리퍼가 낫을 모으기 시작할 때. 모든 PC, 3D(8m까지 온전히, 60m까지 줄어듦) | `Zombie/Reaper/Attack`. 예전 차지 소리(`katana_deploy_01`)를 대신한다 |
| `Reaper Attack Hit 1~6` | 낫이 한 바퀴 돌 때 누구라도 베였으면. 모든 PC, 3D(8m까지 온전히) | `Zombie/Reaper/Hit`. 예전 휘두름(`tonfa_swing_miss1/2`, 0.62배 낮게)을 대신한다 |
| `Reaper Attack Evaded 1·2` | 낫을 다 모아 휘둘렀는데 아무도 안 베였을 때(밑으로 피했거나 원 밖·벽 뒤) | `Zombie/Reaper/Evaded` |
| `Stone Ready` | T를 누르고 있어 왼손이 돌을 꺼내 쥘 때(꺼내기 클립의 핀 순간). 던지는 사람의 PC에만 2D | `Stealth/StoneReady`. 새 소리 묶음 `Stealth` = `Art/Audio/Stealth` |
| `Stone hit` | 던진 돌이 닿은 자리(착지와 튈 때 최대 3번) | `Stealth/StoneHit`. 예전 `StealthSounds` 에셋(킬 확인음과 같은 `qubodupImpactStone`)을 대신한다. 코드에 있던 피치 폭 0.9~1.15는 `Sounds.csv`로 옮겼다 |
| `Pick Up SFX` | 무엇이든 손에 집을 때 공통으로, 종류별 소리 밑에 | `UI/Pick` (`KitSound.PlayPickup`) |

- **리퍼의 "누구라도"**: 각 PC가 자기 플레이어는 실제로 베어 보고(행동한 PC가 정한다), 친구의 사본은 같은 규칙(원 안, 같은 층, 사이에 벽 없음, 낮은 자세 아님)으로 읽는다. 친구의 자세는 `MovementMirror`로 모든 PC에 같으므로 기다리지 않고 모든 PC가 같은 소리를 낸다. 어긋나는 드문 경우: 친구의 PC에서 피해가 막혔을 때(무적 시간 등) 그 친구의 PC만 Evaded를 듣는다.
- **줍기 두 겹**: 사용자: "Pick up SFX는 일단 줍기고 거기에 체력이나 쉴드나 그런 거에 따라 따로 추가 효과음이 있으면 되는 거 아님?" 그래서 손에 집는 모든 곳이 `Pick`을 깔고 그 위에 종류 소리(`PickAmmo`·`PickHeal`·`PickShield`·`PickGun`·`PickMoney`)를 겹친다: 바닥 줍기 다섯 종류(`FieldPickups`), 던진 나이프 다시 줍기, 허브의 총 거치대와 탄약 상자. 동료가 건네준 것(서포트 보급, 데탕트 치료, 동료의 치료)은 줍는 일이 아니라서 종류 소리만 난다. 종류 소리는 지금의 파일(RE4, MWII, L4D1 줍기 소리와 레이저 소리)이고, 돈은 아직 합성 동전(5.9)이다. 종류 소리를 줍기와 다른 "추가 효과음"으로 바꾸려면 그 폴더의 파일만 바꾸면 된다.

### 5.11 하이힐 발소리 (2026-11-21, 0.9.241 고르기, 0.9.244 소리)

사용자: "하이힐 사운드 넣었어? 옷장에서 발이 하이힐이면 하이힐 사운드로 해줘." 0.9.241은 고르는 쪽만 지었다: 입힌 몸의 발 밑을 재서 굽이 3cm 이상이면(`DrawnHeels`, Player.csv `Body.heelsFrom`) 큐가 `@Heels` 줄을 쓴다(자세한 것은 [유지 보수 안내](../Guides/MaintenanceGuide.md)의 "하이힐 발소리"). 그 뒤 사용자가 GMod 창작마당 애드온 **DSteps Dynamic High Heels Footsteps**(2789283802)의 소리 358개를 줬다(0.9.244). 귀로 들어 보지 않았다.

**어떻게 놓았나**: [Tools/Audio/heel_steps.py](../../Tools/Audio/heel_steps.py)가 애드온의 표면 폴더를 게임의 표면 이름으로 `Art/Audio/Movement/Heels/<종류>/<표면>`에 늘어놓는다(소리를 바꾸면 다시 돌리면 된다. GUID는 그대로). 어느 소리를 어디에 둘지는 애드온 자신의 Lua(`dsteps_autorun.lua`)를 따랐다.

| 종류 | 애드온에서 | 쓰는 큐 |
| --- | --- | --- |
| `Walk` | walk | `StepWalk@Heels`, 웅크린 걸음(`StepSneak@Heels`, 애드온처럼 4분의 1 크기), 내려서기·기어오른 뒤 발 딛기·렛지 오르기, 앉은 회피 |
| `Run` | run | `StepRun@Heels`, 착지(애드온처럼 두 발이 잇달아), 넘은 뒤 착지, 회피, 돌진 밀치기 |
| `Settle` | wander: 애드온이 움직이다 멈출 때와 점프할 때 내는 발 고쳐 딛기 | `Jump@Heels` |
| `Land` | land (애드온에 들었지만 쓰지 않음) | `Land@Heels/Gravel`(자갈·흙·풀·모래·눈), `Land@Heels/Metal` |

| 게임 표면 | 애드온 소리 |
| --- | --- |
| Concrete (아스팔트·벽돌·플라스틱 등) | concrete. 걷기는 애드온처럼 피치 0.75 |
| Stone | stone (애드온에 들었지만 쓰지 않음) |
| Marble (타일도) | marble, 피치 0.85 |
| Glass | glass hard·hit (애드온처럼 걷기·뛰기 같은 세 개) |
| Metal (크레인·파이프·강철 표적) | metalbar (애드온: 쇠 = metalbar) |
| MetalBox·Gantry·Duct | metalbox (애드온: 철망·환기구 = metalbox) |
| Wood | wood. 뛰기도 wood 걷기, 멈춤은 squeakywood (애드온 그대로) |
| WoodPanel | squeakywood (애드온: woodpanel = squeakywood), 뛰기는 걷기 |
| WoodPlank (마루·데크) | bluntwood 걷기·멈춤, deckwood 뛰기 (애드온에 들었지만 쓰지 않음) |
| Gravel·Dirt·Grass·Sand | 각자의 것. 모래의 멈춤은 dirt (애드온 그대로) |
| Snow·Mud | snow·mud. 이 소리를 위해 표면을 더했다(Surfaces.csv: 눈은 모래, 진흙은 흙의 자식이라 다른 소리·관통·이펙트는 전과 같다. 키워드 snow, mud) |
| Water | water through (걷기·뛰기), wander (멈춤) |
| Tarp·Carpet | rug (애드온에 들었지만 쓰지 않음). 카펫은 방수포의 자식이라 Tarp 폴더 하나로 둘 다 |

- 한 표면에 어느 종류가 없을 때 애드온이 다른 소리를 빌려 쓰는 곳은 그 파일을 그 폴더에 복사했다. 폴더가 없으면 소리 묶음이 표면의 부모(나무 → 콘크리트)로 넘어가서 엉뚱한 바닥 소리가 난다.
- 옷 스치는 소리는 원래 발소리와 같이 깔린다. 발 떼기(`StepRelease`)와 세게·크게 착지(몸이 부딪는 소리)는 원래 소리다.
- 적에게 들리는 거리(`noise`)는 원래 큐와 같다.
- 출처: GMod 창작마당 애드온이다. 공개 빌드 전에는 교체가 필요할 수 있다.

## 6. 함께 찾은 것

### 6.1 버그

1. **용접 토치가 너무 크다.** `RepairLooks.cs:170`에서 볼륨을 0.3으로 놓지만, `:174`의 `GameAudio.Bind`가 그 값을 채널 볼륨으로 덮어쓴다. 그래서 토치가 전투 소리 최대 크기로 울린다. 0.3은 `SetGain`으로 놓아야 한다.
2. **친구의 보호막 맞는 소리는 항상 합성 소리다.** `S/Network/Sync/PlayerNetworkCombat.Life.cs:14`는 `FeelAudio.PlayAt(FeelSound.ShieldHit, ...)`를 바로 불러서 `A/HitConfirm/ShieldHit` 폴더를 보지 않는다. 내 보호막(`ShieldFeedback`)은 폴더를 먼저 본다.
3. **제트기 소리의 정점이 코드에 박혀 있다.** `GunRunFx.cs:26`의 `JetPeak = 1.3`은 합성 소리가 가장 큰 순간이다. 파일로 바꾸면 그 파일의 정점에 맞춰야 한다.
4. **한 번 울리는 3D 소리마다 GameObject를 새로 만들고 지운다**(`GameAudio.PlayAt`). 재사용이 없다. Sonity의 풀로 옮기면 사라진다.

### 6.2 높은 소리

UI 채널에서 낮은 소리 규칙(700 Hz 아래, 2.2 kHz 로우패스)을 어기는 것이 16개다: 5.9의 15개와 특수 처치 팅. 세계 소리 가운데 밝은 것도 같은 귀가 듣는다: 데탕트 가스, 제트기, 환호 휘파람, 망치, 닌자 칼, 기믹의 `Jingle` `Beep` `FireBell` `CarAlarm` `Ding`, 사이렌, 견인차 삐. 파일을 고를 때 낮은 것을 고르고, 필요하면 Sonity 이벤트의 로우패스로 깎는다.

## 7. 짓는 순서

단계마다 커밋 하나와 `bundleVersion` 하나를 쓰고, 단계가 끝날 때마다 게임은 돌아가는 상태로 남는다.

진행: 사용자가 "일단 5.1부터"라고 해서 3단계의 첫 일(5.1)을 바닥보다 먼저 했다(0.9.237). 지우기만 하는 일이라 Sonity와 상관없다. 1단계는 0.9.249에 지었다(7.1). 다음은 2단계다.

1. **바닥.**
   - Sonity를 넣는다.
   - 믹서를 만든다: 그룹 6개(전투·이동·목소리·UI·음악·환경음), 노출 볼륨, 세계 그룹의 노출 피치.
   - `SoundManager`를 둔다.
   - 얇은 층을 만든다. 하는 일은 넷이다: 표면 대체, 좀비가 듣는 소음, 설정 볼륨, 슬로모션.
   - 이 단계에서는 들리는 것이 바뀌지 않는다.
2. **있는 소리 옮기기.**
   - `SoundBank` 폴더와 `Sounds.csv`에서 실행 중에 Sonity 이벤트를 만든다(3.7). 편집기 도구도, 에셋도 없다. "폴더에 파일을 넣으면 소리가 된다"가 그대로 남는다.
   - 분야 하나씩 옮긴다: 이동, 전투, 목소리, 총, 환경음과 음악.
   - `Sounds.csv`의 소리 숫자는 남는다(4장 1번). 지금 코드 상수에 있는 소리 숫자(감쇠 거리, 동시 재생 수)는 옮기면서 표로 뺀다.
3. **합성 소리를 파일로.**
   - 5.1과 5.2를 먼저 한다. 지우기만 하면 된다.
   - 다음은 이미 있는 파일로 바꿀 수 있는 것(**굵은 글씨**)이다.
   - 새 파일이 필요한 것은 사용자가 파일을 고르는 대로 넣는다.
   - 합성 코드는 바꾼 만큼 지운다.
4. **옛 길 지우기.** `GameAudio.Bind`, `VoiceRing`, `PlayAt`, `SoundBank`를 지운다. Sonity 밖에서 `AudioSource`를 만드는 스크립트가 없는지 보는 감사 검사를 더한다. 손 점검(`Tools/Checks/hands_check.py`)과 같은 방식이다.

### 7.1 1단계에서 지은 것 (0.9.249)

사용자(2026-11-21): "응, 1단계 지어". 게임에서 들리는 것은 바뀌지 않는다. 아직 어떤 게임 소리도 이 길을 타지 않고, 콘솔의 `/sound sonity`만 탄다.

| 무엇 | 어디 | 하는 일 |
| --- | --- | --- |
| Sonity | `Assets/Plugins/Sonity` (받은 그대로, 예제는 git 밖) | 음성 한도와 끊을 소리 고르기, 한 소리의 동시 재생 수, 반복 없는 클립 고르기, AudioSource 재사용 |
| 믹서 | `Data/Resources/Audio/GameMixer.mixer`, 만드는 도구 `Tools/Audio/make_mixer.py` | 채널 그룹 여섯과 그 아래 `Loud` 그룹 여섯. 노출 값: `<채널>Volume`, 세계 채널의 `<채널>Pitch`, `<채널>LoudVolume`. 손으로도, Unity 믹서 창에서도 고치지 않는다. 도구를 고쳐 다시 돌린다(같은 파일이 나오고 .meta의 guid는 지킨다) |
| 엔진 숫자 | `Tables/Audio/SoundEngine.csv` | `Engine.voiceLimit` 32, `voices` 4(한 소리의 동시 재생 수), `loudDecibel` 12, 거리를 말하지 않은 3D 소리의 `near` 1.5·`far` 14 |
| 얇은 층 | `Scripts/Audio/GameSounds.cs` | 묶음 키와 Sounds.csv 줄로 Sonity의 SoundEvent·SoundContainer를 실행 중에 만든다. 표를 다시 읽으면 다음 재생 때 숫자를 다시 쓴다. `Play`(2D), `PlayAt`(3D, `Space`: 온전한 거리·사라지는 거리·직선 또는 로그 감쇠). 첫 장면이 열릴 때 SoundManager를 만든다 |
| 채널의 주인 | `Scripts/Audio/GameAudio.cs` | 설정의 채널 볼륨, 덕킹, 슬로모션 피치를 옛 길(소스마다)과 믹서(노출 값)에 같은 숫자로 쓴다. 마스터 볼륨과 일시정지(`GamePause` → `GameAudio.SetPaused`)는 Sonity의 SoundManager를 거쳐 정한다 |
| 점검 | `Scripts/Audio/SoundEngineCheck.cs` → `Logs/sound-engine-check.txt` | 플레이 없이 돈다. 스크립트를 다시 읽을 때마다, ENFILADE → 소리 → 소리 엔진 점검, `Temp/enfilade-soundcheck.request`. 믹서 그룹 열셋, 모든 묶음 키를 2D와 3D로 만들어 클립 수·그룹·공간 설정을 본다. 첫 결과: ALL KEPT, 키 412개 × 2 = 824개, 1을 넘는 키 6개가 Loud 그룹 |
| 들어 보기 | `/sound sonity <묶음>/<키> [거리 m]` | 같은 키를 Sonity로 재생한다. 거리를 주면 그만큼 앞에서 3D로, 안 주면 2D로 |

지으면서 정한 것과 그 까닭:

- **Sonity가 리스너를 쥔다.** SoundManager는 시작할 때와 리스너를 찾을 때마다 `AudioListener.volume`과 `AudioListener.pause`를 자기 설정값으로 다시 쓴다. 그래서 우리 마스터 볼륨과 일시정지도 Sonity의 `SetGlobalVolumeRatio`와 `SetGlobalPause`를 거쳐 알린다. 시작할 때는 지금의 마스터 볼륨과 일시정지를 먼저 설정에 넣고 깨운다.
- **1을 넘는 볼륨은 Loud 그룹으로 보낸다.** Sonity의 음성은 AudioSource라 1을 넘지 못한다. Sonity의 볼륨 키우기(`SONITY_ENABLE_VOLUME_INCREASE`)는 `AudioListener.volume`을 1000배로 올리고 자기 음성만 1000분의 1로 낮춘다. 그러면 아직 옛 길을 타는 우리 소리가 모두 +60dB로 터진다. 그래서 쓰지 않았다.
- **Sonity의 기본값을 끈다.** 기본값은 피치 무작위 ±0.25반음, 가까울수록 2D로 섞기, 소리 퍼짐 180°, 거리에 따라 늘어나는 리버브다. 표의 피치 범위만 쓰고, 3D는 늘 3D, 퍼짐 0, 리버브 섞기 1로 둔다.
- **거리는 곡선으로 옮긴다.** Sonity는 Unity의 감쇠를 평평하게 두고 소리 크기를 스스로 줄인다(정규화한 거리 → 곡선). Unity의 직선(Linear)과 로그(Logarithmic) 감쇠를 `Space`가 같은 모양의 곡선으로 만든다.
- **한 번 울리는 소리의 주인은 하나다**(SoundManager). 그래서 한 소리의 동시 재생 수는 게임 전체에서 센다(`LimitedGlobally`).
- **아직 옛 소스는 믹서로 보내지 않는다.** 옛 AudioSource는 채널 볼륨을 소스마다 곱한다. 믹서로도 보내면 두 번 줄어든다. 옮기는 대로 하나씩 옛 길을 떠난다(2~4단계).

재 본 것과 못 잰 것:
- **재 봤다**: 컴파일 검사(`CHECK OK`), 에디터 점검 ALL KEPT, Unity가 믹서를 에러 없이 들인 것.
- **못 쟀다**: 플레이 중의 동작. SoundManager가 뜨는지, 믹서 노출 값이 먹는지(Unity는 플레이 중에만 답한다), 마스터 볼륨·일시정지가 예전과 같은지, `/sound sonity`가 옛 `/sound`와 같은 크기로 들리는지는 사용자가 들어 봐야 안다.
