using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Horde-style: during a wave, once revealRemaining or fewer enemies are left (Waves.csv), the living ones show through walls
/// in revealColor, on every PC (guests go by the host's count, WaveSync). Only waves turn it on, so objective and stealth
/// play never show enemies. Each enemy gets a copy of each of its meshes in Hidden/ENFILADE/RevealThroughWalls, drawn only
/// where something stands in front of it. A copy, as DownedAura does for downed teammates, because an extra material on
/// the real mesh would cover only its last submesh and would sit among the hit flash's material slots.
/// </summary>
[DisallowMultipleComponent, RequireComponent(typeof(WaveManager))]
public sealed class EnemyReveal : MonoBehaviour
{
    private const float CheckEvery = .2f;
    private static readonly int ColorId = Shader.PropertyToID("_Color");
    private readonly Dictionary<EnemyHealth, List<GameObject>> shown = new Dictionary<EnemyHealth, List<GameObject>>();
    private readonly List<EnemyHealth> done = new List<EnemyHealth>();
    private readonly List<Renderer> found = new List<Renderer>();
    private WaveManager waves;
    private Material material;
    private float nextCheck;

    private void Awake()
    {
        waves = GetComponent<WaveManager>();
        var shader = Resources.Load<Shader>("Combat/Shaders/RevealThroughWalls");
        if (shader != null) material = new Material(shader) { name = "Reveal Through Walls (Runtime)", hideFlags = HideFlags.HideAndDontSave };
    }

    private void OnDestroy()
    {
        foreach (var copies in shown.Values) Hide(copies);
        shown.Clear();
        if (material != null) Destroy(material);
    }

    private void Update()
    {
        if (material == null || Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + CheckEvery;
        bool on = waves.RevealsRemaining;
        material.SetColor(ColorId, waves.RevealColor);
        done.Clear();
        foreach (var pair in shown)
            if (!on || pair.Key == null || !pair.Key.IsAlive) done.Add(pair.Key);
        foreach (var enemy in done)
        {
            Hide(shown[enemy]);
            shown.Remove(enemy);
        }
        if (!on) return;
        foreach (var enemy in EnemyHealth.Living)
            if (enemy != null && enemy.IsAlive && !shown.ContainsKey(enemy)) shown.Add(enemy, Show(enemy));
    }

    private List<GameObject> Show(EnemyHealth enemy)
    {
        var copies = new List<GameObject>();
        enemy.GetComponentsInChildren(found);
        foreach (var source in found)
        {
            if (!source.enabled || !source.gameObject.activeInHierarchy) continue;
            Renderer copy;
            Mesh mesh;
            if (source is SkinnedMeshRenderer skinned && skinned.sharedMesh != null)
            {
                mesh = skinned.sharedMesh;
                var glow = new GameObject("Reveal", typeof(SkinnedMeshRenderer)).GetComponent<SkinnedMeshRenderer>();
                glow.transform.SetParent(skinned.transform, false);
                glow.sharedMesh = mesh;
                glow.bones = skinned.bones;
                glow.rootBone = skinned.rootBone;
                glow.updateWhenOffscreen = true;
                copy = glow;
            }
            else if (source is MeshRenderer && source.TryGetComponent(out MeshFilter filter) && filter.sharedMesh != null)
            {
                mesh = filter.sharedMesh;
                var holder = new GameObject("Reveal", typeof(MeshFilter), typeof(MeshRenderer));
                holder.transform.SetParent(source.transform, false);
                holder.GetComponent<MeshFilter>().sharedMesh = mesh;
                copy = holder.GetComponent<MeshRenderer>();
            }
            else continue;
            copy.gameObject.layer = source.gameObject.layer;
            copy.shadowCastingMode = ShadowCastingMode.Off;
            copy.receiveShadows = false;
            var slots = new Material[Mathf.Max(1, mesh.subMeshCount)];
            for (int i = 0; i < slots.Length; i++) slots[i] = material;
            copy.sharedMaterials = slots;
            copies.Add(copy.gameObject);
        }
        return copies;
    }

    private static void Hide(List<GameObject> copies)
    {
        foreach (var copy in copies) if (copy != null) Destroy(copy);
    }
}
