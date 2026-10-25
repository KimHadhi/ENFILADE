using System;
using UnityEngine;
using UnityEngine.AI;
using Object = UnityEngine.Object;

internal static class RagdollKnockdownChecks
{
    private static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
    internal static void Run()
    {
        foreach (float dt in new[] { 1f/30f, 1f/60f, 1f/120f }) CheckCapsule(dt);
        CheckCapsule(1f/60f, true);
        CheckBones();
        Debug.Log("RAGDOLL_KNOCKDOWN_CHECKS_PASSED: gravity, torque, floor/wall contacts, navigation, recovery, bone state restoration");
    }
    private static void CheckCapsule(float dt, bool feetPivot = false)
    {
        Vector3 origin = new Vector3(15,0,0);
        var data = NavMeshBuilder.BuildNavMeshData(NavMesh.GetSettingsByIndex(0),
            new System.Collections.Generic.List<NavMeshBuildSource> { new NavMeshBuildSource {
                shape = NavMeshBuildSourceShape.Box, size = new Vector3(12,.2f,12),
                transform = Matrix4x4.TRS(origin + Vector3.down * .1f, Quaternion.identity, Vector3.one), area = 0 } },
            new Bounds(origin, new Vector3(14,6,14)), Vector3.zero, Quaternion.identity);
        var nav = NavMesh.AddNavMeshData(data);
        var enemy = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        enemy.name = "Physics knockdown capsule"; enemy.transform.position = origin + (feetPivot ? Vector3.zero : Vector3.up);
        if (feetPivot) enemy.GetComponent<CapsuleCollider>().center = Vector3.up;
        var agent = enemy.AddComponent<NavMeshAgent>(); agent.baseOffset = feetPivot ? 0f : 1f;
        var target = enemy.AddComponent<MeleeCombatTarget>();
        var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
        wall.transform.position = origin + new Vector3(0,1,1.5f); wall.transform.localScale = new Vector3(5,3,.2f);
        try
        {
            Physics.SyncTransforms();
            Require(agent.isOnNavMesh, "Ragdoll fixture has navigation support");
            target.KnockDown(Vector3.forward, 1.2f);
            var body = enemy.GetComponent<Rigidbody>();
            Require(!body.isKinematic && body.useGravity && !agent.enabled, "Physics takes ownership from navigation");
            bool tilted = false, lowered = false;
            for (float time = 0f; time < .9f; time += dt)
            {
                Physics.Simulate(dt); target.Tick(dt);
                tilted |= Vector3.Dot(enemy.transform.up, Vector3.up) < .8f;
                lowered |= enemy.GetComponent<Collider>().bounds.center.y < .85f;
                Require(enemy.GetComponent<Collider>().bounds.center.z < 1.45f, "Fallen enemy cannot pass through a wall");
                Require(enemy.GetComponent<Collider>().bounds.min.y > -.12f, "Fallen body stays above the floor");
            }
            Require(tilted && lowered, "Body physically tumbles and falls under gravity");
            Object.DestroyImmediate(wall); wall = null;
            for (int i = 0; i < Mathf.CeilToInt(2f/dt) && target.IsKnockedDown; i++)
            { Physics.Simulate(dt); target.Tick(dt); }
            Require(!target.IsKnockedDown && !target.IsStunned && agent.enabled && agent.isOnNavMesh,
                "Recovery returns body to navigation");
            Require(body.isKinematic && !body.useGravity && Vector3.Dot(enemy.transform.up, Vector3.up) > .999f,
                "Upright pose and original body flags restored");
            Require(Mathf.Abs(enemy.transform.position.y - (feetPivot ? 0f : 1f)) < .15f, "Recovery respects the body's standing pivot");
            target.KnockDown(Vector3.right, 1f); Physics.Simulate(.1f);
            target.KnockDown(Vector3.forward, 1.2f); target.ResetPosture();
            Require(!target.IsKnockedDown && body.isKinematic && agent.enabled, "Repeated knockdown/reset preserves original ownership");
        }
        finally
        {
            Object.DestroyImmediate(enemy); if (wall != null) Object.DestroyImmediate(wall);
            nav.Remove(); Object.DestroyImmediate(data);
        }
    }
    private static void CheckBones()
    {
        var root = new GameObject("Jointed enemy"); root.transform.position = new Vector3(-15,1,0);
        var main = root.AddComponent<CapsuleCollider>(); main.height = 2f; main.radius = .4f;
        var animator = root.AddComponent<Animator>();
        var upper = GameObject.CreatePrimitive(PrimitiveType.Capsule); upper.transform.SetParent(root.transform, false);
        upper.transform.localPosition = Vector3.up * .4f; upper.transform.localScale = new Vector3(.4f,.35f,.4f);
        var lower = GameObject.CreatePrimitive(PrimitiveType.Capsule); lower.transform.SetParent(root.transform, false);
        lower.transform.localPosition = Vector3.down * .4f; lower.transform.localScale = new Vector3(.4f,.35f,.4f);
        var a = upper.AddComponent<Rigidbody>(); a.isKinematic = true; a.useGravity = false;
        var b = lower.AddComponent<Rigidbody>(); b.isKinematic = true; b.useGravity = false;
        var joint = lower.AddComponent<CharacterJoint>(); joint.connectedBody = a;
        upper.GetComponent<Collider>().enabled = lower.GetComponent<Collider>().enabled = false;
        var target = root.AddComponent<MeleeCombatTarget>();
        try
        {
            Physics.SyncTransforms(); target.KnockDown(Vector3.forward, 1f);
            Require(!animator.enabled && !main.enabled && !a.isKinematic && !b.isKinematic
                && upper.GetComponent<Collider>().enabled && lower.GetComponent<Collider>().enabled,
                $"Configured bone ragdoll replaces animation and the standing collider: animator={animator.enabled}, main={main.enabled}, kinematic={a.isKinematic}/{b.isKinematic}, colliders={upper.GetComponent<Collider>().enabled}/{lower.GetComponent<Collider>().enabled}");
            for (int i = 0; i < 20; i++) Physics.Simulate(.01f);
            Require(a.angularVelocity.sqrMagnitude > .01f && b.angularVelocity.sqrMagnitude > .01f, "Jointed limbs are physics driven");
            root.SetActive(false);
            Require(a.isKinematic && b.isKinematic && !a.useGravity && main.enabled && animator.enabled
                && !upper.GetComponent<Collider>().enabled && !lower.GetComponent<Collider>().enabled,
                "Disable/pooling restores all original ragdoll, collider and animator states");
            Require(Vector3.Distance(upper.transform.localPosition, Vector3.up * .4f) < .001f, "Bone rest pose restored");
        }
        finally { Object.DestroyImmediate(root); }
    }
}
