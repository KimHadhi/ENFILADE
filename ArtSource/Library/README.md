# 재료 선반 (ArtSource/Library)

게임이 **아직 쓰지 않는** 재료를 두는 곳이다. Unity 프로젝트(`Assets`) 밖이라 Unity가 읽지 않고, 빌드에도 들어가지 않는다.
`Assets/_Project`에는 게임이 실제로 쓰는 것만 둔다는 규칙([구조 안내](../../Docs/Architecture.md)의 "무엇이 어디에 있나")의 짝이다.

| 폴더 | 무엇 |
| --- | --- |
| `Audio/<애드온 이름>` | 워크숍 애드온에서 옮겨 온 원본 소리. 게임에 쓴 것은 잘라서 `Assets/_Project/Art/Audio`의 맞는 폴더에 따로 넣었다([애드온 이식 기록](../../Docs/Records/AddonPortNotes.md)) |
| `Audio/EarlyCombat` | 초기에 쓰던 전투 소리(막기·패링·주먹·피격). 지금은 `Art/Audio/Combat`의 다른 소리와 합성음이 대신한다 |
| `Textures/<애드온 이름>` | 애드온의 원본 텍스처 가운데 쓰지 않은 것 |

## 쓰기로 했을 때

1. 파일을 `Assets/_Project/Art`의 맞는 폴더로 **복사**한다. 소리 묶음이 읽는 폴더(`Art/Audio/Guns`, `Movement`, `HitConfirm` …)에 넣으면 Unity 창을 눌렀을 때 묶음에 저절로 들어간다.
2. 길이나 크기를 손봐야 하면 복사본을 고친다. 여기의 원본은 그대로 둔다.
3. 어디서 온 소리인지 [에셋 출처](../../Docs/Records/AssetSources.md)에 한 줄 적는다.

## 더 안 쓰게 됐을 때

`Assets/_Project`에서 빼낸 것 가운데 다시 쓸 수도 있는 원본은 여기로 옮기고, 만들어진 것(구운 것, 옛 화면 조각)은 지운다.
아무도 쓰지 않는 에셋은 `python Tools/Find-UnusedAssets.py`가 찾아 준다.
