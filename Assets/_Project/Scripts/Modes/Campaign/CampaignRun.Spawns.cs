// CampaignRun: what the horde is told of the mission, so it knows where its zombies may come from. One part of the
// class; the rest is in CampaignRun.cs and the files beside it.
public sealed partial class CampaignRun
{
    // The host, each frame before the horde is paced: where the mission stands (the steps, the one under way, the spots
    // as they stand now, who is alive). The horde's ground lays the mission's route from it, follows the squad along
    // it and keeps what is cleared (SpawnGround); the step's rule says which side they may come from (SpawnRules).
    private void FollowSpawns() => horde.Follow(steps, step, Current, spotsByName, players);

    /// <summary>
    /// The console's /spawns: how the spawn model stands on the host this moment (CampaignHorde.Describe), for the
    /// record (SpawnReport). False, with a word of why, outside a chapter and on a guest, whose PC keeps none of it.
    /// </summary>
    public static bool TrySpawnsNow(out string text)
    {
        if (active == null) { text = "캠페인 맵이 아니에요."; return false; } // dev-text
        if (!active.IsHost) { text = "좀비가 나오는 자리는 방장 PC가 정해요. 방장이 입력해 보세요."; return false; } // dev-text
        text = active.horde.Describe(active.players, active.step, active.Current);
        return true;
    }
}
