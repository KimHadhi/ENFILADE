# 가리키기와 찌르기 (F 꾹)

2026-11-12 사용자: "F가 핑이잖아? 핑 찍을 때 남는 손으로 손가락질 하는 모션 있고 F 꾹 누르고 있으면 GTA5에서 무슨 키 누르고 있으면 계속 손가락질할 수 있잖아? 그런 것처럼 F 꾹 누르고 있으면 손가락질 계속 하고 있을 수 있고 그걸로 친구 건드리면 이 첨부한 파일처럼 스킨 터치 꾹 돼서 볼 누르고 뱃살 누르고 그런 기능 있었으면 좋겠어. 재밌을 거 같아."

첨부한 VRChat 패키지(DocMe Skin Touch)는 느낌만 참고했다. 그 패키지는 VRChat의 Material Contacts와 Poiyomi 모듈이라 이 게임에서 돌지 않고, 라이선스가 코드를 옮겨 쓰는 것을 막는다. 여기의 코드와 셰이더는 모두 처음부터 새로 쓴 것이다.

## 조작

- **F를 짧게**: 예전처럼 조준점의 적을 핑한다(`PlayerPing`).
- **F를 꾹**(0.25초, `Poke.csv` `Point.holdAfter`): 누르고 있는 동안 왼손이 바라보는 곳을 가리킨다. 총은 오른손에 그대로 있고, 맨손이어도 왼손이다(콜아웃 몸짓과 같은 손). 검지는 펴고 나머지는 주먹, 엄지는 그 위로 접는다.
- **상호작용이 먼저다**: F 누름이 줍기·부활·사다리·문·상점·수리 같은 상호작용 위에서 시작됐으면 가리키지 않는다. 가리키는 중에 그런 것이 조준점에 오면 끝난다.
- **손이 필요하면 끝난다**: 조준(ADS)·장전·꺼내기·살펴보기·사격·주먹·가드·차지. 끝나면 F를 다시 눌러야 다시 가리킨다.
- 서 있거나 앉아 있을 때만(걷기 포함). 파쿠르·사다리·쓰러짐·잡힘 중에는 안 된다.

## 찌르기

손이 닿는 거리(어깨에서 검지 끝까지 팔 길이의 97%, `Point.reach`)의 친구를 가리키면 손끝이 그 몸에 닿으러 간다. 몸 중 가장 바깥(옷·머리카락·피부)에 닿는다.

| 부위 | 어디 | 느낌(Poke.csv) |
| --- | --- | --- |
| 볼 | 눈 아래, 코 옆에서 얼굴 옆까지 | 말랑, 1.2cm, 1초에 5번 출렁 |
| 배 | 엉덩이 뼈 조금 위에서 가슴 뼈보다 꽤 아래까지, 몸 앞쪽 | 가장 푹신, 1.8cm, 출렁임 많음 |
| 팔 | 위팔(어깨 쪽 30% 빼고)과 아래팔(손목 앞까지) | 단단, 0.8cm |
| 어깨 | 쇄골 뼈, 위팔의 어깨 쪽 30% | 더 단단, 0.6cm |
| 정수리 | 두 눈보다 눈 사이 거리만큼 위 | 머리카락 위로 살짝 |

- 손끝이 꾹 들어가면(`press`, `pressTime`) 그 자리가 오목하게 들어간다. 손끝을 떼면 스프링처럼 되돌아오며 살짝 부풀었다가(`hz`, `bounce`, `bulge`) 가라앉는다.
- 손끝을 문지르듯 옮기면 자국도 따라간다. 한 번에 6cm(`Touch.slide`) 넘게 옮기면 앞 자국은 되돌아오고 새로 누른다.
- 누를 때 '뽁', 뗄 때 가볍게 '퐁' 소리가 난다. 볼은 높게, 배는 낮게(`pitch`). 소리 파일을 `Resources/Audio/Poke/Press`·`Lift`에 넣으면 그 소리를 쓴다.
- **눌리지 않는 곳**: 가슴, 엉덩이, 사타구니는 어느 부위도 아니라 절대 눌리지 않는다(총관리자의 결정. 장난스럽고 건전하게. 바꾸고 싶으면 말해 주면 된다). 이마·코·입·턱·귀·뒤통수·눈·목·손·다리·등도 부위가 아니다. 이런 곳과 찌르기를 끈 친구에게는 손끝이 1.5cm 앞에서 멈춘다(`Point.hover`).
- 가슴·엉덩이·치마처럼 가슴 뼈·윗가슴 뼈·엉덩이 뼈에 달린 휴머노이드가 아닌 뼈(흔들림 뼈)는 모두 '꺼짐'으로 본다. 배의 윗선은 가슴 뼈보다 몸통 길이의 10% 아래라서 배를 눌러도 가슴까지 번지지 않는다.

## 설정

설정 → 조작 → **친구가 내 캐릭터 찌르기**(기본 켬, `GameSettings.letOthersPoke`, `SettingsDefaults.csv` 열 `letOthersPoke`). 끄면 모두의 화면에서 친구 손끝이 내 캐릭터 앞에서 멈춘다.

## 표정 연결 (face-moods)

찔림은 각 PC에서 이벤트로 나온다. 표정 파일은 건드리지 않았다.

```csharp
// 손끝이 들어가기 시작할 때 true, 뗄 때 false. 모든 PC에서(찔린 사람 자신의 PC 포함).
SkinPoke.Poked += (PlayerMovement poked, PlayerMovement poker, PokeZone zone, bool pressing) => { ... };
// 지금 눌리고 있는지(누르는 동안 표정을 유지하려면)
bool now = SkinPoke.IsPoked(poked, out PokeZone zone);
// PokeZone: None, Cheek, Belly, Arm, Shoulder, HeadTop
```

## 어떻게 되어 있나

### 손 (PlayerPing → PlayerPoint → PointArm)

- `PlayerPing`이 F를 길게 누른 것을 `PlayerPoint.Hold`로 알린다.
- `PlayerPoint`(플레이어)는 가리키는 중인지와 찌르기를 허락하는지를 들고 있다. 다른 PC에는 `PlayerPose.Marks`의 두 비트(`Pointing`, `NoPoke`)로 간다(`MovementMirror`). 가리키는 방향은 포즈가 이미 보내는 시선 그대로라 가리키기만을 위한 전송은 없다.
- `PointArm`(몸 모델, 가리키는 몸에만 붙음)은 FPSAF·손·총·콜아웃 몸짓 다음(`GunFramework + 6`), 옷감 전에 왼팔을 고친다.
  1. 손목에서 검지 뿌리로 가는 선을 가리키는 쪽으로, 손바닥은 아래이면서 몸 안쪽으로(`Point.palmIn`) 돌린다.
  2. 손가락 마디마다 다음 마디가 있는 쪽을 월드 방향으로 겨눈다(`FromToRotation`).
  3. `TwoBoneIK`로 팔꿈치를 아래·바깥으로 굽혀 손목을 놓는다.

  모든 휴머노이드에서 실행 중에 맞춘다(굽기 없음). 1인칭 카메라는 그 전에 놓였으니 시야는 팔을 따라 흔들리지 않는다.
- 모든 PC에서 같은 코드가 같은 시선과 같은 플래그로 돈다. 내 1인칭 몸에도, 남이 보는 내 몸에도 같은 손가락질과 같은 찌르기가 보인다(각 PC의 시선이 조금 늦게 오는 만큼만 다르다).

### 몸 표면 (PokeBody)

친구의 손끝이 처음 가까이 올 때 그 몸의 모든 메시(피부·머리카락·옷·액세서리)를 읽는다. 지금 걸린 블렌드셰이프(체형 옵션·표정)를 더하고, 정점마다 가장 많이 따르는 뼈의 공간에 놓은 뒤, 6mm(`Touch.grid`) 칸마다 점 하나로 줄인다. 한 프레임에 2만 정점(`Touch.bakeVertices`)씩 읽어 끊김이 없다. 걸린 블렌드셰이프는 무게가 큰 12개(10 이상)만 더한다(셰이프 하나가 메시 전체 복사라서. 이치고는 키가 438개).

시선 선은 뼈의 경계 구부터 걸러서 가장 가까운 점(선에서 7mm 안, `Touch.width`)을 찾는다. 그래서 배 위의 셔츠, 볼 위의 옆머리처럼 가장 바깥에 닿는다. 점은 매 프레임 뼈를 따라가므로 Magica가 흔드는 살(`FleshSpring`)도 그려진 자리에서 만난다. FleshSpring은 고치지 않았다.

부위는 휴머노이드 뼈로 정한다.
- 얼굴: 그 플레이어의 시선(머리가 따라감) 기준으로 두 눈 사이에서 눈 사이 거리 단위로 잰다. 눈 뼈가 없는 모델은 몸통 길이로 짐작한다.
- 배: 엉덩이~목 길이의 비율로 정한다.
- 팔: 마디 위의 비율로 정한다.

모드 캐릭터도 그대로 된다. 메시는 Read/Write가 켜져 있어야 하고(BOOTH 가져오기는 켬), 꺼진 메시는 건너뛴다.

### 눌림 (SkinPoke + SkinDent.hlsl)

`SkinPoke`는 손끝 슬롯 넷(방의 네 명)을 가진다. 누르는 동안 슬롯은 손끝을 따라간다. 떼면 누른 뼈에 붙은 채 피부 가운데를 감쇠 스프링으로 되돌리고, 멈추면 비운다. 매 프레임 옷감 다음(`Cloth + 5`)에 셰이더 전역 값을 쓴다.

| 전역 값 | 내용 |
| --- | --- |
| `_EnfDentCount` | 쓰는 슬롯 수 |
| `_EnfDentTip` | 손끝 자리, 오목한 곡률 |
| `_EnfDentPush` | 누르는 방향, 최대 깊이 |
| `_EnfDentShape` | 가장자리 부드러움, 퍼지는 반경, 되돌아올 때 부풂과 그 폭 |

슬롯의 비트(1 << 슬롯)는 눌리는 몸의 모든 렌더러의 셰이더 사용자 값(Unity 6.3 `Renderer.SetShaderUserValue`)에 켠다. 그래서 그 몸과 그 옷만 눌리고, 찌르는 손가락이나 옆 사람은 안 눌린다. 사용자 값의 비트 0~3은 SkinPoke 것이다. 다른 곳에서 사용자 값을 쓰려면 이 비트를 남겨야 한다.

`Assets/_Project/Art/Shaders/SkinDent.hlsl`은 lilToon 정점 단계에서 돈다.
- 손끝에 매달린 그릇 모양(손끝 선에서 r만큼 떨어진 곳의 깊이 -r²/2R)보다 앞에 있는 피부를 그 그릇까지 밀어 넣는다. CPU가 표면을 몰라도 손끝이 들어간 만큼 정확히 오목해진다.
- 가장자리는 부드럽게(soft max) 둥글린다. 손끝 근처는 깊이 그대로, 반경의 바깥 절반에서 사라진다.
- 손끝 바로 앞뒤 몇 cm만 움직인다. 얼굴 앞의 팔이나 머리 뒤쪽은 안 움직인다.
- 법선을 오목한 벽을 따라 기울여 툰 그림자가 진다.
- 모든 패스(색·외곽선·그림자·깊이·모션)에 같이 적용된다.

### lilToon에 넣은 줄 (업데이트하면 다시 넣기)

lilToon 2.4.1의 `Packages/jp.lilxyzw.liltoon/Shader/Includes`에 있는 두 파일에 `ENFILADE` 표시 블록을 넣었다. 하나는 모든 패스가 쓰는 `lil_common_vert.hlsl`이고, 하나는 이치고 머리카락이 얼굴에 드리우는 가짜 그림자 `lil_pass_forward_fakeshadow.hlsl`이다. lilToon을 업데이트하면 lilToon 원래 파일로 돌아가서 이 줄이 빠진다. 그러면 에디터가 스크립트를 읽을 때 콘솔에 `[SkinDent]` 경고를 낸다(`SkinDentHookCheck`). 손끝과 소리는 되지만 피부가 안 눌린다. 아래 곳들을 다시 넣는다.

`lil_common_vert.hlsl`:

1. `#define LIL_CUSTOM_VERT_COPY` 블록 바로 다음:

```hlsl
// ENFILADE (not lilToon): a friend's fingertip dents the skin (SkinDent.hlsl, SkinPoke.cs). Put back after updating
// lilToon, with the call in lilCustomVertexOS below (Assets/_Project/Docs/Poke.md).
#include "Assets/_Project/Art/Shaders/SkinDent.hlsl"
```

2. `void lilCustomVertexOS(inout appdata input, inout float2 uvMain, inout float4 positionOS)`의 `LIL_CUSTOM_VERTEX_OS` 블록 다음, 닫는 `}` 앞:

```hlsl
    // ENFILADE (not lilToon): the skin dent, see the include above.
    #if defined(LIL_APP_NORMAL)
        EnfiladeSkinDent(positionOS, input.normalOS, true);
    #else
        float3 enfiladeNoNormal = 0.0;
        EnfiladeSkinDent(positionOS, enfiladeNoNormal, false);
    #endif
```

`lil_pass_forward_fakeshadow.hlsl`:

1. `#include "lil_common_appdata.hlsl"` 바로 다음에 위 1번과 같은 `#include` 줄(주석 포함).
2. `vert`의 `LIL_VERTEX_POSITION_INPUTS(input.positionOS, vertexInput);` 바로 앞:

```hlsl
    // ENFILADE (not lilToon): the skin dent, see the include above.
    float3 enfiladeNoNormal = 0.0;
    EnfiladeSkinDent(input.positionOS, enfiladeNoNormal, false);
```

lilToon 머티리얼을 쓰는 캐릭터(마요·이치고·에네 옷·카즈사·히로, 모드의 lilToon 아바타)는 눌린다. 실(Poiyomi와 전용 셰이더)은 눌림 없이 손끝·소리·표정 이벤트만 있다.

## 가볍게

- 아무도 누르지 않으면 셰이더는 슬롯 수 하나만 보고 끝난다. 눌리는 몸이 아니면 렌더러 값 하나만 보고 끝난다.
- CPU는 누군가 가리키는 동안, 그 시선 근처(1.3m + 몸 크기)에 있는 몸만 일한다.
- 몸 표면은 몸마다 한 번 읽는다. 옷을 바꾸면 다음 찌르기 때 다시 읽는다.
- 소리는 누를 때와 뗄 때 한 번씩이다.

## 숫자 (Poke.csv)

- `Point.*`: 가리키기.
  - `holdAfter` 누르고 있어야 하는 시간.
  - `in`·`out` 팔이 오가는 시간.
  - `follow` 손끝 따라가기.
  - `range`·`reach`·`extend` 거리.
  - `hover` 못 찌르는 곳 앞에서 멈추는 거리.
  - `palmIn` 손바닥 방향.
  - `indexBend`·`fist`·`thumb` 손가락.
- `Touch.*`: 몸 표면 찾기. `width`, `grid`, `bakeVertices`, `lose`(시선이 잠깐 빗나가도 유지), `slide`.
- `Sound.*`: 소리. `volume`, `lift`, `near`, `far`.
- `Zone.*`: 부위 경계. 볼은 눈 사이 거리 단위, 배는 몸통 길이 비율, 어깨·손목은 팔 마디 비율.
- `Cheek`·`Belly`·`Arm`·`Shoulder`·`HeadTop`의 `.*`: 부위마다 눌리는 느낌.
  - `press`·`pressTime`: 얼마나, 얼마 만에.
  - `deepest`: 최대 깊이.
  - `bowl`: 오목한 곡률. 클수록 말랑하고 넓다.
  - `soft`: 가장자리.
  - `reach`: 퍼지는 반경.
  - `hz`·`bounce`·`bulge`: 되돌아오기.
  - `pitch`: 소리.

## 아직 안 된 것

- DocMe에 있는 '손이 다가오면 피부가 어두워지는 그림자'는 없다. lilToon 프래그먼트까지 고쳐야 해서 뺐다. 툰 그림자는 오목한 법선으로만 생긴다.
- 실 캐릭터는 눌림이 없다(위).
- 한 손(왼손)만 가리킨다.
