using System;
using UnityEngine;

// DeathScreen: The preview. One part of the class; the rest is in DeathScreen.cs and the files beside it.
public sealed partial class DeathScreen
{
    private string StartPreview(string args)
    {
        if (!receiver.IsAlive) return "죽어 있을 때는 미리 볼 수 없습니다. 살아난 뒤에 /death"; // dev-text
        bool asWipe = false, shortOne = GameSettings.Current.deathScreen == DeathScreenMode.Short;
        string cause = null;
        foreach (string word in (args ?? "").ToLowerInvariant().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries))
        {
            switch (word)
            {
                case "wipe": asWipe = true; break;
                case "short": shortOne = true; break;
                case "full": shortOne = false; break;
                case "fall": cause = Texts.Get("death.cause.fall"); break;
                case "bleed": cause = Texts.Format("death.cause.bleed_by", Pink(Texts.Get(ZombieType.NameId(ZombieKind.Shambler)))); break;
                case "friend": cause = Texts.Format("death.cause.friend", PlayerTally.ColoredNameOf(player)); break;
                default:
                    if (!Enum.TryParse(word, true, out ZombieKind kind) || int.TryParse(word, out _))
                        return "모르는 말: " + word + " — /death [좀비 종류 / fall / bleed / friend] [wipe] [short / full]"; // dev-text
                    cause = SampleCause(kind);
                    break;
            }
        }
        cause ??= SampleCause(ZombieKind.Brute);
        if (phase != Phase.Hidden) Finish();
        var chosen = shortOne ? brief : full;
        float now = Time.unscaledTime;
        previewing = true;
        previewWipe = asWipe;
        previewUntil = now + Mathf.Max(.5f, chosen.previewSeconds);
        Show(now, Facts(cause, asWipe), asWipe, chosen);
        // dev-text
        return "데스 스크린 미리 보기 (" + (shortOne ? "짧게" : "켬") + ", " + chosen.previewSeconds.ToString("0.#") + "초). 설정이 끔이어도 보인다. "
            + "/death [좀비 종류 / fall / bleed / friend] [wipe] [short / full]";
    }
}
