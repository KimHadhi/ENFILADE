using UnityEngine;

/// <summary>
/// The layers things lie on, said once. The project uses three: Default (the built world, and most of what is made while
/// playing), Enemy (living zombies) and the players' own (31), which also carries what the players walk through: the dead
/// (a zombie's or a player's doll), gore, thrown things and dropped magazines, so that everything the players test their
/// movement and camera against leaves them out (ZombieCorpse, GoreGibs.Layer). A fourth (OwnHead) holds nothing between
/// frames: only this PC's own hair and hat while its own view renders them (OwnHeadView).
/// "The world a ray stops against" was written out as a mask of its own wherever a throw, a strike, a sight cone or a
/// placement needed it (the investigation of 2026-11-18 found five copies, and one placement rule that tested every layer
/// instead: a corpse blocked a barricade). They ask here now.
/// A layer says where something lies, not what it is: living players share theirs with the dead. Whoever has to tell
/// the two apart asks what the thing is (Placement.Of).
/// </summary>
public static class WorldLayers
{
    /// <summary>The players' own layer, and the layer of what they walk through (the dead, gore, thrown things).</summary>
    public const int Passed = 31;

    /// <summary>
    /// This PC's own hair, ears and hat in first person, for the moment the player's own view renders and no longer (OwnHeadView
    /// draws them from it). The renderer assets (PC_Renderer, Mobile_Renderer) leave it out of their own opaque and transparent
    /// passes, and nothing lies on it otherwise, so no ray or other camera ever meets it.
    /// </summary>
    public const int OwnHead = 30;

    private static int enemy = -2, built;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        enemy = -2;
        built = 0;
    }

    /// <summary>The living zombies' layer (-1 in a project without one).</summary>
    public static int Enemy => enemy != -2 ? enemy : enemy = LayerMask.NameToLayer("Enemy");

    /// <summary>
    /// The built world to a ray: every layer a ray meets by default but the players' own (their bodies, the dead and what
    /// lies loose) and the living zombies'. What a throw lands on, what hides one thing from another, what a strike falls on.
    /// </summary>
    public static int Built
    {
        get
        {
            if (built != 0) return built;
            built = Physics.DefaultRaycastLayers & ~(1 << Passed);
            if (Enemy >= 0) built &= ~(1 << Enemy);
            return built;
        }
    }
}
