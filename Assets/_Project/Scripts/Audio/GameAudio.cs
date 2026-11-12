using System.Collections.Generic;
using UnityEngine;

/// <summary>Music: the campaign's songs (CampaignMusic). Ambience: each map's background sound (MapAmbience).</summary>
public enum AudioChannel { Combat, Movement, Voice, UI, Music, Ambience }

/// <summary>
/// Volume channels for every sound the game plays. Bind a source once when it is created; its volume then follows the
/// Esc menu, and master volume is the AudioListener volume. Play sounds with PlayOneShot volume scales, never by
/// writing source.volume; a loop that fades sets its level with SetGain. This is the one place to switch to an
/// AudioMixer later.
/// </summary>
public static class GameAudio
{
    // ownPitch is the pitch the source's owner gave it; setPitch is what FollowWorldPitch last wrote over it.
    private struct Bound { public AudioSource source; public AudioChannel channel; public float gain, ownPitch, setPitch; }
    private static readonly List<Bound> bound = new List<Bound>();
    private static readonly float[] duck = { 1f, 1f, 1f, 1f, 1f, 1f };
    private static bool listening;

    /// <summary>Share of their own pitch every world sound plays at (1 is normal); see FollowWorldPitch.</summary>
    public static float WorldPitch { get; private set; } = 1f;

    public static float Volume(AudioChannel channel)
    {
        var settings = GameSettings.Current;
        switch (channel)
        {
            case AudioChannel.Movement: return settings.movementVolume;
            case AudioChannel.Voice: return settings.voiceVolume;
            case AudioChannel.UI: return settings.uiVolume;
            case AudioChannel.Music: return settings.musicVolume;
            case AudioChannel.Ambience: return settings.ambienceVolume;
            default: return settings.combatVolume;
        }
    }

    public static void Bind(AudioSource source, AudioChannel channel)
    {
        if (source == null) return;
        Listen();
        bound.RemoveAll(item => item.source == null || item.source == source);
        bound.Add(new Bound { source = source, channel = channel, gain = 1f, ownPitch = source.pitch, setPitch = source.pitch });
        source.volume = Volume(channel) * duck[(int)channel];
        // No Doppler anywhere: Unity takes how fast a source and the listener move from each frame of game time, so
        // whatever keeps moving in real time while the game is slowed (a dodge passing through an enemy during a perfect
        // evade's slow motion) reads as hundreds of m/s and bends the pitch of every 3D sound around it.
        source.dopplerLevel = 0f;
        // Menu sounds keep playing while the pause menu pauses the listener, and stay clear of its filters (a slowdown's
        // muffling, SlowMotionSound) and of the rooms' reverb (ReverbArea).
        source.ignoreListenerPause = source.bypassListenerEffects = channel == AudioChannel.UI;
        // Music isn't in any room: no reverb on it either.
        if (channel == AudioChannel.UI || channel == AudioChannel.Music) source.bypassReverbZones = true;
    }

    /// <summary>The ears of this PC: the player camera's AudioListener, else any; null between scenes.</summary>
    public static AudioListener Listener
    {
        get
        {
            if (listener != null && listener.isActiveAndEnabled) return listener;
            var view = Camera.main;
            listener = view != null && view.TryGetComponent(out AudioListener onView) ? onView : Object.FindAnyObjectByType<AudioListener>();
            return listener;
        }
    }

    private static AudioListener listener;

    /// <summary>
    /// Plays every world sound (all channels but UI) at this share of its own pitch, for a slowed game; 1 hands every
    /// source its own pitch back. Owners keep setting their pitch as they like (a random spread per shot): call this every
    /// frame while it isn't 1, and a pitch changed since the last call counts as the source's own.
    /// </summary>
    public static void FollowWorldPitch(float share)
    {
        share = Mathf.Clamp(share, .1f, 1f);
        if (Mathf.Approximately(share, 1f) && Mathf.Approximately(WorldPitch, 1f)) return;
        WorldPitch = Mathf.Approximately(share, 1f) ? 1f : share;
        for (int i = bound.Count - 1; i >= 0; i--)
        {
            var item = bound[i];
            if (item.source == null) { bound.RemoveAt(i); continue; }
            // A song slowed with the world would drift off its own beat (CampaignMusic times the boss to it).
            if (item.channel == AudioChannel.UI || item.channel == AudioChannel.Music) continue;
            if (!Mathf.Approximately(item.source.pitch, item.setPitch)) item.ownPitch = item.source.pitch;
            item.source.pitch = item.setPitch = item.ownPitch * WorldPitch;
            bound[i] = item;
        }
    }

    /// <summary>A bound source's own level under its channel volume (0–1), for loops that fade in and out.</summary>
    public static void SetGain(AudioSource source, float gain)
    {
        gain = Mathf.Clamp01(gain);
        for (int i = 0; i < bound.Count; i++)
        {
            if (bound[i].source != source) continue;
            var item = bound[i];
            item.gain = gain;
            bound[i] = item;
            source.volume = Volume(item.channel) * gain * duck[(int)item.channel];
            return;
        }
    }

    /// <summary>Holds a whole channel down for a moment, so a big hit stands out (0–1, 1 is no ducking).</summary>
    public static void SetDuck(AudioChannel channel, float level)
    {
        level = Mathf.Clamp01(level);
        if (Mathf.Approximately(duck[(int)channel], level)) return;
        duck[(int)channel] = level;
        Refresh();
    }

    /// <summary>
    /// A one-shot 3D sound at a world point, in place of AudioSource.PlayClipAtPoint. A nearest and farthest distance
    /// (m) over 0 replace Unity's own, fading out in a straight line to the farthest.
    /// </summary>
    public static void PlayAt(AudioClip clip, Vector3 point, AudioChannel channel, float volume = 1f, float near = 0f, float far = 0f)
    {
        if (clip == null) return;
        var source = new GameObject("One Shot (" + clip.name + ")").AddComponent<AudioSource>();
        RuntimeSceneHierarchy.Group(source.gameObject, RuntimeSceneHierarchy.Effects, "Audio");
        source.transform.position = point;
        source.playOnAwake = false;
        source.spatialBlend = 1f;
        if (near > 0f && far > near)
        {
            source.minDistance = near;
            source.maxDistance = far;
            source.rolloffMode = AudioRolloffMode.Linear;
        }
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
        foreach (var item in bound) item.source.volume = Volume(item.channel) * item.gain * duck[(int)item.channel];
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        bound.Clear();
        listening = false;
        listener = null;
        WorldPitch = 1f;
        for (int i = 0; i < duck.Length; i++) duck[i] = 1f;
    }

    // Master volume applies from the first frame, even before any source binds.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Startup() => Listen();
}
