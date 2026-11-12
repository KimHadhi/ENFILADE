using UnityEngine;

/// <summary>
/// A class throwable bursting, by kind, on every PC (ThrownGrenade, or in the hand of a Point who held a cooked flashbang
/// too long): the frag's blast, the flashbang's stun, the Ghost's smoke, the Détente's mist. Mine: this PC's player threw
/// it, so the hits and the balance it breaks are this PC's to land; the look, the sound, the smoke and the mist are every
/// PC's own, and the host's zombies are the ones that react.
/// </summary>
public static class GrenadeBursts
{
    public static void Play(Throwable kind, Vector3 at, PlayerMovement thrower, bool mine)
    {
        var info = Throwables.Of(kind);
        if (info == null) return;
        switch (kind)
        {
            case Throwable.Frag: FragBlast.Play(at, info, thrower, mine); break;
            case Throwable.Flash: FlashBurst.Play(at, info, thrower, mine); break;
            case Throwable.Smoke: SmokeScreen.Spawn(at, info); break;
            case Throwable.Mist: MistField.Spawn(at, info); break;
        }
    }
}
