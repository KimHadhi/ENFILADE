using UnityEngine;

// ClassKit: The ultimate (every card taken, a full gauge: ClassCards.Ultimate). One part of the class; the rest is in ClassKit.cs and the files beside it.
public sealed partial class ClassKit
{
    /// <summary>An Assault's ultimate: adrenaline full and the CHARGE sounded at once (its riders with it, the card held).</summary>
    internal void UltimateCharge()
    {
        if (!PlayerClass.Is(PlayerClass.Assault)) return;
        stacks = StackMax;
        Use();
    }

    /// <summary>A Ghost's ultimate: the Head Hunter full.</summary>
    internal void FillHunter()
    {
        if (!PlayerClass.Is(PlayerClass.Ghost)) return;
        hunter = HunterMax;
        hunterChange = 1;
        hunterChangedAt = Time.time;
        KitSound.Play(KitSound.Cue.Hunter, .9f);
    }

    /// <summary>A Point's ultimate: both guns loaded at once, and the Gunslinger's moment from now.</summary>
    internal void ReloadAll()
    {
        if (slots == null) return;
        for (int slot = 0; slot < 2; slot++)
        {
            var gun = slots.GetWeapon(slot) != null ? slots.GetRuntime(slot) : null;
            if (gun != null && gun.ReloadAside()) refilledAt[slot] = Time.time;
        }
        slots.NotifyAmmo();
        drawnAt = Time.time;
    }

    /// <summary>A Schießhund's ultimate: the Desperado's cylinder full.</summary>
    internal void LoadDesperado()
    {
        var desperado = Desperado;
        if (desperado == null) return;
        for (int i = 0; i < 16 && desperado.Ammo < RoundMax; i++)
        {
            int before = desperado.Ammo;
            desperado.LoadRound();
            if (desperado.Ammo == before) break;
        }
        slots.NotifyAmmo();
        bountyAt = Time.time;
    }
}
