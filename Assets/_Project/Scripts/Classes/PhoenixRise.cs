using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// The phoenix card (불사조), everyone's but dealt only to a player alone (Perks.csv solo; the user, 2026-11-14: "솔로일 때만
/// 나와서 피닉스처럼 세컨드 찬스 주는 느낌으로", its draft name 두 번째 숨). Alone, zero health is the wipe at once, with
/// nobody to come and get them up (PlayerLife). Holding this card, the blow that would do it lifts them back to their feet
/// instead with the card's share of their health (30/50/70%), a burst of flame knocks the zombies round them down (a
/// brute, which nothing knocks down, reels), and for a moment nothing lands on them (Guards, which PlayerDamageReceiver
/// asks). A killing bite or a runner's pin closing counts as well: alone, those are how a run ends.
/// <para>
/// Once a wave, or in a chapter once between rest stops, as a Détente's Resurrection comes back (DetenteKit.Sections); a
/// new map gives it back too. So does a death: alone that is the wipe, and the try starts over from the rest stop or the
/// first wave, where the card is ready again. Only this PC's own player, and only while alone (PlayerPerks.Alone, the
/// rule the card is dealt by), so a player who will not stay down never stands beside friends who could get them up.
/// Its look and sound are PhoenixFx's. Numbers: Perks.csv phoenix (the health share), ClassAbilities.csv Cards.phoenix*.
/// </para>
/// </summary>
public static class PhoenixRise
{
    private const string Row = "Cards";

    // Until when this PC's player takes nothing, and how long that lasts.
    private static float guardUntil = -99f, guardSeconds = 1f;

    /// <summary>Seconds left of the moment nothing lands on this PC's player, and of how many (its slot among the timed effects).</summary>
    public static float GuardLeft => guardUntil - Time.time;
    public static float GuardSeconds => guardSeconds;
    // Spent, and where: the map and the run's stretch (RunRules.Section).
    private static bool spent;
    private static int spentScene = -1, spentSection = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        guardUntil = -99f;
        spent = false;
        spentScene = spentSection = -1;
    }

    /// <summary>Whether <paramref name="who"/> is this PC's player, just risen: nothing lands on them for the moment.</summary>
    public static bool Guards(PlayerMovement who) => who != null && who.IsLocal && !who.IsNetworkDriven && Time.time < guardUntil;

    /// <summary>
    /// <paramref name="player"/>'s health ran out with nobody to get them up (PlayerDamageReceiver, on their own PC). True
    /// when the card lifts them instead: <paramref name="health"/> is what they stand up with, and the burst, the guard and
    /// the look have begun.
    /// </summary>
    public static bool TryRise(PlayerMovement player, out float health)
    {
        health = 0f;
        // Someone already on the ground is the friends' to get up (PlayerRevive): the card catches a death on their feet.
        if (player == null || !player.IsLocal || player.IsNetworkDriven || player.DamageReceiver == null || player.DamageReceiver.IsDowned) return false;
        if (!PlayerPerks.Has(PlayerPerks.Phoenix) || !PlayerPerks.Alone) return false;
        if (Spent())
        {
            // Spent, they die; alone that is the wipe, and the try that follows has the card again.
            spent = false;
            return false;
        }
        Spend();
        health = player.DamageReceiver.MaximumHealth * Mathf.Clamp(PlayerPerks.Total(PlayerPerks.Phoenix), 1f, 100f) / 100f;
        float guard = Mathf.Max(0f, PlayerClass.Ability(Row, "phoenixGuard", 2f)), reach = Mathf.Max(.5f, PlayerClass.Ability(Row, "phoenixRadius", 4f));
        guardUntil = Time.time + guard;
        guardSeconds = Mathf.Max(.1f, guard);
        Scatter(player.transform.position, reach);
        PhoenixFx.Play(player, reach, guard);
        AlertHUD.Self(Texts.Get("perk.phoenix.risen"));
        return true;
    }

    // The flame's burst: every zombie within reach falls away from them; a brute, which no blow throws, reels instead.
    private static void Scatter(Vector3 middle, float reach)
    {
        float down = Mathf.Max(.1f, PlayerClass.Ability(Row, "phoenixKnockdown", 2f)), reel = Mathf.Max(0f, PlayerClass.Ability(Row, "phoenixReel", .8f));
        foreach (var enemy in EnemyHealth.Living)
        {
            if (enemy == null || !enemy.IsAlive) continue;
            Vector3 away = enemy.transform.position - middle;
            if (away.sqrMagnitude > reach * reach || !enemy.TryGetComponent(out MeleeCombatTarget target) || !target.IsAvailable) continue;
            away = Vector3.ProjectOnPlane(away, Vector3.up);
            away = away.sqrMagnitude > 1e-4f ? away.normalized : Vector3.forward;
            if (target.Steadfast) target.Interrupt(reel);
            else target.KnockDown(away, down);
        }
    }

    // Spent in this stretch of the run and on this map; a new one of either gives it back.
    private static bool Spent()
    {
        if (!spent) return false;
        if (SceneManager.GetActiveScene().handle != spentScene || RunRules.Section != spentSection) spent = false;
        return spent;
    }

    private static void Spend()
    {
        spent = true;
        spentScene = SceneManager.GetActiveScene().handle;
        spentSection = RunRules.Section;
    }
}
