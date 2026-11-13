using UnityEngine;

// PlayerGrenades: Aiming. One part of the class; the rest is in PlayerGrenades.cs and the files beside it.
public sealed partial class PlayerGrenades
{
    // Held: the arc it will fly from where the hand lets go, and where it lands the reach of its burst; a cooking one also
    // shows where its fuse runs out along the way, if that comes first (an air burst).
    private void Aim(ThrowableInfo info)
    {
        var eye = Camera.main;
        if (eye == null || info == null) return;
        aiming = true;
        Launch(eye.transform, info, out Vector3 from, out Vector3 velocity);
        if (arc == null) arc = new ThrowArc(transform);
        arc.Trace(from, velocity, Mathf.Max(.01f, info.Size * .5f), info.Gravity);
        bool ready = Time.time >= readyAt;
        arc.Draw(ready ? KitStyle.Ice : KitStyle.With(KitStyle.Ice, .35f), ready ? KitStyle.Accent : KitStyle.With(KitStyle.Ice, .35f), ready ? .85f : .4f);
        if (info.Radius <= 0f) { arc.HideRing(); return; }
        Color reach = KitStyle.With(info.Tint, .8f);
        // The fuse left when it leaves the hand: what is left now less the swing still to come.
        float left = cookLeft - SwingLeft();
        if (cooking && left < arc.FlightSeconds)
        {
            float t = Mathf.Max(0f, left);
            Vector3 air = from + velocity * t + .5f * t * t * Physics.gravity * info.Gravity;
            arc.Ring(air, Vector3.up, info.Radius, KitStyle.With(KitStyle.Accent, .85f));
            return;
        }
        if (arc.Landed) arc.Ring(arc.Landing, arc.LandingNormal, info.Radius, reach);
        else arc.HideRing();
    }

    private void StopAim()
    {
        aiming = false;
        arc?.Hide();
        preview?.Hide();
    }
}
