using System.Collections.Generic;
using MagicaCloth2;
using UnityEngine;

/// <summary>
/// One row of Cloth.csv: which bones of a skin, or of an outfit item (Outfits.csv), hang free and how they move. A
/// preset in Resources/Cloth holds Magica Cloth's full settings, and the columns here overrule the ones worth tuning.
/// </summary>
public sealed class ClothPart
{
    public const string TableName = "Cloth";
    private const string PresetFolder = "Cloth";
    private const char BoneSeparator = '|';
    // Each step round this share of a turn lands far from the last, so neighbours in the list come out different.
    private const float SpreadStep = .618034f;

    public string skin = "", roots = "", preset = "", shapes = "";
    public bool ring;
    /// <summary>A soft lump that springs back to its place, rather than cloth that hangs (Magica's BoneSpring).</summary>
    public bool spring;
    public float rootTurn, tipLength, tipSpread;
    public float gravity = 5f, damping = .1f, worldInertia = 1f, localInertia = 1f, speedLimit = 5f, centrifugal;
    public float windPower = .2f, movingWind, windSync = .7f;
    public float stiffness = .2f, angleLimit = 60f, friction = .05f, teleportDistance = 2f;
    /// <summary>
    /// How thick the cloth is where it collides, as a share of the part's length. Magica collides only along the lines
    /// between bones, so a skirt of many layers needs this to meet a leg where it shows. The arms' press takes it as how
    /// far the skirt's outside lies from its bones. Zero keeps the preset's.
    /// </summary>
    public float thickness;
    /// <summary>How far a bone can leave its place, as a share of the part's length. Zero leaves it free.</summary>
    public float reach;
    /// <summary>How far in from its place a bone can go, as a share of the part's length. Below zero leaves it free.</summary>
    public float backstop = -1f;
    /// <summary>
    /// How far in the arms can press a ring the shapes let them touch, as a share of its length (SkirtPress). Below zero
    /// only the legs stop them; zero, they pass through it.
    /// </summary>
    public float press = -1f;

    /// <summary>The bones the part hangs from, in the table's order: a ring runs through them in turn.</summary>
    internal List<Transform> FindRoots(Dictionary<string, Transform> bones, string model)
    {
        var found = new List<Transform>();
        foreach (string name in roots.Split(BoneSeparator))
        {
            if (bones.TryGetValue(name.Trim(), out var bone)) found.Add(bone);
            else Debug.LogWarning($"[Cloth] {skin}: {model}에 '{name}' 뼈가 없습니다.");
        }
        return found;
    }

    /// <summary>
    /// A root with nothing under it has nothing to swing, so it gets a point hanging below it. Their lengths differ, because
    /// strands of one length swing as one: a longer one takes longer over each swing, as it would on a real head.
    /// </summary>
    internal void AddTips(List<Transform> bones, Vector3 down)
    {
        if (tipLength <= 0f) return;
        for (int i = 0; i < bones.Count; i++)
        {
            var bone = bones[i];
            if (bone.childCount > 0) continue;
            float spread = Mathf.Repeat(i * SpreadStep, 1f) * 2f - 1f;
            var tip = new GameObject("Cloth tip").transform;
            tip.SetParent(bone, false);
            tip.position = bone.position + down * (Vector3.Distance(bone.position, bone.parent.position) * tipLength * (1f + tipSpread * spread));
        }
    }

    /// <summary>Starts the simulation on these bones. Living under the model, it goes when the body does.</summary>
    internal MagicaCloth Wear(Transform model, string id, List<Transform> bones, List<ColliderComponent> colliders, ClothBones shape)
    {
        var holder = new GameObject("Cloth (" + id + ")");
        holder.transform.SetParent(model, false);
        var cloth = holder.AddComponent<MagicaCloth>();
        var data = cloth.SerializeData;
        var settings = Resources.Load<TextAsset>(PresetFolder + "/" + preset);
        if (settings != null) data.ImportJson(settings.text);
        else Debug.LogWarning($"[Cloth] Resources/{PresetFolder}/{preset}.json 파일이 없습니다.");
        data.clothType = spring ? ClothProcess.ClothType.BoneSpring : ClothProcess.ClothType.BoneCloth;
        data.rootBones.AddRange(bones);
        data.connectionMode = ring ? RenderSetupData.BoneConnectionMode.SequentialLoopMesh : RenderSetupData.BoneConnectionMode.Line;
        data.updateMode = ClothUpdateMode.Normal;
        // The other PCs see this cloth even when this PC's camera doesn't.
        data.cullingSettings.cameraCullingMode = CullingSettings.CameraCullingMode.Off;
        data.inertiaConstraint.teleportMode = InertiaConstraint.TeleportMode.Reset;
        // A ring's few bones leave gaps a thigh would slip through point by point.
        data.colliderCollisionConstraint.mode = ColliderCollisionConstraint.Mode.Edge;
        data.colliderCollisionConstraint.colliderList.AddRange(colliders);
        // The backstop keeps each bone out of the way the part's own middle lies: "in" has to mean toward the body.
        if (backstop >= 0f) data.normalAlignmentSetting.alignmentMode = NormalAlignmentSettings.AlignmentMode.BoundingBoxCenter;
        Apply(data, shape);
        cloth.BuildAndRun();
        return cloth;
    }

    /// <summary>Writes the table's numbers over the preset's, keeping the curves that spread them along a bone chain.</summary>
    internal void Apply(ClothSerializeData data, ClothBones shape)
    {
        float length = shape.Length;
        var thick = shape.Thickness(thickness * length);
        if (thick != null) data.radius.SetValue(thickness * length, thick);
        data.rootRotation = rootTurn;
        data.gravity = gravity;
        data.damping.value = damping;
        data.inertiaConstraint.worldInertia = worldInertia;
        data.inertiaConstraint.localInertia = localInertia;
        data.inertiaConstraint.movementSpeedLimit.SetValue(true, speedLimit);
        data.inertiaConstraint.centrifualAcceleration = centrifugal;
        data.inertiaConstraint.teleportDistance = teleportDistance;
        data.wind.influence = windPower;
        data.wind.movingWind = movingWind;
        data.wind.synchronization = windSync;
        data.angleRestorationConstraint.stiffness.value = stiffness;
        data.angleLimitConstraint.limitAngle.value = angleLimit;
        data.colliderCollisionConstraint.friction = friction;
        data.motionConstraint.useMaxDistance = reach > 0f;
        data.motionConstraint.maxDistance.SetValue(reach * length);
        data.motionConstraint.useBackstop = backstop >= 0f;
        data.motionConstraint.backstopDistance.SetValue(Mathf.Max(0f, backstop) * length);
    }
}
