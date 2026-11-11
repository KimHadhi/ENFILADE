using System;
using UnityEngine;

/// <summary>
/// The menus' sounds, played flat on the UI channel (GameAudio: they follow the UI volume and keep playing while the
/// game is paused). A cue is, in order, a clip from the UI sound bank when its folder holds any
/// (Art/Audio/UI/&lt;cue&gt;, e.g. Art/Audio/UI/Buy; volume and pitch in Tables/Sounds, row "UI/&lt;cue&gt;") and otherwise a
/// short sound made here once, so a menu answers the player before any clips exist, as the guns do (FeelAudio).
/// First used by the shop (user, 2026-11-10: nothing told them a buy went through); the HUD's alerts and what the
/// player picks up use it too, each kind of pickup with a sound of its own.
/// </summary>
public static class KitSound
{
    /// <summary>
    /// Buy: something was paid for. Refuse: it couldn't be. Alert: a teammate went down or died (AlertHUD). The Pick
    /// cues: this player took ammo, a medkit, a shield cell, money or a gun off the floor (FieldPickups). Escape: a
    /// chapter is escaped (ChapterEndBanner). Stealth (StealthMarkersHUD, CampaignRun): Suspect, a zombie stopped to look
    /// this player's way; Spotted, one woke on them; Unseen, a stretch got through without being found.
    /// </summary>
    /// Charge: an Assault's CHARGE sounded (ClassKit). Taunt: a Knight's taunt went out (its own, or a friend's, softer
    /// the further). Hunter: a Ghost's headshot added a Head Hunter stack; HunterLost: a hurt took them all. Ping: F
    /// marked an enemy for the team; Mark: a Schießhund's bounty mark went on; Bounty: its mark died at its hand.
    public enum Cue { Buy, Refuse, Alert, PickAmmo, PickHeal, PickShield, PickMoney, PickGun, Escape, Suspect, Spotted, Unseen, Charge,
        Taunt, Hunter, HunterLost, Ping, Mark, Bounty }

    private const string BankId = "UI";
    private const int Rate = 44100;
    private static readonly AudioClip[] made = new AudioClip[Enum.GetValues(typeof(Cue)).Length];
    private static AudioSource source;
    private static SoundBank bank;
    private static bool bankLooked;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Forget()
    {
        source = null;
        bank = null;
        bankLooked = false;
        Array.Clear(made, 0, made.Length);
    }

    public static void Play(Cue cue, float volume = 1f)
    {
        if (!Application.isPlaying || volume <= 0f) return;
        if (source == null)
        {
            var host = new GameObject("Kit Sound");
            UnityEngine.Object.DontDestroyOnLoad(host);
            source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            GameAudio.Bind(source, AudioChannel.UI);
        }
        if (!bankLooked) { bankLooked = true; bank = SoundBank.Load(BankId); }
        string key = cue.ToString();
        var clip = bank != null ? bank.Pick(key) : null;
        if (clip != null)
        {
            var settings = bank.SettingsOf(key);
            source.pitch = settings.RandomPitch();
            source.PlayOneShot(clip, Mathf.Clamp01(settings.Volume * volume));
            return;
        }
        source.pitch = 1f;
        source.PlayOneShot(Made(cue), Mathf.Clamp01(volume));
    }

    private static AudioClip Made(Cue cue)
    {
        int index = (int)cue;
        if (made[index] != null) return made[index];
        float[] data = cue switch
        {
            Cue.Buy => Till(),
            Cue.Alert => Fall(),
            Cue.PickAmmo => Magazine(),
            Cue.PickHeal => Mend(),
            Cue.PickShield => Charge(),
            Cue.PickMoney => Coin(),
            Cue.PickGun => Rack(),
            Cue.Escape => Away(),
            Cue.Suspect => Wonder(),
            Cue.Spotted => Found(),
            Cue.Unseen => Slip(),
            Cue.Charge => Rally(),
            Cue.Taunt => Clash(),
            Cue.Hunter => Notch(),
            Cue.HunterLost => Shatter(),
            Cue.Ping => Spot(),
            Cue.Mark => Brand(),
            Cue.Bounty => Claim(),
            _ => Buzz()
        };
        var clip = AudioClip.Create("Kit " + cue, data.Length, 1, Rate, false);
        clip.SetData(data, 0);
        clip.hideFlags = HideFlags.HideAndDontSave;
        return made[index] = clip;
    }

    // A till: the drawer's clack, then its bell struck twice a fifth apart, the second ringing out.
    private static float[] Till()
    {
        var data = new float[(int)(Rate * .62f)];
        var random = new System.Random(4217);
        float held = 0f;
        int clack = (int)(Rate * .03f);
        for (int i = 0; i < clack; i++)
        {
            float t = i / (float)Rate;
            held += ((float)random.NextDouble() * 2f - 1f - held) * .35f;
            data[i] += held * Mathf.Exp(-t / .006f) * .5f;
        }
        Bell(data, .02f, 1568f, .1f, .34f);
        Bell(data, .11f, 2349f, .22f, .42f);
        return Finish(data, .7f);
    }

    // A struck bell starting at <at> (s): the note, with two partials above it that die sooner.
    private static void Bell(float[] data, float at, float pitch, float decay, float level)
    {
        int from = (int)(at * Rate);
        for (int i = from; i < data.Length; i++)
        {
            float t = (i - from) / (float)Rate, turn = 2f * Mathf.PI * pitch * t;
            float wave = Mathf.Sin(turn) * Mathf.Exp(-t / decay)
                + .45f * Mathf.Sin(turn * 2.76f) * Mathf.Exp(-t / (decay * .55f))
                + .25f * Mathf.Sin(turn * 5.4f) * Mathf.Exp(-t / (decay * .3f));
            data[i] += wave * Mathf.Min(1f, t / .002f) * level;
        }
    }

    // A refusal: two short low blips, dull so they never pass for a hit or a buy.
    private static float[] Buzz()
    {
        const float Blip = .085f, Every = .12f;
        var data = new float[(int)(Rate * (Every + Blip + .03f))];
        int length = (int)(Blip * Rate);
        for (int blip = 0; blip < 2; blip++)
        {
            int from = (int)(blip * Every * Rate);
            for (int i = 0; i < length && from + i < data.Length; i++)
            {
                float t = i / (float)Rate, turn = 2f * Mathf.PI * 165f * t;
                float edge = Mathf.Min(1f, Mathf.Min(t / .004f, (Blip - t) / .012f));
                data[from + i] += (Mathf.Sin(turn) + .5f * Mathf.Sin(turn * 3f) + .22f * Mathf.Sin(turn * 5f)) * edge;
            }
        }
        return Finish(data, .6f);
    }

    // An alert: two round notes, the second a fourth lower and ringing a little longer. Falling, so it reads as bad news,
    // and soft, so it is never taken for a hit.
    private static float[] Fall()
    {
        var data = new float[(int)(Rate * .5f)];
        Note(data, 0f, 880f, .08f, .5f);
        Note(data, .13f, 659f, .15f, .5f);
        return Finish(data, .55f);
    }

    // Ammo: a magazine dropped into a pouch, two dry clicks over a short low knock.
    private static float[] Magazine()
    {
        var data = new float[(int)(Rate * .2f)];
        var random = new System.Random(7741);
        Click(data, random, 0f, .012f, .7f);
        Click(data, random, .055f, .009f, .5f);
        Knock(data, .004f, 190f, .03f, .6f);
        return Finish(data, .6f);
    }

    // A medkit: two round notes going up a fifth, the kind of sound that says better.
    private static float[] Mend()
    {
        var data = new float[(int)(Rate * .42f)];
        Note(data, 0f, 587f, .07f, .5f);
        Note(data, .1f, 880f, .13f, .5f);
        return Finish(data, .5f);
    }

    // A shield cell: a tone sweeping up as it charges, with a fine flutter on it, then let go.
    private static float[] Charge()
    {
        var data = new float[(int)(Rate * .34f)];
        float turn = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            float t = i / (float)Rate, along = t / .34f;
            turn += 2f * Mathf.PI * Mathf.Lerp(420f, 1500f, along * along) / Rate;
            float level = Mathf.Min(1f, t / .01f) * Mathf.Min(1f, (.34f - t) / .09f);
            data[i] = (Mathf.Sin(turn) + .35f * Mathf.Sin(turn * 2f)) * (.8f + .2f * Mathf.Sin(2f * Mathf.PI * 46f * t)) * level;
        }
        return Finish(data, .45f);
    }

    // Money: a coin's two quick high rings, thinner and shorter than the shop's till.
    private static float[] Coin()
    {
        var data = new float[(int)(Rate * .3f)];
        Bell(data, 0f, 2637f, .05f, .3f);
        Bell(data, .045f, 3520f, .1f, .34f);
        return Finish(data, .5f);
    }

    // A gun: taken up and its action worked, a heavy knock and two clacks after it.
    private static float[] Rack()
    {
        var data = new float[(int)(Rate * .3f)];
        var random = new System.Random(1187);
        Knock(data, 0f, 120f, .05f, .8f);
        Click(data, random, .002f, .014f, .6f);
        Click(data, random, .1f, .012f, .7f);
        Click(data, random, .16f, .016f, .55f);
        return Finish(data, .65f);
    }

    // A chapter escaped: four notes climbing a chord, the last held, over a low note under them all.
    private static float[] Away()
    {
        var data = new float[(int)(Rate * 1.5f)];
        Note(data, 0f, 131f, .7f, .35f);
        Note(data, 0f, 523f, .12f, .4f);
        Note(data, .11f, 659f, .12f, .4f);
        Note(data, .22f, 784f, .14f, .4f);
        Note(data, .34f, 1047f, .5f, .5f);
        return Finish(data, .6f);
    }

    // A dry click starting at <at> (s): a burst of dulled noise dying over <decay>.
    private static void Click(float[] data, System.Random random, float at, float decay, float level)
    {
        int from = (int)(at * Rate), length = (int)(decay * 6f * Rate);
        float held = 0f;
        for (int i = 0; i < length && from + i < data.Length; i++)
        {
            float t = i / (float)Rate;
            held += ((float)random.NextDouble() * 2f - 1f - held) * .45f;
            data[from + i] += held * Mathf.Exp(-t / decay) * level;
        }
    }

    // A low knock starting at <at> (s): a tone that drops as it dies, the weight under a click.
    private static void Knock(float[] data, float at, float pitch, float decay, float level)
    {
        int from = (int)(at * Rate);
        float turn = 0f;
        for (int i = from; i < data.Length; i++)
        {
            float t = (i - from) / (float)Rate;
            turn += 2f * Mathf.PI * pitch * (1f + Mathf.Exp(-t / (decay * .5f))) / Rate;
            data[i] += Mathf.Sin(turn) * Mathf.Exp(-t / decay) * Mathf.Min(1f, t / .002f) * level;
        }
    }

    // A plain note starting at <at> (s): the pitch and a little of its octave and twelfth.
    private static void Note(float[] data, float at, float pitch, float decay, float level)
    {
        int from = (int)(at * Rate);
        for (int i = from; i < data.Length; i++)
        {
            float t = (i - from) / (float)Rate, turn = 2f * Mathf.PI * pitch * t;
            data[i] += (Mathf.Sin(turn) + .3f * Mathf.Sin(turn * 2f) + .12f * Mathf.Sin(turn * 3f)) * Mathf.Exp(-t / decay) * Mathf.Min(1f, t / .004f) * level;
        }
    }

    // Something stopped to look this way: two soft notes rising a fourth, a question, quiet so it warns without startling.
    private static float[] Wonder()
    {
        var data = new float[(int)(Rate * .34f)];
        Click(data, new System.Random(77), 0f, .003f, .25f);
        Note(data, 0f, 740f, .07f, .45f);
        Note(data, .085f, 988f, .13f, .55f);
        return Finish(data, .5f);
    }

    // Found: a hard knock under a bright chord that bends up as it strikes, gone in half a second. Our own sting, made
    // here, for the '!' over the zombie that woke.
    private static float[] Found()
    {
        var data = new float[(int)(Rate * .55f)];
        Click(data, new System.Random(913), 0f, .004f, .9f);
        Knock(data, 0f, 150f, .11f, .8f);
        Stab(data, 0f, 880f, .2f, .5f);
        Stab(data, .012f, 1318f, .16f, .32f);
        return Finish(data, .78f);
    }

    // Through unseen: three bells climbing, soft and bright.
    private static float[] Slip()
    {
        var data = new float[(int)(Rate * .9f)];
        Bell(data, 0f, 1175f, .22f, .28f);
        Bell(data, .09f, 1568f, .26f, .32f);
        Bell(data, .18f, 2349f, .45f, .36f);
        return Finish(data, .6f);
    }

    // A CHARGE: a drum's thump under a horn call climbing a fifth, the second note held, as an officer's call to charge.
    private static float[] Rally()
    {
        var data = new float[(int)(Rate * .9f)];
        Knock(data, 0f, 90f, .14f, 1f);
        Click(data, new System.Random(5), 0f, .006f, .5f);
        Horn(data, .02f, 262f, .16f, .45f);
        Horn(data, .17f, 392f, .55f, .55f);
        return Finish(data, .8f);
    }

    // A taunt: a blade struck flat on a shield, twice, the clang ringing over a low horn held under it, as a knight calls
    // the fight onto himself.
    private static float[] Clash()
    {
        var data = new float[(int)(Rate * 1f)];
        var random = new System.Random(1187);
        Knock(data, 0f, 70f, .16f, 1f);
        Click(data, random, 0f, .005f, .8f);
        Clang(data, 0f, 523f, .32f, .55f);
        Click(data, random, .13f, .004f, .6f);
        Clang(data, .13f, 587f, .4f, .45f);
        Horn(data, .05f, 98f, .8f, .4f);
        return Finish(data, .85f);
    }

    // A struck plate starting at <at> (s): partials too far apart to be a bell, so it reads as metal hit, not rung.
    private static void Clang(float[] data, float at, float pitch, float decay, float level)
    {
        int from = (int)(at * Rate);
        for (int i = from; i < data.Length; i++)
        {
            float t = (i - from) / (float)Rate, turn = 2f * Mathf.PI * pitch * t;
            float wave = Mathf.Sin(turn) * Mathf.Exp(-t / decay)
                + .7f * Mathf.Sin(turn * 2.41f) * Mathf.Exp(-t / (decay * .6f))
                + .5f * Mathf.Sin(turn * 3.89f) * Mathf.Exp(-t / (decay * .4f))
                + .3f * Mathf.Sin(turn * 6.13f) * Mathf.Exp(-t / (decay * .25f));
            data[i] += wave * Mathf.Min(1f, t / .001f) * level;
        }
    }

    // A Head Hunter stack: one bright tick climbing, short and quiet, heard under the shot's own crack.
    private static float[] Notch()
    {
        var data = new float[(int)(Rate * .16f)];
        Click(data, new System.Random(31), 0f, .002f, .4f);
        Stab(data, 0f, 1760f, .05f, .5f);
        return Finish(data, .4f);
    }

    // Every stack lost: a dull knock and two notes falling a fourth, as something lets go.
    private static float[] Shatter()
    {
        var data = new float[(int)(Rate * .42f)];
        var random = new System.Random(97);
        Click(data, random, 0f, .01f, .7f);
        Knock(data, 0f, 140f, .06f, .6f);
        Note(data, .02f, 698f, .06f, .4f);
        Note(data, .1f, 523f, .14f, .45f);
        return Finish(data, .55f);
    }

    // A ping: two quick high notes a fifth apart, bright and short, a finger pointed.
    private static float[] Spot()
    {
        var data = new float[(int)(Rate * .22f)];
        Click(data, new System.Random(61), 0f, .002f, .3f);
        Note(data, 0f, 1319f, .04f, .5f);
        Note(data, .05f, 1976f, .08f, .5f);
        return Finish(data, .45f);
    }

    // A bounty mark: a dry knock and a low bell under a high one, a coin pressed on a wanted poster.
    private static float[] Brand()
    {
        var data = new float[(int)(Rate * .45f)];
        Knock(data, 0f, 160f, .05f, .7f);
        Click(data, new System.Random(73), 0f, .004f, .5f);
        Bell(data, .01f, 784f, .18f, .4f);
        Bell(data, .06f, 2349f, .16f, .3f);
        return Finish(data, .55f);
    }

    // A bounty claimed: a revolver's hammer, a heavy knock, and a till's bell ringing three notes up, paid out.
    private static float[] Claim()
    {
        var data = new float[(int)(Rate * .9f)];
        var random = new System.Random(1878);
        Click(data, random, 0f, .006f, .8f);
        Knock(data, 0f, 95f, .12f, 1f);
        Bell(data, .04f, 1047f, .12f, .4f);
        Bell(data, .12f, 1319f, .14f, .4f);
        Bell(data, .2f, 1568f, .4f, .5f);
        return Finish(data, .8f);
    }

    // A horn note starting at <at> (s) for <length> (s): odd and even partials swelling in and dying away.
    private static void Horn(float[] data, float at, float pitch, float length, float level)
    {
        int from = (int)(at * Rate), count = (int)(length * Rate);
        for (int i = 0; i < count && from + i < data.Length; i++)
        {
            float t = i / (float)Rate, turn = 2f * Mathf.PI * pitch * t * (1f + .004f * Mathf.Sin(2f * Mathf.PI * 5f * t));
            float wave = Mathf.Sin(turn) + .55f * Mathf.Sin(turn * 2f) + .4f * Mathf.Sin(turn * 3f) + .22f * Mathf.Sin(turn * 4f) + .12f * Mathf.Sin(turn * 5f);
            float swell = Mathf.Min(1f, t / .03f) * Mathf.Min(1f, (length - t) / .08f);
            data[from + i] += wave * swell * level;
        }
    }

    // A stab starting at <at> (s): a bright chord bending up a little as it strikes.
    private static void Stab(float[] data, float at, float pitch, float decay, float level)
    {
        int from = (int)(at * Rate);
        float turn = 0f;
        for (int i = from; i < data.Length; i++)
        {
            float t = (i - from) / (float)Rate;
            turn += 2f * Mathf.PI * pitch * (1f + .18f * (1f - Mathf.Exp(-t / .03f))) / Rate;
            float wave = Mathf.Sin(turn) + .6f * Mathf.Sin(turn * 1.5f) + .45f * Mathf.Sin(turn * 2f) + .2f * Mathf.Sin(turn * 3.01f);
            data[i] += wave * Mathf.Exp(-t / decay) * Mathf.Min(1f, t / .0015f) * level;
        }
    }

    // Brings the loudest sample to <peak> and fades the last few ms so the end doesn't click.
    private static float[] Finish(float[] data, float peak)
    {
        float top = 0f;
        foreach (float sample in data) top = Mathf.Max(top, Mathf.Abs(sample));
        if (top > 0f)
        {
            float gain = peak / top;
            for (int i = 0; i < data.Length; i++) data[i] *= gain;
        }
        int fade = Mathf.Min(data.Length, Rate / 100);
        for (int i = 0; i < fade; i++) data[data.Length - 1 - i] *= i / (float)fade;
        return data;
    }
}
