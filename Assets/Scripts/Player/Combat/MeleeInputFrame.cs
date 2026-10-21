public readonly struct MeleeInputFrame
{
    public readonly bool AttackPressed, AttackHeld, AttackReleased, GuardPressed, GuardHeld, ShovePressed, ShoveHeld, ShoveReleased;
    public MeleeInputFrame(bool attackPressed = false, bool attackHeld = false, bool attackReleased = false,
        bool guardPressed = false, bool guardHeld = false, bool shovePressed = false, bool shoveHeld = false, bool shoveReleased = false)
    {
        AttackPressed = attackPressed; AttackHeld = attackHeld; AttackReleased = attackReleased;
        GuardPressed = guardPressed; GuardHeld = guardHeld;
        ShovePressed = shovePressed; ShoveHeld = shoveHeld; ShoveReleased = shoveReleased;
    }
}
