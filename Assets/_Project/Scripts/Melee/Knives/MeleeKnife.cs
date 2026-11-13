using UnityEngine;

/// <summary>
/// The class knife in the melee slot, in place of the bare fists (the user, 2026-11-13: "클래스 별로 기본 근접 슬롯에 칼 들게
/// 해 줘"): the knife of the player's class (ClassKnives, the same one its executions stab with) comes out of the hip into the
/// right fist when the melee slot is picked, and every fist move stays what it was (light and charged attacks, the combos,
/// the guard, Sekiro's parry, the shove, the crouched sweep: PlayerMeleeCombat, MeleeCombos), only done with the blade. A
/// light attack slashes, forehand then backhand while they follow quickly; a charged one, a riposte or a beat chain's
/// finisher stabs; a spin or a chase is a backhand slash. The knife deals its own row's numbers in place of the fists'
/// (Damage, Posture, ExtraReach, read by PlayerMeleeCombat), more on the head and in the back (Hit, read by
/// MeleeAttackSweep), names itself in the kill feed and the weapon slots (FeedName, FeedIcon, SlotName, SlotIcon) and
/// sounds as a blade (Sounds, from PlayerFistAudio, whose sounds go to the other PCs as before).
/// The same on every PC (the user's rule: what I see is what friends see): every PC's copy of a player follows the slot,
/// the punches and the guard their PC sends (FistsView.Thrown, PlayerMeleeCombat's shown guard) and plays the same clips;
/// ExecutionKnifeArm poses the right arm with them on each body, after the hands and FPS Animation Framework. The clips are
/// the Blender knife workspace's (KnifeSwing). The fists' own guard, parry and guard break pose the arm themselves: the
/// knife's guard gives way to them and only rides the fist.
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
    // A punch charged this far (0-1) is a heavy one: the knife stabs (PlayerMeleeCombat's heavyPunchCharge, Player.csv).
    private const float DefaultHeavyCharge = .6f;

    private PlayerMovement movement;
    private PlayerMeleeCombat melee;
    private FistsView fists;
    private PlayerBodyHands hands;
    private ExecutionKnifeArm arm;
    private ClassKnives.Knife knife;
    private string knifeClass;
    private Swing swing;
    private KnifeSwing clip;
    private KnifeSwing.Key from, last;
    private float clock, sinceSwing = 99f, blendLeft, guardClock, block;
    private bool hasLast, wasOut, backNext;
    private int nextLook;

    /// <summary>The player's melee knife, made on them if it is not there yet (ExecutionKnife asks).</summary>
    public static MeleeKnife On(GameObject player)
    {
        if (player == null) return null;
        return player.TryGetComponent(out MeleeKnife knife) ? knife : player.AddComponent<MeleeKnife>();
    }

    /// <summary>The player has their class knife out in the melee slot (the fists' slot), on this PC's copy of them.</summary>
    public static bool Out(PlayerMovement who) => Active(who) != null;

    private bool IsOut => melee != null && melee.IsActive;

    // ---- What the knife deals, in place of the fists (PlayerMeleeCombat, MeleeAttackSweep) ----

    /// <summary>What a strike of this charge deals with the knife out: its slash to its stab; else the fists' own.</summary>
    public static float Damage(PlayerMovement who, float charge, float fists)
    {
        var held = Active(who);
        return held != null ? Mathf.Lerp(held.Slash, held.Stab, Mathf.Clamp01(charge)) : fists;
    }

    /// <summary>The posture it breaks, as Damage.</summary>
    public static float Posture(PlayerMovement who, float charge, float fists)
    {
        var held = Active(who);
        return held != null ? Mathf.Lerp(held.SlashPosture, held.StabPosture, Mathf.Clamp01(charge)) : fists;
    }

    /// <summary>How much further than a fist the knife in hand reaches (m; 0 with the fists).</summary>
    public static float ExtraReach(PlayerMovement who) => Active(who)?.Reach ?? 0f;

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

    // ---- The arm (ExecutionKnifeArm, each frame) ----

    /// <summary>
    /// The right arm as the knife has it now: the swing playing (eased from the last one), else the guard; and how much of
    /// the arm it takes (the fists' block and parry take it back, and running forward lets the run clip swing it, as the
    /// fists do). False while the knife is not out.
    /// </summary>
    internal bool Current(out KnifeSwing.Key key, out float weight, out ClassKnives.Knife inHand)
    {
        key = default;
        weight = 0f;
        inHand = null;
        if (!IsOut) return false;
        inHand = KnifeNow();
        bool found = false;
        if (swing != Swing.None && clip != null) found = clip.Sample(clock, out key);
        if (!found)
        {
            var guard = KnifeSwing.Of(inHand.GuardClip);
            if (guard == null || !guard.Sample(guard.Length > 0f ? Mathf.Repeat(guardClock, guard.Length) : 0f, out key)) return false;
            // Charging a strike draws the knife back to where the stab starts, as the fists draw back.
            var stab = melee.IsCharging ? KnifeSwing.Of(inHand.StabClip) : null;
            if (stab != null && stab.Sample(0f, out var cocked)) key = KnifeSwing.Blend(key, cocked, melee.ChargeRatio);
        }
        if (blendLeft > 0f && hasLast) key = KnifeSwing.Blend(from, key, 1f - blendLeft / SwingBlend);
        last = key;
        hasLast = true;
        float up = hands != null ? hands.FistsWeight : 1f;
        weight = Mathf.Clamp01(up * (1f - block));
        return true;
    }

    private void OnEnable()
    {
        movement = GetComponent<PlayerMovement>();
        hands = GetComponent<PlayerBodyHands>();
    }

    private void OnDisable()
    {
        if (fists != null) fists.Thrown -= OnThrown;
        fists = null;
        swing = Swing.None;
        wasOut = hasLast = false;
    }

    private void Update()
    {
        if (movement == null) return;
        if (melee == null) melee = movement.MeleeCombat != null ? movement.MeleeCombat : GetComponentInChildren<PlayerMeleeCombat>();
        // The fists' view comes with the melee component; the knife listens to its strikes on every PC.
        if (fists == null && Time.frameCount >= nextLook)
        {
            nextLook = Time.frameCount + 30;
            fists = GetComponentInChildren<FistsView>();
            if (fists != null) { fists.Thrown -= OnThrown; fists.Thrown += OnThrown; }
        }
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
            Sound(KnifeNow().SheathSound);
        }
        wasOut = outNow;
        if (!outNow) return;
        float dt = movement.DeltaTime;
        clock += dt;
        sinceSwing += dt;
        guardClock += dt;
        if (blendLeft > 0f) blendLeft -= dt;
        if (swing != Swing.None && (clip == null || clock >= clip.Length))
        {
            // Back into the guard from where the swing left the arm.
            if (hasLast) { from = last; blendLeft = SwingBlend; }
            swing = Swing.None;
            clip = null;
        }
        bool blocking = melee.IsGuarding || melee.IsParryActive || melee.IsGuardBreaking;
        block = Mathf.MoveTowards(block, blocking ? 1f : 0f, Time.unscaledDeltaTime / BlockBlend);
    }

    // A fist strike left (FistsView, on every PC; the knife's are all the right hand's): which swing it is.
    private void OnThrown(bool right, float charge)
    {
        if (!IsOut || !right) return;
        var strike = melee != null ? melee.LastStrike : ComboStrike.None;
        Swing next;
        if (strike == ComboStrike.Riposte || strike == ComboStrike.Finisher || charge >= HeavyCharge()) next = Swing.Stab;
        else if (strike == ComboStrike.Spin || strike == ComboStrike.Chase) next = Swing.Backslash;
        else next = backNext && sinceSwing < ChainGap ? Swing.Backslash : Swing.Slash;
        backNext = next == Swing.Slash;
        Begin(next);
    }

    private void Begin(Swing next)
    {
        var held = KnifeNow();
        string table = next == Swing.Draw ? held.DrawClip : next == Swing.Slash ? held.SlashClip
            : next == Swing.Backslash ? held.BackslashClip : next == Swing.Stab ? held.StabClip : null;
        var made = KnifeSwing.Of(table);
        if (made == null) return;
        if (hasLast) { from = last; blendLeft = SwingBlend; }
        else blendLeft = 0f;
        swing = next;
        clip = made;
        clock = 0f;
        sinceSwing = 0f;
    }

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

    private static float heavyCharge = -1f;

    private static float HeavyCharge()
    {
        if (heavyCharge < 0f)
        {
            heavyCharge = DefaultHeavyCharge;
            if (GameTables.TryGetRow(PlayerMovement.TableName, "Fists", out var row) && row.TryGetValue("heavyPunchCharge", out string text)
                && float.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float read))
                heavyCharge = read;
        }
        return heavyCharge;
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
