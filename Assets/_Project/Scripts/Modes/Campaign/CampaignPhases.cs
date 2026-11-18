using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>Where a chapter's fight stands, after PAYDAY 2's cycle (CampaignPhases); every PC holds the host's word.</summary>
public enum CombatPhase : byte
{
    /// <summary>No fight: a quiet stretch, a rest stop, the chapter over, or no chapter at all.</summary>
    Quiet,
    /// <summary>Between assaults: few come and slowly, time to take up what the dead dropped.</summary>
    Control,
    /// <summary>The song builds toward its highlight: the assault is on its way.</summary>
    Anticipation,
    /// <summary>The song's highlight: more come, faster and harder.</summary>
    Assault
}

/// <summary>What the objective bar shows of the fight's phase (CampaignRun.TryPhase, AssaultBar).</summary>
public struct PhaseTrack
{
    public CombatPhase Phase;
    /// <summary>The track under the bar: how far the song has played and its length (s), or a hold's without a song. Length 0: no track.</summary>
    public float Elapsed, Length;
    /// <summary>Until the phase turns (s): an assault's end, or the next assault's start. Infinite when nothing is due.</summary>
    public float Until;
    /// <summary>The assaults along the track, start and end pairs (s) in order.</summary>
    public float[] Assaults;
    /// <summary>The song playing, as the screen says it (Music.csv title and artist through Texts.csv); empty without one.</summary>
    public string Title, Artist;
    /// <summary>A hold without a song (the shutter's siren): one assault, no music.</summary>
    public bool Siren;
}

/// <summary>
/// PAYDAY 2's assault cycle for a chapter's loud stretches (user, 2026-11-14: "페이데이2처럼 control, anticipation, assault
/// 단계 나눠서 노래가 하이라이트 부분으로 됐을 때 딱 UI도 더 간지나게 바뀌면서 적이 더 강하게 들어오고, control 단계일 땐 적 잡고
/// 나온 체력, 탄약, 쉴드 이런 거 챙길 수 있게 좀 여유 시간 주고"). The one place that says which phase the run is in, which
/// song plays and what each phase sends. The host runs it (CampaignRun) and tells every PC (CampaignRun.Status).
/// <para>Loud, a song plays whole (CampaignMusic): the step's own song (a finale's hold, and the escape after it), else
/// the chapter's songs (CampaignChapters.songs, every song of Music.csv when it names none) one after another, from the top
/// of the list each time the run goes loud. A song's highlights (Music.assault) are the assaults, the anticipation begins
/// Phases.csv Anticipation.lead seconds before each, and the rest is control. A hold or escape without a song (the
/// shutter's siren, the tug) is one assault from end to end with the music stopped. Between two songs the run breathes in
/// control: Control.gap seconds of quiet at least, more when the next song's first assault would come sooner than
/// Control.least after the last one ended. Quiet (unseen, a lull, a rest stop, a wipe) has no phase and no song.</para>
/// <para>A rest has a longest length too (Control.longest; user, 2026-11-18: "1분 30초 동안 음악 없이 비어 있는 휴식 시간을
/// 줄여 게임 템포 유지"). A song whose first highlight is far in (song 2's comes at 99 s, after 35 s of near silence) made
/// every loud stretch begin with a minute and a half of rest, so such a song comes in further along (EntryOf): late
/// enough that the rest before its first assault is no longer than that, the rest already had since the last assault
/// counted in. It still plays on to its end from there; a rest between two highlights inside a song is the song's own.</para>
/// <para>What each phase sends, against the step's own numbers (CampaignSteps horde, rate): Phases.csv most and rate; an
/// assault breaks in with mob more at mobRate; special of the shamblers made in a phase come as other kinds; the dead drop
/// things drops times as often (FieldPickups), and what drops in an assault starts running out only once it is over. It
/// took over from stage 5D's tension director, whose build-up, peak and relax this cycle now is.</para>
/// </summary>
internal sealed class CampaignPhases
{
    public const string TableName = "Phases";

    // One row of Tables/Phases.
    private sealed class Numbers
    {
        public float Most, Rate, MobRate, Special, Drops = 1f;
        public int Mob;
    }

    private static readonly Numbers[] paces = new Numbers[4];
    private static float lead = 12f, least = 25f, gap = 3f, longest, enterFade = 2f;
    private static bool loaded;

    // The next of the chapter's songs; the step whose own song has run out; the song a step started as its own ("" when
    // the one playing came from the chapter's list); the quiet left before the next song (s, negative none) and where in
    // itself that song will come in (s); when the last assault ended (real s).
    private int nextSong, mobLeft, ownDone = -1;
    private string ownSong = "";
    private float breath = -1f, enterAt, assaultEnded = float.NegativeInfinity;

    /// <summary>The phase now (the host's).</summary>
    public CombatPhase Now { get; private set; }

    /// <summary>
    /// The host, each frame while the chapter runs: the song and the phase. <paramref name="loud"/>: the horde is on (no
    /// quiet stretch); <paramref name="step"/> and <paramref name="stepSong"/>: the step under way and its own song (""
    /// for none); <paramref name="songless"/>: it is a hold or escape without one, an assault throughout;
    /// <paramref name="playlist"/>: the chapter's songs.
    /// </summary>
    public void Run(bool loud, int step, string stepSong, bool songless, IReadOnlyList<string> playlist, CampaignMusic music)
    {
        float dt = Time.timeScale > 0f ? Time.unscaledDeltaTime : 0f;
        var was = Now;
        if (!loud)
        {
            // The song goes out, and the next loud stretch starts at the top of the list.
            music.Want("", 0f);
            nextSong = 0;
            ownDone = -1;
            ownSong = "";
            breath = -1f;
            enterAt = 0f;
            Now = CombatPhase.Quiet;
        }
        else
        {
            // A step's own song plays once from its start (the escape after a hold naming it plays on); should it run out
            // before the step does, the chapter's songs go on.
            bool own = stepSong.Length > 0 && ownDone != step;
            if (own && string.Equals(music.Playing, stepSong, StringComparison.OrdinalIgnoreCase) && music.Ended) { ownDone = step; own = false; }
            if (own) Own(stepSong, music);
            else if (songless) { music.Want("", 0f); ownSong = ""; breath = -1f; Now = CombatPhase.Assault; }
            else Roam(dt, playlist, music);
        }
        if (Now == was) return;
        if (Now == CombatPhase.Assault) mobLeft = Of(CombatPhase.Assault).Mob;
        else if (was == CombatPhase.Assault) { mobLeft = 0; assaultEnded = Time.unscaledTime; }
    }

    /// <summary>The chapter is over: no phase, and the song plays on under the closing card to its end.</summary>
    public void End()
    {
        Now = CombatPhase.Quiet;
        mobLeft = 0;
    }

    /// <summary>
    /// How many may be about and how many come a second this frame, from the step's own numbers (CampaignSteps horde,
    /// rate), and the share of the shamblers made now that come as other kinds. <paramref name="none"/>: nobody comes
    /// (a quiet stretch, everyone resting behind a shut door).
    /// </summary>
    public void Pace(bool none, int stepMost, float stepRate, out int most, out float rate, out float special)
    {
        most = 0;
        rate = special = 0f;
        if (none || Now == CombatPhase.Quiet) return;
        var pace = Of(Now);
        most = Mathf.RoundToInt(stepMost * pace.Most);
        rate = stepRate * pace.Rate;
        special = pace.Special;
        if (mobLeft <= 0) return;
        // An assault breaks in: its mob on top, coming fast.
        most += pace.Mob;
        rate = Mathf.Max(rate, pace.MobRate);
    }

    /// <summary>The horde made this many: an assault's mob counts them off.</summary>
    public void Spawned(int count) => mobLeft = Mathf.Max(0, mobLeft - count);

    /// <summary>How much more often the dead drop something in this phase (Phases.csv drops; 1 when quiet).</summary>
    public static float DropShare(CombatPhase phase) => phase == CombatPhase.Quiet ? 1f : Of(phase).Drops;

    /// <summary>How long before each assault its anticipation begins (Phases.csv Anticipation.lead, s): the HUD's gauge.</summary>
    public static float Lead
    {
        get
        {
            Load();
            return lead;
        }
    }

    /// <summary>
    /// Where in itself a song comes in (s) so that the rest before its first assault is no longer than Phases.csv
    /// Control.longest, <paramref name="rested"/> s of rest being behind the run already (0 going loud, or for a step's
    /// own song). 0, its first second, for a song whose first highlight is near enough, for one without a highlight,
    /// and when the table sets no longest rest. One place says it: the phases start the song there, and a finale's
    /// hold is that much shorter (CampaignMusic.TryFinale).
    /// </summary>
    public static float EntryOf(CampaignMusic.Song song, float rested = 0f)
    {
        Load();
        if (song == null || longest <= 0f || float.IsInfinity(song.FirstAssault)) return 0f;
        // What the song itself brings before its first anticipation, against the rest the run may still have.
        float brings = Mathf.Max(0f, song.FirstAssault - lead), room = Mathf.Max(0f, longest - Mathf.Max(0f, rested));
        return Mathf.Max(0f, brings - room);
    }

    /// <summary>How long a song that comes in past its first second takes to fade in (Phases.csv Control.enterFade, s): its own fadeIn is for its first beat.</summary>
    public static float EnterFade
    {
        get
        {
            Load();
            return enterFade;
        }
    }

    /// <summary>
    /// The phase a song is in at <paramref name="clock"/> s into it, and how long until that turns: the assault's end, or
    /// the next assault's start (infinite after the last one).
    /// </summary>
    public static CombatPhase PhaseAt(CampaignMusic.Song song, float clock, out float until)
    {
        Load();
        var spans = song.Assaults;
        for (int i = 0; i + 1 < spans.Length; i += 2)
        {
            if (clock < spans[i]) { until = spans[i] - clock; return until <= lead ? CombatPhase.Anticipation : CombatPhase.Control; }
            if (clock < spans[i + 1]) { until = spans[i + 1] - clock; return CombatPhase.Assault; }
        }
        until = float.PositiveInfinity;
        return CombatPhase.Control;
    }

    /// <summary>/director: the phase, the song and what is coming (the host's).</summary>
    public string Report(int alive, CampaignMusic music)
    {
        string phase = Now == CombatPhase.Control ? "정비" : Now == CombatPhase.Anticipation ? "습격 임박" : Now == CombatPhase.Assault ? "습격" : "조용함";
        string song = "";
        if (music.Current != null)
        {
            PhaseAt(music.Current, music.Clock, out float until);
            song = $" · 노래 {music.Playing} {music.Clock:0}초" + (float.IsInfinity(until) ? "" : $" · 다음 단계까지 {until:0}초");
        }
        else if (breath > 0f) song = $" · 다음 노래까지 {breath:0}초";
        return $"단계: {phase} · 적 {alive}" + (mobLeft > 0 ? $" · 떼 {mobLeft} 마리 오는 중" : "") + song;
    }

    // A step's own song from where it comes in (EntryOf: its first second, unless its first highlight is far in), even
    // when the chapter's list was playing that same song already: a finale's hold is as long as what is heard of its
    // song (CampaignRun.TryFinale) and must hear all of that. One an earlier step started as its own plays on.
    private void Own(string stepSong, CampaignMusic music)
    {
        if (!string.Equals(ownSong, stepSong, StringComparison.OrdinalIgnoreCase))
        {
            music.Want("", 0f);
            ownSong = stepSong;
        }
        music.Want(stepSong, EntryOf(CampaignMusic.Find(stepSong)));
        breath = -1f;
        Now = SongPhase(music);
    }

    // Loud between a step's own songs: the song playing goes on to its end, whichever started it; then, after a breath,
    // the next of the chapter's songs from where it comes in (Plan). Going loud, the first comes at once; straight out of
    // a hold without a song (an assault) it waits its breath too.
    private void Roam(float dt, IReadOnlyList<string> playlist, CampaignMusic music)
    {
        if (music.Playing.Length > 0 && !music.Ended) { Now = SongPhase(music); return; }
        if (music.Playing.Length > 0)
        {
            music.Want("", 0f);
            Plan(playlist, true);
        }
        else if (breath < 0f) Plan(playlist, Now == CombatPhase.Assault);
        Now = CombatPhase.Control;
        if (playlist == null || playlist.Count == 0) return;
        // Down to nothing and no lower: a negative breath means none is planned, and the song would be planned again
        // as if the run had just gone loud.
        if (breath > 0f) { breath = Mathf.Max(0f, breath - dt); return; }
        breath = -1f;
        ownSong = "";
        music.Want(playlist[nextSong++ % playlist.Count], enterAt);
        Now = SongPhase(music);
    }

    // The quiet before the next song and where in itself that song comes in. A rest runs from the last assault's end to
    // the next anticipation: what is behind the run already, this quiet, and what the song brings before its first
    // highlight. It is kept between Control.least and Control.longest: the quiet is gap at least and stretched to reach
    // least (song 2 ends four seconds after its last assault; song 1's first comes at sixteen), and a song that would
    // bring more than longest allows comes in further along (song 2's first comes at ninety-nine). Going loud
    // (breathe false) nothing is behind the run and the song comes at once.
    private void Plan(IReadOnlyList<string> playlist, bool breathe)
    {
        Load();
        breath = breathe ? gap : -1f;
        enterAt = 0f;
        if (playlist == null || playlist.Count == 0) return;
        var next = CampaignMusic.Find(playlist[nextSong % playlist.Count]);
        if (!breathe) { enterAt = EntryOf(next); return; }
        float since = Now == CombatPhase.Assault ? 0f : Mathf.Max(0f, Time.unscaledTime - assaultEnded);
        enterAt = EntryOf(next, since + gap);
        float before = Mathf.Max(0f, next.FirstAssault - lead - enterAt);
        breath = Mathf.Max(gap, least - since - before);
    }

    private static CombatPhase SongPhase(CampaignMusic music)
        => music.Current != null ? PhaseAt(music.Current, music.Clock, out _) : CombatPhase.Control;

    private static Numbers Of(CombatPhase phase)
    {
        Load();
        return paces[(int)phase];
    }

    // Tables/Phases, a row a phase; a phase without its row sends at the steps' own numbers.
    private static void Load()
    {
        if (loaded) return;
        loaded = true;
        paces[(int)CombatPhase.Quiet] = new Numbers();
        for (var phase = CombatPhase.Control; phase <= CombatPhase.Assault; phase++)
        {
            var pace = new Numbers { Most = 1f, Rate = 1f, MobRate = 4f };
            if (GameTables.TryGetRow(TableName, phase.ToString(), out var row))
            {
                pace.Most = Mathf.Max(0f, row.Number("most", 1f));
                pace.Rate = Mathf.Max(0f, row.Number("rate", 1f));
                pace.Mob = Mathf.Max(0, row.Whole("mob", 0));
                pace.MobRate = Mathf.Max(.1f, row.Number("mobRate", 4f));
                pace.Special = Mathf.Clamp01(row.Number("special", 0f));
                pace.Drops = Mathf.Max(0f, row.Number("drops", 1f));
            }
            else Debug.LogWarning($"[Campaign] Tables/{TableName} has no row '{phase}'; it sends at the steps' own numbers.");
            paces[(int)phase] = pace;
        }
        lead = Mathf.Max(0f, GameTables.Number(TableName, nameof(CombatPhase.Anticipation), "lead", 12f));
        least = Mathf.Max(0f, GameTables.Number(TableName, nameof(CombatPhase.Control), "least", 25f));
        gap = Mathf.Max(0f, GameTables.Number(TableName, nameof(CombatPhase.Control), "gap", 3f));
        // No longest rest without the cell: every song from its first second, as before the table had it.
        longest = Mathf.Max(0f, GameTables.Number(TableName, nameof(CombatPhase.Control), "longest", 0f));
        enterFade = Mathf.Max(.01f, GameTables.Number(TableName, nameof(CombatPhase.Control), "enterFade", 2f));
    }

    // Live table edits and a new Play session read the phases again.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        loaded = false;
        GameTables.Reloaded -= Forget;
        GameTables.Reloaded += Forget;
    }

    private static void Forget() => loaded = false;
}
