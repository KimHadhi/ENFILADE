using ImpactCFX;
using ImpactCFX.Audio;
using UnityEngine;

/// <summary>
/// Impact CFX's pooled collision voice (a prop's clatter, scrape and roll), played on the game's Combat channel
/// (GameAudio) so the settings' volume, the ducking and the pause hold it as they hold every other sound. The pack plays
/// its own AudioSource at its own level; this is the pack's extension point for that (ImpactAudioSourceBase), set on its
/// audio source templates (Impact CFX Demo (URP)/Prefabs/Audio) in place of the pack's ImpactAudioSource. The pack's level
/// for each collision becomes the source's gain under the channel.
/// </summary>
public sealed class GameImpactAudioSource : ImpactAudioSource
{
    protected override void Awake()
    {
        base.Awake();
        GameAudio.Bind(audioSource, AudioChannel.Combat);
    }

    protected override void playAudio(AudioClip audioClip, float volume, float pitch, bool loop, CollisionResultData collisionResultData)
    {
        base.playAudio(audioClip, volume, pitch, loop, collisionResultData);
        GameAudio.SetGain(audioSource, baseVolume * volume);
    }

    protected override void updateAudio(float volume, float pitch, CollisionResultData collisionResultData)
    {
        base.updateAudio(volume, pitch, collisionResultData);
        GameAudio.SetGain(audioSource, baseVolume * volume);
    }
}
