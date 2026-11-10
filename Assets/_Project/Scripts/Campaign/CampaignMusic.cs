using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// Friend feedback C1: a song plays through a chapter's highlights, the hold-outs and the escape after them. A step
/// names its song (CampaignSteps.music, an id in Tables/Music); a step naming the same song as the one before lets it
/// play on, so a finale's hold carries into its escape. Every PC plays it itself as the run reaches the step, a hold
/// joined late from as far in as the hold already is. The song's highlight (Music.peak, seconds into the file) is when the
/// host sends that step's brutes (C2, CampaignRun). Without the file the clock still runs, so the brutes still come.
/// A song may start partway into its file (Music.start), so one song can serve holds of different lengths with its drop
/// landing inside each (the user gave two songs for four holds, 2026-11-10).
/// The files go under Resources/Audio/Music; the song plays on the Music channel (its own volume in the Esc menu).
/// </summary>
internal sealed class CampaignMusic
{
    public const string TableName = "Music";

    private sealed class Song
    {
        public AudioClip Clip;
        public float Start, Peak, Volume, FadeIn, FadeOut;
    }

    private static readonly Dictionary<string, Song> songs = new Dictionary<string, Song>(StringComparer.OrdinalIgnoreCase);
    private readonly AudioSource source;
    private Song song;
    private float level, target, fade = 1f;

    public CampaignMusic(GameObject owner)
    {
        source = owner.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.loop = true;
        source.spatialBlend = 0f;
        source.priority = 0;
        GameAudio.Bind(source, AudioChannel.Music);
        GameAudio.SetGain(source, 0f);
    }

    /// <summary>The song playing (a Music id), or empty.</summary>
    public string Playing { get; private set; } = "";
    /// <summary>Seconds since the song began, file or not; paused with the game.</summary>
    public float Clock { get; private set; }
    /// <summary>The song has reached its highlight (its start in the file plus the time it has played).</summary>
    public bool PastPeak => song != null && song.Start + Clock >= song.Peak;

    /// <summary>
    /// The song the run wants now (empty for none), and how far into it the run already is. True when a song starts
    /// (not when the same one plays on).
    /// </summary>
    public bool Want(string id, float from)
    {
        id ??= "";
        if (string.Equals(id, Playing, StringComparison.OrdinalIgnoreCase)) return false;
        Playing = id;
        if (id.Length == 0)
        {
            target = 0f;
            if (song != null) fade = song.FadeOut;
            song = null;
            return false;
        }
        song = Find(id);
        Clock = Mathf.Max(0f, from);
        if (song.Clip != null)
        {
            source.clip = song.Clip;
            source.time = Mathf.Repeat(song.Start + Clock, Mathf.Max(.01f, song.Clip.length));
            source.Play();
        }
        else source.Stop();
        level = 0f;
        target = 1f;
        fade = song.FadeIn;
        return true;
    }

    /// <summary>Stops at once (a wipe starting the run over), so the same song starts again from its beginning.</summary>
    public void Stop()
    {
        Playing = "";
        song = null;
        level = target = 0f;
        source.Stop();
        GameAudio.SetGain(source, 0f);
    }

    public void Tick()
    {
        // Real time (the song plays in it), but not while the game is paused.
        float dt = Time.timeScale > 0f ? Time.unscaledDeltaTime : 0f;
        if (song != null) Clock += dt;
        level = Mathf.MoveTowards(level, target, dt / Mathf.Max(.01f, fade));
        GameAudio.SetGain(source, level * (song != null ? song.Volume : 1f));
        if (level <= 0f && target <= 0f && source.isPlaying) source.Stop();
    }

    private static Song Find(string id)
    {
        if (songs.TryGetValue(id, out var known)) return known;
        var song = new Song { Peak = float.PositiveInfinity, Volume = 1f, FadeIn = 1f, FadeOut = 3f };
        if (GameTables.TryGetRow(TableName, id, out var row))
        {
            string path = CampaignStep.Text(row, "clip");
            if (path.Length > 0) song.Clip = Resources.Load<AudioClip>(path);
            if (song.Clip == null) Debug.LogWarning($"[Campaign] song {id}: no clip at Resources/{path} yet; its time still runs.");
            song.Start = Mathf.Max(0f, Number(row, "start", 0f));
            song.Peak = Number(row, "peak", float.PositiveInfinity);
            song.Volume = Mathf.Clamp01(Number(row, "volume", 1f));
            song.FadeIn = Mathf.Max(.01f, Number(row, "fadeIn", 1f));
            song.FadeOut = Mathf.Max(.01f, Number(row, "fadeOut", 3f));
        }
        else Debug.LogWarning($"[Campaign] Tables/Music has no song '{id}'.");
        songs[id] = song;
        return song;
    }

    private static float Number(IReadOnlyDictionary<string, string> row, string key, float fallback)
        => row.TryGetValue(key, out string text) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? value : fallback;

    // Live table edits and a new Play session read the songs again.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        songs.Clear();
        GameTables.Reloaded -= songs.Clear;
        GameTables.Reloaded += songs.Clear;
    }
}
