using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Backlog 722: the dead watch the living until they come back. For a few seconds after death the view stays in the
/// fallen body's eyes (PlayerRagdoll). Then, or at once on a click, it follows a teammate from behind and over the
/// shoulder, turning with their yaw and part of their pitch, so it sees where they go and what they aim at. Left and right
/// click go to the next and previous teammate; Space swaps to a free camera that flies where it looks, through walls like
/// Source's roaming spectator (WASD, Shift for speed), and back. A teammate who dies is watched a moment longer, then the
/// next one; with nobody left on their feet the free camera carries on from where the view is.
/// Only this PC's own player, and only in a room: alone, death is the wipe (PlayerLife). It lets go as soon as they live
/// again, and the body puts the camera back on its eyes that same frame. Runs after the ragdoll's eye ride
/// (FrameOrder.Spectate). Numbers: Tables/Spectate. What the screen says: PlayerLifeHUD.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.Spectate)]
public sealed class PlayerSpectate : MonoBehaviour
{
    public const string TableName = "Spectate", TableRow = "Default";
    public enum Mode { None, Follow, Free }

    private const string Enemies = "Enemy";
    // The camera's size against walls, the gap it keeps from them, and the least it backs off the watched eyes (m).
    private const float PitchLimit = 89f, CameraRadius = .2f, WallGap = .05f, ClosestToEyes = .3f;
    // How quickly the point it orbits catches up with the watched eyes (1/s): steadies the head's bob, keeps up with a vault.
    private const float PivotRate = 20f;
    private static readonly RaycastHit[] hits = new RaycastHit[16];

    [SerializeField, Min(0f), Tooltip("How long the view stays in the fallen body's eyes before following someone (s). A click cuts it short.")]
    private float deathViewSeconds = 3f;
    [SerializeField, Min(.5f), Tooltip("How far behind the watched player's eyes the camera sits (m).")]
    private float followDistance = 2.6f;
    [SerializeField, Tooltip("How far above their eyes (m).")]
    private float followHeight = .35f;
    [SerializeField, Tooltip("How far to their right, over the shoulder (m). 0 is straight behind.")]
    private float followSide = .45f;
    [SerializeField, Range(0f, 1f), Tooltip("Share of their up/down look the camera takes.")]
    private float followPitchShare = .6f;
    [SerializeField, Min(0f), Tooltip("How quickly the camera turns after their look (1/s). 0 snaps.")]
    private float followRate = 10f;
    [SerializeField, Min(0f), Tooltip("How long a watched player who dies stays watched before the next one (s).")]
    private float lostTargetSeconds = 2f;
    [SerializeField, Min(0f), Tooltip("Free camera speed (m/s).")]
    private float freeSpeed = 6f;
    [SerializeField, Min(0f), Tooltip("Free camera speed with Sprint held (m/s).")]
    private float freeFastSpeed = 18f;
    [SerializeField, Min(0f), Tooltip("Free camera mouse sensitivity before the Esc menu's own (PlayerLook's base).")]
    private float lookSensitivity = .1f;

    private readonly List<PlayerMovement> watchable = new List<PlayerMovement>();
    private PlayerMovement player, target, pivotOf;
    private PlayerBody body, targetBody;
    private Transform view, targetLook;
    private InputActionMap combat;
    private InputAction next, previous, look, move, fast, swap;
    private Vector3 position, pivot;
    private float yaw, pitch, diedAt, targetLostAt = -1f;
    private bool dead;

    /// <summary>What the view is doing: None while alive or still in the fallen body's eyes.</summary>
    public Mode Current { get; private set; }
    /// <summary>Who is being watched, or null.</summary>
    public PlayerMovement Target => Current == Mode.Follow ? target : null;
    /// <summary>Dead in a room and still in the fallen body's eyes: a click starts watching.</summary>
    public bool Waiting => dead && Current == Mode.None;

    private void Awake()
    {
        player = GetComponent<PlayerMovement>();
        body = GetComponent<PlayerBody>();
    }

    private void OnEnable()
    {
        var actions = InputSystem.actions;
        if (actions == null) return;
        look = actions.FindAction("Player/Look", false);
        move = actions.FindAction("Player/Move", false);
        fast = actions.FindAction("Player/Sprint", false);
        swap = actions.FindAction("Player/Jump", false);
        // Its own copy of the combat buttons, as PlayerWeaponInput keeps one. The dead don't fire, so nothing else takes them.
        // KeyBindings.Copy keeps it on the player's keys, changed in the key window even while spectating.
        var map = actions.FindActionMap("Combat", false);
        if (map == null) return;
        combat = KeyBindings.Copy(map);
        next = combat.FindAction("Fire", false);
        previous = combat.FindAction("Guard", false);
        combat.Enable();
    }

    private void OnDisable()
    {
        Stop();
        if (combat == null) return;
        KeyBindings.Forget(combat);
        combat.Disable();
        combat.Dispose();
        combat = null;
        next = previous = null;
    }

    private void LateUpdate()
    {
        if (!(GameSession.IsOnline && player.IsInitialized && player.IsDead))
        {
            if (dead) Stop();
            return;
        }
        if (!dead) { dead = true; diedAt = Time.time; }
        if (view == null) view = body != null ? body.View : null;
        if (view == null) return;
        bool controls = InputFocus.TakesInput();
        bool forward = controls && InputFocus.Pressed(next), back = controls && InputFocus.Pressed(previous), swapped = controls && InputFocus.Pressed(swap);
        if (Current == Mode.None)
        {
            if (!forward && !back && !swapped && Time.time - diedAt < deathViewSeconds) return;
            // The press that ends the wait only starts watching: Space starts free, a click with the nearest teammate.
            Begin(swapped);
        }
        else if (swapped) Swap();
        else if (forward || back) Cycle(forward ? 1 : -1);
        if (Current == Mode.Follow) KeepTarget();
        float dt = Time.deltaTime;
        if (Current == Mode.Follow) Follow(dt);
        else Fly(controls, dt);
        view.SetPositionAndRotation(position, Quaternion.Euler(pitch, yaw, 0f));
    }

    // From where the fallen eyes are and look, so the free camera starts there and following swings round from there.
    private void Begin(bool free)
    {
        position = view.position;
        Vector3 angles = view.rotation.eulerAngles;
        yaw = angles.y;
        pitch = Mathf.Clamp(Mathf.DeltaAngle(0f, angles.x), -PitchLimit, PitchLimit);
        if (body != null) body.ShowFromOutside(true);
        var nearest = free ? null : Nearest();
        if (nearest != null) Watch(nearest);
        else GoFree();
    }

    private void Stop()
    {
        dead = false;
        Current = Mode.None;
        target = pivotOf = null;
        targetBody = null;
        targetLook = null;
        targetLostAt = -1f;
        if (body != null) body.ShowFromOutside(false);
    }

    private void Watch(PlayerMovement who)
    {
        target = who;
        targetBody = who.GetComponent<PlayerBody>();
        targetLook = who.transform.Find("CrouchPivot/BobPivot/LookPivot");
        if (targetLook == null) targetLook = who.transform.Find("CrouchPivot/LookPivot");
        targetLostAt = -1f;
        Current = Mode.Follow;
    }

    // The one watched is kept for Space to come back to.
    private void GoFree() => Current = Mode.Free;

    private void Swap()
    {
        if (Current == Mode.Follow) GoFree();
        else if (target != null && Watchable().Contains(target)) Watch(target);
        else Cycle(1);
    }

    // The next or previous teammate on their feet, round the list; from the free camera a click goes to one. Nobody: free.
    private void Cycle(int step)
    {
        var list = Watchable();
        if (list.Count == 0) { GoFree(); return; }
        int at = target != null ? list.IndexOf(target) : -1;
        int index = at < 0 ? (step > 0 ? 0 : list.Count - 1) : ((at + step) % list.Count + list.Count) % list.Count;
        Watch(list[index]);
    }

    // Someone who leaves the room is let go at once; someone who dies is watched a moment longer as they fall.
    private void KeepTarget()
    {
        if (target != null && !target.IsDead) { targetLostAt = -1f; return; }
        if (target != null)
        {
            if (targetLostAt < 0f) targetLostAt = Time.time;
            if (Time.time - targetLostAt < lostTargetSeconds) return;
        }
        Cycle(1);
    }

    private List<PlayerMovement> Watchable()
    {
        watchable.Clear();
        foreach (var other in PlayerRoster.All)
            if (other != null && other != player && other.IsInitialized && !other.IsDead) watchable.Add(other);
        return watchable;
    }

    private PlayerMovement Nearest()
    {
        PlayerMovement nearest = null;
        float best = float.PositiveInfinity;
        foreach (var other in Watchable())
        {
            float distance = (other.transform.position - player.transform.position).sqrMagnitude;
            if (distance < best) { best = distance; nearest = other; }
        }
        return nearest;
    }

    // Behind their eyes and over the shoulder, turning after their look. A new target's eyes are taken at once, and the
    // turn swings round from where the camera faced.
    private void Follow(float dt)
    {
        Vector3 eyes = targetBody != null ? targetBody.EyePosition : target.transform.position + Vector3.up * 1.6f;
        pivot = pivotOf == target ? Vector3.Lerp(pivot, eyes, Ease.Share(PivotRate, dt)) : eyes;
        pivotOf = target;
        float turn = Ease.Share(followRate, dt);
        float theirPitch = targetLook != null ? Mathf.DeltaAngle(0f, targetLook.localEulerAngles.x) : 0f;
        yaw = Mathf.LerpAngle(yaw, target.transform.eulerAngles.y, turn);
        pitch = Mathf.Lerp(pitch, theirPitch * followPitchShare, turn);
        Vector3 wanted = pivot + Quaternion.Euler(pitch, yaw, 0f) * new Vector3(followSide, followHeight, -followDistance);
        position = ShortOfWalls(pivot, wanted);
    }

    // Flies where it looks, through anything, as the mouse look turns (sensitivity and Y inversion from the Esc menu).
    private void Fly(bool controls, float dt)
    {
        pivotOf = null;
        if (!controls) return;
        var settings = GameSettings.Current;
        Vector2 turn = look != null ? look.ReadValue<Vector2>() * (lookSensitivity * settings.mouseSensitivity) : Vector2.zero;
        yaw += turn.x;
        pitch = Mathf.Clamp(pitch + turn.y * (settings.invertY ? 1f : -1f), -PitchLimit, PitchLimit);
        Vector2 input = move != null ? Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1f) : Vector2.zero;
        float speed = fast != null && fast.IsPressed() ? freeFastSpeed : freeSpeed;
        position += Quaternion.Euler(pitch, yaw, 0f) * new Vector3(input.x, 0f, input.y) * (speed * dt);
    }

    // Walls between the watched eyes and the camera bring it in; people, corpses and enemies don't.
    private Vector3 ShortOfWalls(Vector3 from, Vector3 to)
    {
        Vector3 path = to - from;
        float reach = path.magnitude;
        if (reach < .001f) return to;
        Vector3 way = path / reach;
        int solid = player.groundLayers & ~LayerMask.GetMask(Enemies) & ~(1 << gameObject.layer);
        int count = Physics.SphereCastNonAlloc(from, CameraRadius, way, hits, reach, solid, QueryTriggerInteraction.Ignore);
        float nearest = reach;
        for (int i = 0; i < count; i++)
        {
            // Already touching at the start is the watched body itself or someone beside it, never the wall behind.
            if (hits[i].distance <= 0f || hits[i].collider.GetComponentInParent<PlayerMovement>() != null) continue;
            nearest = Mathf.Min(nearest, hits[i].distance);
        }
        return nearest >= reach ? to : from + way * Mathf.Max(Mathf.Min(ClosestToEyes, reach), nearest - WallGap);
    }
}
