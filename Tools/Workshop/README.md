# 워크숍 애드온 도구

- `workshop.py`: L4D2 VPK·GMod GMA 애드온의 목록 만들기(`inventory`)와 통째로 풀기(`extract`).
- `port_assets.py`: 고른 애드온에서 필요한 에셋만 꺼내 프로젝트에 넣는다. 무엇을 어디에 어떻게 넣을지는 [port_manifest.csv](port_manifest.csv), 소리를 폴더로 나누는 규칙은 [sound_rules.csv](sound_rules.csv)에 있다.
- `test_port_assets.py`: 진짜 애드온 없이 합성 VPK·GMA로 `port_assets.py`를 시험한다(임시 폴더만 쓴다).

Windows의 `python`은 스토어 연결뿐이라 모두 Blender에 든 파이썬으로 돌린다.

## 에셋 들여오기

PowerShell에서 프로젝트 폴더(`F:\GameProject\ENFILADE`)로 가서:

```powershell
$py = 'F:\SteamLibrary\steamapps\common\Blender\4.5\python\bin\python.exe'
& $py -I .\Tools\Workshop\test_port_assets.py        # 도구 시험. '모두 통과'가 나오면 된다
& $py -I .\Tools\Workshop\port_assets.py --dry-run   # 무엇을 어디에 넣을지 보기만 한다
& $py -I .\Tools\Workshop\port_assets.py             # 넣는다
```

1. 애드온은 `F:\SteamLibrary`(없으면 기본 Steam 폴더)에서 찾는다. L4D2는 `left4dead2\addons\workshop\<번호>.vpk`, GMod는 `steamapps\workshop\content\4000\<번호>\*.gma`. 다른 곳이면 `--steam`, `--l4d2`, `--gmod`로 알려 준다.
2. 필요한 파일만 프로젝트 밖 `F:\GameProject\_WorkshopImport\<번호>`에 원래 경로대로 푼다([애드온 이식 기록](../../Assets/_Project/Docs/AddonPortNotes.md)의 순서와 같은 곳이라 git에 들어가지 않는다). 모델 변환 결과는 `_WorkshopImport\_converted\<행 이름>`에 남는다.
3. 소리는 `.wav.meta`와 함께, 모델은 FBX와 `Textures\*.png`로 `Assets/_Project` 아래 표의 위치에 들어간다. 이미 있는 다른 파일은 덮어쓰지 않고 요약에 적는다.
4. 넣은 행마다 [외부 에셋 출처](../../Assets/_Project/Docs/AssetSources.md)의 '나중에 교체할 것' 표에 한 줄이 생긴다(임시 자료, 이식표의 ripped·unknown 출처 표시).
5. 끝나면 Unity를 연다. 소리는 소리 묶음에 저절로 들어가고, FBX·PNG는 처음 가져올 때 `ModelImportRules`를 받으며 `.meta`가 생긴다. 그다음 `.\Tools\Test-ProjectStructure.ps1`로 확인하고 커밋한다.

| 하고 싶은 것 | 옵션 |
| --- | --- |
| 한 묶음·애드온·행만 | `--only 5`, `--only 카타나`, `--only 3803570564`, `--only krait-sfx` |
| 카타나 모델 고르기 | 첫 실행이 후보 셋을 `_converted`에 변환만 해 둔다. Blender에서 열어 보고 `--only 5 --pick <번호>` |
| 기본으로 안 하는 행(`default` 0) | 행 이름이나 워크숍 번호로 `--only`(예: `--only krait-model-view`) |
| 애드온 안의 파일 보기(pattern을 고칠 때) | `--list <번호>` |
| 이미 있는 파일·변환 결과·출처 줄을 새로 | `--force` |

## 좀비 목소리 (RE6 Zombie Sound, 3675704366)

`sound_rules.csv`의 `zombie-voice` 규칙이 `sound/npc/infected/` 아래를 키 폴더로 나눈다. idle은 `Idle`(breath가 든 것은 `Breathe`), alert·becomeenraged·recognize는 `Alert`, action/been_shot은 `Hurt`, action/die는 `Death`다. 경로에 `male`·`female` 폴더가 있으면 `<키>/Male`·`<키>/Female`, 없으면 `<키>` 바로 아래에 둔다. `Attack`·`Leap`(rageat·rage)은 0.8.15에 37개가 들어왔으므로 건너뛰고, 몸에 맞는 소리(hit·gore)와 Mega Mob 경보도 뺀다.

0.8.15처럼 고른다. 앞뒤 무음을 자른 길이가 규칙의 범위 안이고(`Hurt`는 가장 큰 곳이 0.2초 안), 이미 들어온 녹음과 같지 않은 것만 넣는다. 크기는 이미 있는 Attack·Leap 클립의 가운데값에 맞추므로, 키마다의 크기는 Sounds.csv의 `Zombie/…` 행으로 정한다. `.wav.meta`는 Attack 클립의 `.meta`를 GUID만 바꿔 쓴다(Preload Audio Data, Load In Background 켬). 요약의 '규칙 없음'에 나온 폴더는 규칙에 줄을 더하면 들어간다.

## 손으로 해야 하는 것

- 카타나 모델 후보 셋 중 고르기. 칼집이 붙었는지, 크기(Unity의 Scale Factor), 손에 쥐는 위치를 눈으로 본다.
- 애니메이션: 변환은 메시와 뼈대만 옮긴다. 휘두르기 호나 권총 슬라이드·탄창 동작은 따로 굽는다.
- `v_` 모델(`krait-model-view`)에 붙은 팔 지우기, Source 노멀맵의 초록 채널 방향 확인.
- 들어 보고 어색한 소리 지우기, 칼 소리 폴더 안 음량 맞추기(BOOM 때처럼 3dB 안), FleshyHit을 Feedback.asset 배열에 연결하기.

## 넣지 않는 것

- 작성자가 이식·재사용을 금지한 애드온 8개(`port_assets.py`의 `NO_REUSE`)는 수치만 참고한다. 표에 넣거나 `--only`로 불러도 거부한다.
- 이식표의 다른 에셋 항목: 묶음 2의 저폴리 감염자(3386016169), 걷는 좀비 애니메이션(3353266656, 3709350917), 고어 조각(3828922073)은 몸·애니메이션·절단을 구매 팩과 자체 구현으로 바꿔서, 묶음 7의 총소리 팩(3785893356, 3798403978)은 묶음이 끝나서 넣지 않는다. 권총 대안(3740412239, 2942607592)과 Meaty Melee의 처형 소리는 `default` 0인 행으로만 있다.
