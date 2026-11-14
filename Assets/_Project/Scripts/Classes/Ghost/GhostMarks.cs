using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A Ghost's quiet marks (조용한 표적; the user's card feedback of 2026-11-14): a small white mark over a zombie it picked
/// through its sights while that zombie knew nothing, on every PC (GhostNet), so the team sees which ones are the Ghost's.
/// The Ghost's headshot on one of its own marks is a silent kill (GhostCards). A mark goes when its zombie dies or wakes,
/// or when its time is up; nothing else comes of that (a miss is never a minus). On the Ghost's own PC a thin ring fills
/// round the spot while it holds its sights on one (the second it takes), and a small chevron stands over the zombie its
/// 암살 연쇄 card would take it to. Drawn in the world, turned to the eye and kept to about the same size on screen at any
/// distance, landing once and then still: nothing blinks. One of these is made on first need and lives with the scene.
/// </summary>
[DisallowMultipleComponent]
public sealed class GhostMarks : MonoBehaviour
{
    // How far over its head a mark stands (m); its size as a share of the distance to the eye, and the least (m); how long a
    // new mark takes to land (s).
    private const float OverHead = .4f, Share = .016f, Least = .1f, Land = .18f;

    private sealed class Mark
    {
        public EnemyHealth Zombie;
        public ZombieMind Mind;
        public PlayerMovement By;
        public float Until, Since;
    }

    private static GhostMarks instance;
    // This PC's own Ghost, as of the frame said: the zombie it holds its sights on and how far into its second it is, and
    // the zombie its chain would take it to.
    private static EnemyHealth aimed, pointed;
    private static float aimShare;
    private static int aimFrame = -9, pointFrame = -9;
    private readonly List<Mark> marks = new List<Mark>();
    private FxShapes shapes;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        instance = null;
        aimed = pointed = null;
        aimFrame = pointFrame = -9;
    }

    /// <summary>Every PC (GhostNet): <paramref name="by"/> marked this zombie, for <paramref name="seconds"/> at most.</summary>
    public static void Show(EnemyHealth zombie, PlayerMovement by, float seconds)
    {
        if (!Application.isPlaying || zombie == null || !zombie.IsAlive) return;
        var self = Ensure();
        // One of theirs on it already is put on again, for the full time.
        self.marks.RemoveAll(mark => mark.Zombie == zombie && mark.By == by);
        self.marks.Add(new Mark
        {
            Zombie = zombie, Mind = zombie.GetComponent<ZombieMind>(), By = by,
            Until = Time.time + Mathf.Max(.5f, seconds), Since = Time.unscaledTime
        });
    }

    /// <summary>
    /// Whether <paramref name="by"/>'s mark is on this zombie now. Asked as a shot is judged and again as its kill is told,
    /// when the zombie is already down to nothing: its mark still counts then.
    /// </summary>
    public static bool IsMarkedBy(Component zombie, PlayerMovement by)
    {
        if (instance == null || zombie == null || by == null) return false;
        foreach (var mark in instance.marks)
            if (mark.By == by && mark.Zombie != null && mark.Zombie.gameObject == zombie.gameObject && Holds(mark)) return true;
        return false;
    }

    /// <summary>This PC's own Ghost holds its sights on this zombie, this far into the second a mark takes (0 to 1), this frame.</summary>
    internal static void Aiming(EnemyHealth zombie, float share)
    {
        if (zombie == null) return;
        Ensure();
        aimed = zombie;
        aimShare = Mathf.Clamp01(share);
        aimFrame = Time.frameCount;
    }

    /// <summary>This PC's own Ghost's 암살 연쇄 would take it to this zombie, this frame.</summary>
    internal static void Pointing(EnemyHealth zombie)
    {
        if (zombie == null) return;
        Ensure();
        pointed = zombie;
        pointFrame = Time.frameCount;
    }

    private static GhostMarks Ensure()
    {
        if (instance != null) return instance;
        instance = new GameObject("Ghost Marks").AddComponent<GhostMarks>();
        RuntimeSceneHierarchy.Group(instance.gameObject, RuntimeSceneHierarchy.Systems, "Combat");
        return instance;
    }

    // Its time not up, and its zombie not awake: a mark is for one that knows nothing.
    private static bool Holds(Mark mark) => Time.time < mark.Until && (mark.Mind == null || !mark.Mind.IsAwake);

    private void Awake() => shapes = new FxShapes(transform, "Ghost Marks", false);

    private void OnDestroy()
    {
        shapes?.Destroy();
        if (instance == this) instance = null;
    }

    private void LateUpdate()
    {
        for (int i = marks.Count - 1; i >= 0; i--)
        {
            var mark = marks[i];
            if (mark.Zombie == null || !mark.Zombie.IsAlive || !Holds(mark)) marks.RemoveAt(i);
        }
        var eye = Camera.main;
        if (eye == null || shapes == null || !shapes.Begin(eye)) return;
        Vector3 from = eye.transform.position;
        foreach (var mark in marks)
        {
            float landed = Ease.Out((Time.unscaledTime - mark.Since) / Land);
            Diamond(Over(mark.Zombie), from, Mathf.Lerp(1.7f, 1f, landed), Mathf.Lerp(.3f, 1f, landed));
        }
        // The Ghost's own: the second its sights take, filling round where the mark will stand.
        if (aimFrame >= Time.frameCount - 1 && aimed != null && aimed.IsAlive)
        {
            Vector3 at = Over(aimed);
            float size = Size(at, from);
            shapes.Ring(at, shapes.Right, shapes.Up, size * .85f, size * .1f, FxShapes.With(Color.white, .2f), 40);
            if (aimShare > 0f)
                shapes.Ring(at, shapes.Right, shapes.Up, size * .85f, size * .14f, FxShapes.With(Color.white, .85f), 40,
                    Mathf.PI * .5f, Mathf.PI * .5f - aimShare * Mathf.PI * 2f);
        }
        // The Ghost's own: where its chain would take it, a small chevron pointing down at the head.
        if (pointFrame >= Time.frameCount - 1 && pointed != null && pointed.IsAlive) Chevron(Over(pointed), from);
        shapes.End();
    }

    // Over the head, a little above: a zombie's capsule's top (CombatFoe.Top).
    private static Vector3 Over(Component zombie) => CombatFoe.Top(zombie) + Vector3.up * OverHead;

    private static float Size(Vector3 at, Vector3 eye) => Mathf.Max(Least, Vector3.Distance(at, eye) * Share);

    // A small diamond outline over a soft dark one, so it reads on a pale sky as on a dark wall.
    private void Diamond(Vector3 at, Vector3 eye, float scale, float alpha)
    {
        float size = Size(at, eye) * scale;
        Vector3 up = shapes.Up * size * .55f, right = shapes.Right * size * .42f;
        Vector3 top = at + up, bottom = at - up, left = at - right, rightCorner = at + right;
        Color shade = FxShapes.With(Color.black, .35f * alpha), line = FxShapes.With(Color.white, .92f * alpha);
        float thick = size * .2f, thin = size * .1f;
        shapes.Blade(top, rightCorner, thick, shade); shapes.Blade(rightCorner, bottom, thick, shade);
        shapes.Blade(bottom, left, thick, shade); shapes.Blade(left, top, thick, shade);
        shapes.Blade(top, rightCorner, thin, line); shapes.Blade(rightCorner, bottom, thin, line);
        shapes.Blade(bottom, left, thin, line); shapes.Blade(left, top, thin, line);
    }

    private void Chevron(Vector3 at, Vector3 eye)
    {
        float size = Size(at, eye);
        Vector3 tip = at - shapes.Up * size * .2f, arm = shapes.Up * size * .45f, wide = shapes.Right * size * .45f;
        shapes.Blade(tip + arm - wide, tip, size * .18f, FxShapes.With(Color.black, .3f));
        shapes.Blade(tip + arm + wide, tip, size * .18f, FxShapes.With(Color.black, .3f));
        shapes.Blade(tip + arm - wide, tip, size * .09f, FxShapes.With(Color.white, .9f));
        shapes.Blade(tip + arm + wide, tip, size * .09f, FxShapes.With(Color.white, .9f));
    }
}
