using UnityEngine;

/// <summary>
/// One thing on a campaign map that players work (MapGimmicks): one byte of state, the same on every PC. The acting PC
/// works it on [F] (Use, which returns the state it comes to) or by a shot; every PC then shows the state (Show), the host
/// doing the zombies' part. Its Tick runs every frame on every PC, and Seed when the run's seed is known (where it lies
/// this run). The builder makes each one and lists it on the map's MapGimmicks.
/// </summary>
public abstract class GimmickSwitch : MonoBehaviour
{
    [SerializeField, Min(.3f), Tooltip("How near (m, along the floor) a player must stand to its handle to work it with [F].")]
    protected float reach = 1.8f;

    /// <summary>Its number on the map's list, the same on every PC.</summary>
    internal int Index { get; set; } = -1;

    /// <summary>Its state on this PC (MapGimmicks keeps it; Show is told when it changes).</summary>
    internal byte State { get; set; }

    internal float Reach => reach;

    /// <summary>Where [F] is measured from and looked at.</summary>
    internal virtual Vector3 Handle => transform.position;

    /// <summary>How long [F] must be held to work it (s); 0 for a press.</summary>
    internal virtual float HoldSeconds => 0f;

    /// <summary>What [F] would do for this player now, as the prompt says it; null when it offers nothing.</summary>
    internal virtual string Offer(PlayerMovement mine) => null;

    /// <summary>This PC's player worked it: the state it comes to, or -1 for no change. Called on the acting PC only.</summary>
    internal virtual int Use(PlayerMovement mine) => -1;

    /// <summary>After Use's state has gone out (what follows it, such as the campaign step it finishes): the acting PC.</summary>
    internal virtual void Used(PlayerMovement mine) { }

    /// <summary>
    /// Its state changed on this PC. Fresh: it just happened (sounds, the moment); otherwise this PC is only catching up
    /// (a late joiner) and it should simply look the way that state looks.
    /// </summary>
    internal virtual void Show(byte state, bool fresh) { }

    /// <summary>Every frame on every PC; host: this PC runs the zombies.</summary>
    internal virtual void Tick(float dt, bool host) { }

    /// <summary>The run's seed is known on this PC (MapGimmicks.Roll).</summary>
    internal virtual void Seed(ushort seed) { }

    /// <summary>The host breaking a tie: whether a guest's word moving it from <paramref name="now"/> may stand.</summary>
    internal virtual bool Accepts(byte now, byte proposed) => true;

    /// <summary>The host: the state a guest's accepted word brings it to from <paramref name="now"/>; the word itself, or
    /// both together for a switch whose state is parts that add up (GimmickTwoHands: each lever a bit).</summary>
    internal virtual byte Merge(byte now, byte proposed) => proposed;

    /// <summary>Sets its state as the acting PC.</summary>
    protected void Set(byte state) => MapGimmicks.Set(Index, state);

    /// <summary>The PC's own player, alive and in play.</summary>
    protected static PlayerMovement Mine
    {
        get
        {
            var mine = PlayerRoster.Local;
            return mine != null && mine.IsInitialized && !mine.IsDead ? mine : null;
        }
    }
}
