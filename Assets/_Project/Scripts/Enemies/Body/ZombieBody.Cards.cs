using UnityEngine;

// ZombieBody: what class cards and ultimates show on the body. One part of the class; the rest is in ZombieBody.cs and
// the files beside it. The cards never call in here: ZombieEffects is their door.
public sealed partial class ZombieBody
{
    // A cower is shown no oftener than this (s): rounds keep a suppression alive several times a second. How often a
    // zombie out of the gas looks whether it is in it now (s).
    private const float CowerEvery = .8f, GasLook = .25f;

    private float coweredAt = float.NegativeInfinity, gasLookAt;

    /// <summary>
    /// Every PC: it shrinks a moment (pinned down by fire, coughing in gas): its short flinch on top of whatever it is
    /// doing, which goes on. Nothing is stopped here; what stops it is said apart, where it is judged.
    /// </summary>
    internal void Cower()
    {
        if (!graph.IsValid() || down || IsExecuted || Time.time - coweredAt < CowerEvery) return;
        coweredAt = Time.time;
        flinching = true;
        flinchTime = 0f;
    }

    // Every PC, each frame: in blinding gas (ZombieEffects.Blind) it coughs now and then. Where it thinks, the cough
    // comes with the moment it stops for it (ZombieMind.GasThink, which flinches it as any interrupt does); a copy shows
    // its own cough here, on its own clock. And one holding this PC's own player in the gas lets go: this PC stops its own
    // copy, which tells the host, as the antiseptic mist frees whoever is held in it (each PC frees its own player).
    private void CoughTick()
    {
        if (!ZombieMind.AnyGas || Time.time < gasLookAt) return;
        gasLookAt = Time.time + GasLook;
        if (network == null || !network.IsCopy || !ZombieMind.InGas(transform.position, out _)) return;
        gasLookAt = Time.time + (mind != null ? mind.GasCough : 1.2f) * Random.Range(.8f, 1.2f);
        Cower();
        var mine = PlayerRoster.Local;
        if (mine != null && target != null && TryGetComponent(out ZombieGrip holding) && holding.IsHolding && holding.Held == mine)
            target.Interrupt(mind != null ? mind.GasStun : .7f);
    }
}
