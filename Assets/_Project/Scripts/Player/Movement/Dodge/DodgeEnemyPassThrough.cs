using System.Collections.Generic;
using UnityEngine;

/// <summary>Ignores only player/enemy body pairs. World collision and attack queries stay enabled.</summary>
[DisallowMultipleComponent]
public sealed class DodgeEnemyPassThrough : MonoBehaviour
{
    private PlayerMovement player;
    private CharacterController body;
    private readonly HashSet<Collider> ignored = new HashSet<Collider>();
    private Vector3 lastSafe;
    private bool hasSafe;
    private Vector3 travelDirection;
    public int IgnoredCount => ignored.Count;
    public bool IsResolving { get; private set; }
    public bool HasEnemyOverlap => body != null && EnemyAt(transform.position);
    // Only the intersecting part of a dodge ignores slow motion. Invulnerability still uses game time.
    public float MovementDelta(float dt) => Time.timeScale > 0f && Time.timeScale < 1f && HasEnemyOverlap
        ? Mathf.Min(.05f, dt / Time.timeScale) : dt;
    private void Awake() { player = GetComponent<PlayerMovement>(); body = GetComponent<CharacterController>(); }
    private bool Active => player != null && player.IsInitialized && player.DamageReceiver.IsAlive
        && (player.CurrentState == PlayerMovement.MovementState.StepDodge
            || (player.Abilities.Dodge.Kind == DodgeKind.Micro && player.Abilities.Dodge.IsInvulnerable));
    internal void IgnoreSlideTarget(MeleeCombatTarget target, Vector3 direction)
    {
        travelDirection = direction;
        if (ignored.Count == 0 && IsFree(transform.position)) { lastSafe = transform.position; hasSafe = true; }
        foreach (var collider in target.GetComponentsInChildren<Collider>())
        {
            if (collider.isTrigger || collider.transform.IsChildOf(transform)) continue;
            if (!ignored.Contains(collider) && Physics.GetIgnoreCollision(body, collider)) continue;
            Physics.IgnoreCollision(body, collider, true); ignored.Add(collider);
        }
    }
    public void BeforeMove()
    {
        if (body == null) Awake();
        if (body == null || !body.enabled) return;
        if (player.IsInitialized && player.IsSliding && player.DamageReceiver.IsAlive)
        {
            travelDirection = player.SlideDirection;
            foreach (var collider in ignored)
                if (collider != null) Physics.IgnoreCollision(body, collider, true);
            return;
        }
        if (!Active) { Finish(); return; }
        travelDirection = player.CurrentState == PlayerMovement.MovementState.StepDodge
            ? player.Abilities.ActionDirection : transform.right * player.Abilities.Dodge.LeanSide;
        if (travelDirection.sqrMagnitude < .01f) travelDirection = transform.forward;
        if (ignored.Count == 0 && IsFree(transform.position)) { lastSafe = transform.position; hasSafe = true; }
        float range = Mathf.Max(5f, player.AbilitySettings.stepDistance + body.height + 1f);
        foreach (var collider in Physics.OverlapSphere(transform.position, range, ~0, QueryTriggerInteraction.Ignore))
        {
            if (!IsEnemy(collider) || collider.transform.IsChildOf(transform)) continue;
            // Changing controller dimensions can invalidate native collision pairs. Reassert pairs we own.
            if (!ignored.Contains(collider) && Physics.GetIgnoreCollision(body, collider)) continue;
            Physics.IgnoreCollision(body, collider, true); ignored.Add(collider);
        }
    }
    public void AfterMove()
    {
        if (ignored.Count > 0 && IsFree(transform.position)) { lastSafe = transform.position; hasSafe = true; }
        if (player.IsSliding && ignored.Count > 0)
        {
            var restored = new List<Collider>();
            foreach (var collider in ignored)
            {
                if (collider != null && OverlapsEnemy(collider)) continue;
                if (collider != null) Physics.IgnoreCollision(body, collider, false);
                restored.Add(collider);
            }
            foreach (var collider in restored) ignored.Remove(collider);
            if (ignored.Count == 0) hasSafe = false;
        }
    }
    public void Finish()
    {
        if (body == null || ignored.Count == 0) return;
        Vector3 current = transform.position;
        bool keepSupport = HasSupport(current);
        bool clear = IsFree(current);
        if (!clear && TryGetForwardExit(out var exit))
        { MoveClear(exit); clear = IsFree(transform.position); }
        if (!clear)
        {
            // Search locally, checking the complete path against walls, not just its endpoint.
            Vector3 preferred = hasSafe ? Vector3.ProjectOnPlane(lastSafe - current, Vector3.up).normalized : -transform.forward;
            if (preferred.sqrMagnitude < .01f) preferred = -transform.forward;
            for (float distance = .2f; distance <= 4f && !clear; distance += .2f)
            for (int i = 0; i < 16 && !clear; i++)
            {
                float angle = (i % 2 == 0 ? 1f : -1f) * ((i + 1) / 2) * 22.5f;
                Vector3 candidate = current + Quaternion.AngleAxis(angle, Vector3.up) * preferred * distance;
                if (!IsFree(candidate) || (keepSupport && !HasSupport(candidate)) || !ClearWorldPath(current, candidate)) continue;
                MoveClear(candidate); clear = IsFree(transform.position);
            }
            if (!clear && hasSafe && IsFree(lastSafe) && (!keepSupport || HasSupport(lastSafe)) && ClearWorldPath(transform.position, lastSafe))
            { MoveClear(lastSafe); clear = IsFree(transform.position); }
        }
        // In a completely sealed crowd keep only overlapping pairs ignored until escape is possible.
        // Never teleport through a wall or re-enable a deeply intersecting capsule.
        var restored = new List<Collider>();
        foreach (var collider in ignored)
        {
            if (collider == null) { restored.Add(collider); continue; }
            if (!clear && OverlapsEnemy(collider)) continue;
            Physics.IgnoreCollision(body, collider, false); restored.Add(collider);
        }
        foreach (var collider in restored) ignored.Remove(collider);
        if (ignored.Count == 0) hasSafe = false;
    }
    public bool TryGetForwardExit(out Vector3 exit)
    {
        Vector3 current = transform.position;
        Vector3 forward = Vector3.ProjectOnPlane(travelDirection, Vector3.up).normalized;
        if (forward.sqrMagnitude < .01f) forward = transform.forward;
        bool support = HasSupport(current);
        float reach = player.AbilitySettings.dodgeExitAssistDistance;
        // Exhaust the forward path first; only choose a side exit if forward is obstructed.
        for (int i = 0; i < 9; i++)
        {
            float angle = (i % 2 == 0 ? 1 : -1) * ((i + 1) / 2) * 22.5f;
            Vector3 direction = Quaternion.AngleAxis(angle, Vector3.up) * forward;
            for (float distance = .1f; distance <= reach + .001f; distance += .1f)
            {
                Vector3 candidate = current + direction * distance;
                if (!IsFree(candidate) || (support && !HasSupport(candidate)) || !ClearWorldPath(current, candidate)) continue;
                exit = candidate; return true;
            }
        }
        exit = current; return false;
    }
    private bool EnemyAt(Vector3 position, Collider only = null)
    {
        Capsule(position, out var a, out var b, out _);
        // Restore only beyond the full body plus clearance, not a skin-shrunken overlap volume.
        var scale = transform.lossyScale;
        float radius = body.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)) + .04f;
        foreach (var collider in Physics.OverlapCapsule(a, b, radius, ~0, QueryTriggerInteraction.Ignore))
            if (!collider.transform.IsChildOf(transform) && IsEnemy(collider) && (only == null || collider == only)) return true;
        return false;
    }
    private bool OverlapsEnemy(Collider enemy)
    {
        return EnemyAt(transform.position, enemy);
    }
    private void MoveClear(Vector3 position)
    {
        IsResolving = true;
        try
        {
            if (body.enabled && gameObject.activeInHierarchy) body.Move(position - transform.position);
            else transform.position = position;
            Physics.SyncTransforms();
        }
        finally { IsResolving = false; }
    }
    private bool IsFree(Vector3 position)
    {
        if (EnemyAt(position)) return false;
        Capsule(position, out var a, out var b, out float radius);
        foreach (var collider in Physics.OverlapCapsule(a, b, radius, ~0, QueryTriggerInteraction.Ignore))
            if (!collider.transform.IsChildOf(transform)) return false;
        return true;
    }
    private bool HasSupport(Vector3 position)
    {
        Capsule(position, out var bottom, out _, out float radius);
        foreach (var hit in Physics.RaycastAll(bottom - Vector3.up * radius + Vector3.up * .15f,
            Vector3.down, .45f, ~0, QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(transform) && !IsEnemy(hit.collider)
                && hit.normal.y >= Mathf.Cos(body.slopeLimit * Mathf.Deg2Rad)) return true;
        return false;
    }
    private bool ClearWorldPath(Vector3 from, Vector3 to)
    {
        Capsule(from, out var a, out var b, out float radius);
        Vector3 delta = to - from;
        if (delta.sqrMagnitude < .0001f) return true;
        foreach (var hit in Physics.CapsuleCastAll(a, b, radius, delta.normalized, delta.magnitude, ~0, QueryTriggerInteraction.Ignore))
            if (!hit.transform.IsChildOf(transform) && !IsEnemy(hit.collider)) return false;
        return true;
    }
    private void Capsule(Vector3 position, out Vector3 a, out Vector3 b, out float radius)
    {
        Vector3 scale = transform.lossyScale;
        float fullRadius = body.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
        radius = Mathf.Max(.01f, fullRadius - body.skinWidth * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z)) - .02f);
        Vector3 center = transform.TransformPoint(body.center) + position - transform.position;
        float half = Mathf.Max(0f, body.height * Mathf.Abs(scale.y) * .5f - fullRadius);
        a = center - Vector3.up * half; b = center + Vector3.up * half;
    }
    internal static bool IsEnemy(Collider collider)
    {
        if (collider == null) return false;
        if (collider.GetComponentInParent<MeleeCombatTarget>() != null) return true;
        if (collider.GetComponentInParent<EnemyHealth>() != null) return true;
        for (var owner = collider.transform; owner != null; owner = owner.parent)
            if (owner.gameObject.tag == "Enemy") return true;
        return false;
    }
    private void OnDisable()
    {
        Finish();
        foreach (var collider in ignored) if (body != null && collider != null) Physics.IgnoreCollision(body, collider, false);
        ignored.Clear(); hasSafe = false;
    }
}
