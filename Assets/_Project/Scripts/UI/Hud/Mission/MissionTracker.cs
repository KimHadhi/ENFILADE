using System;
using System.Collections.Generic;
using UnityEngine;

public sealed class MissionTracker : MonoBehaviour
{
    private readonly List<string> finished = new List<string>();

    public MissionObjective Current { get; private set; }
    /// <summary>What this run has got done, oldest first. Goals with no DoneTitle aren't worth remembering.</summary>
    public IReadOnlyList<string> Finished => finished;
    public event Action<MissionObjective, bool> Changed;

    public void SetObjective(MissionObjective objective, bool announce = true)
    {
        if (objective == null) { ClearObjective(); return; }
        bool isNew = Current == null || Current.Id != objective.Id || Current.Title != objective.Title;
        if (isNew && Current != null && !string.IsNullOrEmpty(Current.DoneTitle)) finished.Add(Current.DoneTitle);
        Current = objective;
        Changed?.Invoke(Current, announce && isNew);
    }

    // Progress is deliberately silent: a kill must never replay the new-objective alarm.
    public void SetProgress(string id, int current, int target)
    {
        if (Current == null || Current.Id != id) return;
        SetObjective(Current.WithProgress(current, target), false);
    }

    /// <summary>The run's objectives ended: stopped or wiped, not done, so the list starts over.</summary>
    public void ClearObjective()
    {
        Current = null;
        finished.Clear();
        Changed?.Invoke(null, false);
    }
}
