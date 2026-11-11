using System;
using UnityEngine;

/// <summary>
/// Bundle 3, GMod Horde's endless (sv_horde.lua: past its last wave, five more enemies a wave, their health and damage
/// 1.1 to the power of the waves past it), in the campaign's own form. The main menu and the rooms only ever start a map,
/// and the maps that play are the campaign's: the old wave mode (WaveManager) runs only from the /wave test command. So
/// endless is an option on the campaign's final hold (endlessStep): on, that hold doesn't end when its time is up. No
/// ride comes; waves of endlessWaveSeconds follow one another, each letting endlessExtra more be about than the step's
/// horde (before the players' growth, CampaignDirector.Extra) and making every new zombie's health and blows
/// endlessGrowth to the power of the wave (EnemyHealth.SetBoost), and every endlessBossEvery waves the step's boss slot
/// comes again. A wipe ends it as it ends any hold, back at the last rest stop. CampaignRun runs it on the host and
/// tells the room the wave. Numbers: Tables/Campaign, row Endless. The host's `endless` command turns it on or off for
/// the session; turned off during the waves, the ride comes after all.
/// </summary>
[Serializable]
public sealed class CampaignEndless
{
    [Tooltip("On: the hold endlessStep names goes on in waves when its time is up. The host's `endless` command overrides it for the session.")]
    public bool endless;
    [Tooltip("The hold that goes on (a CampaignSteps id): the campaign's final one.")]
    public string endlessStep = "Ch3.08";
    [Tooltip("How long each endless wave lasts (s).")]
    public float endlessWaveSeconds = 60f;
    [Tooltip("How many more may be about each wave, over the step's own horde (Horde: 5 more enemies a wave).")]
    public int endlessExtra = 5;
    [Tooltip("Every new zombie's health and blows, times this to the power of the wave (Horde: 1.1).")]
    public float endlessGrowth = 1.1f;
    [Tooltip("The step's boss slot comes again every this many waves (0: never).")]
    public int endlessBossEvery = 3;
    [Tooltip("The song under the waves (a Tables/Music id); empty: none.")]
    public string endlessMusic = "Endless";

    // The host's word for this session (the endless command); null: the table's.
    private static bool? chosen;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => chosen = null;

    /// <summary>Endless is on: the host's word this session, else the table's.</summary>
    public bool On => chosen ?? endless;
    /// <summary>How long a wave lasts (s).</summary>
    public float WaveSeconds => Mathf.Max(1f, endlessWaveSeconds);
    /// <summary>How many more may be about in this wave than the step's own horde.</summary>
    public int ExtraAt(int wave) => Mathf.Max(0, endlessExtra) * Mathf.Max(0, wave);
    /// <summary>A new zombie's health and blows in this wave, times this.</summary>
    public float BoostAt(int wave) => Mathf.Pow(Mathf.Max(1f, endlessGrowth), Mathf.Max(0, wave));
    /// <summary>The boss slot comes again as this wave begins.</summary>
    public bool BossAt(int wave) => endlessBossEvery > 0 && wave > 0 && wave % endlessBossEvery == 0;

    /// <summary>The `endless` command: on, off, or nothing to flip it, for this session. Only the host's word counts,
    /// since only the host runs the campaign. Returns what the console prints.</summary>
    internal static string Command(string args)
    {
        bool now = chosen ?? TableSaysOn();
        bool want;
        switch ((args ?? "").Trim().ToLowerInvariant())
        {
            case "": want = !now; break;
            case "on": case "1": case "켜기": want = true; break;
            case "off": case "0": case "끄기": want = false; break;
            default: return Texts.Format("campaign.endless.usage", State(now));
        }
        if (GameSession.IsGuest) return Texts.Format("campaign.endless.guest", State(now));
        chosen = want;
        return Texts.Format("campaign.endless.now", State(want));
    }

    private static string State(bool on) => on ? Texts.Get("campaign.endless.on") : Texts.Get("campaign.endless.off");

    // The table's word, read here so the command answers outside a chapter too (the title screen); as the tables read a
    // true/false cell.
    private static bool TableSaysOn()
    {
        if (!GameTables.TryGetRow(CampaignRun.TableName, CampaignRun.EndlessRow, out var row)) return false;
        switch (CampaignStep.Text(row, "endless").ToLowerInvariant())
        {
            case "1": case "true": case "yes": case "y": case "o": return true;
            default: return false;
        }
    }
}
