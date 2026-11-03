using UnityEngine;

/// <summary>
/// A few sources taken in turn, each stopped before it plays again. PlayOneShot alone never ends the shots before it,
/// so a fast enough string of hits piles past Unity's real-voice limit (32), and from there new sounds start virtual:
/// the hits suddenly go quiet. Here the oldest shot gives way instead.
/// </summary>
public sealed class VoiceRing
{
    private readonly AudioSource[] sources;
    private int next;

    public VoiceRing(AudioSource[] sources) { this.sources = sources; }

    /// <summary>The next source, stopped, for the caller to place and play.</summary>
    public AudioSource Take()
    {
        var source = sources[next];
        next = (next + 1) % sources.Length;
        source.Stop();
        return source;
    }

    public void Stop() { foreach (var source in sources) if (source != null) source.Stop(); }

    public void ForEach(System.Action<AudioSource> action) { foreach (var source in sources) if (source != null) action(source); }
}
