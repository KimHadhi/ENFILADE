using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using Object = UnityEngine.Object;
using State = PlayerMovement.MovementState;

[InitializeOnLoad]
public static class SlideCombatPlayChecks
{
    private const string Pending = "Morphang.SlideCombatPlayChecks";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static SlideCombatPlayChecks() => EditorApplication.playModeStateChanged += StateChanged;
    public static void RunBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use an isolated Unity batch project.");
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        // Headless play mode has no focused/locked game window; validate input arbitration in edit mode.
        var inputFixture = new GameObject("Slide input fixture"); inputFixture.layer = 31;
        var inputPlayer = inputFixture.AddComponent<PlayerMovement>(); Call(inputPlayer, "Awake");
        try { CheckInput(inputPlayer, (MovementAbilities)Get(inputPlayer, "Abilities")); }
        finally { Object.DestroyImmediate(inputFixture); }
        SessionState.SetBool(Pending, true); EditorApplication.isPlaying = true;
    }
    private static void StateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
        SessionState.SetBool(Pending, false);
        try { Check(); Debug.Log("SLIDE_COMBAT_PLAY_CHECKS_PASSED"); EditorApplication.Exit(0); }
        catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
    }
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    private static object Get(object owner, string property) => owner.GetType().GetProperty(property, Any).GetValue(owner);
    private static void Call(object owner, string method, params object[] args) => owner.GetType().GetMethod(method, Any).Invoke(owner, args);
    private static GameObject Box(string name, Vector3 position, Vector3 size)
    {
        var go = new GameObject(name); go.transform.position = position; go.AddComponent<BoxCollider>().size = size; return go;
    }
    private static void Check()
    {
        Physics.simulationMode = SimulationMode.Script;
        Box("Floor", new Vector3(0,-.1f,0), new Vector3(50,.2f,50));
        var go = new GameObject("Slide player"); go.layer = 31;
        var body = go.AddComponent<CharacterController>(); body.height = 2f; body.radius = .3f; body.skinWidth = .03f;
        var eye = new GameObject("Camera"); eye.transform.SetParent(go.transform, false); eye.AddComponent<Camera>();
        var player = go.AddComponent<PlayerMovement>();
        var abilities = (MovementAbilities)Get(player, "Abilities");
        var motor = Get(player, "Motor");
        foreach (float dt in new[] { 1f/30f, 1f/60f, 1f/120f })
        foreach (EnemyBodySize size in Enum.GetValues(typeof(EnemyBodySize)))
        foreach (bool perfect in new[] { false, true })
            CheckReaction(player, abilities, motor, size, perfect, dt);

        Start(player);
        Require(abilities.Slide.TryBoost(), "Slide boost starts");
        Require(Mathf.Abs(player.CurrentSlideSpeed - 11.5f) < .001f, "Alt gives a small speed boost");
        Call(abilities.Slide, "Tick", .179f);
        Require(abilities.Slide.IsJustSlide, "Window remains active at .179 seconds");
        Call(abilities.Slide, "Tick", .001f);
        Require(!abilities.Slide.IsJustSlide, "Window ends at .18 seconds");
        Require(!abilities.Slide.TryBoost(), "Repeated input cannot stack acceleration during cooldown");
        Call(abilities.Slide, "Tick", .4f);
        Require(abilities.Slide.TryBoost(), "Boost becomes available after cooldown");
        player.ChangeState(State.Standing);
        Require(!abilities.Slide.IsJustSlide && !abilities.Slide.TryBoost(), "Leaving slide cancels the window");

        CheckWall(player, abilities, motor, false);
        CheckWall(player, abilities, motor, true);
        CheckOverlapExpiry(player, abilities, motor);
        RagdollKnockdownChecks.Run();
        Debug.Log("PASS SLIDE: six size/timing reactions at 30/60/120Hz, wall occlusion, expiry, input, cleanup");
    }
    private static void Start(PlayerMovement player)
    {
        player.ResetVelocity(); player.ChangeState(State.Standing);
        player.transform.position = Vector3.up * .05f;
        player.ChangeState(State.Sliding);
        player.CurrentSlideSpeed = 10f; player.SlideDirection = Vector3.forward;
        player.HorizontalVelocity = Vector3.forward * 10f; player.VerticalVelocity = -2f;
        player.UpdateHeightsAndTransform(1f); Physics.SyncTransforms();
        Call(Get(player, "Motor"), "CheckGroundedAndSlope");
    }
    private static void CheckReaction(PlayerMovement player, MovementAbilities abilities, object motor,
        EnemyBodySize size, bool perfect, float dt)
    {
        var enemy = Box("Slide " + size, new Vector3(0,1,1.4f), new Vector3(.8f,2,.8f));
        var health = enemy.AddComponent<EnemyHealth>();
        var target = enemy.GetComponent<MeleeCombatTarget>();
        typeof(MeleeCombatTarget).GetField("bodySize", Any).SetValue(target, size);
        var child = Box("Second collider", enemy.transform.position, new Vector3(.7f,1.8f,.82f)); child.transform.SetParent(enemy.transform);
        var rb = enemy.AddComponent<Rigidbody>(); rb.useGravity = false; rb.constraints = RigidbodyConstraints.FreezePositionY | RigidbodyConstraints.FreezeRotation;
        Start(player);
        if (perfect) Require(abilities.Slide.TryBoost(), "Boost starts for " + size);
        float speed = player.CurrentSlideSpeed;
        for (int i = 0; i < Mathf.CeilToInt(.4f / dt); i++)
        {
            Call(motor, "Move", dt);
            Call(abilities.Slide, "Tick", dt);
            Call(motor, "CheckGroundedAndSlope");
        }
        bool pass = perfect || size == EnemyBodySize.Small;
        Physics.Simulate(.001f); // Flush AddForce before checking the rigidbody's response.
        string label = size + "/" + perfect + "/" + dt;
        Require(Mathf.Abs(health.Health - (100f-player.AbilitySettings.slideDamage)) < .001f, "Exactly one slide hit: " + label);
        Require(target.IsKnockedDown == (size == EnemyBodySize.Small || (perfect && size == EnemyBodySize.Medium)), "Knockdown rule: " + label);
        if (target.IsKnockedDown)
            Require(!rb.isKinematic && rb.useGravity && rb.angularVelocity.magnitude > .1f, "Knockdown activates body physics: " + label);
        else Require((rb.linearVelocity.z > .1f) == (!perfect && size == EnemyBodySize.Medium), "Only ordinary medium contact pushes: " + label);
        if (pass)
        {
            Require(player.transform.position.z > 2.1f, "Actually crosses all enemy colliders: " + label + " pos=" + player.transform.position);
            float expected = perfect ? speed : speed * player.AbilitySettings.slideSmallSpeedRetention;
            Require(Mathf.Abs(player.CurrentSlideSpeed - expected) < .01f, "Contact speed retention: " + label);
        }
        else Require(player.HorizontalVelocity.z < 0f && player.transform.position.z < .8f, "Wall-style bounce: " + label);
        Require(!player.IsDodging && player.CurrentState != State.Tackle, "Slide never starts dodge immunity or tackle");
        Require(player.DodgePassThrough.IgnoredCount == 0, "Collision pairs restored after exit: " + label);
        if (target.IsKnockedDown)
        {
            target.Tick(player.AbilitySettings.slideKnockdownDuration + .01f);
            Require(!target.IsKnockedDown && !target.IsStunned, "Enemy recovers from knockdown");
        }
        player.ResetVelocity(); Object.DestroyImmediate(enemy);
    }
    private static void CheckWall(PlayerMovement player, MovementAbilities abilities, object motor, bool perfect)
    {
        var wall = Box("Solid wall", new Vector3(0,1,1f), new Vector3(5,3,.2f));
        var enemy = Box("Enemy behind wall", new Vector3(0,1,1.7f), new Vector3(.8f,2,.8f));
        var health = enemy.AddComponent<EnemyHealth>();
        Start(player); if (perfect) abilities.Slide.TryBoost();
        Call(motor, "Move", .3f);
        Require(player.transform.position.z < .7f && player.HorizontalVelocity.z < 0f, "World wall still bounces during just slide=" + perfect);
        Require(health.Health == 100f, "Sweep cannot damage enemies through a wall");
        player.ResetVelocity(); Object.DestroyImmediate(wall); Object.DestroyImmediate(enemy);
    }
    private static void CheckOverlapExpiry(PlayerMovement player, MovementAbilities abilities, object motor)
    {
        var enemy = Box("Wide large enemy", new Vector3(0,1,1.6f), new Vector3(.8f,2,1.8f));
        enemy.AddComponent<EnemyHealth>();
        typeof(MeleeCombatTarget).GetField("bodySize", Any).SetValue(enemy.GetComponent<MeleeCombatTarget>(), EnemyBodySize.Large);
        Start(player); abilities.Slide.TryBoost(); Call(motor, "Move", .1f);
        Require(player.DodgePassThrough.HasEnemyOverlap, "Expiry fixture is inside the enemy");
        Call(abilities.Slide, "Tick", .18f);
        Call(motor, "Move", .2f);
        Require(player.transform.position.z > 2.8f && player.DodgePassThrough.IgnoredCount == 0, "Successful pass survives window expiry and restores pairs");
        Start(player); abilities.Slide.TryBoost(); Call(motor, "Move", .1f);
        player.ChangeState(State.Standing); player.DodgePassThrough.BeforeMove();
        Require(!player.DodgePassThrough.HasEnemyOverlap && player.DodgePassThrough.IgnoredCount == 0, "Interrupted slide resolves overlap");
        Object.DestroyImmediate(enemy);
    }
    private static void CheckInput(PlayerMovement player, MovementAbilities abilities)
    {
        var asset = AssetDatabase.LoadAssetAtPath<InputActionAsset>("Assets/_Project/Input/Player.inputactions");
        var dodge = asset.FindAction("Player/Dodge");
        bool altBound = false;
        foreach (var binding in dodge.bindings) altBound |= binding.effectivePath == "<Keyboard>/leftAlt";
        Require(altBound, "Dodge is bound to Alt");
        Start(player);
        // Supply the sampled press, as the movement checks do; editor/headless input has a separate update clock.
        typeof(MovementInput).GetProperty("DodgePressed", Any).SetValue(Get(player, "Input"), true);
        Call(abilities, "ProcessInput");
        Require(abilities.Slide.IsJustSlide && player.CurrentState == State.Sliding, "Dodge press boosts the active slide");
    }
}
