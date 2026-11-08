/// <summary>
/// The order scripts run in each frame, written in one place. Unity runs every Update in this order, then plays the
/// Animator graphs (the body's clips and KINEMATION FPS Animation Framework), then runs every LateUpdate in this order
/// again. Each script with a set place takes its [DefaultExecutionOrder] from a constant here. Changing who goes before
/// whom is one edit in this file, and the reason for each place is written next to it. Scripts with no set place run at
/// 0, with PlayerLook.
/// Scripts that share a number run in no set order between them. If one must read what another writes in the same
/// frame, give it its own number.
/// </summary>
public static class FrameOrder
{
    // ---- Before anything: taking back last frame's after-effects ----

    /// <summary>The view punch comes off the camera at the start of the frame, so aiming and hitscan use the steady view;
    /// it goes back on after everything else (ViewPunch).</summary>
    public const int ViewPunchRestore = -10000;

    /// <summary>A scene's lone player is spawned before anything else wakes, so the HUD finds it the same way everywhere.</summary>
    public const int SpawnPoint = -1000;

    // ---- Input and combat rules, before the player moves ----

    /// <summary>Weapon input is read first. A parried enemy's lean comes off before gameplay reads its rotation and goes
    /// back on later (enemies, visual only).</summary>
    public const int WeaponInput = -200;
    public const int EnemyParryRecoil = -200;
    /// <summary>Perfect defense and its counter.</summary>
    public const int PerfectDefense = -180;
    /// <summary>The weapon slot in hand this frame, before melee and the weapons use it.</summary>
    public const int WeaponSlots = -175;
    /// <summary>The deathblow (execution).</summary>
    public const int Execution = -160;
    /// <summary>Melee (guard, punches, parry) and the Q/E slip, in no set order between them.</summary>
    public const int MeleeCombat = -150;
    public const int MicroSlip = -150;
    /// <summary>An enemy throwing off a player standing on its head, before the movement runs.</summary>
    public const int HeadStandShakeOff = -140;
    /// <summary>Combat's head moves (a slip's dip, the elbow's glance, look turns), before the look and camera take them.</summary>
    public const int CombatHeadMotion = -120;
    /// <summary>Another PC's pose is played back on its copy before that copy's movement runs.</summary>
    public const int NetworkPose = -110;

    // ---- The player moves and looks ----

    /// <summary>The player's movement and its state, which the body, the camera and the weapons all read.</summary>
    public const int Movement = -100;
    /// <summary>The peeking hitboxes. PlayerBody moves them to the eyes in its LateUpdate (BodyLean.Carry).</summary>
    public const int LeanHitboxes = -50;
    /// <summary>The mouse turns the look.</summary>
    public const int Look = 0;
    /// <summary>The camera's movement and combat feel (punches, tilts, field of view) on the look.</summary>
    public const int CameraEffects = 200;

    // ---- The body: clips in Update, then (after the Animator) the pose, gun, hands and cloth in LateUpdate ----

    /// <summary>
    /// Update: chooses the clips and the look bend's inputs. LateUpdate: the pose work after the animation pass, in the
    /// order PlayerBody.LateUpdate lists it (the look bend, the body placed, the legs, then the eyes and the camera).
    /// </summary>
    public const int Body = 250;
    /// <summary>Footsteps from where the posed feet landed, and the debug test interaction, in no set order between them.</summary>
    public const int Footsteps = 260;
    public const int TestInteraction = 260;
    /// <summary>
    /// Update: FPS Animation Framework's inputs from the movement and the body (FPSBody). LateUpdate: the gun kept in view
    /// where FPSAF lets it go, after the camera is placed and before the weapon fires from its muzzle.
    /// </summary>
    public const int GunHold = 295;
    /// <summary>The weapon's rules (ammo, fire, aim blend, cover) and the gun's pose for this frame (FPSBody.Pose).</summary>
    public const int Weapons = 300;
    /// <summary>What a shot shows (feedback, flame, light), once the weapon has fired this frame.</summary>
    public const int ShotEffects = 310;
    /// <summary>The body's hands onto the gun, fists, ledges, rungs and bars, after the gun has been posed.</summary>
    public const int Hands = 320;
    /// <summary>FPS Animation Framework's own late work: its camera shake and gun camera motion go on the camera last, then
    /// the gun follows a hand the body took off FPSAF's pose (FPSBody.AfterHands).</summary>
    public const int GunFramework = 330;
    /// <summary>Cloth from the finished pose, before Magica Cloth simulates.</summary>
    public const int Cloth = 340;

    // ---- After the player's frame ----

    /// <summary>The ammo HUD, after the weapon's frame.</summary>
    public const int AmmoHud = 400;
    /// <summary>The kill score popup (CombatFeedHUD).</summary>
    public const int CombatFeedHud = 470;
    /// <summary>A dead body's ragdoll, after the living body's pose work.</summary>
    public const int Ragdoll = 1000;
    /// <summary>The view punch goes on the finished camera; the frame's hits are confirmed once, at its end.</summary>
    public const int ViewPunch = 10000;
    public const int HitConfirm = 10000;
    /// <summary>Hit markers and damage numbers, after the view punch.</summary>
    public const int HitMarkers = 10010;
    public const int DamagePopups = 10010;
    /// <summary>Incoming damage's screen effects and the HUD camera, from the final camera.</summary>
    public const int DamageFeedback = 10020;
    public const int HudCamera = 10020;
    /// <summary>The HUD's side planes after the HUD camera, and the score popup's anchor on the gun last.</summary>
    public const int HudPlanes = 10030;
    public const int GunScoreAnchor = 10040;
}
