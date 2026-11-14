using UnityEngine;

/// <summary>
/// An alarm wire across a tower room (the map gimmick card "오늘의 층 규칙": 경보 선), red and ankle high: the builder lays
/// three in every room, hidden, and the run shows those of the day's wire floor (TowerRun.Rules), and hides a room's once
/// someone walks through one (its lane's next room holds twice the swarm).
/// </summary>
[DisallowMultipleComponent]
public sealed class TowerWire : MonoBehaviour
{
    [Tooltip("The tower level of its room (TowerLayout).")]
    public int Level;
    [Tooltip("Its room's lane.")]
    public int Lane;
}
