using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Kills done some way worth a name (user, 2026-11-11: "다양한 점수 효과 넣을 것. 예를 들어 산탄총의 모든 펠렛이 한
/// 대상에게 적중 시 meatshot, 달려오는 중인 차저를 근접 무기로 머리를 정확히 노려서 처치했을 경우 railgun, 도약하는
/// 적을 공중에서 격추시키면 skeet"). Each stamps its name under the crosshair with a line saying what it was for
/// (SpecialKillStamp) and pays a bonus on top of the kill, into the popup's number and the money: only ever on top, a plain
/// kill loses nothing. This PC's own kills only (GunFeelDirector calls <see cref="Kill"/> after the kill's score).
/// <list type="bullet">
/// <item>MEATSHOT: every pellet of a shotgun blast went into the one it killed. Pellets fly one after another within the
/// frame and the body falls at the one that kills it, so a pellet still flying into where it stood counts too.</item>
/// <item>RAILGUN: a charger running at someone killed by a blow of the fists (no gun, no shove) to its head.</item>
/// <item>SKEET: a runner killed in the air, mid-leap.</item>
/// <item>COLLATERAL: one shot killed two or more.</item>
/// <item>HAT TRICK: three headshot kills in a row (a kill any other way starts the count again).</item>
/// <item>LAST ROUND: a kill with the magazine's last round.</item>
/// <item>SAVIOR: the one killed was holding a teammate (a grab, a meal, a pin).</item>
/// </list>
/// Points, each rule's number and colour: Tables/SpecialKills.csv; words: Texts special.*.
/// </summary>
public static class SpecialKills
{
    public const string TableName = "SpecialKills";

    public enum Kind { Meatshot, Railgun, Skeet, Collateral, HatTrick, LastRound, Savior }

    /// <summary>A special kill was scored (for the results screen's highlights and the like).</summary>
    public static event Action<Kind> Scored;

    // A kill by one of this PC's bullets, waiting for its shot to finish: the blast's other pellets, the rounds left.
    private struct Pending
    {
        public long Id;
        public Component Target;
        public int Frame;
    }

    // A body killed this frame: what it was made of and where it stood, for pellets still flying into it.
    private sealed class Fallen
    {
        public Component Target;
        public Bounds Bounds;
        public readonly HashSet<Collider> Colliders = new HashSet<Collider>();
    }

    private static readonly List<Pending> pending = new List<Pending>();
    private static readonly List<Fallen> fallen = new List<Fallen>();
    private static readonly Stack<Fallen> spareFallen = new Stack<Fallen>();
    private static readonly List<long> shotIds = new List<long>();
    private static readonly List<Component> shotStruck = new List<Component>();
    private static readonly List<Collider> found = new List<Collider>();
    private static readonly HashSet<Component> killed = new HashSet<Component>();
    private static int fallenFrame = -1, shotPellets = 1, headRow;
    private static WeaponController watched;
    private static PlayerMovement watchedFor;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetSession()
    {
        Scored = null;
        pending.Clear();
        fallen.Clear();
        shotIds.Clear();
        shotStruck.Clear();
        fallenFrame = -1;
        headRow = 0;
        watched = null;
        watchedFor = null;
    }

    /// <summary>
    /// Keeps listening to this PC's own gun (SpecialKillStamp asks every frame), so a blast's pellets are all seen from
    /// the first, even the first shot of a run.
    /// </summary>
    internal static void Watch()
    {
        var local = PlayerRoster.Local;
        if (local == watchedFor && watched != null) return;
        watchedFor = local;
        var weapon = local != null ? local.GetComponentInChildren<WeaponController>() : null;
        if (weapon == watched) return;
        if (watched != null) watched.ShotFired -= OnShot;
        watched = weapon;
        if (watched != null) watched.ShotFired += OnShot;
    }

    /// <summary>This PC's player killed the report's target (GunFeelDirector, after the kill's own score).</summary>
    public static void Kill(CombatDamageReport report, bool bullet, bool headshot, bool execution)
    {
        if (!Application.isPlaying || report.Target == null) return;
        var hit = report.Hit;
        var target = report.Target;
        // Three headshot kills in a row; a kill any other way starts again.
        headRow = bullet && headshot ? headRow + 1 : 0;
        var rules = SpecialKillStamp.Rules;
        int row = Mathf.Max(1, Mathf.RoundToInt(rules.Of(Kind.HatTrick).need));
        if (headRow > 0 && headRow % row == 0) Award(Kind.HatTrick, headRow);
        if (Charging(target) && !bullet && !execution && hit.Method != DamageMethod.Shove && OnHead(target, hit, rules.Of(Kind.Railgun).need))
            Award(Kind.Railgun);
        if (Airborne(target)) Award(Kind.Skeet);
        if (HoldsTeammate(target)) Award(Kind.Savior);
        if (!bullet) return;
        // The rest are the shot's to say, once its pellets are all out (OnShot).
        Remember(target);
        pending.Add(new Pending { Id = hit.Id, Target = target, Frame = Time.frameCount });
    }

    // A pellet or a bullet left the gun: what it struck first is noted, and the shot is judged once its last pellet is out.
    private static void OnShot(WeaponShot shot)
    {
        int frame = Time.frameCount;
        if (shot.Pellet == 0)
        {
            shotIds.Clear();
            shotStruck.Clear();
            shotPellets = watched != null && watched.EquippedData != null ? watched.EquippedData.Pellets : 1;
        }
        shotIds.Add(shot.AttackId);
        shotStruck.Add(Struck(shot));
        if (shot.Pellet < shotPellets - 1) return;

        killed.Clear();
        for (int i = pending.Count - 1; i >= 0; i--)
        {
            var kill = pending[i];
            if (kill.Frame != frame) { pending.RemoveAt(i); continue; }
            if (!shotIds.Contains(kill.Id)) continue;
            // By reference: the body may already be on its way out.
            if (!ReferenceEquals(kill.Target, null)) killed.Add(kill.Target);
            pending.RemoveAt(i);
        }
        if (killed.Count == 0) return;
        var rules = SpecialKillStamp.Rules;
        if (shotPellets > 1)
            foreach (var target in killed)
            {
                int into = 0;
                foreach (var struck in shotStruck) if (ReferenceEquals(struck, target)) into++;
                if (into >= Mathf.CeilToInt(shotPellets * Mathf.Clamp01(rules.Of(Kind.Meatshot).need) - .001f)) { Award(Kind.Meatshot); break; }
            }
        if (killed.Count >= Mathf.Max(2, Mathf.RoundToInt(rules.Of(Kind.Collateral).need))) Award(Kind.Collateral, killed.Count);
        var gun = watched != null ? watched.EquippedData : null;
        if (shot.AmmoAfterShot == 0 && gun != null && gun.MagazineSize > 1) Award(Kind.LastRound);
    }

    // What a pellet went into first: an enemy (or practice target) by its collider, or a body killed earlier this frame
    // whose colliders it met or whose place it flew through.
    private static Component Struck(WeaponShot shot)
    {
        var trace = shot.Trace;
        Collider collider = trace.HasHit ? trace.Hit.collider : null;
        if (fallenFrame == Time.frameCount)
            foreach (var body in fallen)
            {
                if (collider != null && body.Colliders.Contains(collider)) return body.Target;
                float reach = trace.HasHit ? trace.Hit.distance : float.PositiveInfinity;
                if (body.Bounds.IntersectRay(new Ray(shot.Origin, shot.Direction), out float at) && at <= reach) return body.Target;
            }
        if (collider == null) return null;
        var enemy = collider.GetComponentInParent<EnemyHealth>();
        if (enemy != null) return enemy;
        var damageable = collider.GetComponentInParent<IDamageReceiver>() as Component;
        return damageable;
    }

    // The body as it was when it died: its colliders, and their bounds a little wide (a pellet grazing the edge).
    private static void Remember(Component target)
    {
        if (fallenFrame != Time.frameCount)
        {
            foreach (var body in fallen) spareFallen.Push(body);
            fallen.Clear();
            fallenFrame = Time.frameCount;
        }
        var entry = spareFallen.Count > 0 ? spareFallen.Pop() : new Fallen();
        entry.Target = target;
        entry.Colliders.Clear();
        target.GetComponentsInChildren(true, found);
        bool any = false;
        foreach (var collider in found)
        {
            if (collider == null || collider.isTrigger) continue;
            entry.Colliders.Add(collider);
            if (!collider.enabled || !collider.gameObject.activeInHierarchy) continue;
            if (!any) { entry.Bounds = collider.bounds; any = true; }
            else entry.Bounds.Encapsulate(collider.bounds);
        }
        if (!any) entry.Bounds = new Bounds(target.transform.position + Vector3.up, new Vector3(.6f, 1.8f, .6f));
        entry.Bounds.Expand(.08f);
        fallen.Add(entry);
    }

    private static bool Charging(Component target) => target.TryGetComponent(out ZombieCharger charger) && charger.IsCharging;

    private static bool Airborne(Component target)
        => target.TryGetComponent(out EnemyCombatAI combat) && combat.IsPouncing && !combat.PounceLanded;

    // Holding someone who isn't this PC's player. A copy doesn't know whom it holds: then it is someone else while this
    // player is held by nothing.
    private static bool HoldsTeammate(Component target)
    {
        if (!target.TryGetComponent(out ZombieGrip grip) || !grip.IsHolding) return false;
        var local = PlayerRoster.Local;
        if (grip.Held != null) return grip.Held != local;
        return local == null || !local.TryGetComponent(out PlayerHold hold) || !hold.IsHeld;
    }

    // The blow landed on the head: a bone hitbox says so, or it struck within reach of the head bone.
    private static bool OnHead(Component target, DamageHit hit, float reach)
    {
        if (hit.Part == BodyPart.Head) return true;
        var animator = target.GetComponentInChildren<Animator>();
        var head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
        return head != null && hit.Point != Vector3.zero && (hit.Point - head.position).sqrMagnitude <= reach * reach;
    }

    private static void Award(Kind kind, int count = 0)
    {
        var rule = SpecialKillStamp.Rules.Of(kind);
        string key = "special." + Key(kind);
        string word = Texts.Get(key);
        // The reason under the word: how many in a row, how many with one shot.
        string note = kind == Kind.HatTrick ? Texts.Format(key + ".note", count)
            : kind == Kind.Collateral && count > 2 ? Texts.Format(key + ".many", count) : Texts.Get(key + ".note");
        CombatFeedHUD.Special(word, rule.points, rule.colour);
        SpecialKillStamp.Show(word, note, rule.colour);
        Scored?.Invoke(kind);
    }

    /// <summary>The kind's id in the table and the texts: meatshot, railgun, skeet, collateral, hattrick, lastround, savior.</summary>
    public static string Key(Kind kind) => kind.ToString().ToLowerInvariant();
}
