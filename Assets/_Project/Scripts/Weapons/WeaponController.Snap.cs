// WeaponController: a Ghost's 빠른 조준, the first shot once the sights are all the way up. One part of the class; the rest is in WeaponController.cs and the files beside it.
public sealed partial class WeaponController
{
    // The sights came all the way up and no shot has gone since (GhostCards.QuickAims): the next one flies with no spread.
    private bool snapReady, snapSighted;

    // The spread's last factor (UpdateSpread): none at all for that first shot, else 1.
    private float SnapSpread => snapReady ? 0f : 1f;

    // Each frame before the spread: ready as the sights reach all the way, kept while they stay there, gone as they drop.
    // A shot spends it (ProcessFrame).
    private void WatchSnap()
    {
        bool sighted = AimBlend >= .999f;
        if (!sighted) snapReady = false;
        else if (!snapSighted) snapReady = GhostCards.QuickAims(movement);
        snapSighted = sighted;
    }
}
