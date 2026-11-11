using System.Collections.Generic;
using UnityEngine;

/// <summary>Builds what the first shot, hit or kill screen would otherwise build in the middle of a fight. Runs while a map loads.</summary>
public static class CombatWarmup
{
    // A sound bank loads every clip it holds the first time anything asks for it, so the first footstep, breath or grunt
    // (Voice: over a hundred clips, now first asked for by the first dodge or counter, since breathing waits for effort),
    // hit confirm or bullet impact would stall the frame it plays in. They load here and stay loaded.
    // Zombie: their voices (ZombieVoice), first asked for by the first attack of a fight. Melee and Defense: the katana's
    // own sounds and its metal parries (KatanaAudio), once their folders have files.
    private static readonly string[] SoundBanks = { "Movement", "Voice", "HitConfirm", "Bullets", ZombieVoice.BankId,
        KatanaAudio.MeleeBank, KatanaAudio.DefenseBank };
    private static readonly List<SoundBank> keptBanks = new List<SoundBank>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => keptBanks.Clear();

    public static void Prepare()
    {
        ImpactPool.Prepare();
        BulletHoles.Prepare();
        ImpactVfx.Prepare();
        CombatHitAudio.Prepare();
        ZombieVoice.Prepare();
        FeelAudio.Prepare();
        HitMarkerHUD.Prepare();
        DamagePopupManager.Prepare();
        PostProcessWarmup.Run();
        SkullSlowMotion.Prepare();
        LoadSoundBanks();
    }

    private static void LoadSoundBanks()
    {
        if (keptBanks.Count > 0 || !Application.isPlaying) return;
        foreach (string id in SoundBanks)
        {
            var bank = SoundBank.Load(id);
            if (bank != null) keptBanks.Add(bank);
        }
    }
}
