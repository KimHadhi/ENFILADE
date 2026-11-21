using UnityEngine;

/// <summary>
/// The class knife in the melee slot, in place of the bare fists (the user, 2026-11-13: "클래스 별로 기본 근접 슬롯에 칼 들게
/// 해 줘"): the knife of the player's class (ClassKnives, the same one its executions stab with) comes out of the hip into the
/// right fist when the melee slot is picked, and the fists' moves are done with the blade (the combos, the guard, Sekiro's
/// parry, the shove, the crouched sweep: PlayerMeleeCombat, MeleeCombos). A light attack slashes, forehand then backhand
/// while they follow quickly; a charged one, a riposte or a beat chain's finisher is the heavy clip (a stab; the light clip
/// again for a knife with no heavy one of its own); a spin or a chase is a backhand slash. The knife deals its own row's
/// numbers (ClassKnives.csv, read by PlayerMeleeCombat), more on the head and in the back (Hit, read by BladeTrace),
/// names itself in the kill feed and the weapon slots (FeedName, FeedIcon, SlotName, SlotIcon) and sounds as a blade
/// (Sounds, from PlayerFistAudio, whose sounds go to the other PCs as before).
/// The arm goes by the swing's own timeline (PlayerMeleeCombat's SwingTimeline; the user, 2026-11-18: "즉시 발동 대신 무기별
/// 선딜레이 부여(선딜과 준비 동작이 반드시 일치할 필요는 없으며, 꾹 누르면 선딜 이후 손이 떨리며 차징됨)"): through the
/// wind-up the knife is drawn from the guard to the coming swing's first frame; held past it, on to the heavy clip's first
/// frame as the charge grows, the fist trembling more the fuller it is (the row's chargeShake); the swing is then its clip
/// at the timeline's own clip time, so what the arm shows, when the blade is live and when the next action may begin are
/// one clock. Before, the arm ran a clock of its own beside a hit test that never asked it.
/// What the swing cuts is this blade where the arm shows it (BladeTrace through PlayerMeleeCombat.BladePosed; the user,
/// 2026-11-18: "히트 판정은 … 실제 무기 모델 자체를 기준으로"). The knife tells how far each of its clips reaches (Reach),
/// and through the blade's marks the arm reaches as much further ahead as the swing was aimed to need (ReachIn).
/// The same on every PC (the user's rule: what I see is what friends see): every PC's copy of a player has a timeline their
/// PC's words step (the wind-up, the swing, the letting go) and plays the same clips; ExecutionKnifeArm poses the right arm
/// with them on each body, after the hands and FPS Animation Framework. The clips are the Blender knife workspace's
/// (KnifeSwing). The fists' own guard, parry and guard break pose the arm themselves: the knife's guard gives way to them
/// and only rides the fist.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.MeleeCombat + 5)]
public sealed class MeleeKnife : MonoBehaviour
{
    public enum Swing : byte { None, Draw, Slash, Backslash, Stab }

    // Light slashes take turns, forehand then backhand, while they come this close together (s); after a pause the next
    // is a forehand again.
    private const float ChainGap = .8f;
    // A swing that starts while another plays blends from where the arm was over this long (s).
    private const float SwingBlend = .06f;
    // The knife's guard gives the arm to the fists' block, parry and guard break over this long (real s), and takes it back.
    private const float BlockBlend = .08f;
    // The sounds play at the hand: this high on the body.
    private const float SoundHeight = 1.4f;
    // The charging fist's tremble: how fast it shakes (changes of way a second, about) and how far the hand turns with it
    // at full charge (degrees). How far it moves is the knife's own (ClassKnives.csv chargeShake).
    private const float TrembleRate = 13f, TrembleTurn = 2.5f;

    private PlayerMovement movement;
    private PlayerMeleeCombat melee;
    private PlayerBodyHands hands;
    private ExecutionKnifeArm arm;
    private ClassKnives.Knife knife;
    private string knifeClass;
    private Swing swing;
    private KnifeSwing clip;
    private KnifeSwing.Key from, last;
    // The clip's finger joints as sampled this frame, as last shown (after any blend) and where a blend starts from.
    private readonly FingerWays sampledFingers = new FingerWays(), lastFingers = new FingerWays(), fromFingers = new FingerWays(),
        cockedFingers = new FingerWays();
    // clock: the draw's own (it is no swing of the timeline's). sinceSwing: since the last attack left, for the chain.
    private float clock, sinceSwing = 99f, blendLeft, guardClock, block;
    // How much of the arm the knife's own move has (its draw, a wind-up, a swing): taken and handed back over the hands'
    // own time between holds (Player.csv Hands.blendTime, read once), as the hands change any hold.
    private float acting, handsBlend = .15f;
    private bool hasLast, wasOut, backNext;

    /// <summary>The player's melee knife, made on them if it is not there yet (ExecutionKnife asks).</summary>
    public static MeleeKnife On(GameObject player)
    {
        if (player == null) return null;
        return player.TryGetComponent(out MeleeKnife knife) ? knife : player.AddComponent<MeleeKnife>();
    }

    /// <summary>The player has their class knife out in the melee slot (the fists' slot), on this PC's copy of them.</summary>
    public static bool Out(PlayerMovement who) => Active(who) != null;

    /// <summary>The knife of this player's class, out or not: what their swings deal and how they swing (PlayerMeleeCombat).</summary>
    public static ClassKnives.Knife Held(PlayerMovement who) => ClassKnives.Of(ClassKnives.ClassOf(who));

    private bool IsOut => melee != null && melee.IsActive;

    // ---- What the knife deals (BladeTrace) ----

    /// <summary>
    /// A strike landing on <paramref name="collider"/>: on the head (the head hitbox, else a plain body's top 22%, as the
    /// guns' WeaponHitRegion judges) and in the back (struck the way the body faces) the knife deals its row's factors more.
    /// The fists' strikes come back as they went.
    /// </summary>
    public static float Hit(PlayerMovement who, Collider collider, Vector3 point, Vector3 forward, float dealt, out bool head)
    {
        head = false;
        var held = Active(who);
        if (held == null || collider == null) return dealt;
        head = IsHead(collider, point);
        bool back = FromBehind(collider, forward);
        return dealt * (head ? held.HeadFactor : 1f) * (back ? held.BackFactor : 1f);
    }

    /// <summary>How a strike of this player's is dealt: a blade while the knife is out (the feed names it, it bleeds as a shot).</summary>
    public static DamageMethod MethodOf(PlayerMovement who) => Active(who) != null ? DamageMethod.Blade : DamageMethod.Other;

    // ---- Its name and picture (GunFeelDirector's kill feed, WeaponSlotsHUD) ----

    /// <summary>The kill feed's picture of a blade's kill: the knife of the class of whoever struck.</summary>
    public static string FeedIcon(in DamageHit hit)
    {
        var held = KnifeOf(hit.Source);
        return held != null ? held.Icon : WeaponIcons.Fist;
    }

    /// <summary>A blade's blow that broke more posture than halfway from the knife's slash to its stab: a stab's (KnifeHitFx).</summary>
    public static bool Heavy(in DamageHit hit)
    {
        var held = KnifeOf(hit.Source);
        return held != null && hit.PostureDamage >= (held.SlashPosture + held.StabPosture) * .5f;
    }

    /// <summary>The kill feed's words for a blade's kill: that knife's name.</summary>
    public static string FeedName(in DamageHit hit) => Texts.Cell((KnifeOf(hit.Source) ?? ClassKnives.Of(null)).Name);

    /// <summary>The melee slot's picture for this player: their class knife's.</summary>
    public static Sprite SlotIcon(PlayerMovement who) => who != null ? WeaponIcons.For(ClassKnives.Of(ClassKnives.ClassOf(who)).Icon) : null;

    /// <summary>The melee slot's name for this player: their class knife's.</summary>
    public static string SlotName(PlayerMovement who) => who != null ? Texts.Cell(ClassKnives.Of(ClassKnives.ClassOf(who)).Name) : "";

    // ---- Its sounds (PlayerFistAudio) ----

    /// <summary>
    /// A fist sound on this player's body while the knife is out: the knife's own cue plays instead and this says so. The
    /// fist sound still goes to the other PCs (PlayerFistAudio.Played), where their copy plays the knife's the same way.
    /// </summary>
    internal static bool Sounds(PlayerFistAudio audio, FistSound sound)
    {
        if (audio == null || !audio.TryGetComponent(out MeleeKnife self) || !self.IsOut) return false;
        var held = self.KnifeNow();
        string cue = sound == FistSound.Swing ? held.SwingSound : sound == FistSound.Hit ? held.HitSound
            : sound == FistSound.HeavyHit ? held.HeavyHitSound : sound == FistSound.WallHit ? held.WallSound : null;
        if (string.IsNullOrEmpty(cue) || !SoundCues.TryFind(cue, out _)) return false;
        self.Sound(cue);
        return true;
    }

    // ---- The swing that leaves (PlayerMeleeCombat's timeline, on every PC) ----

    /// <summary>
    /// A swing leaves now, charged this far, as this combo move (None: the button's own): which clip it is. A riposte, a
    /// finisher or a charge from <paramref name="heavyFrom"/> is the heavy clip; a spin or a chase the backhand; else the
    /// light ones take turns. The arm plays it from here at the timeline's clip time. Null when the knife has no such clip.
    /// </summary>
    internal KnifeSwing Take(float charge, ComboStrike strike, float heavyFrom)
    {
        Swing next;
        if (strike == ComboStrike.Riposte || strike == ComboStrike.Finisher || charge >= heavyFrom) next = Swing.Stab;
        else if (strike == ComboStrike.Spin || strike == ComboStrike.Chase) next = Swing.Backslash;
        else next = NextLight();
        var made = Begin(next);
        if (made != null) backNext = next == Swing.Slash;
        return made;
    }

    // The light swing a press would be now: the backhand right after a forehand, the forehand otherwise.
    private Swing NextLight() => backNext && sinceSwing < ChainGap ? Swing.Backslash : Swing.Slash;

    /// <summary>The clip of the light swing a press would throw now: a wind-up is aimed by it (PlayerMeleeCombat).</summary>
    internal KnifeSwing Coming => KnifeSwing.Of(ClipOf(KnifeNow(), NextLight()));

    // ---- Where the blade goes (KnifeSeat; PlayerMeleeCombat's step in and its cut) ----

    // Each clip's reach on this body with this knife, worked out once (again when the knife or the tables change).
    private readonly System.Collections.Generic.Dictionary<KnifeSwing, float> reaches = new System.Collections.Generic.Dictionary<KnifeSwing, float>();
    private string reachesOf;

    /// <summary>
    /// How far ahead of the eyes this clip's blade gets while it is live, held by this body's hand (m; KnifeSeat.Reach):
    /// what a swing can cut from where the player stands, so how near they must step.
    /// </summary>
    internal float Reach(KnifeSwing swung)
    {
        if (swung == null) return 0f;
        var held = KnifeNow();
        string key = held.Id + "/" + held.Model;
        if (reachesOf != key || reaches.Count > 32) { reaches.Clear(); reachesOf = key; }
        if (reaches.TryGetValue(swung, out float known)) return known;
        // The arm measures the hand and the model; until it has, a hand's usual length and the knife's plain shape.
        bool worn = arm != null && arm.Wears(held);
        float reach = KnifeSeat.Reach(swung, held, arm != null ? arm.HandLength : KnifeSeat.NominalHand, worn ? arm.Shape : KnifeSeat.Shape.Plain);
        if (worn) reaches[swung] = reach;
        return reach;
    }

    /// <summary>
    /// The blade where the swing's clip alone puts it now, from the eyes: what the swing cuts with on a body the arm
    /// cannot pose (no humanoid to hold a knife). False when no swing's clip is playing.
    /// </summary>
    internal bool BladeByClip(Transform eyes, out Vector3 start, out Vector3 tip)
    {
        start = tip = default;
        if (eyes == null || melee == null || clip == null || swing == Swing.None || swing == Swing.Draw
            || !clip.Sample(melee.SwingClipTime, out var key)) return false;
        bool worn = arm != null && arm.Wears(KnifeNow());
        if (!KnifeSeat.Blade(key, KnifeNow(), arm != null ? arm.HandLength : KnifeSeat.NominalHand, worn ? arm.Shape : KnifeSeat.Shape.Plain,
                out Vector3 from, out Vector3 to)) return false;
        start = eyes.position + eyes.rotation * from;
        tip = eyes.position + eyes.rotation * to;
        return true;
    }

    // ---- The arm (ExecutionKnifeArm, each frame) ----

    /// <summary>
    /// The right arm as the knife has it now: the swing's clip at the timeline's clip time (eased from the last pose),
    /// else the guard, drawn back to the coming swing's first frame through a wind-up and on to the heavy one's as a
    /// charge grows, trembling; and how much of the arm it takes. The knife's own moves (the draw, a wind-up, a swing) have
    /// all of it whatever the legs do; only its guard at rest lets a run forward swing the arm, as the fists' guard does
    /// (the user, 2026-11-21: "칼 드는 모션이 찰져서 … 움직이면서 칼 들면 칼 드는 팔 모션이 바로 걷기나 달리기에 뺏겨서 못
    /// 보잖아": the draw went by the fists' rule, which hands the arm to the run, so a draw on the move never showed). The
    /// fists' block and parry take the arm back from all of it. False while the knife is not out.
    /// </summary>
    internal bool Current(out KnifeSwing.Key key, out float weight, out ClassKnives.Knife inHand, FingerWays fingers = null)
    {
        key = default;
        weight = 0f;
        inHand = null;
        if (!IsOut) return false;
        inHand = KnifeNow();
        bool found = false;
        if (swing != Swing.None && clip != null)
        {
            float at = swing == Swing.Draw ? clock : melee.SwingClipTime;
            if (found = clip.Sample(at, out key)) clip.SampleFingers(at, sampledFingers);
            if (found && swing != Swing.Draw) ReachIn(ref key, at);
        }
        if (!found)
        {
            var guard = KnifeSwing.Of(inHand.GuardClip);
            float at = guard != null && guard.Length > 0f ? Mathf.Repeat(guardClock, guard.Length) : 0f;
            if (guard == null || !guard.Sample(at, out key)) return false;
            guard.SampleFingers(at, sampledFingers);
            float wind = melee.WindShare;
            if (wind > 0f)
            {
                // The wind-up draws the knife back to where the coming light swing starts.
                Cock(ref key, KnifeSwing.Of(ClipOf(inHand, NextLight())), Ease.Out(wind));
                // Held past it, on to where the heavy one starts, the fist trembling as the charge fills.
                float charge = melee.ChargeRatio;
                if (charge > 0f)
                {
                    Cock(ref key, KnifeSwing.Of(inHand.StabClip), charge);
                    Tremble(ref key, inHand.ChargeShake, charge);
                }
            }
        }
        if (blendLeft > 0f && hasLast)
        {
            float share = 1f - blendLeft / SwingBlend;
            key = KnifeSwing.Blend(from, key, share);
            FingerWays.Blend(fromFingers, sampledFingers, share, sampledFingers);
        }
        last = key;
        lastFingers.CopyFrom(sampledFingers);
        fingers?.CopyFrom(sampledFingers);
        hasLast = true;
        float up = hands != null ? hands.FistsWeight : 1f;
        weight = Mathf.Clamp01(Mathf.Max(up, acting) * (1f - block));
        return true;
    }

    // The arm reaches in to the body the swing is aimed at (PlayerMeleeCombat.AimSwing): the wrist goes that much further
    // ahead than the clip has it, eased in up to the blade's first mark, whole through the marks, and eased out by the
    // mark that frees the next action. The elbow follows by half, so the arm straightens rather than slides.
    private void ReachIn(ref KnifeSwing.Key key, float at)
    {
        float further = melee.SwingReachIn;
        if (further <= 0f) return;
        var marks = melee.SwingMarks;
        float home = marks.Next > marks.To ? marks.Next : marks.Length;
        float share = at < marks.From ? Mathf.SmoothStep(0f, 1f, marks.From > 1e-4f ? at / marks.From : 1f)
            : at <= marks.To ? 1f : 1f - Mathf.SmoothStep(0f, 1f, home > marks.To ? Mathf.InverseLerp(marks.To, home, at) : 1f);
        key.Wrist += Vector3.forward * (further * share);
        key.Elbow += Vector3.forward * (further * share * .5f);
    }

    // The pose drawn toward a clip's first frame by this share (the fingers with it).
    private void Cock(ref KnifeSwing.Key key, KnifeSwing toward, float share)
    {
        if (toward == null || share <= 0f || !toward.Sample(0f, out var cocked)) return;
        key = KnifeSwing.Blend(key, cocked, share);
        if (toward.SampleFingers(0f, cockedFingers)) FingerWays.Blend(sampledFingers, cockedFingers, share, sampledFingers);
    }

    // The charging fist trembles: the wrist wanders a little and the hand turns with it, more the fuller the charge. Smooth
    // noise, so it shivers and never jumps; the same on every PC only in kind, which is all a tremble has.
    private static void Tremble(ref KnifeSwing.Key key, float centimetres, float charge)
    {
        if (centimetres <= 0f || charge <= 0f) return;
        float t = Time.time * TrembleRate;
        var noise = new Vector3(Mathf.PerlinNoise(t, .31f) - .5f, Mathf.PerlinNoise(.73f, t) - .5f, Mathf.PerlinNoise(t + 11.3f, 5.17f) - .5f) * 2f;
        Vector3 shift = noise * (centimetres * .01f * charge);
        key.Wrist += shift;
        key.Elbow += shift * .4f;
        var turn = Quaternion.Euler(noise.y * TrembleTurn * charge, noise.z * TrembleTurn * charge, noise.x * TrembleTurn * charge);
        key.Along = turn * key.Along;
        key.Palm = turn * key.Palm;
    }

    private void OnEnable()
    {
        movement = GetComponent<PlayerMovement>();
        hands = GetComponent<PlayerBodyHands>();
        handsBlend = Mathf.Max(.01f, GameTables.Number(PlayerMovement.TableName, "Hands", "blendTime", .15f));
    }

    private void OnDisable()
    {
        swing = Swing.None;
        wasOut = hasLast = false;
    }

    private void Update()
    {
        if (movement == null) return;
        if (melee == null) melee = movement.MeleeCombat != null ? movement.MeleeCombat : GetComponentInChildren<PlayerMeleeCombat>();
        if (hands == null) hands = GetComponent<PlayerBodyHands>();
        if (arm == null && Time.frameCount % 30 == 0) arm = ExecutionKnifeArm.Of(movement);
        bool outNow = IsOut;
        if (outNow && !wasOut)
        {
            if (arm != null) arm.Wear(KnifeNow());
            Begin(Swing.Draw);
            Sound(KnifeNow().DrawSound);
        }
        else if (!outNow && wasOut)
        {
            swing = Swing.None;
            hasLast = false;
            // The next draw takes the arm from wherever it then is, eased, not at once from what this one left.
            acting = 0f;
            Sound(KnifeNow().SheathSound);
        }
        wasOut = outNow;
        if (!outNow) return;
        float dt = movement.DeltaTime;
        clock += dt;
        sinceSwing += dt;
        guardClock += dt;
        if (blendLeft > 0f) blendLeft -= dt;
        // The draw ends with its clip; an attack with the timeline's swing (its clip run out, a new wind-up begun over
        // its way home, or the swing let go of): back into the guard, or the wind-up, from where it left the arm.
        bool over = swing == Swing.Draw ? clip == null || clock >= clip.Length : swing != Swing.None && !melee.IsSwinging;
        if (over)
        {
            if (hasLast) { from = last; fromFingers.CopyFrom(lastFingers); blendLeft = SwingBlend; }
            swing = Swing.None;
            clip = null;
        }
        bool blocking = melee.IsGuarding || melee.IsParryActive || melee.IsGuardBreaking;
        block = Mathf.MoveTowards(block, blocking ? 1f : 0f, Time.unscaledDeltaTime / BlockBlend);
        // The draw, a wind-up and a swing are the knife's own moves: the arm is theirs while they last, and goes back to
        // whatever the fists' rule gives it (a run's swing, the guard) over the hands' time once they end.
        bool moving = swing != Swing.None || melee.WindShare > 0f || melee.IsSwinging;
        acting = Mathf.MoveTowards(acting, moving ? 1f : 0f, dt / handsBlend);
    }

    // The arm takes up this clip from where it is; null (and nothing changed) when the knife has no table for it.
    private KnifeSwing Begin(Swing next)
    {
        var made = KnifeSwing.Of(ClipOf(KnifeNow(), next));
        if (made == null) return null;
        if (hasLast) { from = last; fromFingers.CopyFrom(lastFingers); blendLeft = SwingBlend; }
        else blendLeft = 0f;
        swing = next;
        clip = made;
        clock = 0f;
        if (next != Swing.Draw) sinceSwing = 0f;
        return made;
    }

    private static string ClipOf(ClassKnives.Knife held, Swing which)
        => which == Swing.Draw ? held.DrawClip : which == Swing.Slash ? held.SlashClip
            : which == Swing.Backslash ? held.BackslashClip : which == Swing.Stab ? held.StabClip : null;

    private ClassKnives.Knife KnifeNow()
    {
        string id = ClassKnives.ClassOf(movement);
        if (knife == null || id != knifeClass)
        {
            knife = ClassKnives.Of(id);
            knifeClass = id;
            if (arm != null && IsOut) arm.Wear(knife);
        }
        return knife;
    }

    private void Sound(string cue)
    {
        if (string.IsNullOrEmpty(cue) || !Application.isPlaying) return;
        BodyBreakSounds.Play(cue, transform.position + Vector3.up * SoundHeight);
    }

    private static ClassKnives.Knife Active(PlayerMovement who)
    {
        if (who == null || !who.TryGetComponent(out MeleeKnife self)) return null;
        return self.IsOut ? self.KnifeNow() : null;
    }

    private static ClassKnives.Knife KnifeOf(GameObject source)
    {
        var who = source != null ? source.GetComponentInParent<PlayerMovement>() : null;
        return who != null ? ClassKnives.Of(ClassKnives.ClassOf(who)) : null;
    }

    private static bool IsHead(Collider collider, Vector3 point)
    {
        var region = collider.GetComponentInParent<WeaponHitRegion>();
        if (region != null) return region.Kind == WeaponHitRegion.Region.Head;
        if (collider.GetComponentInParent<EnemyHealth>() == null) return false;
        if (!(collider is CapsuleCollider) && !(collider is CharacterController)) return false;
        var bounds = collider.bounds;
        return point.y >= bounds.min.y + bounds.size.y * .78f;
    }

    // Struck the way the body faces (within 60 degrees): from behind.
    private static bool FromBehind(Collider collider, Vector3 forward)
    {
        var body = collider.GetComponentInParent<MeleeCombatTarget>();
        Transform root = body != null ? body.transform : collider.transform.root;
        Vector3 facing = Vector3.ProjectOnPlane(root.forward, Vector3.up), along = Vector3.ProjectOnPlane(forward, Vector3.up);
        if (facing.sqrMagnitude < 1e-4f || along.sqrMagnitude < 1e-4f) return false;
        return Vector3.Dot(facing.normalized, along.normalized) > .5f;
    }
}
