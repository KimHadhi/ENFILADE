using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.SceneManagement;
using Random = UnityEngine.Random;

/// <summary>
/// Friend feedback C3: each map's background sound, and since 2026-11-10 each area's (user: "구역별로"). As a scene
/// loads, its row in Tables/Ambience (id = the scene's name) is the map's own sound, heard everywhere outside the
/// areas; each AmbienceArea in the scene names another row, heard while this PC's ears (GameAudio.Listener: the
/// player's camera, or the spectator's) are inside its box. Moving between them crossfades over the fadeIn of the
/// sound being entered; the map's own fades in over its fadeIn as the scene starts. A row is one or more looping
/// files under Resources/Audio/Ambience, each at its own volume, all on the Ambience channel (its own volume in the
/// Esc menu). Every sound the scene can play is loaded as it loads (no hitch on entering a room), and only those
/// being heard play. A map with no row and no areas, or files not there yet, stays quiet.
/// </summary>
public sealed class MapAmbience : MonoBehaviour
{
    public const string TableName = "Ambience";
    private static readonly char[] Split = { ';' };

    private sealed class Sound
    {
        public readonly List<AudioSource> Layers = new List<AudioSource>();
        public readonly List<float> Volumes = new List<float>();
        public float FadeIn = 3f, Level;
        public bool Playing;
    }

    // Every row this scene can play, by id; null where the row is missing or none of its files are there.
    private readonly Dictionary<string, Sound> sounds = new Dictionary<string, Sound>(StringComparer.OrdinalIgnoreCase);
    private Sound map;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Hook()
    {
        SceneManager.sceneLoaded -= OnSceneLoaded;
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    // The scene's objects are awake and on by now, so its areas have registered themselves.
    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        bool hasRow = GameTables.TryGetRow(TableName, scene.name, out _);
        bool hasAreas = false;
        foreach (var area in AmbienceArea.All)
            if (area != null && area.gameObject.scene == scene) { hasAreas = true; break; }
        if (!hasRow && !hasAreas) return;
        var holder = new GameObject("Map Ambience");
        SceneManager.MoveGameObjectToScene(holder, scene);
        RuntimeSceneHierarchy.Group(holder, RuntimeSceneHierarchy.Systems, "Audio");
        holder.AddComponent<MapAmbience>().Begin(scene);
    }

    private void Begin(Scene scene)
    {
        map = GameTables.TryGetRow(TableName, scene.name, out _) ? Load(scene.name) : null;
        foreach (var area in AmbienceArea.All)
            if (area != null && area.gameObject.scene == scene) Load(area.Sound);
        bool any = false;
        foreach (var sound in sounds.Values) any |= sound != null;
        if (!any) Destroy(gameObject);
    }

    // A row's files as stopped, silent sources; each id once.
    private Sound Load(string id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (sounds.TryGetValue(id, out var known)) return known;
        sounds[id] = null;
        if (!GameTables.TryGetRow(TableName, id, out var row))
        {
            Debug.LogWarning($"[Ambience] {gameObject.scene.name}: Tables/{TableName} has no row '{id}'.");
            return null;
        }
        var sound = new Sound();
        string[] clips = Text(row, "clips").Split(Split, StringSplitOptions.RemoveEmptyEntries);
        string[] levels = Text(row, "volumes").Split(Split, StringSplitOptions.RemoveEmptyEntries);
        if (float.TryParse(Text(row, "fadeIn"), NumberStyles.Float, CultureInfo.InvariantCulture, out float fade))
            sound.FadeIn = Mathf.Max(.01f, fade);
        for (int i = 0; i < clips.Length; i++)
        {
            string path = clips[i].Trim();
            var clip = Resources.Load<AudioClip>(path);
            if (clip == null) { Debug.LogWarning($"[Ambience] {id}: no clip at Resources/{path} yet."); continue; }
            float volume = i < levels.Length && float.TryParse(levels[i].Trim(), NumberStyles.Float,
                CultureInfo.InvariantCulture, out float v) ? Mathf.Clamp01(v) : 1f;
            var source = gameObject.AddComponent<AudioSource>();
            source.clip = clip;
            source.loop = true;
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.priority = 16;
            GameAudio.Bind(source, AudioChannel.Ambience);
            GameAudio.SetGain(source, 0f);
            sound.Layers.Add(source);
            sound.Volumes.Add(volume);
        }
        if (sound.Layers.Count == 0) return null;
        sounds[id] = sound;
        return sound;
    }

    private void Update()
    {
        Sound wanted = map;
        var listener = GameAudio.Listener;
        if (listener != null)
        {
            var area = AmbienceArea.Chosen(listener.transform.position);
            if (area != null && sounds.TryGetValue(area.Sound, out var there) && there != null) wanted = there;
        }
        float dt = Time.unscaledDeltaTime;
        foreach (var sound in sounds.Values)
        {
            if (sound == null) continue;
            // A crossfade at the pace of the sound being entered (or, entering silence, of the one leaving).
            float pace = (wanted ?? sound).FadeIn;
            sound.Level = Mathf.MoveTowards(sound.Level, sound == wanted ? 1f : 0f, dt / pace);
            if (sound.Level > 0f && !sound.Playing) Play(sound);
            else if (sound.Level <= 0f && sound.Playing) Stop(sound);
            for (int i = 0; i < sound.Layers.Count; i++)
                if (sound.Layers[i] != null) GameAudio.SetGain(sound.Layers[i], sound.Level * sound.Volumes[i]);
        }
    }

    private static void Play(Sound sound)
    {
        sound.Playing = true;
        foreach (var layer in sound.Layers)
        {
            if (layer == null) continue;
            // Each layer from its own point in its loop, so two of the same length don't line up, and a room
            // entered again doesn't start over.
            layer.time = Random.Range(0f, layer.clip.length);
            layer.Play();
        }
    }

    private static void Stop(Sound sound)
    {
        sound.Playing = false;
        foreach (var layer in sound.Layers) if (layer != null) layer.Stop();
    }

    private static string Text(IReadOnlyDictionary<string, string> row, string key)
        => row.TryGetValue(key, out string text) ? text.Trim() : "";
}
