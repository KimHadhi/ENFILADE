using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

/// <summary>Real player damage, world bearings, independent expiry, mesh output, camera restoration and lifecycle.</summary>
[InitializeOnLoad]
public static class PlayerDamageFeedbackPlayChecks
{
    private const string Pending = "ENFILADE.PlayerDamageFeedbackChecks";
    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly List<string> errors = new List<string>();
    private static IEnumerator<float> steps;
    private static float resumeAt;

    static PlayerDamageFeedbackPlayChecks() => EditorApplication.playModeStateChanged += StateChanged;

    public static void RunBatch()
    {
        if (!Application.isBatchMode) throw new InvalidOperationException("Run in an isolated Unity batch project.");
        EditorSceneManager.OpenScene("Assets/_Project/Scenes/Development/CombatFoundation.unity");
        SessionState.SetBool(Pending, true); EditorApplication.isPlaying = true;
    }

    private static void StateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode || !SessionState.GetBool(Pending, false)) return;
        SessionState.SetBool(Pending, false); errors.Clear();
        Application.logMessageReceived += Collect;
        steps = Steps(); resumeAt = 0f; EditorApplication.update += Tick;
    }

    private static void Collect(string message, string stack, LogType type)
    {
        if (stack != null && stack.Contains("UnityEditor.Search")) return;
        if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors.Add(message);
    }

    private static void Tick()
    {
        if (Time.realtimeSinceStartup < resumeAt) return;
        try
        {
            if (steps.MoveNext()) { resumeAt = Time.realtimeSinceStartup + steps.Current; return; }
            Require(errors.Count == 0, "No runtime errors: " + string.Join("\n", errors));
            Debug.Log("PLAYER_DAMAGE_FEEDBACK_PLAY_CHECKS_PASSED"); Finish(0);
        }
        catch (Exception exception) { Debug.LogException(exception); Finish(1); }
    }

    private static void Finish(int exitCode)
    {
        Time.timeScale = 1f;
        EditorApplication.update -= Tick; Application.logMessageReceived -= Collect;
        EditorApplication.Exit(exitCode);
    }

    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }

    private static DamageHit Hit(Camera camera, Vector3 origin, float amount = 5f, GameObject source = null)
        => new DamageHit(DamageHit.NewAttackId(), amount, DamageKind.Attack, camera.transform.position,
            (camera.transform.position - origin).normalized, source, origin: origin);

    private static IEnumerator<float> Steps()
    {
        foreach (var wave in Object.FindObjectsByType<WaveManager>(FindObjectsSortMode.None)) wave.enabled = false;
        foreach (var enemy in Object.FindObjectsByType<EnemyCombatAI>(FindObjectsSortMode.None)) enemy.enabled = false;
        yield return .3f;
        var player = Object.FindFirstObjectByType<PlayerMovement>();
        var receiver = player.DamageReceiver;
        var feedback = player.GetComponent<PlayerDamageFeedback>();
        var camera = player.GetComponentInChildren<Camera>();
        Require(feedback != null && camera != null, "Existing player installs feedback without scene changes");
        receiver.RestoreHealth();
        player.enabled = false;
        player.GetComponent<PlayerLook>().enabled = false;
        foreach (var effects in player.GetComponentsInChildren<PlayerCameraEffects>()) effects.enabled = false;
        var punch = CameraViewPunch.For(camera);
        typeof(CameraViewPunch).GetMethod("Restore", Any).Invoke(punch, null);
        Quaternion steady = camera.transform.rotation;
        Vector3 position = camera.transform.position;
        Vector3 left = position - camera.transform.right * 15f;
        Vector3 front = position + camera.transform.forward * 15f;
        Vector3 rear = position - camera.transform.forward * 15f;

        var source = new GameObject("Temporary attacker"); source.transform.position = left;
        var first = Hit(camera, left, source: source);
        Require(receiver.ReceiveHit(first) == DamageResult.Damaged, "Real receiver accepts the incoming attack");
        Require(feedback.ActiveHitCount == 1 && feedback.ActiveSplashCount > 0 && punch.DamageTrauma > 0f,
            "A hit creates independent direction, a blood splash and trauma");
        Require(receiver.ReceiveHit(first) == DamageResult.Duplicate && feedback.ActiveHitCount == 1,
            "Duplicate attack IDs do not add feedback");
        receiver.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), 0f, DamageKind.Attack, position, Vector3.forward));
        Require(feedback.ActiveHitCount == 1, "Ignored damage does not add feedback");
        source.transform.position = front; Object.Destroy(source);
        yield return .08f;
        Require(feedback.TryGetIndicator(0, out Vector3 stored, out Vector2 bearing, out _) && stored == left && bearing.x < -.95f,
            "Moving/destroying the attacker leaves its original world position intact");
        Require(Quaternion.Angle(steady, camera.transform.rotation) > .001f, "Damage physically tilts the rendered camera");
        typeof(CameraViewPunch).GetMethod("Restore", Any).Invoke(punch, null);
        Require(Quaternion.Angle(steady, camera.transform.rotation) < .001f, "Gameplay camera pose restores without drift");
        var graphic = Object.FindFirstObjectByType<PlayerDamageGraphic>();
        Require(graphic != null && !graphic.raycastTarget && graphic.GetComponentInParent<Canvas>().sortingOrder == 24,
            "Damage overlay cannot intercept input and renders below combat markers");
        var leftMesh = MeshOf(graphic);
        Require(Alpha(leftMesh, 0, 14) > Alpha(leftMesh, 48, 14) + .1f, "Left hits redden the left edge more than the right");
        // Rendering can stall on first shader use; hold hit lifetimes still while the shader output is compared.
        Time.timeScale = 0f;
        CheckRenderedBlood(camera, graphic);
        yield return .04f;
        Time.timeScale = 1f;
        foreach (float wait in Snapshot(camera, graphic, "damage-feedback-left")) yield return wait;
        Object.Destroy(leftMesh);

        typeof(CameraViewPunch).GetMethod("Restore", Any).Invoke(punch, null);
        camera.transform.rotation = steady * Quaternion.Euler(0f, 180f, 0f);
        yield return .05f;
        Require(feedback.TryGetIndicator(0, out stored, out bearing, out _) && stored == left && bearing.x > .95f,
            $"An instantaneous 180 degree turn sends the original left arc to the right: count={feedback.ActiveHitCount}, bearing={bearing}");
        typeof(CameraViewPunch).GetMethod("Restore", Any).Invoke(punch, null);
        camera.transform.rotation = steady;
        camera.transform.position = position + steady * Vector3.forward * 8f;
        yield return .05f;
        feedback.TryGetIndicator(0, out _, out bearing, out _);
        Require(bearing.y < -.35f, "Player translation recomputes bearing to the saved 3D position");
        typeof(CameraViewPunch).GetMethod("Restore", Any).Invoke(punch, null);
        camera.transform.position = position; camera.transform.rotation = steady;

        receiver.RestoreHealth();
        punch.Punch(new Vector3(2f, 0f, 0f));
        receiver.ReceiveHit(Hit(camera, left));
        yield return .03f;
        feedback.Clear();
        Require(punch.DamageTrauma == 0f && Quaternion.Angle(steady, camera.transform.rotation) > .02f,
            "Clearing damage preserves the concurrent firearm kick in the current rendered frame");
        yield return .7f;
        receiver.ReceiveHit(Hit(camera, front));
        yield return .8f;
        receiver.ReceiveHit(Hit(camera, left));
        yield return .05f;
        Require(feedback.ActiveHitCount == 2, "Two directions coexist");
        feedback.TryGetIndicator(0, out _, out _, out float oldAlpha);
        feedback.TryGetIndicator(1, out _, out _, out float freshAlpha);
        Require(oldAlpha < freshAlpha - .1f, "Old and fresh hits have independent opacity");
        foreach (float wait in Snapshot(camera, graphic, "damage-feedback-multi")) yield return wait;
        yield return .9f;
        Require(feedback.ActiveHitCount == 1 && feedback.TryGetIndicator(0, out stored, out _, out _) && stored == left,
            "Only the first hit expires; the second retains its own lifetime");
        yield return .9f;
        Require(feedback.ActiveHitCount == 0, "The final direction also expires");
        Require(feedback.ActiveSplashCount > 0 && graphic.enabled, "Blood keeps receding after every arc has expired");
        foreach (float wait in Snapshot(camera, graphic, "damage-feedback-receding")) yield return wait;

        receiver.RestoreHealth(); receiver.ReceiveHit(Hit(camera, rear));
        yield return .05f;
        var rearMesh = MeshOf(graphic);
        Require(Alpha(rearMesh, 24, 0) > Alpha(rearMesh, 24, 28) + .1f && feedback.RearWash > .02f,
            "Rear damage adds bottom blood and a full-screen wash");
        foreach (float wait in Snapshot(camera, graphic, "damage-feedback-rear")) yield return wait;
        Object.Destroy(rearMesh);
        float trauma = punch.DamageTrauma;
        feedback.TryGetIndicator(0, out _, out _, out float pausedAlpha);
        Time.timeScale = 0f;
        yield return .25f;
        feedback.TryGetIndicator(0, out _, out _, out float afterPause);
        Require(Mathf.Abs(pausedAlpha - afterPause) < .001f && Mathf.Abs(trauma - punch.DamageTrauma) < .001f,
            "Pause freezes damage fade and trauma decay");
        Time.timeScale = 1f;

        receiver.RestoreHealth(); receiver.SetShield(20f);
        receiver.ReceiveHit(Hit(camera, front));
        yield return .05f;
        Require(receiver.Health == receiver.MaximumHealth && feedback.ActiveHitCount == 1 && !feedback.IsCritical,
            "Shield-only damage gives impact feedback without triggering critical HP");
        receiver.RestoreHealth(); receiver.ReceiveHit(Hit(camera, left, 75f));
        yield return .05f;
        Require(feedback.IsCritical && Mathf.Approximately(receiver.Health / receiver.MaximumHealth, .25f),
            "Heartbeat starts at exactly 25 percent HP");
        float low = 1f, high = 0f;
        for (int i = 0; i < 16; i++)
        {
            low = Mathf.Min(low, feedback.PulseWeight); high = Mathf.Max(high, feedback.PulseWeight);
            if (feedback.PulseWeight > .7f)
                foreach (float wait in Snapshot(camera, graphic, "damage-feedback-critical")) yield return wait;
            yield return .07f;
        }
        Require(high - low > .3f, "Critical vignette visibly pulses across a heartbeat");
        Require(feedback.Baseline >= PlayerDamageFeedbackSettings.Instance.criticalBlood * .95f && feedback.Vignette > .1f && graphic.enabled,
            "Critical blood and red vignette stay on between beats");
        receiver.Heal(1f); yield return .05f;
        Require(!feedback.IsCritical && feedback.PulseWeight == 0f, "Healing above 25 percent immediately ends the pulse");
        receiver.SetMaximumHealth(400f); yield return .05f;
        Require(feedback.IsCritical, "Critical ratio follows maximum-health changes without needing another hit");
        receiver.SetMaximumHealth(100f, true);

        receiver.RestoreHealth(); receiver.SetShield(500f);
        int objectsBefore = graphic.transform.parent.childCount;
        for (int i = 0; i < 80; i++) receiver.ReceiveHit(Hit(camera, front + Vector3.right * i, 1f));
        Require(feedback.ActiveHitCount == 24 && feedback.ActiveSplashCount == 32 && feedback.ActiveDripCount == 24
            && graphic.transform.parent.childCount == objectsBefore, "Sustained fire uses bounded pools without per-hit objects");
        yield return .2f;
        foreach (float wait in Snapshot(camera, graphic, "damage-feedback-heavy")) yield return wait;
        for (int i = 0; i < feedback.ActiveHitCount; i++)
            Require(feedback.TryGetIndicator(i, out stored, out _, out _) && stored.x - front.x >= 55.9f,
                "Pool saturation retains the newest hits even when all attacks arrive in the same frame");
        receiver.ReceiveHit(Hit(camera, rear, 1000f));
        Require(!receiver.IsAlive && feedback.ActiveHitCount == 0 && feedback.ActiveSplashCount == 0 && feedback.ActiveDripCount == 0 && feedback.Baseline == 0f
            && !feedback.IsCritical && punch.DamageTrauma == 0f, "Death clears damage overlays, pulse and damage shake");
        receiver.RestoreHealth();
        receiver.ReceiveHit(Hit(camera, front));
        feedback.enabled = false; yield return .05f;
        Require(Object.FindFirstObjectByType<PlayerDamageGraphic>() == null && punch.DamageTrauma == 0f,
            "Disabling the component removes its overlay and shake");
        feedback.enabled = true;
        receiver.ReceiveHit(Hit(camera, front));
        Require(feedback.ActiveHitCount == 1, "Re-enabling subscribes once without stale hits");
        receiver.RestoreHealth();

        var router = new ChatCommandRouter(); PlayerConsoleCommands.Register(router, () => player);
        Require(router.Execute("/hit 5 left").Contains("Damaged") && feedback.ActiveHitCount == 1,
            "Preview command goes through real directional damage");
        receiver.RestoreHealth();
        CheckTraceOrigins();
        Debug.Log("Damage feedback screenshots: " + Directory.GetParent(Application.dataPath).FullName);
    }

    private static void CheckTraceOrigins()
    {
        var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
        target.transform.position = new Vector3(2000f, 20f, 2000f);
        var receiver = target.AddComponent<PlayerDamageReceiver>(); receiver.RestoreHealth();
        Vector3? captured = null;
        receiver.HitResolved += (hit, _) => captured = hit.Origin;
        Vector3 origin = target.transform.position - Vector3.forward * 5f;
        Physics.SyncTransforms();
        var attack = new DamageHit(DamageHit.NewAttackId(), 1f, DamageKind.Attack, origin, Vector3.forward);
        Require(AttackTrace.Raycast(attack, origin, Vector3.forward, 10f) == DamageResult.Damaged
            && captured.HasValue && captured.Value == origin, "Real raycasts propagate the actual muzzle origin");
        Vector3 launch = origin - Vector3.right * 3f;
        attack = new DamageHit(DamageHit.NewAttackId(), 1f, DamageKind.Attack, origin, Vector3.forward, origin: launch);
        AttackTrace.Raycast(attack, origin, Vector3.forward, 10f);
        Require(captured.HasValue && captured.Value == launch, "Explicit launch origins survive re-tracing at impact");
        Object.Destroy(target);
    }

    private static Mesh MeshOf(PlayerDamageGraphic graphic)
    {
        var helper = new VertexHelper();
        try
        {
            Canvas.ForceUpdateCanvases();
            typeof(PlayerDamageGraphic).GetMethod("OnPopulateMesh", Any, null, new[] { typeof(VertexHelper) }, null)
                .Invoke(graphic, new object[] { helper });
            var mesh = new Mesh(); helper.FillMesh(mesh); return mesh;
        }
        finally { helper.Dispose(); }
    }

    private static float Alpha(Mesh mesh, int x, int y) => mesh.colors32[y * 49 + x].a / 255f;

    private static IEnumerable<float> Snapshot(Camera camera, PlayerDamageGraphic graphic, string name)
    {
        float timeScale = Time.timeScale;
        Time.timeScale = 0f;
        try
        {
            Capture(camera, graphic, name);
            // Rendering/readback can compile shaders and stall the first frame. Flush that frame while paused,
            // so screenshot overhead cannot consume a hit's real-time lifetime in the next gameplay frame.
            yield return .04f;
        }
        finally { Time.timeScale = timeScale; }
    }

    /// <summary>Compares real shader output with and without the overlay: struck edge stained, centre untouched.</summary>
    private static void CheckRenderedBlood(Camera camera, PlayerDamageGraphic graphic)
    {
        var stained = Render(camera, graphic);
        graphic.enabled = false;
        var clean = Render(camera, graphic);
        graphic.enabled = true;
        float centre = Difference(stained, clean, 640, 360), edge = 0f;
        for (int row = 0; row < 5; row++) edge += Difference(stained, clean, 6, 200 + row * 80) / 5f;
        Object.Destroy(stained); Object.Destroy(clean);
        Require(centre < .02f, $"Side damage leaves the rendered centre untouched: {centre:0.000}");
        Require(edge > .1f, $"Side damage visibly stains the rendered left edge: {edge:0.000}");
    }

    private static float Difference(Texture2D a, Texture2D b, int x, int y)
    {
        Color pa = a.GetPixel(x, y), pb = b.GetPixel(x, y);
        return Mathf.Max(Mathf.Abs(pa.r - pb.r), Mathf.Abs(pa.g - pb.g), Mathf.Abs(pa.b - pb.b));
    }

    private static void Capture(Camera camera, PlayerDamageGraphic graphic, string name)
    {
        var image = Render(camera, graphic);
        File.WriteAllBytes(Path.Combine(Directory.GetParent(Application.dataPath).FullName, name + ".png"), image.EncodeToPNG());
        Object.Destroy(image);
    }

    private static Texture2D Render(Camera camera, PlayerDamageGraphic graphic)
    {
        var canvas = graphic.GetComponentInParent<Canvas>();
        var previous = camera.targetTexture; var active = RenderTexture.active;
        var rt = new RenderTexture(1280, 720, 24);
        camera.targetTexture = rt;
        canvas.renderMode = RenderMode.ScreenSpaceCamera; canvas.worldCamera = camera; canvas.planeDistance = camera.nearClipPlane + .5f;
        try
        {
            Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = rt;
            var image = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            image.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0); image.Apply();
            return image;
        }
        finally
        {
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.worldCamera = null;
            camera.targetTexture = previous; RenderTexture.active = active; rt.Release(); Object.Destroy(rt);
        }
    }
}
