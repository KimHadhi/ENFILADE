using UnityEngine;

// ClassKit: Any class: a Support's ammo crate. One part of the class; the rest is in ClassKit.cs and the files beside it.
public sealed partial class ClassKit
{
    private bool crateShown;

    private UnityEngine.InputSystem.InputAction interact;

    // An ammo crate under the crosshair (Barricade.CrateFor): its line at the foot of the screen, and F takes a resupply
    // from it, once in a while each (Support.crateEvery); the host's crate loses a take.
    private void Crate()
    {
        var crate = Barricade.CrateFor(movement);
        if (crate == null)
        {
            if (crateShown) { crateShown = false; DetenteLooks.Prompt(null); }
            return;
        }
        float every = Mathf.Max(0f, PlayerClass.Ability("Support", "crateEvery", 20f));
        float wait = tookAt.TryGetValue(crate.Id, out float took) ? took + every - Time.time : 0f;
        crateShown = true;
        DetenteLooks.Prompt(wait > 0f ? Texts.Format("class.crate.wait", Mathf.CeilToInt(wait)) : Texts.Format("class.crate.prompt", crate.Stock));
        if (interact == null && UnityEngine.InputSystem.InputSystem.actions != null)
            interact = UnityEngine.InputSystem.InputSystem.actions.FindAction("Player/Interact", false);
        if (wait > 0f || interact == null || !interact.WasPressedThisFrame() || !movement.InputAvailable || movement.IsDead || movement.IsDowned) return;
        tookAt[crate.Id] = Time.time;
        string owner = crate.Owner != null && crate.Owner.TryGetComponent(out PlayerTally tally) ? tally.DisplayName : "";
        SupportKit.Receive(movement, Mathf.Max(1, Mathf.RoundToInt(PlayerClass.Ability("Support", "crateMagazines", 2f))),
            Mathf.Max(0f, PlayerClass.Ability("Support", "resupplyOverfill", 25f)), Mathf.Max(0f, PlayerClass.Ability("Support", "resupplySyringe", 25f)), owner);
        if (Barricade.Authority) Barricade.Take(crate.Id);
        else if (TryGetComponent(out PlayerNetworkCombat network)) network.TakeCrate(crate.Id);
    }
}
