using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

[InitializeOnLoad]
public static class PerfectDefensePlayChecks
{
    private const string Pending = "Morphang.PerfectDefensePlayChecks";
    private static NavMeshData data;
    private static NavMeshDataInstance nav;
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static PerfectDefensePlayChecks() => EditorApplication.playModeStateChanged += StateChanged;
    public static void RunBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated Unity batch project.");
        WeaponSlotsMeleeChecks.RunBatch();
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        SessionState.SetBool(Pending, true); EditorApplication.isPlaying = true;
    }
    private static void StateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
        SessionState.SetBool(Pending, false);
        try { Check(); Debug.Log("PERFECT_DEFENSE_PLAY_CHECKS_PASSED"); Finish(0); }
        catch (Exception error) { Debug.LogException(error); Finish(1); }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static GameObject Box(string name, Vector3 position, Vector3 size)
    {
        var go = new GameObject(name); go.transform.position = position; go.AddComponent<BoxCollider>().size = size; return go;
    }
    private static void Check()
    {
        Vector3 origin = new Vector3(1000,0,1000);
        Box("Floor", origin + Vector3.down * .1f, new Vector3(20,.2f,20));
        var go = new GameObject("Player"); go.layer = 31; go.transform.position = origin + Vector3.up;
        var cc = go.AddComponent<CharacterController>(); cc.height = 2; cc.radius = .3f; cc.skinWidth = .03f;
        var eye = new GameObject("Camera"); eye.transform.SetParent(go.transform,false); eye.AddComponent<Camera>();
        var player = go.AddComponent<PlayerMovement>();
        var pass = player.DodgePassThrough;
        var enemy = Box("Tagged enemy", origin + new Vector3(0,1,1), new Vector3(.8f,2,.8f)); enemy.tag = "Enemy";
        var enemyBody = enemy.GetComponent<Collider>();
        var sibling = Box("Enemy child body", origin + new Vector3(.2f,1,1), new Vector3(.2f,1,.4f)); sibling.transform.SetParent(enemy.transform);
        Physics.SyncTransforms();
        cc.Move(Vector3.forward * 2f);
        Require(go.transform.position.z < origin.z + .8f, "Enemy normally blocks the player");
        go.transform.position = origin + Vector3.up; Physics.SyncTransforms();
        Physics.IgnoreCollision(cc, sibling.GetComponent<Collider>(), true);
        player.ChangeState(PlayerMovement.MovementState.StepDodge); pass.BeforeMove();
        Require(Physics.GetIgnoreCollision(cc, enemyBody) && Physics.GetIgnoreCollision(cc, sibling.GetComponent<Collider>()), "Dodge ignores the Enemy tag on root and child colliders");
        cc.Move(Vector3.forward * 2f); pass.AfterMove();
        Require(go.transform.position.z > origin.z + 1.8f, "Dodge crosses the enemy instead of stopping");
        player.ChangeState(PlayerMovement.MovementState.Standing);
        Require(!Physics.GetIgnoreCollision(cc, enemyBody) && pass.IgnoredCount == 0, "All pairs restored after a clear exit");
        Require(Physics.GetIgnoreCollision(cc, sibling.GetComponent<Collider>()), "Pre-existing ignored pairs retain their original state");
        Physics.IgnoreCollision(cc, sibling.GetComponent<Collider>(), false);
        // Exercise the actual state + motor path, including ground and posture queries.
        // Movement initialization uses a feet-based pivot (controller.center.y = height / 2).
        go.transform.position = origin + Vector3.up; Physics.SyncTransforms(); cc.Move(Vector3.down * 1.2f);
        var motor = typeof(PlayerMovement).GetProperty("Motor", Any).GetValue(player);
        var abilities = (MovementAbilities)typeof(PlayerMovement).GetProperty("Abilities", Any).GetValue(player);
        typeof(MovementAbilities).GetField("ActionDirection", Any).SetValue(abilities, Vector3.forward);
        var machine = (MovementStateMachine)typeof(PlayerMovement).GetProperty("StateMachine", Any).GetValue(player);
        typeof(MovementMotor).GetMethod("CheckGroundedAndSlope", Any).Invoke(motor, null);
        player.ChangeState(PlayerMovement.MovementState.StepDodge);
        for (int i = 0; i < 6; i++)
        {
            machine.Current.UpdateLogic(1f/30f); machine.Current.UpdatePhysics(1f/30f);
            typeof(MovementMotor).GetMethod("Move", Any).Invoke(motor,new object[]{1f/30f});
            typeof(MovementMotor).GetMethod("CheckGroundedAndSlope", Any).Invoke(motor,null);
        }
        Require(go.transform.position.z > origin.z + 1.8f && player.CurrentState != PlayerMovement.MovementState.StepDodge
            && !Physics.GetIgnoreCollision(cc,enemyBody), $"Full movement state/motor: pos={go.transform.position-origin}, state={player.CurrentState}, grounded={player.IsGrounded}, ignored={pass.IgnoredCount}");
        go.transform.position = origin + Vector3.up; Physics.SyncTransforms();
        var wall = Box("Wall beside landing", origin + new Vector3(.9f,1,1), new Vector3(.2f,3,4));
        player.ChangeState(PlayerMovement.MovementState.StepDodge); pass.BeforeMove();
        cc.Move(Vector3.forward); Physics.SyncTransforms();
        Require(Physics.ComputePenetration(cc, go.transform.position, go.transform.rotation, enemyBody, enemy.transform.position, enemy.transform.rotation,
            out _, out float overlap) && overlap > .1f, "Fixture ends inside an enemy body");
        player.ChangeState(PlayerMovement.MovementState.Standing);
        Require(pass.IgnoredCount == 0 && !Physics.GetIgnoreCollision(cc, enemyBody), "Overlapping exit finds a safe position and restores collision");
        Require(go.transform.position.z > origin.z + 1.7f, "Overlapping exit goes through the enemy instead of back to the entry side");
        Require(!Physics.ComputePenetration(cc, go.transform.position, go.transform.rotation, enemyBody, enemy.transform.position, enemy.transform.rotation,
            out _, out float remainingOverlap) || remainingOverlap <= cc.skinWidth + .025f, "Enemy overlap is resolved within controller skin tolerance");
        Require(go.transform.position.x < origin.x + .6f, "Overlap resolution never crosses the adjacent wall");
        // A genuine wall still blocks movement while enemy collision is disabled.
        var forwardWall = Box("Forward wall", origin + new Vector3(0,1,2.5f), new Vector3(5,3,.2f));
        go.transform.position = origin + Vector3.up; Physics.SyncTransforms();
        player.ChangeState(PlayerMovement.MovementState.StepDodge); pass.BeforeMove(); cc.Move(Vector3.forward * 4);
        Require(go.transform.position.z < origin.z + 2.3f, "World collisions remain solid throughout a dodge");
        player.ChangeState(PlayerMovement.MovementState.Standing); pass.Finish();
        Object.DestroyImmediate(enemy); Object.DestroyImmediate(wall); Object.DestroyImmediate(forwardWall);
        CheckDodgeFlow(player, origin);
        CheckSlideImpact(player, origin);
        CheckTime(player);
        CheckEnemyAttack(player, origin);
    }
    private static void CheckDodgeFlow(PlayerMovement player, Vector3 origin)
    {
        var cc = player.GetComponent<CharacterController>();
        var motor = (MovementMotor)typeof(PlayerMovement).GetProperty("Motor", Any).GetValue(player);
        var abilities = (MovementAbilities)typeof(PlayerMovement).GetProperty("Abilities", Any).GetValue(player);
        var machine = (MovementStateMachine)typeof(PlayerMovement).GetProperty("StateMachine", Any).GetValue(player);
        var front = Box("Dodge crowd front", origin + new Vector3(0,1,2f), new Vector3(.8f,2,.8f)); front.tag="Enemy";
        var rear = Box("Dodge crowd rear", origin + new Vector3(0,1,2.7f), new Vector3(.8f,2,.8f)); rear.tag="Enemy";
        try
        {
            foreach (float realDt in new[] { 1f/30f, 1f/60f, 1f/120f })
            {
                player.ResetVelocity(); player.ChangeState(PlayerMovement.MovementState.Standing);
                player.transform.position = origin + Vector3.up * .05f;
                player.UpdateHeightsAndTransform(1f); Physics.SyncTransforms(); cc.Move(Vector3.down * .2f);
                typeof(MovementMotor).GetMethod("CheckGroundedAndSlope",Any).Invoke(motor,null);
                typeof(MovementAbilities).GetField("ActionDirection",Any).SetValue(abilities,Vector3.forward);
                abilities.Dodge.TryStart(DodgeKind.Step,0,.18f,1.25f);
                player.ChangeState(PlayerMovement.MovementState.StepDodge);
                Time.timeScale=.08f;
                bool acceleratedInside=false, reasserted=false;
                for (int i=0; i<Mathf.CeilToInt(3f/realDt) && player.CurrentState==PlayerMovement.MovementState.StepDodge; i++)
                {
                    float dt=realDt*Time.timeScale;
                    bool inside=player.DodgePassThrough.HasEnemyOverlap;
                    Vector3 before=player.transform.position;
                    abilities.Dodge.Tick(dt);
                    machine.Current.UpdateLogic(dt); machine.Current.UpdatePhysics(dt);
                    player.UpdateHeightsAndTransform(dt);
                    if (i==2)
                    {
                        // Simulate a native collider rebuild invalidating one of our pairs.
                        Physics.IgnoreCollision(cc,front.GetComponent<Collider>(),false);
                    }
                    typeof(MovementMotor).GetMethod("Move",Any).Invoke(motor,new object[]{dt});
                    typeof(MovementMotor).GetMethod("CheckGroundedAndSlope",Any).Invoke(motor,null);
                    float advance=player.transform.position.z-before.z;
                    Require(advance>=-.001f, "Crowd traversal cannot bounce backward");
                    if(i==0) Require(advance < .07f, "Slow motion remains active outside enemy bodies");
                    if(i==2) reasserted=Physics.GetIgnoreCollision(cc,front.GetComponent<Collider>());
                    if(inside && advance > player.AbilitySettings.stepDistance/player.AbilitySettings.stepDuration*dt*2f)
                        acceleratedInside=true;
                }
                Require(acceleratedInside && reasserted, "Body overlap alone speeds up travel; rebuilt collision pairs are restored");
                Require(player.CurrentState!=PlayerMovement.MovementState.StepDodge
                    && player.transform.position.z>origin.z+3.44f && !player.DodgePassThrough.HasEnemyOverlap
                    && player.DodgePassThrough.IgnoredCount==0,
                    $"30/60/120Hz slow-motion crowd exit: {player.transform.position-origin}, {player.CurrentState}");
                Require(Time.timeScale==.08f, "Dodge assist never changes world slow motion");
                Time.timeScale=1f;
            }
        }
        finally { Time.timeScale=1f; Object.DestroyImmediate(front); Object.DestroyImmediate(rear); }
        Debug.Log("DODGE_FLOW_PLAY_CHECKS_PASSED");
    }
    private static void CheckSlideImpact(PlayerMovement player, Vector3 origin)
    {
        var body = player.GetComponent<CharacterController>();
        var target = Box("Slide target", origin + new Vector3(0, 1, 1.2f), new Vector3(.8f, 2f, .8f));
        var health = target.AddComponent<EnemyHealth>();
        // Two colliders on one enemy must still produce just one impact.
        var child = Box("Slide target child", target.transform.position, new Vector3(.6f, 1.8f, .85f));
        child.transform.SetParent(target.transform);
        foreach (var state in new[] { PlayerMovement.MovementState.Sliding,
            PlayerMovement.MovementState.HeadFirstSliding, PlayerMovement.MovementState.SupineSliding })
        {
            player.ChangeState(PlayerMovement.MovementState.Standing);
            player.transform.position = origin + Vector3.up * .05f;
            player.ChangeState(state);
            player.CurrentSlideSpeed = 8f; player.SlideDirection = Vector3.forward;
            player.HorizontalVelocity = Vector3.forward * 8f;
            player.UpdateHeightsAndTransform(1f);
            Physics.SyncTransforms();
            float before = health.Health;
            body.Move(Vector3.forward * 1.5f);
            Require(Mathf.Abs(health.Health - (before - player.AbilitySettings.tackleDamage)) < .01f,
                "Native sliding contact deals tackle damage: " + state);
            body.Move(Vector3.forward * .1f);
            player.ChangeState(state == PlayerMovement.MovementState.SupineSliding
                ? PlayerMovement.MovementState.HeadFirstSliding : PlayerMovement.MovementState.SupineSliding);
            body.Move(Vector3.forward * .1f);
            Require(Mathf.Abs(health.Health - (before - player.AbilitySettings.tackleDamage)) < .01f,
                "Repeated contact and slide posture transitions cannot hit the same enemy twice");
            Require(target.GetComponent<MeleeCombatTarget>().IsStunned, "Sliding interrupts the target like tackle");
        }
        player.ChangeState(PlayerMovement.MovementState.Standing);
        player.UpdateHeightsAndTransform(1f);
        player.HorizontalVelocity = Vector3.zero; player.VerticalVelocity = 0f;
        player.GetComponent<CombatTimeEffects>().Release(player);
        Object.DestroyImmediate(target);
        Debug.Log("SLIDE_IMPACT_PLAY_CHECKS_PASSED");
    }
    private static void CheckTime(PlayerMovement player)
    {
        var time = player.GetComponent<CombatTimeEffects>();
        var other = new GameObject("Other time owner");
        float fixedStep = Time.fixedDeltaTime;
        time.RequestSlowdown(player.PerfectDefense, .08f, 1f);
        Require(Mathf.Abs(Time.timeScale - .08f) < .001f, "Perfect defense applies heavy slow motion");
        time.RequestSlowdown(other, .1f, .08f); time.Tick(.1f);
        Require(Mathf.Abs(Time.timeScale - .08f) < .001f, "Tackle expiry cannot overwrite the counter slowdown");
        time.Release(player.PerfectDefense);
        Require(Time.timeScale == 1f && Mathf.Abs(Time.fixedDeltaTime - fixedStep) < .00001f, "Time and physics step restore after counter");
        time.RequestSlowdown(player.PerfectDefense, .08f, 1f); Time.timeScale = 0;
        time.Tick(2f); time.Release(player.PerfectDefense);
        Require(Time.timeScale == 0, "Cancellation cannot unpause the game");
        Time.timeScale = 1; time.Tick(.01f);
        Require(Mathf.Abs(Time.fixedDeltaTime - fixedStep) < .00001f, $"Resuming another time owner restores the physics step: {Time.fixedDeltaTime} expected {fixedStep}");
        Time.timeScale = .5f; Time.fixedDeltaTime = .01f;
        time.RequestSlowdown(player.PerfectDefense, .08f, .2f); time.Tick(.3f);
        Require(Time.timeScale == .5f && Mathf.Abs(Time.fixedDeltaTime - .01f) < .00001f, "Non-default previous game speed is preserved");
        Time.timeScale = 1; Time.fixedDeltaTime = fixedStep;
        Object.DestroyImmediate(other);
    }
    private static void CheckEnemyAttack(PlayerMovement player, Vector3 origin)
    {
        var source = new NavMeshBuildSource { shape=NavMeshBuildSourceShape.Box,
            transform=Matrix4x4.TRS(origin+Vector3.down*.1f,Quaternion.identity,Vector3.one), size=new Vector3(20,.2f,20) };
        data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByIndex(0), new List<NavMeshBuildSource>{source},
            new Bounds(Vector3.zero,new Vector3(22,10,22)), origin, Quaternion.identity); nav=NavMesh.AddNavMeshData(data);
        player.transform.position=origin+Vector3.up; player.DamageReceiver.RestoreHealth();
        var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Enemy/Enemy_Test.prefab");
        var enemy=Object.Instantiate(prefab,origin+new Vector3(0,1,1.3f),Quaternion.identity);
        var ai=enemy.GetComponent<EnemyCombatAI>(); typeof(EnemyCombatAI).GetField("player",Any).SetValue(ai,player);
        Physics.SyncTransforms(); float hp=player.DamageReceiver.Health;
        ai.Tick(0f); Require(!ai.IsPreparingAttack, "Paused time cannot start an enemy cue/attack");
        ai.Tick(.001f);
        Require(ai.IsPreparingAttack && enemy.GetComponent<CombatCueAudio>()?.LastCue==CombatCue.EnemyAttackStart, "First attack starts its cue immediately in range");
        ai.Tick(ai.WindupDuration-.02f);
        Require(player.DamageReceiver.Health==hp,"No impact before the cue completes");
        ai.Tick(.03f);
        Require(player.DamageReceiver.Health<hp && !ai.IsPreparingAttack,"Impact lands at the end of the imported cue");
        var finishers=new List<bool>(); player.DamageReceiver.HitResolved+=(hit,result)=>finishers.Add(hit.IsComboFinisher);
        typeof(EnemyCombatAI).GetField("comboHits",Any).SetValue(ai,2);
        ai.Tick(.56f); ai.Tick(ai.WindupDuration+.01f); ai.Tick(ai.WindupDuration+.01f);
        Require(finishers.Count==2 && !finishers[0] && finishers[1],"A real two-hit combo marks only its last hit as a finisher");
    }
    private static void Finish(int code)
    {
        Time.timeScale=1; Time.fixedDeltaTime=.02f;
        if(nav.valid)NavMesh.RemoveNavMeshData(nav);
        if(data!=null)Object.Destroy(data);
        EditorApplication.Exit(code);
    }
}
