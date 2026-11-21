using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What a Ghost sees through walls, drawn only where something stands in front of a zombie: a soft white fill over its
/// hidden parts and a white band round their shape, so the body reads as itself (EnemyOutlines, as pings and holders are):
/// <list type="bullet">
/// <item>Its own wall sight, the class's base (the user, 2026-11-14: "고스트는 패시브로 주변 근처에 있는 적을 벽을 뚫고 볼 수
/// 있어"): on the Ghost's own PC only, every living zombie within its reach (GhostCards.SightReach: ClassAbilities
/// Ghost.wallSight, farther with the 정찰 card).</item>
/// <item>A Ghost's ultimate (GhostNet): on every PC, for its seconds, every zombie within its reach of that Ghost, coming
/// in as a wave runs out from where it went off (Ghost.phantomWave), so the whole team sees what the Ghost sees.</item>
/// </list>
/// A ping's, a bounty's, a holder's or a wave's last enemies' look wins where both would draw (EnemyOutlines: the Ghost's is
/// the weakest). Each fades in and out on its own, never blinking, and goes the moment its zombie dies (its body goes to
/// its corpse). Nothing is copied from the zombie: until 2026-11-21 copies of its renderers were made the moment it came
/// in reach, and one that came in reach as it appeared, still in the enemy's capsule shell before its body was put on,
/// kept a copy of that capsule for as long as it stayed near: the white pill the user saw on a pinged reaper. One of
/// these is made on first need and lives with the scene. Numbers: Tables/ClassAbilities Ghost.wallSight, sightAlpha,
/// phantomWave.
/// </summary>
[DisallowMultipleComponent]
public sealed class GhostSight : MonoBehaviour
{
    private const string Row = PlayerClass.GhostRow;
    // How long a silhouette takes to come in and to go (s); how much farther than its reach one already shown keeps showing
    // (m), so one pacing at the edge doesn't come and go; how long the Ghost's own reach holds without being said again (s).
    private const float FadeIn = .3f, FadeOut = .45f, Linger = 1f, OwnHold = .5f;

    private sealed class Shown
    {
        // How strong it is wanted now (0: not), the last strength it was wanted at, and how far in it has faded (0 to 1).
        public float Want, Level, Faded;
    }

    private struct Veil
    {
        public PlayerMovement Ghost;
        public Vector3 From;
        public float Since, Until, Reach;
    }

    private static GhostSight instance;
    private static float ownReach, ownSaidAt = -99f;
    private readonly Dictionary<EnemyHealth, Shown> shown = new Dictionary<EnemyHealth, Shown>();
    private readonly List<EnemyHealth> gone = new List<EnemyHealth>();
    private readonly List<Veil> veils = new List<Veil>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        ownReach = 0f;
        ownSaidAt = -99f;
    }

    /// <summary>This PC's own Ghost sees through walls this far now (m; 0: it doesn't). GhostCards says so every frame.</summary>
    public static void Own(float reach)
    {
        ownReach = Mathf.Max(0f, reach);
        ownSaidAt = Time.time;
        if (ownReach > 0f) Ensure();
    }

    /// <summary>
    /// Every PC (GhostNet): <paramref name="ghost"/>'s ultimate went off at <paramref name="from"/>. For
    /// <paramref name="seconds"/> every zombie within <paramref name="reach"/> of the Ghost shows through walls here, those
    /// already near it coming in as a wave runs out from there.
    /// </summary>
    public static void Phantom(PlayerMovement ghost, Vector3 from, float seconds, float reach)
    {
        if (!Application.isPlaying || seconds <= 0f || reach <= 0f) return;
        Ensure().veils.Add(new Veil { Ghost = ghost, From = from, Since = Time.time, Until = Time.time + seconds, Reach = reach });
    }

    /// <summary>
    /// The zombie the crosshair is on among those this PC sees through walls, or null: a Ghost's 정찰 card pings it with F
    /// (PlayerPing asks when its own look met a wall first). Any part of its body will do, give or take Ghost.reconAim.
    /// <paramref name="at"/> is the spot on the look nearest it, where the hand points.
    /// </summary>
    public static Component AimedThroughWall(PlayerMovement player, out Vector3 at)
    {
        at = default;
        if (instance == null || player == null || !GhostCards.ReconPings(player)) return null;
        var eyes = PlayerView.EyesOf(player);
        if (eyes == null) return null;
        float give = Mathf.Max(.05f, PlayerClass.Ability(Row, "reconAim", .5f)), best = float.PositiveInfinity, bestAlong = 0f;
        Component aimed = null;
        foreach (var pair in instance.shown)
        {
            var enemy = pair.Key;
            if (enemy == null || !enemy.IsAlive || pair.Value.Want <= 0f) continue;
            float miss = Miss(eyes.position, eyes.forward, enemy, out float along);
            if (miss > give || miss >= best) continue;
            best = miss;
            bestAlong = along;
            aimed = enemy;
        }
        if (aimed != null) at = eyes.position + eyes.forward * bestAlong;
        return aimed;
    }

    /// <summary>The middle of a foe's body: its own collider's (a zombie's capsule), else a man's middle over its feet.</summary>
    internal static Vector3 Middle(Component foe)
    {
        if (foe == null) return Vector3.zero;
        return foe.TryGetComponent(out Collider own) && own.enabled && !own.isTrigger ? own.bounds.center : foe.transform.position + Vector3.up;
    }

    /// <summary>
    /// Where a foe stands: the bottom of its own collider (a zombie's capsule; the zombie itself stands a metre up, at its
    /// walk-mesh agent's base offset), else its position.
    /// </summary>
    internal static Vector3 Feet(Component foe)
    {
        if (foe == null) return Vector3.zero;
        if (!foe.TryGetComponent(out Collider own) || !own.enabled || own.isTrigger) return foe.transform.position;
        var bounds = own.bounds;
        return new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
    }

    // How far the look passes from the zombie's body, its upright line from its feet to its head (m), and how far along the
    // look that is: aiming at its head or its legs counts as much as at its middle.
    private static float Miss(Vector3 eye, Vector3 look, EnemyHealth enemy, out float along)
    {
        Vector3 feet = Feet(enemy), top = CombatFoe.Top(enemy);
        Vector2 flat = new Vector2(look.x, look.z), toward = new Vector2(feet.x - eye.x, feet.z - eye.z);
        float flatSquared = flat.sqrMagnitude;
        along = flatSquared > 1e-6f ? Vector2.Dot(toward, flat) / flatSquared : 0f;
        if (along <= .5f) return float.PositiveInfinity;
        Vector3 point = eye + look * along;
        float across = new Vector2(point.x - feet.x, point.z - feet.z).magnitude;
        float above = point.y > top.y ? point.y - top.y : point.y < feet.y ? feet.y - point.y : 0f;
        return Mathf.Sqrt(across * across + above * above);
    }

    private static GhostSight Ensure()
    {
        if (instance != null) return instance;
        instance = new GameObject("Ghost Sight").AddComponent<GhostSight>();
        RuntimeSceneHierarchy.Group(instance.gameObject, RuntimeSceneHierarchy.Systems, "Combat");
        return instance;
    }

    private void OnDestroy()
    {
        gone.Clear();
        gone.AddRange(shown.Keys);
        foreach (var enemy in gone) Hide(enemy);
        gone.Clear();
        if (instance == this) instance = null;
    }

    private void Update()
    {
        float now = Time.time;
        for (int i = veils.Count - 1; i >= 0; i--)
            if (now >= veils[i].Until) veils.RemoveAt(i);
        var local = PlayerRoster.Local;
        float own = now - ownSaidAt <= OwnHold && local != null && !local.IsDead ? ownReach : 0f;
        // Nothing to show and nothing showing: a player of another class whose friend's ultimate is long over.
        if (own <= 0f && veils.Count == 0 && shown.Count == 0) return;
        float strength = Mathf.Clamp01(PlayerClass.Ability(Row, "sightAlpha", .3f)), wave = Mathf.Max(1f, PlayerClass.Ability(Row, "phantomWave", 60f));
        foreach (var entry in shown.Values) entry.Want = 0f;
        if (own > 0f || veils.Count > 0)
            foreach (var enemy in EnemyHealth.Living)
            {
                if (enemy == null || !enemy.IsAlive) continue;
                shown.TryGetValue(enemy, out var entry);
                float slack = entry != null ? Linger : 0f;
                Vector3 at = enemy.transform.position;
                bool wanted = own > 0f && Within(at, local.transform.position, own + slack);
                foreach (var veil in veils)
                {
                    if (wanted) break;
                    // Round the Ghost where it is now; the wave from where it went off reaches each in turn.
                    Vector3 middle = veil.Ghost != null && !veil.Ghost.IsDead ? veil.Ghost.transform.position : veil.From;
                    wanted = Within(at, middle, veil.Reach + slack) && (now - veil.Since) * wave >= Vector3.Distance(at, veil.From);
                }
                if (!wanted) continue;
                if (entry == null) entry = Show(enemy);
                entry.Want = strength;
            }
        Fade(Time.deltaTime);
    }

    private static bool Within(Vector3 a, Vector3 b, float reach) => (a - b).sqrMagnitude <= reach * reach;

    // Each in or out at its own pace, in the Ghost's white; one faded all the way out is let go.
    private void Fade(float dt)
    {
        Color tint = MomentLook.ColorOf(PlayerClass.Ghost);
        gone.Clear();
        foreach (var pair in shown)
        {
            var entry = pair.Value;
            if (pair.Key == null || !pair.Key.IsAlive) { gone.Add(pair.Key); continue; }
            if (entry.Want > 0f) entry.Level = entry.Want;
            entry.Faded = Mathf.MoveTowards(entry.Faded, entry.Want > 0f ? 1f : 0f, dt / (entry.Want > 0f ? FadeIn : FadeOut));
            if (entry.Faded <= 0f && entry.Want <= 0f) { gone.Add(pair.Key); continue; }
            EnemyOutlines.Set(pair.Key, OutlineSource.Ghost, LookOf(tint, entry.Level, Ease.Smooth(entry.Faded)));
        }
        foreach (var enemy in gone) Hide(enemy);
        gone.Clear();
    }

    /// <summary>
    /// What the Ghost's sight lays on a zombie (EnemyOutlines; the editor's OutlineBench draws the same): only where hidden, a
    /// fill in its colour at this strength and a band round the hidden shape, both as far faded in as <paramref name="shown"/>.
    /// </summary>
    public static OutlineLook LookOf(Color tint, float strength, float shown)
    {
        var band = tint;
        band.a = shown;
        var fill = tint;
        fill.a = strength * shown;
        return new OutlineLook(Color.clear, band, fill);
    }

    private Shown Show(EnemyHealth enemy)
    {
        var entry = new Shown();
        enemy.Died += Died;
        shown.Add(enemy, entry);
        return entry;
    }

    // Its kill hands its body to its corpse: the copies go at once, not after a fade on a body lying there.
    private void Died(EnemyHealth enemy) => Hide(enemy);

    private void Hide(EnemyHealth enemy)
    {
        if (ReferenceEquals(enemy, null) || !shown.ContainsKey(enemy)) return;
        shown.Remove(enemy);
        if (enemy == null) return;
        enemy.Died -= Died;
        EnemyOutlines.Clear(enemy, OutlineSource.Ghost);
    }
}
