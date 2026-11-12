using UnityEngine;

/// <summary>Replace/disable this adapter when a campaign mission system owns the tracker.</summary>
public sealed class WaveMissionSource : MonoBehaviour
{
    [SerializeField] private WaveManager waves;
    [SerializeField] private MissionTracker tracker;
    // Texts.csv ids (plain words work too), so the words are rewritten in the one file with every other line.
    [SerializeField, Tooltip("Texts.csv id of the objective's title during a wave (or plain words).")]
    private string combatTitle = "wave.combat.title";
    [SerializeField, Tooltip("Texts.csv id of the objective's description during a wave (or plain words).")]
    private string combatDescription = "wave.combat.detail";
    private string ownedId;

    private void OnEnable()
    {
        // The HUD is a prefab shared between scenes, so it finds this scene's waves itself.
        if (waves == null) waves = FindFirstObjectByType<WaveManager>();
        if (waves != null) waves.ObjectiveChanged += Refresh;
        Refresh();
    }
    private void OnDisable()
    {
        if (waves != null) waves.ObjectiveChanged -= Refresh;
    }
    private void Refresh()
    {
        if (tracker == null || waves == null) return;
        if (!waves.IsRunning)
        {
            if (tracker.Current != null && tracker.Current.Id == ownedId) tracker.ClearObjective();
            ownedId = null;
            return;
        }
        if (waves.Phase == WaveManager.WavePhase.Countdown)
        {
            Set(new MissionObjective("wave.prepare." + (waves.CurrentWave + 1), Texts.Get("wave.prepare.title"),
                Texts.Get("wave.prepare.detail")));
            return;
        }
        string id = "wave.combat." + waves.CurrentWave;
        if (ownedId == id) tracker.SetProgress(id, waves.DefeatedEnemies, waves.TotalEnemiesThisWave);
        else Set(new MissionObjective(id, Texts.Cell(combatTitle), Texts.Cell(combatDescription), MissionProgressDisplay.Count,
            waves.DefeatedEnemies, waves.TotalEnemiesThisWave, Texts.Format("wave.cleared", waves.CurrentWave)));
    }
    private void Set(MissionObjective objective)
    {
        if (ownedId == objective.Id) return;
        ownedId = objective.Id;
        tracker.SetObjective(objective);
    }
}
