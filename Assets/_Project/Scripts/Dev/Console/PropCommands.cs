using System.Collections.Generic;
using System.Text;
using UnityEngine;

// dev-text: file (console lines for trying props out; nothing here is shown in the game)

/// <summary>
/// /prop makes Impact CFX's items in front of this PC's player, to try physics props anywhere, the hub included (the
/// user, 2026-11-21: "그 impact cfx에 있는 아이템들도 다 허브에 테스팅용으로 넣어 줘. 아님 콘솔로 생성할 수 있게"):
/// /prop lists them (PropShelf), /prop &lt;name or number&gt; [count] makes them, /prop clear takes away what it made.
/// A loose one (with a Rigidbody) is a PhysicsProp: kick, shove or shoot it into a zombie and the strike is yours
/// (PropStrike, the breaking block under the crosshair on a kill). What it makes is this PC's alone, not the room's.
/// Registered in GameCommands, so the chat's console channel has it too.
/// </summary>
public static class PropCommands
{
    // The most one /prop makes, and how far ahead of the eyes and how far apart they appear (m).
    private const int MostAtOnce = 20;
    private const float Ahead = 1.6f, Apart = .45f;
    private static readonly List<GameObject> made = new List<GameObject>();

    public static void Register(ChatCommandRouter router)
    {
        router.Register("prop", "Impact CFX 물건 — 비우면 목록 · <이름 또는 번호> [개수] 눈앞에 만들기 · clear 만든 것 치우기. 차고 밀고 쏘아 좀비를 맞혀 죽이면 "
            + "물리 프롭 처치(깨지는 블록). 이 PC에만 생긴다 (PropShelf, PropStrike)", Run);
    }

    private static string Run(string args)
    {
        var shelf = PropShelf.Get();
        if (shelf == null) return "Resources/World/PropShelf가 없습니다.";
        string[] words = (args ?? "").Trim().Split(new[] { ' ' }, System.StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return List(shelf);
        if (!Application.isPlaying) return "이 명령어는 Play 중에 사용할 수 있습니다. 목록은 /prop";
        if (words[0] == "clear") return Clear();
        var prefab = shelf.Find(words[0]);
        if (prefab == null) return "'" + words[0] + "'에 맞는 물건이 하나가 아닙니다. 목록: /prop";
        int count = 1;
        if (words.Length > 1 && (!int.TryParse(words[1], out count) || count < 1)) return "사용법: /prop <이름 또는 번호> [개수] — 개수는 1 이상의 정수";
        count = Mathf.Min(count, MostAtOnce);
        var mine = PlayerRoster.Local;
        if (mine == null) return "대상 플레이어를 찾지 못했습니다.";
        var view = mine.TryGetComponent(out PlayerBody body) && body.View != null ? body.View : mine.transform;
        CollisionEffects.Ensure();
        Vector3 forward = Vector3.ProjectOnPlane(view.forward, Vector3.up);
        if (forward.sqrMagnitude < 1e-4f) forward = mine.transform.forward;
        forward.Normalize();
        Vector3 right = Vector3.Cross(Vector3.up, forward);
        Vector3 at = view.position + forward * Ahead;
        for (int i = 0; i < count; i++)
        {
            // A row across the view, a little above the eyes, so they drop and settle.
            Vector3 place = at + right * ((i - (count - 1) * .5f) * Apart) + Vector3.up * (.2f + (i % 3) * .15f);
            var item = Object.Instantiate(prefab, place, Quaternion.LookRotation(-forward));
            item.name = prefab.name;
            RuntimeSceneHierarchy.Group(item, RuntimeSceneHierarchy.World, "Props");
            if (item.TryGetComponent(out Rigidbody _) && !item.TryGetComponent(out PhysicsProp _)) item.AddComponent<PhysicsProp>();
            made.Add(item);
        }
        return prefab.name + " " + count + "개를 만들었습니다 (이 PC에만). 치우기: /prop clear";
    }

    private static string List(PropShelf shelf)
    {
        var text = new StringBuilder("Impact CFX 물건 (/prop <이름 또는 번호> [개수]):");
        var items = shelf.Items;
        for (int i = 0; i < items.Count; i++)
        {
            if (items[i] == null) continue;
            bool loose = items[i].TryGetComponent(out Rigidbody _);
            text.Append('\n').Append(i + 1).Append(". ").Append(items[i].name).Append(loose ? "" : " (고정)");
        }
        return text.ToString();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession() => made.Clear();

    private static string Clear()
    {
        int count = 0;
        foreach (var item in made) if (item != null) { Object.Destroy(item); count++; }
        made.Clear();
        return count + "개를 치웠습니다.";
    }
}
