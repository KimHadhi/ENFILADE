using UnityEngine;

// DeathScreen: The words. One part of the class; the rest is in DeathScreen.cs and the files beside it.
public sealed partial class DeathScreen
{
    private DeathCard.Facts Facts(string cause, bool asWipe) => new DeathCard.Facts
    {
        Title = Texts.Get(asWipe ? "death.title.wipe" : "death.title.dead"),
        Cause = cause,
        LastedSeconds = Mathf.Max(0f, Time.time - lifeStart),
        Kills = Mathf.Max(0, (tally != null ? tally.Kills : 0) - killsAtStart),
        Money = Wallet.Balance,
    };

    private DeathCard.Facts Sample() => new DeathCard.Facts
    {
        Title = Texts.Get("death.title.dead"),
        Cause = SampleCause(ZombieKind.Brute),
        LastedSeconds = 252f,
        Kills = 23,
        Money = 1250,
        Next = Texts.Format("life.respawn.seconds", 20),
    };

    // What comes next, and how far its countdown has run down (0-1; below 0 when it has none).
    private string Next(float now, out float fill)
    {
        fill = -1f;
        if (previewing)
        {
            float left = Mathf.Max(0f, previewUntil - now);
            fill = left / Mathf.Max(.01f, look.previewSeconds);
            int seconds = Mathf.Max(1, Mathf.CeilToInt(left));
            return previewWipe ? Texts.Format(WipeKey(), seconds) : Texts.Format("life.respawn.seconds", seconds);
        }
        if (life == null) return "";
        if (wipe || !GameSession.IsOnline)
        {
            float left = life.WipeLeft;
            fill = wipeFrom > 0f ? Mathf.Clamp01(left / wipeFrom) : -1f;
            return Texts.Format(WipeKey(), Mathf.Max(1, Mathf.CeilToInt(left)));
        }
        if (TowerRun.KeepsDead) return Texts.Get("life.respawn.tower");
        if (life.WaitsForWave) return Texts.Get("life.respawn.next_wave");
        fill = respawnFrom > 0f ? Mathf.Clamp01(life.RespawnLeft / respawnFrom) : -1f;
        return Texts.Format("life.respawn.seconds", Mathf.Max(1, Mathf.CeilToInt(life.RespawnLeft)));
    }

    // Where everyone comes back after a wipe (PlayerLife.Respawn's order: the tower's hall, the campaign's last rest stop,
    // the waves from the first, the spawn point).
    private string WipeKey()
    {
        if (TowerRun.TryCheckpoint(out _)) return "death.next.wipe.tower";
        if (CampaignRun.TryCheckpoint(out _)) return "death.next.wipe.campaign";
        if (waves == null) waves = FindFirstObjectByType<WaveManager>();
        if (waves != null && waves.IsRunning) return "death.next.wipe.waves";
        return "death.next.wipe.start";
    }

    // The killer's name in their colour, of the last blow if it is recent: who put them down.
    private string RecentKiller()
    {
        if (Time.time - lastBlowAt > RecentBlow) return null;
        var source = lastBlow.Source;
        if (source == null) return null;
        var someone = source.GetComponentInParent<PlayerMovement>();
        if (someone != null) return someone == player ? null : PlayerTally.ColoredNameOf(someone);
        var enemy = source.GetComponentInParent<EnemyHealth>();
        return Pink(enemy != null ? CombatDamageEvents.DisplayNameOf(enemy) : CombatDamageEvents.DisplayNameOf(source.transform));
    }

    // The cause line of a killing blow: who (a zombie's kind in pink, an elite marked, a friend in their colour), then how.
    private string Describe(in DamageHit hit)
    {
        var source = hit.Source;
        if (source == null) return Texts.Get(hit.Kind == DamageKind.Fall ? "death.cause.fall" : "death.cause.unknown");
        var someone = source.GetComponentInParent<PlayerMovement>();
        if (someone != null)
            return someone == player ? Texts.Get("death.cause.self") : Texts.Format("death.cause.friend", PlayerTally.ColoredNameOf(someone));
        var enemy = source.GetComponentInParent<EnemyHealth>();
        string name = enemy != null ? CombatDamageEvents.DisplayNameOf(enemy) : CombatDamageEvents.DisplayNameOf(source.transform);
        bool elite = enemy != null && (enemy.IsStandout
            || enemy.TryGetComponent(out ZombieType type) && type.Kind != ZombieKind.Shambler && type.Kind != ZombieKind.Runner);
        return Line(name, elite, HowKey(hit, enemy != null && enemy.TryGetComponent(out ZombieExploder _), enemy != null));
    }

    private static string HowKey(in DamageHit hit, bool exploder, bool zombie)
    {
        switch (hit.Method)
        {
            case DamageMethod.Feed: return "death.how.feed";
            case DamageMethod.Slam: return "death.how.slam";
            case DamageMethod.Pounce: return "death.how.pounce";
            case DamageMethod.Blast: return "death.how.blast";
            case DamageMethod.Bullet: return "death.how.bullet";
            case DamageMethod.Thrown: return "death.how.thrown";
        }
        if (exploder) return "death.how.blast";
        // A runner's bite at the neck, or being eaten while held: blows that kill where they land.
        return hit.IsFatal && zombie ? "death.how.bite" : null;
    }

    private static string SampleCause(ZombieKind kind)
    {
        string how = kind switch
        {
            ZombieKind.Brute => "death.how.slam",
            ZombieKind.Exploder => "death.how.blast",
            ZombieKind.Runner => "death.how.pounce",
            ZombieKind.Shambler => "death.how.bite",
            _ => null,
        };
        return Line(Texts.Get(ZombieType.NameId(kind)), kind != ZombieKind.Shambler && kind != ZombieKind.Runner, how);
    }

    // "<name>에게 당했다", the elite's small pink tag, then how in a smaller pale type.
    private static string Line(string name, bool elite, string howKey)
    {
        string line = Texts.Format("death.cause.by", Pink(name));
        if (elite) line += "  <size=60%><mark=#" + ColorUtility.ToHtmlStringRGB(KitStyle.Accent) + "55> " + Texts.Get("death.cause.elite") + " </mark></size>";
        if (!string.IsNullOrEmpty(howKey)) line += "  <size=72%><color=#FFFFFF8C>· " + Texts.Get(howKey) + "</color></size>";
        return line;
    }

    private static string Pink(string name) => "<color=#" + ColorUtility.ToHtmlStringRGB(KitStyle.Accent) + ">" + name + "</color>";
}
