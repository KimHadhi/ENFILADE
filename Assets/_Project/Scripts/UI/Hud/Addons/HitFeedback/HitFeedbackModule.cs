using UnityEngine;

namespace HudAddons
{
    /// <summary>
    /// The HitFeedback module: what a hit and a kill show: the Black Ops VI Hitmarker addon (its marker, its sounds and
    /// its score popup) and the Cool Killchains addon, refactored as one topic (the user: "히트 마커라는 큰 주제 하나로 묶는
    /// 게 편할 거 아니야?"), drawn on the shared layer (Overlay, Painter, HudFont, Pictures, Sound, Smooth).
    /// <para>One kill feeds all three: the red marker and its sounds, the popup's rows and the chain's skull. The two
    /// addons each counted "kills in a row" on a clock of their own (the hit marker's server reset kill_count 4 s after a
    /// kill; the skulls lasted 4 s + 0.33 s per skull); here it is one count on the kill streak's clock
    /// (CombatFeedHUD.StreakWindow, unscaled), the user's rule for the chain ("킬 체인은 연속 처치 시간이랑 동기화 시켜야지"),
    /// so the 3rd, 4th and 5th kill rows and the skulls end with the streak.</para>
    /// <para>The popup's points are the player's score and experience (the user's pick: "점수 체계도 저 애드온대로"):
    /// <see cref="TotalScore"/> sums every row since <see cref="ResetScore"/>, counted whether or not anything is drawn
    /// (the options only change what shows).</para>
    /// Boot.Install calls Install when the local player's HUD comes up, again for every new local player, so it must be
    /// safe to call more than once.
    /// </summary>
    public static class HitFeedbackModule
    {
        /// <summary>The module's numbers: Tables/Hud/HitFeedback.csv.</summary>
        public const string TableName = HitFeedbackHud.TableName;
        private const string PointsRow = "Points";

        private static HitFeedbackHud hud;
        // The kills in a row now (the addon's kill_count) and when the last came (unscaled seconds).
        private static int run;
        private static float lastKill = float.NegativeInfinity;

        /// <summary>The score this run: every point the popup has added (the player's experience), apart from money.</summary>
        public static long TotalScore { get; private set; }

        /// <summary>What every kill is worth in the popup (the addon's 100, whatever the kind): for words that tell it.</summary>
        public static int KillPoints => Points("kill", 100f);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            hud = null;
            ResetScore();
        }

        public static void Install()
        {
            if (!Application.isPlaying || hud != null) return;
            hud = HitFeedbackHud.Create();
        }

        /// <summary>A new run starts from nothing: the score and the kills in a row.</summary>
        public static void ResetScore()
        {
            TotalScore = 0;
            run = 0;
            lastKill = float.NegativeInfinity;
        }

        /// <summary>
        /// A hit of the local player's that did harm and killed nothing (bo6_hit). headshot: it struck the head; organism:
        /// it struck flesh (false for a target that sounds its own hits, IOwnHitFeedback: the prop badge and the item
        /// sound); burn: it was fire (KillMarks.Fire: the flame badge). This frame's hits show once, in the HUD's LateUpdate.
        /// </summary>
        public static void Hit(bool headshot, bool organism, bool burn)
        {
            if (Application.isPlaying && hud != null) hud.QueueHit(headshot, organism, burn);
        }

        /// <summary>
        /// A kill of the local player's (bo6_kill, and Cool Killchains' kill). marks: what it happened under
        /// (KillConditions), which picks its row and its skull; headshot: it struck the head; organism: as for Hit;
        /// counted: it counts for the run (false for a practice target's: the marker and its sounds, no score, no skull,
        /// as the game counts it in neither the streak nor the score).
        /// </summary>
        public static void Kill(KillMarks marks, bool headshot, bool organism = true, bool counted = true)
        {
            if (!Application.isPlaying) return;
            var kind = KindOf(marks);
            bool shown = hud != null;
            if (shown) hud.QueueKill(headshot, organism, kind == HitFeedbackKill.Burn);
            if (!counted) return;

            float now = Smooth.Now, window = CombatFeedHUD.StreakWindow;
            run = now - lastKill <= window ? run + 1 : 1;
            lastKill = now;
            var o = GameSettings.Current.addonHitFeedback;
            if (shown && o.skulls) hud.AddSkull(kind, headshot, now + window);

            // The kill's row: a blast, fire, a headshot, a blow, else a plain elimination (the addon's order); then the
            // jump, then the chain's.
            bool rows = shown && o.enable;
            int kill = Points("kill", 100f);
            if (kind == HitFeedbackKill.Blast) Add(o.nameExplosive, "hitfeedback.name.explosive", kill, rows);
            else if (kind == HitFeedbackKill.Burn) Add(o.nameBurn, "hitfeedback.name.burn", kill, rows);
            else if (headshot) Add(o.nameHeadshot, "hitfeedback.name.headshot", kill, rows);
            else if (kind == HitFeedbackKill.Kick || kind == HitFeedbackKill.Melee) Add(o.nameMelee, "hitfeedback.name.melee", kill, rows);
            else Add(o.nameEliminate, "hitfeedback.name.eliminate", kill, rows);
            var me = PlayerRoster.Local;
            if (me != null && !me.IsGrounded) Add(o.nameJump, "hitfeedback.name.jump", Points("jump", 25f), rows);
            if (run == Points("tripleAt", 3f)) Add(o.nameTriple, "hitfeedback.name.triple", Points("triple", 25f), rows);
            else if (run == Points("quadraAt", 4f)) Add(o.nameQuadra, "hitfeedback.name.quadra", Points("quadra", 25f), rows);
            else if (run >= Points("pentaAt", 5f)) Add(o.namePenta, "hitfeedback.name.penta", Points("penta", 50f), rows);
        }

        /// <summary>
        /// Cool Killchains' kill type, first that fits, for the skull and the popup's row. Fire comes first: it is the
        /// damage itself, while KillConditions also marks any non-bullet blow without a source as bare hands. Then a shove
        /// (a kick, a sweep, a dash push: the kick picture), a blow or cut held in hand (a punch, the class knife, an
        /// execution, a counter, a takedown: the knife), a blast, a thrown prop. A thrown knife is a projectile, as the
        /// addons' weapon checks treat one (inflictor not a weapon, its damage not a club's): a plain kill.
        /// </summary>
        public static HitFeedbackKill KindOf(KillMarks marks)
        {
            const KillMarks Blows = KillMarks.Melee | KillMarks.Blade | KillMarks.Execution | KillMarks.Counter | KillMarks.Takedown;
            if ((marks & KillMarks.Fire) != 0) return HitFeedbackKill.Burn;
            if ((marks & KillMarks.Shove) != 0) return HitFeedbackKill.Kick;
            if ((marks & Blows) != 0) return HitFeedbackKill.Melee;
            if ((marks & KillMarks.Explosion) != 0) return HitFeedbackKill.Blast;
            if ((marks & KillMarks.Prop) != 0) return HitFeedbackKill.Prop;
            return HitFeedbackKill.Plain;
        }

        // AddScore(name, exp) and total = total + exp: the score always, the popup's row when it shows. The name is the
        // one the player typed, else the addon's own (Texts.csv, in English for every language: the popup's rule).
        private static void Add(string typed, string textId, int points, bool row)
        {
            TotalScore += points;
            if (row) hud.AddRow(string.IsNullOrEmpty(typed) ? Texts.InEnglish(textId) : typed, points);
        }

        private static int Points(string field, float fallback)
            => Mathf.RoundToInt(GameTables.Number(TableName, PointsRow, field, fallback));
    }
}
