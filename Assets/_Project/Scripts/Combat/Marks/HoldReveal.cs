using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A zombie holding a player (ZombieGrip: a shambler's grab, one kneeling to eat, a runner's pin) stands out on every PC
/// while it holds: the holder's band all round its body, seen or not (CombatFeedbackSettings holderOutlineColor, HDR so
/// it catches the bloom), and a flat fill breathing over it where something stands in front of it (EnemyOutlines). The
/// user's playtest of 2026-11-11: a shambler below a step grabbed them and the very floor they stood on hid it, and a
/// friend who wants to help has to see at once both the grabbed teammate (DownedAura) and what holds them. A grip signs
/// up here as it starts holding (Watch) and drops off once it lets go; one of these is made on first need and lives with
/// the scene. Numbers: Tables/HoldReveal.csv (the fill's colour and its breaths, smooth ones: never a blink, the user's
/// rule of 2026-11-14; until 2026-11-21 it was a saw that jumped back to full twice a second).
/// </summary>
[DisallowMultipleComponent]
public sealed class HoldReveal : MonoBehaviour
{
    public const string TableName = "HoldReveal", TableRow = "Default";
    private static HoldReveal instance;

    [SerializeField, Tooltip("The fill over the holder's hidden parts; alpha is how solid it is at the top of a breath.")]
    private Color holderColor = new Color(1f, .16f, .3f, .7f);
    [SerializeField, Min(0f), Tooltip("Breaths a minute: the fill rises from dimShare to full and back, smoothly.")]
    private float beats = 90f;
    [SerializeField, Range(0f, 1f), Tooltip("How solid the fill stays between breaths, as a share of holderColor's alpha.")]
    private float dimShare = .55f;

    private readonly List<ZombieGrip> watched = new List<ZombieGrip>();
    private readonly HashSet<ZombieGrip> shown = new HashSet<ZombieGrip>();
    private readonly List<ZombieGrip> done = new List<ZombieGrip>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    /// <summary>This grip has started holding someone, on this PC's knowledge (ZombieGrip): show it while it holds.</summary>
    internal static void Watch(ZombieGrip grip)
    {
        if (grip == null || !Application.isPlaying) return;
        if (instance == null)
        {
            instance = new GameObject("Hold Reveal").AddComponent<HoldReveal>();
            RuntimeSceneHierarchy.Group(instance.gameObject, RuntimeSceneHierarchy.Systems, "Combat");
            GameTables.Bind(TableName, TableRow, instance);
        }
        if (!instance.watched.Contains(grip)) instance.watched.Add(grip);
    }

    /// <summary>
    /// A holder's look on its body (EnemyOutlines; the editor's OutlineBench draws the same): the holder's band seen or hidden,
    /// and over the hidden parts the fill at this breath (1 at the top, 0 at the bottom).
    /// </summary>
    public static OutlineLook LookOf(Color fillColour, float dim, float breath)
    {
        var band = CombatFeedbackSettings.InstanceOrDefaults.holderOutlineColor;
        band.a = 1f;
        var fill = fillColour;
        fill.a *= Mathf.Lerp(dim, 1f, Mathf.Clamp01(breath));
        return new OutlineLook(band, band, fill);
    }

    private void OnDestroy()
    {
        foreach (var grip in shown) Hide(grip);
        shown.Clear();
        if (instance == this) instance = null;
    }

    private void Update()
    {
        float breath = .5f + .5f * Mathf.Cos(Time.unscaledTime * beats / 60f * 2f * Mathf.PI);
        var look = LookOf(holderColor, dimShare, breath);
        done.Clear();
        foreach (var grip in watched)
        {
            bool holding = Holds(grip);
            // By reference: a grip destroyed with its zombie still has its entry here (its outline goes with its body).
            bool has = !ReferenceEquals(grip, null) && shown.Contains(grip);
            if (holding)
            {
                EnemyOutlines.Set(grip, OutlineSource.Holder, look);
                if (!has) shown.Add(grip);
            }
            else if (has)
            {
                Hide(grip);
                shown.Remove(grip);
            }
            if (!holding) done.Add(grip);
        }
        foreach (var grip in done) watched.Remove(grip);
    }

    // Holding someone, alive, and still wearing the body it held them with (a death hands the body to its corpse).
    private static bool Holds(ZombieGrip grip)
    {
        if (grip == null || !grip.isActiveAndEnabled || !grip.IsHolding) return false;
        return grip.TryGetComponent(out EnemyHealth health) && health.IsAlive;
    }

    private static void Hide(ZombieGrip grip)
    {
        if (grip != null) EnemyOutlines.Clear(grip, OutlineSource.Holder);
    }
}
