/// <summary>Builds what the first shot or hit would otherwise build in the middle of a fight. Runs while a map loads.</summary>
public static class CombatWarmup
{
    public static void Prepare()
    {
        ImpactPool.Prepare();
        BulletHoles.Prepare();
        ImpactVfx.Prepare();
        CombatHitAudio.Prepare();
        FeelAudio.Prepare();
        HitMarkerHUD.Prepare();
        DamagePopupManager.Prepare();
    }
}
