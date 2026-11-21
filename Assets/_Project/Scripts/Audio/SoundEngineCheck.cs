#if UNITY_EDITOR
using System;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The editor's check of the sound floor (GameSounds over Sonity, Docs/Features/SoundRebuild.md stage 1), without
/// playing: the mixer has every channel's group and Loud group, and every key of every sound bank builds into a Sonity
/// sound the way GameSounds builds it while the game runs, flat and in 3D, with its clips, its channel's group (the Loud
/// one for a volume past 1) and none of Sonity's own defaults that would make it sound unlike the older way.
/// Logs/sound-engine-check.txt ("소리 엔진 점검 봐 줘"); its first line says ALL KEPT or how many broke. It runs by
/// itself once after every script reload and after a play, from ENFILADE → 소리 → 소리 엔진 점검, and when
/// Temp/enfilade-soundcheck.request is made (CheckOnRequest). What it can't see: how a sound sounds, and the mixer's
/// exposed parameters (Unity answers for them only while playing).
/// </summary>
[InitializeOnLoad]
internal static class SoundEngineCheck
{
    private const string Request = "Temp/enfilade-soundcheck.request", ReportPath = "Logs/sound-engine-check.txt";

    static SoundEngineCheck() => new CheckOnRequest(Request, Run);

    [MenuItem("ENFILADE/소리/소리 엔진 점검")]
    private static void Run()
    {
        var report = new CheckReport();
        int sounds = 0;
        try
        {
            Mixer(report);
            sounds = Sounds(report);
        }
        catch (Exception problem)
        {
            report.Stopped(problem);
        }
        report.Write(ReportPath, "sound engine check, " + sounds + " sounds built");
    }

    private static void Mixer(CheckReport report)
    {
        report.Rule("the mixer Resources/Audio/GameMixer has a group and a Loud group for every channel (python Tools/Audio/make_mixer.py)");
        report.Expect(GameAudio.Mixer != null, "no mixer at Resources/Audio/GameMixer");
        if (GameAudio.Mixer == null) return;
        foreach (AudioChannel channel in Enum.GetValues(typeof(AudioChannel)))
        {
            var group = GameAudio.Group(channel);
            var loud = GameAudio.Group(channel, true);
            report.Expect(group != null, channel + ": no group");
            report.Expect(loud != null, channel + ": no Loud group");
        }
        report.Note("Loud groups add " + GameTables.Number(GameAudio.EngineTable, GameAudio.EngineRow, "loudDecibel", 12f)
            + " dB (×" + GameAudio.LoudRatio.ToString("0.##") + ")");
    }

    // Every key of every bank, flat and in 3D, built and thrown away again.
    private static int Sounds(CheckReport report)
    {
        report.Rule("every bank key builds into a Sonity sound with its clips, its channel's group and the older way's sound");
        int built = 0, loud = 0;
        foreach (var bank in Resources.LoadAll<SoundBank>(SoundBank.ResourceFolder))
        {
            foreach (var entry in bank.Entries)
            {
                if (entry == null || entry.clips == null || entry.clips.Length == 0) continue;
                foreach (bool spatial in new[] { false, true })
                {
                    string name = bank.name + "/" + entry.key + (spatial ? " 3D" : "");
                    var sound = GameSounds.Build(bank.name, entry.key, AudioChannel.Combat,
                        spatial ? GameSounds.Space.Default : (GameSounds.Space?)null, out string problem);
                    try
                    {
                        report.Expect(sound != null && problem == null, name + ": " + (problem ?? "not built"));
                        if (sound == null) continue;
                        var container = sound.internals.soundContainers[0];
                        var data = container.internals.data;
                        report.Expect(container.internals.audioClips.Length == entry.clips.Length,
                            name + ": " + container.internals.audioClips.Length + " clips, the bank has " + entry.clips.Length);
                        report.Expect(!sound.IsNull(), name + ": Sonity calls it empty");
                        report.Expect(Mathf.Approximately(data.spatialBlend, spatial ? 1f : 0f) && data.distanceEnabled == spatial,
                            name + ": spatial blend " + data.spatialBlend + ", distance " + data.distanceEnabled);
                        report.Expect(Mathf.Approximately(data.spatialBlendDistance3DIncrease, 1f) && Mathf.Approximately(data.spatialSpreadRatio, 0f),
                            name + ": Sonity's near-2D blend or spread left on");
                        var settings = bank.SettingsOf(entry.key);
                        bool over = settings.Volume > 1f;
                        report.Expect(sound.internals.data.audioMixerGroup == GameAudio.Group(AudioChannel.Combat, over),
                            name + ": not in the Combat" + (over ? " Loud" : "") + " group");
                        if (over && !spatial) loud++;
                        built++;
                    }
                    finally
                    {
                        GameSounds.Forget(sound);
                    }
                }
            }
        }
        report.Note(built + " built (each key flat and 3D), " + loud + " keys over full volume play in a Loud group");
        return built;
    }
}
#endif
