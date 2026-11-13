using UnityEngine;

// TowerRun: The end. One part of the class; the rest is in TowerRun.cs and the files beside it.
public sealed partial class TowerRun
{
    // The tower is through, on every PC: ENFILADED!! over the frozen moment, the money, the results kept for the hub,
    // the class's experience; the closing card after the banner.
    private void Finish()
    {
        if (finished) return;
        finished = true;
        float seconds = Time.time - startedAt;
        string clock = Mathf.FloorToInt(seconds / 60f).ToString("00") + ":" + Mathf.FloorToInt(seconds % 60f).ToString("00");
        EnfiladedFinale.Play(Texts.Get("tower.finale.heading"), Texts.Format("tower.finale.under", DayText(seed), clock), finaleSeconds);
        CombatFeedHUD.Award(Texts.Get("tower.feed.clear"), clearBonus);
        string today = seed.ToString();
        if (PlayerPrefs.GetString(FirstTodayKey, "") != today)
        {
            PlayerPrefs.SetString(FirstTodayKey, today);
            PlayerPrefs.Save();
            CombatFeedHUD.Award(Texts.Get("tower.feed.today"), firstTodayBonus);
        }
        PlayerRecords.ChapterEscaped(gameObject.scene.name);
        climb = ClassGrowth.Settle();
        MatchResults.Capture(gameObject.scene.name, Texts.Get("tower.card.title"), gameObject.scene.name, climb, _ => true);
        cardAt = Time.unscaledTime + finaleSeconds - .3f;
        if (tracker != null) tracker.SetObjective(new MissionObjective("tower.end", Texts.Get("tower.objective.done"), ""));
    }

    private void ShowCardWhenDue()
    {
        if (finished && Time.unscaledTime >= nextResults) { nextResults = Time.unscaledTime + .5f; MatchResults.Refresh(); }
        if (cardAt < 0f || Time.unscaledTime < cardAt) return;
        cardAt = -1f;
        bool hub = HubFlow.IsBuilt;
        float seconds = Mathf.Max(4f, outroSeconds);
        if (screen != null) screen.ShowCard(Texts.Get("tower.card.title"), Texts.Lines(Texts.Get("tower.card.outro")), seconds, hub ? null : Results());
        if (climb != null && !hub) RankClimb.Play(climb, seconds);
        climb = null;
    }

    // With no hub to show them, the results under the closing card: this player's kills and money.
    private static string Results()
    {
        var mine = PlayerRoster.Local;
        int kills = mine != null && mine.TryGetComponent(out PlayerTally own) ? own.Kills : 0;
        return Texts.Get("campaign.results.kills") + "<pos=50%>" + kills + "\n" + Texts.Get("campaign.results.money") + "<pos=50%><color=#FFD36B>"
               + Wallet.Format(Wallet.Balance) + "</color>\n";
    }

    // "11월 12일 훈련" from the day's seed.
    private static string DayText(int day) => Texts.Format("tower.day", day / 100 % 100, day % 100);
}
