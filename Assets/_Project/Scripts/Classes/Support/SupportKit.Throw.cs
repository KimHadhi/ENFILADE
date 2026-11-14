using System;
using UnityEngine;

// SupportKit: The supply pack thrown to a friend (the throw card). One part of the class; the rest is in SupportKit.cs and the files beside it.
public sealed partial class SupportKit
{
    // Where a pack leaves the hand, from the eyes (m: right, up, ahead): low on the left, tossed underarm past the gun.
    private static readonly Vector3 HandOff = new Vector3(-.2f, -.28f, .42f);

    /// <summary>
    /// The friend F would throw a supply pack to now (the throw card, 보급 꾸러미 던지기: the class key held, a standing
    /// friend under the crosshair within Support.throwReach and in sight), or null. ClassHUD names them.
    /// </summary>
    public PlayerMovement Tossable { get; private set; }

    /// <summary>What a thrown pack takes from the bag (Support.throwCost).</summary>
    public float ThrowCost => Mathf.Max(1f, Number("throwCost", 25f));

    // While the class key is held with the card: the friend a pack would go to, chosen the way the hand resupply chooses,
    // farther and narrower.
    private void AimToss()
        => Tossable = PlayerPerks.Has(PlayerPerks.SupportThrow) && !movement.IsDead && !movement.IsDowned
            ? Aimed(Mathf.Max(.5f, Number("throwReach", 8f)), Mathf.Clamp(Number("throwAngle", 10f), 1f, 45f)) : null;

    // F while the class key is held, with a friend there and the card (the user's card feedback, 2026-11-14: hold the
    // wheel, look at a friend, F throws them a pack): a pack flies to them and fills their guns as a hand resupply does
    // when it lands. This PC decides it all and the bag pays now; the others fly the same pack, and the friend's own PC
    // takes what is in it with the numbers sent from here (memory actor-decides). True when it was thrown; a bag too
    // light says so and throws nothing.
    private bool Toss()
    {
        var friend = Tossable;
        if (friend == null) return false;
        if (Supply < ThrowCost - .001f)
        {
            KitSound.Play(KitSound.Cue.Refuse, .5f);
            AlertHUD.Self(Texts.Get("class.support.low"));
            return false;
        }
        supply -= ThrowCost;
        lastGive = Time.time;
        int magazines = Mathf.Max(1, Mathf.RoundToInt(Number("resupplyMagazines", 2f)));
        float overfill = Mathf.Max(0f, Number("resupplyOverfill", 25f)), syringe = Mathf.Max(0f, Number("resupplySyringe", 25f));
        var eyes = PlayerView.EyesOf(movement);
        Vector3 from = eyes != null ? eyes.position + eyes.rotation * HandOff : movement.transform.position + Vector3.up * 1.3f;
        float seconds = Mathf.Clamp(Vector3.Distance(from, friend.transform.position + Vector3.up * 1.1f) / Mathf.Max(1f, Number("throwSpeed", 12f)),
            .2f, 1.2f);
        Tossed(movement, friend, from, seconds, magazines, overfill, syringe);
        if (movement.TryGetComponent(out PlayerNetworkCombat network) && network.IsSpawned)
            network.SendSupplyThrow(friend, from, seconds, magazines, overfill, syringe);
        // Handing a friend ammo is the Support's fight, thrown or by hand: it fills the gauge (UltimateCharge.AwardHelp).
        UltimateCharge.AwardHelp(UltimateCharge.Help.Resupplied);
        KitSound.Play(KitSound.Cue.Supply, .5f);
        AlertHUD.Self(Texts.Format("class.support.threw", NameOf(friend)));
        return true;
    }

    /// <summary>
    /// A Support's pack is on its way to <paramref name="friend"/>, thrown by <paramref name="thrower"/> from
    /// <paramref name="from"/> (on this PC, or on another as PlayerNetworkCombat tells this one): it flies here as on every
    /// PC (SupplyPack), and if the friend is this PC's own player it fills their guns when it lands, as a hand resupply
    /// does (Receive), with the numbers the Support's PC sent. If they are gone or dead by then it gives nothing.
    /// </summary>
    public static void Tossed(PlayerMovement thrower, PlayerMovement friend, Vector3 from, float seconds, int magazines, float overfill, float syringe)
    {
        if (friend == null) return;
        Action caught = null;
        if (friend.IsLocal)
        {
            string name = thrower != null ? PlayerTally.NameOf(thrower) : "";
            caught = () =>
            {
                if (friend != null && friend.DamageReceiver != null && friend.DamageReceiver.IsAlive)
                    Receive(friend, magazines, overfill, syringe, name);
            };
        }
        SupplyPack.Fly(from, friend, seconds, caught);
    }
}
