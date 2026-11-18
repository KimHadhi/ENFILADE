using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What a kind of zombie makes of a survivor another zombie already has out of the fight (a runner's pin, a candyman's
/// drag): Tables/ZombieKinds `taken`. Guts &amp; Blackpowder's shamblers leave such a one to the zombie that has them and go
/// for someone still on their feet (user, 2026-11-18: "러너 등에게 붙잡힌 생존자는 쉠블러가 무시하고 다른 생존자를 노리도록").
/// </summary>
public enum TakenRule : byte
{
    /// <summary>As anyone else (a runner: several may pin one player, up to PlayerHold.MostPinning).</summary>
    Any,
    /// <summary>Passed over while anyone else can be gone for; with nobody else (alone, or everyone taken) gone for after all.</summary>
    Last,
    /// <summary>Never its quarry: with nobody else it has none, and waits where it is (no target is every AI's "nothing to do").</summary>
    Never
}

/// <summary>
/// The player this enemy is after: the nearest one alive, reconsidered now and then so it doesn't keep flipping. A
/// Knight's taunt (ClassKit, stage 2 of the classes) turns it on the Knight for a few seconds, whatever else it was
/// after, so long as the Knight stays on their feet. Someone another zombie already has is left to it by this zombie's
/// kind's rule (TakenRule): the one place that says so, for whoever it noticed as for the nearest (Pick).
/// </summary>
[DisallowMultipleComponent]
public sealed class EnemyTargeting : MonoBehaviour
{
    /// <summary>One survivor as a zombie weighs them (Pick): how far, whether another zombie has them, whether smoke hides them from it.</summary>
    internal struct Candidate
    {
        public float Distance;
        public bool Taken, Hidden;
    }

    [SerializeField, Min(.05f)] private float retargetInterval = .5f;
    private static readonly List<EnemyTargeting> all = new List<EnemyTargeting>();
    // The players weighed this look and how each stands (Nearest): kept, so a look makes nothing.
    private static readonly List<PlayerMovement> weighed = new List<PlayerMovement>();
    private static readonly List<Candidate> candidates = new List<Candidate>();
    private PlayerMovement player, taunter;
    private ZombieMind mind;
    private EnemyNetwork network;
    private EnemyCombatAI combat;
    private ZombieType type;
    private ZombieGrip grip;
    private float nextLook, tauntUntil;
    // Called out in a Schießhund's duel: the blow it is winding up on someone else is finished first.
    private bool finishFirst;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => all.Clear();

    private void Awake()
    {
        type = GetComponent<ZombieType>();
        grip = GetComponent<ZombieGrip>();
    }

    private void OnEnable() => all.Add(this);

    private void OnDisable()
    {
        all.Remove(this);
        taunter = null;
    }

    /// <summary>
    /// The players it may go after, or null for anyone. The ENFILADE tower pens each lane's zombies in with that lane's
    /// players (TowerRun): someone behind the glass next door is never its quarry, nor is a taunt or a sound from there
    /// turning it. Set on the PC that runs it (the host or a lone PC); a copy only follows its host.
    /// </summary>
    public System.Predicate<PlayerMovement> Pen { get; set; }

    public PlayerMovement Player
    {
        get
        {
            long began = ZombieCost.Begin();
            var found = Find();
            ZombieCost.End(ZombieCost.Part.Target, began);
            return found;
        }
    }

    // Whom it is after now: a taunt first, then whoever it noticed, then the nearest, looked at again now and then.
    private PlayerMovement Find()
    {
        if (Pen != null)
        {
            if (taunter != null && !Pen(taunter)) taunter = null;
            if (player != null && !Pen(player)) player = null;
        }
        // Taunted: the Knight, until it wears off or they go down. Called out in a duel, it first finishes the blow it
        // was already winding up on someone else, which then lands on nothing (HoundDuel): turning on the hound mid-swing
        // would cheapen the save (the user, 2026-11-12: "적이 공격하려고 내려찍으려 하고 있었어 근데 결투를 하면 갑자기
        // 캔슬하고 시순트를 바라보면 구해지는 입장에서 짜칠 것 같아").
        if (taunter != null)
        {
            if (Time.time < tauntUntil && Standing(taunter))
            {
                if (combat == null) combat = GetComponent<EnemyCombatAI>();
                if (finishFirst && player != null && player != taunter && combat != null && combat.IsPreparingAttack) return player;
                finishFirst = false;
                return player = taunter;
            }
            taunter = null;
            finishFirst = false;
        }
        // A zombie that noticed someone goes after them, not whoever is nearest.
        if (mind == null) mind = GetComponent<ZombieMind>();
        var noticed = mind != null ? mind.Target : null;
        if (noticed != null && (Pen == null || Pen(noticed)) && !PassedOver(noticed)) return player = noticed;
        // Someone no zombie can see for now (ZombieEffects.Unseen) is dropped at once, not at its next look; so is someone
        // another zombie has just taken, when this kind leaves such to it.
        bool lost = player == null || !player.IsInitialized || !player.DamageReceiver.IsAlive || Unseen(player) || PassedOver(player);
        if (lost || Time.time >= nextLook)
        {
            nextLook = Time.time + retargetInterval;
            // A Ghost's smoke hangs somewhere (SmokeScreen), or someone can't be seen: the nearest it can see goes
            // before a nearer one it can't.
            player = Nearest(SmokeScreen.Count > 0 || ZombieMind.AnyUnseen);
        }
        return player;
    }

    /// <summary>What this zombie's kind makes of a survivor another zombie already has (ZombieKinds taken); anything without a kind weighs them as anyone.</summary>
    public TakenRule Rule => type != null ? type.Taken : TakenRule.Any;

    // Someone another zombie already has, whom this kind leaves to it: never its quarry, or not while anyone else can be
    // gone for.
    private bool PassedOver(PlayerMovement who)
    {
        var rule = Rule;
        if (rule == TakenRule.Any || !ZombieGrip.TakenByAnother(who, grip)) return false;
        return rule == TakenRule.Never || AnyoneElse(who);
    }

    // Another living player it could go for instead: in the pen, not unseen, and not taken by another zombie either.
    private bool AnyoneElse(PlayerMovement but)
    {
        foreach (var someone in PlayerRoster.All)
        {
            if (someone == null || someone == but || !someone.IsInitialized || !someone.DamageReceiver.IsAlive) continue;
            if ((Pen != null && !Pen(someone)) || Unseen(someone) || ZombieGrip.TakenByAnother(someone, grip)) continue;
            return true;
        }
        return false;
    }

    // The closest living player (the pen allowing) it may go for, as Pick weighs them. By sight (smoke hangs somewhere, or
    // someone can't be seen), the unseen are nobody's quarry at all, and smoke hides whom it hides.
    private PlayerMovement Nearest(bool bySight)
    {
        var rule = Rule;
        weighed.Clear();
        candidates.Clear();
        foreach (var someone in PlayerRoster.All)
        {
            if (someone == null || !someone.IsInitialized || !someone.DamageReceiver.IsAlive || (Pen != null && !Pen(someone))) continue;
            if (bySight && Unseen(someone)) continue;
            weighed.Add(someone);
            candidates.Add(new Candidate
            {
                Distance = (someone.transform.position - transform.position).sqrMagnitude,
                Taken = rule != TakenRule.Any && ZombieGrip.TakenByAnother(someone, grip),
                Hidden = bySight && SmokeScreen.HidesFrom(this, someone)
            });
        }
        int picked = Pick(candidates, rule);
        return picked >= 0 ? weighed[picked] : null;
    }

    /// <summary>
    /// Which of the candidates a zombie goes for, or -1 for none: the nearest it can see that no other zombie has; else
    /// the nearest no other zombie has (smoke hides them all, and it loses them there: SmokeScreen); else, by
    /// <paramref name="rule"/>, the nearest another zombie has (Last: nobody else is left, alone or everyone taken) or
    /// nobody (Never). Under Any a taken one is weighed as anyone. Pure, so the rule can be read and checked without a
    /// zombie (Cases).
    /// </summary>
    internal static int Pick(IReadOnlyList<Candidate> candidates, TakenRule rule)
    {
        int seen = -1, any = -1, left = -1;
        for (int i = 0; i < candidates.Count; i++)
        {
            var one = candidates[i];
            if (one.Taken && rule != TakenRule.Any)
            {
                if (rule == TakenRule.Last && (left < 0 || one.Distance < candidates[left].Distance)) left = i;
                continue;
            }
            if (any < 0 || one.Distance < candidates[any].Distance) any = i;
            if (!one.Hidden && (seen < 0 || one.Distance < candidates[seen].Distance)) seen = i;
        }
        return seen >= 0 ? seen : any >= 0 ? any : left;
    }

    /// <summary>
    /// /targets cases: Pick over the cases the rule is for, under each rule (alone and pinned; two players, one pinned;
    /// everyone pinned; smoke), what each must give written beside what it gives.
    /// </summary>
    internal static string Cases()
    {
        var text = new System.Text.StringBuilder();
        var cases = new (string Name, Candidate[] Who, int Any, int Last, int Never)[]
        {
            ("alone, pinned", new[] { new Candidate { Distance = 4f, Taken = true } }, 0, 0, -1),
            ("two, the nearer pinned", new[] { new Candidate { Distance = 4f, Taken = true }, new Candidate { Distance = 100f } }, 0, 1, 1),
            ("two, the farther pinned", new[] { new Candidate { Distance = 4f }, new Candidate { Distance = 100f, Taken = true } }, 0, 0, 0),
            ("everyone pinned", new[] { new Candidate { Distance = 9f, Taken = true }, new Candidate { Distance = 4f, Taken = true } }, 1, 1, -1),
            ("the free one hidden by smoke", new[] { new Candidate { Distance = 4f, Taken = true }, new Candidate { Distance = 9f, Hidden = true } }, 0, 1, 1),
            ("nobody", new Candidate[0], -1, -1, -1)
        };
        int wrong = 0;
        foreach (var one in cases)
        {
            int any = Pick(one.Who, TakenRule.Any), last = Pick(one.Who, TakenRule.Last), never = Pick(one.Who, TakenRule.Never);
            bool right = any == one.Any && last == one.Last && never == one.Never;
            if (!right) wrong++;
            text.Append(right ? "ok    " : "WRONG ").Append(one.Name).Append(": any ").Append(any).Append(" (").Append(one.Any)
                .Append("), last ").Append(last).Append(" (").Append(one.Last).Append("), never ").Append(never).Append(" (")
                .Append(one.Never).Append(")\n");
        }
        text.Append("wrong: ").Append(wrong).Append('\n');
        return text.ToString();
    }

    /// <summary>
    /// The console's /targets (whom every zombie this PC runs is after and whom it passes over) and /targets cases (the
    /// rule itself over its cases, no zombie needed). Also written to Logs/zombie-targets.txt in the editor.
    /// </summary>
    internal static string Command(string args)
    {
        bool cases = (args ?? "").Trim() == "cases";
        string text = cases ? Cases() : Report();
#if UNITY_EDITOR
        try { System.IO.File.WriteAllText(System.IO.Path.Combine(Application.dataPath, "..", "Logs", "zombie-targets.txt"), text); }
        catch (System.Exception problem) { Debug.LogWarning("[Targeting] Logs/zombie-targets.txt: " + problem.Message); }
#endif
        Debug.Log("[Targeting] " + (cases ? "cases" : "report") + "\n" + text);
        int end = text.IndexOf('\n');
        return (cases ? text.Substring(text.LastIndexOf("wrong:", System.StringComparison.Ordinal)).Trim() : end > 0 ? text.Substring(0, end) : text)
            + " · Logs/zombie-targets.txt"; // dev-text
    }

    /// <summary>
    /// /targets: every enemy this PC runs: its kind's rule, whom it is after, and each living player another zombie has,
    /// with whether this one passes them over.
    /// </summary>
    internal static string Report()
    {
        var text = new System.Text.StringBuilder();
        int count = 0, passing = 0;
        foreach (var enemy in all)
        {
            if (enemy == null || !enemy.isActiveAndEnabled) continue;
            if (enemy.network == null) enemy.network = enemy.GetComponent<EnemyNetwork>();
            if (enemy.network != null && enemy.network.IsCopy) continue;
            count++;
            var after = enemy.Find();
            text.Append(enemy.name).Append(' ').Append(enemy.type != null ? enemy.type.Kind.ToString() : "?").Append(" rule ")
                .Append(enemy.Rule).Append(" after ").Append(after != null ? after.name : "nobody");
            foreach (var someone in PlayerRoster.All)
            {
                if (someone == null || !someone.IsInitialized || !someone.DamageReceiver.IsAlive) continue;
                if (!ZombieGrip.TakenByAnother(someone, enemy.grip)) continue;
                bool passed = enemy.PassedOver(someone);
                if (passed) passing++;
                text.Append(" | ").Append(someone.name).Append(" taken by another: ").Append(passed ? "passed over" : "still its quarry");
            }
            text.Append('\n');
        }
        text.Insert(0, "enemies run here: " + count + " · passing someone over: " + passing + "\n");
        return text.ToString();
    }

    /// <summary>
    /// A Knight's taunt at <paramref name="at"/>: every enemy this PC runs (the host's or a lone PC's; a copy only follows
    /// its host) within <paramref name="reach"/> that is already in the fight goes after <paramref name="knight"/> for
    /// <paramref name="seconds"/>, an elite as much as the pack. One standing calm is left be: a taunt never wakes anything,
    /// so a Knight can't give the team's stealth away with it.
    /// </summary>
    public static void Taunt(PlayerMovement knight, Vector3 at, float reach, float seconds)
    {
        if (!Standing(knight) || seconds <= 0f || reach <= 0f) return;
        float until = Time.time + seconds, most = reach * reach;
        foreach (var enemy in all)
        {
            if (enemy == null || !enemy.isActiveAndEnabled || (enemy.transform.position - at).sqrMagnitude > most) continue;
            if (enemy.network == null) enemy.network = enemy.GetComponent<EnemyNetwork>();
            if (enemy.network != null && enemy.network.IsCopy) continue;
            if (enemy.mind == null) enemy.mind = enemy.GetComponent<ZombieMind>();
            if (enemy.mind != null && !enemy.mind.IsAwake) continue;
            enemy.taunter = knight;
            enemy.tauntUntil = until;
        }
    }

    /// <summary>
    /// A Schießhund's duel (HoundDuel): this one enemy, if this PC runs it (the host's or a lone PC's; a copy follows its
    /// host), goes after <paramref name="hound"/> alone for <paramref name="seconds"/>, whatever it was after, as a taunt
    /// turns it, until the hound goes down.
    /// </summary>
    public static void Duel(EnemyHealth enemy, PlayerMovement hound, float seconds)
    {
        if (enemy == null || seconds <= 0f || !Standing(hound) || !enemy.TryGetComponent(out EnemyTargeting targeting)) return;
        if (targeting.network == null) targeting.network = enemy.GetComponent<EnemyNetwork>();
        if (targeting.network != null && targeting.network.IsCopy) return;
        targeting.taunter = hound;
        targeting.tauntUntil = Time.time + seconds;
        targeting.finishFirst = true;
    }

    // No zombie can see this player for now (ZombieEffects.Unseen), and they are not close enough for this one to touch.
    private bool Unseen(PlayerMovement who)
    {
        if (!ZombieMind.AnyUnseen) return false;
        if (mind == null) mind = GetComponent<ZombieMind>();
        return mind != null ? mind.CannotSee(who) : ZombieMind.IsUnseen(who);
    }

    // Alive and not down: a Knight lying on the ground no longer holds anything's eye (they'd only be eaten).
    private static bool Standing(PlayerMovement who) => who != null && who.IsInitialized && who.DamageReceiver.IsAlive && !who.IsDowned;
}
