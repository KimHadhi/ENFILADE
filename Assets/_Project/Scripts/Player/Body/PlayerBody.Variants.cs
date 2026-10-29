using UnityEngine;
using UnityEngine.Playables;
using MovementState = PlayerMovement.MovementState;

/// <summary>
/// Which clip of a family fits the moment: jumps by run-up speed, the mid-air knee tuck, a long fall's flailing, vaults and mantles by
/// obstacle height, landings by impact, crouched landings and slide exits, the ledge catch and shimmy, the wall kick's
/// side, and the gun-holding version of a clip while a rifle or pistol is out. Names and thresholds come from
/// Player.csv, row Body.
/// </summary>
public sealed partial class PlayerBody
{
    [Header("Clip variants")]
    [SerializeField] private string jumpStill = "jumpstill", jumpSlow = "jumpslow", airTuck = "jumpcoil";
    [SerializeField] private string landCrouch = "landcrouch", coilLand = "jumpcoilend", coilLandCrouch = "jumpcoilendcrouch";
    [SerializeField] private string landSevere = "mx_hard_landing";
    [SerializeField, Tooltip("A long fall (Ability.bigFallHeight): arms and legs flail until touchdown, Ctrl or not.")]
    private string fallFlail = "fallinguncontrolled";
    [SerializeField] private string supineSlideEndCrouch = "meslideendcrouch", supineSlideEndProne = "meslideendprone";
    [SerializeField] private string bellySlideEndCrouch = "diveslideendcrouch";
    [SerializeField] private string vaultHigh = "mx_vault_two_hands";
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
    [SerializeField, Min(0f), Tooltip("Obstacle height above the feet (m) from which the higher vault plays.")]
    private float vaultHighHeight = 1f;
    [SerializeField, Min(0f), Tooltip("Mantle heights (m): below the first a step up, below the second a low step onto it, below the third a high one, above it a full climb.")]
    private float mantleStepHeight = .7f, mantleLowHeight = 1.1f, mantleMidHeight = 1.6f;
    [SerializeField, Min(0f), Tooltip("Falls faster than this land on hands and knees (m/s), whether moving or not.")]
    private float severeLandingSpeed = 16f;
    [SerializeField, Min(.1f), Tooltip("Sideways speed the ledge shimmy clips were authored for (m/s).")]
    private float shimmyClipSpeed = .5f;
    [SerializeField, Min(.1f), Tooltip("Playback speed of the vault clips. A vault keeps the run-up speed and is over "
        + "before its clip, which then plays on to touchdown.")]
    private float vaultClipSpeed = 1.3f;
    [SerializeField, Range(.1f, 1f), Tooltip("Share of a vault clip at which its feet touch down; the body runs on from there.")]
    private float vaultLandShare = .72f;
    [SerializeField, Min(0f), Tooltip("A vault this soon after the last one (s) takes the other vault clip: one clip cannot "
        + "crossfade into itself, so a repeat would snap back to its first frame.")]
    private float vaultChainTime = 1.5f;
    private string lastVault;
    private float lastVaultAt = float.NegativeInfinity;

    private WeaponController weapons;
    private string jumpClip, obstacleClip, wallKickClip;
    private bool tucked, flailing;

    private string[] VariantClips => new[]
    {
        jumpStill, jumpSlow, airTuck, landCrouch, coilLand, coilLandCrouch, landSevere, fallFlail, supineSlideEndCrouch, supineSlideEndProne,
        bellySlideEndCrouch, vaultHigh, stepUp, mantleLow, mantleMid, ledgeClimbUp, ledgeCatch, ledgeShimmyLeft, ledgeShimmyRight,
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

    private string JumpFor(float speed) => speed < jumpStillSpeed ? jumpStill : speed < jumpFastSpeed ? jumpSlow : jump;

    // A quick second vault takes the other vault clip; climbing up from a hang starts from the hanging pose, whatever the height.
    private string ObstacleClip(bool isVault, bool fromHang)
    {
        float height = player.Abilities.Traversal.Obstacle.Height;
        if (isVault)
        {
            string clip = height > vaultHighHeight ? vaultHigh : vault;
            if (clip == lastVault && Time.time - lastVaultAt < vaultChainTime) clip = clip == vault ? vaultHigh : vault;
            lastVault = clip;
            lastVaultAt = Time.time;
            return clip;
        }
        if (fromHang) return ledgeClimbUp;
        return height < mantleStepHeight ? stepUp : height < mantleLowHeight ? mantleLow : height < mantleMidHeight ? mantleMid : mantle;
    }

    private void StartVault()
    {
        var channel = Find(obstacleClip);
        if (channel == null) return;
        Restart(channel);
        channel.Playable.SetSpeed(vaultClipSpeed);
    }

    // The player is over the obstacle before the clip is: it plays on to its touchdown, then the body runs on.
    private void PlayOutVault()
    {
        var channel = Find(obstacleClip);
        if (channel == null || channel.Synced) return;
        float left = (vaultLandShare * channel.Length - (float)channel.Playable.GetTime()) / vaultClipSpeed;
        if (left <= blendTime) return;
        transition = channel;
        transitionLeft = left;
        playingOut = true;
    }

    // A kick that throws the body to its right leaves a wall on its left, and the other way round.
    private string WallKickFor() => Vector3.Dot(player.HorizontalVelocity, transform.right) >= 0f ? wallKick : wallKickRight;

    // By impact speed: a severe fall lands on hands and knees, a hard one sinks deep into the knees (or runs it off when
    // moving; crouched ones end crouched instead of standing up and ducking again). A softer landing after a mid-air
    // tuck just straightens the legs, as Beatrun pairs its coil with the coil end. Plain jumps blend on.
    private string LandingFor(bool crouched, float impact, bool wasTucked)
    {
        if (impact > severeLandingSpeed) return crouched ? landCrouch : landSevere;
        if (impact > hardLandingSpeed) return crouched ? landCrouch : HorizontalSpeed() > walkClipSpeed ? landMoving : land;
        if (wasTucked) return crouched ? coilLandCrouch : coilLand;
        return null;
    }

    private string SlideEndFor(MovementState from, MovementState to)
    {
        switch (from)
        {
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
        else Want(player.VerticalVelocity < -hardLandingSpeed * .6f ? fall : air);
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
        if (Mathf.Abs(player.CurrentInput.x) > .1f && Mathf.Abs(side) > .1f)
        {
            string shimmy = side < 0f ? ledgeShimmyLeft : ledgeShimmyRight;
            Repeat(shimmy);
            Find(shimmy)?.Playable.SetSpeed(Mathf.Clamp(Mathf.Abs(side) / shimmyClipSpeed, .5f, 2.5f));
        }
        else Sequence(ledgeCatch, ledgeHang);
    }

    // Keep a one-shot clip going round while it is wanted.
    private void Repeat(string clipName)
    {
        var channel = Find(clipName);
        if (channel != null && channel.Playable.GetTime() >= channel.Length) channel.Playable.SetTime(0);
        Want(clipName);
    }
}
