using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// One player of a finished chapter stood on the hub's stage for the results (ResultsShow), the way the title screen
/// stands the character (MainMenuPreview): their own skin, colour set and outfit from the look the room knew
/// (MatchResults.Entry.Look), their hair and skirt swinging (PreviewCloth), their face alive (CharacterFace), and a
/// victory pose of its own (Resources/Poses, the clips Tools/Blender/Poses/make_victory_poses.py makes on Mayo: the
/// user, 2026-11-12, "포즈 네가 블렌더나 그런 거 써서 직접 만들어"). A copy and not the player's own body, so nothing of
/// the game's body (the gun hold, the look bend, the hands' IK, the network's pose) bends the pose: the show hides the
/// real bodies while it plays. It stands relaxed (the clip's first frame) until its shot strikes the pose, then holds
/// it. The pose runs on the show's clock (ResultsShow.Step), so a hitch pauses it instead of skipping the strike.
/// </summary>
[DisallowMultipleComponent]
public sealed class ResultsMannequin : MonoBehaviour
{
    public const string PoseFolder = "Poses", MvpPose = "enf_pose_fist";

    // The face each pose is struck with (Faces.csv ids); a pose not named here keeps the stage's held cute face.
    private static readonly Dictionary<string, string> moods = new Dictionary<string, string>
    {
        { "enf_pose_fist", "delight" },
        { "enf_pose_vsign", "pose.wink" },
        { "enf_pose_point", "pose.smug" },
        { "enf_pose_salute", "call.proud" },
        { "enf_pose_hips", "smug" },
    };

    private static AnimationClip[] poses;
    private PlayableGraph graph;
    private AnimationClipPlayable clip;
    private Animator animator;
    private CharacterFace face;
    private string pose = "";
    private float clock = -1f, length;
    private bool loops;

    /// <summary>
    /// Where the struck pose puts its leading hand (the higher one), its chest and its eyes, in this object's space, and
    /// how far a hand reaches out in front of the chest (m): the show's camera glides from the hand over the chest to the
    /// face (ResultsShow), and keeps clear of a hand thrust at it. Measured once from the pose's last frame.
    /// </summary>
    public Vector3 Hand { get; private set; } = new Vector3(.2f, 1f, .15f);
    public Vector3 Chest { get; private set; } = new Vector3(0f, 1.2f, .05f);
    public Vector3 Eyes { get; private set; } = new Vector3(0f, 1.45f, .08f);
    public float Reach { get; private set; }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics() => poses = null;

    /// <summary>The victory poses there are, in name order (any FBX dropped in Resources/Poses joins them).</summary>
    public static AnimationClip[] Poses
    {
        get
        {
            if (poses != null) return poses;
            var found = new List<AnimationClip>(Resources.LoadAll<AnimationClip>(PoseFolder));
            found.RemoveAll(c => c == null || c.name.StartsWith("__preview__"));
            found.Sort((a, b) => string.CompareOrdinal(a.name, b.name));
            return poses = found.ToArray();
        }
    }

    // The others' poses, in this order (the rest of the folder after them, by name): the V first, so a pair shows it.
    private static readonly string[] turns = { "enf_pose_vsign", "enf_pose_point", "enf_pose_salute", "enf_pose_hips" };

    /// <summary>A player's pose: the fist for the best of the room who won a title (<paramref name="best"/>), the others
    /// the <paramref name="turn"/>-th of the rest (0 for the first handed out), so neighbours differ.</summary>
    public static AnimationClip PoseFor(int turn, bool best)
    {
        var all = Poses;
        if (all.Length == 0) return null;
        var mvp = System.Array.Find(all, c => c.name == MvpPose);
        if (best && mvp != null) return mvp;
        var rest = new List<AnimationClip>(all);
        if (mvp != null && rest.Count > 1) rest.Remove(mvp);
        int Turn(AnimationClip c)
        {
            int at = System.Array.IndexOf(turns, c.name);
            return at < 0 ? turns.Length : at;
        }
        rest.Sort((a, b) => Turn(a) != Turn(b) ? Turn(a).CompareTo(Turn(b)) : string.CompareOrdinal(a.name, b.name));
        return rest[Mathf.Max(0, turn) % rest.Count];
    }

    /// <summary>The pose whose name holds <paramref name="part"/> ("vsign" finds enf_pose_vsign), or null.</summary>
    public static AnimationClip Named(string part)
    {
        if (string.IsNullOrWhiteSpace(part)) return null;
        part = part.Trim();
        foreach (var clip in Poses)
            if (string.Equals(clip.name, part, System.StringComparison.OrdinalIgnoreCase)) return clip;
        foreach (var clip in Poses)
            if (clip.name.IndexOf(part, System.StringComparison.OrdinalIgnoreCase) >= 0) return clip;
        return null;
    }

    /// <summary>Every pose's name, for the console.</summary>
    public static string Names()
    {
        var names = new List<string>();
        foreach (var clip in Poses) names.Add(clip.name);
        return names.Count > 0 ? string.Join(", ", names) : "(없음: Resources/Poses가 비었어요)";
    }

    /// <summary>Stands <paramref name="look"/> at <paramref name="feet"/>, turned to <paramref name="facing"/>, sized by
    /// <paramref name="scale"/>, ready to strike <paramref name="pose"/> (null: the title screen's idle). Null if the
    /// look's character can't be made.</summary>
    public static ResultsMannequin Make(string look, Vector3 feet, Quaternion facing, float scale, AnimationClip pose)
    {
        var profile = PlayerBodyProfile.Load();
        if (profile == null) return null;
        PlayerBodyProfile.SplitLook(OutfitLook.SkinPart(look ?? ""), out string skinId, out string variantId);
        var skin = profile.SkinOf(skinId) ?? profile.CurrentSkin;
        var source = profile.ModelOf(skin);
        if (source == null) return null;
        var host = new GameObject("Results pose " + (skin != null ? skin.Id : "?"));
        host.transform.SetPositionAndRotation(feet, facing);
        var model = Instantiate(source, host.transform, false);
        model.transform.localScale *= Mathf.Max(.1f, scale);
        var variant = skin?.VariantOf(variantId);
        SkinVariant.Wear(model, null, variant);
        foreach (var renderer in model.GetComponentsInChildren<Renderer>())
            if (renderer is SkinnedMeshRenderer skinned) skinned.updateWhenOffscreen = true;
        var outfit = OutfitLook.Parse(OutfitLook.OutfitPart(look ?? ""));
        var dressing = OutfitDresser.Dress(model, source, skin, outfit);
        // On the soles it draws, as in the game and on the title screen (DrawnSole).
        model.transform.localPosition += Vector3.down * (DrawnSole.Of(model.transform, dressing.Leaving) * model.transform.localScale.y);
        PreviewCloth.Wear(model, skin?.Id, dressing, outfit.ToString());
        var face = CharacterFace.Wear(model, skin?.Id);
        if (face != null) face.Unscaled = face.Watched = face.Showing = true;
        var mannequin = host.AddComponent<ResultsMannequin>();
        mannequin.face = face;
        mannequin.Bind(model, pose ?? profile.Find(MainMenuPreview.IdleClip));
        return mannequin;
    }

    private void Bind(GameObject model, AnimationClip pose)
    {
        animator = model.GetComponentInChildren<Animator>();
        if (animator == null || pose == null) return;
        this.pose = pose.name;
        animator.applyRootMotion = false;
        // Seen only by the results' own camera: never let culling stop it.
        animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        clip = AnimationPlayableUtilities.PlayClip(animator, pose, out graph);
        // The show's clock drives it, not the game's.
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        length = Mathf.Max(.01f, pose.length);
        loops = pose.isLooping;
        if (!loops) Measure();
        clip.SetTime(0d);
        graph.Evaluate(0f);
    }

    // The struck pose's hand, chest and eyes, from its last frame (put back to the first by Bind).
    private void Measure()
    {
        if (!animator.isHuman) return;
        clip.SetTime(length);
        graph.Evaluate(0f);
        Transform Bone(HumanBodyBones bone) => animator.GetBoneTransform(bone);
        var head = Bone(HumanBodyBones.Head);
        var chest = Bone(HumanBodyBones.UpperChest) != null ? Bone(HumanBodyBones.UpperChest) : Bone(HumanBodyBones.Chest);
        var leftEye = Bone(HumanBodyBones.LeftEye);
        var rightEye = Bone(HumanBodyBones.RightEye);
        Vector3? Palm(HumanBodyBones wrist, HumanBodyBones knuckle)
        {
            var w = Bone(wrist);
            if (w == null) return null;
            var k = Bone(knuckle);
            // The middle of the hand (a fist's front), not the wrist the bone sits at.
            return k != null ? Vector3.Lerp(w.position, k.position, .7f) : w.position;
        }
        var left = Palm(HumanBodyBones.LeftHand, HumanBodyBones.LeftMiddleProximal);
        var right = Palm(HumanBodyBones.RightHand, HumanBodyBones.RightMiddleProximal);
        if (head != null)
        {
            Vector3 eyes = leftEye != null && rightEye != null
                ? (leftEye.position + rightEye.position) * .5f
                : head.position + transform.up * .08f + transform.forward * .06f;
            Eyes = transform.InverseTransformPoint(eyes);
        }
        if (chest != null) Chest = transform.InverseTransformPoint(chest.position);
        if (left.HasValue || right.HasValue)
        {
            // The higher hand leads (the right one when they are level): the raised fist, the V, the salute.
            Vector3 lead = !left.HasValue ? right.Value
                : !right.HasValue ? left.Value
                : left.Value.y > right.Value.y + .05f ? left.Value : right.Value;
            Hand = transform.InverseTransformPoint(lead);
            float reach = 0f;
            if (left.HasValue) reach = Mathf.Max(reach, transform.InverseTransformPoint(left.Value).z - Chest.z);
            if (right.HasValue) reach = Mathf.Max(reach, transform.InverseTransformPoint(right.Value).z - Chest.z);
            Reach = reach;
        }
    }

    /// <summary>Strikes the pose now (from relaxed), and holds it, with the face that goes with it.</summary>
    public void Strike()
    {
        clock = 0f;
        if (face != null && moods.TryGetValue(pose, out string mood)) face.Pulse(mood, 1f, 30f);
    }

    /// <summary>
    /// On the show's clock, every frame: relaxed until struck, then the strike plays on and the pose holds on its last
    /// frame. Without poses imported yet it stands in the title screen's idle, which loops.
    /// </summary>
    public void Tick(float step)
    {
        if (!graph.IsValid()) return;
        if (loops) clock = Mathf.Max(0f, clock) + step;
        else if (clock >= 0f) clock = Mathf.Min(clock + step, length);
        clip.SetTime(loops ? clock % length : Mathf.Max(0f, clock));
        graph.Evaluate(0f);
    }

    private void OnDestroy()
    {
        if (graph.IsValid()) graph.Destroy();
    }
}
