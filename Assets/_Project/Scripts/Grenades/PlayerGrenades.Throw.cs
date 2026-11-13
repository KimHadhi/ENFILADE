using UnityEngine;

// PlayerGrenades: The throw. One part of the class; the rest is in PlayerGrenades.cs and the files beside it.
public sealed partial class PlayerGrenades
{
    private void Throw(ThrowableInfo info)
    {
        var eye = Camera.main;
        if (eye == null || info == null || count <= 0) { cooking = false; return; }
        Launch(eye.transform, info, out Vector3 from, out Vector3 velocity);
        count--;
        readyAt = Time.time + Cooldown;
        ushort id = NextId();
        // A cooked one flies with what is left of its fuse; one thrown on reflex keeps at least half the reflex's lead, so
        // even a stalled frame can't make it burst at the hand.
        float fuse = !info.Cooks ? info.Fuse
            : info.ThrowsOut ? Mathf.Max(cookLeft, Throwables.Rule("reflexLead", .5f) * .5f) : Mathf.Max(0f, cookLeft);
        cooking = holding = letGo = false;
        StopAim();
        Throwables.NoteThrower(movement, armKind);
        if (armKind == Throwable.Knife)
        {
            var knife = ThrownKnife.Throw(id, from, velocity, movement, true);
            if (knife != null) knives[id] = knife;
            Throwables.Play("KnifeThrow", from, .9f, 1.5f, 20f);
            Throwables.Play("KnifeThrowCloth", from, .8f, 1.5f, 15f);
        }
        else
        {
            var grenade = ThrownGrenade.Throw(armKind, id, from, velocity, fuse, movement, true, OnOwnBurst);
            if (grenade != null) flying[id] = grenade;
            Throwables.Play("Throw", from, .8f, 1.5f, 20f);
        }
        Thrown?.Invoke(armKind, id, from, velocity, fuse);
        Feel();
        PlayerCallouts.Thrown(info.Callout);
    }

    private void OnOwnBurst(ThrownGrenade grenade)
    {
        if (grenade == null) return;
        flying.Remove(grenade.Id);
        Burst?.Invoke(grenade.Kind, grenade.Id, grenade.Position);
    }

    private static float Cooldown => Mathf.Max(0f, Throwables.Rule("cooldown", .35f));

    private ushort NextId()
    {
        if (nextId == 0) nextId = 1;
        return nextId++;
    }

    // From where the hand lets go (GrenadeArm: the path's release on this body), toward what the crosshair is on, so it goes
    // where the player looks; a knife nearly straight. Without an arm (no humanoid body), from beside the eyes.
    private void Launch(Transform eye, ThrowableInfo info, out Vector3 from, out Vector3 velocity)
    {
        if (armBody == null) armBody = GrenadeArm.Of(movement);
        if (armBody == null || !armBody.TryReleasePoint(out from))
            from = eye.position + eye.forward * .35f - eye.right * .16f - eye.up * .12f;
        if (Physics.Linecast(eye.position, from, out RaycastHit wall, Throwables.WorldMask, QueryTriggerInteraction.Ignore))
            from = wall.point - (from - eye.position).normalized * .08f;
        Vector3 target = Physics.Raycast(eye.position, eye.forward, out RaycastHit seen, 80f, Throwables.WorldMask, QueryTriggerInteraction.Ignore)
            ? seen.point : eye.position + eye.forward * 80f;
        Vector3 way = (target - eye.position).sqrMagnitude > 4f ? (target - from).normalized : eye.forward;
        Vector3 carried = movement.ActualVelocity;
        carried.y = 0f;
        velocity = way * info.Speed + Vector3.up * info.Lift + carried * .5f;
    }

    private Vector3 HandPoint()
    {
        var eye = Camera.main;
        if (eye == null) return transform.position + Vector3.up * 1.3f;
        var view = eye.transform;
        return view.position + view.forward * .45f - view.right * .16f - view.up * .25f;
    }

    // The throw felt: the gun nods as the left hand goes, and the view gives a small kick that way; a knife's flick
    // dips the view and rolls it a little, as GMod's [MW] Quick Throwing Knife does (ViewPunch 2 down, a short roll).
    private void Feel()
    {
        if (weapon == null) weapon = GetComponent<WeaponController>();
        if (weapon != null) FPSBody.Holding(weapon)?.Point();
        var eye = Camera.main;
        if (eye == null) return;
        if (armKind == Throwable.Knife)
        {
            // GMod's [MW] Quick Throwing Knife: ViewPunch 2 down, the viewmodel camera's turn times 1.5, and a roll
            // that shakes back over 0.15 s (GrenadeRules knifeKick, knifeRoll).
            CameraViewPunch.For(eye)?.Punch(new Vector3(Throwables.Rule("knifeKick", 2.5f), -.8f, Throwables.Rule("knifeRoll", 2f)));
            knifeShakeAt = Time.time + .08f;
        }
        else CameraViewPunch.For(eye)?.Punch(new Vector3(-1.2f, -.9f, 2f));
    }

    // A knife drawn for the throw: the blade and the sleeve's sounds together, as the addon's pull-out.
    private static void KnifeOut(Vector3 at)
    {
        Throwables.Play("KnifeDraw", at, .8f, 1.2f, 12f);
        Throwables.Play("KnifeDrawCloth", at, .7f, 1.2f, 10f);
    }
}
