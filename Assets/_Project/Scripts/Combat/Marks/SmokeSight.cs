using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Tells every see-through drawing where the smoke is, so smoke hides as a wall does (the user, 2026-11-18: "연막탄
/// 내부에서도 핑 찍힌 적이 선명하게 보이도록"). What is drawn through walls goes by one rule, SeeThrough.hlsl: a body shows
/// as a flat silhouette where it is hidden from the eye. Until now "hidden" only meant something solid in front (the
/// scene's depth). Smoke is puffs that write no depth and are drawn over a body and its ping outline alike, so a pinged
/// enemy in a cloud kept only its diamond. The rule now counts smoke too, and by the game's own measure of it: the
/// clouds as the spheres a zombie's eyes are judged by (SmokeScreen.Through) and the length of smoke that blinds one
/// (GrenadeRules smokeHide), never the drawn puffs. So what a zombie cannot see through, a mark cuts through, on every PC
/// the same. It covers everything EnemyOutlines draws: pings, bounties, a holder, the Ghost's sight, a wave's last
/// enemies.
/// No object of its own: it listens for each frame's rendering and does nothing while no cloud hangs.
/// </summary>
public static class SmokeSight
{
    /// <summary>Most clouds the shaders take at once (SeeThrough.hlsl ENFILADE_SMOKE_MOST): the oldest that still hang.</summary>
    public const int Most = 8;

    private static readonly int SpheresId = Shader.PropertyToID("_SmokeSpheres"), CountId = Shader.PropertyToID("_SmokeCount"),
        HideId = Shader.PropertyToID("_SmokeHide");
    private static readonly Vector4[] spheres = new Vector4[Most];
    // How many clouds the shaders were last told of; -1 before the first telling.
    private static int told = -1;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Begin()
    {
        told = -1;
        RenderPipelineManager.beginContextRendering -= Tell;
        RenderPipelineManager.beginContextRendering += Tell;
    }

    // Before anything is drawn this frame: the clouds as they are now. An array's length is fixed by its first setting,
    // so all eight go every time and the count says how many are real.
    private static void Tell(ScriptableRenderContext context, List<Camera> cameras)
    {
        int count = Fill(SmokeScreen.Clouds, spheres);
        if (count == 0 && told == 0) return;
        told = count;
        Shader.SetGlobalFloat(CountId, count);
        if (count == 0) return;
        Shader.SetGlobalVectorArray(SpheresId, spheres);
        Shader.SetGlobalFloat(HideId, SmokeScreen.HideLength);
    }

    /// <summary>
    /// The clouds as spheres (xyz the middle in the world, w the radius now, m), as many as <paramref name="into"/> holds;
    /// returns how many. Apart from the telling so it can be checked without a screen.
    /// </summary>
    public static int Fill(IReadOnlyList<SmokeScreen> clouds, Vector4[] into)
    {
        int count = 0;
        for (int i = 0; i < clouds.Count && count < into.Length; i++)
        {
            var cloud = clouds[i];
            if (cloud == null) continue;
            Vector3 middle = cloud.Middle;
            into[count++] = new Vector4(middle.x, middle.y, middle.z, cloud.Radius);
        }
        return count;
    }
}
