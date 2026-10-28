using System.Collections.Generic;
using UnityEngine;

public enum AudioChannel { Combat, Movement, Voice, UI }

/// <summary>
/// Volume channels for every sound the game plays. Bind a source once when it is created; its volume then follows the
/// Esc menu, and master volume is the AudioListener volume. Play sounds with PlayOneShot volume scales, never by
/// writing source.volume. This is the one place to switch to an AudioMixer later.
/// </summary>
public static class GameAudio
{
    private struct Bound { public AudioSource source; public AudioChannel channel; }
    private static readonly List<Bound> bound = new List<Bound>();
    private static bool listening;

    public static float Volume(AudioChannel channel)
    {
        var settings = GameSettings.Current;
        switch (channel)
        {
            case AudioChannel.Movement: return settings.movementVolume;
            case AudioChannel.Voice: return settings.voiceVolume;
            case AudioChannel.UI: return settings.uiVolume;
            default: return settings.combatVolume;
        }
    }

    public static void Bind(AudioSource source, AudioChannel channel)
    {
        if (source == null) return;
        Listen();
        bound.RemoveAll(item => item.source == null || item.source == source);
        bound.Add(new Bound { source = source, channel = channel });
        source.volume = Volume(channel);
        // Menu sounds keep playing while the pause menu pauses the listener.
        source.ignoreListenerPause = channel == AudioChannel.UI;
    }

    /// <summary>A one-shot 3D sound at a world point, in place of AudioSource.PlayClipAtPoint.</summary>
    public static void PlayAt(AudioClip clip, Vector3 point, AudioChannel channel, float volume = 1f)
    {
        if (clip == null) return;
        var source = new GameObject("One Shot (" + clip.name + ")").AddComponent<AudioSource>();
        source.transform.position = point;
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        Bind(source, channel);
        source.PlayOneShot(clip, Mathf.Clamp01(volume));
        Object.Destroy(source.gameObject, clip.length + .1f);
    }

    private static void Listen()
    {
        if (listening) return;
        listening = true;
        GameSettings.Changed += Refresh;
        Refresh();
    }

    private static void Refresh()
    {
        AudioListener.volume = GameSettings.Current.masterVolume;
        bound.RemoveAll(item => item.source == null);
        foreach (var item in bound) item.source.volume = Volume(item.channel);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        bound.Clear();
        listening = false;
    }

    // Master volume applies from the first frame, even before any source binds.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Startup() => Listen();
}
