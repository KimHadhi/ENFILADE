using MovementState = PlayerMovement.MovementState;

/// <summary>What a traversal move leaves the hands for a gun.</summary>
public enum TraversalHandUse
{
    /// <summary>Not a traversal move: the usual rules hold.</summary>
    None,
    /// <summary>A step up needs no hands; both stay on the gun.</summary>
    Gun,
    /// <summary>A vault, or climbing onto a waist-high top: the gun stays in the right hand, the left plants on the obstacle.</summary>
    LeftHand,
    /// <summary>Climbing up high, hanging, wall moves, bars, ladders, cables, rolls, heavy landings and any vault begun during a reload: both hands, and the gun is put away.</summary>
    Bare,
}

/// <summary>
/// Which hands a move needs. Beatrun hides the weapon for all its climbing moves; here vaults keep it in one hand and
/// only the moves that need both hands put it away. A landing roll or a heavy landing puts both hands to the ground, so
/// the gun goes away for those too (Beatrun rolls with it in hand instead, merollgun). A vault begun during a reload finds
/// both hands on the gun, so it puts the gun away as well, and the reload stops. WeaponController hides the gun for
/// those, PlayerBodyHands keeps the right hand on it for the others.
/// </summary>
public static class TraversalHands
{
    public static TraversalHandUse For(PlayerMovement player)
    {
        if (player == null || !player.IsInitialized) return TraversalHandUse.None;
        switch (player.CurrentState)
        {
            case MovementState.Vault:
                return player.Abilities.Traversal.Obstacle.BareHanded ? TraversalHandUse.Bare : TraversalHandUse.LeftHand;
            case MovementState.Mantle:
                var kind = player.Abilities.Traversal.Obstacle.Kind;
                // Off the top of a ladder the hands are still on its rails.
                if (player.Abilities.Traversal.Obstacle.ExitLadder != null) return TraversalHandUse.Bare;
                if (kind == VaultKind.StepUp) return TraversalHandUse.Gun;
                return kind == VaultKind.Onto && !player.Abilities.Traversal.Obstacle.BareHanded
                    ? TraversalHandUse.LeftHand : TraversalHandUse.Bare;
            case MovementState.LedgeHang:
            case MovementState.LedgeJump:
            case MovementState.WallClimb:
            case MovementState.WallKick:
            case MovementState.BarGrab:
            case MovementState.BarSwing:
            case MovementState.LacheRelease:
            case MovementState.Ladder:
            case MovementState.Zipline:
            case MovementState.LandingRoll:
            case MovementState.HeavyLanding:
                return TraversalHandUse.Bare;
            default:
                return TraversalHandUse.None;
        }
    }
}
