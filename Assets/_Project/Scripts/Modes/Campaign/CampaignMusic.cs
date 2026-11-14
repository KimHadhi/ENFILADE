using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The campaign's songs (Tables/Music, one row a song file). A song plays whole, from its first second to its last, and
/// is never cut into or played round (user, 2026-11-14: "노래만 원본 그대로 싹 다 플레이할 수 없음?"). Which song plays and
/// when is CampaignPhases' word, and the song's highlights (Music.assault, seconds into the file) are the fight's
/// assaults; its peak (Music.peak, else its first assault) is when a hold's brutes come (friend feedback C2). A song that
/// gives way to another goes out over its own fade on one source while the next comes in on the other.
/// Every PC plays the song itself: the host picks it, and a guest plays the host's song from where the host is in it and
/// is set right whenever it has drifted off (Follow). The clock is the song as heard: the source's own place while it
/// plays. Without the file the clock still runs, so the phases and the brutes still come, and the song never ends.
/// The files go under Resources/Audio/Music; the song plays on the Music channel (its own volume in the Esc menu).
/// </summary>
internal sealed class CampaignMusic
{
    public const string TableName = "Music";
    // A guest further than this from the host's place in the song is moved there (s); a finale's hold is never shorter
    // than this, whatever its song's length (s).
    private const float MostDrift = 1f, ShortestHold = 20f;

    /// <summary>One row of Tables/Music.</summary>
    public sealed class Song
    {
        public AudioClip Clip;
        /// <summary>Its title and artist as Music.csv's cells hold them (Texts.csv ids, or plain words): Texts.Cell shows them.</summary>
        public string Title = "", Artist = "";
        public float Volume = 1f, FadeIn = .25f, FadeOut = 4f, Peak = float.PositiveInfinity;
        /// <summary>Its assaults as start and end pairs (s into the file), in order.</summary>
        public float[] Assaults = Array.Empty<float>();
        /// <summary>How long it plays (s); without its file, endless.</summary>
        public float Length => Clip != null ? Clip.length : float.PositiveInfinity;
        /// <summary>Where its first assault starts (s); endless without one.</summary>
        public float FirstAssault => Assaults.Length >= 2 ? Assaults[0] : float.PositiveInfinity;
    }

    // One of the two sources: a song coming in while the one before goes out.
    private sealed class Voice
    {
        public AudioSource Source;
        public float Level, Target, Fade = 1f, Volume = 1f;
    }

    private static readonly Dictionary<string, Song> songs = new Dictionary<string, Song>(StringComparer.OrdinalIgnoreCase);
    private static readonly List<string> order = new List<string>();
    private readonly Voice[] voices = new Voice[2];
    private Voice voice;
    private Song song;

    public CampaignMusic(GameObject owner)
    {
        for (int i = 0; i < voices.Length; i++)
        {
            var source = owner.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.priority = 0;
            GameAudio.Bind(source, AudioChannel.Music);
            GameAudio.SetGain(source, 0f);
            voices[i] = new Voice { Source = source };
        }
        voice = voices[0];
    }

    /// <summary>The song playing (a Music id), or empty.</summary>
    public string Playing { get; private set; } = "";
    /// <summary>The song playing, or null.</summary>
    public Song Current => song;
    /// <summary>Seconds into the song: where it is heard while its file plays, counted otherwise; paused with the game.</summary>
    public float Clock { get; private set; }
    /// <summary>The song has played to its last second (never without its file).</summary>
    public bool Ended => song != null && Clock >= song.Length;
    /// <summary>The song has reached its peak (Music.peak).</summary>
    public bool PastPeak => song != null && Clock >= song.Peak;

    /// <summary>
    /// The song wanted now (empty for none), from <paramref name="from"/> s into it. True when a song starts (not when the
    /// same one plays on, and not for none).
    /// </summary>
    public bool Want(string id, float from)
    {
        id ??= "";
        if (string.Equals(id, Playing, StringComparison.OrdinalIgnoreCase)) return false;
        Playing = id;
        // The song before goes out over its own fade, on its own source.
        voice.Target = 0f;
        if (song != null) voice.Fade = song.FadeOut;
        song = id.Length > 0 ? Find(id) : null;
        if (song == null) return false;
        voice = voice == voices[0] ? voices[1] : voices[0];
        Clock = Mathf.Max(0f, from);
        voice.Volume = song.Volume;
        voice.Level = 0f;
        voice.Target = 1f;
        voice.Fade = song.FadeIn;
        Seek(Clock);
        return true;
    }

    /// <summary>A guest: the host's song from where the host is in it (s). Starts it, or moves there when this PC is off by more than a second.</summary>
    public void Follow(string id, float at)
    {
        if (Want(id, at) || song == null || Mathf.Abs(Clock - at) <= MostDrift) return;
        Clock = Mathf.Max(0f, at);
        Seek(Clock);
    }

    /// <summary>Stops at once (a wipe starting the run over), so the same song starts again from its beginning.</summary>
    public void Stop()
    {
        Playing = "";
        song = null;
        Clock = 0f;
        foreach (var each in voices)
        {
            each.Level = each.Target = 0f;
            each.Source.Stop();
            GameAudio.SetGain(each.Source, 0f);
        }
    }

    public void Tick()
    {
        // Real time (the song plays in it), but not while the game is paused.
        float dt = Time.timeScale > 0f ? Time.unscaledDeltaTime : 0f;
        if (song != null)
        {
            // While the file plays, the clock is where it is heard (never back: a stream just started may still read 0).
            var source = voice.Source;
            Clock = song.Clip != null && source.clip == song.Clip && source.isPlaying ? Mathf.Max(Clock, source.time) : Clock + dt;
        }
        foreach (var each in voices)
        {
            each.Level = Mathf.MoveTowards(each.Level, each.Target, dt / Mathf.Max(.01f, each.Fade));
            GameAudio.SetGain(each.Source, each.Level * each.Volume);
            if (each.Level <= 0f && each.Target <= 0f && each.Source.isPlaying) each.Source.Stop();
        }
    }

    // The song's source plays from there, or stays still past its end and without its file.
    private void Seek(float at)
    {
        var source = voice.Source;
        if (song.Clip == null || at >= song.Length) { source.Stop(); return; }
        source.clip = song.Clip;
        source.time = Mathf.Min(at, song.Length - .01f);
        if (!source.isPlaying) source.Play();
    }

    /// <summary>
    /// A finale's song (CampaignRun): how long the hold under it lasts, the song's length less <paramref name="board"/>,
    /// the time the ride waits after it. False without the song's file, since its length is not known, and for a song
    /// too short for that wait: the step's own seconds stand then.
    /// </summary>
    public static bool TryFinale(string id, float board, out float hold)
    {
        hold = 0f;
        if (string.IsNullOrEmpty(id) || board <= 0f) return false;
        var finale = Find(id);
        if (finale.Clip == null || finale.Length < board + ShortestHold) return false;
        hold = finale.Length - board;
        return true;
    }

    /// <summary>Every song of Tables/Music in the table's order: the songs a chapter plays when it names none (CampaignChapters.songs).</summary>
    public static IReadOnlyList<string> All
    {
        get
        {
            if (order.Count == 0) order.AddRange(GameTables.RowIds(TableName));
            return order;
        }
    }

    /// <summary>A song's place in Tables/Music from 1, 0 for none: how the host names it to the others (CampaignRun.Status).</summary>
    public static byte IndexOf(string id)
    {
        if (string.IsNullOrEmpty(id)) return 0;
        var all = All;
        for (int i = 0; i < all.Count && i < byte.MaxValue; i++)
            if (string.Equals(all[i], id, StringComparison.OrdinalIgnoreCase)) return (byte)(i + 1);
        return 0;
    }

    /// <summary>The song at a place IndexOf gave, or empty.</summary>
    public static string IdAt(byte index)
    {
        var all = All;
        return index > 0 && index <= all.Count ? all[index - 1] : "";
    }

    /// <summary>The song of that id (one without a row or a file still keeps time).</summary>
    public static Song Find(string id)
    {
        if (songs.TryGetValue(id, out var known)) return known;
        var song = new Song();
        if (GameTables.TryGetRow(TableName, id, out var row))
        {
            string path = row.Text("clip");
            if (path.Length > 0) song.Clip = Resources.Load<AudioClip>(path);
            if (song.Clip == null) Debug.LogWarning($"[Campaign] song {id}: no clip at Resources/{path} yet; its time still runs.");
            song.Title = row.Text("title");
            song.Artist = row.Text("artist");
            song.Volume = Mathf.Clamp01(row.Number("volume", 1f));
            song.FadeIn = Mathf.Max(.01f, row.Number("fadeIn", .25f));
            song.FadeOut = Mathf.Max(.01f, row.Number("fadeOut", 4f));
            song.Assaults = Assaults(id, row.Text("assault"));
            song.Peak = row.Number("peak", song.FirstAssault);
        }
        else Debug.LogWarning($"[Campaign] Tables/Music has no song '{id}'.");
        songs[id] = song;
        return song;
    }

    // "16.1~62.5|93.7~155.5": each highlight from its start to its end (s into the file), put in order.
    private static float[] Assaults(string id, string cell)
    {
        var spans = new List<Vector2>();
        foreach (string part in TableCells.Parts(cell))
        {
            int mark = part.IndexOf('~');
            if (mark > 0 && TableCells.TryNumber(part.Substring(0, mark), out float from)
                && TableCells.TryNumber(part.Substring(mark + 1), out float to) && to > from)
                spans.Add(new Vector2(Mathf.Max(0f, from), to));
            else Debug.LogWarning($"[Campaign] song {id}: assault '{part}' should read start~end in seconds, like 16.1~62.5.");
        }
        spans.Sort((a, b) => a.x.CompareTo(b.x));
        var pairs = new float[spans.Count * 2];
        for (int i = 0; i < spans.Count; i++) { pairs[i * 2] = spans[i].x; pairs[i * 2 + 1] = spans[i].y; }
        return pairs;
    }

    // Live table edits and a new Play session read the songs again.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        Forget();
        GameTables.Reloaded -= Forget;
        GameTables.Reloaded += Forget;
    }

    private static void Forget()
    {
        songs.Clear();
        order.Clear();
    }
}
