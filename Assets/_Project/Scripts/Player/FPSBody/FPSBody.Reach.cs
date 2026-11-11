/// <summary>
/// Taking something off the floor is felt in the hands (user, 2026-11-10: a pickup had no motion at all): the gun dips
/// and comes back as the body goes for the thing, in about a quarter of a second. It is the crouch's own short motion
/// played once, so it needs no clip of its own and sits with the rest of the gun's sway; with no gun in hand there is
/// nothing to dip. A hand that really lets go and reaches wants a clip, which the gun-hands stage (E) can bring.
/// </summary>
public sealed partial class FPSBody
{
    /// <summary>Something was just picked up (FieldPickups, on the taker's own PC).</summary>
    internal void Reach()
    {
        if (assets != null) Motion(assets.crouchingMotion);
    }

    /// <summary>
    /// An enemy was just pinged (PlayerPing): the gun gives a short nod, the stop's settle played once, so the press is
    /// felt. The left hand really pointing at it (the user's ask) wants a clip of its own, as reaching does.
    /// </summary>
    internal void Point()
    {
        if (assets != null) Motion(assets.stopMotion);
    }
}
