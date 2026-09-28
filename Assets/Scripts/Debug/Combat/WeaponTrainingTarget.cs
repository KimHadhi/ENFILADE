using System.Collections.Generic;
using UnityEngine;

public sealed class WeaponTrainingTarget : MonoBehaviour, IDamageReceiver
{
    [SerializeField, Min(1f)] private float maximumHealth = 1000f;
    private readonly HashSet<long> seen = new HashSet<long>();
    private readonly Queue<long> recent = new Queue<long>();
    public float Health { get; private set; }
    private void Awake() => ResetTarget();
    [ContextMenu("Reset Target")]
    public void ResetTarget() { Health = maximumHealth; seen.Clear(); recent.Clear(); }
    public DamageResult ReceiveHit(DamageHit hit)
    {
        if (hit.Id <= 0 || hit.Amount <= 0f || Health <= 0f) return DamageResult.Ignored;
        if (!seen.Add(hit.Id)) return DamageResult.Duplicate;
        recent.Enqueue(hit.Id); if (recent.Count > 256) seen.Remove(recent.Dequeue());
        Health = Mathf.Max(0f, Health - hit.Amount);
        return DamageResult.Damaged;
    }
    private void OnGUI()
    {
        var camera = Camera.main; if (camera == null) return;
        Vector3 screen = camera.WorldToScreenPoint(transform.position + Vector3.up * 1.3f);
        if (screen.z > 0f) GUI.Label(new Rect(screen.x - 80f, Screen.height - screen.y, 190f, 40f), $"{name}\nHP {Health:0}/{maximumHealth:0}");
    }
}
