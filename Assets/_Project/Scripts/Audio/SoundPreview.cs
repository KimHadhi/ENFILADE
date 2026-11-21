using System;
using System.Linq;
using System.Text;

/// <summary>
/// The /sound console command: hear a sound without staging the moment it belongs to (an execution from behind, a limb
/// torn off, a shield cell picked up), so a clip dropped into a bank folder or a row changed in Tables/Sounds or
/// Tables/SoundCues is checked at once. Plays flat at the volume and pitch its tables give.
///   /sound Zombie/Bone/NeckSnap     one key of a bank (Resources/Audio/&lt;bank&gt;), a random clip of it
///   /sound cue Execution/Behind     a SoundCues row, delayed parts included (BodyBreakSounds.Play)
///   /sound list Zombie              the keys of a bank
///   /sound sonity Zombie/Reaper/Hit [m]   the same key through Sonity (GameSounds), the way every sound is to play:
///                                   flat, or that many metres ahead of the eyes in 3D (Space.Default)
/// </summary>
public static class SoundPreview
{
    private const string Usage = "사용법: /sound <뱅크>/<키> (예: Zombie/Bone/NeckSnap) · /sound cue <큐 이름> (예: Execution/Behind) · /sound list <뱅크> (Movement·Voice·HitConfirm·Bullets·Guns·UI·Zombie·Stealth) · /sound sonity <뱅크>/<키> [거리 m] (Sonity로, 거리를 주면 그만큼 앞에서 3D)";

    public static string Command(string args)
    {
        string text = (args ?? "").Trim();
        if (text.Length == 0) return Usage;
        string[] parts = text.Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
        string verb = parts[0], rest = parts.Length > 1 ? parts[1].Trim() : "";
        if (verb.Equals("list", StringComparison.OrdinalIgnoreCase)) return List(rest);
        if (verb.Equals("sonity", StringComparison.OrdinalIgnoreCase)) return ThroughSonity(rest);
        if (verb.Equals("cue", StringComparison.OrdinalIgnoreCase))
            return rest.Length == 0 ? Usage : BodyBreakSounds.Play(rest, null) ? "큐 " + rest + " 재생" : "SoundCues.csv에 '" + rest + "' 줄이 없습니다.";
        return Key(text);
    }

    private static string Key(string path)
    {
        int slash = path.IndexOf('/');
        if (slash <= 0 || slash == path.Length - 1) return Usage;
        string bankId = path.Substring(0, slash), key = path.Substring(slash + 1);
        var bank = SoundBank.Load(bankId);
        if (bank == null) return "소리 뱅크 '" + bankId + "'가 없습니다 (Resources/Audio).";
        var clip = bank.Pick(key);
        if (clip == null) return "'" + bankId + "' 뱅크에 '" + key + "' 키가 없습니다. /sound list " + bankId + "로 키를 봅니다.";
        var settings = bank.SettingsOf(key);
        FeelAudio.PlayClip(clip, settings.Volume, settings.RandomPitch());
        return path + " → " + clip.name + " (볼륨 " + settings.Volume.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture) + ")";
    }

    // The new floor heard by itself (stage 1: nothing in play uses it yet): the key as GameSounds plays it, on the Combat
    // channel, flat, or 3D that far ahead of the eyes.
    private static string ThroughSonity(string rest)
    {
        string[] parts = rest.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return Usage;
        string path = parts[0];
        int slash = path.IndexOf('/');
        if (slash <= 0 || slash == path.Length - 1) return Usage;
        string bankId = path.Substring(0, slash), key = path.Substring(slash + 1);
        float metres = -1f;
        if (parts.Length > 1 && (!TableCells.TryNumber(parts[1], out metres) || metres < 0f)) return Usage;
        var eye = UnityEngine.Camera.main;
        bool played = metres < 0f || eye == null
            ? GameSounds.Play(bankId, key, AudioChannel.Combat)
            : GameSounds.PlayAt(bankId, key, AudioChannel.Combat, eye.transform.position + eye.transform.forward * metres, GameSounds.Space.Default);
        if (!played) return "'" + bankId + "' 뱅크에 '" + key + "' 키가 없거나 Sonity가 아직 없습니다. /sound list " + bankId + "로 키를 봅니다.";
        return path + " → Sonity " + (metres < 0f || eye == null ? "2D" : metres.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture) + "m 앞 3D");
    }

    private static string List(string bankId)
    {
        if (bankId.Length == 0) return Usage;
        var bank = SoundBank.Load(bankId);
        if (bank == null) return "소리 뱅크 '" + bankId + "'가 없습니다 (Resources/Audio).";
        var text = new StringBuilder(bankId).Append(": ");
        text.Append(string.Join(", ", bank.Entries.Where(e => e != null && e.clips != null && e.clips.Length > 0)
            .Select(e => e.key + "(" + e.clips.Length + ")")));
        return text.ToString();
    }
}
