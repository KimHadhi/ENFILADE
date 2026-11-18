// dev-text: file (the console's /noclip, its answers and its readout box: a developer's tool, nothing here is shown to players)
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Console /noclip (the user, 2026-11-18: "개발자용 노클립(Noclip) 기능 구현: 맵 및 콜라이더 디버깅용 노클립 기능 추가"):
/// this PC's player leaves the world's physics and flies where the view looks, through anything, to look at a map from
/// the places play can't reach: behind a wall, under a floor, inside a prop's collision box.
/// What it is: the body has one mover, PlayerMovement. While this flies, that mover is told the body is carried
/// (PlayerMovement.Carried) and runs nothing but its posture, the capsule is off so nothing bumps into or pushes the
/// flyer, and this alone moves the transform. Nothing is laid over the movement's states and nothing fights its
/// gravity: the body has another carrier for a while, as a copy moved by the network has.
/// Keys (the player's own bindings): Move flies along the view, Jump up, Crouch down, Sprint fast, Dodge slow, the mouse
/// wheel changes the cruising speed. Numbers: Tables/Core/Probes.csv, rows Noclip.*.
/// Zombies: by default their eyes don't make the flyer out (ZombieEffects.Unseen, the rule the Ghost's card uses,
/// renewed while flying; sounds and touch still count), "/noclip seen" lets them.
/// The game takes the body back by itself when it must: death, going down or a zombie's grip end the flight where it
/// is. Turned off, the body is the game's again at that spot: in the air it falls and lands by the usual rules, inside
/// something solid the answer says what it is in.
/// The box it shows reads the collider under the crosshair and the one under the feet (name, shape, size, layer,
/// whether anything is drawn there), so a wall that is drawn but does not block shows as a ray that goes on past it,
/// and an invisible box shows as a hit with nothing drawn.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.Movement)]
public sealed class Noclip : MonoBehaviour
{
    /// <summary>Tables/Core/Probes.csv, the dev probes' numbers.</summary>
    private const string Table = "Probes";
    private const string Usage = "사용법: /noclip (켜기·끄기) · on · off · 숫자(기본 속도 m/s) · seen(좀비가 봄) · unseen(좀비 눈에 안 보임)";
    private const string Keys = "\n키: 이동 = 보는 쪽으로 날기 · 점프 위 · 앉기 아래 · 달리기 빠르게 · 회피 느리게 · 마우스 휠 = 기본 속도. /noclip을 다시 입력하면 그 자리에서 몸으로 돌아옵니다(공중이면 떨어짐).";
    // How far the readout's rays look (m), how often it is written (s), and the zombies' blind spell renewed while
    // flying: each spell outlasts the gap to the next one, so it never lapses in between.
    private const float RayReach = 400f, ReadEvery = .1f, UnseenFor = 2f, UnseenEvery = 1f;
    private const float Width = 900f, Margin = 12f, LineHeight = 20f;
    // How many of the flyer's own parts (hitboxes) a ray may have to step past before it meets the world.
    private const int OwnParts = 12;
    private static readonly RaycastHit[] hits = new RaycastHit[48];
    private static readonly Collider[] touching = new Collider[64];
    private static readonly List<Renderer> drawn = new List<Renderer>();

    private readonly StringBuilder text = new StringBuilder(512);
    private PlayerMovement player;
    private CharacterController capsule;
    private Transform view;
    private InputAction move, rise, sink, fast, slow;
    private float speed, readAt, unseenAt;
    private bool unseen, ended;
    private string readout = "";

    /// <summary>The console's /noclip.</summary>
    public static string Command(string args)
    {
        if (!Application.isPlaying) return "이 명령어는 Play 중에 사용할 수 있습니다.";
        var mine = PlayerRoster.Local;
        if (mine == null || !mine.IsInitialized) return "플레이어가 없습니다.";
        mine.TryGetComponent(out Noclip flying);
        string word = (args ?? "").Trim().ToLowerInvariant();
        switch (word)
        {
            case "": return flying != null ? flying.End("노클립을 껐습니다.") : Begin(mine, 0f, null);
            case "on": return flying != null ? "이미 켜져 있습니다. " + flying.Describe() : Begin(mine, 0f, null);
            case "off": return flying != null ? flying.End("노클립을 껐습니다.") : "노클립이 꺼져 있습니다.";
            case "seen":
            case "unseen":
                if (flying == null) return Begin(mine, 0f, word == "unseen");
                flying.unseen = word == "unseen";
                flying.unseenAt = 0f;
                return flying.Describe();
        }
        if (!TableCells.TryNumber(word, out float wanted) || float.IsNaN(wanted) || float.IsInfinity(wanted) || wanted <= 0f) return Usage;
        if (flying == null) return Begin(mine, wanted, null);
        flying.speed = wanted;
        return flying.Describe();
    }

    /// <summary>This PC's player is flying.</summary>
    public static bool Flying => PlayerRoster.Local != null && PlayerRoster.Local.Carried;

    private static string Begin(PlayerMovement mine, float wanted, bool? eyes)
    {
        if (!mine.IsLocal || mine.IsNetworkDriven) return "이 PC가 움직이는 캐릭터가 아닙니다.";
        if (mine.Carried) return "다른 것이 이미 몸을 옮기고 있습니다.";
        if (mine.IsDead || mine.IsDowned || mine.IsHeld) return "지금은 몸을 뺄 수 없습니다(사망·쓰러짐·좀비에게 잡힘).";
        var flight = mine.gameObject.AddComponent<Noclip>();
        flight.Take(mine, wanted, eyes);
        return "노클립을 켰습니다. " + flight.Describe() + Keys;
    }

    private void Take(PlayerMovement mine, float wanted, bool? eyes)
    {
        player = mine;
        capsule = mine.GetComponent<CharacterController>();
        // The view the body puts on its eyes (PlayerBody.View); a player without a visible body flies by its camera.
        view = mine.TryGetComponent(out PlayerBody body) ? body.View : null;
        if (view == null)
        {
            var camera = mine.GetComponentInChildren<Camera>();
            view = camera != null ? camera.transform : mine.transform;
        }
        var actions = InputSystem.actions;
        if (actions != null)
        {
            move = actions.FindAction("Player/Move", false);
            rise = actions.FindAction("Player/Jump", false);
            sink = actions.FindAction("Player/Crouch", false);
            fast = actions.FindAction("Player/Sprint", false);
            slow = actions.FindAction("Player/Dodge", false);
        }
        speed = wanted > 0f ? wanted : Mathf.Max(.01f, GameTables.Number(Table, "Noclip.speed", "value", 8f));
        unseen = eyes ?? GameTables.Number(Table, "Noclip.unseen", "value", 1f) >= .5f;
        // What the body was doing ends by its own rules (a ladder is let go of, a slide stops, a swing is cancelled) and
        // its speed is cleared, as at a spawn. From here on the movement runs nothing until the body is given back.
        player.ResetVelocity();
        capsule.enabled = false;
        player.Carried = true;
    }

    private void Update()
    {
        if (ended) return;
        if (player == null || capsule == null) { ended = true; Destroy(this); return; }
        // The game takes the body back when it must: the dead fall, the downed crawl, a grip holds them where they are.
        if (player.IsDead || player.IsDowned || player.IsHeld)
        {
            Debug.Log("[Noclip] " + End("게임이 몸을 가져갔습니다(사망·쓰러짐·좀비에게 잡힘): 노클립을 껐습니다."));
            return;
        }
        // A spawn, a lift or a teleport switches the capsule back on as it places the body (PlayerMovement.MoveTo): the
        // place it gave stays, the capsule goes off again.
        if (capsule.enabled) capsule.enabled = false;
        // The flight keeps its pace through slow motion; a frame that was really a pause moves it no further than the
        // longest frame the game itself allows.
        if (Time.timeScale > 0f && InputFocus.TakesInput()) Fly(Mathf.Min(Time.unscaledDeltaTime, Time.maximumDeltaTime));
        if (unseen && Time.time >= unseenAt)
        {
            unseenAt = Time.time + UnseenEvery;
            ZombieEffects.Unseen(player, UnseenFor);
        }
        if (Time.unscaledTime >= readAt)
        {
            readAt = Time.unscaledTime + ReadEvery;
            Read();
        }
    }

    // Along the view, pitch and all, as Source's noclip flies; up and down are the world's.
    private void Fly(float dt)
    {
        var mouse = Mouse.current;
        float wheel = mouse != null ? mouse.scroll.ReadValue().y : 0f;
        if (wheel != 0f)
        {
            float step = Mathf.Max(1.01f, GameTables.Number(Table, "Noclip.step", "value", 1.5f));
            speed = wheel > 0f ? speed * step : speed / step;
        }
        Vector2 keys = move != null ? Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1f) : Vector2.zero;
        float lift = (Held(rise) ? 1f : 0f) - (Held(sink) ? 1f : 0f);
        if (keys == Vector2.zero && lift == 0f) return;
        Vector3 way = Vector3.ClampMagnitude(view.forward * keys.y + view.right * keys.x + Vector3.up * lift, 1f);
        float pace = speed;
        if (Held(fast)) pace *= Mathf.Max(1f, GameTables.Number(Table, "Noclip.fast", "value", 4f));
        if (Held(slow)) pace *= Mathf.Clamp(GameTables.Number(Table, "Noclip.slow", "value", .25f), .01f, 1f);
        transform.position += way * (pace * dt);
    }

    private static bool Held(InputAction action) => action != null && action.IsPressed();

    private string End(string why)
    {
        if (ended) return why;
        ended = true;
        string inside = player != null && capsule != null ? Inside() : null;
        if (capsule != null) capsule.enabled = true;
        if (player != null)
        {
            player.Carried = false;
            // Back on its feet where it is, as a spawn puts it: standing if there is room, else crouched or prone. The
            // dead are left to the ragdoll, which has its own say over the capsule.
            if (!player.IsDead) player.ResetVelocity();
        }
        Destroy(this);
        return inside == null ? why : why + " 지금 자리는 " + inside + " 속입니다: 끼었으면 /noclip으로 다시 빠져나오세요.";
    }

    // The scene went away, or the player object did: nothing is left to give back but the flag.
    private void OnDestroy()
    {
        if (ended) return;
        ended = true;
        if (capsule != null) capsule.enabled = true;
        if (player != null) player.Carried = false;
    }

    private string Describe() => "기본 속도 " + speed.ToString("0.##") + " m/s · " + (unseen ? "좀비 눈에 안 보임(소리와 손 닿는 거리는 들킴)" : "좀비가 봄");

    // What the standing capsule would be inside of here, or null when it is free. Its own hitboxes don't count.
    private string Inside()
    {
        float radius = capsule.radius, height = Mathf.Max(capsule.height, radius * 2f);
        Vector3 feet = transform.TransformPoint(capsule.center - Vector3.up * (height * .5f));
        int count = Physics.OverlapCapsuleNonAlloc(feet + Vector3.up * radius, feet + Vector3.up * (height - radius), radius * .95f,
            touching, player.groundLayers, QueryTriggerInteraction.Ignore);
        for (int i = 0; i < count; i++)
            if (touching[i] != null && !touching[i].transform.IsChildOf(transform)) return Path(touching[i].transform);
        return null;
    }

    private void Read()
    {
        text.Clear();
        Vector3 at = transform.position;
        text.Append("노클립 · 발 자리 ").Append(at.x.ToString("0.00")).Append(", ").Append(at.y.ToString("0.00")).Append(", ")
            .Append(at.z.ToString("0.00")).Append(" · ").Append(Describe());
        string inside = Inside();
        text.Append(inside == null ? " · 여기서 끄면 설 수 있음" : " · 여기는 " + inside + " 속");
        text.Append("\n조준점: ");
        Look(view.position, view.forward);
        text.Append("\n발밑: ");
        Look(at + Vector3.up * .05f, Vector3.down);
        readout = text.ToString();
    }

    // The nearest solid along a ray that is not the flyer's own, the triggers passed on the way, and whether that
    // solid is something one can see: a hit with nothing drawn is an invisible blocker, a ray that goes on through a
    // drawn wall is a wall that does not block (from this side: a mesh collider stops rays only from its front).
    private void Look(Vector3 from, Vector3 way)
    {
        // The nearest solid by single casts, stepping past the flyer's own parts: a buffer of many hits comes back
        // unsorted and drops hits at random once it is full, so it can't say which one is nearest.
        RaycastHit hit = default;
        bool found = false;
        float gone = 0f;
        for (int i = 0; i < OwnParts && gone < RayReach; i++)
        {
            if (!Physics.Raycast(from + way * gone, way, out hit, RayReach - gone, ~0, QueryTriggerInteraction.Ignore)) break;
            if (!hit.collider.transform.IsChildOf(transform)) { found = true; break; }
            gone += hit.distance + .01f;
        }
        float reach = found ? gone + hit.distance : RayReach;
        if (!found) text.Append(RayReach.ToString("0")).Append(" m 안에 막는 것이 없음");
        else
        {
            var collider = hit.collider;
            Vector3 size = collider.bounds.size;
            collider.GetComponentsInChildren(false, drawn);
            int seen = 0;
            foreach (var renderer in drawn) if (renderer != null && renderer.enabled) seen++;
            drawn.Clear();
            text.Append(reach.ToString("0.00")).Append(" m · ").Append(Path(collider.transform)).Append(" · ").Append(Shape(collider))
                .Append(' ').Append(size.x.ToString("0.00")).Append("×").Append(size.y.ToString("0.00")).Append("×").Append(size.z.ToString("0.00"))
                .Append(" m · 레이어 ").Append(LayerMask.LayerToName(collider.gameObject.layer))
                .Append(seen > 0 ? "" : " · 그려지는 것 없음(충돌만)");
        }
        // The triggers on the way there (a full buffer means "at least").
        int count = Physics.RaycastNonAlloc(from, way, hits, reach, ~0, QueryTriggerInteraction.Collide), triggers = 0;
        for (int i = 0; i < count; i++)
            if (hits[i].collider != null && hits[i].collider.isTrigger && !hits[i].collider.transform.IsChildOf(transform)) triggers++;
        if (triggers > 0) text.Append(" · 그 앞에 트리거 ").Append(triggers).Append(count == hits.Length ? "개 이상" : "개");
    }

    private static string Shape(Collider collider) => collider switch
    {
        BoxCollider => "상자",
        SphereCollider => "구",
        CapsuleCollider => "캡슐",
        MeshCollider mesh => mesh.convex ? "볼록 메시" : "메시",
        CharacterController => "캐릭터 캡슐",
        TerrainCollider => "지형",
        _ => collider.GetType().Name,
    };

    // The object's own name under its two nearest parents: enough to find it in the hierarchy.
    private static string Path(Transform part)
    {
        string path = part.name;
        var parent = part.parent;
        for (int i = 0; i < 2 && parent != null; i++, parent = parent.parent) path = parent.name + "/" + path;
        return path;
    }

    private void OnGUI()
    {
        if (ended || string.IsNullOrEmpty(readout)) return;
        GUILayout.BeginArea(new Rect(Margin, Screen.height * .12f, Width, LineHeight * 4.5f), GUI.skin.box);
        foreach (string line in readout.Split('\n')) GUILayout.Label(line);
        GUILayout.EndArea();
    }
}
