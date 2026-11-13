using UnityEngine;

// WeaponController: one frame of the gun in hand, the trigger, the shot, the reload and where it aims. One part of the class; the rest is in WeaponController.cs and the files beside it.
public sealed partial class WeaponController
{
    internal void ProcessFrame(float dt, WeaponInputFrame frame)
    {
        if (dt <= 0f || float.IsNaN(dt) || float.IsInfinity(dt)) return;
        // Held by a shambler the gun can't be used either (PlayerHold).
        var mover = owner != null ? owner.GetComponent<PlayerMovement>() : null;
        if (mover != null && (mover.CombatInputLocked || mover.IsHeld))
        { Runtime?.Interrupt(); ResetPresentation(); return; }
        if (Runtime == null || Runtime.Data.Category == WeaponCategory.Melee ||
            aimOrigin == null ||
            !isActiveAndEnabled ||
            (health != null && !health.IsAlive))
            return;

        var dodge = movement.OrNull()?.Abilities?.Dodge;
        int activation = dodge?.Activation ?? 0;
        if (activation != dodgeActivation)
        {
            if (dodge.Kind == DodgeKind.Step) InterruptForSurvival();
            dodgeActivation = activation;
        }
        bool stepDodging = dodge != null && dodge.Kind == DodgeKind.Step && dodge.IsInvulnerable;
        shoveRecovery = Mathf.Max(0f, shoveRecovery - dt);
        switchLeft = Mathf.Max(0f, switchLeft - dt);
        bloom = Mathf.MoveTowards(bloom, 0f, weaponData.BloomRecovery * dt);
        timeSinceShot += dt;
        if (timeSinceShot > .35f) recoilIndex = 0;
        // Coming out or going away holds the gun back like a move that needs the hands.
        bool bodyAction = bareHandsLeft > 0f || switchLeft > 0f || (movement != null && (movement.IsParkour
            || movement.CurrentState == PlayerMovement.MovementState.DashPush || movement.IsLandingRecovery));
        // A look over the gun ends by itself, or as soon as the hands are wanted for anything else.
        if (inspecting)
        {
            inspectLeft -= dt;
            if (inspectLeft <= 0f) inspecting = false;
            else if (frame.FirePressed || frame.FireHeld || frame.AimHeld || frame.ReloadPressed || bodyAction || stepDodging) StopInspecting();
        }
        // Asked for like an R press, so whatever holds a reload back now holds this one back until it lets go.
        if (AutoReloadDue(dt)) frame = new WeaponInputFrame(frame.FirePressed, frame.FireHeld, true, frame.AimHeld);
        if (survivalInterrupted || bodyAction || stepDodging || shoveRecovery > 0f)
            frame = new WeaponInputFrame(frame.FirePressed, frame.FireHeld, false, frame.AimHeld);
        survivalInterrupted = false;
        bool aiming = frame.AimHeld && !Runtime.IsReloading && !bodyAction && shoveRecovery <= 0f
            && !stepDodging && !frame.ReloadPressed;
        float nextAim = Mathf.MoveTowards(AimBlend, aiming ? 1f : 0f, dt / AdsTime);
        Vector3 target = AimTarget(weaponData.Range, out Collider onCrosshair);
        AimPoint = target;
        HasAimPoint = true;
        // Probe the requested sight position even while high ready suppresses ADS. Otherwise
        // a clear hip pose and blocked ADS pose can alternate, causing zoom/ready flicker.
        Vector3 nominalMuzzle = presentation != null ? presentation.Prepare(PoseView, weaponData, aiming ? 1f : nextAim) : PoseView.position;
        // Held in close, the usual hold counts as clear again only with the same room a raised gun needs to come down.
        float margin = IsHighReady || IsHeldClose ? RaisedClearance : 0f;
        bool blocked = WeaponAimSolver.IsObstructed(owner, PoseView, nominalMuzzle, target, hitLayers, margin);
        AimBlend = blocked || presentation?.IsHighReady == true ? 0f : nextAim;
        if (presentation != null)
        {
            // While aiming is held, still the sight position: high ready holds AimBlend at 0, and the hip pose under low
            // cover would keep it raised for as long as the player aimed over that cover.
            nominalMuzzle = presentation.Prepare(PoseView, weaponData, aiming ? 1f : AimBlend);
            blocked |= WeaponAimSolver.IsObstructed(owner, PoseView, nominalMuzzle, target, hitLayers, margin);
            if (blocked && AimBlend > 0f)
            { AimBlend = 0f; presentation.Prepare(PoseView, weaponData, 0f); }
        }
        // A pistol comes in to the chest instead of going up, sights down (FPSBody.CloseHold).
        IsHeldClose = HoldClose(dt, ref blocked, target, IsHighReady ? RaisedClearance : 0f, WeaponAimSolver.IsFoe(onCrosshair));
        if (IsHeldClose) AimBlend = 0f;
        bool readyToAct = !Runtime.IsReloading && !bodyAction && shoveRecovery <= 0f && !stepDodging;
        bool wantsGun = aiming || (readyToAct && (frame.FirePressed || frame.FireHeld));
        if (blocked) raisedHoldLeft = RaisedHold;
        else if (IsHighReady && raisedHoldLeft > 0f)
        {
            if (wantsGun || !WeaponAimSolver.IsObstructed(owner, PoseView, nominalMuzzle, target, hitLayers, WideClearance))
                raisedHoldLeft = 0f;
            else { raisedHoldLeft -= dt; blocked = true; }
        }
        bool quickLower = !blocked && wantsGun;
        presentation?.HoldClose(IsHeldClose);
        IsHighReady = presentation != null ? presentation.Pose(owner, PoseView, target, blocked, Runtime, dt, hitLayers, quickLower) : blocked;
        if (IsHighReady) AimBlend = 0f;
        // Apex's Peacekeeper precision choke: held fully down the sights, a shotgun draws its pellets in over Choke Seconds;
        // anything else lets it out (a shot does, below).
        bool choking = weaponData.HasChoke && AimBlend >= .999f && !Runtime.IsReloading;
        float chokeBefore = chokeCharge;
        chokeCharge = choking ? Mathf.MoveTowards(chokeCharge, 1f, dt / Mathf.Max(.01f, weaponData.ChokeSeconds)) : 0f;
        if (chokeBefore < 1f && chokeCharge >= 1f) { chokePop = ChokePopTime; ChokeCharged?.Invoke(); }
        chokePop = Mathf.Max(0f, chokePop - dt);
        bool cannotFire = IsHighReady || shoveRecovery > 0f || bodyAction;
        if (cannotFire) Runtime.CancelBurst();
        if (blocked || !readyToAct) queuedFireLeft = 0f;
        else if (frame.FirePressed && IsHighReady) queuedFireLeft = QueuedFireWindow;
        else if (!cannotFire && queuedFireLeft > 0f)
        { frame = new WeaponInputFrame(true, frame.FireHeld, frame.ReloadPressed, frame.AimHeld); queuedFireLeft = 0f; }
        else queuedFireLeft = Mathf.Max(0f, queuedFireLeft - dt);
        bool reloadingBeforeTick = Runtime.IsReloading;
        // The trigger during a shell reload ends it once the shell in hand is in (KINEMATION's TacticalShotgun plays its end).
        if (reloadingBeforeTick && !cannotFire && (frame.FirePressed || frame.FireHeld)) Runtime.StopAfterShell();
        if (cannotFire || reloadingBeforeTick)
            frame = new WeaponInputFrame(false, false, frame.ReloadPressed, frame.AimHeld);
        UpdateSpread();

        bool wasReloading = Runtime.IsReloading;
        int stepsBefore = Runtime.StepCount;
        // A revolver in Fanning fans only from the hip (user, 2026-11-11: "리볼버에 Fanning 기능 넣기").
        Runtime.FanFromHip = AimBlend < .5f;
        int shots = Runtime.Tick(dt, frame);

        if (!wasReloading && Runtime.IsReloading) ReloadStarted?.Invoke(Runtime.IsEmptyReload);
        else if (wasReloading && !Runtime.IsReloading) { loadsSeen = Runtime.Loads; ReloadCompleted?.Invoke(); }
        else if (Runtime.IsReloading && Runtime.StepCount != stepsBefore) ReloadStepped?.Invoke(Runtime.Step);
        if (shots == 0 && frame.FirePressed && (Runtime.Ammo == 0 || Runtime.ChamberDue) && !Runtime.IsReloading) DryFired?.Invoke();

        for (int i = 0; i < shots; i++)
        {
            Vector3 origin = muzzlePoint != null ? muzzlePoint.position : PoseView.position;
            target = AimTarget(weaponData.Range, out Collider aimedNow);
            // Held in close, no enemy holds the shot back, and one the gun is pressed into is struck from just short of it
            // (a bullet starting inside its hitboxes would pass them).
            if (IsHeldClose) origin = WeaponAimSolver.ShortOfFoe(owner, PoseView, origin, hitLayers);
            if (WeaponAimSolver.IsObstructed(owner, PoseView, origin, target, hitLayers, 0f, IsHeldClose, IsHeldClose && WeaponAimSolver.IsFoe(aimedNow)))
            { Runtime.RefundUnfiredShots(shots - i); IsHighReady = true; AimBlend = 0f; break; }
            // From the muzzle to what the crosshair is on, which stays in the middle of the screen: the gun turns to point
            // there (FPSBody's GunConvergeLayer), as the gun did before KINEMATION (user, 2026-11-09: "총이 조준선을 옮기면
            // 안 되고 조준선 쪽으로 총이 이동해야지").
            Vector3 toSight = (target - origin).normalized;
            Vector3 aimed = WeaponAimSolver.SpreadDirection(toSight, CurrentSpread, UnityEngine.Random.insideUnitCircle);
            // Siege's recoil is nearly the same down the sights as from the hip; aiming only steadies it a little.
            Vector2 recoil = weaponData.RecoilKick(recoilIndex++) * Mathf.Lerp(1f, .85f, AimBlend)
                * (Crouched ? weaponData.CrouchRecoil : 1f);
            // A vertical grip steadies the climb (Siege's).
            if (Grip == GunAttachments.Kind.VerticalGrip) recoil.x *= weaponData.GripRecoil;

            // A shotgun's pellets each fly their own way, each a bullet of its own (own attack, own hit); one shot of a
            // single-bullet gun is pellet 0 on its own. The pellets fill one circle evenly, middle to edge, every shot
            // (EvenDisk): the circle round the crosshair's middle that the gun's spread and its Pellet Spread make together,
            // which is the ring the crosshair draws while such a gun is in hand (UpdateCrosshair). So what the ring holds is
            // what the shot covers, with no clump and no hole (user, 2026-11-10: pellets thrown at random read as weak and
            // unfair, and one sent dead on the middle was not it either: "조준점 동그란 부분이 다 맞게"). Only the pattern's
            // turn is left to chance.
            int pellets = weaponData.Pellets;
            float patternTurn = pellets > 1 ? UnityEngine.Random.value * Mathf.PI * 2f : 0f;
            for (int pellet = 0; pellet < pellets; pellet++)
            {
                long id = DamageHit.NewAttackId();
                Vector3 direction = aimed;
                if (pellets > 1)
                {
                    // Each pellet is aimed from the eyes, as the single bullet is: it goes from the muzzle to what the
                    // eyes see at its own place in the ring. Spread about the muzzle's line to the crosshair's point, the
                    // pattern only met the ring at that point's distance; a zombie nearer than what the middle of the
                    // screen was on, a step away with the middle looking past its shoulder, stood inside the ring and
                    // was missed by a hand's breadth, the gun being off to the side of the eyes (user, 2026-11-10:
                    // "근거리면 원 안 쪽이 다 맞게", "부정확한 소총"). A pellet whose point is no place to shoot from the
                    // muzzle (beside or behind it, against a wall) keeps the muzzle's own spread.
                    Vector2 place = EvenDisk(pellet, pellets, patternTurn);
                    float cone = CurrentSpread + weaponData.PelletCone(AimBlend, ChokeShare);
                    direction = WeaponAimSolver.SpreadDirection(toSight, cone, place);
                    Vector3 seen = AimTarget(WeaponAimSolver.SpreadDirection(aimOrigin.forward, cone, place), weaponData.Range) - origin;
                    if (seen.sqrMagnitude > .04f && Vector3.Dot(seen.normalized, direction) > PelletAimAgrees) direction = seen.normalized;
                }

                // Body damage falls off with range, as in Rainbow Six Siege; a headshot kills at any range there, so the
                // head's multiplier makes up what the range took (WeaponData.FalloffAt).
                float falloff = AttackTrace.FirstExternalHit(owner, origin, direction, weaponData.Range, hitLayers, out RaycastHit first)
                    ? weaponData.FalloffAt(first.distance) : 1f;
                var attack = new DamageHit(
                    id,
                    weaponData.Damage * falloff,
                    DamageKind.Attack,
                    origin,
                    direction,
                    owner,
                    postureDamage: weaponData.PostureDamage,
                    method: DamageMethod.Bullet);

                var trace = AttackTrace.Bullet(
                    attack,
                    origin,
                    direction,
                    weaponData.Range,
                    hitLayers, weaponData.HeadshotMultiplier / Mathf.Max(.01f, falloff), weaponData.Penetration, true, out BulletLeg[] later,
                    weaponData.BodyPierce);

                var shot = new WeaponShot(
                    id,
                    origin,
                    direction,
                    trace,
                    recoil,
                    Runtime.Ammo + shots - i - 1,
                    later,
                    pellet);

                LastShot = shot;
                PushProp(trace, direction, 1f);
                if (later != null)
                    foreach (var leg in later) PushProp(leg.Trace, leg.Onward.Direction, leg.Share);

                if (drawRays)
                {
                    Debug.DrawRay(
                        origin,
                        trace.End - origin,
                        trace.HasHit ? Color.red : Color.cyan,
                        0.2f);
                }

                // Zombies hear it (ZombieMind), once per shot however many pellets.
                if (shot.Pellet == 0) Noise.Make(origin, ShotNoise, owner);
                // Presentation (WeaponFeedback, MuzzleEffects, impact and blood effects) subscribes here.
                ShotFired?.Invoke(shot);
            }
            bloom = Mathf.Min(weaponData.MaxBloom, bloom + weaponData.BloomPerShot);
            timeSinceShot = 0f;
            // The shot lets the choke out; held down the sights it draws in again for the next.
            chokeCharge = chokePop = 0f;
            look.OrNull()?.ApplyRecoil(recoil, weaponData);
            presentation?.OnShot(weaponData, recoil);
            UpdateSpread();
        }
        UpdateCrosshair();
        cameraEffects.OrNull()?.RefreshWeaponFov();
    }

    // A pellet takes the point its place in the ring shows only while that lies within about 40 degrees of where the
    // muzzle's own spread would send it (the cosine).
    private const float PelletAimAgrees = .766f;

    // Where pellet <index> of <count> goes in the unit circle: a sunflower's seeds, which fill it evenly from the middle
    // to the edge whatever the count, the whole pattern turned by <turn> (radians).
    private static Vector2 EvenDisk(int index, int count, float turn)
    {
        const float GoldenAngle = 2.39996323f;
        float radius = Mathf.Sqrt((index + .5f) / Mathf.Max(1, count)), angle = index * GoldenAngle + turn;
        return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
    }

    // ArcCW's automatic reload (arccw_automaticreload): an empty magazine with rounds in reserve reloads 0.2 s after the
    // gun could fire again, and so does a gun whose cut-short reload left only its bolt to work (WeaponRuntime.ChamberDue;
    // one left with its magazine out is empty). The player can switch it off (Settings → 조작 → 자동 장전; user, 2026-11-11:
    // "총알 다 썼을 때 자동 장전 되는지 확인하고"). Someone else's copy reloads when their PC says so.
    private bool AutoReloadDue(float dt)
    {
        bool waiting = weaponData.AutoReloadDelay >= 0f && GameSettings.Current.autoReload && (movement == null || !movement.IsNetworkDriven)
            && ((Runtime.Ammo == 0 && Runtime.ReserveAmmo > 0) || Runtime.ChamberDue) && !Runtime.IsReloading && Runtime.CooldownRemaining <= 0f;
        emptyReady = waiting ? emptyReady + dt : 0f;
        return waiting && emptyReady > weaponData.AutoReloadDelay;
    }

    // ArcCW's crouch rule: on the ground and crouched.
    private bool Crouched => movement != null && movement.IsGrounded && movement.IsCrouching;

    // A loose prop the bullet struck takes the share of the push the bullet still carried.
    private void PushProp(in AttackTraceResult trace, Vector3 direction, float share)
    {
        var prop = trace.HasHit ? PhysicsProp.Of(trace.Hit.collider) : null;
        if (prop != null) prop.Push(direction.normalized * (weaponData.PropImpulse * share), trace.Hit.point);
    }

    private Vector3 AimTarget(float range, out Collider aimed) => AimTarget(aimOrigin.forward, range, out aimed);

    // What the eyes see along `forward`: the same ray turned off the middle of the screen, for a pellet's place in the ring.
    private Vector3 AimTarget(Vector3 forward, float range) => AimTarget(forward, range, out _);

    private Vector3 AimTarget(Vector3 forward, float range, out Collider aimed)
    {
        Vector3 start = aimOrigin.position;
        if (eyes != null) start += forward * Mathf.Max(0f, Vector3.Dot(eyes.position - start, forward));
        if (AttackTrace.FirstExternalHit(owner, start, forward, range, hitLayers, out RaycastHit hit))
        {
            aimed = hit.collider;
            return hit.point;
        }
        aimed = null;
        return start + forward * range;
    }

    // A pistol's close hold (FPSBody.CloseHold; the user, 2026-11-13: "권총은 아무리 가까워도 … 가슴쪽에 가까이 대는 존 윅
    // 자세 … 아무리 가까워도 총 쏠 수 있는 상태가 돼서 CQB하는 데 쓰이게"): wanted while an enemy is close before the eyes or the
    // usual hold is blocked, and for its linger after; taken while the gun held in close is clear of whatever blocks it, which
    // no enemy does, so it fires into one pressed against it. The usual hold's block then doesn't raise the gun; a wall that
    // blocks even the hold in close raises it to high ready as before. Every PC runs it for its copy of the player.
    private bool HoldClose(float dt, ref bool blocked, Vector3 target, float margin, bool foeAimed)
    {
        if (presentation == null || !presentation.CanHoldClose(PoseView, IsHeldClose, hitLayers, out Vector3 close, out bool foe, out float linger))
        {
            closeLeft = 0f;
            return false;
        }
        bool reason = foe || blocked;
        if (!reason && closeLeft <= 0f) return false;
        if (WeaponAimSolver.IsObstructed(owner, PoseView, close, target, hitLayers, margin, true, foeAimed))
        {
            closeLeft = 0f;
            return false;
        }
        closeLeft = reason ? linger : closeLeft - dt;
        blocked = false;
        // Coming down from high ready into the hold, the gun doesn't wait out the raised hold.
        raisedHoldLeft = 0f;
        return true;
    }
}
