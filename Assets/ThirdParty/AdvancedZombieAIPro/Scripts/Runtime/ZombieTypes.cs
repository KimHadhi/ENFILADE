using System;

namespace AdvancedZombieAIPro
{
    public enum ZombieStateType
    {
        Idle,
        Wander,
        Alert,
        Chase,
        Search,
        Inspect,
        Stun,
        Hit,
        Attack,
        Eat,
        Dead
    }

    public enum ZombieWanderMode
    {
        Random,
        Waypoint
    }

    public enum ZombieAttackKind
    {
        StandAttack,
        ChaseAttack
    }

    public enum ZombieHitReactionKind
    {
        StandHit,
        ChaseHit
    }

    public enum ZombieDeathMode
    {
        Animation,
        Ragdoll
    }

    public enum ZombieMode
    {
        Standing,
        Crawling
    }

    [Flags]
    public enum ZombieVisionRegion
    {
        None = 0,
        Top = 1 << 0,
        Bottom = 1 << 1,
        Center = 1 << 2,
        Left = 1 << 3,
        Right = 1 << 4
    }
}
