// CampaignRun: what the horde is told of the mission, so it knows where its zombies may come from. One part of the
// class; the rest is in CampaignRun.cs and the files beside it.
public sealed partial class CampaignRun
{
    // The host, each frame before the horde is paced: where the mission stands (the steps, the one under way, the spots
    // as they stand now, who is alive). The horde's ground lays the mission's route from it, follows the squad along
    // it and keeps what is cleared (SpawnGround); the step's rule says which side they may come from (SpawnRules).
    private void FollowSpawns() => horde.Follow(steps, step, Current, spotsByName, players);
}
