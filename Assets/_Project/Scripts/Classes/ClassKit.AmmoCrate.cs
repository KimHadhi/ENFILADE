using UnityEngine;

// ClassKit: Any class: a Support's ammo crate. One part of the class; the rest is in ClassKit.cs and the files beside it.
public sealed partial class ClassKit
{
    /// <summary>Taking from an ammo crate under the crosshair, as the interact key's one owner knows it (InteractKey).</summary>
    public const string CrateOffer = "crate";

    private bool crateShown;

    // An ammo crate under the crosshair (Barricade.CrateFor): its line at the foot of the screen, and F takes a resupply
    // from it, once in a while each (Support.crateEvery); the host's crate loses a take.
    // F is shared, so the crate says what F would do at it, or why not yet, to the key's one owner (InteractKey) and
    // takes only on a press the owner calls its own. Until 2026-11-18 it read the key for itself and asked nobody, so
    // one press took from the crate and also picked up what lay beside it. While it waits it refuses, which leaves the
    // press to whatever stands behind it; its line shows while the owner says it is the word on F (Shows).
    private void Crate()
    {
        var crate = movement.IsDead || movement.IsDowned ? null : Barricade.CrateFor(movement);
        float wait = 0f;
        string waits = null;
        if (crate != null)
        {
            float every = Mathf.Max(0f, PlayerClass.Ability("Support", "crateEvery", 20f));
            wait = tookAt.TryGetValue(crate.Id, out float took) ? took + every - Time.time : 0f;
            if (wait > 0f) waits = Texts.Format("class.crate.wait", Mathf.CeilToInt(wait));
            InteractKey.Offer(CrateOffer, refusal: waits, target: crate);
        }
        if (crate == null || !InteractKey.Shows(CrateOffer))
        {
            if (crateShown) { crateShown = false; DetenteLooks.Prompt(null); }
            return;
        }
        crateShown = true;
        DetenteLooks.Prompt(waits ?? Texts.Format("class.crate.prompt", crate.Stock));
        if (wait > 0f || !InteractKey.Pressed(CrateOffer)) return;
        tookAt[crate.Id] = Time.time;
        string owner = crate.Owner != null && crate.Owner.TryGetComponent(out PlayerTally tally) ? tally.DisplayName : "";
        SupportKit.Receive(movement, Mathf.Max(1, Mathf.RoundToInt(PlayerClass.Ability("Support", "crateMagazines", 2f))),
            Mathf.Max(0f, PlayerClass.Ability("Support", "resupplyOverfill", 25f)), Mathf.Max(0f, PlayerClass.Ability("Support", "resupplySyringe", 25f)), owner);
        if (Barricade.Authority) Barricade.Take(crate.Id);
        else if (TryGetComponent(out PlayerNetworkCombat network)) network.TakeCrate(crate.Id);
    }
}
