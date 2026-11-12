using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

/// <summary>
/// A bought effect prefab (PackFxLibrary) played as one of the game's moments and toned to the user's rules: restrained
/// and readable, never over the screen, short, the same on every PC (vfx-restraint, same-world-view). Each use is a row
/// of Tables/PackFx.csv:
/// - which prefab, and how big: for others' eyes (scale) and at this PC's own eye (selfScale, its own muzzle). It draws
///   smaller still when it goes off nearer the camera than near (m), so it never fills the view;
/// - how fast it runs (speed);
/// - which of its parts are left out (drop): the pack's big dark fuzz and smoke clouds (5 to 8 m across) and its own
///   muzzle flash where the gun has one; more at this PC's own eye (selfDrop);
/// - a tint for some parts (tint, tintParts): a bounty's skull in the mark's teal;
/// - which are fired at once (pop): the hero, so the moment lands now rather than after the pack's own delay or loop;
/// - when its loops stop (stopAfter) and when it is gone (life; 0 or less: when its caller ends it, Hold or End).
/// The prefab is never changed: all of it is done to the copy, which this component then ends.
/// </summary>
[DisallowMultipleComponent]
public sealed class PackFx : MonoBehaviour
{
    public const string TableName = "PackFx";

    private float stopAt = -1f, goneAt;
    private bool stopped;

    /// <summary>
    /// Plays <paramref name="use"/> at <paramref name="at"/>, its forward along <paramref name="facing"/>.
    /// <paramref name="self"/>: it goes off at this PC's own eye (their own muzzle). Returns the copy (a flying round is
    /// moved by its caller), or null when the use is off or its prefab is missing.
    /// </summary>
    public static GameObject Play(string use, Vector3 at, Quaternion facing, bool self = false)
    {
        if (!Application.isPlaying || string.IsNullOrEmpty(use) || Number(use, "on", 1f) < .5f) return null;
        var library = PackFxLibrary.Instance;
        var prefab = library != null ? library.Find(Text(use, "prefab")) : null;
        if (prefab == null) return null;
        float scale = Mathf.Max(.01f, Number(use, self ? "selfScale" : "scale", Number(use, "scale", 1f)));
        // Close to the camera it draws smaller, down to a share of itself, so it never covers the view.
        float near = Number(use, "near", 0f);
        var eye = Camera.main;
        if (near > 0f && eye != null)
        {
            float distance = Vector3.Distance(eye.transform.position, at);
            if (distance < near) scale *= Mathf.Lerp(Mathf.Clamp01(Number(use, "nearShare", .35f)), 1f, distance / near);
        }
        var copy = Instantiate(prefab, at, facing);
        copy.name = prefab.name + " (" + use + ")";
        copy.transform.localScale = prefab.transform.localScale * scale;
        Tone(copy, use, self);
        var life = copy.AddComponent<PackFx>();
        float now = Time.time, stopAfter = Number(use, "stopAfter", -1f), lasts = Number(use, "life", 2f);
        life.stopAt = stopAfter >= 0f ? now + stopAfter : -1f;
        // A life of 0 or less: it lasts until its caller ends it (a mark's skull, an aura held for a while).
        life.goneAt = lasts > 0f ? now + Mathf.Max(.1f, lasts) : float.MaxValue;
        return copy;
    }

    /// <summary>It loops for <paramref name="seconds"/> more, then stops, and is gone <paramref name="fade"/> after.</summary>
    public void Hold(float seconds, float fade)
    {
        stopped = false;
        stopAt = Time.time + Mathf.Max(0f, seconds);
        goneAt = stopAt + Mathf.Max(.05f, fade);
    }

    /// <summary>It stops now and is gone <paramref name="fade"/> after.</summary>
    public void End(float fade)
    {
        StopNow();
        goneAt = Mathf.Min(goneAt, Time.time + Mathf.Max(.05f, fade));
    }

    /// <summary>Every prefab the library holds, for a warm-up that draws each once behind the loading screen.</summary>
    public static IEnumerable<GameObject> Prefabs()
    {
        var library = PackFxLibrary.Instance;
        if (library == null || library.entries == null) yield break;
        foreach (var entry in library.entries)
            if (entry.prefab != null) yield return entry.prefab;
    }

    /// <summary>Its loops stop now and what is out fades; it is gone at its time.</summary>
    public void StopNow()
    {
        if (stopped) return;
        stopped = true;
        foreach (var system in GetComponentsInChildren<ParticleSystem>()) system.Stop(false, ParticleSystemStopBehavior.StopEmitting);
    }

    private void Update()
    {
        float now = Time.time;
        if (!stopped && stopAt >= 0f && now >= stopAt) StopNow();
        if (now >= goneAt) Destroy(gameObject);
    }

    // The row's changes to the copy: its speed, the parts left out (more at this PC's own eye), its tint, the parts fired
    // at once, its lights.
    private static void Tone(GameObject copy, string use, bool self)
    {
        float speed = Mathf.Max(.05f, Number(use, "speed", 1f));
        if (!Mathf.Approximately(speed, 1f))
            foreach (var system in copy.GetComponentsInChildren<ParticleSystem>(true))
            {
                var main = system.main;
                main.simulationSpeed *= speed;
            }
        Drop(copy.transform, Text(use, "drop"));
        if (self) Drop(copy.transform, Text(use, "selfDrop"));
        if (ColorUtility.TryParseHtmlString(Text(use, "tint"), out Color tint))
        {
            string only = Text(use, "tintParts");
            if (string.IsNullOrEmpty(only))
                foreach (var system in copy.GetComponentsInChildren<ParticleSystem>(true)) Tint(system, tint);
            else
                foreach (string part in Parts(only))
                    foreach (var found in PartsNamed(copy.transform, part))
                        if (found.TryGetComponent(out ParticleSystem system)) Tint(system, tint);
        }
        foreach (string part in Parts(Text(use, "pop")))
        {
            int colon = part.LastIndexOf(':');
            string path = colon > 0 ? part.Substring(0, colon) : part;
            int count = colon > 0 && int.TryParse(part.Substring(colon + 1), NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : 1;
            foreach (var found in PartsNamed(copy.transform, path))
            {
                if (!found.gameObject.activeInHierarchy || !found.TryGetComponent(out ParticleSystem system)) continue;
                // Once, now: no wait, no loop, nothing more of it after.
                system.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
                var main = system.main;
                main.startDelay = 0f;
                main.loop = false;
                var emission = system.emission;
                emission.enabled = false;
                system.Play(false);
                system.Emit(Mathf.Clamp(count, 1, 20));
            }
        }
        if (Number(use, "lights", 1f) < .5f)
            foreach (var light in copy.GetComponentsInChildren<Light>(true)) light.enabled = false;
    }

    private static void Drop(Transform root, string list)
    {
        foreach (string part in Parts(list))
            foreach (var found in PartsNamed(root, part)) found.gameObject.SetActive(false);
    }

    // Its start colour times the tint, its alpha kept: a bounty's skull in the mark's teal.
    private static void Tint(ParticleSystem system, Color tint)
    {
        var main = system.main;
        var start = main.startColor;
        switch (start.mode)
        {
            case ParticleSystemGradientMode.Color:
                start.color = Times(start.color, tint);
                break;
            case ParticleSystemGradientMode.TwoColors:
                start.colorMin = Times(start.colorMin, tint);
                start.colorMax = Times(start.colorMax, tint);
                break;
            default:
                start = new ParticleSystem.MinMaxGradient(new Color(tint.r, tint.g, tint.b, 1f));
                break;
        }
        main.startColor = start;
    }

    private static Color Times(Color a, Color tint) => new Color(a.r * tint.r, a.g * tint.g, a.b * tint.b, a.a);

    private static IEnumerable<string> Parts(string list)
    {
        if (string.IsNullOrWhiteSpace(list)) yield break;
        foreach (string part in list.Split('|'))
            if (!string.IsNullOrWhiteSpace(part)) yield return part.Trim();
    }

    // A part by its path under the copy ("Loop/FuzzAlpha_Soft": every one there) or by its name anywhere in it.
    private static IEnumerable<Transform> PartsNamed(Transform root, string part)
    {
        bool path = part.IndexOf('/') >= 0;
        foreach (var t in root.GetComponentsInChildren<Transform>(true))
        {
            if (t == root) continue;
            if (path ? PathOf(t, root) == part : t.name == part) yield return t;
        }
    }

    private static string PathOf(Transform t, Transform root)
    {
        string path = t.name;
        for (var up = t.parent; up != null && up != root; up = up.parent) path = up.name + "/" + path;
        return path;
    }

    /// <summary>A number from Tables/PackFx.csv, or the fallback.</summary>
    public static float Number(string row, string key, float fallback)
        => GameTables.TryGetRow(TableName, row, out var cells) && cells.TryGetValue(key, out string text)
            && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : fallback;

    /// <summary>Words from Tables/PackFx.csv (a prefab's name, a list of parts), or empty.</summary>
    public static string Text(string row, string key)
        => GameTables.TryGetRow(TableName, row, out var cells) && cells.TryGetValue(key, out string text) && text != null ? text.Trim() : "";
}
