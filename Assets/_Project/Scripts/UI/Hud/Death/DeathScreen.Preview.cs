using System;
using UnityEngine;

// DeathScreen: The preview. One part of the class; the rest is in DeathScreen.cs and the files beside it.
public sealed partial class DeathScreen
{
    private string StartPreview(string args)
    {
        if (!receiver.IsAlive) return "죽어 있을 때는 미리 볼 수 없습니다. 살아난 뒤에 /death"; // dev-text
        bool asWipe = false;
        string cause = null;
        foreach (string word in (args ?? "").ToLowerInvariant().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            switch (word)
            {
                case "wipe": asWipe = true; break;
                case "fall": cause = Texts.Get("death.cause.fall"); break;
                case "bleed": cause = Texts.Format("death.cause.bleed_by", Pink(Texts.Get(ZombieType.NameId(ZombieKind.Shambler)))); break;
                case "friend": cause = Texts.Format("death.cause.friend", PlayerTally.ColoredNameOf(player)); break;
                default:
                    if (!Enum.TryParse(word, true, out ZombieKind kind) || int.TryParse(word, out _))
                        return "모르는 말: " + word + " — /death [좀비 종류 / fall / bleed / friend] [wipe]"; // dev-text
                    cause = SampleCause(kind);
                    break;
            }
        }
        cause ??= SampleCause(ZombieKind.Brute);
        if (phase != Phase.Hidden) Finish();
        float now = Time.unscaledTime;
        previewing = true;
        previewWipe = asWipe;
        previewUntil = now + Mathf.Max(.5f, look.previewSeconds);
        // The addon's Preview button: shown even with the screen switched off.
        Show(now, Facts(cause, asWipe), asWipe);
        // dev-text
        return "데스 스크린 미리 보기 (" + look.previewSeconds.ToString("0.#") + "초). 설정에서 꺼 두어도 보인다. "
            + "/death [좀비 종류 / fall / bleed / friend] [wipe]";
    }
}
