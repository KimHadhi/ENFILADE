using UnityEngine;

// SupportKit: Mending (F held on a worn barricade). One part of the class; the rest is in SupportKit.cs and the files beside it.
public sealed partial class SupportKit
{
    // The barricade under the crosshair within reach, anyone's (a sapper mends whatever stands), worn or whole, or null.
    // Barbed wire is a trigger (walked through), so the look takes triggers too, but only a barricade's: any other trigger
    // (a zone, a pickup's reach) is looked through, and anything solid nearer hides what is behind it. An ammo crate is
    // left to its own line (ClassKit.Crate).
    private Barricade AimedBarricade()
    {
        var eyes = PlayerView.EyesOf(movement);
        if (eyes == null) return null;
        float reach = Mathf.Max(.5f, Number("repairReach", 3.5f));
        int count = Physics.RaycastNonAlloc(eyes.position, eyes.forward, aimHits, reach, ~(1 << movement.gameObject.layer), QueryTriggerInteraction.Collide);
        Barricade found = null;
        float nearest = float.MaxValue;
        for (int i = 0; i < count; i++)
        {
            var hit = aimHits[i];
            if (hit.collider == null || hit.distance >= nearest) continue;
            var barricade = hit.collider.GetComponentInParent<Barricade>();
            if (hit.collider.isTrigger && barricade == null) continue;
            nearest = hit.distance;
            found = barricade;
        }
        return found != null && found.Type != Barricade.Kind.Crate && found.Health > 0f ? found : null;
    }

    // The tool at work: the hammer at its pace, or the torch faster while its fuel lasts (spent by the health put in;
    // empty, the hammer takes over). The bag pays nothing. The host's or a lone PC's mends at once; a client's adds it up
    // and tells the host every so often (FlushMend), whose word brings the planks back on this copy.
    private void Mend(Barricade barricade)
    {
        bool torch = Torching;
        float rate = Mathf.Max(0f, torch ? Number("torchRate", 120f) : Number("repairRate", 40f));
        if (mendId != barricade.Id) FlushMend();
        float amount = Mathf.Min(rate * Time.deltaTime, barricade.MostHealth - barricade.Health - mendOwed);
        if (torch)
        {
            float cost = Mathf.Max(0f, Number("torchCost", 15f)) / 100f;
            if (cost > 0f) amount = Mathf.Min(amount, TorchFuel / cost);
            torchFuel = Mathf.Max(0f, TorchFuel - Mathf.Max(0f, amount) * cost);
            if (torchFuel <= .5f) torchDry = true;
        }
        if (amount <= 0f) return;
        // Keeping the team's wall up is the Support's fight too: it fills the gauge (UltimateCharge.AwardHelp).
        UltimateCharge.AwardHelp(UltimateCharge.Help.Repaired, amount);
        // The hammer knocks where its head meets the planks, at its stroke's strike on every PC (RepairLooks, RepairArm); the
        // torch hisses by itself.
        if (Barricade.Authority)
        {
            Barricade.Mend(barricade.Id, amount);
            return;
        }
        mendId = barricade.Id;
        mendOwed += amount;
    }

    // The tool in hand, here and in the others' sight, when it changes.
    private void Show(RepairLooks.Tool tool)
    {
        if (tool == shownTool) return;
        shownTool = tool;
        RepairLooks.Hold(movement, tool);
        ToolHeld?.Invoke((byte)tool);
    }

    // The barricade under the crosshair, at the foot of the screen on the line the ammo crate uses (DetenteLooks.Prompt),
    // where the eyes are: the tool, F held, its health, and whether it is whole or under the tool. An offered treatment's
    // answer goes first. With nothing aimed at the line is let go, unless another's has taken it (a crate's, an offer's).
    private void Line()
    {
        string text = AimedAt != null && !DetenteKit.Answering ? LineFor(AimedAt) : null;
        if (text != null)
        {
            lineShown = true;
            DetenteLooks.Prompt(text);
            return;
        }
        if (!lineShown) return;
        lineShown = false;
        if (!DetenteKit.Answering && Barricade.CrateFor(movement) == null) DetenteLooks.Prompt(null);
    }

    // What the line says: whole, worn, or under the tool; with the torch card its fuel, or how full it is again while
    // the hammer stands in for it (Texts: class.hud.repair*).
    private string LineFor(Barricade barricade)
    {
        int health = Mathf.RoundToInt(barricade.Health), most = Mathf.RoundToInt(barricade.MostHealth);
        bool whole = !barricade.Worn;
        if (Torching)
            return Texts.Format(whole ? "class.hud.repair.full.torch" : Mending ? "class.hud.repairing.torch" : "class.hud.repair.torch",
                health, most, Mathf.FloorToInt(TorchFuel));
        if (TorchDry)
            return Texts.Format(whole ? "class.hud.repair.full.dry" : Mending ? "class.hud.repairing.dry" : "class.hud.repair.dry",
                health, most, Mathf.FloorToInt(100f * TorchFuel / TorchFuelMax));
        return Texts.Format(whole ? "class.hud.repair.full" : Mending ? "class.hud.repairing" : "class.hud.repair", health, most);
    }

    // A client's mending so far, told the host.
    private void FlushMend()
    {
        if (mendOwed <= 0f) return;
        if (movement.TryGetComponent(out PlayerNetworkCombat network) && network.IsSpawned) network.SendBarricadeMend(mendId, mendOwed);
        mendOwed = 0f;
        nextMendSend = Time.time + MendEvery;
    }

    private bool HandsFree()
    {
        var execution = movement.Execution;
        if (execution != null && (execution.IsExecuting || execution.Candidate != null)) return false;
        if (FieldPickups.Targeting) return false;
        if (TryGetComponent(out PlayerRevive revive) && revive.Candidate != null) return false;
        if (movement.Abilities != null && movement.Abilities.Fixtures.ClimbDownLadder != null) return false;
        return !CampaignRun.Offering && !TowerRun.Offering && !ShopMenu.IsOpen;
    }

    private static string NameOf(PlayerMovement player)
        => player.TryGetComponent(out PlayerTally tally) && !string.IsNullOrEmpty(tally.DisplayName) ? tally.DisplayName : player.name;

    private static float Number(string name, float fallback) => PlayerClass.Ability(Row, name, fallback);
}
