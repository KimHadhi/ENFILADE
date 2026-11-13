using KINEMATION.RetargetPro.Editor.Scripts.Bakers;
using KINEMATION.RetargetPro.Runtime;
using KINEMATION.RetargetPro.Runtime.Features.FPSRetargeting;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// GunClipRetarget: True size. One part of the class; the rest is in GunClipRetarget.cs and the files beside it.
internal static partial class GunClipRetarget
{
    // The scale the game fits this skin to (PlayerBody.FitScaleOf, eyes to the standing camera height of Player.prefab).
    internal static float FitScaleOf(GameObject skin)
    {
        var player = Resources.Load<GameObject>("Characters/Player");
        var pivot = player != null ? player.transform.Find("CrouchPivot") : null;
        if (pivot == null) return 1f;
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(skin);
        try
        {
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            return PlayerBody.FitScaleOf(instance.GetComponentInChildren<Animator>(), instance.transform, pivot.localPosition.y);
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    // Retarget Pro's FPS feature scales where the gun goes and where the hands sit on it by the arms' length ratio
    // (FPSRetargetFeatureState._fpsScale), as if the gun shrank or grew with the body. Our guns are their true size, so
    // the hands missed their grips and the gun sat too near the eyes on a body with shorter arms (user, 2026-11-07). Made
    // for the body at the size it plays at, every length on it comes out in KINEMATION's true metres: the scale is one
    // over the body's fit scale. The field is private to Retarget Pro and read through reflection; without it, the bake
    // keeps Retarget Pro's own scale and the report says so.
    private static bool TrueSize(RetargetAnimBaker baker, float fit)
    {
        const System.Reflection.BindingFlags Private = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        var states = baker.RetargetComponent != null
            ? typeof(RetargetProComponent).GetField("_retargetFeatureStates", Private)?.GetValue(baker.RetargetComponent) as System.Collections.IList
            : null;
        var scale = typeof(FPSRetargetFeatureState).GetField("_fpsScale", Private);
        if (states == null || scale == null || fit <= 0f)
        {
            Debug.LogWarning("GunClipRetarget: Retarget Pro's FPS scale could not be set; the guns are baked at Retarget Pro's own arm-length scale.");
            return false;
        }
        bool set = false;
        foreach (var state in states)
            if (state is FPSRetargetFeatureState)
            {
                scale.SetValue(state, 1f / fit);
                set = true;
            }
        return set;
    }
}
