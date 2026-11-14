using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// A campaign map's gimmicks (the user, 2026-11-14: "맵에 기믹이 없어서 아쉬워. 페이데이나 거블파 같은 PVE 게임들 맵들
/// 사람들이 재밌다고 하는 미션 보면서 맵 아예 개편해서 재밌는 기믹 넣어 줘"; the cards they kept on the gimmick page): the
/// things on the way that players work with [F] or a shot. A TV that draws zombies off (GimmickLure), a breaker that lights a
/// building (GimmickBreaker), a key and the note that says where it is (GimmickItem, GimmickClue), the door it opens or that
/// can be smashed in (GimmickLock), and the like, each a GimmickSwitch with one byte of state.
/// One per campaign scene, put in by the builder with every switch of the scene in a fixed order (switches), so a switch
/// is the same number on every PC. The acting PC sets a state at once and tells the host, which takes it as it is (the
/// network rule, 2026-11-14: the actor decides, the host only breaks ties: a key taken twice stays taken) and tells
/// everyone; each PC then shows it (GimmickSwitch.Show). The host also picks the run's seed as the chapter starts, which
/// decides the map's variants on every PC alike (GimmickVariant: which walls are open and which flats are shut this run;
/// where the keys lie: GimmickItem), and shares it with every state every couple of seconds, for anyone who joined late
/// or missed a word (MapGimmicks.Sharing.cs).
/// [F] goes through CampaignRun's prompt, after a step's button and a carried can (Prompt): the switch in reach that the
/// view looks most nearly at, and one that wants [F] held (HoldSeconds) shows how far along the hold is.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.Campaign + 1)]
public sealed partial class MapGimmicks : MonoBehaviour
{
    // Off the view's middle by more than this (degrees), a switch further than a step away doesn't take [F].
    private const float FacingAngle = 65f, CloseEnough = 1.1f, HighReach = 1.6f;
    // A hold's bar in the prompt: this many cells.
    private const int HoldCells = 10;

    [SerializeField, Tooltip("Every switch of the map in a fixed order, the same on every PC (the builder fills it).")]
    private GimmickSwitch[] switches = new GimmickSwitch[0];

    private static MapGimmicks active;
    private static readonly List<GimmickSwitch> candidates = new List<GimmickSwitch>();
    private byte[] states = new byte[0];
    private InputAction interact;
    // The switch whose [F] is being held, and for how long (s).
    private int holding = -1;
    private float held;

    /// <summary>This map's gimmicks, or null on a map with none.</summary>
    internal static MapGimmicks Active => active;

    /// <summary>The host's word decides (or there is no room).</summary>
    internal static bool IsHost
    {
        get
        {
            var network = GameSession.Network;
            return network == null || !network.IsListening || network.IsServer;
        }
    }

    /// <summary>The map's switches in their fixed order.</summary>
    internal IReadOnlyList<GimmickSwitch> Switches => switches;

    private void Awake()
    {
        active = this;
        if (switches == null) switches = new GimmickSwitch[0];
        states = new byte[switches.Length];
        heldBack = new float[switches.Length];
        for (int i = 0; i < switches.Length; i++)
            if (switches[i] != null) switches[i].Index = i;
    }

    private void Start()
    {
        interact = InputSystem.actions != null ? InputSystem.actions.FindAction("Player/Interact", false) : null;
        Listen();
        if (IsHost) PickSeed();
    }

    private void OnDestroy()
    {
        StopListening();
        if (active == this) active = null;
    }

    private void Update()
    {
        float dt = Time.deltaTime;
        bool host = IsHost;
        foreach (var each in switches) if (each != null) each.Tick(dt, host);
        if (host) Share();
    }

    /// <summary>
    /// The acting PC: <paramref name="index"/>'s state is <paramref name="state"/> now. It shows here at once and goes to the
    /// host, which passes it on (or, on the host, straight to everyone).
    /// </summary>
    internal static void Set(int index, byte state)
    {
        if (active == null || index < 0 || index >= active.switches.Length) return;
        active.Apply(index, state, true);
        if (IsHost) active.SendState(index, state);
        else active.Ask(index, state);
    }

    /// <summary>A switch's state on this PC.</summary>
    internal static byte StateOf(int index)
        => active != null && index >= 0 && index < active.states.Length ? active.states[index] : (byte)0;

    // Takes a state on this PC: the switch shows it, with its sounds when it is news (fresh), quietly when it is only
    // catching up (a late joiner's first snapshot).
    private void Apply(int index, byte state, bool fresh)
    {
        if (states[index] == state) return;
        states[index] = state;
        var target = switches[index];
        if (target == null) return;
        target.State = state;
        target.Show(state, fresh);
    }

    /// <summary>
    /// The team holds the item called <paramref name="item"/> (a key, a keycard, a code read off a note, a gas can): someone
    /// has picked it up and it isn't used up. Items are the team's, not one player's: whoever is at the door can use it.
    /// </summary>
    internal static bool Has(string item)
    {
        if (active == null || string.IsNullOrEmpty(item)) return false;
        foreach (var each in active.switches)
            if (each is GimmickItem found && found.Id == item && found.Held) return true;
        return false;
    }

    /// <summary>The team's <paramref name="item"/> is used up (the gas can let into a vent): the acting PC.</summary>
    internal static void UseUp(string item)
    {
        if (active == null || string.IsNullOrEmpty(item)) return;
        foreach (var each in active.switches)
            if (each is GimmickItem found && found.Id == item && found.Held) { found.UseUp(); return; }
    }

    /// <summary>
    /// The item called <paramref name="item"/>, for where it lies (a clue's mark); null when the map has none.
    /// </summary>
    internal static GimmickItem ItemCalled(string item)
    {
        if (active == null || string.IsNullOrEmpty(item)) return null;
        foreach (var each in active.switches)
            if (each is GimmickItem found && found.Id == item) return found;
        return null;
    }

    /// <summary>
    /// The circuit called <paramref name="circuit"/> has power: one of its breakers is up. A circuit no breaker serves is
    /// always live, so a map without the breaker still works.
    /// </summary>
    internal static bool Powered(string circuit)
    {
        if (active == null || string.IsNullOrEmpty(circuit)) return true;
        bool served = false;
        foreach (var each in active.switches)
        {
            if (!(each is GimmickBreaker breaker) || breaker.Circuit != circuit) continue;
            served = true;
            if (breaker.On) return true;
        }
        return !served;
    }

    /// <summary>
    /// CampaignRun's [F]: the words for the switch this player can work now, or null. A press works it; one that wants
    /// [F] held fills a bar while it is held and works it when the bar is full, and letting go starts it over.
    /// </summary>
    internal static string Prompt(PlayerMovement mine, bool able, bool pressed)
    {
        if (active == null || !able || mine == null) { if (active != null) active.holding = -1; return null; }
        return active.Offer(mine, pressed);
    }

    private string Offer(PlayerMovement mine, bool pressed)
    {
        var view = mine.TryGetComponent(out PlayerBody body) ? body.View : null;
        Vector3 eye = view != null ? view.position : mine.transform.position + Vector3.up * 1.6f;
        Vector3 look = view != null ? view.forward : mine.transform.forward;
        candidates.Clear();
        foreach (var each in switches)
        {
            if (each == null || !each.isActiveAndEnabled) continue;
            Vector3 handle = each.Handle;
            Vector3 flat = handle - mine.transform.position;
            float rise = flat.y;
            flat.y = 0f;
            if (flat.sqrMagnitude > each.Reach * each.Reach || rise < -.8f || rise > HighReach + 1f) continue;
            if (flat.magnitude > CloseEnough && Vector3.Angle(look, handle - eye) > FacingAngle) continue;
            candidates.Add(each);
        }
        // The one looked at most nearly first.
        candidates.Sort((a, b) => Vector3.Angle(look, a.Handle - eye).CompareTo(Vector3.Angle(look, b.Handle - eye)));
        foreach (var each in candidates)
        {
            string words = each.Offer(mine);
            if (string.IsNullOrEmpty(words)) continue;
            candidates.Clear();
            return Work(each, words, mine, pressed);
        }
        candidates.Clear();
        holding = -1;
        return null;
    }

    // Works a switch on [F]: at once, or once [F] has been held long enough.
    private string Work(GimmickSwitch target, string words, PlayerMovement mine, bool pressed)
    {
        float hold = target.HoldSeconds;
        if (hold <= 0f)
        {
            holding = -1;
            if (pressed) Use(target, mine);
            return words;
        }
        if (pressed) { holding = target.Index; held = 0f; }
        bool down = interact != null && interact.IsPressed() && InputFocus.TakesPlayInput();
        if (holding != target.Index || !down)
        {
            holding = -1;
            return Texts.Format("gimmick.prompt.hold", words);
        }
        held += Time.deltaTime;
        if (held >= hold)
        {
            holding = -1;
            Use(target, mine);
            return words;
        }
        int filled = Mathf.Clamp(Mathf.FloorToInt(held / hold * HoldCells), 0, HoldCells);
        return words + "  " + new string('■', filled) + new string('□', HoldCells - filled);
    }

    private static void Use(GimmickSwitch target, PlayerMovement mine)
    {
        int next = target.Use(mine);
        if (next < 0 || next > byte.MaxValue) return;
        Set(target.Index, (byte)next);
        target.Used(mine);
    }
}
