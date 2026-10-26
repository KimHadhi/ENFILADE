using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

/// <summary>One synchronous entry point for the existing Edit Mode regression checks.</summary>
public static class ProjectChecks
{
    [MenuItem("Tools/Validation/Run Edit Mode Checks")]
    public static void RunEditMode()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play Mode before running checks.");

        var errors = new List<string>();
        void Collect(string message, string stack, LogType type)
        {
            if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                errors.Add(message);
        }

        Application.logMessageReceived += Collect;
        try
        {
            MovementAbilityChecks.RunAll();
            WeaponFoundationChecks.Run();
            WeaponSlotsMeleeChecks.Run();
            RangedCombatChecks.Run();
            MissionHUDChecks.Run();
            UltimateHUDChecks.Run();
            UltimateFireSetup.Check();
            PlayerHealthHUDSetup.Check();
            ChatHUDSetup.Check();
            if (errors.Count > 0)
                throw new InvalidOperationException("Errors during project checks:\n" + string.Join("\n", errors));
            Debug.Log("PROJECT_EDIT_MODE_CHECKS_PASSED");
        }
        finally { Application.logMessageReceived -= Collect; }
    }
}
