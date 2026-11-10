using UnityEngine;

/// <summary>Replace/disable this adapter when a campaign mission system owns the tracker.</summary>
public sealed class WaveMissionSource : MonoBehaviour
{
    [SerializeField] private WaveManager waves;
    [SerializeField] private MissionTracker tracker;
    [SerializeField] private string combatTitle = "남은 적을 처치하세요";
    [SerializeField, TextArea] private string combatDescription = "구역 내의 적을 모두 처치하세요. 추가로 투입되는 적까지 정리하면 현재 공세가 종료됩니다.";
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
        else Set(new MissionObjective(id, combatTitle, combatDescription, MissionProgressDisplay.Count,
            waves.DefeatedEnemies, waves.TotalEnemiesThisWave, Texts.Format("wave.cleared", waves.CurrentWave)));
    }
    private void Set(MissionObjective objective)
    {
        if (ownedId == objective.Id) return;
        ownedId = objective.Id;
        tracker.SetObjective(objective);
    }
}
