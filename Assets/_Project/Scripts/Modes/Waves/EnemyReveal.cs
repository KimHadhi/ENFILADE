using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Horde-style: during a wave, once revealRemaining or fewer enemies are left (Waves.csv), the living ones show through walls
/// in revealColor, on every PC (guests go by the host's count, WaveSync). Only waves turn it on, so objective and stealth
/// play never show enemies. Each enemy's body is filled in revealColor where something stands in front of it, with a band
/// of the same colour round its hidden shape (EnemyOutlines); where it can be seen it shows as it is.
/// </summary>
[DisallowMultipleComponent, RequireComponent(typeof(WaveManager))]
public sealed class EnemyReveal : MonoBehaviour
{
    private const float CheckEvery = .2f;
    private readonly HashSet<EnemyHealth> shown = new HashSet<EnemyHealth>();
    private readonly List<EnemyHealth> done = new List<EnemyHealth>();
    private WaveManager waves;
    private float nextCheck;

    /// <summary>A wave's last enemy's look on its body (EnemyOutlines): only where hidden, the band and the fill in this colour.</summary>
    public static OutlineLook LookOf(Color colour)
    {
        var band = colour;
        band.a = 1f;
        return new OutlineLook(Color.clear, band, colour);
    }

    private void Awake() => waves = GetComponent<WaveManager>();

    private void OnDestroy()
    {
        foreach (var enemy in shown) if (enemy != null) EnemyOutlines.Clear(enemy, OutlineSource.Wave);
        shown.Clear();
    }

    private void Update()
    {
        if (Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + CheckEvery;
        bool on = waves.RevealsRemaining;
        done.Clear();
        foreach (var enemy in shown)
            if (!on || enemy == null || !enemy.IsAlive) done.Add(enemy);
        foreach (var enemy in done)
        {
            if (enemy != null) EnemyOutlines.Clear(enemy, OutlineSource.Wave);
            shown.Remove(enemy);
        }
        if (!on) return;
        var look = LookOf(waves.RevealColor);
        foreach (var enemy in EnemyHealth.Living)
        {
            if (enemy == null || !enemy.IsAlive) continue;
            EnemyOutlines.Set(enemy, OutlineSource.Wave, look);
            shown.Add(enemy);
        }
    }
}
