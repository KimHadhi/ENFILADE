using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Runs in the disposable validation project: actual Play Mode -> assembly reload -> resume.
[InitializeOnLoad]
public static class RuntimeReloadChecks
{
    private const string Key = "Morphang.ReloadCheck.";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static int ticks;

    static RuntimeReloadChecks()
    {
        if (!SessionState.GetBool(Key + "active", false)) return;
        if (SessionState.GetInt(Key + "phase", 0) == 2) SessionState.SetBool(Key + "reloaded", true);
        EditorApplication.update += Tick;
    }

    public static void RunBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Use the separate batch validation project.");
        SessionState.SetBool(Key + "active", true);
        SessionState.SetBool(Key + "reloaded", false);
        SessionState.SetInt(Key + "phase", 1);
        SessionState.SetString(Key + "deadline", DateTime.UtcNow.AddMinutes(2).Ticks.ToString());
        EditorSceneManager.OpenScene("Assets/_Project/Scenes/Development/CombatFoundation.unity");
        EditorApplication.update += Tick;
        EditorApplication.EnterPlaymode();
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException("Reload check: " + message); }

    private static void Tick()
    {
        try
        {
            if (DateTime.UtcNow.Ticks > long.Parse(SessionState.GetString(Key + "deadline", "0")))
                throw new TimeoutException("Play Mode reload check timed out.");
            int phase = SessionState.GetInt(Key + "phase", 0);
            if (phase == 3)
            {
                if (!EditorApplication.isPlayingOrWillChangePlaymode) Finish(0);
                return;
            }
            if (!EditorApplication.isPlaying || EditorApplication.isCompiling || ++ticks < 30) return;
            var player = UnityEngine.Object.FindFirstObjectByType<PlayerMovement>();
            Require(player != null && player.IsInitialized, "movement services exist");
            var weapon = player.GetComponent<WeaponController>();
            if (phase == 1)
            {
                player.DamageReceiver.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), 20f, DamageKind.Fall, player.transform.position, Vector3.down));
                player.ChangeState(PlayerMovement.MovementState.Crouching);
                player.UpdateHeightsAndTransform(0f);
                var turret = GameObject.CreatePrimitive(PrimitiveType.Cube);
                turret.name = "Reload test attacker"; turret.transform.position = player.transform.position + Vector3.right * 50f;
                turret.AddComponent<MovementTrainingAttacker>();
                weapon.Runtime.Tick(0.02f, new WeaponInputFrame(true, true));
                SessionState.SetInt(Key + "ammo", weapon.Runtime.Ammo);
                SessionState.SetFloat(Key + "standingHeight", player.StandingColliderHeight);
                SessionState.SetInt(Key + "phase", 2);
                EditorUtility.RequestScriptReload();
            }
            else if (phase == 2 && SessionState.GetBool(Key + "reloaded", false))
            {
                Require(Mathf.Abs(player.DamageReceiver.Health - 80f) < 0.01f, "health is preserved, not healed or zeroed");
                Require(Mathf.Abs(player.StandingColliderHeight - SessionState.GetFloat(Key + "standingHeight", 0f)) < 0.01f,
                    "crouched native capsule did not replace the original standing height");
                Require(weapon.Runtime != null && weapon.Runtime.Ammo == SessionState.GetInt(Key + "ammo", -1), "weapon and ammo survived reload");
                var turret = UnityEngine.Object.FindFirstObjectByType<MovementTrainingAttacker>();
                Require(turret != null && typeof(MovementTrainingAttacker).GetField("color", Any).GetValue(turret) != null,
                    "attacker presentation object was recreated");
                var effects = player.GetComponent<PlayerCameraEffects>();
                typeof(PlayerCameraEffects).GetMethod("LateUpdate", Any).Invoke(effects, null);
                Require(!float.IsNaN(player.Velocity.sqrMagnitude), "movement and camera are readable after reload");
                Debug.Log("RUNTIME_RELOAD_CHECKS_PASSED: actual Play Mode assembly reload; movement, pose, health, weapon, attacker, camera");
                SessionState.SetInt(Key + "phase", 3);
                EditorApplication.ExitPlaymode();
            }
        }
        catch (Exception exception) { Debug.LogException(exception); Finish(1); }
    }

    private static void Finish(int code)
    {
        SessionState.SetBool(Key + "active", false);
        EditorApplication.update -= Tick;
        EditorApplication.Exit(code);
    }
}
