using UnityEngine;

// ClassKit: The ultimate (every card taken, a full gauge: ClassCards.Ultimate). One part of the class; the rest is in ClassKit.cs and the files beside it.
public sealed partial class ClassKit
{
    // An Assault's ultimate is ClassKit.Assault's (UltimateAdrenaline) beside its A-10 run (ClassCards.GunRun). A Point's
    // is its nine-bang (PointBreach), with nothing of the kit's.

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
