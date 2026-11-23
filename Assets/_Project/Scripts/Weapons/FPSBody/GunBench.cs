#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using KINEMATION.CharacterAnimationSystem.Integrations.FPSAnimationPack.Scripts;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

/// <summary>
/// The editor's bench for a gun in a body's hands, without playing: a character is stood in a gun's action at a moment,
/// the gun put where that body holds it and moved as the action's track moves it, both palms put on the gun by arm IK and
/// the fingers bent as the track has them, with the very classes the game does each with (HoldBody, GunHold, GunTrack,
/// SupportSlide, ArmIK.HoldPalm, HandGrip). Then it is measured (how far each palm ends up from its place, how straight
/// each arm is) and, when asked, photographed from the eyes and from outside.
/// So a hold or a draw that is wrong on one character is a number and a picture before anyone plays it, and the session
/// that changed it looks first (the user, 2026-11-15: every break of the gun's hold so far was found by them, playing).
/// What it shows is the gun's base place and the hands: the body standing as the action's clip stands it, the view level,
/// nothing of FPS Animation Framework's layers on top (no aim, sway, recoil or wall). A grip the gun wears is not held yet
/// (the support hand goes where the bare gun is held).
/// ENFILADE → 총 → 검사대 runs every character with every gun's hold, numbers only. A tool outside Unity asks through
/// Temp/enfilade-gunbench.request, whose lines say what to stand (all optional):
///   skins=mayo,ichigo        characters by id (all)
///   guns=Herrington1187      guns by their WeaponData name (all)
///   actions=Hold,Draw        GunTrackKind names (Hold)
///   times=0,0.2,0.63         moments of an action to photograph, s (five across it)
///   pictures=fp,side,front   from the eyes (the body's own head left out), from the right side, from the front quarter (none)
///   width=640                a picture's width in pixels (16:9)
///   reach=none               the gun moves as the clip moves it, whatever the arms reach (left out: carried by the hold's
///                            reach rule, GunHold.Carried, the shoulder joints where the action's clip has them)
///   rule.ReachMost=0.85      what if one of the hold's rules were another number (a field of GunHold.Rules, by its name):
///                            for this run only, the table stays as it is
///   trace=yes                every moment measured, a line each under its action (ask for one gun's one action)
/// Logs/GunBench/report.txt has the numbers; each stood action's pictures are one sheet beside it, a column to a moment
/// and a row to a view. It lives beside FPSBody because it stands the body with the game's own classes.
/// </summary>
[InitializeOnLoad]
internal static class GunBench
{
    private const string Request = "Temp/enfilade-gunbench.request", Folder = "Logs/GunBench", ReportPath = Folder + "/report.txt";
    // The report marks a palm that ends further than this from its place on the gun (m): the arm did not reach it. The
    // bench's own; it decides nothing in the game.
    private const float PalmMissed = .01f;
    // An action is measured at no more than this many of its frames.
    private const int MostSamples = 400;
    // The first-person lens: the game's default field of view (GameSettings.fieldOfView 90 across a 16:9 screen).
    private const float ScreenFieldAcross = 90f, ScreenAspect = 16f / 9f;
    private static readonly Color Backdrop = new Color(.32f, .34f, .38f), Ambient = new Color(.45f, .47f, .52f);

    private static double next;

    static GunBench() => EditorApplication.update += Tick;

    private static void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        if (now < next) return;
        next = now + .5;
        if (!File.Exists(Request)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        Ask ask;
        try
        {
            ask = Ask.Read(File.ReadAllLines(Request));
            File.Delete(Request);
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            return;
        }
        Run(ask);
    }

    [MenuItem("ENFILADE/총/검사대 (모든 캐릭터, 쥐기)")]
    private static void RunAll() => Run(new Ask());

    // ---- What was asked ----

    private sealed class Ask
    {
        public HashSet<string> Skins, Guns;
        public readonly List<GunTrackKind> Actions = new List<GunTrackKind>();
        public List<float> Times;
        public bool Eyes, Side, Front, Trace;
        public int Width = 640;
        // How the gun is carried through an action: by the hold's reach rule with the shoulder joints where the action's
        // clip has them (the rule as the game will ask it), or not at all (the clip's move as it is, which is what the game
        // does until the rule is in it).
        public Carry Reach = Carry.Rule;
        // The hold's rules asked about with another number, by the field's name.
        public readonly Dictionary<string, float> WhatIf = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);

        public bool Pictures => Eyes || Side || Front;

        public static Ask Read(string[] lines)
        {
            var ask = new Ask();
            foreach (string raw in lines)
            {
                int cut = raw.IndexOf('=');
                if (cut <= 0) continue;
                string key = raw.Substring(0, cut).Trim().ToLowerInvariant();
                var values = raw.Substring(cut + 1).Split(new[] { ',', ' ', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (key.StartsWith("rule.", StringComparison.Ordinal))
                {
                    if (values.Length > 0 && float.TryParse(values[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float number))
                        ask.WhatIf[key.Substring(5)] = number;
                    continue;
                }
                switch (key)
                {
                    case "skins":
                        ask.Skins = new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
                        break;
                    case "guns":
                        ask.Guns = new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
                        break;
                    case "actions":
                        foreach (string value in values)
                            if (Enum.TryParse(value, true, out GunTrackKind kind) && !ask.Actions.Contains(kind)) ask.Actions.Add(kind);
                        break;
                    case "times":
                        ask.Times = new List<float>();
                        foreach (string value in values)
                            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out float time)) ask.Times.Add(Mathf.Max(0f, time));
                        break;
                    case "pictures":
                        foreach (string value in values)
                        {
                            string view = value.ToLowerInvariant();
                            if (view == "fp" || view == "eyes") ask.Eyes = true;
                            else if (view == "side") ask.Side = true;
                            else if (view == "front" || view == "tp") ask.Front = true;
                        }
                        break;
                    case "reach":
                        if (values.Length > 0) ask.Reach = values[0].ToLowerInvariant() == "none" ? Carry.None : Carry.Rule;
                        break;
                    case "trace":
                        ask.Trace = values.Length > 0 && values[0].ToLowerInvariant() != "no";
                        break;
                    case "width":
                        if (values.Length > 0 && int.TryParse(values[0], out int width)) ask.Width = Mathf.Clamp(width, 160, 1920);
                        break;
                }
            }
            return ask;
        }
    }

    private enum Carry { None, Rule }

    // ---- The run ----

    // The numbers the game holds a gun by, read where the game reads them.
    private struct Numbers
    {
        public GunHold.Rules Rules;
        public float SlideFade, ShoulderReach, Near;
        // The game's camera sits this far above and ahead of the eye bones (PlayerBody's eyeAdjust and eyeViewAhead, m).
        public float EyeUp, EyeAhead;
    }

    // Player.csv's Body.eyeAdjust ("up;ahead", m) and Body.eyeViewAhead, as PlayerBody places its camera from the eyes.
    private static void EyeOffsets(out float up, out float ahead)
    {
        up = 0f;
        ahead = GameTables.Number(PlayerMovement.TableName, "Body", "eyeViewAhead", .02f);
        if (!GameTables.TryGetRow(PlayerMovement.TableName, "Body", out var row) || !row.TryGetValue("eyeAdjust", out string cell) || cell == null) return;
        var parts = cell.Split(';');
        if (parts.Length > 0 && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float adjustUp)) up = adjustUp;
        if (parts.Length > 1 && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float adjustAhead)) ahead += adjustAhead;
    }

    private static void Run(Ask ask)
    {
        var profile = PlayerBodyProfile.Load();
        var assets = FPSBodyAssets.Instance;
        var bakes = GunBakes.Instance;
        var tracks = GunTracks.Instance;
        if (profile == null || assets == null || bakes == null || tracks == null)
        {
            Debug.LogWarning("GunBench: the body profile, the gun list, the gun bakes or the gun tracks are missing; nothing to stand.");
            return;
        }
        if (ask.Actions.Count == 0) ask.Actions.Add(GunTrackKind.Hold);
        var numbers = new Numbers
        {
            Rules = FPSBody.TableRules(),
            SlideFade = FPSBody.TableSlideFade(),
            ShoulderReach = GameTables.Number(PlayerMovement.TableName, "Hands", "shoulderReach", 15f),
            Near = GameTables.Number(PlayerMovement.TableName, "Body", "firstPersonNear", .01f)
        };
        EyeOffsets(out numbers.EyeUp, out numbers.EyeAhead);
        string whatIf = Suppose(ask, ref numbers.Rules);
        // The arms ease in at the end of their reach as the game's do (PlayerBodyHands sets the same number every frame).
        float softWas = ArmIK.SoftReachFrom;
        ArmIK.SoftReachFrom = GameTables.Number(PlayerMovement.TableName, "Hands", "softReachFrom", .93f);
        // Read afresh: outside play nothing else forgets the guns' handles.
        GunHandles.Forget();

        var text = new StringBuilder();
        text.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} gun bench: each character stood in a gun's action with the game's own classes, the view level, "
            + "no FPSAF layer on top. off its grip = how far a palm that the clip has on the gun ends from its place there after the "
            + "arm's IK (cm), and how far its fingers' way or facing is turned from it (degrees); short = the same for a palm the "
            + $"clip has taken off the gun (to a pouch, into the air: over {numbers.Rules.HoldsWithin * 100f:0.#} cm from the gun's surface), "
            + "which is as far as this body's arm goes toward where KINEMATION's arm went; straight = shoulder joint to wrist over the "
            + $"arm's own length. '!' marks a palm on the gun over {PalmMissed * 100f:0.#} cm off its grip (the bench's own mark). "
            + (ask.Reach == Carry.None ? "The gun moves as the clip moves it, whatever the arms reach (reach=none)."
                : "The gun is carried by the hold's reach rule (GunHold.Carried) with the shoulder joints where the action's clip has them, "
                    + $"each arm as much as its hand has hold of the gun (all of it within {numbers.Rules.HoldsWithin * 100f:0.#} cm of the gun's "
                    + $"surface, none from {numbers.Rules.TakesHoldWithin * 100f:0.#} cm): 'brought in' = how far that moved the gun from where the "
                    + "clip has it; 'ahead of a hold' = how much of that was for a hand whose palm was not at the gun's own surface at that "
                    + "moment (a hand on its way to the gun or from it, and one seating a magazine, whose palm is on the magazine). The rule "
                    + "moves the gun the least way that lets the arms reach, toward the body if need be, and nothing keeps the gun out of "
                    + "the body yet: an action brought in far is one a body cannot do as its clip has it (Docs/Features/GunHandling.md, "
                    + "section 10), so read it beside its pictures.")
            + whatIf);
        Directory.CreateDirectory(Folder);
        Stage stage = null;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        int stood = 0, sheets = 0;
        try
        {
            if (ask.Pictures) stage = new Stage();
            float camera = PlayerBody.StandingCamera();
            int done = 0;
            foreach (var skin in profile.Skins)
            {
                done++;
                if (skin == null || (ask.Skins != null && !ask.Skins.Contains(skin.Id))) continue;
                var model = profile.ModelOf(skin);
                if (model == null) continue;
                EditorUtility.DisplayProgressBar("ENFILADE: 총 검사대", skin.Id, done / (float)Mathf.Max(1, profile.Skins.Count));
                Stand(ask, numbers, stage, profile, skin.Id, model, camera, assets, bakes, tracks, text, ref stood, ref sheets);
            }
        }
        catch (Exception error)
        {
            text.AppendLine("! the bench stopped: " + error.Message);
            Debug.LogException(error);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            stage?.Dispose();
            GunHandles.Forget();
            ArmIK.SoftReachFrom = softWas;
        }
        text.AppendLine();
        text.AppendLine($"({stood} actions stood, {sheets} picture sheets, {watch.Elapsed.TotalSeconds:0.0} s)");
        File.WriteAllText(ReportPath, text.ToString());
        Debug.Log($"GunBench: {stood} actions stood, {sheets} picture sheets; the numbers are in {ReportPath}.");
    }

    // The hold's rules with the numbers a request supposes instead of the table's, and a line for the report saying so.
    private static string Suppose(Ask ask, ref GunHold.Rules rules)
    {
        if (ask.WhatIf.Count == 0) return "";
        var text = new StringBuilder(" WHAT IF, for this run only:");
        object boxed = rules;
        foreach (var pair in ask.WhatIf)
        {
            var field = typeof(GunHold.Rules).GetField(pair.Key, BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
            if (field == null || field.FieldType != typeof(float))
            {
                text.Append($" (no rule is named {pair.Key})");
                continue;
            }
            text.Append($" {field.Name} = {pair.Value.ToString("0.###", CultureInfo.InvariantCulture)} (the table has "
                + $"{((float)field.GetValue(boxed)).ToString("0.###", CultureInfo.InvariantCulture)});");
            field.SetValue(boxed, pair.Value);
        }
        rules = (GunHold.Rules)boxed;
        return text.ToString();
    }

    // One character with every gun asked for.
    private static void Stand(Ask ask, in Numbers numbers, Stage stage, PlayerBodyProfile profile, string id, GameObject model, float camera, FPSBodyAssets assets,
        GunBakes bakes,
        GunTracks tracks, StringBuilder text, ref int stood, ref int sheets)
    {
        var instance = Object.Instantiate(model);
        instance.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            var animator = instance.GetComponentInChildren<Animator>();
            var head = animator != null && animator.isHuman ? animator.GetBoneTransform(HumanBodyBones.Head) : null;
            if (head == null)
            {
                text.AppendLine();
                text.AppendLine($"== {id}: no humanoid avatar, not stood.");
                return;
            }
            // As a player's body plays (PlayerBody.Build): HoldBody.Measure says why.
            animator.applyRootMotion = false;
            instance.transform.localScale = Vector3.one * PlayerBody.DrawnScaleOf(animator, instance.transform, camera);
            // The arms and hands measured at rest, before any clip, as PlayerBodyHands binds them.
            var right = ArmIK.Create(animator, false);
            var left = ArmIK.Create(animator, true);
            if (right == null || left == null)
            {
                text.AppendLine();
                text.AppendLine($"== {id}: no humanoid arms, not stood.");
                return;
            }
            var shoulder = HoldBody.ShoulderOf(animator);
            // The player's own head, by the game's rule (PlayerBody.Build): the view from the eyes is taken without it, as the
            // game shows it with the own-head option off, so the gun and the hands are seen and not the inside of a face.
            var headParts = new List<Renderer>();
            foreach (var renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                bool onHead = renderer is SkinnedMeshRenderer skinned && HeadMeshes.OnHead(skinned, head);
                foreach (string part in profile.FirstPersonShadowOnly)
                    onHead |= string.Equals(renderer.name, part, StringComparison.OrdinalIgnoreCase);
                if (onHead) headParts.Add(renderer);
            }
            stage?.Take(instance);
            text.AppendLine();
            text.AppendLine($"== {id} ({model.name}), drawn x{Mathf.Abs(instance.transform.lossyScale.x):0.000}");

            foreach (var entry in assets.guns)
            {
                if (entry.weapon == null || entry.prefab == null) continue;
                if (ask.Guns != null && !ask.Guns.Contains(entry.weapon.name)) continue;
                if (!bakes.TryGet(model, entry.weapon, out var bake, out bool own) || bake.pose == null) continue;
                var set = tracks.For(entry.weapon);
                if (set == null)
                {
                    text.AppendLine($"{entry.weapon.name}: no tracks (ENFILADE → 총 → 총 동작 굽기).");
                    continue;
                }
                var gun = Object.Instantiate(entry.prefab);
                gun.hideFlags = HideFlags.HideAndDontSave;
                try
                {
                    gun.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                    gun.transform.localScale = Vector3.one;
                    var kinemation = gun.GetComponent<CasFPSWeapon>();
                    Quaternion frame = FPSBody.GunFrameOf(kinemation);
                    // The sight and parts the gun starts with, as the game's gun shows them (FPSGun.Set).
                    var sights = gun.GetComponent<GunSights>();
                    if (sights != null && sights.Current < 0) sights.Select(sights.initial);
                    var parts = gun.GetComponent<GunAttachments>();
                    if (parts != null) parts.Begin();
                    var hold = GunHold.For(GunHandles.Of(entry, set, frame, gun.transform, kinemation != null ? kinemation.aimPoint : null), numbers.Rules);
                    var body = HoldBody.Measure(animator, bake.pose, shoulder);
                    if (body == null)
                    {
                        text.AppendLine($"{entry.weapon.name}: this body could not be measured in its pose.");
                        continue;
                    }
                    var clip = body.PlaceOf(bake, own);
                    var pose = hold != null ? hold.On(body, clip) : new GunHold.Pose { Gun = clip };
                    stage?.Take(gun);
                    var one = new Standing
                    {
                        Id = id, Root = instance.transform, Animator = animator, Head = head, Right = right, Left = left, Gun = gun.transform,
                        Frame = frame, Start = pose.Gun, Set = set, Bake = bake, Body = body,
                        Slide = new SupportSlide(pose.Slide, hold != null ? hold.Gun.LeftPalm.z : 0f, numbers.SlideFade),
                        Hold = hold, Reach = ask.Reach, HoldsWithin = numbers.Rules.HoldsWithin,
                        ShoulderReach = numbers.ShoulderReach, Near = numbers.Near,
                        EyeUp = numbers.EyeUp, EyeAhead = numbers.EyeAhead, HeadParts = headParts
                    };
                    one.FindEyes();
                    foreach (var kind in ask.Actions)
                    {
                        var track = set.Track(kind);
                        var bodyClip = BodyClipOf(bake, kind);
                        if (track == null || bodyClip == null) continue;
                        Act(ask, stage, one, entry.weapon.name, kind, track, bodyClip, hold != null ? hold.Name : "as its clip has it", text, ref sheets);
                        stood++;
                    }
                }
                finally
                {
                    Object.DestroyImmediate(gun);
                }
            }
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    // The body's own clip of an action (remade for our bodies, GunBakes): what the game plays on the body while the track
    // moves the gun.
    private static AnimationClip BodyClipOf(in GunBakes.Entry bake, GunTrackKind kind)
    {
        switch (kind)
        {
            case GunTrackKind.Hold:
            case GunTrackKind.HoldNoGrip: return bake.pose;
            case GunTrackKind.Draw: return bake.draw;
            case GunTrackKind.Holster: return bake.holster;
            case GunTrackKind.Inspect: return bake.inspect;
            case GunTrackKind.ReloadTactical: return bake.reloadTactical;
            case GunTrackKind.ReloadEmpty: return bake.reloadEmpty;
            case GunTrackKind.ReloadShell: return bake.reloadShell;
            case GunTrackKind.ReloadEnd: return bake.reloadEnd;
            case GunTrackKind.Cycle: return bake.fire;
        }
        return null;
    }

    // ---- One body with one gun ----

    // What one moment of an action comes to.
    private struct Posed
    {
        public float RightMiss, RightTurn, LeftMiss, LeftTurn, RightStraight, LeftStraight;
        // The clip has this hand on the gun at this moment (its palm at the gun's surface, wherever on it), not away at a
        // pouch or in the air.
        public bool RightOnGun, LeftOnGun;
        // How far the reach rule moved the gun from where the clip has it (m in game), that move itself and where the clip
        // has the gun from the eyes as the hold's pose stands them, both along the body's own axes (m in game: x right, y up,
        // z ahead), for how fast each changes.
        public float BroughtIn;
        public Vector3 Brought, ClipAt;
        // How much of that move was for a hand whose palm is not at the gun's own surface at this moment (m in game): the
        // gun on its way to meet a hand that is coming to it, on its way back after one left, or held in for a hand that is
        // seating a magazine.
        public float Ahead;
        // Each palm's gap from the gun's surface (m), how much its hand has hold of the gun (GunHold.Holds), and how straight
        // the clip's place for the gun would have that arm before the rule moves it.
        public float RightGap, LeftGap, RightHolds, LeftHolds, RightWould, LeftWould;
        // How far the body's own clip has each shoulder joint and the eyes from where the hold's pose has them (m in game),
        // before the arms are put on the gun.
        public Vector3 RightShoulderMoved, LeftShoulderMoved, EyesMoved;
        public Quaternion CameraTurn;
    }

    private sealed class Standing
    {
        public string Id;
        public Transform Root, Head, Gun;
        public Animator Animator;
        public ArmIK Right, Left;
        public Quaternion Frame;
        // Where this body holds the gun: KINEMATION's weapon bone in the body's own space (GunHold.Pose.Gun).
        public KTransform Start;
        public GunTrackSet Set;
        public GunBakes.Entry Bake;
        public HoldBody Body;
        public SupportSlide Slide;
        // The gun's hold on this body (null for a gun whose pose could not be read: it moves as its clip moves it).
        public GunHold Hold;
        public Carry Reach;
        public float HoldsWithin;
        public float ShoulderReach, Near, EyeUp, EyeAhead;
        // The body's own head (hidden for the view from the eyes).
        public List<Renderer> HeadParts;
        // The game's camera and the eyes themselves (HoldBody.Eyes), each as a point of the head.
        private Vector3 eyeInHead, eyesOnly;
        private readonly Vector3[] rightDigits = new Vector3[GunArms.DigitCount], leftDigits = new Vector3[GunArms.DigitCount];

        /// <summary>
        /// Where the game's camera rides, as a point of the head, taken as the gun's pose stands the body, so it goes with the
        /// head through an action: the eyes (HoldBody.Eyes) and from them the game's own step up and ahead (PlayerBody's
        /// eyeAdjust and eyeViewAhead, metres in the world). The hold itself is measured from the eyes without that step.
        /// </summary>
        public void FindEyes()
        {
            ClipSampler.Sample(Bake.pose, Animator.gameObject, 0f);
            eyeInHead = Head.InverseTransformPoint(Root.TransformPoint(Body.Eyes) + Root.up * EyeUp + Root.forward * EyeAhead);
            eyesOnly = Head.InverseTransformPoint(Root.TransformPoint(Body.Eyes));
        }

        public Vector3 Eyes => Head.TransformPoint(eyeInHead);

        /// <summary>A point of the body's space from the gun as this body holds it, in the gun's own frame (m in game).</summary>
        public Vector3 FromGun(Vector3 point) => Quaternion.Inverse(Start.rotation * Frame) * (point - Start.position) * Body.Scale;

        /// <summary>
        /// The body in <paramref name="bodyClip"/> at <paramref name="time"/>, the gun where this body holds it moved as the
        /// track moves it, both hands on it: the base place and the hands as the game's parts make them.
        /// </summary>
        public bool Pose(GunTrackKind kind, GunTrack track, AnimationClip bodyClip, float time, out Posed posed)
        {
            posed = default;
            posed.CameraTurn = Quaternion.identity;
            ClipSampler.Sample(bodyClip, Animator.gameObject, Mathf.Clamp(time, 0f, bodyClip.length));
            if (!track.Sample(time, rightDigits, leftDigits, out GunContact moment, out posed.CameraTurn)) return false;
            posed.RightShoulderMoved = (Root.InverseTransformPoint(Right.Upper.position) - Body.Right.Shoulder) * Body.Scale;
            posed.LeftShoulderMoved = (Root.InverseTransformPoint(Left.Upper.position) - Body.Left.Shoulder) * Body.Scale;
            posed.EyesMoved = (Root.InverseTransformPoint(Head.TransformPoint(eyesOnly)) - Body.Eyes) * Body.Scale;
            // The plain hold's support hand is the bare gun's where the gun has such a pose (FPSBody.HoldSide).
            Vector3 leftPalm = moment.LeftPalm, leftFingers = moment.LeftFingers, leftFacing = moment.LeftFacing, leftElbow = moment.LeftElbow;
            float leftGap = moment.LeftGap;
            var leftWays = moment.LeftDigits;
            var bare = kind == GunTrackKind.Hold ? Set.Track(GunTrackKind.HoldNoGrip) : null;
            if (bare != null && bare.Sample(0f, null, leftDigits, out GunContact open, out _))
            {
                leftPalm = open.LeftPalm;
                leftFingers = open.LeftFingers;
                leftFacing = open.LeftFacing;
                leftElbow = open.LeftElbow;
                leftGap = open.LeftGap;
                leftWays = open.LeftDigits;
            }
            // On the gun: the palm at the gun's surface, wherever on it (the hold's rule: GunHold.Holds).
            posed.RightOnGun = moment.RightGap <= HoldsWithin;
            posed.LeftOnGun = leftGap <= HoldsWithin;
            posed.RightGap = moment.RightGap;
            posed.LeftGap = leftGap;

            // Where the action's clip has the gun: the hold (KINEMATION's weapon bone in the body's own space) moved as the
            // track moves it. The gun is its true size whatever the body's, so its move is in metres whatever the body's scale.
            Quaternion holdAxes = Start.rotation * Frame;
            var bone = new KTransform(Start.position + holdAxes * (moment.GunMove / Body.Scale), holdAxes * moment.GunTurn * Quaternion.Inverse(Frame),
                Vector3.one);
            // Then as this body's arms carry it: no further than the arms whose hands have hold of it reach (GunHold.Carried),
            // the shoulder joints where the action's clip has them at this moment.
            if (Hold != null && Reach != Carry.None)
            {
                var rightArm = Body.Right.At(Root.InverseTransformPoint(Right.Upper.position));
                var leftArm = Body.Left.At(Root.InverseTransformPoint(Left.Upper.position));
                Vector3 leftAt = Slide.Of(leftPalm);
                posed.RightHolds = Hold.Holds(moment.RightGap);
                posed.LeftHolds = Hold.Holds(leftGap);
                Quaternion axes = bone.rotation * Frame;
                posed.RightWould = rightArm.Straight(rightArm.Wrist(bone.position + axes * (moment.RightPalm / Body.Scale), axes * moment.RightFingers));
                posed.LeftWould = leftArm.Straight(leftArm.Wrist(bone.position + axes * (leftAt / Body.Scale), axes * leftFingers));
                var carried = Hold.Carried(rightArm, leftArm, Body.Scale, bone, moment.RightPalm, moment.RightFingers, posed.RightHolds,
                    leftAt, leftFingers, posed.LeftHolds);
                // What the hands at the gun's surface at this very moment need of it, nothing taken up for a hand on its way.
                var needed = Hold.Carried(rightArm, leftArm, Body.Scale, bone, moment.RightPalm, moment.RightFingers, posed.RightOnGun ? 1f : 0f,
                    leftAt, leftFingers, posed.LeftOnGun ? 1f : 0f);
                posed.Ahead = Vector3.Distance(carried.position, needed.position) * Body.Scale;
                posed.Brought = (carried.position - bone.position) * Body.Scale;
                posed.BroughtIn = posed.Brought.magnitude;
                posed.ClipAt = Body.FromEyes(bone.position);
                bone = carried;
            }
            else posed.ClipAt = Body.FromEyes(bone.position);
            Vector3 gunAt = Root.TransformPoint(bone.position);
            Quaternion holdTurn = Root.rotation * holdAxes, gunTurn = Root.rotation * bone.rotation * Frame;
            Gun.SetPositionAndRotation(gunAt, gunTurn);
            Hand(Right, false, gunAt, gunTurn, holdTurn, moment.RightPalm, moment.RightFingers, moment.RightFacing, moment.RightElbow, moment.RightDigits,
                out posed.RightMiss, out posed.RightTurn);
            Hand(Left, true, gunAt, gunTurn, holdTurn, Slide.Of(leftPalm), leftFingers, leftFacing, leftElbow, leftWays, out posed.LeftMiss, out posed.LeftTurn);
            posed.RightStraight = Straight(Right);
            posed.LeftStraight = Straight(Left);
            return true;
        }

        // One hand onto its place on the gun, as PlayerBodyHands puts it there (HoldGunContacts, then the fingers).
        private void Hand(ArmIK arm, bool isLeft, Vector3 gunAt, Quaternion gunTurn, Quaternion holdTurn, Vector3 palm, Vector3 fingers, Vector3 facing,
            Vector3 elbow, Vector3[] ways, out float miss, out float turn)
        {
            Vector3 at = gunAt + gunTurn * palm, along = gunTurn * fingers, faces = gunTurn * facing;
            at = GunHands.Palm(Id, isLeft, at, along, faces);
            // KINEMATION's elbow way is kept in the frame of the gun at its hold (FPSBody.ElbowWay lays it on the rest hold).
            bool hasWay = elbow.sqrMagnitude > .5f;
            arm.HoldPalm(at, along, faces, hasWay, hasWay ? holdTurn * elbow.normalized : Vector3.zero, 1f, ShoulderReach);
            if (ways != null) arm.WearDigits(ways, 1f, null, 0f, 0f);
            arm.KeepFingersHuman();
            miss = Vector3.Distance(arm.PalmPosition, at);
            turn = Mathf.Max(Vector3.Angle(arm.FingersForward, along), Vector3.Angle(arm.PalmFacing, faces));
        }

        private static float Straight(ArmIK arm) => arm.Reach > 1e-4f ? Vector3.Distance(arm.Upper.position, arm.Hand.position) / arm.Reach : 0f;
    }

    // One action of one gun on one body: measured across its frames, and photographed at the moments asked for.
    private static void Act(Ask ask, Stage stage, Standing one, string gun, GunTrackKind kind, GunTrack track, AnimationClip bodyClip, string holdName,
        StringBuilder text, ref int sheets)
    {
        bool still = kind == GunTrackKind.Hold || kind == GunTrackKind.HoldNoGrip;
        int frames = still ? 1 : track.Frames, step = Mathf.Max(1, Mathf.CeilToInt(frames / (float)MostSamples));
        Posed most = default;
        float rightLeast = 1f, leftLeast = 1f, rightMissAt = 0f, leftMissAt = 0f, rightShort = 0f, leftShort = 0f, broughtIn = 0f, broughtInAt = 0f;
        float ahead = 0f, aheadAt = 0f;
        Vector3 rightShoulderMost = Vector3.zero, leftShoulderMost = Vector3.zero, eyesMost = Vector3.zero;
        // How fast the reach rule moves the gun and how fast the clip itself does (m/s), each at its fastest between two
        // moments measured: a rule that jumped would be far faster than the motion it follows.
        float ruleFastest = 0f, ruleFastestAt = 0f, clipFastest = 0f, lastTime = 0f;
        Vector3 lastBrought = Vector3.zero, lastClipAt = Vector3.zero;
        var trace = ask.Trace ? new StringBuilder() : null;
        int samples = 0;
        for (int i = 0; i < frames; i += step)
        {
            float time = track.TimeOf(i);
            if (!one.Pose(kind, track, bodyClip, time, out Posed posed)) continue;
            if (samples > 0 && time > lastTime)
            {
                float rule = Vector3.Distance(posed.Brought, lastBrought) / (time - lastTime);
                if (rule > ruleFastest)
                {
                    ruleFastest = rule;
                    ruleFastestAt = time;
                }
                clipFastest = Mathf.Max(clipFastest, Vector3.Distance(posed.ClipAt, lastClipAt) / (time - lastTime));
            }
            lastTime = time;
            lastBrought = posed.Brought;
            lastClipAt = posed.ClipAt;
            samples++;
            trace?.AppendLine($"   {time:0.00}: shoulders moved R {Cm(posed.RightShoulderMoved)} L {Cm(posed.LeftShoulderMoved)} eyes {Cm(posed.EyesMoved)}; "
                + $"gun {Cm(posed.ClipAt)} from the eyes, the rule moves it {Cm(posed.Brought)} = {posed.BroughtIn * 100f:0.0} cm "
                + $"({posed.Ahead * 100f:0.0} ahead of a hold); right gap {posed.RightGap * 100f:0.0} holds {posed.RightHolds:0.00} asked {posed.RightWould:0.00} "
                + $"is {posed.RightStraight:0.00} off {posed.RightMiss * 100f:0.0}; left gap {posed.LeftGap * 100f:0.0} holds {posed.LeftHolds:0.00} asked "
                + $"{posed.LeftWould:0.00} is {posed.LeftStraight:0.00} off {posed.LeftMiss * 100f:0.0}");
            // A hand the clip has on the gun must be on it; one it has taken away goes as far as this arm reaches.
            if (!posed.RightOnGun) rightShort = Mathf.Max(rightShort, posed.RightMiss);
            else
            {
                if (posed.RightMiss > most.RightMiss) { most.RightMiss = posed.RightMiss; rightMissAt = time; }
                most.RightTurn = Mathf.Max(most.RightTurn, posed.RightTurn);
            }
            if (!posed.LeftOnGun) leftShort = Mathf.Max(leftShort, posed.LeftMiss);
            else
            {
                if (posed.LeftMiss > most.LeftMiss) { most.LeftMiss = posed.LeftMiss; leftMissAt = time; }
                most.LeftTurn = Mathf.Max(most.LeftTurn, posed.LeftTurn);
            }
            if (posed.BroughtIn > broughtIn)
            {
                broughtIn = posed.BroughtIn;
                broughtInAt = time;
            }
            if (posed.Ahead > ahead)
            {
                ahead = posed.Ahead;
                aheadAt = time;
            }
            if (posed.RightShoulderMoved.sqrMagnitude > rightShoulderMost.sqrMagnitude) rightShoulderMost = posed.RightShoulderMoved;
            if (posed.LeftShoulderMoved.sqrMagnitude > leftShoulderMost.sqrMagnitude) leftShoulderMost = posed.LeftShoulderMoved;
            if (posed.EyesMoved.sqrMagnitude > eyesMost.sqrMagnitude) eyesMost = posed.EyesMoved;
            most.RightStraight = Mathf.Max(most.RightStraight, posed.RightStraight);
            most.LeftStraight = Mathf.Max(most.LeftStraight, posed.LeftStraight);
            rightLeast = Mathf.Min(rightLeast, posed.RightStraight);
            leftLeast = Mathf.Min(leftLeast, posed.LeftStraight);
        }
        if (samples == 0)
        {
            text.AppendLine($"{gun} {kind}: could not be stood.");
            return;
        }
        bool missed = most.RightMiss > PalmMissed || most.LeftMiss > PalmMissed;
        text.Append($"{(missed ? "! " : "")}{gun} {kind} ({holdName}");
        if (!still) text.Append($", {track.length:0.00} s, {samples} moments");
        text.Append($"): palms off their grip at most R {most.RightMiss * 100f:0.0} cm {most.RightTurn:0}°");
        if (!still) text.Append($" at {rightMissAt:0.00}");
        text.Append($", L {most.LeftMiss * 100f:0.0} cm {most.LeftTurn:0}°");
        if (!still) text.Append($" at {leftMissAt:0.00}");
        if (rightShort > 0f || leftShort > 0f) text.Append($"; away from the gun, short by at most R {rightShort * 100f:0.0} L {leftShort * 100f:0.0} cm");
        if (broughtIn > .0005f)
        {
            text.Append($"; gun brought in at most {broughtIn * 100f:0.0} cm" + (still ? "" : $" at {broughtInAt:0.00}"));
            if (!still)
                text.Append($", ahead of a hold at most {ahead * 100f:0.0} cm at {aheadAt:0.00}, moved by the rule at most {ruleFastest * 100f:0} cm/s at "
                    + $"{ruleFastestAt:0.00} (by the clip at most {clipFastest * 100f:0} cm/s)");
        }
        text.Append(still ? $"; arms straight R {most.RightStraight:0.00} L {most.LeftStraight:0.00}"
            : $"; arms straight R {rightLeast:0.00}-{most.RightStraight:0.00} L {leftLeast:0.00}-{most.LeftStraight:0.00}");
        if (!still)
            text.Append($"; the body's own clip moves the shoulder joints at most R {Cm(rightShoulderMost)} L {Cm(leftShoulderMost)} and the eyes "
                + $"{Cm(eyesMost)} cm from the hold's pose");
        // Beside the tracks' report, which has the same of KINEMATION's arms: where a body's shoulders are from the gun it holds.
        if (kind == GunTrackKind.Hold)
            text.Append($"; shoulder joints from the gun in the gun's own frame: right {Cm(one.FromGun(one.Body.Right.Shoulder))} left "
                + $"{Cm(one.FromGun(one.Body.Left.Shoulder))} cm, arms {one.Body.Right.Length * one.Body.Scale * 100f:0.0} and "
                + $"{one.Body.Left.Length * one.Body.Scale * 100f:0.0} cm long");

        if (stage != null)
        {
            var times = new List<float>();
            if (still) times.Add(0f);
            else if (ask.Times != null && ask.Times.Count > 0)
                foreach (float time in ask.Times) times.Add(Mathf.Min(time, track.length));
            else
                for (int i = 0; i <= 4; i++) times.Add(track.length * i / 4f);
            string file = $"{Folder}/{one.Id}.{gun}.{kind}.png";
            if (Photograph(ask, stage, one, kind, track, bodyClip, times, file))
            {
                sheets++;
                text.Append($"; pictures {file} (columns:");
                foreach (float time in times) text.Append($" {time:0.00}");
                text.Append($"; rows:{(ask.Eyes ? " eyes" : "")}{(ask.Side ? " side" : "")}{(ask.Front ? " front" : "")})");
            }
        }
        text.AppendLine();
        if (trace != null)
        {
            text.AppendLine("   (each moment: the gun where the clip has it, cm right, up, ahead; a palm's gap from the gun's surface in cm, how much "
                + "it has hold, how straight the clip's place asks that arm and how straight it is, how far the palm ends off its place in cm)");
            text.Append(trace);
        }
    }

    private static string Cm(Vector3 metres) => $"({metres.x * 100f:0.0}, {metres.y * 100f:0.0}, {metres.z * 100f:0.0})";

    // The moments asked for, each from every view asked for, laid in one sheet: a column to a moment, a row to a view.
    private static bool Photograph(Ask ask, Stage stage, Standing one, GunTrackKind kind, GunTrack track, AnimationClip bodyClip, List<float> times, string file)
    {
        int width = ask.Width, height = Mathf.RoundToInt(width / ScreenAspect);
        var rows = new List<List<Texture2D>>();
        try
        {
            if (ask.Eyes) rows.Add(new List<Texture2D>());
            if (ask.Side) rows.Add(new List<Texture2D>());
            if (ask.Front) rows.Add(new List<Texture2D>());
            foreach (float time in times)
            {
                if (!one.Pose(kind, track, bodyClip, time, out Posed posed)) return false;
                Vector3 eyes = one.Eyes, up = one.Root.up, ahead = one.Root.forward, side = one.Root.right;
                int row = 0;
                if (ask.Eyes)
                {
                    // From the eyes, looking where the body looks, turned as the action's clip turns the camera (FPSBodyCamera).
                    float field = Camera.HorizontalToVerticalFieldOfView(ScreenFieldAcross, ScreenAspect);
                    foreach (var part in one.HeadParts) part.forceRenderingOff = true;
                    try
                    {
                        rows[row++].Add(stage.Shoot(eyes, Quaternion.LookRotation(ahead, up) * posed.CameraTurn, field, one.Near, width, height));
                    }
                    finally
                    {
                        foreach (var part in one.HeadParts) part.forceRenderingOff = false;
                    }
                }
                if (ask.Side)
                {
                    // From the right side, level with the shoulders: where a stock sits on the shoulder shows.
                    Vector3 target = eyes - up * .22f + ahead * .18f;
                    Vector3 spot = target + side * 1.25f;
                    rows[row++].Add(stage.Shoot(spot, Quaternion.LookRotation(target - spot, up), 38f, .05f, width, height));
                }
                if (ask.Front)
                {
                    Vector3 target = eyes - up * .25f + ahead * .1f;
                    Vector3 spot = target + ahead * 1.2f + side * .75f + up * .1f;
                    rows[row].Add(stage.Shoot(spot, Quaternion.LookRotation(target - spot, up), 38f, .05f, width, height));
                }
            }
            if (rows.Count == 0 || rows[0].Count == 0) return false;
            var sheet = new Texture2D(width * rows[0].Count, height * rows.Count, TextureFormat.RGB24, false);
            try
            {
                for (int r = 0; r < rows.Count; r++)
                    for (int c = 0; c < rows[r].Count; c++)
                        // A texture's rows run from the bottom: the first view goes on top.
                        sheet.SetPixels32(c * width, (rows.Count - 1 - r) * height, width, height, rows[r][c].GetPixels32());
                sheet.Apply();
                File.WriteAllBytes(file, sheet.EncodeToPNG());
            }
            finally
            {
                Object.DestroyImmediate(sheet);
            }
            return true;
        }
        finally
        {
            foreach (var row in rows)
                foreach (var picture in row)
                    if (picture != null) Object.DestroyImmediate(picture);
        }
    }

    // ---- Where the pictures are taken ----

    /// <summary>
    /// A scene of its own where nothing else is (the editor's preview scene, as the wardrobe's picture bake stands its
    /// pieces), with two lights and a lens. What is stood in it is lit the same whatever scene the editor has open.
    /// </summary>
    private sealed class Stage : IDisposable
    {
        private readonly Scene scene;
        private readonly GameObject root;
        private readonly Camera lens;
        private readonly bool wasAsync;
        private readonly MaterialPropertyBlock block = new MaterialPropertyBlock();
        private RenderTexture film;

        public Stage()
        {
            // Shaders compile as they are needed, so no picture comes out in the placeholder's flat cyan.
            wasAsync = ShaderUtil.allowAsyncCompilation;
            ShaderUtil.allowAsyncCompilation = false;
            scene = EditorSceneManager.NewPreviewScene();
            root = new GameObject("Gun Bench") { hideFlags = HideFlags.HideAndDontSave };
            SceneManager.MoveGameObjectToScene(root, scene);
            var lensObject = new GameObject("Lens") { hideFlags = HideFlags.HideAndDontSave };
            lensObject.transform.SetParent(root.transform, false);
            lens = lensObject.AddComponent<Camera>();
            lens.enabled = false;
            lens.clearFlags = CameraClearFlags.SolidColor;
            lens.backgroundColor = Backdrop;
            lens.allowHDR = false;
            lens.allowMSAA = true;
            lens.farClipPlane = 40f;
            lens.useOcclusionCulling = false;
            lens.scene = scene;
            var data = lens.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = false;
            data.renderShadows = false;
            data.antialiasing = AntialiasingMode.None;
            // One light over the shoulder for what the eyes see of the gun and the hands, one from the front for the views
            // from outside. The body faces +Z.
            AddLight("Over the shoulder", Quaternion.Euler(38f, 18f, 0f), 1.1f);
            AddLight("Front", Quaternion.Euler(22f, 205f, 0f), .9f);
        }

        private void AddLight(string name, Quaternion turn, float intensity)
        {
            var holder = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
            holder.transform.SetParent(root.transform, false);
            holder.transform.rotation = turn;
            var light = holder.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = Color.white;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
        }

        /// <summary>Stands a model in the scene: lit by the stage alone, drawn wherever its bones take it.</summary>
        public void Take(GameObject thing)
        {
            SceneManager.MoveGameObjectToScene(thing, scene);
            var flat = new SphericalHarmonicsL2();
            flat.AddAmbientLight(Ambient);
            var probes = new[] { flat };
            foreach (var renderer in thing.GetComponentsInChildren<Renderer>(true))
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
                if (renderer is SkinnedMeshRenderer skinned)
                {
                    skinned.updateWhenOffscreen = true;
                    // Skinned again at every picture: outside play nothing else re-skins a mesh between two renders, and the
                    // first sheets showed the gun moving through a draw past a body that stood in one pose (2026-11-15).
                    skinned.forceMatrixRecalculationPerRender = true;
                }
                renderer.lightProbeUsage = LightProbeUsage.CustomProvided;
                renderer.GetPropertyBlock(block);
                block.CopySHCoefficientArraysFrom(probes);
                renderer.SetPropertyBlock(block);
            }
        }

        /// <summary>A picture from a place, now (Camera.Render). The caller destroys it.</summary>
        public Texture2D Shoot(Vector3 spot, Quaternion turn, float fieldOfView, float near, int width, int height)
        {
            if (film == null || film.width != width || film.height != height)
            {
                if (film != null)
                {
                    film.Release();
                    Object.DestroyImmediate(film);
                }
                film = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32) { name = "Gun Bench Film", antiAliasing = 4, hideFlags = HideFlags.HideAndDontSave };
                film.Create();
            }
            lens.transform.SetPositionAndRotation(spot, turn);
            lens.fieldOfView = fieldOfView;
            lens.nearClipPlane = Mathf.Max(.01f, near);
            lens.aspect = width / (float)height;
            var previous = RenderTexture.active;
            var picture = new Texture2D(width, height, TextureFormat.RGB24, false);
            try
            {
                lens.targetTexture = film;
                lens.Render();
                RenderTexture.active = film;
                picture.ReadPixels(new Rect(0f, 0f, width, height), 0, 0);
                picture.Apply();
            }
            finally
            {
                RenderTexture.active = previous;
                lens.targetTexture = null;
            }
            return picture;
        }

        public void Dispose()
        {
            if (film != null)
            {
                film.Release();
                Object.DestroyImmediate(film);
            }
            if (root != null) Object.DestroyImmediate(root);
            EditorSceneManager.ClosePreviewScene(scene);
            ShaderUtil.allowAsyncCompilation = wasAsync;
        }
    }
}
#endif
