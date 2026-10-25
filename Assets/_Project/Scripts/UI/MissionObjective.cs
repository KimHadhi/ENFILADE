using System;
using UnityEngine;

public enum MissionProgressDisplay { None, Count, Percent }

/// <summary>A mission can come from waves, interactions, travel, or any later mission system.</summary>
public sealed class MissionObjective
{
    public string Id { get; }
    public string Title { get; }
    public string Description { get; }
    public MissionProgressDisplay Display { get; }
    public int Current { get; }
    public int Target { get; }
    public float Progress => Target > 0 ? Mathf.Clamp01((float)Current / Target) : 0;
    public string ProgressText => Display == MissionProgressDisplay.Percent
        ? $"{Mathf.FloorToInt(Progress * 100)}%" : $"{Current} / {Target}";

    public MissionObjective(string id, string title, string description,
        MissionProgressDisplay display = MissionProgressDisplay.None, int current = 0, int target = 0)
    {
        if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A mission needs a stable id.", nameof(id));
        Id = id; Title = title ?? ""; Description = description ?? "";
        Target = Mathf.Max(0, target); Current = Mathf.Clamp(current, 0, Target);
        Display = Target > 0 ? display : MissionProgressDisplay.None;
    }

    public MissionObjective WithProgress(int current, int target) =>
        new MissionObjective(Id, Title, Description, Display, current, target);
}
