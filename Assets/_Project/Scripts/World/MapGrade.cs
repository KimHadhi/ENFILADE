using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

/// <summary>
/// Each map's colour grade (map dressing, the user 2026-11-13: "후처리 진행"), from its row of Tables/MapGrade (id = the
/// scene's name): a global volume made when the scene loads, under every effect's own volume, so it needs no rebuild.
/// It grades only with what no runtime effect writes. The kill screen (KillConfirmEffect), low health (LowHealthGrade),
/// the death screen and the fall warp write exposure, contrast, saturation, colour filter, white balance, bloom,
/// vignette, lens distortion and chromatic aberration as absolute values (Docs/MaintenanceGuide, 화면 후처리), so a map
/// moving any of those would make every kill flatten or darken its screen. This uses split toning (a colour mixed into
/// the shadows and one into the highlights, worked out in gamma: the midtones, the avatars' skin, take half of each and
/// the two mostly cancel there) and Color Curves: the master curve as an S through linear 18% grey (contrast without
/// ColorAdjustments; the project grades in LDR, where the LUT and the curves see linear values, so the pivot is the
/// linear mid-grey, not 0.5) and luminance against saturation (how much colour the dark parts keep). Both come before
/// the global saturation, so the drained death screen and low health still go grey. A scene with no row (the hub,
/// where the avatars are looked at closely, and the menus) gets no volume. Every PC reads the same table, so every view
/// matches. In the editor, saving the table grades the running scene again (GameTables.Reloaded).
/// </summary>
public sealed class MapGrade : MonoBehaviour
{
    public const string TableName = "MapGrade";
    // Under every other volume: the effects' (the kill screen at 200, the loan's neutral at 199, low health, the fall
    // warp, the death screen) and any a scene places at the default 0.
    private const float Priority = -10f;
    // The contrast curve's pivot (linear 18% grey), where it is sampled (closer in the darks, where the eye is), and where
    // the shadows' own saturation has faded back to the image's.
    private const float Pivot = .18f, ShadowsEnd = .15f, SteepestEnd = 4f;
    private static readonly float[] CurveAt = { 0f, .005f, .015f, .035f, .07f, .12f, .18f, .28f, .4f, .55f, .7f, .85f, 1f };

    private VolumeProfile profile;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Hook()
    {
        SceneManager.sceneLoaded -= Loaded;
        SceneManager.sceneLoaded += Loaded;
        GameTables.Reloaded -= Regrade;
        GameTables.Reloaded += Regrade;
        Grade(SceneManager.GetActiveScene());
    }

    // A map loaded in place of the last (an additive scene, a UI or a lighting piece, is not a map of its own).
    private static void Loaded(Scene scene, LoadSceneMode mode)
    {
        if (Application.isPlaying && mode == LoadSceneMode.Single) Grade(scene);
    }

    // A table saved in the editor: the running map's grade made again from its row, or taken away with it. The hook
    // outlives Play in the editor, and a grade must never be made into a scene being edited.
    private static void Regrade()
    {
        if (!Application.isPlaying) return;
        foreach (var old in FindObjectsByType<MapGrade>(FindObjectsInactive.Include, FindObjectsSortMode.None)) Destroy(old.gameObject);
        Grade(SceneManager.GetActiveScene());
    }

    private static void Grade(Scene scene)
    {
        if (!scene.IsValid() || !GameTables.TryGetRow(TableName, scene.name, out var row)) return;
        var host = new GameObject("Map Grade (Tables/MapGrade)");
        if (host.scene != scene) SceneManager.MoveGameObjectToScene(host, scene);
        RuntimeSceneHierarchy.Group(host, "EFFECTS", "Screen");
        host.AddComponent<MapGrade>().Build(row);
    }

    private void Build(IReadOnlyDictionary<string, string> row)
    {
        profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "Map Grade (Runtime)";
        profile.hideFlags = HideFlags.HideAndDontSave;
        var split = profile.Add<SplitToning>();
        split.shadows.Override(Colour(row, "shadowTint"));
        split.highlights.Override(Colour(row, "highlightTint"));
        split.balance.Override(Mathf.Clamp(Number(row, "tintBalance", 0f), -100f, 100f));
        float contrast = Mathf.Clamp(Number(row, "contrast", 0f), -.5f, .6f);
        float darkColour = Mathf.Clamp(Number(row, "shadowSaturation", 1f), 0f, 2f);
        bool steeper = Mathf.Abs(contrast) > .001f, darker = Mathf.Abs(darkColour - 1f) > .001f;
        if (steeper || darker)
        {
            var curves = profile.Add<ColorCurves>();
            if (steeper) curves.master.Override(ContrastCurve(1f + contrast));
            // The shader doubles the curve (0.5 = as it is): flat at both keys, so the shadows ease into the rest.
            if (darker)
                curves.lumVsSat.Override(new TextureCurve(new[] { new Keyframe(0f, .5f * darkColour, 0f, 0f), new Keyframe(ShadowsEnd, .5f, 0f, 0f) },
                    .5f, false, new Vector2(0f, 1f)));
        }
        var volume = gameObject.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = Priority;
        volume.weight = 1f;
        volume.sharedProfile = profile;
    }

    // y = p (x / p)^k under the pivot p and 1 - (1 - p) ((1 - x) / (1 - p))^k over it: the pivot stays where it is with a
    // slope of k (k > 1 more contrast, < 1 less), black stays black and white white. It is the same contrast in log terms
    // as ColorAdjustments' (around the same grey), sampled with each key's own slope so the curve stays smooth.
    private static TextureCurve ContrastCurve(float k)
    {
        var keys = new Keyframe[CurveAt.Length];
        for (int i = 0; i < CurveAt.Length; i++)
        {
            float x = CurveAt[i];
            bool under = x <= Pivot;
            float t = under ? x / Pivot : (1f - x) / (1f - Pivot);
            float y = under ? Pivot * Mathf.Pow(t, k) : 1f - (1f - Pivot) * Mathf.Pow(t, k);
            // At black and white the slope is 0 (k > 1) or would be endless (k < 1).
            float slope = t <= 0f ? (k < 1f ? SteepestEnd : 0f) : Mathf.Min(SteepestEnd, k * Mathf.Pow(t, k - 1f));
            keys[i] = new Keyframe(x, y, slope, slope);
        }
        return new TextureCurve(keys, 0f, false, new Vector2(0f, 1f));
    }

    private void OnDestroy()
    {
        if (profile == null) return;
        // The curves hold textures of their own; the components and the profile are this grade's alone.
        foreach (var component in profile.components)
        {
            if (component == null) continue;
            component.Release();
            Destroy(component);
        }
        Destroy(profile);
        profile = null;
    }

    private static float Number(IReadOnlyDictionary<string, string> row, string key, float fallback)
        => row.TryGetValue(key, out string text) && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? value : fallback;

    // A split toning colour as written (#808080, mid-grey, mixes nothing in).
    private static Color Colour(IReadOnlyDictionary<string, string> row, string key)
        => row.TryGetValue(key, out string text) && ColorUtility.TryParseHtmlString(text.Trim(), out Color colour) ? colour : Color.grey;
}
