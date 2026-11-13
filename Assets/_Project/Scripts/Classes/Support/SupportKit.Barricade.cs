using Unity.Netcode;
using UnityEngine;

// SupportKit: The barricade (the class key; ClassKit leaves a Support's key to this). One part of the class; the rest is in SupportKit.cs and the files beside it.
public sealed partial class SupportKit
{
    // The class key, the key window's Player/ClassAbility: held, the preview; let go (or tapped), the barricade there. The
    // guard key calls it off, and so does anything that takes the hands (a menu, a hold, going down, a counter, death).
    private void Plan()
    {
        var key = KeyBindings.Find("Player/ClassAbility");
        bool able = key != null && movement.InputAvailable && !movement.IsDead && !movement.IsDowned && !movement.IsHeld
            && !movement.CombatInputLocked;
        if (!able)
        {
            planning = false;
            return;
        }
        if (KeyBindings.Pressed(key)) planning = true;
        if (!Holds(planKind)) planKind = Barricade.Kind.Planks;
        if (!planning) return;
        // F goes on to the next kind this Support holds.
        if (interact != null && interact.WasPressedThisFrame())
        {
            var next = planKind;
            for (int i = 0; i < 4; i++)
            {
                next = (Barricade.Kind)(((int)next + 1) % 4);
                if (Holds(next)) break;
            }
            if (next != planKind) { planKind = next; KitSound.Play(KitSound.Cue.Ping, .4f); }
        }
        // The guard key (right click unless the key window says otherwise): off, and letting go then puts nothing up.
        if (KeyBindings.Pressed("Combat/Guard"))
        {
            planning = false;
            KitSound.Play(KitSound.Cue.Refuse, .25f);
            return;
        }
        planFit = Spot(out planAt, out planYaw);
        if (KeyBindings.Held(key)) return;
        planning = false;
        Build(planFit, planAt, planYaw);
    }

    // Let go: a barricade where the preview stood, by the very rule it was drawn by.
    private void Build(Barricade.Fit fit, Vector3 at, float yaw)
    {
        // A client still waiting for the host's word on the last one asks for none other meanwhile.
        if (pendingId != 0 && Time.time - pendingAt < AnswerWait)
        {
            KitSound.Play(KitSound.Cue.Refuse, .35f);
            return;
        }
        pendingId = 0;
        if (fit != Barricade.Fit.Ok)
        {
            Refuse(fit, at, yaw);
            return;
        }
        supply -= BuildCost;
        lastBuild = Time.time;
        ushort id = NextId(planKind);
        if (Barricade.Authority)
        {
            Raise(id, at, yaw);
            return;
        }
        // A client asks the host, whose world is the true one (its zombies stand where they really are). The bag pays now
        // and gets it back if the host says no; the preview stays where it was let go until the host's word comes.
        if (!movement.TryGetComponent(out PlayerNetworkCombat network) || !network.IsSpawned)
        {
            supply += BuildCost;
            Refuse(Barricade.Fit.Blocked, at, yaw);
            return;
        }
        pendingId = id;
        pendingAt = Time.time;
        pendingCost = BuildCost;
        heldSpot = at;
        heldYaw = yaw;
        network.AskBarricade(id, at, yaw, MostStanding);
    }

    // The host's or a lone PC's own: up at once, its oldest down past its most, and the others told.
    private void Raise(ushort id, Vector3 at, float yaw)
    {
        taken.Clear();
        Barricade.Raise(id, at, yaw, movement, MostStanding, taken);
        foreach (ushort old in taken) TookDown?.Invoke(old);
        KitSound.Play(KitSound.Cue.Hammer, .9f);
        Built?.Invoke(id, at, yaw);
    }

    /// <summary>
    /// The host put this PC's Support's barricade <paramref name="id"/> up (PlayerNetworkCombat, on this PC): the one it
    /// asked for stands, with its hammer. One asked for before a class pick comes down again at once.
    /// </summary>
    internal void Placed(ushort id)
    {
        if (id != 0 && id == dropped)
        {
            dropped = 0;
            Barricade.Collapse(id);
            TookDown?.Invoke(id);
            return;
        }
        if (id != pendingId) return;
        pendingId = 0;
        KitSound.Play(KitSound.Cue.Hammer, .9f);
    }

    /// <summary>The host turned barricade <paramref name="id"/> down (its world had no room there): the bag gets its share back, and why is said.</summary>
    internal void Refused(ushort id, Barricade.Fit fit)
    {
        if (id == 0 || id != pendingId) return;
        pendingId = 0;
        supply = Mathf.Min(SupplyMax, Supply + pendingCost);
        Refuse(fit, heldSpot, heldYaw);
    }

    // Not there: the refusal's sound, why on the screen, and the preview red a moment where it was let go.
    private void Refuse(Barricade.Fit fit, Vector3 at, float yaw)
    {
        KitSound.Play(KitSound.Cue.Refuse, .5f);
        AlertHUD.Self(Why(fit));
        refusedAt = Time.time;
        heldSpot = at;
        heldYaw = yaw;
    }

    /// <summary>Why a barricade can't go up there, in words (the alert, and the HUD's note while the key is held).</summary>
    public static string Why(Barricade.Fit fit)
    {
        switch (fit)
        {
            case Barricade.Fit.NoGround: return Texts.Get("class.support.fit.ground");
            case Barricade.Fit.Steep: return Texts.Get("class.support.fit.steep");
            case Barricade.Fit.Walled: return Texts.Get("class.support.fit.wall");
            case Barricade.Fit.Blocked: return Texts.Get("class.support.fit.blocked");
            case Barricade.Fit.Far: return Texts.Get("class.support.fit.far");
            case Barricade.Fit.Short: return Texts.Get("class.support.low");
            default: return Texts.Get("class.support.nowhere");
        }
    }

    // Its owner's client in the high byte, its kind in the next two bits (Barricade.KindOf), a count in the rest: no two
    // PCs make the same, and every PC builds the same kind from it.
    private ushort NextId(Barricade.Kind kind)
    {
        ulong client = movement.TryGetComponent(out NetworkObject network) && network.IsSpawned ? network.OwnerClientId : 0UL;
        made++;
        return (ushort)(((int)(client & 0xFF) << 8) | ((int)kind << 6) | (made & 0x3F));
    }

    /// <summary>
    /// The placement rule on the builder's PC, the preview's and the build's alike: it stands where the view comes down to
    /// the ground between the nearest and the farthest reach ahead (looking level or up, the farthest; at a wall, just
    /// before it; at a step, ramp or ledge, on it), across the look. There must be ground there, level enough, with
    /// nothing in its way (Barricade.Room, which the host checks again), no wall between the eyes and it, and a
    /// barricade's worth in the bag.
    /// </summary>
    private Barricade.Fit Spot(out Vector3 at, out float yaw)
    {
        var feet = movement.transform.position;
        at = feet;
        yaw = movement.transform.eulerAngles.y;
        var eyes = PlayerView.EyesOf(movement);
        if (eyes == null) return Barricade.Fit.NoGround;
        Vector3 look = eyes.forward, flat = Vector3.ProjectOnPlane(look, Vector3.up);
        if (flat.sqrMagnitude < 1e-4f) flat = Vector3.ProjectOnPlane(movement.transform.forward, Vector3.up);
        flat.Normalize();
        yaw = Quaternion.LookRotation(flat, Vector3.up).eulerAngles.y;
        float near = Mathf.Max(.8f, Number("barricadeAhead", 1.8f)), far = Mathf.Max(near, Number("barricadeReach", 3.5f));
        // How far ahead the view comes down to the floor under the feet (looking level or up: never, so the farthest) ...
        float ahead = far;
        if (look.y < -.01f) ahead = Vector3.Dot(eyes.position + look * ((eyes.position.y - feet.y) / -look.y) - feet, flat);
        // ... unless the view meets something first: ground (a step, a ramp, a ledge), or a wall it would stand just before.
        if (worldMask == 0) worldMask = Physics.DefaultRaycastLayers & ~(1 << 31) & ~LayerMask.GetMask("Enemy") & ~(1 << movement.gameObject.layer);
        if (Physics.Raycast(eyes.position, look, out RaycastHit seen, far + 3f, worldMask, QueryTriggerInteraction.Ignore)
            && !seen.collider.transform.IsChildOf(movement.transform))
        {
            float there = Vector3.Dot(seen.point - feet, flat);
            ahead = seen.normal.y >= .7f ? there : Mathf.Min(ahead, there - Barricade.SizeOf(planKind).z * .5f - .1f);
        }
        ahead = Mathf.Clamp(ahead, near, far);
        Vector3 above = feet + flat * ahead + Vector3.up * 1.2f;
        if (!Physics.Raycast(above, Vector3.down, out RaycastHit ground, 3f, ~0, QueryTriggerInteraction.Ignore))
        {
            at = feet + flat * ahead;
            return Barricade.Fit.NoGround;
        }
        at = ground.point;
        var fit = Barricade.Room(at, yaw, movement.transform, planKind);
        if (fit != Barricade.Fit.Ok) return fit;
        // Through a wall it can't go up: the eyes must see its middle (a hit right at its foot is the ground it stands on;
        // a zombie or a friend between is no wall).
        if (Physics.Linecast(eyes.position, at + Vector3.up * .5f, out RaycastHit wall, worldMask, QueryTriggerInteraction.Ignore)
            && !wall.collider.transform.IsChildOf(movement.transform) && (wall.point - at).sqrMagnitude > .36f)
            return Barricade.Fit.Walled;
        return CanBuild ? Barricade.Fit.Ok : Barricade.Fit.Short;
    }

    // The preview while the key is held: blue where it may go up, red where not, and amber over the oldest it would take
    // down. Let go, it stays where it was let go while a client waits for the host's word, and red a moment if turned down.
    private void Preview()
    {
        float alpha = Mathf.Clamp01(Number("previewAlpha", .34f));
        if (planning)
        {
            bool fits = planFit == Barricade.Fit.Ok;
            Ghost().Show(planAt, planYaw, fits ? BarricadeGhost.Look.Fits : BarricadeGhost.Look.Refused, alpha, planKind);
            var oldest = fits && Standing >= MostStanding ? Barricade.OldestOf(movement, planKind) : null;
            if (oldest != null) Doomed().Show(oldest.transform.position, oldest.transform.eulerAngles.y, BarricadeGhost.Look.Doomed, alpha, planKind);
            else if (doomed != null) doomed.Hide();
            return;
        }
        if (doomed != null) doomed.Hide();
        if (pendingId != 0 && Time.time - pendingAt < AnswerWait) Ghost().Show(heldSpot, heldYaw, BarricadeGhost.Look.Fits, alpha * 1.4f, Barricade.KindOf(pendingId));
        else if (Time.time - refusedAt < RefusedShow) Ghost().Show(heldSpot, heldYaw, BarricadeGhost.Look.Refused, alpha, planKind);
        else if (ghost != null) ghost.Hide();
    }

    private BarricadeGhost Ghost() => ghost != null ? ghost : ghost = BarricadeGhost.Make("Barricade Preview");

    private BarricadeGhost Doomed() => doomed != null ? doomed : doomed = BarricadeGhost.Make("Barricade Preview (Oldest)");

    private void HidePreview()
    {
        if (ghost != null) ghost.Hide();
        if (doomed != null) doomed.Hide();
    }

    // The friend under the crosshair within reach, standing (the Détente's way of choosing).
    private PlayerMovement Aimed()
    {
        var eyes = PlayerView.EyesOf(movement);
        if (eyes == null) return null;
        float reach = Number("resupplyReach", 3.5f), best = Number("resupplyAngle", 14f);
        PlayerMovement found = null;
        foreach (var other in PlayerRoster.All)
        {
            if (other == null || other == movement || !other.IsInitialized || other.DamageReceiver == null) continue;
            if (!other.DamageReceiver.IsAlive || other.DamageReceiver.IsDowned) continue;
            Vector3 chest = other.transform.position + Vector3.up * 1.1f, to = chest - eyes.position;
            if (to.sqrMagnitude > reach * reach) continue;
            float angle = Vector3.Angle(eyes.forward, to);
            if (angle > best) continue;
            if (Physics.Linecast(eyes.position, chest, out RaycastHit hit, ~(1 << movement.gameObject.layer), QueryTriggerInteraction.Ignore)
                && !hit.transform.IsChildOf(other.transform)) continue;
            best = angle;
            found = other;
        }
        return found;
    }
}
