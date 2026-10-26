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

    private static void RequireFeedbackGroup(Component component, string groupName, int sortingOrder = -1)
    {
        Require(component != null, groupName + " feedback object was created");
        var group = component.transform.parent;
        Require(group != null && group.name == groupName
            && group.parent != null && group.parent.name.Contains("EFFECTS"),
            component.name + " is grouped under EFFECTS/" + groupName);
        if (sortingOrder < 0) return;
        var canvas = component.GetComponent<Canvas>();
        var scaler = component.GetComponent<UnityEngine.UI.CanvasScaler>();
        Require(canvas != null && canvas.isRootCanvas && canvas.renderMode == RenderMode.ScreenSpaceOverlay
            && canvas.sortingOrder == sortingOrder && scaler != null
            && scaler.referenceResolution == new Vector2(1920f, 1080f),
            component.name + " preserves its independent overlay, draw order and reference size");
    }

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
        var audioRoot = GameObject.Find("Feel Audio");
        var effectsRoot = GameObject.Find("Weapon Feel FX");
        Require(audioRoot != null, "Shot plays through FeelAudio");
        Require(effectsRoot != null, "Shot spawns muzzle smoke / brass");
        RequireFeedbackGroup(audioRoot.GetComponent<FeelAudio>(), "Audio");
        RequireFeedbackGroup(effectsRoot.transform, "World");
        Require(audioRoot.transform.parent.parent == effectsRoot.transform.parent.parent
            && audioRoot.transform.parent.parent.name.Contains("EFFECTS")
            && audioRoot.scene == player.gameObject.scene && effectsRoot.scene == player.gameObject.scene,
            "Audio and world effects share one EFFECTS category in the player's scene");
        Require(effectsRoot.transform.parent.name == "World", "Weapon effects belong to EFFECTS/World");
        Require(effectsRoot.transform.position == Vector3.zero && Vector3.Distance(effectsRoot.transform.lossyScale, Vector3.one) < .0001f,
            "Grouping preserves the world-space origin and scale used by smoke and casings");
        RequireFeedbackGroup(CombatHitAudio.Instance, "Audio");
        RequireFeedbackGroup(DamagePopupManager.Instance, "Screen", 30);
        RequireFeedbackGroup(Object.FindFirstObjectByType<HitMarkerHUD>(), "Screen", 26);
        Require(Object.FindFirstObjectByType<HitMarkerHUD>()?.LastKind == HitMarkerKind.Body, "Body hit shows the body marker");
        var flash = enemy.GetComponent<HitFlashRenderer>();
        Require(flash != null && flash.IsFlashing, "Body hit flashes the enemy");
        Require(reaction.enabled && reaction.SpeedMultiplier < 1f, "Bullet slows the enemy");
        Require(posture.Posture < postureBefore, "Bullet wears down posture");
        Require(GameObject.Find("Impact VFX") != null, "Body hit sprays blood");
        RequireFeedbackGroup(Object.FindFirstObjectByType<ImpactVfx>(), "World");
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
        RequireFeedbackGroup(Object.FindFirstObjectByType<CorpseTopple>(), "World");
        var pulse = Object.FindFirstObjectByType<KillConfirmEffect>();
        Require(pulse != null && pulse.Weight >= .99f && pulse.Current == KillScreen.Gta, "A gun kill shows the GTA kill screen at full strength");
        RequireFeedbackGroup(pulse, "Screen");
        Require(CombatFeedHUD.TotalScore >= scoreBefore + GunFeelSettings.Instance.killScore, "Kill adds kill score");
        var feed = Object.FindFirstObjectByType<CombatFeedHUD>();
        RequireFeedbackGroup(feed, "Screen", 27);
        Require(feed.StreakText.StartsWith(GunFeelSettings.Instance.killScore.ToString()) && feed.StreakText.Contains("XP"), "Kill shows '100 XP': " + feed.StreakText);
        var streak = (RectTransform)feed.transform.Find("Streak");
        Require(streak.anchoredPosition.x > 0f && streak.anchoredPosition.y < 0f, "Kill score sits lower right of the crosshair: " + streak.anchoredPosition);
        // The kill and its first render may already have consumed part of the hold (shader compilation/readback).
        // Schedule against the effect's actual age instead of adding a second full delay after those frames.
        float hold = GunFeelSettings.Instance.gtaKillHoldSeconds, fade = GunFeelSettings.Instance.gtaKillFadeSeconds;
        yield return Mathf.Max(0f, hold * .6f - KillScreenAge(pulse));
        Require(pulse.Weight >= .99f, "Kill screen holds at full strength for the hold time");
        yield return Mathf.Max(0f, hold + fade * .5f - KillScreenAge(pulse));
        Require(pulse.Weight > 0f && pulse.Weight < 1f, "Kill screen fades after the hold: " + pulse.Weight);
        yield return Mathf.Max(0f, hold + fade + .1f - KillScreenAge(pulse));
        Require(pulse.Weight <= 0f, "Kill screen is gone after hold + fade");
        Require(cameraData.renderPostProcessing == authoredPost, "After a gun kill the camera's post-processing is back as authored");
        yield return 2.3f;
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
        Require(Object.FindFirstObjectByType<HitMarkerHUD>().LastKind == HitMarkerKind.HeadKill, "Headshot kill shows the red kill marker");
        Require(pulse.Weight > 0f && pulse.Current == KillScreen.Gta, "A headshot gun kill shows the GTA kill screen too");
        yield return 2.6f; // let the streak fade so the execution starts a new one

        // ---- execution: the special kill screen, then the camera is handed back
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
        Require(pulse != null && pulse.Weight > 0f && pulse.Current == KillScreen.Execution, "Execution shows the special kill screen");
        RequireFeedbackGroup(pulse, "Screen");
        Require(feed.StreakText.Contains("XP"), "Execution scores");
        yield return 1.2f;
        Require(pulse.Weight <= 0f, "Kill pulse ends");
        Require(cameraData.renderPostProcessing == authoredPost && !pulse.IsBorrowingPostProcessing,
            "After the pulse the camera's post-processing is back as authored");
        foreach (float pause in BloodDecalPooling()) yield return pause;
    }

    private static float KillScreenAge(KillConfirmEffect effect)
        => (float)typeof(KillConfirmEffect).GetField("age", Any).GetValue(effect);

    private static IEnumerable<float> BloodDecalPooling()
    {
        Vector3 origin = new Vector3(2000f, 100f, 2000f);
        var floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
        floor.name = "Blood pooling floor";
        floor.transform.position = origin - Vector3.up * .1f;
        floor.transform.localScale = new Vector3(4f, .2f, 4f);
        Physics.SyncTransforms();
        try
        {
            // More hits than the limit must reuse visible decals rather than grow the hierarchy.
            for (int i = 0; i < 64; i++) ImpactVfx.Blood(origin + Vector3.up, Vector3.forward, false, true, false, null);
            yield return .05f;
            var impact = Object.FindFirstObjectByType<ImpactVfx>();
            var decals = (Transform[])typeof(ImpactVfx).GetField("decals", Any).GetValue(impact);
            var ages = (float[])typeof(ImpactVfx).GetField("decalAge", Any).GetValue(impact);
            var folder = impact.transform.Find("Blood Decals (Pool)");
            Require(decals.Length == 32 && folder != null && folder.childCount == 32,
                "64 blood events retain at most 32 decals inside their own folder");
            foreach (var decal in decals) Require(decal != null && decal.gameObject.activeSelf, "Fresh blood decals are visible");
            var original = (Transform[])decals.Clone();

            // Advance past the authored lifetime and fade, including a manually disabled decal.
            decals[0].gameObject.SetActive(false);
            for (int i = 0; i < ages.Length; i++) ages[i] = GunFeelSettings.Instance.bloodDecalSeconds + 2f;
            typeof(ImpactVfx).GetMethod("Update", Any).Invoke(impact, null);
            yield return .05f;
            Require(folder.childCount == 32, "Expired blood objects remain in the bounded reuse pool");
            for (int i = 0; i < decals.Length; i++)
                Require(decals[i] == original[i] && !decals[i].gameObject.activeSelf,
                    "Expired decals are hidden without destroying their reusable objects");

            ImpactVfx.Blood(origin + Vector3.up, Vector3.forward, false, false, false, null);
            yield return .05f;
            int visible = 0;
            for (int i = 0; i < decals.Length; i++)
            {
                Require(decals[i] == original[i], "A new hit reuses the same decal objects without allocation");
                if (decals[i].gameObject.activeSelf) visible++;
            }
            Require(folder.childCount == 32 && visible == 1, "A new hit reactivates one existing decal and does not grow the pool");
            for (int i = 0; i < ages.Length; i++) ages[i] = GunFeelSettings.Instance.bloodDecalSeconds + 2f;
            typeof(ImpactVfx).GetMethod("Update", Any).Invoke(impact, null);
            yield return .05f;
            foreach (var decal in decals) Require(!decal.gameObject.activeSelf, "Reused decals return to the inactive pool on expiry");
            Debug.Log("BLOOD_DECAL_POOL_CHECKS_PASSED");
        }
        finally { Object.Destroy(floor); }
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
