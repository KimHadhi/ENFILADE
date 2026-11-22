using UnityEngine;

namespace HudAddons
{
    /// <summary>
    /// The quick grenade's own sounds (sh_quicknade.lua's sound.Add scripts, played by c_eq_frag.mdl's sound events): the
    /// gear rattles (Viewmodel.Small / Viewmodel.Medium), the pin (QuickNade.PinPull with QuickNade.PinPull.Cloth) and the
    /// throw's whoosh (QuickNade.Throw), all volume 1 and pitch 100, from Art/Audio/Addons/Grenades.
    /// <para>The original timed them by its viewmodel's two sequences. Our hand plays clips of its own, so each sequence is
    /// laid on the moments it shares with our clip: the pull-out on the draw's start (its first rattle) and on the pin
    /// (the pin's sounds as our clip pulls it, the rattles after), the throw on the release (the grenade leaves the hand
    /// 0.3 s into the throw). The spacing of every sound from those moments is the original's; one that falls before its
    /// call (our clip was quicker than the sequence) plays at once. Times run on game time, as the hand's clips do.</para>
    /// </summary>
    internal sealed class QuickNadeSounds
    {
        public const string Rattle = "Grenades/RattleSmall", RattleMedium = "Grenades/RattleMedium", Pin = "Grenades/PinPull",
            PinCloth = "Grenades/PinPullCloth", Whoosh = "Grenades/Throw";

        private struct Pending
        {
            public float At;
            public string Key;
        }

        // A pull-out and a throw are 7 sounds; room for a few overlapping ones.
        private readonly Pending[] queue = new Pending[24];
        private int count;

        /// <summary>The off hand starts taking a grenade out: the rattle the pull-out opens with, as far into it as the
        /// original's.</summary>
        public void PullOut(QuickNadeSoundTimes times) => Add(Time.time + times.pullRattle * PullFrame(times), Rattle);

        /// <summary>The pin comes out now: its two sounds, and the rattles that follow it.</summary>
        public void PinPulled(QuickNadeSoundTimes times)
        {
            float frame = PullFrame(times), now = Time.time;
            Add(now, Pin);
            Add(now, PinCloth);
            Add(now + (times.pullRattle2 - times.pullPin) * frame, Rattle);
            Add(now + (times.pullRattle3 - times.pullPin) * frame, Rattle);
        }

        private static float PullFrame(QuickNadeSoundTimes times) => 1f / Mathf.Max(1e-3f, times.pullFps * times.pullRate);

        /// <summary>The hand starts its throw; the grenade leaves it <paramref name="secondsToRelease"/> from now.</summary>
        public void Throw(QuickNadeSoundTimes times, float secondsToRelease)
        {
            float frame = 1f / Mathf.Max(1e-3f, times.throwFps * times.throwRate);
            float start = Time.time + Mathf.Max(0f, secondsToRelease) - times.releaseAt;
            Add(start + times.throwRattle * frame, RattleMedium);
            Add(start + times.throwWhoosh * frame, Whoosh);
            Add(start + times.throwRattle2 * frame, RattleMedium);
        }

        /// <summary>The hand put it back or let go of the motion: what it had still to play stays silent, as a viewmodel
        /// sequence cut off plays no more of its events.</summary>
        public void Stop() => count = 0;

        /// <summary>Plays what is due this frame.</summary>
        public void Tick()
        {
            if (count == 0) return;
            float now = Time.time;
            for (int i = 0; i < count;)
            {
                if (queue[i].At > now) { i++; continue; }
                Sound.Play(queue[i].Key, 1f, 1f, 1f);
                queue[i] = queue[--count];
            }
        }

        private void Add(float at, string key)
        {
            if (count == queue.Length) return;
            queue[count++] = new Pending { At = at, Key = key };
        }
    }
}
