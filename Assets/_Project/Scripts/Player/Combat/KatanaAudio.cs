using UnityEngine;

/// <summary>What a katana did, for its sounds here and on the other PCs (PlayerNetworkCombat sends the byte).</summary>
public enum KatanaSound : byte { None, Swing, HeavySwing, HitFlesh, HitWorld, Draw, Parry, ParryBreak, Block }

/// <summary>
/// The katana's sounds. Each comes from its folder once there are files in it (sound banks, SoundBankBuilder):
/// Art/Audio/Melee/Katana/Swing, HitFlesh, HitWorld and Draw (bank Melee), and its parries and blocks from the metal
/// clips kept for it in Art/Audio/Combat/Defense (bank Defense: Parry, ReservedStrongParries, Block), layered over the
/// fists' parry cue. Until the katana's own files arrive it borrows the combat sounds already in Feedback: the fists'
/// swing pitched up for the whoosh, Hitfullness' flesh break for the cut, the shove's world thud for steel on a wall,
/// and no sound for the draw. Volume and pitch per folder: Sounds.csv (Melee/Katana/…, Defense/…). One's own katana
/// sounds flat, someone else's from where they stand.
/// </summary>
[DisallowMultipleComponent]
public sealed class KatanaAudio : MonoBehaviour
{
    public const string MeleeBank = "Melee", DefenseBank = "Defense";
    private const string SwingKey = "Katana/Swing", FleshKey = "Katana/HitFlesh", WorldKey = "Katana/HitWorld", DrawKey = "Katana/Draw";
    private const float OthersReach = 25f;
    // How much higher the borrowed fist whoosh plays, so it cuts rather than thumps (a heavy one less so).
    private const float BorrowedSwingPitch = 1.3f, BorrowedHeavyPitch = 1.08f, BorrowedWorldPitch = 1.15f;
    // Two swings and four hits at most (VoiceRing), as the fists have.
    private const int SwingVoices = 2, HitVoices = 4;
    // What is left of a whoosh once the blade has met something, so the hit lands clean.
    private const float SwingUnderHit = .35f;

    private VoiceRing swings, hits;
    private SoundBank melee, defense;
    private bool banksLooked;
    private AudioClip lastSwing, lastFlesh, lastWorld;

    /// <summary>The whoosh as the blade sets off on its cut; pitch is the move's (Tables/Katana swingPitch) for a borrowed whoosh.</summary>
    public void Swing(bool heavy, float pitch)
    {
        Banks();
        var settings = CombatFeedbackSettings.Instance;
        if (melee != null && melee.TryGet(SwingKey, out _))
        {
            var own = melee.SettingsOf(SwingKey);
            Emit(true, melee.Pick(SwingKey), own.Volume * (heavy ? 1.15f : 1f), own.RandomPitch() * (heavy ? .92f : 1f));
            return;
        }
        if (settings == null) return;
        Emit(true, Pick(settings.fistSwingSounds, ref lastSwing), settings.fistSwingVolume * (heavy ? 1.4f : 1.2f), Mathf.Max(.5f, pitch));
    }

    public void Play(KatanaSound sound)
    {
        if (!Application.isPlaying || !isActiveAndEnabled) return;
        Banks();
        var settings = CombatFeedbackSettings.Instance;
        switch (sound)
        {
            case KatanaSound.Swing: Swing(false, BorrowedSwingPitch); return;
            case KatanaSound.HeavySwing: Swing(true, BorrowedHeavyPitch); return;
            case KatanaSound.HitFlesh:
                if (FromBank(melee, FleshKey)) return;
                if (settings != null) Emit(false, Pick(settings.fleshBreakSounds, ref lastFlesh), Mathf.Clamp01(settings.hitSoundVolume * 1.5f), Varied());
                return;
            case KatanaSound.HitWorld:
                if (FromBank(melee, WorldKey)) return;
                if (settings == null) return;
                var world = settings.shoveWorldSound != null ? settings.shoveWorldSound : Pick(settings.fistWallSounds, ref lastWorld);
                Emit(false, world, settings.shoveWorldVolume, BorrowedWorldPitch * Varied());
                return;
            case KatanaSound.Draw: FromBank(melee, DrawKey); return;
            case KatanaSound.Parry: FromBank(defense, "Parry"); return;
            case KatanaSound.ParryBreak: if (!FromBank(defense, "ReservedStrongParries")) FromBank(defense, "Parry"); return;
            case KatanaSound.Block: FromBank(defense, "Block"); return;
        }
    }

    /// <summary>Makes the voices now (at spawn), so the first swing adds no AudioSources mid-fight.</summary>
    public void Prepare()
    {
        if (!Application.isPlaying) return;
        if (swings == null) swings = NewRing(SwingVoices);
        if (hits == null) hits = NewRing(HitVoices);
    }

    private void Banks()
    {
        if (banksLooked) return;
        banksLooked = true;
        melee = SoundBank.Load(MeleeBank);
        defense = SoundBank.Load(DefenseBank);
    }

    // A hit sound from its folder, with the folder's volume and pitch; false when the folder has nothing yet.
    private bool FromBank(SoundBank bank, string key)
    {
        if (bank == null || !bank.TryGet(key, out _)) return false;
        var own = bank.SettingsOf(key);
        Emit(false, bank.Pick(key), own.Volume, own.RandomPitch());
        return true;
    }

    private static float Varied()
    {
        float variance = CombatFeedbackSettings.InstanceOrDefaults.hitPitchVariance;
        return 1f + Random.Range(-variance, variance);
    }

    private void Emit(bool swing, AudioClip clip, float volume, float pitch)
    {
        if (clip == null || !isActiveAndEnabled || !Application.isPlaying) return;
        Prepare();
        if (!swing) swings.ForEach(source => GameAudio.SetGain(source, SwingUnderHit));
        var source = Place((swing ? swings : hits).Take());
        GameAudio.SetGain(source, 1f);
        source.pitch = Mathf.Clamp(pitch, .1f, 3f);
        source.PlayOneShot(clip, Mathf.Clamp(volume, 0f, 1f));
    }

    private VoiceRing NewRing(int count)
    {
        var sources = new AudioSource[count];
        for (int i = 0; i < count; i++)
        {
            var source = gameObject.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = 2f;
            source.maxDistance = OthersReach;
            GameAudio.Bind(source, AudioChannel.Combat);
            sources[i] = source;
        }
        return new VoiceRing(sources);
    }

    // One's own katana sounds flat; someone else's comes from where they stand. Asked at every sound, since the voices
    // are made at spawn, before the network says whose copy this is.
    private AudioSource Place(AudioSource source)
    {
        bool someoneElse = TryGetComponent(out PlayerMovement player) && !player.IsLocal;
        source.spatialBlend = someoneElse ? 1f : 0f;
        return source;
    }

    // A random clip, never the one just played when there is a choice.
    private static AudioClip Pick(AudioClip[] clips, ref AudioClip previous)
    {
        if (clips == null || clips.Length == 0) return null;
        int alternatives = 0;
        AudioClip fallback = null;
        foreach (var clip in clips)
        {
            if (clip == null) continue;
            fallback = clip;
            if (clip != previous) alternatives++;
        }
        if (alternatives == 0) return previous = fallback;
        int choice = Random.Range(0, alternatives);
        foreach (var clip in clips)
            if (clip != null && clip != previous && choice-- == 0) return previous = clip;
        return previous = fallback;
    }

    private void OnDisable()
    {
        swings?.Stop();
        hits?.Stop();
    }
}
