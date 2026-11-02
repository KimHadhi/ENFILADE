using System;
using System.Collections.Generic;
using MagicaCloth2;
using UnityEngine;

/// <summary>
/// Skirts, coats and hair that hang free. Cloth.csv names the bones of each skin. The PC that plays a character simulates
/// them with Magica Cloth; every other PC's copy runs no simulation and follows what that PC reports (PlayerNetworkCloth),
/// so everyone sees the same cloth. Made again whenever the body is, so a skin swap brings its own. A corpse is this same
/// body, so its cloth goes on hanging as it falls. Numbers: Tables/Cloth, Tables/ClothBody.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(340)]
public sealed class PlayerBodyCloth : MonoBehaviour
{
    public const string TableName = "ClothBody", TableRow = "Default";

    private sealed class Worn
    {
        public ClothPart Part;
        public MagicaCloth Cloth;
        public ClothBones Bones;
    }

    [SerializeField, Min(.01f), Tooltip("Seconds between the cloth poses sent to the other PCs.")]
    private float sendInterval = .066f;
    [SerializeField, Min(1f), Tooltip("How quickly a copy's cloth closes on the last pose it got (per second).")]
    private float followRate = 20f;

    private readonly Dictionary<string, ClothPart> parts = new Dictionary<string, ClothPart>(StringComparer.OrdinalIgnoreCase);
    private readonly List<Worn> worn = new List<Worn>();
    private PlayerMovement player;
    private PlayerBody body;
    private Animator builtFor;

    internal float SendInterval => sendInterval;

    private void Awake()
    {
        player = GetComponent<PlayerMovement>();
        body = GetComponent<PlayerBody>();
        GameTables.Reloaded += OnTablesReloaded;
    }

    private void OnDestroy() => GameTables.Reloaded -= OnTablesReloaded;

    private void LateUpdate()
    {
        var animator = body != null ? body.BodyAnimator : null;
        if (animator != builtFor) Build(animator);
        foreach (var item in worn)
        {
            if (item.Cloth != null) item.Cloth.SetTimeScale(player.Pace);
            else item.Bones.Follow(followRate, Time.deltaTime);
        }
    }

    private void Build(Animator animator)
    {
        builtFor = animator;
        worn.Clear();
        if (animator == null || body.Skin == null) return;
        var bones = new Dictionary<string, Transform>();
        foreach (var bone in animator.GetComponentsInChildren<Transform>(true)) bones.TryAdd(bone.name, bone);
        var colliders = GetComponent<PlayerBodyClothColliders>();
        foreach (string id in GameTables.RowIds(ClothPart.TableName))
        {
            var part = Part(id);
            if (!string.Equals(part.skin, body.Skin.Id, StringComparison.OrdinalIgnoreCase)) continue;
            var roots = part.FindRoots(bones, animator.name);
            if (roots.Count == 0) continue;
            part.AddTips(roots, -animator.transform.up);
            // Read before the simulation first moves them.
            var free = new ClothBones(roots, part.rootTurn);
            var cloth = player.IsNetworkDriven ? null
                : part.Wear(animator.transform, id, roots, colliders.For(animator, part.shapes), free.Length);
            worn.Add(new Worn { Part = part, Cloth = cloth, Bones = free });
        }
    }

    // One object per row, because a table's binding keeps the object it was first given.
    private ClothPart Part(string id)
    {
        if (parts.TryGetValue(id, out var part)) return part;
        part = new ClothPart();
        GameTables.BindNested(ClothPart.TableName, id, this, part);
        return parts[id] = part;
    }

    // The bones and shapes stay as they were built: those columns take effect on the next body.
    private void OnTablesReloaded()
    {
        foreach (var item in worn)
        {
            if (item.Cloth == null) continue;
            item.Part.Apply(item.Cloth.SerializeData, item.Bones.Length);
            item.Cloth.SetParameterChange();
        }
    }

    /// <summary>Everything this body's cloth is doing right now, for the other PCs.</summary>
    internal void Capture(ref ClothPose pose)
    {
        foreach (var item in worn) item.Bones.Write(ref pose);
    }

    /// <summary>What the real player's PC reported. It is ignored if this copy hasn't changed to their skin yet.</summary>
    internal void Show(ref ClothPose pose)
    {
        int count = 0, at = 0;
        foreach (var item in worn) count += item.Bones.Moving;
        if (count != pose.Count) return;
        foreach (var item in worn) at = item.Bones.Read(ref pose, at);
    }
}
