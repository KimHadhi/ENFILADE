using System;
using UnityEngine;

public sealed class MissionTracker : MonoBehaviour
{
    public MissionObjective Current { get; private set; }
    public event Action<MissionObjective, bool> Changed;

    public void SetObjective(MissionObjective objective, bool announce = true)
    {
        if (objective == null) { ClearObjective(); return; }
        bool isNew = Current == null || Current.Id != objective.Id || Current.Title != objective.Title;
        Current = objective;
        Changed?.Invoke(Current, announce && isNew);
    }

    // Progress is deliberately silent: a kill must never replay the new-objective alarm.
    public void SetProgress(string id, int current, int target)
    {
        if (Current == null || Current.Id != id) return;
        SetObjective(Current.WithProgress(current, target), false);
    }

    public void ClearObjective()
    {
        Current = null;
        Changed?.Invoke(null, false);
    }
}
