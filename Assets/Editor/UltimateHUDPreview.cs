using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>Editor-only preview clock. Gameplay resource events never run in edit mode.</summary>
[InitializeOnLoad]
public static class UltimateHUDPreview
{
    private static bool suspended;
    private static double previousTime;
    private static UltimateHUD[] views = Array.Empty<UltimateHUD>();
    private static UltimatePortraitGlow[] glows = Array.Empty<UltimatePortraitGlow>();
    private static AdaptiveHealthHUD[] healthViews = Array.Empty<AdaptiveHealthHUD>();
    private static UltimateFireEffect[] fires = Array.Empty<UltimateFireEffect>();

    static UltimateHUDPreview()
    {
        previousTime = EditorApplication.timeSinceStartup;
        EditorApplication.update += Tick;
        AssemblyReloadEvents.beforeAssemblyReload += StopAll;
        EditorApplication.quitting += StopAll;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        EditorSceneManager.sceneSaving += BeforeSave;
        EditorSceneManager.sceneSaved += AfterSave;
        EditorSceneManager.sceneClosing += BeforeClose;
        Undo.undoRedoPerformed += StopAll;
    }

    private static void Tick()
    {
        if (Application.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || suspended) return;
        double now = EditorApplication.timeSinceStartup;
        double elapsed = now - previousTime;
        if (elapsed < 1d / 30d) return;
        previousTime = now;
        Refresh((float)Math.Min(elapsed, 0.1d));
        // Edit-mode MonoBehaviour.Update alone does not provide a continuous heartbeat clock.
        InternalEditorUtility.RepaintAllViews();
    }

    public static void Refresh(float deltaTime)
    {
        if (Application.isPlaying || suspended) return;
        views = Resources.FindObjectsOfTypeAll<UltimateHUD>();
        glows = Resources.FindObjectsOfTypeAll<UltimatePortraitGlow>();
        healthViews = Resources.FindObjectsOfTypeAll<AdaptiveHealthHUD>();
        fires = Resources.FindObjectsOfTypeAll<UltimateFireEffect>();
        foreach (UltimateFireEffect fire in fires)
        {
            if (CanPreview(fire)) fire.RefreshNow(deltaTime);
            else if (fire != null && !EditorUtility.IsPersistent(fire)) fire.StopPreview();
        }
        foreach (AdaptiveHealthHUD health in healthViews)
        {
            if (CanPreview(health)) health.RefreshNow();
            else if (health != null && !EditorUtility.IsPersistent(health)) health.StopPreview();
        }
        foreach (UltimateHUD view in views)
        {
            if (CanPreview(view)) view.RefreshNow();
            else if (view != null && !EditorUtility.IsPersistent(view)) view.StopPreview();
        }
        foreach (UltimatePortraitGlow glow in glows)
        {
            if (CanPreview(glow)) glow.RefreshNow(deltaTime);
            else if (glow != null && !EditorUtility.IsPersistent(glow)) glow.StopPreview();
        }
    }

    private static bool CanPreview(Behaviour component)
    {
        return component != null && component.isActiveAndEnabled && !EditorUtility.IsPersistent(component)
            && component.gameObject.scene.IsValid() && component.gameObject.scene.isLoaded
            && !EditorSceneManager.IsPreviewScene(component.gameObject.scene);
    }

    public static void StopAll()
    {
        foreach (UltimateFireEffect fire in fires) if (fire != null) fire.StopPreview();
        fires = Array.Empty<UltimateFireEffect>();
        foreach (UltimateHUD view in views) if (view != null) view.StopPreview();
        foreach (UltimatePortraitGlow glow in glows) if (glow != null) glow.StopPreview();
        foreach (AdaptiveHealthHUD health in healthViews) if (health != null) health.StopPreview();
        healthViews = Array.Empty<AdaptiveHealthHUD>();
        views = Array.Empty<UltimateHUD>();
        glows = Array.Empty<UltimatePortraitGlow>();
    }

    private static void BeforeSave(Scene scene, string path) { suspended = true; StopAll(); }
    private static void AfterSave(Scene scene) { suspended = false; previousTime = EditorApplication.timeSinceStartup; }
    private static void BeforeClose(Scene scene, bool removingScene) { StopAll(); }
    private static void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (state == PlayModeStateChange.ExitingEditMode)
        {
            suspended = true;
            StopAll();
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            suspended = false;
            previousTime = EditorApplication.timeSinceStartup;
        }
    }
}
