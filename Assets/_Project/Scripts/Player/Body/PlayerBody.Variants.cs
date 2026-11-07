using UnityEngine;
using UnityEngine.Playables;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// Which clip of a family fits the moment: jumps by run-up speed, the mid-air knee tuck, a long fall's flailing, vaults and mantles by
/// obstacle height, landings by how hard they were (MovementLanding's tier), the way a heavy landing goes down, a fast
/// landing roll or a landing on the spot, crouched landings and slide exits, the ledge catch and shimmy, the wall kick's side, and the
/// gun-holding version of a clip while a rifle or pistol is out. Names and thresholds come from Player.csv, row Body.
/// </summary>
public sealed partial class PlayerBody
{
    [Header("Clip variants")]
    [SerializeField] private string jumpStill = "jumpstill", jumpSlow = "jumpslow", airTuck = "jumpcoil";
    [SerializeField] private string landCrouch = "landcrouch", coilLand = "jumpcoilend", coilLandCrouch = "jumpcoilendcrouch";
    [SerializeField, Tooltip("Down after a heavy landing and up again: onto hands and knees, flat on the face (a huge "
        + "fall), onto the back (moving backwards, or thrown by a blow). Each plays over the time the player stays down.")]
    private string heavyLand = "mx_hard_landing", heavyLandFaceDown = "mx_get_up", heavyLandBack = "mx_stand_up_back";
    [SerializeField, Tooltip("A landing too slow to roll on stays where it came down (Ability.rollLandSpeed), or crouched under a low ceiling.")]
    private string rollLand = "land";
    [SerializeField, Tooltip("A long fall (Ability.bigFallHeight): arms and legs flail until touchdown, Ctrl or not.")]
    private string fallFlail = "fallinguncontrolled";
    [SerializeField] private string supineSlideEndCrouch = "meslideendcrouch", supineSlideEndProne = "meslideendprone";
    [SerializeField, Tooltip("Lying on the back as Beatrun's jump turn does: coming down onto it from a dive, and getting up "
        + "from it standing (with a gun out, its gun version) or under a low ceiling crouched. A slide ends onto it with "
        + "supineSlideEndProne, legs up first.")]
    private string supineLand = "jumpturnland", supineGetUp = "jumpturnlandstand", supineGetUpGun = "jumpturnlandstandgun",
        supineGetUpCrouch = "jumpturnlandcrouch";
    [SerializeField] private string bellySlideEndCrouch = "diveslideendcrouch";
    [SerializeField] private string vaultHigh = "vaultoverhigh", vaultKong = "vaultkong", springboard = "springboardleftleg";
    [SerializeField] private string stepUp = "stepuprightleg", mantleLow = "vaultonto", mantleMid = "vaultontohigh";
    [SerializeField] private string ledgeClimbUp = "mx_ledge_climb_up";
    [SerializeField] private string ledgeCatch = "mx_ledge_catch_air";
    [SerializeField] private string ledgeShimmyLeft = "mx_ledge_shimmy_left", ledgeShimmyRight = "mx_ledge_shimmy_right";
    [SerializeField] private string wallClimbStart = "wallrunverticalstart", wallKickRight = "jumpwrright";
    [SerializeField, Tooltip("Play the gun-holding versions of clips. Off until the hands hold the gun by IK: those clips "
        + "relied on Source locking the hands to the gun, and without it the arms twist.")]
    private bool armedClips;
    [SerializeField, Tooltip("Ending of the gun-holding version of a clip, played while that gun is out (meslideloop → meslideloop_ar2).")]
    private string rifleSuffix = "_ar2", pistolSuffix = "_pistol";
    [SerializeField, Min(0f), Tooltip("Run-up speed (m/s): below the first a jump is a standing jump, above the second a running jump.")]
    private float jumpStillSpeed = 1f, jumpFastSpeed = 4f;
    [SerializeField, Min(0f), Tooltip("For a mantle off a bar, which has no move of its own (m): below the first a step up, below the second a low step onto it, below the third a high one, above it a full climb.")]
    private float mantleStepHeight = .7f, mantleLowHeight = 1.1f, mantleMidHeight = 1.6f;
    [SerializeField, Min(.1f), Tooltip("Sideways speed the ledge shimmy clips were authored for (m/s).")]
    private float shimmyClipSpeed = .5f;
    [SerializeField, Min(.1f), Tooltip("Playback speed of the vault and climb clips. The player is over before the clip is, "
        + "and it plays on until the feet are down and moving.")]
    private float vaultClipSpeed = 1f;
    [SerializeField, Min(.1f), Tooltip("Playback speed of the climb onto a low obstacle.")]
    private float ontoClipSpeed = 1.15f;
    [SerializeField, Min(0f), Tooltip("A vault this soon after the last one (s) takes the other vault clip: one clip cannot "
        + "crossfade into itself, so a repeat would snap back to its first frame.")]
    private float vaultChainTime = 1.5f;
    private string lastVault;
    private float lastVaultAt = float.NegativeInfinity;

    private WeaponController weapons;
    private string jumpClip, obstacleClip, wallKickClip, rollClip, downClip;
    private float obstacleYaw;
    private bool tucked, flailing;

    private string[] VariantClips => new[]
    {
        jumpStill, jumpSlow, airTuck, landCrouch, coilLand, coilLandCrouch, heavyLand, heavyLandFaceDown, heavyLandBack, rollLand,
        fallFlail, supineSlideEndCrouch, supineSlideEndProne, supineLand, supineGetUp, supineGetUpGun, supineGetUpCrouch,
        bellySlideEndCrouch, vaultHigh, vaultKong, springboard, stepUp, mantleLow, mantleMid, ledgeClimbUp, ledgeCatch, ledgeShimmyLeft, ledgeShimmyRight,
        wallClimbStart, wallKickRight
    };

    // The gun-holding channels of every one-shot clip the profile has one for.
    private void AddArmedChannels()
    {
        foreach (var name in new System.Collections.Generic.List<string>(byName.Keys))
            foreach (string suffix in new[] { rifleSuffix, pistolSuffix })
                if (!string.IsNullOrEmpty(suffix) && !byName[name].Synced && profile.Find(name + suffix) != null)
                    AddChannel(name + suffix, 0f);
    }

    private string Armed(string clipName)
    {
        if (!armedClips || string.IsNullOrEmpty(clipName)) return clipName;
        if (weapons == null) weapons = player.GetComponent<WeaponController>();
        var gun = weapons != null ? weapons.EquippedData : null;
        if (gun == null) return clipName;
        string armed = clipName + (gun.SoundProfile == GunSoundProfile.Pistol ? pistolSuffix : rifleSuffix);
        return byName.ContainsKey(armed) ? armed : clipName;
    }

    // A gun is out in the hands (not put away for a move, not fists).
    private bool HoldsGun
    {
        get
        {
            if (weapons == null) weapons = player.GetComponent<WeaponController>();
            return weapons != null && weapons.ShowsWeapon && weapons.EquippedData != null;
        }
    }

    private string JumpFor(float speed) => speed < jumpStillSpeed ? jumpStill : speed < jumpFastSpeed ? jumpSlow : jump;

    private string ObstacleClip()
    {
        var obstacle = player.Abilities.Traversal.Obstacle;
        switch (obstacle.Kind)
        {
            case VaultKind.StepUp: return stepUp;
            case VaultKind.Onto: return mantleLow;
            case VaultKind.OntoHigh: return mantleMid;
            case VaultKind.Heave: return ledgeClimbUp;
            case VaultKind.Over: return Vaulting(vault, vaultKong);
            case VaultKind.Kong: return Vaulting(vaultKong, vault);
            case VaultKind.OverHigh: return Vaulting(vaultHigh, vault);
        }
        float height = obstacle.Height;
        return height < mantleStepHeight ? stepUp : height < mantleLowHeight ? mantleLow : height < mantleMidHeight ? mantleMid : mantle;
    }

    // A quick second vault takes another clip.
    private string Vaulting(string clip, string other)
    {
        if (clip == lastVault && Time.time - lastVaultAt < vaultChainTime) clip = other;
        lastVault = clip;
        lastVaultAt = Time.time;
        return clip;
    }

    private float ObstacleClipSpeed => player.Abilities.Traversal.Obstacle.Kind == VaultKind.Onto ? ontoClipSpeed : vaultClipSpeed;

    // Clips of the moves without a VaultKind follow the route's progress instead.
    private void StartObstacleClip()
    {
        var traversal = player.Abilities.Traversal;
        var channel = Find(obstacleClip);
        if (channel == null || traversal.Obstacle.Kind == VaultKind.None) return;
        Restart(channel);
        channel.Playable.SetSpeed(ObstacleClipSpeed);
        obstacleYaw = transform.eulerAngles.y;
        if (traversal.Obstacle.Kind != VaultKind.Heave) return;
        StartHeave();
        if (traversal.Ledge.Surface == null) return;
        Vector3 inward = -traversal.Ledge.Surface.transform.TransformDirection(traversal.Ledge.LocalNormal);
        obstacleYaw = Mathf.Atan2(inward.x, inward.z) * Mathf.Rad2Deg;
    }

    // The move keeps the way it started: looking round turns the head, not the climb. In a vault the body turns with a view
    // that leads it too far, and late in it after the view, as Beatrun's does (PlayerBody.VaultLook).
    private void ObstaclePose(float dt)
    {
        TurnVaultWithView(dt);
        legYaw = obstacleYaw;
        targetYaw = Mathf.DeltaAngle(transform.eulerAngles.y, obstacleYaw);
        legsPlanted = true;
        turning = 0;
        var traversal = player.Abilities.Traversal;
        if (traversal.Obstacle.Kind == VaultKind.Heave) Drive(obstacleClip, traversal.ObstacleProgress);
        // A vault carried by its clip's hands (WarpedTraversal) plays the clip at the time the movement drives, so the hands
        // stay where the capsule was put for them.
        else if (traversal.ObstacleClipTime >= 0f && Find(obstacleClip) is Channel driven && driven.Length > 0f)
            Drive(obstacleClip, traversal.ObstacleClipTime / driven.Length);
        else
        {
            ReleaseObstacleClip();
            Want(obstacleClip);
        }
    }

    // After the movement has driven it, the clip runs on by itself from where it was, at its own pace.
    private void ReleaseObstacleClip()
    {
        var channel = Find(obstacleClip);
        if (channel != null && channel.Playable.IsValid() && channel.Playable.GetSpeed() <= 0f)
            channel.Playable.SetSpeed(ObstacleClipSpeed);
    }

    // In one of these moves the model stays on the capsule, as in GMod: easing the head back would pull the hands off.
    private bool ClipCarriesBody => playingOut || heaving || (!dead && (state == MovementState.Vault || state == MovementState.Mantle)
        && player.Abilities.Traversal.Obstacle.Kind != VaultKind.None) || ClimbsOffLadder;

    // Climbing off a ladder's top is warped on its clip's hands too (WarpedTraversal), so the model stays on the capsule.
    private bool ClimbsOffLadder => !dead && state == MovementState.Mantle && player.Abilities.Traversal.Obstacle.ExitLadder != null;

    /// <summary>A vault's or climb's clip is playing on after the move, to the touchdown (not a springboard).</summary>
    internal bool PlaysOutObstacle => playingOut && !springing;

    // The player is over before the clip is: it plays on until the feet are down and moving, or a spring takes over.
    private void PlayOutObstacle()
    {
        var channel = Find(obstacleClip);
        var traversal = player.Abilities.Traversal;
        if (channel == null || channel.Synced || traversal.Obstacle.Kind == VaultKind.None) return;
        ReleaseObstacleClip();
        // A move cut short never got over: carrying on with its clip would show a vault that isn't happening.
        if (traversal.ObstacleProgress < VaultMotion.EarliestLeave) return;
        if (player.VerticalVelocity > 1f && Find(springboard) is Channel spring)
        {
            Restart(spring);
            channel = spring;
            transitionLeft = spring.Length;
            springing = true;
        }
        else
        {
            float left = (channel.Length - (float)channel.Playable.GetTime()) / ObstacleClipSpeed;
            if (left <= blendTime) return;
            transitionLeft = left;
        }
        transition = channel;
        playingOut = true;
    }

    // A kick that throws the body to its right leaves a wall on its left, and the other way round.
    private string WallKickFor() => Vector3.Dot(player.HorizontalVelocity, transform.right) >= 0f ? wallKick : wallKickRight;

    // By MovementLanding's tier: a hard landing sinks deep into the knees (crouched ones end crouched instead of
    // standing up and ducking again); a heavy one has its own state. A light landing after a mid-air tuck runs out of it
    // with Beatrun's coil end, whose legs stride, so only on the move; dropping straight down, see SettleFromTuck. Plain
    // jumps blend on.
    private string LandingFor(bool crouched, bool wasTucked)
    {
        if (player.LastLandingTier != MovementLanding.Tier.Light) return crouched ? landCrouch : land;
        if (wasTucked && HorizontalSpeed() > walkClipSpeed) return crouched ? coilLandCrouch : coilLand;
        return null;
    }

    // Down from a tuck on the spot, the knees stay bent a moment on planted feet before the body stands up. The crouch
    // holds the head about where the tuck had it, so nothing jumps at the camera.
    private void SettleFromTuck()
    {
        var channel = Find(crouchIdle);
        if (channel == null || channel.Synced) return;
        transition = channel;
        transitionLeft = blendTime * 3f;
        playingOut = false;
    }

    // As Beatrun's safety roll picks them.
    private string RollFor() => !player.Abilities.Landing.LandsInPlace ? roll
        : player.HasCeilingAbove(player.StandingColliderHeight) ? landCrouch : rollLand;

    private string DownClip(MovementLanding.Pose pose) =>
        pose == MovementLanding.Pose.Back ? heavyLandBack : pose == MovementLanding.Pose.FaceDown ? heavyLandFaceDown : heavyLand;

    // Down after a heavy landing: the clip keeps pace with the time until the player is up, and the body keeps facing
    // the way it went down while the view turns.
    private void DownPose()
    {
        var landing = player.Abilities.Landing;
        Drive(downClip, stateTime / Mathf.Max(.05f, landing.HeavyDuration));
        legYaw = landing.HeavyYaw;
        targetYaw = Mathf.DeltaAngle(transform.eulerAngles.y, legYaw);
        legsPlanted = true;
        turning = 0;
    }

    private string SlideEndFor(MovementState from, MovementState to)
    {
        // A slide that came down from a dive on the back is lying already: it stays down, or gets up as from lying.
        if (from == MovementState.SupineSliding && slidesOnBack) from = MovementState.Supine;
        // Beatrun's jump turn on the ground: down onto the back legs first, and up again with jumpturnlandstand.
        if (to == MovementState.Supine)
            return from == MovementState.Sliding || from == MovementState.SupineSliding ? supineSlideEndProne
                : from == MovementState.SupineDiving ? supineLand : null;
        switch (from)
        {
            // Beatrun's DoJumpTurnStand: holding a weapon it stands up with jumpturnlandstandgun.
            case MovementState.Supine:
                return to == MovementState.Standing ? (HoldsGun ? supineGetUpGun : supineGetUp)
                    : to == MovementState.Crouching ? supineGetUpCrouch : null;
            case MovementState.Sliding: return to == MovementState.Proning ? null : slideEnd;
            case MovementState.SupineSliding:
                return to == MovementState.Proning ? supineSlideEndProne : to == MovementState.Crouching ? supineSlideEndCrouch : supineSlideEnd;
            case MovementState.HeadFirstSliding:
                return to == MovementState.Proning ? null : to == MovementState.Crouching ? bellySlideEndCrouch : bellySlideEnd;
            default: return null;
        }
    }

    // A long fall flails to the end: Ctrl in it only readies the landing roll and never tucks.
    private void AirPose()
    {
        bool flail = player.IsBigFall;
        if (flail && !flailing) Restart(Find(fallFlail));
        flailing = flail;
        if (flail) Want(fallFlail);
        else if (player.IsAirCrouching)
        {
            if (!tucked) Restart(Find(airTuck));
            tucked = true;
            Want(airTuck);
        }
        else if (jumped && airTime < (Find(jumpClip)?.Length ?? 0f)) Want(jumpClip);
        else Want(player.FallSpeed > fallClipSpeed ? fall : air);
    }

    // Catch the ledge and hang facing the wall wherever the player looks; shimmy, at the pace of the sideways
    // movement, while A/D actually moves along the ledge.
    private void LedgePose()
    {
        var ledge = player.Abilities.Traversal.Ledge;
        Vector3 inward = ledge.Surface != null ? -ledge.Surface.transform.TransformDirection(ledge.LocalNormal) : transform.forward;
        inward = Vector3.ProjectOnPlane(inward, Vector3.up);
        if (inward.sqrMagnitude > .01f) targetYaw = Vector3.SignedAngle(transform.forward, inward, Vector3.up);
        float side = Vector3.Dot(player.ActualVelocity, Vector3.Cross(Vector3.up, inward.normalized));
        if ((player.IsNetworkDriven || Mathf.Abs(player.CurrentInput.x) > .1f) && Mathf.Abs(side) > .1f)
        {
            string shimmy = side < 0f ? ledgeShimmyLeft : ledgeShimmyRight;
            Repeat(shimmy);
            Find(shimmy)?.Playable.SetSpeed(Mathf.Clamp(Mathf.Abs(side) / shimmyClipSpeed, .5f, 2.5f));
            FaceWall(Time.deltaTime);
        }
        else if (Find(ledgeCatch) is Channel catching && stateTime < catching.Length - blendTime) Want(ledgeCatch);
        else HangLook(inward.normalized, Time.deltaTime);
    }

    // Keep a one-shot clip going round while it is wanted.
    private void Repeat(string clipName)
    {
        var channel = Find(clipName);
        if (channel != null && channel.Playable.GetTime() >= channel.Length) channel.Playable.SetTime(0);
        Want(clipName);
    }
}
