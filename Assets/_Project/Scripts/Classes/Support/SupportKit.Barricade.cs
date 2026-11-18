using Unity.Netcode;
using UnityEngine;

// SupportKit: The barricade (the class key; ClassKit leaves a Support's key to this). One part of the class; the rest is in SupportKit.cs and the files beside it.
public sealed partial class SupportKit
{
    // The class key, the key window's Player/ClassAbility: held, the preview; let go (or tapped), the barricade there. The
    // guard key calls it off, and so does anything that takes the hands (a menu, a hold, going down, a counter, death),
    // and so does a supply pack thrown meanwhile (the throw card: F on a friend while it is held).
    private void Plan()
    {
        Tossable = null;
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
        // F throws a supply pack to the friend under the crosshair (the throw card), else goes on to the next kind this
        // Support holds.
        AimToss();
        if (interact != null && interact.WasPressedThisFrame())
        {
            if (Tossable != null)
            {
                // Thrown, the key's hold was for the throw: letting go puts nothing up.
                if (Toss())
                {
                    planning = false;
                    Tossable = null;
                    return;
                }
            }
            else
            {
                var next = planKind;
                for (int i = 0; i < 4; i++)
                {
                    next = (Barricade.Kind)(((int)next + 1) % 4);
                    if (Holds(next)) break;
                }
                if (next != planKind) { planKind = next; KitSound.Play(KitSound.Cue.Ping, .4f); }
            }
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

    // Let go: a barricade where the preview stood, by the very rule it was drawn by, up at once on this PC whoever plays
    // it, the host, a guest or someone alone (the user, 2026-11-14, memory actor-decides: "본인이 계산하고 결과만 넘기는
    // 방식으로"; a guest used to ask the host, wait for its word and sometimes be turned down for a spot its own screen had
    // shown blue). Its oldest of the kind come down past the most this Support's own count allows, and the others are
    // told both (PlayerNetworkCombat) with the health it went up with, so the host puts it up as told and never judges it.
    private void Build(Barricade.Fit fit, Vector3 at, float yaw)
    {
        // What the rule met there goes on record either way (Logs/placement.txt): the collider that stood in the way of a
        // refusal, and what lay on the spot and gave way (a corpse under a barricade that went up reads "passed corpse").
        Placement.Record(planKind.ToString(), fit.ToString(), fit == Barricade.Fit.Ok, at, yaw, Barricade.SizeOf(planKind), planBy, movement.transform);
        if (fit != Barricade.Fit.Ok)
        {
            Refuse(fit, at, yaw);
            return;
        }
        supply -= BuildCost;
        lastBuild = Time.time;
        ushort id = NextId(planKind);
        float health = MostHealthOf(planKind);
        taken.Clear();
        Barricade.Raise(id, at, yaw, movement, MostStanding, taken, health);
        foreach (ushort old in taken) TookDown?.Invoke(old);
        KitSound.Play(KitSound.Cue.Hammer, .9f);
        Raised?.Invoke(id, at, yaw, health);
    }

    /// <summary>
    /// The health a structure of <paramref name="kind"/> goes up with, as this Support's PC reckons it and sends it with
    /// the build: the table's (Barricade.BaseHealth), and with the fortify card (요새화) its share more. An ammo crate's
    /// health is its stock of takes rather than how much it stands, so the card leaves it alone.
    /// </summary>
    public float MostHealthOf(Barricade.Kind kind)
    {
        float health = Barricade.BaseHealth(kind);
        if (kind != Barricade.Kind.Crate) health *= 1f + Mathf.Max(0f, PlayerPerks.Total(PlayerPerks.SupportFortify)) / 100f;
        return Mathf.Max(1f, Mathf.Round(health));
    }

    // Not there (this PC's own rule said no): the refusal's sound, why on the screen, and the preview red a moment where it
    // was let go.
    private void Refuse(Barricade.Fit fit, Vector3 at, float yaw)
    {
        KitSound.Play(KitSound.Cue.Refuse, .5f);
        AlertHUD.Self(Why(fit, planBy));
        refusedAt = Time.time;
        heldSpot = at;
        heldYaw = yaw;
    }

    /// <summary>
    /// Why nothing can go up where the preview stands now, in words (the HUD's note while the key is held), naming what is
    /// in the way where the placement rule knows it.
    /// </summary>
    public string PlanWhy => Why(planFit, planBy);

    /// <summary>
    /// Why it can't go up there, in words, with what stood in the way named: a zombie, a friend or another structure
    /// (Placement says what <paramref name="by"/> is). Since the dead and what lies loose no longer block, a refusal on
    /// open floor always has one of these to point at, and "자리가 막혀 있다" alone is left for the map's own walls and props.
    /// </summary>
    public static string Why(Barricade.Fit fit, Collider by)
    {
        if (fit != Barricade.Fit.Blocked || by == null) return Why(fit);
        switch (Placement.Of(by))
        {
            case Placement.Thing.Structure: return Texts.Get("class.support.fit.structure");
            case Placement.Thing.Body:
                return Texts.Get(by.GetComponentInParent<EnemyHealth>() != null ? "class.support.fit.zombie" : "class.support.fit.friend");
            default: return Why(fit);
        }
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
    /// The placement rule on the builder's PC, the preview's and the build's alike, and the only one (no other PC checks it
    /// again): it stands where the view comes down to the ground between the nearest and the farthest reach ahead (looking
    /// level or up, the farthest; at a wall, just before it; at a step, ramp or ledge, on it), across the look. There must
    /// be ground there, level enough, with nothing in its way (Barricade.Room), no wall between the eyes and it, and a
    /// barricade's worth in the bag. What counts as ground, as a wall and as being in the way is Placement's to say, the
    /// same for everything a player sets down: a corpse, gore or anything lying loose on the spot is none of them (the
    /// user, 2026-11-18: "시체 콜라이더로 인해 서포트 구조물 … 설치가 막히는 판정 수정").
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
        // What the view meets, what is ground and what is in the way are the one placement rule's to say (Placement): the
        // dead and what lies loose are looked past and built over.
        var self = movement.transform;
        planBy = null;
        if (Placement.Ray(eyes.position, look, far + 3f, self, Placement.Built, out RaycastHit seen) != Placement.Thing.None)
        {
            float there = Vector3.Dot(seen.point - feet, flat);
            ahead = seen.normal.y >= .7f ? there : Mathf.Min(ahead, there - Barricade.SizeOf(planKind).z * .5f - .1f);
        }
        ahead = Mathf.Clamp(ahead, near, far);
        Vector3 above = feet + flat * ahead + Vector3.up * 1.2f;
        if (Placement.Ray(above, Vector3.down, 3f, self, Placement.Solid, out RaycastHit ground) == Placement.Thing.None)
        {
            at = feet + flat * ahead;
            return Barricade.Fit.NoGround;
        }
        at = ground.point;
        var fit = Barricade.Room(at, yaw, self, planKind, out planBy);
        if (fit != Barricade.Fit.Ok) return fit;
        // Through a wall it can't go up: the eyes must see its middle (a hit right at its foot is the ground it stands on;
        // a zombie or a friend between is no wall).
        Vector3 middle = at + Vector3.up * .5f, toMiddle = middle - eyes.position;
        if (Placement.Ray(eyes.position, toMiddle, toMiddle.magnitude, self, Placement.Built, out RaycastHit wall) != Placement.Thing.None
            && (wall.point - at).sqrMagnitude > .36f)
        {
            planBy = wall.collider;
            return Barricade.Fit.Walled;
        }
        return CanBuild ? Barricade.Fit.Ok : Barricade.Fit.Short;
    }

    // The preview while the key is held: blue where it may go up, red where not, and amber over the oldest it would take
    // down. Let go where it can't go up, it stays red a moment there.
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
        if (Time.time - refusedAt < RefusedShow) Ghost().Show(heldSpot, heldYaw, BarricadeGhost.Look.Refused, alpha, planKind);
        else if (ghost != null) ghost.Hide();
    }

    private BarricadeGhost Ghost() => ghost != null ? ghost : ghost = BarricadeGhost.Make("Barricade Preview");

    private BarricadeGhost Doomed() => doomed != null ? doomed : doomed = BarricadeGhost.Make("Barricade Preview (Oldest)");

    private void HidePreview()
    {
        if (ghost != null) ghost.Hide();
        if (doomed != null) doomed.Hide();
    }

    // The friend under the crosshair within reach, standing (the Détente's way of choosing): within the hand resupply's
    // reach and angle, or a thrown pack's (Toss).
    private PlayerMovement Aimed() => Aimed(Number("resupplyReach", 3.5f), Number("resupplyAngle", 14f));

    private PlayerMovement Aimed(float reach, float best)
    {
        var eyes = PlayerView.EyesOf(movement);
        if (eyes == null) return null;
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
