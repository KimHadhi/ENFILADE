# HUD 애드온 도구 (Tools/HudAddons)

게리 모드 HUD 애드온을 게임의 공용 그리기 층(`Assets/_Project/Scripts/UI/Hud/Addons/Shared`, 네임스페이스 `HudAddons`)으로 옮길 때 쓰는 파이썬 도구. 순수 파이썬이라 Pillow 같은 것이 필요 없다. 언제나 `python -I`로 돌린다.

- `addon_picture.py <in.vtf|in.png> <out.png>`: 애드온 그림을 원래 크기(mip 0) 그대로 PNG로 풀고, 같은 실행에서 바로 앞에 UI 그림용 `.meta`(sRGB, 알파 = 투명, 밉맵 Kaiser, 삼선형, 가장자리 고정, 원본보다 작게 줄이지 않는 최대 크기, 고품질 압축)를 쓴다. 넣을 곳은 `Assets/_Project/Art/Resources/UI/Addons/<모듈>/...`, 게임에서는 `HudAddons.Pictures.Get("<모듈>/<이름>")`. 폴더를 주면 그 안의 `.vtf`·`.png`를 모두(이름은 소문자로) 옮긴다. `--info <vtf|폴더>`는 크기·형식·프레임만 보여 준다. 움직이는 VTF는 `--frame N` 또는 `--frames`(이름_0.png, 이름_1.png …). 이미 있는 그림은 `--replace`일 때만 다시 쓰고 `.meta`(GUID)는 건드리지 않는다. VTF 7.0~7.5, DXT1/DXT3/DXT5, BGRA8888 등 HUD에 쓰이는 형식을 푼다.
- `font_metrics.py <ttf|폴더> [--csv]`: 글꼴의 unitsPerEm, 윈도 위·아래(usWinAscent/usWinDescent), 굵기, 이름들, 변수 글꼴의 인스턴스를 읽는다. `Data/Resources/Tables/Hud/AddonFonts.csv`의 줄을 만들 때 쓴다(GMod 글꼴 크기는 칸 높이라 이 세 수가 있어야 크기와 기준선이 원본과 같다).
