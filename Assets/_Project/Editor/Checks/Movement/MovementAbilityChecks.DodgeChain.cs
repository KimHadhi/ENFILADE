using UnityEngine;
using State = PlayerMovement.MovementState;

public static partial class MovementAbilityChecks
{
    private static void RunDodgeChainChecks()
    {
        Check("removed wall state preserves later serialized values", RemovedWallState);
        Check("successful micro dodge immediately chains with fresh input", MicroDodgeChain);
        Check("step trail rewards a real melee near miss without damage", TrailMelee);
        Check("success restarts an unfinished step but the same attack cannot refund twice", StepDodgeChain);
        Check("step can chain into micro; micro creates no trail", CrossDodgeChain);
        Check("inactive, far away and expired attacks never reward a miss", TrailLifetime);
        Check("recognition follows actual movement rather than requested distance", TrailWallStop);
        Check("hitscan near misses refund cooldown while cover blocks recognition", TrailHitscan);
        Check("ray geometry includes diagonal swept body, caps and padding only", TrailGeometry);
        Check("reset removes trails; real damage remains enabled outside dodge", TrailReset);
    }
    private static DodgeController Dodge(Fixture f) => (DodgeController)Service(f.Abilities, "Dodge");
    private static StepDodgeTrail Trail(Fixture f) => (StepDodgeTrail)Service(f.Abilities, "StepTrail");
    private static void StartTravel(Fixture f, int frames = 6)
    {
        Input(f, "DodgePressed", true);
        for (int i = 0; i < frames; i++) f.Step();
        Require(f.P.CurrentState == State.StepDodge && Trail(f).IsActive, "Step trail active after actual movement");
    }
    private static AttackHitbox TrailAttack(Fixture f, Vector3 offset)
    {
        var go = f.Box(offset, Vector3.one * 0.04f);
        go.GetComponent<Collider>().isTrigger = true; Physics.SyncTransforms();
        return go.AddComponent<AttackHitbox>();
    }
    private static void RemovedWallState()
    {
        Require(!System.Enum.IsDefined(typeof(State), 15), "Removed WallRun value is not reusable");
        Require((int)State.WallKick == 16 && (int)State.LedgeHang == 20 && (int)State.LedgeJump == 21,
            "Serialized wall kick, bar and ledge values stay stable");
    }
    private static void MicroDodgeChain()
    {
        using (var f = new Fixture(true))
        {
            Input(f, "LeanSide", -1); Input(f, "LeanPressed", true); f.Step();
            var first = Attack(); f.P.DamageReceiver.ReceiveHit(first);
            Require(Dodge(f).CanStart && Dodge(f).CooldownRemaining == 0f, "Micro success immediately ready");
            int activation = Dodge(f).Activation;
            Input(f, "LeanSide", 1); Input(f, "LeanPressed", true); f.Step();
            Require(Dodge(f).Activation == activation + 1 && f.P.IsDodging && !Dodge(f).CanStart, "Fresh E opens a new window immediately");
            Require(f.P.DamageReceiver.ReceiveHit(first) == DamageResult.Duplicate && !Dodge(f).CanStart, "Old swing cannot reward new dodge");
            Require(f.P.DamageReceiver.ReceiveHit(Attack()) == DamageResult.Evaded && Dodge(f).CanStart, "New attack can reward new micro dodge");
            f.Step(); Require(Dodge(f).Activation == activation + 1, "Holding lean does not auto restart");
        }
    }
    private static void TrailMelee()
    {
        using (var f = new Fixture(true))
        {
            StartTravel(f);
            var attack = TrailAttack(f, new Vector3(0.4f, 1f, -0.5f));
            Require(Vector3.Distance(attack.transform.position, f.P.transform.position + Vector3.up) > 1f, "Melee misses the current body");
            int rewards = 0; f.P.DamageReceiver.HitResolved += (hit, result) => { if (result == DamageResult.Evaded) rewards++; };
            attack.BeginAttack(); attack.CheckDodgeTrails(); attack.CheckDodgeTrails();
            Require(Dodge(f).CanStart && rewards == 1, "Swept enlarged trail awards exactly one success");
            Near(f.P.DamageReceiver.Health, 100f, "Reward volume never takes damage");
        }
    }
    private static void StepDodgeChain()
    {
        using (var f = new Fixture(true))
        {
            StartTravel(f, 3);
            var attack = TrailAttack(f, new Vector3(0f, 1f, 0f));
            attack.BeginAttack(); attack.CheckDodgeTrails();
            int activation = Dodge(f).Activation; Vector3 start = f.P.transform.position;
            f.MoveInput(Vector2.right); Input(f, "DodgePressed", true); f.Step();
            Require(Dodge(f).Activation == activation + 1 && f.P.CurrentState == State.StepDodge, "Successful Alt restarts during old step");
            Require(f.P.transform.position.x > start.x + 0.25f, "New step uses new direction immediately");
            attack.transform.position = start + Vector3.up; Physics.SyncTransforms(); attack.CheckDodgeTrails();
            Require(!Dodge(f).CanStart, "Same active swing cannot refund the new step");
            for (int i = 0; i < 6; i++) f.Step();
            Near(f.P.transform.position.x - start.x, 2f, "Chained step travels the full configured distance", 0.02f);
            f.Step(); Require(f.P.HorizontalVelocity.magnitude <= f.P.WalkSpeed * f.P.StrafeCompensation + 0.1f, "Burst speed is not retained as walking momentum: "
                + f.P.HorizontalVelocity.magnitude + " walk=" + f.P.WalkSpeed + " state=" + f.P.CurrentState);
        }
    }
    private static void CrossDodgeChain()
    {
        using (var f = new Fixture(true))
        {
            StartTravel(f, 2); f.P.DamageReceiver.ReceiveHit(Attack());
            Input(f, "LeanSide", -1); Input(f, "LeanPressed", true); f.Step();
            Require(f.P.CurrentState != State.StepDodge && Dodge(f).Kind == DodgeKind.Micro && f.P.IsDodging, "Success lets lean replace unfinished step");
            Require(!Trail(f).IsActive, "Micro has no lingering enlarged step volume");
            f.P.DamageReceiver.ReceiveHit(Attack()); Input(f, "DodgePressed", true); f.Step();
            Require(f.P.CurrentState == State.StepDodge && Trail(f).IsActive, "Micro success can chain back into step");
        }
    }
    private static void TrailLifetime()
    {
        using (var f = new Fixture(true))
        {
            StartTravel(f);
            var attack = TrailAttack(f, new Vector3(0f, 1f, -1f));
            attack.CheckDodgeTrails(); Require(!Dodge(f).CanStart, "Inactive attack cannot reward");
            attack.transform.position = f.Origin + new Vector3(0.6f, 1f, -1f); Physics.SyncTransforms();
            attack.BeginAttack(); attack.CheckDodgeTrails(); Require(!Dodge(f).CanStart, "Outside padding never rewards");
            attack.EndAttack();
            for (int i = 0; i < 6; i++) f.Step();
            attack.transform.position = f.Origin + Vector3.up; Physics.SyncTransforms();
            attack.BeginAttack(); attack.CheckDodgeTrails();
            Require(!Trail(f).IsActive && !Dodge(f).CanStart, "Expired trail cannot refund failed dodge cooldown");
            Near(f.P.DamageReceiver.Health, 100f, "Stale path never damages player");
        }
    }
    private static void TrailWallStop()
    {
        using (var f = new Fixture(true))
        {
            f.Box(new Vector3(0f, 1f, -1f), new Vector3(4f, 2f, 0.08f));
            StartTravel(f, 1);
            Require(!Trail(f).IntersectsSegment(f.Origin + new Vector3(-1f, 1f, -2f),
                f.Origin + new Vector3(1f, 1f, -2f)), "Requested destination behind wall is not part of actual trail");
            f.Step(); f.Step(); f.Step();
            Require(!Trail(f).IsActive, "Wall cancellation removes the recognition trail");
        }
    }
    private static void TrailHitscan()
    {
        foreach (bool covered in new[] { false, true })
        using (var f = new Fixture(true))
        {
            StartTravel(f);
            if (covered) f.Box(new Vector3(-1f, 1f, 0f), new Vector3(0.2f, 3f, 2f));
            var hit = Attack();
            AttackTrace.Raycast(hit, f.Origin + new Vector3(-3f, 1f, 0f), Vector3.right, 6f);
            Require(Dodge(f).CanStart == !covered, "Hitscan trail respects solid cover: " + covered);
            Near(f.P.DamageReceiver.Health, 100f, "Hitscan near miss only rewards");
            if (!covered)
            {
                Input(f, "DodgePressed", true); f.Step();
                AttackTrace.Raycast(hit, f.P.transform.position + new Vector3(-3f, 1f, 0.2f), Vector3.right, 6f);
                Require(!Dodge(f).CanStart, "Same ray attack id cannot refund a second dodge");
            }
        }
    }
    private static void TrailGeometry()
    {
        foreach (Vector2 direction in new[] { Vector2.down, new Vector2(1f, 1f).normalized })
        using (var f = new Fixture(true))
        {
            f.MoveInput(direction); StartTravel(f);
            Vector3 along = new Vector3(direction.x, 0f, direction.y);
            Vector3 side = Vector3.Cross(Vector3.up, along);
            Vector3 middle = f.Origin + along * 1.3f + Vector3.up;
            Require(Trail(f).IntersectsSegment(middle - side * 2f, middle + side * 2f), "Ray crosses swept body");
            Require(Trail(f).IntersectsSegment(middle + side * 0.4f, middle + side * 0.4f + along * 0.1f), "Padding grazes parallel segment");
            Require(!Trail(f).IntersectsSegment(middle + side * 0.6f, middle + side * 0.6f + along * 0.1f), "Outside padding rejected");
            Require(!Trail(f).IntersectsSegment(middle + Vector3.up * 2f - side, middle + Vector3.up * 2f + side), "Ray above head rejected");
            Require(Trail(f).IntersectsSegment(f.Origin + Vector3.up, f.Origin + Vector3.up), "Original body remains in trail");
        }
    }
    private static void TrailReset()
    {
        using (var f = new Fixture(true))
        {
            StartTravel(f); f.P.ResetVelocity();
            Require(!Trail(f).IsActive, "Reset clears reward volume");
            Require(f.P.DamageReceiver.ReceiveHit(Attack()) == DamageResult.Damaged, "Normal body remains damageable after dodge");
            Near(f.P.DamageReceiver.Health, 90f, "Normal hit deals original damage");
        }
    }
}
