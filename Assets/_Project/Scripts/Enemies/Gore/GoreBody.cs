using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// One zombie's body as gore sees it (ZombieGore): its character, skins and human bones, what has been cut off and the
/// damage each limb has taken. A cut hides the part on the skin only: each skin's bone list points the part's bones at a
/// zero-size stand-in at the joint, so the part's skin shrinks into the stump while the real bones, the Animator, the
/// corpse doll (which only turns real bones) and every measurement made from them stay as they were. The part's hitboxes
/// stop taking bullets. It follows the character from dressing until its corpse is gone.
/// </summary>
internal sealed class GoreBody
{
    public readonly EnemyHealth Enemy;
    public readonly GameObject Model;
    public readonly SkinnedMeshRenderer[] Skins;
    // Each skin's bones as dressed, before any cut: what the pieces are worked out from (GoreGibs).
    public readonly Transform[][] RestBones;
    private readonly Transform[] human = new Transform[(int)HumanBodyBones.LastBone];
    private readonly bool[] cut = new bool[BodyParts.Count];
    private readonly float[] blow = new float[BodyParts.Count], worn = new float[BodyParts.Count];
    private readonly int[] blowFrame = new int[BodyParts.Count];

    private GoreBody(EnemyHealth enemy, GameObject model, SkinnedMeshRenderer[] skins)
    {
        Enemy = enemy;
        Model = model;
        Skins = skins;
        RestBones = new Transform[skins.Length][];
        for (int i = 0; i < skins.Length; i++) RestBones[i] = skins[i].bones;
    }

    /// <summary>A dressed zombie's character, or null if it has no humanoid rig or skin.</summary>
    public static GoreBody Follow(EnemyHealth enemy, GameObject model)
    {
        if (model == null) return null;
        var animator = model.GetComponentInChildren<Animator>();
        if (animator == null || !animator.isHuman || animator.avatar == null || !animator.avatar.isValid) return null;
        var skins = model.GetComponentsInChildren<SkinnedMeshRenderer>(false);
        if (skins.Length == 0) return null;
        var body = new GoreBody(enemy, model, skins);
        for (int i = 0; i < body.human.Length; i++) body.human[i] = animator.GetBoneTransform((HumanBodyBones)i);
        return body;
    }

    public Transform Bone(HumanBodyBones bone) => bone >= 0 && bone < HumanBodyBones.LastBone ? human[(int)bone] : null;

    /// <summary>Its bones by the part they belong to, for reading which part a hitbox sits on.</summary>
    public void Register(Dictionary<Transform, BodyPart> parts)
    {
        for (int i = 0; i < human.Length; i++)
        {
            var part = BodyParts.OfBone((HumanBodyBones)i);
            if (human[i] != null && part != BodyPart.None) parts[human[i]] = part;
        }
    }

    public void Unregister(Dictionary<Transform, BodyPart> parts)
    {
        foreach (var bone in human) if (!ReferenceEquals(bone, null)) parts.Remove(bone);
    }

    public bool IsCut(BodyPart part) => cut[(int)part];

    /// <summary>Still on the body: neither it nor the part it hangs from is cut.</summary>
    public bool Has(BodyPart part)
    {
        if (part == BodyPart.None || cut[(int)part]) return false;
        var upper = BodyParts.Upper(part);
        return upper == BodyPart.None || !cut[(int)upper];
    }

    public int LegsLost => Lost(BodyPart.LeftUpperLeg, BodyPart.LeftLowerLeg) + Lost(BodyPart.RightUpperLeg, BodyPart.RightLowerLeg);
    public int ArmsLost => Lost(BodyPart.LeftUpperArm, BodyPart.LeftLowerArm) + Lost(BodyPart.RightUpperArm, BodyPart.RightLowerArm);
    private int Lost(BodyPart upper, BodyPart lower) => cut[(int)upper] || cut[(int)lower] ? 1 : 0;

    /// <summary>Damage on a part this frame so far: a shotgun's pellets in one limb land as one blow.</summary>
    public float Blow(BodyPart part, float amount)
    {
        int i = (int)part;
        if (blowFrame[i] != Time.frameCount) { blowFrame[i] = Time.frameCount; blow[i] = 0f; }
        return blow[i] += Mathf.Max(0f, amount);
    }

    /// <summary>All the damage a part has taken.</summary>
    public float Wear(BodyPart part, float amount) => worn[(int)part] += Mathf.Max(0f, amount);

    /// <summary>
    /// Takes the part off the skin: its bones (and any stand-in of a part already cut below it) give way to a zero-size
    /// stand-in at the joint, under the bone it hangs from. Returns the part's first bone, or null if it can't be cut.
    /// </summary>
    public Transform Hide(BodyPart part)
    {
        var root = Bone(BodyParts.Root(part));
        if (root == null || root.parent == null) return null;
        cut[(int)part] = true;
        for (int i = 0; i < cut.Length; i++)
            if (BodyParts.Upper((BodyPart)i) == part) cut[i] = true;

        var stand = new GameObject("Cut " + part).transform;
        stand.SetParent(root.parent, false);
        stand.SetPositionAndRotation(root.position, root.rotation);
        stand.localScale = Vector3.zero;
        foreach (var skin in Skins)
        {
            if (skin == null) continue;
            var bones = skin.bones;
            bool changed = false;
            for (int i = 0; i < bones.Length; i++)
            {
                if (bones[i] == null || !bones[i].IsChildOf(root)) continue;
                bones[i] = stand;
                changed = true;
            }
            if (changed) skin.bones = bones;
        }
        // Gone, so not shot: its hitboxes (triggers on its bones) are switched off whole. A shot's blood mark hangs on the
        // hitbox it hit (EnemyBloodEffect), even the mark of the shot that cut it, which comes after this, so it goes
        // dark with it instead of floating where the limb was. Anything else drawn on the part's bones goes too.
        foreach (var collider in root.GetComponentsInChildren<Collider>(true))
            if (collider.isTrigger) collider.gameObject.SetActive(false);
        foreach (var renderer in root.GetComponentsInChildren<Renderer>(true))
            if (!(renderer is SkinnedMeshRenderer)) renderer.enabled = false;
        return root;
    }
}
