using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// What this PC's player has before them at the moment a callout is picked: who speaks, what they look at, and the
/// moments a thanks has its reason from. PlayerCallouts fills it (Sense, Track); the facts read it.
/// </summary>
public sealed class CalloutSense
{
    public PlayerMovement Speaker;
    public PlayerHold Hold;
    public WeaponController Weapon;
    /// <summary>The zombie a line would be about, and whether the crosshair is on it.</summary>
    public EnemyHealth Target;
    public bool Aimed;
    /// <summary>A spot looked at that a line could pin.</summary>
    public bool HasSpot;
    public Vector3 Spot;
    /// <summary>Awake zombies about, and right on top of the speaker.</summary>
    public int Crowd, Close;
    public float FreedAt = -99f, RaisedAt = -99f, HealedAt = -99f;
    // The thresholds the facts use (Player.csv Callout.*, which PlayerCallouts is bound to).
    public int HordeCount = 8, NearCount = 4;
    public float HurtShare = .35f, HealShare = .6f, ThanksSeconds = 8f;

    /// <summary>The target's kind as the tables write it ("brute"); a zombie with no type is a shambler.</summary>
    public string TargetKind => KindOf(Target);

    /// <summary>A zombie's kind as the tables write it.</summary>
    public static string KindOf(EnemyHealth enemy)
        => enemy != null && enemy.TryGetComponent(out ZombieType type) ? type.Kind.ToString().ToLowerInvariant() : "shambler";

    public float HealthShare
    {
        get
        {
            var receiver = Speaker != null ? Speaker.DamageReceiver : null;
            return receiver != null ? receiver.Health / Mathf.Max(1f, receiver.MaximumHealth) : 1f;
        }
    }
}

/// <summary>
/// The facts a callout's condition is made of (CalloutWhen): each a name the tables use and what the game knows under
/// it, from the speaker's own state to the team, the class kits and the campaign. A table row combines them
/// ("class:knight &amp; taunt.ready", "friend.down &amp; !grabbed"); this is the one list of what can be asked.
/// They used to be the cases of a switch inside PlayerCallouts, each compound a word of its own. A system that comes to
/// know something worth shouting about adds its fact here by name (Add) and every table can use it at once, the mods'
/// too; the lines themselves, their slots and their conditions are rows.
/// The words are listed for table writers in Callouts.csv's #설명 row and in Docs/Modding.md (콜아웃 줄·칸 더하기).
/// </summary>
public static class CalloutFacts
{
    /// <summary>Whether the fact holds for this speaker now; word is what a row wrote after the fact's colon ("" for none).</summary>
    public delegate bool Test(CalloutSense sense, string word);

    private static readonly Dictionary<string, Test> tests = new Dictionary<string, Test>(StringComparer.OrdinalIgnoreCase);

    static CalloutFacts()
    {
        Add("always", (s, w) => true);
        // What is looked at.
        Add("none", (s, w) => s.Target == null);
        Add("aimed", (s, w) => s.Aimed && s.Target != null);
        Add("kind", (s, w) => s.Target != null && string.Equals(s.TargetKind, w, StringComparison.OrdinalIgnoreCase));
        // That kind, down on its belly crawling (a brute whose leg was shot out: its head is the way now).
        Add("crawl", (s, w) => s.Target != null && s.Target.TryGetComponent(out ZombieType crawler) && crawler.Crawls
            && string.Equals(s.TargetKind, w, StringComparison.OrdinalIgnoreCase));
        Add("elite", (s, w) => s.Target != null && PlayerClass.IsElite(s.Target));
        Add("spot", (s, w) => s.HasSpot);
        Add("horde", (s, w) => s.Crowd >= s.HordeCount);
        Add("surrounded", (s, w) => s.Close >= s.NearCount);
        // The speaker.
        Add("pinned", (s, w) => s.Hold != null && s.Hold.IsPinned);
        Add("hooked", (s, w) => s.Hold != null && s.Hold.IsDragged);
        Add("eaten", (s, w) => s.Hold != null && s.Hold.IsEaten);
        Add("grabbed", (s, w) => s.Speaker.IsHeld);
        Add("downed", (s, w) => s.Speaker.IsDowned);
        Add("hurt", (s, w) => s.HealthShare < s.HurtShare);
        Add("heal", (s, w) => s.HealthShare < s.HealShare);
        Add("ammo", (s, w) => LowOnAmmo(s));
        Add("mag.empty", (s, w) => Gun(s) != null && Gun(s).Data != null && Gun(s).Data.Category == WeaponCategory.Ranged && Gun(s).Ammo <= 0);
        Add("reloading", (s, w) => Gun(s) != null && Gun(s).IsReloading);
        Add("ult.ready", (s, w) => s.Speaker.Ultimate != null && s.Speaker.Ultimate.IsReady);
        Add("freed", (s, w) => Time.time - s.FreedAt < s.ThanksSeconds);
        Add("revived", (s, w) => Time.time - s.RaisedAt < s.ThanksSeconds);
        Add("healed", (s, w) => Time.time - s.HealedAt < s.ThanksSeconds);
        // The class and its kit.
        Add("class", (s, w) => PlayerClass.Is(w));
        Add("taunt.ready", (s, w) => ClassKit.Local != null && ClassKit.Local.TauntReady);
        Add("charge.ready", (s, w) => ClassKit.Local != null && ClassKit.Local.ChargeReady);
        Add("syringe.empty", (s, w) => DetenteKit.Local != null && DetenteKit.Local.Empty);
        Add("supply.ready", (s, w) => SupportKit.Local != null && SupportKit.Local.Supply >= SupportKit.Local.GiveCost);
        Add("bounty", (s, w) => PingMarks.BountyOf(s.Speaker) != null);
        // The team.
        Add("team", (s, w) => Friend(s, friend => !friend.IsDead && friend.TryGetComponent(out PlayerTally tally)
            && string.Equals(tally.ClassId, w, StringComparison.OrdinalIgnoreCase)));
        Add("friend.down", (s, w) => Friend(s, friend => friend.IsDowned && !friend.IsDead));
        Add("friend.held", (s, w) => Friend(s, friend => friend.InZombieGrip && !friend.IsDead));
        Add("friend.dead", (s, w) => Friend(s, friend => friend.IsDead));
        Add("alone", (s, w) => !Friend(s, friend => true));
    }

    /// <summary>
    /// Adds a fact under this name (or replaces it): whoever knows something a line could be about says so here, once,
    /// and every table row may ask it.
    /// </summary>
    public static void Add(string name, Test test)
    {
        if (!string.IsNullOrWhiteSpace(name) && test != null) tests[name.Trim()] = test;
    }

    /// <summary>There is a fact of this name.</summary>
    public static bool Known(string name) => name != null && tests.ContainsKey(name);

    /// <summary>Every fact's name, for the console's list.</summary>
    public static IEnumerable<string> Names => tests.Keys;

    /// <summary>Whether the fact holds for this speaker now; null when there is no fact of that name.</summary>
    public static bool? Holds(CalloutSense sense, string name, string word)
    {
        if (name == null || !tests.TryGetValue(name, out var test)) return null;
        return sense != null && sense.Speaker != null && test(sense, word ?? "");
    }

    private static WeaponRuntime Gun(CalloutSense sense) => sense.Weapon != null ? sense.Weapon.Runtime : null;

    // The gun in hand has a magazine of supply or less left besides what is in it (WeaponData.SupplyRounds: a double
    // barrel's two shells are not "a magazine left").
    private static bool LowOnAmmo(CalloutSense sense)
    {
        var runtime = Gun(sense);
        return runtime != null && runtime.Data != null && runtime.ReserveAmmo <= runtime.Data.SupplyRounds;
    }

    // Someone else in the room of whom this is true.
    private static bool Friend(CalloutSense sense, Predicate<PlayerMovement> of)
    {
        foreach (var player in PlayerRoster.All)
            if (player != null && player != sense.Speaker && of(player)) return true;
        return false;
    }
}
