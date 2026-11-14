using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// A zombie taken into a Schießhund's showdown (HoundShowdown), on the PC that runs it: the host, or a lone PC. The
/// hound's own PC moves it there and back (ZombieEffects.Move, the zombie session's door, called once on that PC). This
/// only readies the arena for it and keeps it on its hound while the showdown lasts:
/// - the arena's walk-mesh is laid for its kind of agent before it arrives (HoundArena.Walkable), so it fights there on
///   its own legs and with its own brain, as on any map;
/// - its pen (EnemyTargeting.Pen, as the zombie session's note offers it) lets nobody but its hound be its quarry, and
///   its own pen comes back when the showdown ends (the ENFILADE tower pens zombies by floor, which down here would let
///   nobody in, the hound included);
/// - should the hound's PC leave the room before it says the showdown is over, it goes back to where it stood by itself,
///   a little after the showdown's time, by the same door.
/// Added to the zombie when first needed; it changes none of the zombie's own parts.
/// </summary>
[DisallowMultipleComponent]
public sealed class ArenaPen : MonoBehaviour
{
    // Past the showdown's end, how long it waits to hear that it is over before it goes back by itself (s).
    private const float Grace = 3f;

    private System.Predicate<PlayerMovement> before;
    private Vector3 home;
    private Quaternion homeFacing = Quaternion.identity;
    private float until;
    private bool holding;

    /// <summary>Whether this PC runs <paramref name="enemy"/> (the host's, or alone): only here is it penned.</summary>
    public static bool Runs(EnemyHealth enemy) => enemy != null && (!enemy.TryGetComponent(out EnemyNetwork network) || !network.IsCopy);

    /// <summary>
    /// The showdown begins: the arena's walk-mesh is laid for <paramref name="enemy"/> in <paramref name="arena"/>, and
    /// until it ends (or <paramref name="seconds"/> and a little more) <paramref name="hound"/> is its only quarry.
    /// <paramref name="home"/> and <paramref name="homeFacing"/>: where it stood, should it have to go back by itself.
    /// </summary>
    public static void Hold(EnemyHealth enemy, PlayerMovement hound, HoundArena arena, Vector3 home, Quaternion homeFacing, float seconds)
    {
        if (!Runs(enemy) || !enemy.IsAlive) return;
        if (arena != null) arena.Walkable(enemy.TryGetComponent(out NavMeshAgent agent) ? agent.agentTypeID : 0);
        if (!enemy.TryGetComponent(out ArenaPen pen)) pen = enemy.gameObject.AddComponent<ArenaPen>();
        pen.Keep(hound, home, homeFacing, seconds);
    }

    /// <summary>The hound's PC said the showdown is over: the zombie's own pen comes back.</summary>
    public static void Release(EnemyHealth enemy)
    {
        if (enemy != null && enemy.TryGetComponent(out ArenaPen pen)) pen.Free();
    }

    private void Keep(PlayerMovement hound, Vector3 at, Quaternion facing, float seconds)
    {
        if (!TryGetComponent(out EnemyTargeting targeting)) return;
        if (!holding) before = targeting.Pen;
        var only = hound;
        targeting.Pen = someone => someone != null && someone == only;
        home = at;
        homeFacing = facing;
        until = Time.time + Mathf.Max(.5f, seconds) + Grace;
        holding = true;
    }

    private void Update()
    {
        if (!holding || Time.time < until) return;
        // Never told it was over (the hound's PC left the room): back where it stood, if it still stands down there.
        Free();
        if (TryGetComponent(out EnemyHealth health) && health.IsAlive && transform.position.y < home.y - 50f)
            ZombieEffects.Move(health, home, homeFacing);
    }

    private void Free()
    {
        if (!holding) return;
        holding = false;
        if (TryGetComponent(out EnemyTargeting targeting)) targeting.Pen = before;
        before = null;
    }
}
