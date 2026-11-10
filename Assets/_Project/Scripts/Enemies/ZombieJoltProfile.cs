using IronSamiStudio.ProceduralHitReactions;
using UnityEngine;

/// <summary>
/// One row of Tables/ZombieJolt: how one part of a zombie's body jolts when it is hit. It is the bought asset's own
/// ReactionProfile (IronSami Studio's Procedural Hit Reactions: how long the reaction holds, how fast it comes and
/// goes, how much random swing it mixes in) with the numbers ZombieJolt needs to aim it for where the blow landed.
/// The table fills both by field name, so the asset's settings are tuned in the table like ours, with no profile
/// assets to make in the editor.
/// </summary>
public sealed class ZombieJoltProfile : ReactionProfile
{
    [Tooltip("Degrees the part turns along the shot wherever on it the blow landed, at full strength. Below zero, a blow near the joint folds the part toward the shooter instead (a torso doubles over a shot low in the belly), and only one further up than push / bend of its length throws it back.")]
    public float push;
    [Min(0f), Tooltip("More degrees along the shot the further from the part's joint the blow landed (this many at its far end), and as many fewer on the near side of the joint.")]
    public float bend;
    [Min(0f), Tooltip("Degrees the part twists about its own length when the blow lands off to one side (this many at its edge).")]
    public float twist;
    [Range(0f, 1f), Tooltip("The share of a blow on this part that the part next to it takes as a blow of its own (head and torso each other's, an arm's or a leg's the torso).")]
    public float follow;
}
