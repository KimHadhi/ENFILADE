using System.Collections.Generic;
using System.Globalization;
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
    /// <summary>
    /// How much of the gravity goes while the character stands upright (Magica's gravityFalloff, which measures the
    /// whole body's lean, not each bone's: 1 hangs nothing while she stands, half as she lies down). Below zero keeps
    /// the preset's.
    /// </summary>
    public float gravityFalloff = -1f;
    /// <summary>
    /// The renderers this part moves, "name|name". While none of them shows (the piece is off, or a new outfit hid it) the
    /// part doesn't swing at all, so the arms never push a skirt nobody sees. Empty swings always.
    /// </summary>
    public string parts = "";
    /// <summary>Made by BoothImport from VRChat PhysBones: it is rewritten the next time the import reads them.</summary>
    public bool vrc;
    /// <summary>
    /// How much less of the body's motion the bones near the root feel than those at the end (Magica's depthInertia,
    /// 0..1, falling off as 1 - depth²). Below zero keeps the preset's.
    /// </summary>
    public float depthInertia = -1f;
    /// <summary>
    /// How angleLimit, stiffness, damping and thickness run down the part, root (0) to far end (1), each as a share of
    /// its number: "depth:share|depth:share", straight between, or "1" for the same all the way. Empty keeps the preset's
    /// own curve, which the rows written by hand were tuned with; BoothImport writes VRChat's.
    /// </summary>
    public string angleCurve = "", stiffnessCurve = "", dampingCurve = "", thicknessCurve = "";

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

    /// <summary>
    /// The thinnest this part's cloth gets where it lies close to the body (metres): the presets' own, or its thickness
    /// where that is thinner still, at its thinnest depth (Apply).
    /// </summary>
    internal float Thinnest(ClothBones shape) =>
        thickness > 0f ? Mathf.Min(ClothBones.LeastThickness, thickness * shape.Length * Least(Curve(thicknessCurve))) : ClothBones.LeastThickness;

    /// <summary>A chain BoothImport made that hangs (not a ring, not a spring): it is fitted bone by bone (ClothBones.FitRadii).</summary>
    internal bool Hangs => vrc && !ring && !spring;

    /// <summary>The thickest its particles may be (metres): its maker's radius (thickness) or, without one, the preset's.</summary>
    internal float Thickest(ClothBones shape) => thickness > 0f ? thickness * shape.Length : ClothBones.LeastThickness;

    /// <summary>How that thickness runs down the part (thicknessCurve), null for the same all the way.</summary>
    internal AnimationCurve ThickestAlong => thickness > 0f ? Curve(thicknessCurve) : null;

    /// <summary>The angle limit at a depth down the part (degrees): angleLimit along angleCurve.</summary>
    internal float LimitAt(float depth) => angleLimit * (Curve(angleCurve) is AnimationCurve along ? Mathf.Max(0f, along.Evaluate(depth)) : 1f);

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
        // A ring's few bones leave gaps a thigh would slip through point by point. A chain BoothImport made collides point
        // by point, as Magica's own presets do: only its free bones meet the body, so a root on the scalp or at a tail's
        // base never levers the chain off the shape its maker drew (PlayerBodyClothColliders.Fit). Rows written by hand
        // keep the lines they were tuned with.
        data.colliderCollisionConstraint.mode = ring || !vrc ? ColliderCollisionConstraint.Mode.Edge : ColliderCollisionConstraint.Mode.Point;
        data.colliderCollisionConstraint.colliderList.AddRange(colliders);
        // The backstop keeps each bone out of the way the part's own middle lies: "in" has to mean toward the body.
        if (backstop >= 0f) data.normalAlignmentSetting.alignmentMode = NormalAlignmentSettings.AlignmentMode.BoundingBoxCenter;
        // Magica takes every transform under a root: what an outfit hung inside this part (the Sera Yura shirt's hem on
        // Mayo's breast) is marked out of it, neither moved nor put back by this part, so only the outfit's rows move it.
        var foreign = new List<Transform>();
        foreach (var bone in bones) ClothBones.Collect(bone, null, foreign);
        var attributes = cloth.GetSerializeData2().boneAttributeDict;
        foreach (var hung in foreign) attributes[hung] = VertexAttribute.Invalid;
        Apply(data, shape);
        cloth.BuildAndRun();
        return cloth;
    }

    /// <summary>Writes the table's numbers over the preset's, keeping the curves that spread them along a bone chain.</summary>
    internal void Apply(ClothSerializeData data, ClothBones shape)
    {
        float length = shape.Length;
        // A chain fitted bone by bone has its particles from its own cloth (ClothBones.FitRadii).
        if (shape.FittedRadius != null) data.radius.SetValue(shape.FittedScale, shape.FittedRadius);
        else if (shape.Thickness(thickness * length, Curve(thicknessCurve)) is AnimationCurve thick) data.radius.SetValue(thickness * length, thick);
        data.rootRotation = rootTurn;
        data.gravity = gravity;
        if (gravityFalloff >= 0f) data.gravityFalloff = Mathf.Clamp01(gravityFalloff);
        Down(data.damping, damping, dampingCurve);
        data.inertiaConstraint.worldInertia = worldInertia;
        data.inertiaConstraint.localInertia = localInertia;
        if (depthInertia >= 0f) data.inertiaConstraint.depthInertia = Mathf.Clamp01(depthInertia);
        data.inertiaConstraint.movementSpeedLimit.SetValue(true, speedLimit);
        data.inertiaConstraint.centrifualAcceleration = centrifugal;
        data.inertiaConstraint.teleportDistance = teleportDistance;
        data.wind.influence = windPower;
        data.wind.movingWind = movingWind;
        data.wind.synchronization = windSync;
        Down(data.angleRestorationConstraint.stiffness, stiffness, stiffnessCurve);
        Down(data.angleLimitConstraint.limitAngle, angleLimit, angleCurve);
        data.colliderCollisionConstraint.friction = friction;
        data.motionConstraint.useMaxDistance = reach > 0f;
        data.motionConstraint.maxDistance.SetValue(reach * length);
        data.motionConstraint.useBackstop = backstop >= 0f;
        data.motionConstraint.backstopDistance.SetValue(Mathf.Max(0f, backstop) * length);
    }

    // A number down the part: as its curve column says, or under the preset's own curve where the column is empty.
    private static void Down(CurveSerializeData data, float value, string curve)
    {
        if (string.IsNullOrWhiteSpace(curve)) data.value = value;
        else if (Curve(curve) is AnimationCurve along) data.SetValue(value, along);
        else data.SetValue(value);
    }

    /// <summary>A curve column as a curve of shares by depth, null where it is empty or the same all the way ("1").</summary>
    internal static AnimationCurve Curve(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var keys = new List<Keyframe>();
        foreach (string pair in text.Split(BoneSeparator))
        {
            int colon = pair.IndexOf(':');
            if (colon < 0
                || !float.TryParse(pair.Substring(0, colon).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float depth)
                || !float.TryParse(pair.Substring(colon + 1).Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out float share)) continue;
            keys.Add(new Keyframe(Mathf.Clamp01(depth), Mathf.Max(0f, share)));
        }
        if (keys.Count < 2) return null;
        keys.Sort((a, b) => a.time.CompareTo(b.time));
        return ClothBones.Straight(keys);
    }

    // The smallest share a curve gives (straight between its keys), 1 for none.
    private static float Least(AnimationCurve curve)
    {
        if (curve == null) return 1f;
        float least = float.PositiveInfinity;
        foreach (var key in curve.keys) least = Mathf.Min(least, key.value);
        return least;
    }
}
