using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Run only in an isolated batch project (-batchmode -executeMethod GunFeelPlayChecks.RunBatch, without -quit).
/// Fires the real CombatFoundation rifle at a spawned Enemy_Test and checks every feel layer over real frames:
/// shot (sound, muzzle, brass, view punch), hit (marker that rides the punch, flash, blood), reaction (slow, posture,
/// headshot interrupt), kill (corpse, XP, kill feed; screen pulse only for executions), and that the camera's
/// post-processing stays as authored.
/// </summary>
[InitializeOnLoad]
public static class GunFeelPlayChecks
{
    private const string Pending = "Morphang.GunFeelPlayChecks";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly List<string> errors = new List<string>();
    private static IEnumerator<float> steps;
    private static float resumeAt;

    static GunFeelPlayChecks() => EditorApplication.playModeStateChanged += StateChanged;

    public static void RunBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run in an isolated Unity batch project.");
        EditorSceneManager.OpenScene("Assets/_Project/Scenes/Development/CombatFoundation.unity");
        SessionState.SetBool(Pending, true); EditorApplication.isPlaying = true;
    }

    private static void StateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
        SessionState.SetBool(Pending, false);
        Application.logMessageReceived += Collect;
        steps = Steps(); resumeAt = 0f;
        EditorApplication.update += Tick;
    }

    private static void Collect(string message, string stack, LogType type)
    {
        // A fresh batch project's Search indexer can throw on startup; that is editor tooling, not the game.
        if (stack != null && stack.Contains("UnityEditor.Search")) return;
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(type + ": " + message);
    }

    private static void Tick()
    {
        if (Time.realtimeSinceStartup < resumeAt) return;
        try
        {
            if (steps.MoveNext()) { resumeAt = Time.realtimeSinceStartup + steps.Current; return; }
            Require(errors.Count == 0, "No errors during the run:\n" + string.Join("\n", errors));
            Debug.Log("GUN_FEEL_PLAY_CHECKS_PASSED");
            Finish(0);
        }
        catch (Exception e) { Debug.LogException(e); Finish(1); }
    }

    private static void Finish(int code)
    {
        EditorApplication.update -= Tick;
        Application.logMessageReceived -= Collect;
        EditorApplication.Exit(code);
    }

    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }

    private static IEnumerator<float> Steps()
    {
        Require(FeelAssets.OverlayShader != null && FeelAssets.OverlayShader.isSupported, "Hit flash overlay shader loads and is supported");
        var particle = FeelAssets.Particle(FeelAssets.SoftDot, true);
        Require(particle != null && particle.shader.name == "Hidden/Morphang/FeelParticle" && particle.shader.isSupported, "Feel particle shader loads");

        var player = Object.FindFirstObjectByType<PlayerMovement>();
        var slots = player.GetComponent<PlayerWeaponSlots>();
        var gun = player.GetComponent<WeaponController>();
        var view = player.GetComponentInChildren<Camera>();
        foreach (var wave in Object.FindObjectsByType<WaveManager>(FindObjectsSortMode.None)) wave.enabled = false;
        slots.SelectSlot(0);
        yield return .3f;
        Require(gun.EquippedData != null && gun.EquippedData.Category == WeaponCategory.Ranged, "Slot 1 holds a gun");
        Require(gun.GetComponent<WeaponFeedback>() != null, "WeaponController adds WeaponFeedback at runtime");

        var cameraData = UnityEngine.Rendering.Universal.CameraExtensions.GetUniversalAdditionalCameraData(view);
        bool authoredPost = cameraData.renderPostProcessing;
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_Project/Prefabs/Enemies/Enemy_Test.prefab").GetComponent<EnemyHealth>();
        var enemy = Spawn(player, prefab);
        var reaction = enemy.GetComponent<EnemyHitReaction>();
        var posture = enemy.GetComponent<MeleeCombatTarget>();
        Require(reaction != null, "EnemyHealth adds EnemyHitReaction");
        yield return .2f;

        // ---- body hit
        Aim(player, view, Chest(enemy));
        float health = enemy.Health, postureBefore = posture.Posture;
        Fire(gun);
        Require(enemy.Health < health, "Body shot damages the enemy");
        yield return .02f;
        var punch = view.GetComponent<CameraViewPunch>();
        Require(punch != null, "Shot adds a camera view punch");
        Require(GameObject.Find("Feel Audio") != null, "Shot plays through FeelAudio");
        Require(GameObject.Find("Weapon Feel FX") != null, "Shot spawns muzzle smoke / brass");
        Require(Object.FindFirstObjectByType<HitMarkerHUD>()?.LastKind == HitMarkerKind.Body, "Body hit shows the body marker");
        var flash = enemy.GetComponent<HitFlashRenderer>();
        Require(flash != null && flash.IsFlashing, "Body hit flashes the enemy");
        Require(reaction.enabled && reaction.SpeedMultiplier < 1f, "Bullet slows the enemy");
        Require(posture.Posture < postureBefore, "Bullet wears down posture");
        Require(GameObject.Find("Impact VFX") != null, "Body hit sprays blood");
        Require(CombatFeedHUD.TotalScore == 0, "Damage alone does not score (kills only)");
        Require(cameraData.renderPostProcessing == authoredPost, "Hits leave the camera's post-processing as authored (no grey tint)");
        yield return .5f;
        Require(!flash.IsFlashing, "Flash clears and removes its overlay");
        Require(enemy.GetComponent<Renderer>().sharedMaterials.Length == 1, "Overlay material is removed after the flash");

        // ---- headshot: interrupts
        yield return 1.2f; // past the interrupt cooldown of the body hit, if any
        Aim(player, view, Head(enemy));
        health = enemy.Health;
        Fire(gun);
        Require(enemy.Health < health, "Headshot damages the enemy");
        yield return .02f;
        Require(Object.FindFirstObjectByType<HitMarkerHUD>().LastKind == HitMarkerKind.Head, "Headshot shows the gold marker");
        Require(posture.IsStunned && reaction.InterruptCooldown > 0f, "Headshot interrupts the enemy");
        yield return .4f;

        // ---- body kill
        long scoreBefore = CombatFeedHUD.TotalScore;
        for (int i = 0; i < 20 && enemy != null && enemy.IsAlive; i++)
        {
            Aim(player, view, Chest(enemy));
            Fire(gun);
            if (enemy != null && enemy.IsAlive) yield return .12f;
        }
        Require(enemy == null || !enemy.IsAlive, "Sustained fire kills the enemy");
        yield return .02f;
        Require(Object.FindFirstObjectByType<HitMarkerHUD>().LastKind == HitMarkerKind.BodyKill, "Kill shows the red kill marker");
        Require(Object.FindFirstObjectByType<CorpseTopple>() != null, "Kill leaves a toppling corpse");
        var pulse = Object.FindFirstObjectByType<KillConfirmEffect>();
        Require(pulse == null || pulse.Weight <= 0f, "An ordinary gun kill does not pulse the screen");
        Require(CombatFeedHUD.TotalScore >= scoreBefore + GunFeelSettings.Instance.killScore, "Kill adds kill score");
        var feed = Object.FindFirstObjectByType<CombatFeedHUD>();
        Require(feed.StreakText.StartsWith(GunFeelSettings.Instance.killScore.ToString()) && feed.StreakText.Contains("XP"), "Kill shows '100 XP': " + feed.StreakText);
        var streak = (RectTransform)feed.transform.Find("Streak");
        Require(streak.anchoredPosition.x > 0f && streak.anchoredPosition.y < 0f, "Kill score sits lower right of the crosshair: " + streak.anchoredPosition);
        Require(cameraData.renderPostProcessing == authoredPost, "A gun kill leaves the camera's post-processing as authored");
        yield return 3.1f;
        Require(Object.FindFirstObjectByType<CorpseTopple>() == null, "Corpse sinks away");

        // ---- hit marker rides the view punch
        var marker = Object.FindFirstObjectByType<HitMarkerHUD>();
        var markerRoot = (RectTransform)marker.transform.Find("Marker");
        var aimed = Spawn(player, prefab);
        yield return .2f;
        Aim(player, view, Chest(aimed));
        Fire(gun);
        yield return .03f;
        Require(markerRoot.anchoredPosition.magnitude > .5f, "Hit marker moves with the view punch: " + markerRoot.anchoredPosition);
        yield return .6f;
        Require(!markerRoot.gameObject.activeSelf, "Hit marker shrinks away");
        Object.Destroy(aimed.gameObject);
        yield return .1f;

        // ---- headshot kill
        var second = Spawn(player, prefab);
        yield return .2f;
        for (int i = 0; i < 10 && second != null && second.IsAlive; i++)
        {
            Aim(player, view, Head(second));
            Fire(gun);
            if (second != null && second.IsAlive) yield return .15f;
        }
        Require(second == null || !second.IsAlive, "Headshots kill the second enemy");
        yield return .02f;
        Require(Object.FindFirstObjectByType<HitMarkerHUD>().LastKind == HitMarkerKind.HeadKill, "Headshot kill shows the crowned kill marker");
        pulse = Object.FindFirstObjectByType<KillConfirmEffect>();
        Require(pulse == null || pulse.Weight <= 0f, "A headshot gun kill does not pulse the screen either");
        yield return 2.6f; // let the streak fade so the execution starts a new one

        // ---- execution: the rare kill that keeps the full screen pulse
        // PlayerExecution needs focused player input, which a batch run never has (its own checks cover the move itself),
        // so the execution's killing blow is delivered here exactly as PlayerExecution reports it.
        var victim = Spawn(player, prefab);
        yield return .1f;
        Aim(player, view, Chest(victim));
        var blow = new DamageHit(DamageHit.NewAttackId(), victim.MaximumHealth * 10f, DamageKind.Execution,
            victim.transform.position, view.transform.forward, player.gameObject);
        Require(victim.ReceiveHit(blow) == DamageResult.Damaged, "Execution blow lands");
        Require(victim == null || !victim.IsAlive, "Execution kills the victim");
        yield return .03f;
        pulse = Object.FindFirstObjectByType<KillConfirmEffect>();
        Require(pulse != null && pulse.Weight > 0f, "Execution pulses the screen");
        Require(feed.StreakText.Contains("XP"), "Execution scores");
        yield return 1.2f;
        Require(pulse.Weight <= 0f, "Kill pulse ends");
        Require(cameraData.renderPostProcessing == authoredPost && !pulse.IsBorrowingPostProcessing,
            "After the pulse the camera's post-processing is back as authored");
    }

    private static EnemyHealth Spawn(PlayerMovement player, EnemyHealth prefab)
    {
        Require(ConsoleEnemySpawner.TrySpawn(player, prefab, out var enemy, out string error), "Enemy spawns in front of the player: " + error);
        // Hold it at range: walking into melee would raise the rifle to high ready and block the test shots.
        // The NavMeshAgent stays, so bullet push and slow still act on the real agent.
        enemy.GetComponent<EnemyChaseAI>().enabled = false;
        enemy.GetComponent<EnemyCombatAI>().enabled = false;
        Physics.SyncTransforms();
        return enemy;
    }

    private static Vector3 Chest(EnemyHealth enemy)
    {
        var bounds = enemy.GetComponent<Collider>().bounds;
        return bounds.center;
    }

    private static Vector3 Head(EnemyHealth enemy)
    {
        var bounds = enemy.GetComponent<Collider>().bounds;
        // Middle of the default head band (top 22% of the capsule), leaving room for hip-fire spread.
        return new Vector3(bounds.center.x, bounds.max.y - bounds.size.y * .11f, bounds.center.z);
    }

    /// <summary>Points the real look rig at a world point (yaw on the body, pitch on the look pivot).</summary>
    private static void Aim(PlayerMovement player, Camera view, Vector3 point)
    {
        // Between frames the camera still carries last frame's visual punch; gameplay never sees it, so remove it here too.
        var punch = view.GetComponent<CameraViewPunch>();
        if (punch != null) typeof(CameraViewPunch).GetMethod("Restore", Any).Invoke(punch, null);
        var look = player.GetComponent<PlayerLook>();
        look.ClearRecoil();
        Vector3 direction = point - view.transform.position;
        float yaw = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        float pitch = -Mathf.Asin(Mathf.Clamp(direction.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
        typeof(PlayerLook).GetField("yaw", Any).SetValue(look, yaw);
        typeof(PlayerLook).GetField("pitch", Any).SetValue(look, pitch);
        typeof(PlayerLook).GetMethod("TickRecoil", Any).Invoke(look, new object[] { .0001f });
        Physics.SyncTransforms();
    }

    private static void Fire(WeaponController gun)
    {
        var process = typeof(WeaponController).GetMethod("ProcessFrame", Any);
        process.Invoke(gun, new object[] { .3f, default(WeaponInputFrame) });
        process.Invoke(gun, new object[] { .11f, new WeaponInputFrame(true, true) });
    }
}
