# 받은 파일 (Inbox)

블렌더 좀비 작업실의 **클립 불러오기** 목록에 "받은 파일:"로 뜨는 폴더다. 여기 넣은 FBX는 게임에 바로 들어가지 않는다. 블렌더에서 불러와 고친 뒤 **게임에 보내기**를 눌러야 들어간다.

- Mixamo: 캐릭터는 아무거나, 동작을 고른 뒤 Download → Format **FBX Binary(.fbx)**, Skin **With Skin**, Frames per Second **30**. 걷기·뛰기는 **In Place**에 체크.
- 다른 곳에서 산 사람 뼈대 FBX도 된다. 뼈 이름이 Mixamo·Unreal식이면 알아서 맞춘다.
- Skin 없이 받은 파일을 여러 개 쓸 때는 같은 폴더에 그 캐릭터의 T-포즈 파일을 `폴더이름.tpose.fbx`로 함께 둔다.

블렌더를 거치지 않고 바로 게임에 넣고 싶으면 Mixamo 파일(With Skin)을 `Assets/_Project/Art/Animations/Zombies/<슬롯>/`에 넣으면 된다. 그 슬롯 목록에 자동으로 들어간다.
