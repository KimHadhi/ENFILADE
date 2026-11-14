using UnityEngine;

/// <summary>
/// A block's lift (the map gimmick card "멈춘 엘리베이터 통로", with the user's note: "그냥 라우드로 갈 거면 엘리베이터 타고
/// 가는 거 좋다. 그래서 엘리베이터로 그 층 가면 띵 소리 나면서 그 층은 깨는 걸로 해서 빠르게 가는 대신 리스크 지는 라우드 플레이에
/// 좋겠네"): with its circuit powered (GimmickBreaker: the roof's machine room), [F] in the car takes it down to the ground
/// floor (or back up to the top), and [F] at a floor's call button brings it there. Wherever it opens it chimes, and
/// everything not yet awake on that floor gets up (wakeBoxes, the host's ZombieMind.WakeWithin): fast, and loud.
/// The car is built once on every floor, the same room behind each floor's doors (car: the one on the ground floor, the
/// rest up the floors' heights), so nothing rides through the shaft: the doors shut, each PC moves its own player from the
/// car they stood in to the car they arrive in (the actor moves itself), and the doors open there. States: the floor the
/// car stands at (an index of floors).
/// </summary>
public sealed class GimmickElevator : GimmickSwitch
{
    private enum Phase { Still, Closing, Riding, Opening }

    // Doors (s), and a floor's ride (s, at least one floor's worth).
    private const float DoorSeconds = .6f, PerFloor = .55f, LeastRide = 1.2f;

    [SerializeField, Tooltip("The circuit that powers it (GimmickBreaker).")]
    private string circuit = "";
    [SerializeField, Tooltip("Each floor's height (m, the car's floor there), bottom up.")]
    private float[] floors = new float[0];
    [SerializeField, Tooltip("The car's room on the bottom floor (world box): the others are the same box up the floors.")]
    private Bounds car;
    [SerializeField, Tooltip("Each floor's doors, bottom up: they slide by doorOpen while the car stands there.")]
    private Transform[] doors = new Transform[0];
    [SerializeField, Tooltip("How far the doors slide open (local).")]
    private Vector3 doorOpen = new Vector3(1.2f, 0f, 0f);
    [SerializeField, Tooltip("Each floor's call button, bottom up (where [F] is outside the car).")]
    private Transform[] calls = new Transform[0];
    [SerializeField, Tooltip("Each floor's box that wakes when the car chimes there, bottom up.")]
    private Bounds[] wakeBoxes = new Bounds[0];
    [SerializeField, Min(0), Tooltip("The floor it stands at as the map is built.")]
    private int startFloor;

    private Vector3[] shut;
    private Phase phase;
    private float phaseAt, ride;
    private int shownFloor = -1, from, to;
    private bool carried;

    private void Awake()
    {
        shut = new Vector3[doors.Length];
        for (int i = 0; i < doors.Length; i++) if (doors[i] != null) shut[i] = doors[i].localPosition;
    }

    private void Start()
    {
        if (shownFloor < 0) Stand(Mathf.Clamp(startFloor + State, 0, Mathf.Max(0, floors.Length - 1)));
    }

    // The state is the floor less where it starts, so a new map's lift is at state 0 (MapGimmicks' bytes start there).
    private int FloorOf(byte state) => Mathf.Clamp((startFloor + state) % Mathf.Max(1, floors.Length), 0, Mathf.Max(0, floors.Length - 1));

    private byte StateFor(int floor) => (byte)((floor - startFloor + floors.Length) % Mathf.Max(1, floors.Length));

    // The floor the local player is on: the nearest floor height at or below their feet.
    private int FloorNear(Vector3 at)
    {
        int best = 0;
        for (int i = 0; i < floors.Length; i++) if (at.y >= floors[i] - .5f) best = i;
        return best;
    }

    private Bounds CarAt(int floor)
    {
        var box = car;
        box.center += Vector3.up * (floors[floor] - floors[0]);
        return box;
    }

    internal override Vector3 Handle
    {
        get
        {
            var mine = Mine;
            if (mine == null || floors.Length == 0) return transform.position;
            int floor = FloorNear(mine.transform.position);
            if (CarAt(floor).Contains(mine.transform.position + Vector3.up)) return CarAt(floor).center;
            return floor < calls.Length && calls[floor] != null ? calls[floor].position : transform.position;
        }
    }

    internal override string Offer(PlayerMovement mine)
    {
        if (floors.Length < 2 || phase != Phase.Still) return null;
        if (!MapGimmicks.Powered(circuit)) return Texts.Get("gimmick.lift.dead");
        int here = FloorNear(mine.transform.position);
        bool inside = CarAt(here).Contains(mine.transform.position + Vector3.up);
        if (inside && here == shownFloor) return Texts.Get(here == 0 ? "gimmick.lift.up" : "gimmick.lift.down");
        return here == shownFloor ? null : Texts.Get("gimmick.lift.call");
    }

    internal override int Use(PlayerMovement mine)
    {
        if (floors.Length < 2 || phase != Phase.Still || !MapGimmicks.Powered(circuit)) return -1;
        int here = FloorNear(mine.transform.position);
        bool inside = CarAt(here).Contains(mine.transform.position + Vector3.up);
        int next = inside && here == shownFloor ? (here == 0 ? floors.Length - 1 : 0) : here;
        return next == shownFloor ? -1 : StateFor(next);
    }

    // A second call while it already goes somewhere waits its turn: the first word stands.
    internal override bool Accepts(byte now, byte proposed) => phase == Phase.Still;

    internal override void Show(byte state, bool fresh)
    {
        int floor = FloorOf(state);
        if (!fresh || shownFloor < 0) { Stand(floor); return; }
        if (floor == shownFloor) return;
        from = shownFloor;
        to = floor;
        ride = Mathf.Max(LeastRide, Mathf.Abs(to - from) * PerFloor);
        carried = false;
        phase = Phase.Closing;
        phaseAt = Time.time;
    }

    internal override void Tick(float dt, bool host)
    {
        if (phase == Phase.Still) return;
        float since = Time.time - phaseAt;
        switch (phase)
        {
            case Phase.Closing:
                Door(from, 1f - Mathf.Clamp01(since / DoorSeconds));
                if (since < DoorSeconds) return;
                // Shut: this PC's own player in the car rides with it (the others move their own).
                Carry();
                Next(Phase.Riding);
                return;
            case Phase.Riding:
                if (since < ride) return;
                shownFloor = to;
                Arrive(host);
                Next(Phase.Opening);
                return;
            case Phase.Opening:
                Door(to, Mathf.Clamp01(since / DoorSeconds));
                if (since >= DoorSeconds) phase = Phase.Still;
                return;
        }
    }

    private void Next(Phase now)
    {
        phase = now;
        phaseAt = Time.time;
    }

    private void Carry()
    {
        if (carried) return;
        carried = true;
        var mine = Mine;
        if (mine == null || !CarAt(from).Contains(mine.transform.position + Vector3.up)) return;
        mine.MoveTo(mine.transform.position + Vector3.up * (floors[to] - floors[from]));
    }

    // It opens at a floor: the chime, and that floor's sleepers and the rest are up (the host's part).
    private void Arrive(bool host)
    {
        Vector3 at = CarAt(to).center;
        GimmickSounds.PlayAt(GimmickSounds.Cue.Ding, at, 1f, 6f, 60f);
        if (!host) return;
        var near = PlayerRoster.Nearest(at);
        if (to < wakeBoxes.Length && wakeBoxes[to].size != Vector3.zero) ZombieMind.WakeWithin(wakeBoxes[to], near);
    }

    // Laid out with the car standing at this floor: its doors open, the rest shut.
    private void Stand(int floor)
    {
        shownFloor = floor;
        phase = Phase.Still;
        for (int i = 0; i < doors.Length; i++) Door(i, i == floor ? 1f : 0f);
    }

    private void Door(int floor, float open)
    {
        if (floor < 0 || floor >= doors.Length || doors[floor] == null) return;
        doors[floor].localPosition = shut[floor] + doorOpen * open;
    }
}
