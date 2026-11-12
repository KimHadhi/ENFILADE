using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// A finished chapter's numbers, kept past the scene that made them (memo 37): the chapter ends, everyone goes back to the
/// hub (HubFlow), and the results are played there over the players themselves (ResultsShow). Every PC takes its own copy
/// from what it knows of every player (PlayerTally, whose numbers each owner's PC shares through PlayerNetworkScore),
/// when the chapter ends and again while the closing card stays up, so the last moments' numbers are in. The players of
/// a scene are made again in the next, so this is the one place the numbers live on. Static, per PC, until shown.
/// </summary>
public static class MatchResults
{
    /// <summary>One player's line of a finished chapter.</summary>
    public sealed class Entry
    {
        /// <summary>Their seat in the room (Netcode client id); 0 alone.</summary>
        public ulong Client;
        public bool IsLocal;
        public string Name = "", ClassId = "";
        public Color Color = Color.white;
        public int Kills, Executions, Downs, Deaths, Headshots, Revives, BestStreak, Takedowns;
        public float Damage, Healing, Farthest;
        public long Earned;
        /// <summary>On the ride when it left (or there was no ride to miss).</summary>
        public bool Escaped = true;
        /// <summary>Their skin, colour set and outfit (OutfitLook.Join), so the results can stand a copy of them posing
        /// whether or not they are still in the room (ResultsMannequin).</summary>
        public string Look = "";
    }

    private static readonly List<Entry> entries = new List<Entry>();

    /// <summary>Waiting to be shown in the hub.</summary>
    public static bool Pending { get; private set; }
    /// <summary>The chapter's scene, its title, the scene after it (empty after the last) and how long it took (s).</summary>
    public static string Chapter { get; private set; } = "";
    public static string Title { get; private set; } = "";
    public static string Next { get; private set; } = "";
    public static float Seconds { get; private set; }
    /// <summary>The last chapter was done: the campaign is over.</summary>
    public static bool CampaignDone => string.IsNullOrEmpty(Next);
    /// <summary>What the chapter did to this PC's class (stage 6E), shown climbing in the results; null for none.</summary>
    public static ClassGrowth.Climb Climb { get; private set; }
    /// <summary>Everyone in the chapter as it ended, in seat order.</summary>
    public static IReadOnlyList<Entry> Entries => entries;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        entries.Clear();
        Pending = false;
        Chapter = Title = Next = "";
        Seconds = 0f;
        Climb = null;
    }

    /// <summary>
    /// The chapter is over (CampaignRun.ShowFinish): takes everyone's numbers now. <paramref name="aboard"/> tells who made
    /// the ride (null: everyone did).
    /// </summary>
    public static void Capture(string chapter, string title, string next, ClassGrowth.Climb climb, System.Func<PlayerMovement, bool> aboard)
    {
        Chapter = chapter ?? "";
        Title = title ?? "";
        Next = next ?? "";
        Climb = climb;
        Seconds = Time.timeSinceLevelLoad;
        entries.Clear();
        foreach (var someone in PlayerRoster.All)
        {
            if (someone == null || !(someone.IsLocal || someone.IsNetworkDriven) || !someone.TryGetComponent(out PlayerTally tally)) continue;
            var entry = new Entry { Client = SeatOf(someone), IsLocal = someone.IsLocal, Escaped = aboard == null || aboard(someone) };
            Read(entry, tally);
            entries.Add(entry);
        }
        entries.Sort((a, b) => a.Client.CompareTo(b.Client));
        Pending = true;
    }

    /// <summary>The numbers again, for the players still here (CampaignRun, while the closing card is up): late kills,
    /// and a teammate's last numbers on their way over the network.</summary>
    public static void Refresh()
    {
        if (!Pending) return;
        foreach (var someone in PlayerRoster.All)
        {
            if (someone == null || !someone.TryGetComponent(out PlayerTally tally)) continue;
            ulong seat = SeatOf(someone);
            foreach (var entry in entries)
                if (entry.Client == seat && entry.IsLocal == someone.IsLocal) { Read(entry, tally); break; }
        }
    }

    /// <summary>
    /// The console's /results before any chapter has ended in this session: everyone here, with what they have done so
    /// far, so the show (and a pose touched up in Blender, ArtSource/Poses) can be seen at once. It names the chosen
    /// chapter as the next one, so the hub never takes it for the campaign's end (HubLobby clears the perks then).
    /// </summary>
    public static bool Preview(string title)
    {
        string next = HubFlow.Chosen;
        Capture("", title, string.IsNullOrEmpty(next) ? "Preview" : next, null, null);
        return entries.Count > 0;
    }

    /// <summary>Shown (or no longer wanted): nothing waits for the hub any more.</summary>
    public static void Clear() => Pending = false;

    /// <summary>The results again, for the console's /results (they stay after being shown until the next chapter ends).</summary>
    public static bool Replay()
    {
        if (entries.Count == 0) return false;
        Pending = true;
        return true;
    }

    /// <summary>The seat of a player in the room, or 0 alone.</summary>
    public static ulong SeatOf(PlayerMovement someone)
        => someone != null && someone.TryGetComponent(out NetworkObject network) && network.IsSpawned ? network.OwnerClientId : 0UL;

    private static void Read(Entry entry, PlayerTally tally)
    {
        entry.Name = string.IsNullOrEmpty(tally.DisplayName) ? tally.name : tally.DisplayName;
        entry.ClassId = tally.ClassId;
        entry.Color = tally.Color;
        entry.Kills = tally.Kills; entry.Executions = tally.Executions; entry.Downs = tally.Downs; entry.Deaths = tally.Deaths;
        entry.Headshots = tally.Headshots; entry.Revives = tally.Revives; entry.BestStreak = tally.BestStreak;
        entry.Takedowns = tally.Takedowns; entry.Damage = tally.Damage; entry.Healing = tally.Healing;
        entry.Farthest = tally.FarthestHeadshot; entry.Earned = tally.Earned;
        string look = LookOf(tally);
        if (look.Length > 0) entry.Look = look;
    }

    // What the room was told this player looks like; alone, built as PlayerNetworkPose would send it.
    private static string LookOf(PlayerTally tally)
    {
        if (tally.TryGetComponent(out PlayerNetworkPose pose) && pose.IsSpawned && !string.IsNullOrEmpty(pose.Look)) return pose.Look;
        if (!tally.TryGetComponent(out PlayerMovement someone) || !someone.IsLocal) return "";
        var settings = GameSettings.Current;
        string skinId = PlayerBodyProfile.Load()?.CurrentSkin?.Id ?? settings.characterSkin;
        return OutfitLook.Join(PlayerBodyProfile.LookId(settings.characterSkin, settings.characterVariant), OutfitLook.SavedText(skinId));
    }
}
