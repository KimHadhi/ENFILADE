using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The Schießhund's bounty dye on the zombies it splashed (Throwable.Dye, DyeBurst; the user's pick of 2026-11-12, "현상금
/// 염료탄"): a small bounty for its row's seconds. It shows as a teal outline that glows in the dark (the hound's
/// fluorescent dye: HitFlashRenderer's mark outline in the bounty's teal, at GrenadeRules dyeOutline; a ping's or a
/// bounty mark's own outline wins while one is on the same zombie, and the dye's comes back after). For the Schießhund
/// who threw it, those zombies take more from its guns and pay a little bounty each (GrenadeRules dyeDamage, dyeMoney,
/// dyePoints): the class's own code asks here (GunFactor, Money, KillPoints).
/// On every PC: each PC dyes what the burst splashed on its own copies, so everyone sees the teal, and only the
/// thrower's PC gains from it. A dye ends with its time or its zombie's life.
/// </summary>
[DisallowMultipleComponent]
public sealed class DyeMarks : MonoBehaviour
{
    private sealed class Mark
    {
        public Component Enemy;
        public ICombatFoe Foe;
        public PlayerMovement By;
        public float Until;
        public HitFlashRenderer Flash;
        public bool Lit;
    }

    private static DyeMarks instance;
    private readonly List<Mark> marks = new List<Mark>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => instance = null;

    /// <summary>Dyes this foe for <paramref name="seconds"/> as <paramref name="by"/>'s; dyed again, its time starts over as the newest thrower's.</summary>
    public static void Dye(Component enemy, PlayerMovement by, float seconds)
    {
        if (!Application.isPlaying || enemy == null || !(enemy is ICombatFoe foe) || !foe.IsAlive || seconds <= 0f) return;
        if (instance == null)
        {
            instance = new GameObject("Dye Marks").AddComponent<DyeMarks>();
            RuntimeSceneHierarchy.Group(instance.gameObject, RuntimeSceneHierarchy.Systems, "Combat");
        }
        instance.Add(enemy, foe, by, seconds);
    }

    /// <summary>Whether <paramref name="hunter"/>'s dye is on this foe now.</summary>
    public static bool IsDyedBy(Component enemy, PlayerMovement hunter)
    {
        if (instance == null || enemy == null || hunter == null) return false;
        foreach (var mark in instance.marks)
            if (mark.By == hunter && mark.Enemy != null && mark.Enemy.gameObject == enemy.gameObject && Time.time < mark.Until) return true;
        return false;
    }

    /// <summary>
    /// A bullet's factor from the dye (PlayerClass.GunFactor, for this PC's Schießhund's shots): 1 + GrenadeRules
    /// dyeDamage % on a foe it dyed, else 1.
    /// </summary>
    public static float GunFactor(Component target, PlayerMovement hunter)
        => IsDyedBy(target, hunter) ? 1f + Mathf.Max(0f, Throwables.Rule("dyeDamage", 15f)) / 100f : 1f;

    /// <summary>The money a kill of a foe it dyed pays its Schießhund on top (GrenadeRules dyeMoney), else 0.</summary>
    public static int Money(Component target, PlayerMovement hunter)
        => IsDyedBy(target, hunter) ? Mathf.Max(0, Mathf.RoundToInt(Throwables.Rule("dyeMoney", 30f))) : 0;

    /// <summary>The points toward the Desperado's next round such a kill gives (GrenadeRules dyePoints), else 0.</summary>
    public static int KillPoints(Component target, PlayerMovement hunter)
        => IsDyedBy(target, hunter) ? Mathf.Max(0, Mathf.RoundToInt(Throwables.Rule("dyePoints", 1f))) : 0;

    private void Add(Component enemy, ICombatFoe foe, PlayerMovement by, float seconds)
    {
        float until = Time.time + seconds;
        foreach (var mark in marks)
        {
            if (mark.Enemy == null || mark.Enemy.gameObject != enemy.gameObject) continue;
            mark.By = by;
            mark.Until = Mathf.Max(mark.Until, until);
            return;
        }
        marks.Add(new Mark { Enemy = enemy, Foe = foe, By = by, Until = until, Flash = HitFlashRenderer.For(enemy) });
    }

    private void LateUpdate()
    {
        float strength = Mathf.Clamp01(Throwables.Rule("dyeOutline", .6f));
        for (int i = marks.Count - 1; i >= 0; i--)
        {
            var mark = marks[i];
            bool on = mark.Enemy != null && mark.Foe != null && mark.Foe.IsAlive && Time.time < mark.Until;
            // A ping's or a bounty mark's outline on it wins; the dye's comes back when that one ends.
            bool marked = mark.Enemy != null && PingMarks.IsMarked(mark.Enemy);
            if (!on)
            {
                if (mark.Lit && !marked && mark.Flash != null) mark.Flash.SetPing(0f, Color.clear);
                marks.RemoveAt(i);
                continue;
            }
            if (marked || mark.Flash == null) { mark.Lit = false; continue; }
            // Its last second fades out.
            mark.Flash.SetPing(strength * Mathf.Clamp01(mark.Until - Time.time), PingMarks.BountyColor);
            mark.Lit = true;
        }
    }

    private void OnDestroy()
    {
        foreach (var mark in marks)
            if (mark.Lit && mark.Flash != null && mark.Enemy != null && !PingMarks.IsMarked(mark.Enemy)) mark.Flash.SetPing(0f, Color.clear);
        marks.Clear();
        if (instance == this) instance = null;
    }
}
