using UnityEngine;

/// <summary>What the gun's sway reads each frame (WeaponSway.Advance).</summary>
public readonly struct SwayInput
{
    /// <summary>The frame the gun is posed in (the eyes), and the aim alone (the look pivot: mouse and recoil).</summary>
    public readonly Quaternion View, Aim;
    /// <summary>The body's velocity (world, m/s).</summary>
    public readonly Vector3 Velocity;
    public readonly bool Grounded, Prone, Sliding;
    /// <summary>Aiming down the sights was asked for (ArcCW's STATE_SIGHTS).</summary>
    public readonly bool Sighted;
    /// <summary>1 at the hip, 0 fully aimed (ArcCW's sight delta).</summary>
    public readonly float SightDelta;
    /// <summary>The body's walking speed (m/s): a stride's bob grows with speed up to it, and only a little past it.</summary>
    public readonly float WalkSpeed;
    /// <summary>The weapon's look and movement shares (Weapons.csv lookSway, moveSway).</summary>
    public readonly float LookScale, MoveScale;

    public SwayInput(Quaternion view, Quaternion aim, Vector3 velocity, bool grounded, bool prone, bool sliding, bool sighted,
        float sightDelta, float walkSpeed, float lookScale, float moveScale)
    {
        View = view; Aim = aim; Velocity = velocity; Grounded = grounded; Prone = prone; Sliding = sliding; Sighted = sighted;
        SightDelta = sightDelta; WalkSpeed = walkSpeed; LookScale = lookScale; MoveScale = moveScale;
    }
}

/// <summary>
/// The gun's sway with look and movement. Moving and walking are ArcCW's viewmodel sway ("coolsway", cl_viewmodel.lua:
/// Move_Process and Step_Process, sharing their offsets as they do there): moving, the gun leans with the body; walking,
/// it bobs with each stride. Breathing is ArcCW's Breath_Process the right way up, and turning is TFA Base's Sway
/// (client/viewmodel.lua): the gun trails a turn, muzzle last, and swings back a little past its place before it settles.
/// All of it fades as the sights come up. Presentation only: the gun is moved about the eyes after the shot and cover
/// checks used the plain pose, so bullets still go where the crosshair points.
/// Changed from ArcCW after the user's test (2026-11-06): its look sway turns the gun into the turn, which all but cancels
/// its slide against it, so the gun hardly trailed a turn; its stride grows with speed and a sprint adds more (×1.25 and
/// sextra), which looked comical on a body whose own run already bobs the eyes the gun rides; and its breath tips the
/// muzzle down as the gun rises, which read as a dip.
/// Both bases run once per drawn frame (ArcCW treats any frame longer than Source's 66.66 ticks as one tick), so here it
/// runs in fixed 1/66.66 s steps and shows a blend of the last two: the same sway at any frame rate. Offsets are in GMod
/// units (2.54 cm) and degrees; angles keep Source's sense (pitch down, yaw left, roll clockwise), and ArcCW's add up
/// component by component, as GMod's Angle:Add does.
/// </summary>
public sealed class WeaponSway
{
    private const float Unit = .0254f;
    private const float Tick = 1f / 66.66f;
    private const float StepEnd = Mathf.PI * 4f;
    // A frame this long after a pause or a hitch drops the rest instead of catching up.
    private const int MostTicks = 8;
    // Past the walking speed, a stride's bob grows by this share of the extra speed only.
    private const float RunShare = .3f;
    // ArcCW's breath: 0.1 units either way, once every π s (sin 2t). Here the gun rises over the first share of each breath,
    // settles over the next, and rests for the rest; it sways about the breath's mean height.
    private const float BreathReach = .2f, BreathPeriod = Mathf.PI, Inhale = .4f, Exhale = .45f, BreathMean = (Inhale + Exhale) * .5f;
    // TFA's Sway with its weapon-weight rule off: rate 1^1.5 × 10, wiggle factor 0.15 (counter rate × (0.75 + 0.5 − 0.15)),
    // cl_tfa_gunbob_intensity 1 (× 3).
    private const float LookRate = 10f, CounterRate = LookRate * (.75f + .5f - .15f), LookStrength = 3f;

    // ArcCW's VMPosOffset (x up, y right, z forward), VMAngOffset (x pitch, y yaw, z roll) and their smoothed copies, shared
    // by its processes as there: Step smooths the same position Move did.
    private Vector3 posOffset, angOffset, posLerp, angLerp;
    private float stepBob, stepRandomX = 1f, stepRandomY = 1f, clock, sinceTick;
    // TFA's motion and counterMotion (x pitch, y yaw, Source sense; the aim has no roll).
    private Vector2 motion, counterMotion;
    private Vector2 lastAim, frameAim;
    private bool started;
    private Vector3 previousOffset, lastOffset;
    private Quaternion previousTurn = Quaternion.identity, lastTurn = Quaternion.identity;
    private readonly System.Random random = new System.Random();

    public void Reset()
    {
        posOffset = angOffset = posLerp = angLerp = previousOffset = lastOffset = Vector3.zero;
        stepBob = clock = sinceTick = 0f;
        stepRandomX = stepRandomY = 1f;
        motion = counterMotion = Vector2.zero;
        previousTurn = lastTurn = Quaternion.identity;
        started = false;
    }

    /// <summary>
    /// Advances the sway by `dt` and gives where the gun goes in the view's frame: moved by `offset` (m) and turned by
    /// `turn`, both about the eyes.
    /// </summary>
    public void Advance(float dt, in SwayInput input, out Vector3 offset, out Quaternion turn)
    {
        Vector2 aim = SourceAim(input.Aim);
        if (!started)
        {
            started = true;
            lastAim = frameAim = aim;
            sinceTick = 0f;
        }
        if (dt > 0f)
        {
            // Each step sees the aim where it was at that moment of the frame.
            float at = Tick - sinceTick;
            int ticks = 0;
            while (at <= dt && ticks < MostTicks)
            {
                Vector2 sampled = new Vector2(Mathf.LerpAngle(frameAim.x, aim.x, at / dt), Mathf.LerpAngle(frameAim.y, aim.y, at / dt));
                Step(input, sampled);
                at += Tick;
                ticks++;
            }
            sinceTick = ticks == MostTicks ? 0f : Mathf.Repeat(sinceTick + dt, Tick);
            frameAim = aim;
        }
        float blend = Mathf.Clamp01(sinceTick / Tick);
        offset = Vector3.Lerp(previousOffset, lastOffset, blend);
        turn = Quaternion.Slerp(previousTurn, lastTurn, blend);
    }

    // One of ArcCW's frames (GetVMPosition), with TFA's Sway on top as TFA lays it over its own bob.
    private void Step(in SwayInput input, Vector2 aim)
    {
        clock += Tick;
        Vector3 eye = SourceAngles(input.View);
        Vector3 vmAng = eye;
        Vector3 vmPos = Vector3.zero;
        // WorldToLocal(velocity, angle_zero, vector_origin, EyeAng): x forward, y left, z up, in units a second.
        Vector3 local = Quaternion.Inverse(input.View) * input.Velocity / Unit;
        var velocity = new Vector3(local.z, -local.x, local.y);

        float sd = Mathf.Clamp01(input.SightDelta);
        // ArcCW: every multiplier is Lerp(sight delta, 0, its setting).
        float swayMult = Mathf.Max(0f, input.MoveScale) * sd, swaySpeed = sd;

        Move(ref vmPos, ref vmAng, velocity, input.Sighted, sd);
        Stride(ref vmPos, ref vmAng, velocity.magnitude, input, swayMult, swaySpeed);
        Breathe(ref vmPos, ref vmAng, input.Sighted);
        Quaternion trail = Look(ref vmPos, vmAng, aim, 1f - sd, Mathf.Max(0f, input.LookScale));

        Quaternion view = Rotation(eye), toView = Quaternion.Inverse(view);
        previousOffset = lastOffset;
        previousTurn = lastTurn;
        lastOffset = toView * (vmPos * Unit);
        lastTurn = toView * Rotation(vmAng) * trail;
    }

    // Move_Process: the gun rides up and back with the body's climb and drop, and leans with a strafe.
    private void Move(ref Vector3 vmPos, ref Vector3 vmAng, Vector3 velocity, bool sighted, float sd)
    {
        // ArcCW's Move pitches with what its look sway left in VMAngOffset.x; the look is TFA's here, which leaves none.
        angOffset.x = 0f;
        float sightedMult = sighted ? .05f : 1f;
        posOffset.x = Mathf.Clamp(velocity.z * .0025f, -1f, 1f) * sightedMult;
        posOffset.x += velocity.x * .001f * sd;
        posOffset.y = Mathf.Clamp(velocity.y * -.002f, -1f, 1f) * sightedMult;
        posOffset.z = Mathf.Clamp(posOffset.x * -2f, -4f, 4f);
        posLerp.x = Mathf.Lerp(posLerp.x, posOffset.x, 8f * Tick);
        posLerp.y = Mathf.Lerp(posLerp.y, posOffset.y, 8f * Tick);
        posLerp.z = Mathf.Lerp(posLerp.z, posOffset.z, 8f * Tick);
        angOffset.y = posOffset.y;
        angOffset.z = posOffset.y * .5f + posOffset.x * -5f + velocity.x * -.005f * sd;
        angLerp.x = LerpC(10f * Tick, angLerp.x, angOffset.x, .75f);
        angLerp.y = LerpC(5f * Tick, angLerp.y, angOffset.y, .6f);
        angLerp.z = Mathf.Lerp(angLerp.z, angOffset.z, 25f * Tick);
        vmPos += Up(vmAng) * posLerp.x + Right(vmAng) * posLerp.y + Forward(vmAng) * posLerp.z;
        angLerp = new Vector3(Mathf.DeltaAngle(0f, angLerp.x), Mathf.DeltaAngle(0f, angLerp.y), Mathf.DeltaAngle(0f, angLerp.z));
        vmAng += angLerp;
    }

    // Step_Process: a stride's bob, two dips and one sway from side to side every 4π of the step's phase. A slide takes no
    // strides, and past walking pace a stride grows by a share of the extra speed only: the body's own run already bobs the
    // eyes the gun rides, and ArcCW's sprint on top of it (×1.25, sextra) swung the gun about comically.
    private void Stride(ref Vector3 vmPos, ref Vector3 vmAng, float speed, in SwayInput input, float swayMult, float swaySpeed)
    {
        speed = input.Sliding ? 0f : Mathf.Min(speed, 400f);
        float walk = input.WalkSpeed / Unit;
        if (speed > walk) speed = walk + (speed - walk) * RunShare;
        float delta = Mathf.Abs(stepBob * 2f / StepEnd - 1f);
        float sightedMult = input.Sighted ? .25f : 1f;
        // ArcCW doubles the side sway only for guns lowered while running; these keep firing.
        const float sprintMult = 1f;
        float proneMult = input.Prone ? 10f : 1f;
        stepBob += (speed * .00015f + Mathf.Pow(delta, .01f) * .03f) * swaySpeed * Tick * 300f;
        if (stepBob >= StepEnd)
        {
            stepBob = 0f;
            stepRandomX = Range(1f, 1.5f);
            stepRandomY = Range(1f, 1.5f);
        }
        if (speed == 0f) stepBob = 0f;
        if (input.Grounded)
        {
            posOffset.x = Mathf.Sin(stepBob) * speed * .000375f * sightedMult * swayMult * stepRandomX;
            posOffset.y = Mathf.Sin(stepBob * .5f) * speed * .0005f * sightedMult * sprintMult * proneMult * swayMult * stepRandomY;
            posOffset.z = Mathf.Sin(stepBob * .75f) * speed * .002f * sightedMult * proneMult * swayMult;
        }
        posLerp.x = Mathf.Lerp(posLerp.x, posOffset.x, 32f * Tick);
        posLerp.y = Mathf.Lerp(posLerp.y, posOffset.y, 4f * Tick);
        posLerp.z = Mathf.Lerp(posLerp.z, posOffset.z, 2f * Tick);
        angOffset.x = posLerp.x * 2f;
        angOffset.y = posLerp.y * -7.5f;
        angOffset.z = posLerp.y * 10f;
        vmPos += Up(vmAng) * posLerp.x + Right(vmAng) * posLerp.y + Forward(vmAng) * posLerp.z;
        vmAng += angOffset;
    }

    // Breath_Process the right way up. ArcCW's gun rises and falls on a sine and tips its muzzle down as it rises, which
    // reads as a dip; here it rises over the inhale with the muzzle lifting, as a rifle's does as the chest fills, settles
    // over the exhale, and rests a moment before the next breath. ArcCW's reach, period and sideways drift. ArcCW multiplies
    // its breathing by the sighted share every frame and the line that would restore it is commented out, so there the
    // breathing stops for good at the first aim; here it is the sighted share each step.
    private void Breathe(ref Vector3 vmPos, ref Vector3 vmAng, bool sighted)
    {
        float intensity = sighted ? .05f : 1f;
        const float rate = 1f;
        posOffset.x = (Breath(Mathf.Repeat(clock * rate / BreathPeriod, 1f)) - BreathMean) * BreathReach * intensity;
        posOffset.y = Mathf.Sin(clock * 2.5f * rate) * .025f * intensity;
        angOffset.x = posOffset.x * -1.5f;
        angOffset.y = posOffset.y * 2f;
        angOffset.z = posOffset.y * posOffset.x * -40f;
        vmPos += Up(vmAng) * posOffset.x + Right(vmAng) * posOffset.y;
        vmAng += angOffset;
    }

    // How full the breath is (0–1) this far through it: filling, emptying, then a rest.
    private static float Breath(float phase)
    {
        if (phase < Inhale) return Mathf.SmoothStep(0f, 1f, phase / Inhale);
        if (phase < Inhale + Exhale) return Mathf.SmoothStep(1f, 0f, (phase - Inhale) / Exhale);
        return 0f;
    }

    // TFA Base's Sway ("based on Juckey's methods"): the turn's speed, per 120 °/s and eased off the further the gun has
    // swung, pulls `motion` at 10 a second; `counterMotion` chases −motion a little faster and their sum feeds back, so the
    // gun swings back past its place when the turn stops, then settles. The gun turns against the turn by motion (the yaw
    // by two thirds) and slides with it by a fifth of a unit per degree, which turns it about a point some 30 cm ahead of
    // the eyes, so the muzzle trails most; at the sights it swings 15 % as far, about a point twice as far ahead. TFA
    // leaves the view punch out of the turn; this aim holds the recoil's quick kick too (PlayerLook keeps it to itself),
    // a small, short swing.
    private Quaternion Look(ref Vector3 vmPos, Vector3 vmAng, Vector2 aim, float ads, float scale)
    {
        float clampFac = 1.1f - Mathf.Min((Mathf.Abs(motion.x) + Mathf.Abs(motion.y)) / 20f, 1f);
        // The turn crosses ±180° the short way round, as math.AngleDifference does.
        var delta = new Vector2(Mathf.DeltaAngle(lastAim.x, aim.x), Mathf.DeltaAngle(lastAim.y, aim.y)) / Tick / 120f * clampFac;
        lastAim = aim;
        counterMotion = Vector2.Lerp(counterMotion, -motion, Tick * CounterRate);
        motion = Vector2.Lerp(motion, delta + motion + counterMotion, Tick * LookRate);
        float fac = LookStrength * ((1f - ads) * .85f + .15f) * scale, slide = .2f + .2f * ads;
        vmPos += Right(vmAng) * (-motion.y * slide * .66f * fac) + Up(vmAng) * (-motion.x * slide * fac);
        // ang:RotateAroundAxis(ang:Right(), p) lifts the muzzle as the view drops; ang:RotateAroundAxis(ang:Up(), −y) swings
        // it right as the view turns left. Each turns about the gun's own axes, so they follow its rotation.
        return Quaternion.Euler(-motion.x * fac, 0f, 0f) * Quaternion.Euler(0f, motion.y * .66f * fac, 0f);
    }

    private static float LerpC(float t, float a, float b, float power) => a + (b - a) * Mathf.Pow(t, power);

    private float Range(float from, float to) => from + (to - from) * (float)random.NextDouble();

    // Source angles (pitch down, yaw left, roll clockwise) of a Unity rotation, and back: Unity's yaw and roll turn the
    // other way, and both apply yaw, then pitch, then roll.
    private static Vector3 SourceAngles(Quaternion rotation)
    {
        Vector3 e = rotation.eulerAngles;
        return new Vector3(Mathf.DeltaAngle(0f, e.x), -Mathf.DeltaAngle(0f, e.y), -Mathf.DeltaAngle(0f, e.z));
    }

    private static Vector2 SourceAim(Quaternion rotation)
    {
        Vector3 e = rotation.eulerAngles;
        return new Vector2(Mathf.DeltaAngle(0f, e.x), -Mathf.DeltaAngle(0f, e.y));
    }

    private static Quaternion Rotation(Vector3 source) => Quaternion.Euler(source.x, -source.y, -source.z);
    private static Vector3 Up(Vector3 source) => Rotation(source) * Vector3.up;
    private static Vector3 Right(Vector3 source) => Rotation(source) * Vector3.right;
    private static Vector3 Forward(Vector3 source) => Rotation(source) * Vector3.forward;
}
