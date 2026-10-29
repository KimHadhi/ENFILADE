using UnityEngine;

/// <summary>
/// One looping sound that fades in and out, such as a slide's scrape. Its level is a gain under the channel volume
/// (GameAudio.SetGain); call Tick every frame.
/// </summary>
public sealed class SoundLoop
{
    private readonly AudioSource source;
    private float level, target, fadeTime = .15f;

    public SoundLoop(AudioSource source)
    {
        this.source = source;
        source.loop = true;
        GameAudio.SetGain(source, 0f);
    }

    public AudioClip Clip => source != null ? source.clip : null;

    /// <summary>Starts the clip, or switches to it; the level carries on fading from where it is.</summary>
    public void Play(AudioClip clip, float pitch)
    {
        if (source == null || clip == null) return;
        if (source.clip != clip || !source.isPlaying)
        {
            source.clip = clip;
            source.Play();
        }
        source.pitch = pitch;
    }

    /// <summary>The level to fade toward (0–1), and how long a fade across the whole range takes (s).</summary>
    public void FadeTo(float wanted, float seconds)
    {
        target = Mathf.Clamp01(wanted);
        fadeTime = Mathf.Max(.01f, seconds);
    }

    /// <summary>Advances the fade and stops the clip once it has faded out.</summary>
    public void Tick(float dt)
    {
        if (source == null) return;
        level = Mathf.MoveTowards(level, target, dt / fadeTime);
        GameAudio.SetGain(source, level);
        if (level <= 0f && source.isPlaying) source.Stop();
    }

    public void Stop()
    {
        level = target = 0f;
        if (source == null) return;
        GameAudio.SetGain(source, 0f);
        source.Stop();
    }
}
