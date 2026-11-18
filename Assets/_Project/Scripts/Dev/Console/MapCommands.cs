/// <summary>
/// The console commands of the maps side (2026-11-18): the tools for looking at a map and its colliders from where
/// play can't go. Each is added beside its feature and registered here, so the chat's console channel and the developer
/// console get the same list (GameCommands).
/// </summary>
public static class MapCommands
{
    public static void Register(ChatCommandRouter router)
    {
        router.Register("noclip", "켜기·끄기 · on · off · 숫자(기본 속도 m/s) · seen · unseen — 맵과 콜라이더를 보려고 몸이 벽·바닥을 그대로 지나 보는 쪽으로 난다(이동 키, 점프 위, 앉기 아래, 달리기 빠르게, 회피 느리게, 휠 = 속도). 좀비 눈에는 안 보이고(seen으로 바꿈), 조준점과 발밑의 콜라이더 이름·모양·크기·레이어가 화면에 뜬다. 다시 입력하면 그 자리에서 몸으로 돌아온다 (Noclip)", Noclip.Command); // dev-text
    }
}
