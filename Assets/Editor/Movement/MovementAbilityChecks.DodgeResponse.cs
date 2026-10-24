using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static partial class MovementAbilityChecks
{
    [MenuItem("Tools/Movement/Run Dodge Response Checks")]
    public static void RunDodgeResponseChecks()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new System.InvalidOperationException("Exit Play Mode first.");
        Scene previous = SceneManager.GetActiveScene();
        if (Application.isBatchMode && string.IsNullOrEmpty(previous.path)) EditorSceneManager.SaveScene(previous, "Assets/MovementCheckHost.unity");
        Scene test = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        float scale = Time.timeScale;
        try
        {
            Time.timeScale = 1f;
            SharedDodge(); MicroDodge(); MicroWall(); MicroDodgeChain(); CrossDodgeChain();
            MicroSuccessTiming(); DodgeRecognitionWindows(); ReleasedMicroCannotSucceed();
            Debug.Log("DODGE_RESPONSE_CHECKS_PASSED: 8 groups");
        }
        finally
        {
            Time.timeScale = scale;
            if (previous.IsValid()) SceneManager.SetActiveScene(previous);
            EditorSceneManager.CloseScene(test, true);
        }
    }

    private static void MicroSuccessTiming()
    {
        foreach (int hz in new[] { 30, 60, 120 })
        foreach (float scale in new[] { 1f, .08f })
        foreach (int side in new[] { -1, 1 })
        using (var f = new Fixture(true))
        {
            Time.timeScale = 1f;
            Input(f, "LeanSide", side); Input(f, "LeanPressed", true); f.Step(.01f);
            Require(Mathf.Abs(f.P.LeanRoll) < f.P.AbilitySettings.leanAngle * .5f,
                "Ordinary lean still eases in before a confirmed dodge");
            Vector3 before = f.P.transform.position;
            float initialRoll = f.P.LeanRoll, initialOffset = f.P.LeanOffset;
            Require(f.P.DamageReceiver.ReceiveHit(Attack()) == DamageResult.Evaded, "Micro success is confirmed by an actual attack");
            Require(Dodge(f).HasSucceeded, "A real intercepted attack marks the activation successful");
            Near(f.P.LeanRoll, initialRoll, "The success callback does not teleport camera roll", .001f);
            Near(f.P.LeanOffset, initialOffset, "The success callback does not teleport the camera sideways", .001f);
            Near(f.P.transform.position.x, before.x, "The success callback does not teleport the body", .001f);
            // Releasing after success lets the short reaction complete. A real pause freezes it.
            Input(f, "LeanSide", 0);
            Time.timeScale = 0f;
            Call(Service(f.Abilities, "Lean"), "Tick", .02f);
            Near(f.P.transform.position.x, before.x, "Paused micro reaction does not move", .001f);
            Near(f.P.LeanRoll, initialRoll, "Paused micro reaction does not advance camera roll", .001f);
            Near(f.P.LeanOffset, initialOffset, "Paused micro reaction does not advance camera offset", .001f);
            Time.timeScale = scale;
            int frames = Mathf.CeilToInt(f.P.AbilitySettings.microSuccessDuration * hz);
            float previousRoll = Mathf.Abs(initialRoll), previousTravel = 0f;
            for (int i = 0; i < frames; i++)
            {
                f.Step(scale / hz);
                float roll = Mathf.Abs(f.P.LeanRoll);
                float travel = side * (f.P.transform.position.x - before.x);
                Require(roll >= previousRoll - .001f && roll <= f.P.AbilitySettings.leanAngle + .001f,
                    "Successful lean progresses continuously without reversing or overshooting");
                Require(travel >= previousTravel - .001f && travel <= f.P.AbilitySettings.microSuccessDistance + .006f,
                    "Successful body movement progresses in the chosen direction without overshooting");
                if (i == 0)
                {
                    Require(roll > Mathf.Abs(initialRoll) && roll < f.P.AbilitySettings.leanAngle,
                        "The first visible reaction frame is between the starting and full lean");
                    Require(Mathf.Abs(f.P.LeanOffset) > Mathf.Abs(initialOffset)
                        && Mathf.Abs(f.P.LeanOffset) < f.P.AbilitySettings.leanOffset,
                        "Camera offset also moves through an intermediate pose");
                    float constantSpeedSlice = f.P.AbilitySettings.microSuccessDistance /
                        (f.P.AbilitySettings.microSuccessDuration * hz);
                    Require(travel > 0f && travel < constantSpeedSlice,
                        "The body accelerates into the reaction instead of immediately taking a full-speed step");
                }
                previousRoll = roll; previousTravel = travel;
            }
            Near(f.P.LeanRoll, -side * f.P.AbilitySettings.leanAngle, "The smooth reaction reaches full roll", .001f);
            Near(f.P.LeanOffset, side * f.P.AbilitySettings.leanOffset, "The smooth reaction reaches full offset", .001f);
            Near(f.P.transform.position.x - before.x, side * f.P.AbilitySettings.microSuccessDistance,
                $"Micro reaction completes in real time at {hz}Hz / scale {scale}", .006f);
            Require(Time.timeScale == scale, "Micro reaction never changes world slow motion");
            float finished = f.P.transform.position.x;
            for (int i = 0; i < 10; i++) f.Step(scale / hz);
            Near(f.P.transform.position.x, finished, "Successful movement is not repeated", .006f);
            Require(Mathf.Abs(f.P.LeanRoll) < f.P.AbilitySettings.leanAngle,
                "Released lean returns normally after the confirmed reaction completes");
            Time.timeScale = 1f;
        }
    }

    private static void DodgeRecognitionWindows()
    {
        foreach (int hz in new[] { 30, 60, 120 })
        foreach (DodgeKind kind in new[] { DodgeKind.Step, DodgeKind.Micro })
        foreach (float hitDelay in new[] { .04f, .07f, .09f, .20f })
        using (var f = new Fixture(true))
        {
            Near(f.P.AbilitySettings.stepInvulnerability, .18f, "Step dodge uses the original recognition window", .0001f);
            Near(f.P.AbilitySettings.microInvulnerability, .08f, "Micro dodge only recognizes attacks shortly after input", .0001f);
            if (kind == DodgeKind.Step) Input(f, "DodgePressed", true);
            else { Input(f, "LeanSide", -1); Input(f, "LeanPressed", true); }
            f.Step(1f / hz);
            for (float elapsed = 0f; elapsed < hitDelay;)
            {
                float delta = Mathf.Min(1f / hz, hitDelay - elapsed);
                Dodge(f).Tick(delta); elapsed += delta;
            }
            float expectedWindow = kind == DodgeKind.Step ? .18f : .08f;
            DamageResult expected = hitDelay < expectedWindow ? DamageResult.Evaded : DamageResult.Damaged;
            Require(f.P.DamageReceiver.ReceiveHit(Attack()) == expected,
                $"{kind} at {hz}Hz recognizes an in-window hit and rejects late success ({hitDelay:0.00}s)");
            Dodge(f).Tick(.25f);
            Require(f.P.DamageReceiver.ReceiveHit(Attack()) == DamageResult.Damaged,
                "Dodge immunity still expires after a successful interception");
        }
    }

    private static void ReleasedMicroCannotSucceed()
    {
        foreach (int hz in new[] { 30, 60, 120 })
        foreach (int nextSide in new[] { 0, 1 })
        using (var f = new Fixture(true))
        {
            Input(f, "LeanSide", -1); Input(f, "LeanPressed", true); f.Step(1f / hz);
            Require(Dodge(f).IsInvulnerable && !Dodge(f).HasSucceeded, "Fresh Q opens an unconfirmed micro window");
            int activation = Dodge(f).Activation;
            Vector3 before = f.P.transform.position;
            Input(f, "LeanSide", nextSide); Input(f, "LeanPressed", nextSide != 0); f.Step(1f / hz);
            Require(!Dodge(f).IsInvulnerable && !Dodge(f).HasSucceeded,
                "Releasing or reversing an unconfirmed lean closes its still-young defensive window");
            Require(Dodge(f).Activation == activation && Dodge(f).CooldownRemaining > 0f && !Dodge(f).CanStart,
                "Cancelling an unconfirmed lean does not refund cooldown or open another activation");
            float timeScale = Time.timeScale;
            Require(f.P.DamageReceiver.ReceiveHit(Attack()) == DamageResult.Damaged,
                "An attack while returning from or reversing Q cannot trigger late micro success");
            Require(Time.timeScale == timeScale && (f.P.PerfectDefense == null || !f.P.PerfectDefense.CounterAvailable),
                "A cancelled lean cannot trigger perfect-defense slowdown or a counter opportunity");
            for (int i = 0; i < 10; i++) f.Step(1f / hz);
            Near(f.P.transform.position.x, before.x, "Cancelled lean never launches a success step", .006f);
        }
    }
}
