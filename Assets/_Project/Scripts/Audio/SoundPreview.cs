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
/// </summary>
public static class SoundPreview
{
    private const string Usage = "사용법: /sound <뱅크>/<키> (예: Zombie/Bone/NeckSnap) · /sound cue <큐 이름> (예: Execution/Behind) · /sound list <뱅크> (Movement·Voice·HitConfirm·Bullets·Guns·UI·Zombie)";

    public static string Command(string args)
    {
        string text = (args ?? "").Trim();
        if (text.Length == 0) return Usage;
        string[] parts = text.Split(new[] { ' ', '\t' }, 2, StringSplitOptions.RemoveEmptyEntries);
        string verb = parts[0], rest = parts.Length > 1 ? parts[1].Trim() : "";
        if (verb.Equals("list", StringComparison.OrdinalIgnoreCase)) return List(rest);
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
