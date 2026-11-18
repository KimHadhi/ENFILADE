using UnityEngine;

// PlayerGrenades: Aiming. One part of the class; the rest is in PlayerGrenades.cs and the files beside it.
public sealed partial class PlayerGrenades
{
    // Held: what it will do once the hand lets go, drawn from where it leaves the hand. A grenade's line is flown by the
    // flight the thrown one itself is moved by (GrenadeFlight), for the fuse it would leave the hand with: it turns at every
    // wall and floor it bounces off, each bounce marked, and ends where it bursts, on the ground or in the air, with the
    // reach of its burst there (the user, 2026-11-18: "벽이나 오브젝트에 튕겨 나오는 예상 바운스 궤적 표시"). The line used to
    // stop at the first thing it met and put the burst there, where the grenade had only begun to bounce. A knife has no
    // fuse and sticks where it first lands: its line is traced to there.
    private void Aim(ThrowableInfo info)
    {
        var eye = Camera.main;
        if (eye == null || info == null) return;
        aiming = true;
        Launch(eye.transform, info, out Vector3 from, out Vector3 velocity);
        if (arc == null) arc = new ThrowArc(transform);
        bool ready = Time.time >= readyAt;
        Color line = ready ? KitStyle.Ice : KitStyle.With(KitStyle.Ice, .35f), end = ready ? KitStyle.Accent : KitStyle.With(KitStyle.Ice, .35f);
        if (info.Fuse <= 0f)
        {
            arc.Trace(from, velocity, Mathf.Max(.01f, info.Size * .5f), info.Gravity);
            arc.Draw(line, end, ready ? .85f : .4f);
            arc.HideRing();
            return;
        }
        // The fuse it would leave the hand with: a cooking one's now, less the swing still to come (its whole fuse while the
        // pin is not out yet).
        float fuse = FuseOutOfHand(info, cooking ? cookLeft - SwingLeft() : info.Fuse);
        arc.Fly(from, velocity, fuse, GrenadeFlight.BodyOf(info), GrenadeFlight.RulesNow);
        arc.Draw(line, end, ready ? .85f : .4f);
        if (info.Radius <= 0f) { arc.HideRing(); return; }
        // A cooking one's reach in the HUD's pink (its fuse is running: this is where it goes off), the others' in their own colour.
        arc.Ring(arc.Landing, arc.LandingNormal, info.Radius, cooking ? KitStyle.With(KitStyle.Accent, .85f) : KitStyle.With(info.Tint, .8f));
    }

    private void StopAim()
    {
        aiming = false;
        arc?.Hide();
        preview?.Hide();
    }
}
