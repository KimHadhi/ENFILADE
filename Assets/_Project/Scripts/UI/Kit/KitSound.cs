using System;
using UnityEngine;

/// <summary>
/// The menus' sounds, played flat on the UI channel (GameAudio: they follow the UI volume and keep playing while the
/// game is paused). A cue is a clip from the UI sound bank (Art/Audio/UI/&lt;cue&gt;, e.g. Art/Audio/UI/Buy; volume and
/// pitch in Tables/Sounds, row "UI/&lt;cue&gt;"). The cues no file has been chosen for yet still have a short sound made
/// here (Docs/Features/SoundRebuild.md 5.9), and those go as their files come in; a cue whose file is in its folder has
/// none, and is silent if the folder is emptied. First used by the shop (user, 2026-11-10: nothing told them a buy went
/// through); the HUD's alerts and what the player picks up use it too, every pickup the shared taking (Pick) under a
/// sound of its own kind (PlayPickup).
/// </summary>
public static class KitSound
{
    /// <summary>
    /// Buy: something was paid for. Refuse: it couldn't be. Alert: a teammate went down or died (AlertHUD). The Pick
    /// cues: this player took ammo, a medkit, a shield cell, money or a gun off the floor (FieldPickups). Escape: the
    /// results show's rows and its wait (ResultsShow). Stealth (StealthMarkersHUD, CampaignRun): Suspect, a zombie stopped to look
    /// this player's way; Spotted, one woke on them; Unseen, a stretch got through without being found.
    /// </summary>
    /// Charge: an Assault's CHARGE sounded (ClassKit). Taunt: a Knight's taunt went out (its own, or a friend's, softer
    /// the further). Hunter: a Ghost's headshot added a Head Hunter stack; HunterLost: a hurt took them all. Ping: F
    /// marked an enemy for the team; Mark: a Schießhund's bounty mark went on; Bounty: its mark died at its hand. Supply:
    /// a Support handed ammo over; Hammer: it put a barricade up; Splinter: a zombie's blow on one; Collapse: one broke.
    /// The ENFILADE tower (TowerRun): TowerRise, a hall's scanners coming up out of the floor; TowerTick, a second of the
    /// scanners' count; TowerScan, a palm read; TowerOpen, a lane's door unlocking; TowerClear, a floor got through;
    /// Enfiladed, the tower's last kill and its banner (EnfiladedFinale).
    /// The ability cards (PerkCardScreen): CardPick, a card pressed and let go; CardPickGrand, the same with a low fifth
    /// under it, for a card picked to its last level or a special one; CardBurn, the cards not picked catching fire.
    /// Pick: the taking itself, under every pickup's own cue (PlayPickup). Added last so the cues before it keep their numbers.
    public enum Cue { Buy, Refuse, Alert, PickAmmo, PickHeal, PickShield, PickMoney, PickGun, Escape, Suspect, Spotted, Unseen, Charge,
        Taunt, Hunter, HunterLost, Ping, Mark, Bounty, Supply, Hammer, Splinter, Collapse,
        TowerRise, TowerTick, TowerScan, TowerOpen, TowerClear, Enfiladed, CardPick, CardPickGrand, CardBurn, Pick }

    private const string BankId = "UI";
    private const int Rate = 44100;
    private static readonly AudioClip[] made = new AudioClip[Enum.GetValues(typeof(Cue)).Length];
    // When each cue's last play runs out (real time), for Sounding.
    private static readonly float[] endsAt = new float[made.Length];
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
        Array.Clear(endsAt, 0, endsAt.Length);
    }

    /// <summary>
    /// The cue's last play is still sounding. A caller whose news comes in bursts (several zombies looking this way in
    /// one second) asks this rather than keeping a gap of its own, so the gap is always the sound's own length: the
    /// stealth stings became clips of two and three seconds (user, 2026-11-20), and a fixed gap sized for the made
    /// half-second tones would have stacked them.
    /// </summary>
    public static bool Sounding(Cue cue) => Application.isPlaying && Time.unscaledTime < endsAt[(int)cue];

    public static void Play(Cue cue, float volume = 1f)
    {
        if (!Application.isPlaying || volume <= 0f) return;
        if (source == null)
        {
            var host = new GameObject("Kit Sound");
            RuntimeSceneHierarchy.Keep(host);
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
        }
        else
        {
            // Only the cues still waiting for a file have a sound made here (Docs/Features/SoundRebuild.md 5.9); the
            // others are silent while their folder is empty.
            clip = Made(cue);
            if (clip == null) return;
            source.pitch = 1f;
            source.PlayOneShot(clip, Mathf.Clamp01(volume));
        }
        endsAt[(int)cue] = Time.unscaledTime + clip.length / Mathf.Max(.05f, source.pitch);
    }

    /// <summary>
    /// Something taken into the hands (off the floor, a rack, a crate, a thrown knife picked back up): the taking itself
    /// (Pick, Art/Audio/UI/Pick) under what was taken (PickAmmo, PickHeal…), so every pickup sounds like one and each kind
    /// adds its own (user, 2026-11-21: "Pick up SFX는 일단 줍기고 거기에 체력이나 쉴드나 그런 거에 따라 따로 추가 효과음").
    /// Given by a friend (a Support's resupply, a heal) is not a pickup and plays the kind's cue alone.
    /// </summary>
    public static void PlayPickup(Cue kind, float volume = 1f)
    {
        Play(Cue.Pick, volume);
        Play(kind, volume);
    }

    private static AudioClip Made(Cue cue)
    {
        int index = (int)cue;
        if (made[index] != null) return made[index];
        float[] data = cue switch
        {
            Cue.Buy => Till(),
            Cue.Refuse => Buzz(),
            Cue.Alert => Fall(),
            Cue.PickMoney => Coin(),
            Cue.Escape => Away(),
            Cue.Unseen => Slip(),
            Cue.Charge => Rally(),
            Cue.Taunt => Clash(),
            Cue.Hunter => Notch(),
            Cue.HunterLost => Shatter(),
            Cue.Ping => Spot(),
            Cue.Mark => Brand(),
            Cue.Bounty => Claim(),
            Cue.Supply => Pack(),
            Cue.Hammer => Nails(),
            Cue.Splinter => Crack(),
            Cue.Collapse => Timber(),
            Cue.TowerRise => Rise(),
            Cue.TowerTick => Tick(),
            Cue.TowerScan => Scan(),
            Cue.TowerOpen => Unlock(),
            Cue.TowerClear => Through(),
            Cue.Enfiladed => Raked(),
            Cue.CardPick => Pressed(false),
            Cue.CardPickGrand => Pressed(true),
            Cue.CardBurn => Kindled(),
            _ => null
        };
        if (data == null) return null;
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

    // Money: a coin's two quick high rings, thinner and shorter than the shop's till.
    private static float[] Coin()
    {
        var data = new float[(int)(Rate * .3f)];
        Bell(data, 0f, 2637f, .05f, .3f);
        Bell(data, .045f, 3520f, .1f, .34f);
        return Finish(data, .5f);
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

    // Ammo handed over: a bag's zip, then two magazines knocking into a pouch.
    private static float[] Pack()
    {
        var data = new float[(int)(Rate * .4f)];
        var random = new System.Random(4041);
        float held = 0f;
        int zip = (int)(Rate * .12f);
        for (int i = 0; i < zip; i++)
        {
            float t = i / (float)Rate;
            held += ((float)random.NextDouble() * 2f - 1f - held) * .6f;
            data[i] += held * (.5f + .5f * Mathf.Sin(2f * Mathf.PI * 90f * t)) * .35f;
        }
        Click(data, random, .15f, .01f, .7f);
        Knock(data, .15f, 180f, .03f, .5f);
        Click(data, random, .24f, .012f, .6f);
        Knock(data, .24f, 160f, .03f, .5f);
        return Finish(data, .6f);
    }

    // A barricade put up: three hammer blows on wood, each a dull knock with a sharp edge, the last the hardest.
    private static float[] Nails()
    {
        var data = new float[(int)(Rate * .62f)];
        var random = new System.Random(77);
        for (int blow = 0; blow < 3; blow++)
        {
            float at = blow * .17f, level = .7f + blow * .15f;
            Click(data, random, at, .004f, level);
            Knock(data, at, 230f - blow * 20f, .045f, level);
        }
        return Finish(data, .7f);
    }

    // A blow on a barricade: wood cracking, a short burst of splintering over a low knock.
    private static float[] Crack()
    {
        var data = new float[(int)(Rate * .26f)];
        var random = new System.Random(9137);
        Knock(data, 0f, 120f, .05f, .6f);
        for (int i = 0; i < 4; i++) Click(data, random, i * .025f, .006f, .8f - i * .15f);
        return Finish(data, .55f);
    }

    // A barricade broken: a heavy knock and planks clattering down one after another.
    private static float[] Timber()
    {
        var data = new float[(int)(Rate * .9f)];
        var random = new System.Random(2718);
        Knock(data, 0f, 85f, .16f, 1f);
        Click(data, random, 0f, .01f, .9f);
        float[] falls = { .16f, .27f, .35f, .5f, .62f };
        for (int i = 0; i < falls.Length; i++)
        {
            Click(data, random, falls[i], .008f, .6f - i * .08f);
            Knock(data, falls[i], 200f + i * 25f, .03f, .45f - i * .06f);
        }
        return Finish(data, .75f);
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

    // ---- The ENFILADE tower ----

    // A hall's scanners coming up: a motor's hum climbing under a ratchet, a clack as they lock at the top.
    private static float[] Rise()
    {
        var data = new float[(int)(Rate * 1.1f)];
        var random = new System.Random(5281);
        Sweep(data, 0f, 62f, 118f, .85f, .5f);
        for (int i = 0; i < 9; i++) Click(data, random, .05f + i * .09f, .003f, .22f);
        Knock(data, .86f, 96f, .09f, .8f);
        Click(data, random, .86f, .006f, .6f);
        return Finish(data, .55f);
    }

    // A second of the scanners' count: one clean blip, short enough to sit under the voices.
    private static float[] Tick()
    {
        var data = new float[(int)(Rate * .16f)];
        Note(data, 0f, 1318.5f, .045f, .7f);
        return Finish(data, .45f);
    }

    // A palm read: three quick blips climbing, the reader's chirp.
    private static float[] Scan()
    {
        var data = new float[(int)(Rate * .45f)];
        Note(data, 0f, 880f, .05f, .5f);
        Note(data, .07f, 1174.7f, .05f, .5f);
        Note(data, .14f, 1568f, .12f, .55f);
        return Finish(data, .5f);
    }

    // A lane's door unlocking: the bolt's clank, then three notes of a major chord going up, access granted.
    private static float[] Unlock()
    {
        var data = new float[(int)(Rate * .9f)];
        var random = new System.Random(6197);
        Knock(data, 0f, 82f, .08f, .9f);
        Click(data, random, 0f, .005f, .7f);
        Clang(data, .02f, 392f, .18f, .3f);
        Note(data, .1f, 523.3f, .2f, .4f);
        Note(data, .19f, 659.3f, .2f, .4f);
        Note(data, .28f, 784f, .45f, .5f);
        return Finish(data, .65f);
    }

    // A floor got through: a bright stab with a bell ringing over it.
    private static float[] Through()
    {
        var data = new float[(int)(Rate * 1.2f)];
        Stab(data, 0f, 392f, .22f, .6f);
        Stab(data, .12f, 587.3f, .3f, .5f);
        Bell(data, .12f, 1568f, .35f, .35f);
        return Finish(data, .65f);
    }

    // The tower's last kill (EnfiladedFinale): the frame stops on a boom, a crash of metal, and a brass chord swelling in
    // under the banner's slam.
    private static float[] Raked()
    {
        var data = new float[(int)(Rate * 2.6f)];
        var random = new System.Random(7741);
        Knock(data, 0f, 46f, .5f, 1f);
        Click(data, random, 0f, .02f, .9f);
        Clang(data, 0f, 311f, .6f, .45f);
        Clang(data, .02f, 466f, .5f, .3f);
        Stab(data, .1f, 261.6f, .9f, .45f);
        Stab(data, .1f, 392f, .9f, .35f);
        Horn(data, .14f, 65.4f, 2.2f, .35f);
        Horn(data, .14f, 98f, 2.2f, .25f);
        Bell(data, .16f, 2093f, .6f, .2f);
        return Finish(data, .9f);
    }

    // A tone gliding from one pitch to another over <length> (s) starting at <at>, swelling in and out: a motor or a siren.
    private static void Sweep(float[] data, float at, float from, float to, float length, float level)
    {
        int start = (int)(at * Rate), count = (int)(length * Rate);
        float turn = 0f;
        for (int i = 0; i < count && start + i < data.Length; i++)
        {
            float t = i / (float)Rate, share = t / length;
            turn += 2f * Mathf.PI * Mathf.Lerp(from, to, share) / Rate;
            float wave = Mathf.Sin(turn) + .5f * Mathf.Sin(turn * 2f) + .25f * Mathf.Sin(turn * 3f);
            float swell = Mathf.Min(1f, t / .05f) * Mathf.Min(1f, (length - t) / .1f);
            data[start + i] += wave * swell * level;
        }
    }

    // ---- The ability cards: low and soft. Their first sounds rang bells at 1.7-3.1 kHz whose overtones reached past
    // 10 kHz (the user, 2026-11-14: "효과음도 너무 고주파라 귀가 쨍해서 아파요"), so these use no bells, keep every note
    // under about 700 Hz and run a gentle low-pass over the whole clip, as the card looks draft page does. ----

    // A card pressed: a soft low knock as it goes down, then, let go a beat later (PerkCardScreen: its press and hold), a
    // warm swell under a soft chord; grand, a low fifth under them as well.
    private static float[] Pressed(bool grand)
    {
        const float Release = .09f;
        var data = new float[(int)(Rate * (grand ? 1.3f : 1.1f))];
        var random = new System.Random(3301);
        Thud(data, 0f, 150f, 70f, .045f, .5f);
        Rush(data, random, 0f, .03f, .2f, 800f, 400f, 0f);
        Glide(data, Release, 110f, 220f, .3f, .3f);
        Pad(data, Release + .02f, new[] { 220f, 277.2f, 329.6f }, .03f, .14f, .08f);
        if (grand) Pad(data, Release + .05f, new[] { 110f, 164.8f }, .05f, .2f, .08f);
        LowPass(data, 2200f);
        return Finish(data, .6f);
    }

    // Cards catching fire: a low rush that swells in and dies away, with a few dull pops in it.
    private static float[] Kindled()
    {
        var data = new float[(int)(Rate * .95f)];
        var random = new System.Random(6611);
        Rush(data, random, 0f, .9f, .32f, 450f, 900f, .25f);
        for (int pop = 0; pop < 9; pop++)
            Rush(data, random, .1f + (float)random.NextDouble() * .6f, .02f, .12f + (float)random.NextDouble() * .1f, 1100f, 700f, 0f);
        LowPass(data, 2200f);
        return Finish(data, .45f);
    }

    // A thud starting at <at> (s): a sine falling from <from> to <to> (Hz) over its first 1.5 × <decay> and gone by about
    // 5 × <decay>: a card going down, a landing.
    private static void Thud(float[] data, float at, float from, float to, float decay, float level)
    {
        int start = (int)(at * Rate), count = (int)(decay * 5f * Rate);
        float turn = 0f, fall = decay * 1.5f, tau = decay * 5f / 9.21f;
        for (int i = 0; i < count && start + i < data.Length; i++)
        {
            float t = i / (float)Rate;
            turn += 2f * Mathf.PI * from * Mathf.Pow(to / from, Mathf.Min(1f, t / fall)) / Rate;
            data[start + i] += Mathf.Sin(turn) * Mathf.Min(1f, t / .004f) * Mathf.Exp(-t / tau) * level;
        }
    }

    // A soft chord starting at <at> (s): triangle notes (their overtones fall away fast, so they stay warm) swelling in
    // over <attack> and gone by about 4 × <decay> after it.
    private static void Pad(float[] data, float at, float[] notes, float attack, float decay, float level)
    {
        int start = (int)(at * Rate), count = (int)((attack + decay * 4f) * Rate);
        float tau = decay * 4f / 9.21f;
        foreach (float pitch in notes)
            for (int i = 0; i < count && start + i < data.Length; i++)
            {
                float t = i / (float)Rate;
                float wave = 4f * Mathf.Abs(Mathf.Repeat(pitch * t, 1f) - .5f) - 1f;
                float envelope = t < attack ? t / attack : Mathf.Exp(-(t - attack) / tau);
                data[start + i] += wave * envelope * level;
            }
    }

    // A sine gliding up from <from> to <to> (Hz) over <length> (s), swelling in over its first third and dying after:
    // something rising.
    private static void Glide(float[] data, float at, float from, float to, float length, float level)
    {
        int start = (int)(at * Rate), count = (int)(length * 1.4f * Rate);
        float turn = 0f, peak = length * .3f, tau = length * 1.1f / 9.21f;
        for (int i = 0; i < count && start + i < data.Length; i++)
        {
            float t = i / (float)Rate;
            turn += 2f * Mathf.PI * from * Mathf.Pow(to / from, Mathf.Min(1f, t / length)) / Rate;
            float envelope = t < peak ? t / peak : Mathf.Exp(-(t - peak) / tau);
            data[start + i] += Mathf.Sin(turn) * envelope * level;
        }
    }

    // Noise through a low-pass whose corner glides from <from> to <to> (Hz) over <length> (s), dying away; with <swell>
    // above 0 it swells in over that share of its length first: a rush of fire, a soft tap, a dull pop.
    private static void Rush(float[] data, System.Random random, float at, float length, float level, float from, float to, float swell)
    {
        int start = (int)(at * Rate), count = Mathf.Max(1, (int)(length * Rate));
        float low = 0f, lower = 0f;
        for (int i = 0; i < count && start + i < data.Length; i++)
        {
            float k = i / (float)count, corner = from * Mathf.Pow(to / from, k);
            float a = 1f - Mathf.Exp(-2f * Mathf.PI * corner / Rate);
            low += ((float)random.NextDouble() * 2f - 1f - low) * a;
            lower += (low - lower) * a;
            float envelope = swell > 0f ? Mathf.Min(1f, k / swell) * Mathf.Pow(1f - k, 1.5f) : Mathf.Exp(-k * 7f);
            data[start + i] += lower * envelope * level * 2f;
        }
    }

    // A gentle low-pass over a whole clip (two one-pole stages): what lies above <corner> (Hz) fades out.
    private static void LowPass(float[] data, float corner)
    {
        float a = 1f - Mathf.Exp(-2f * Mathf.PI * corner / Rate), low = 0f, lower = 0f;
        for (int i = 0; i < data.Length; i++)
        {
            low += (data[i] - low) * a;
            lower += (low - lower) * a;
            data[i] = lower;
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
