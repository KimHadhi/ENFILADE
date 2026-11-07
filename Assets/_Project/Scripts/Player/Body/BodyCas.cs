using System.Collections.Generic;
using System.Reflection;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Core;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Modifiers.BoneControls;
using KINEMATION.CharacterAnimationSystem.Scripts.Runtime.Playables;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Rig;
using UnityEngine;

/// <summary>
/// KINEMATION Character Animation System on the body, after FPS Animation Framework (FPSBody) in the Animator's graph:
/// - Look: the spine, neck and head follow the view while no gun is held (FPSAF's own Look layer has them with a gun),
///   and Q/E tips the spine; PlayerBody decides how much (upright or sliding, the slide's limits) and this feeds it.
/// - Before the look bends the head, a copy of it is kept (HeadAsClip) for what follows the clip's head (PlayerBody).
/// The feet are our own foot IK (FootPlanting), which keeps the knees in the clip's plane; CAS's leg IK turned them.
/// The skeleton is the body's humanoid bones and that helper only, so hair and cloth stay Magica Cloth's.
/// Settings are made per body at run time from the avatar (any humanoid).
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(296)]
public sealed class BodyCas : MonoBehaviour
{
    private const string HeadCopyName = "CAS Head (clip)";

    // FPSAF goes first; a body FPSAF can't take is given this many frames before CAS goes on alone.
    private const int WaitForFramework = 3;

    private PlayerBody body;
    private FPSBody framework;
    private Animator animator;
    private int waited;
    private Transform headCopy;
    private readonly List<Object> made = new();
    private readonly List<GameObject> helpers = new();
    private LookModifierSettings look;
    private bool reported;

    /// <summary>The head as the clips (and FPSAF, with a gun) posed it, before Look bent it with the view; null until set up.</summary>
    internal Transform HeadAsClip => animator != null && headCopy != null ? headCopy : null;

    /// <summary>The spine bones Look bends, from the hips up, with their angle at a 90° look down and up (CAS's clamped angles).</summary>
    internal IReadOnlyList<(Transform bone, float down, float up)> SpineBend => spineBend;
    private readonly List<(Transform bone, float down, float up)> spineBend = new();

    private void Awake()
    {
        body = GetComponent<PlayerBody>();
    }

    private void OnDestroy() => Forget();

    private void Update()
    {
        if (body == null) return;
        var bodyAnimator = body.BodyAnimator;
        if (bodyAnimator != animator)
        {
            if (!ReadyFor(bodyAnimator)) return;
            Install(bodyAnimator);
        }
        if (look == null) return;
        Feed();
    }

    // FPSAF's outputs come first in the Animator's graph, after the body's clips, so CAS waits for it.
    private bool ReadyFor(Animator bodyAnimator)
    {
        if (animator != null) Forget();
        if (bodyAnimator == null || !bodyAnimator.playableGraph.IsValid() || body.IsLimp || !bodyAnimator.isHuman) { waited = 0; return false; }
        if (framework == null) framework = GetComponent<FPSBody>();
        if (framework != null && !framework.InstalledOn(bodyAnimator) && waited++ < WaitForFramework) return false;
        waited = 0;
        return true;
    }

    // ---- Setting CAS up on the body ----

    private void Install(Animator bodyAnimator)
    {
        animator = bodyAnimator;
        var hips = animator.GetBoneTransform(HumanBodyBones.Hips);
        var armature = hips != null ? hips.parent : null;
        if (armature == null)
        {
            Report($"{animator.name} has no armature above its hips; Character Animation System stays off.");
            animator = null;
            return;
        }

        // The head as the clip has it, copied before the look bends it.
        headCopy = Helper(HeadCopyName, armature);
        var skeleton = armature.gameObject.AddComponent<CharacterSkeleton>();
        var bones = new List<Transform> { armature };
        for (var bone = HumanBodyBones.Hips; bone < HumanBodyBones.LastBone; bone++)
            if (animator.GetBoneTransform(bone) is Transform found && !bones.Contains(found)) bones.Add(found);
        bones.Add(headCopy);
        Fill(skeleton, bones);

        var procedural = animator.gameObject.AddComponent<ProceduralAnimationComponent>();
        var settings = Make<ProceduralAnimationSettings>();
        settings.modifiers = new List<AnimationModifierSettings>();
        var head = animator.GetBoneTransform(HumanBodyBones.Head);
        var copyHead = Make<CopyBoneSettings>();
        Copy(copyHead, head, headCopy);
        settings.modifiers.Add(copyHead);
        look = MakeLook();
        settings.modifiers.Add(look);
        var animation = Make<CharacterAnimationSettings>();
        animation.blendTime = new BlendTime(0f, 0f, new EaseMode(EEaseFunc.Linear));
        animation.proceduralSettings = settings;
        // Woken now: the skeleton and procedural component are already on the body, and the graph holds FPSAF's outputs.
        var cas = animator.gameObject.AddComponent<BodyCasAnimation>();
        cas.UpdateAnimationSettings(animation);
        if (procedural == null) Report("Character Animation System's procedural component did not start.");
    }

    // KINEMATION's Look modifier on the body's spine, neck and head: a look down or up bends the spine its share
    // (PlayerBody's spineBendDown / spineBendUp, 45% and 20%) and the neck and head the rest (0.3 / 0.7 of it); turning,
    // the spine takes 60% and neck and head 20% each; Q/E tips the spine alone. Each angle is that bone's share at 90°, as
    // CAS spreads a look.
    private LookModifierSettings MakeLook()
    {
        var settings = Make<LookModifierSettings>();
        settings.enableTurnInPlace = false;
        settings.useYawAsDelta = false;
        var spine = new List<Transform>();
        foreach (var bone in new[] { HumanBodyBones.Spine, HumanBodyBones.Chest, HumanBodyBones.UpperChest })
            if (animator.GetBoneTransform(bone) is Transform found) spine.Add(found);
        var neck = animator.GetBoneTransform(HumanBodyBones.Neck);
        var head = animator.GetBoneTransform(HumanBodyBones.Head);
        int count = Mathf.Max(1, spine.Count);
        float spineDown = body.SpineBendDown, spineUp = body.SpineBendUp;
        float neckShare = neck != null ? .3f : 0f, headShare = 1f - neckShare;
        spineBend.Clear();
        foreach (var bone in spine)
        {
            float down = 90f * spineDown / count, up = 90f * spineUp / count;
            settings.pitchOffsetElements.Add(Look(bone, down, up));
            settings.yawOffsetElements.Add(Look(bone, 90f * .6f / count, 90f * .6f / count));
            settings.rollOffsetElements.Add(Look(bone, 90f / count, 90f / count));
            spineBend.Add((bone, down, up));
        }
        if (neck != null)
        {
            settings.pitchOffsetElements.Add(Look(neck, 90f * (1f - spineDown) * neckShare, 90f * (1f - spineUp) * neckShare));
            settings.yawOffsetElements.Add(Look(neck, 90f * .2f, 90f * .2f));
        }
        if (head != null)
        {
            settings.pitchOffsetElements.Add(Look(head, 90f * (1f - spineDown) * headShare, 90f * (1f - spineUp) * headShare));
            settings.yawOffsetElements.Add(Look(head, 90f * (neck != null ? .2f : .4f), 90f * (neck != null ? .2f : .4f)));
        }
        return settings;

        LookLayerElement Look(Transform bone, float positive, float negative) => new LookLayerElement
        {
            rigElement = Element(bone),
            clampedAngle = new Vector2(positive, negative),
            cachedClampedAngle = new Vector2(positive, negative)
        };
    }

    private static void Copy(CopyBoneSettings settings, Transform from, Transform to)
    {
        settings.copyFrom = Element(from);
        settings.copyTo = Element(to);
        settings.copySpace = ESpaceType.ComponentSpace;
        settings.copyMode = EModifyMode.Replace;
        settings.copyPosition = settings.copyRotation = true;
        settings.copyScale = false;
    }

    private static KRigElement Element(Transform bone) => new KRigElement(-1, bone != null ? bone.name : "None");

    private Transform Helper(string helperName, Transform parent)
    {
        var made = new GameObject(helperName).transform;
        made.SetParent(parent, false);
        helpers.Add(made.gameObject);
        return made;
    }

    private T Make<T>() where T : ScriptableObject
    {
        var made = ScriptableObject.CreateInstance<T>();
        made.hideFlags = HideFlags.DontSave;
        this.made.Add(made);
        return made;
    }

    // CAS's skeleton lists every transform under it (CharacterSkeleton.UpdateSkeleton); the body's is its humanoid bones and
    // the helpers, so hair and cloth bones never enter the Animator's stream. Filled at run time, the one difference
    // from KINEMATION's own setup.
    private static void Fill(CharacterSkeleton skeleton, List<Transform> bones)
    {
        var list = new List<CharacterSkeletonBone>(bones.Count);
        for (int i = 0; i < bones.Count; i++)
            list.Add(new CharacterSkeletonBone
            {
                transform = bones[i],
                rigElement = new KRigElement(i, bones[i].name, Depth(bones[i], bones[0])),
                meshPose = KTransform.Identity
            });
        SkeletonField.SetValue(skeleton, list);
        SkeletonMapField.SetValue(skeleton, null);
    }

    private static int Depth(Transform bone, Transform root)
    {
        int depth = 0;
        for (var at = bone; at != null && at != root; at = at.parent) depth++;
        return depth;
    }

    private static readonly FieldInfo SkeletonField = typeof(CharacterSkeleton).GetField("skeleton", BindingFlags.Instance | BindingFlags.NonPublic);
    private static readonly FieldInfo SkeletonMapField = typeof(CharacterSkeleton).GetField("_skeletonMap", BindingFlags.Instance | BindingFlags.NonPublic);

    // The body went (a new skin, a rebuild); its components went with it.
    private void Forget()
    {
        foreach (var helper in helpers) if (helper != null) Destroy(helper);
        helpers.Clear();
        foreach (var copy in made) if (copy != null) Destroy(copy);
        made.Clear();
        animator = null;
        headCopy = null;
        look = null;
        spineBend.Clear();
    }

    // ---- What CAS gets each frame (before its own update, BodyCasAnimation at 297) ----


    private void Feed()
    {
        look.alpha = body.CasLookWeight;
        look.pitchInput.SetDefaultValue(body.CasLookPitch);
        look.yawInput.SetDefaultValue(body.CasLookYaw);
        look.rollInput.SetDefaultValue(body.CasLookRoll);
    }

    private void Report(string message)
    {
        if (reported) return;
        reported = true;
        Debug.LogWarning("BodyCas: " + message, this);
    }
}
