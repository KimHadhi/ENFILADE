#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// The editor's bench for the Détente's healing arm, without playing: every character is stood at the size the game
/// draws it, and its left arm is put through the dose into itself, the dose into a friend and the bandage with the very
/// functions the game poses a player's arm with each frame (InjectArm.SpotOn, FrameFor, Stand, SeatOf, on the game's own
/// tables). What comes of it is measured and written down, and photographed when asked, so a needle that points away
/// from the body, a thigh an arm cannot reach or a path that asks for more arm than there is shows as a line in a report
/// and a picture before anyone plays it (the user, 2026-11-18, heading the whole list: "문제를 근본적으로 생각하고 뿌리를
/// 고치며"; every break of a hand's place so far was found by them, playing).
/// What it measures:
/// - the tables: each motion's row and path are read, its strike lies inside it, and the path never asks the wrist
///   further from the shoulder than the arm is long;
/// - a dose into itself, on each character: where the needle's spot on the thigh is, how far the path alone leaves the
///   tip from it, how far it is once the arm has homed on it and how much of the arm's reach that takes, and how far
///   the needle is from pointing straight in (past 60 degrees it lies along the thigh and only touches it);
/// - a dose into a friend (the same character, stood facing it with the spot on their arm straight ahead of this
///   shoulder) at three distances: within the arm's reach the tip must end on the friend's arm; further off it is
///   written how far short the arm stops;
/// - the bandage, mid-loop: where the roll is from the friend's arm.
/// What it cannot: the body stands as its file has it (arms out, no clip), so the shoulder is where an upright body has
/// it, not where the armed idle leans it; the body's surface is not read (PokeBody bakes while playing), so a spot is a
/// sixth of its bone's length out, as the game falls back to; the fingers close by the path's curl, not joint by joint;
/// and nothing of the hand leaving the gun, the timing or the network is here. Those are the play record's
/// (Logs/inject.txt) and the user's.
/// ENFILADE → 캐릭터 → 주사 검사대 runs every character with pictures. A tool outside Unity asks through
/// Temp/enfilade-injectbench.request, whose lines say what to stand (all optional):
///   skins=mayo,ichigo        characters by id (all)
///   pictures=no              numbers only (pictures are taken unless told not to)
///   width=320                one picture's width and height in pixels
/// Logs/InjectBench/report.txt has the numbers; each character's pictures are one sheet beside it, a row to a motion
/// (itself, a friend in reach, the bandage) and a column to a view (front, the body's left side, from above).
/// </summary>
[InitializeOnLoad]
internal static class InjectBench
{
    private const string Request = "Temp/enfilade-injectbench.request", Folder = "Logs/InjectBench", ReportPath = Folder + "/report.txt";
    // The bench's own marks; they decide nothing in the game.
    // A tip further than this from its spot though the arm had reach to spare (m).
    private const float TipMissed = .005f;
    // An arm is at the end of its reach from here (arm lengths): the solver never straightens it wholly.
    private const float ReachSpent = .995f;
    // The needle goes in point first while it is within this of straight into the body (degrees): past it the syringe
    // lies along the body and its tip only touches (the first draft of the dose into itself stood at 81).
    private const float NeedleAskew = 60f;
    // How far ahead of the Détente's shoulder the spot on the friend's arm is for the doses tried, in the Détente's arm
    // lengths: well inside its reach, a step off, across a room.
    private static readonly float[] FriendAt = { .9f, 1.7f, 4f };

    private static double next;

    static InjectBench() => EditorApplication.update += Tick;

    private static void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        if (now < next) return;
        next = now + .5;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (!File.Exists(Request)) return;
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

    [MenuItem("ENFILADE/캐릭터/주사 검사대 (모든 캐릭터)")]
    private static void RunAll() => Run(new Ask());

    private sealed class Ask
    {
        public HashSet<string> Skins;
        public bool Pictures = true;
        public int Width = 320;

        public static Ask Read(string[] lines)
        {
            var ask = new Ask();
            foreach (string raw in lines)
            {
                int cut = raw.IndexOf('=');
                if (cut <= 0) continue;
                string key = raw.Substring(0, cut).Trim().ToLowerInvariant();
                var values = raw.Substring(cut + 1).Split(new[] { ',', ' ', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (key == "skins") ask.Skins = new HashSet<string>(values, StringComparer.OrdinalIgnoreCase);
                else if (key == "pictures") ask.Pictures = values.Length == 0 || values[0].ToLowerInvariant() != "no";
                else if (key == "width" && values.Length > 0 && int.TryParse(values[0], out int width)) ask.Width = Mathf.Clamp(width, 96, 1024);
            }
            return ask;
        }
    }

    // What the bench found: a line per thing looked at, the failures counted.
    private sealed class Report
    {
        public readonly StringBuilder Text = new StringBuilder();
        public int Failed, Looked;

        public void Line(string line) => Text.AppendLine(line);

        public void Expect(bool holds, string what, string numbers)
        {
            Looked++;
            if (holds) return;
            Failed++;
            Text.AppendLine("  FAIL " + what + ": " + numbers);
        }
    }

    // The standing camera height the game sizes every body to (Player.prefab's CrouchPivot, as the gun bench takes it).
    private static float CameraHeight()
    {
        var player = Resources.Load<GameObject>("Characters/Player");
        var pivot = player != null ? player.transform.Find("CrouchPivot") : null;
        return pivot != null ? pivot.localPosition.y : 1.6f;
    }

    private static void Run(Ask ask)
    {
        var report = new Report();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        int stood = 0, sheets = 0;
        WardrobeBench.Stage stage = null;
        try
        {
            report.Line("== the motions (Tables/InjectArms and each one's path)");
            Tables(report);
            var profile = PlayerBodyProfile.Load();
            if (profile == null) report.Expect(false, "the body profile", "Resources/Characters/PlayerBody is missing; nobody to stand");
            else
            {
                if (ask.Pictures)
                {
                    stage = new WardrobeBench.Stage();
                    Directory.CreateDirectory(Folder);
                }
                float camera = CameraHeight();
                int done = 0;
                foreach (var skin in profile.Skins)
                {
                    done++;
                    if (skin == null || (ask.Skins != null && !ask.Skins.Contains(skin.Id))) continue;
                    var model = profile.ModelOf(skin);
                    if (model == null) continue;
                    EditorUtility.DisplayProgressBar("ENFILADE: 주사 검사대", skin.Id, done / (float)Mathf.Max(1, profile.Skins.Count));
                    report.Line("");
                    if (Stand(ask, report, stage, skin, model, camera, ref sheets)) stood++;
                }
            }
        }
        catch (Exception problem)
        {
            report.Failed++;
            report.Line("  FAIL the bench stopped: " + problem);
            Debug.LogException(problem);
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            stage?.Dispose();
        }
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(ReportPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} inject bench: {stood} characters stood, {sheets} picture sheets, {report.Looked} things checked: "
                + (report.Failed == 0 ? "ALL KEPT" : report.Failed + " BROKEN") + $"  ({watch.Elapsed.TotalSeconds:0.0} s)\n"
                + "Bodies stand as their files have them (no clip), at the size the game draws them; a spot on a body is a sixth of its bone's\n"
                + "length out from the bone (the game reads the real surface while playing). Lengths in the game's metres.\n\n" + report.Text, new UTF8Encoding(false));
        }
        catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
        {
            Debug.LogWarning("InjectBench: " + ReportPath + " could not be written: " + error.Message);
        }
        if (report.Failed > 0) Debug.LogWarning($"InjectBench: {report.Failed} checks broken, see {ReportPath}.");
        else Debug.Log($"InjectBench: {stood} characters stood, all kept; {ReportPath}.");
    }

    // ---- The tables ----

    private static readonly InjectArm.Kind[] Kinds = { InjectArm.Kind.Self, InjectArm.Kind.Friend, InjectArm.Kind.Bandage };

    private static void Tables(Report report)
    {
        foreach (var kind in Kinds)
        {
            string id = InjectArm.RowId(kind);
            var move = InjectArm.Move.Of(kind);
            report.Expect(move != null, id, $"its row of Tables/{InjectArm.Table} or its path table could not be read");
            if (move == null) continue;
            // Along the whole path: how far it asks the wrist from the shoulder (arm lengths), and whether the hand's two ways are ways.
            float far = 0f, farAt = 0f, skew = 0f;
            int steps = Mathf.Max(2, Mathf.CeilToInt(move.Length * 60f));
            for (int i = 0; i <= steps; i++)
            {
                float t = move.Length * i / steps;
                if (!GrenadeArmPath.Sample(move.Table, t, out var key)) continue;
                if (key.Wrist.magnitude > far)
                {
                    far = key.Wrist.magnitude;
                    farAt = t;
                }
                skew = Mathf.Max(skew, Mathf.Abs(Vector3.Dot(key.Along.normalized, key.Palm.normalized)));
            }
            report.Line($"{id}: path {move.Table}, {move.Length:0.00} s, "
                + (move.Strike >= 0f ? $"strike at {move.Strike:0.00} s, plunger home {move.PlungeSeconds:0.00} s later" : "no strike (a loop)")
                + $", {(move.Aims ? "aimed from the shoulder at the spot" : "in the body's own axes")}; the wrist at most {far * 100f:0}% of the arm from the shoulder (at {farAt:0.00} s)");
            report.Expect(far <= 1f, id, $"its path asks the wrist {far * 100f:0}% of the arm's length from the shoulder at {farAt:0.00} s: no arm reaches that");
            report.Expect(skew < .3f, id, $"the hand's run and its palm's facing are not square along the path (their cosine reaches {skew:0.00})");
            if (move.HasTip)
            {
                report.Expect(move.Strike >= 0f, id, "it has a needle and no strike row: the tip is never homed on a body");
                report.Expect(move.Strike + move.PlungeSeconds <= move.Length + 1e-3f, id,
                    $"the plunger is home at {move.Strike + move.PlungeSeconds:0.00} s, after the path's end ({move.Length:0.00} s)");
            }
        }
    }

    // ---- One character ----

    private static bool Stand(Ask ask, Report report, WardrobeBench.Stage stage, PlayerBodyProfile.Skin skin, GameObject model, float camera, ref int sheets)
    {
        GameObject body = null, friend = null, syringe = null, roll = null;
        var tiles = new List<Texture2D[]>();
        try
        {
            body = Fresh(model, skin, camera, out var animator);
            if (animator == null)
            {
                report.Line($"== {skin.Id}: no humanoid avatar, not stood.");
                return false;
            }
            var arm = new ArmBones();
            if (!arm.Bind(animator) || !arm.HasLength)
            {
                report.Line($"== {skin.Id}: no left arm to pose, not stood.");
                return false;
            }
            // The way the body faces, by its own skeleton: some files have the model standing the other way round.
            Quaternion facing = FaceFit.FrameOf(animator, Matrix4x4.identity);
            Vector3 right = facing * Vector3.right, ahead = facing * Vector3.forward;
            Vector3 shoulder = arm.Upper.position;
            report.Line($"== {skin.Id}: drawn x{body.transform.localScale.x:0.00}, arm {arm.ArmLength:0.000} m, hand {arm.HandLength:0.000} m");
            if (stage != null)
            {
                stage.Take(body);
                syringe = Prop("Syringe", true);
                roll = Prop("Bandage", false);
                stage.Take(syringe);
                stage.Take(roll);
            }

            // A dose into itself.
            var self = InjectArm.Move.Of(InjectArm.Kind.Self);
            if (self != null && GrenadeArmPath.Sample(self.Table, Mathf.Max(0f, self.Strike), out var key))
            {
                string what = skin.Id + " · self";
                bool found = InjectArm.SpotOn(self, animator, shoulder, InjectArm.OwnSide(right, ahead), null, out Vector3 spot, out Vector3 inward);
                report.Expect(found, what, $"the body has no {self.Bone} to put a needle in");
                if (found)
                {
                    Quaternion frame = InjectArm.FrameFor(self, facing, shoulder, true, spot);
                    Vector3 want = spot + inward * self.Depth;
                    InjectArm.Stand(arm, self, key, frame, 0f, want, out _, out _);
                    float alone = TipFrom(arm, self, want, out _);
                    InjectArm.Stand(arm, self, key, frame, 1f, want, out float left, out float reach);
                    float askew = Askew(arm, self, inward);
                    Vector3 off = Quaternion.Inverse(facing) * (spot - shoulder);
                    report.Line($"{what}: the spot is {Vector3.Distance(spot, shoulder):0.000} m from the shoulder (right {off.x:+0.00;-0.00}, up {off.y:+0.00;-0.00}, ahead {off.z:+0.00;-0.00}); "
                        + $"the path alone leaves the tip {alone * 100f:0.0} cm off it; "
                        + $"homed, {Mathf.Max(0f, left) * 100f:0.0} cm off with the wrist at {reach * 100f:0}% of the arm; the needle {askew:0}° from straight in");
                    report.Expect(left >= 0f && (left <= TipMissed || reach >= ReachSpent), what, $"the tip ends {left * 100f:0.0} cm off its spot though the arm had reach to spare ({reach * 100f:0}%)");
                    report.Expect(reach < ReachSpent, what, $"the arm cannot reach its own thigh: {left * 100f:0.0} cm short at full stretch");
                    report.Expect(askew < NeedleAskew, what, $"the needle is {askew:0}° from pointing into the thigh: it does not go in point first");
                    if (stage != null)
                    {
                        Close(arm, frame, key);
                        Hold(arm, self, syringe, roll, true);
                        tiles.Add(Views(stage, (shoulder + spot) * .5f, right, ahead, Mathf.Max(.42f, Vector3.Distance(shoulder, spot) * .85f), ask.Width));
                    }
                }
            }

            // A dose into a friend, and the bandage: the same character stood facing this one.
            friend = Fresh(model, skin, camera, out var theirs);
            if (theirs != null)
            {
                if (stage != null) stage.Take(friend);
                var dose = InjectArm.Move.Of(InjectArm.Kind.Friend);
                for (int i = 0; dose != null && i < FriendAt.Length; i++)
                {
                    float apart = FriendAt[i] * arm.ArmLength;
                    string what = $"{skin.Id} · friend's arm {apart:0.00} m ahead";
                    if (!GrenadeArmPath.Sample(dose.Table, Mathf.Max(0f, dose.Strike), out key)) break;
                    bool found = Face(friend.transform, theirs, dose, shoulder, ahead, apart, out Vector3 spot, out Vector3 inward);
                    report.Expect(found, what, "the friend has no upper arm to put a needle in");
                    if (!found) continue;
                    Quaternion frame = InjectArm.FrameFor(dose, facing, shoulder, true, spot);
                    Vector3 want = spot + inward * dose.Depth;
                    InjectArm.Stand(arm, dose, key, frame, 0f, want, out _, out _);
                    float alone = TipFrom(arm, dose, want, out _);
                    InjectArm.Stand(arm, dose, key, frame, 1f, want, out float left, out float reach);
                    float askew = Askew(arm, dose, inward);
                    bool spent = reach >= ReachSpent;
                    report.Line($"{what}: the spot is {Vector3.Distance(spot, shoulder):0.000} m from the shoulder; the path alone leaves the tip {alone * 100f:0.0} cm off it; "
                        + (spent ? $"homed, the arm is at full stretch and stops {Mathf.Max(0f, left) * 100f:0.0} cm short"
                            : $"homed, {Mathf.Max(0f, left) * 100f:0.0} cm off with the wrist at {reach * 100f:0}% of the arm")
                        + $"; the needle {askew:0}° from straight in");
                    report.Expect(left >= 0f && (left <= TipMissed || spent), what, $"the tip ends {left * 100f:0.0} cm off its spot though the arm had reach to spare ({reach * 100f:0}%)");
                    if (i == 0)
                    {
                        // The nearest friend is the one a dose is meant for: in reach, and the needle into the arm.
                        report.Expect(!spent, what, $"a friend's arm nine tenths of an arm's length ahead is out of the arm's reach ({left * 100f:0.0} cm short)");
                        report.Expect(askew < NeedleAskew, what, $"the needle is {askew:0}° from pointing into the friend's arm: it does not go in point first");
                        if (stage != null)
                        {
                            Close(arm, frame, key);
                            Hold(arm, dose, syringe, roll, true);
                            tiles.Add(Views(stage, (shoulder + spot) * .5f, right, ahead, Mathf.Max(.5f, apart * .9f), ask.Width));
                        }
                    }
                }

                var wind = InjectArm.Move.Of(InjectArm.Kind.Bandage);
                if (wind != null && GrenadeArmPath.Sample(wind.Table, wind.Length * .5f, out key))
                {
                    float apart = FriendAt[0] * arm.ArmLength;
                    string what = $"{skin.Id} · bandage, friend's arm {apart:0.00} m ahead";
                    bool found = Face(friend.transform, theirs, wind, shoulder, ahead, apart, out Vector3 spot, out _);
                    Quaternion frame = InjectArm.FrameFor(wind, facing, shoulder, found, spot);
                    InjectArm.Stand(arm, wind, key, frame, 0f, spot, out _, out _);
                    bool seated = InjectArm.SeatOf(arm, wind, out Vector3 at, out _);
                    report.Expect(seated, what, "the hand has no axes to seat the roll by");
                    report.Line($"{what}: mid-loop the wrist is at {Vector3.Distance(arm.Hand.position, shoulder) / arm.ArmLength * 100f:0}% of the arm"
                        + (found && seated ? $", the roll {Vector3.Distance(at, spot) * 100f:0} cm from the friend's arm" : ", no arm of the friend's was found to wind toward"));
                    if (stage != null)
                    {
                        Close(arm, frame, key);
                        Hold(arm, wind, syringe, roll, false);
                        tiles.Add(Views(stage, found ? (shoulder + spot) * .5f : shoulder + ahead * .3f, right, ahead, Mathf.Max(.5f, apart * .9f), ask.Width));
                    }
                }
            }
            if (tiles.Count > 0)
            {
                string sheet = Folder + "/" + skin.Id + ".png";
                WardrobeBench.SaveSheet(sheet, tiles, ask.Width);
                sheets++;
                report.Line($"   pictures: {sheet}, columns front · the body's left side · from above, rows from the top: itself | a friend in reach | the bandage");
            }
            return true;
        }
        catch (Exception problem)
        {
            report.Expect(false, skin.Id, "standing it stopped: " + problem.Message);
            Debug.LogException(problem);
            return false;
        }
        finally
        {
            foreach (var row in tiles)
                foreach (var tile in row)
                    if (tile != null) Object.DestroyImmediate(tile);
            if (syringe != null) Object.DestroyImmediate(syringe);
            if (roll != null) Object.DestroyImmediate(roll);
            if (friend != null) Object.DestroyImmediate(friend);
            if (body != null) Object.DestroyImmediate(body);
        }
    }

    // A character as it comes, at the size the game draws it, standing at the origin; its Animator null without a humanoid avatar.
    private static GameObject Fresh(GameObject model, PlayerBodyProfile.Skin skin, float camera, out Animator animator)
    {
        var instance = Object.Instantiate(model);
        instance.hideFlags = HideFlags.HideAndDontSave;
        instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        instance.transform.localScale = Vector3.one;
        animator = instance.GetComponentInChildren<Animator>();
        if (animator == null || !animator.isHuman || animator.GetBoneTransform(HumanBodyBones.Head) == null)
        {
            animator = null;
            return instance;
        }
        // As a player's body plays (PlayerBody.Build).
        animator.applyRootMotion = false;
        instance.transform.localScale = Vector3.one * PlayerBody.DrawnScaleOf(animator, instance.transform, camera);
        // What its file brought that a body may not keep goes, as the game takes it off (an empty look puts nothing on).
        OutfitDresser.Dress(instance, model, skin, new OutfitLook());
        return instance;
    }

    // The friend stood facing this body with the spot on their arm straight ahead of this shoulder, that far off (level):
    // how wide a friend is then makes no difference to what is in reach. False when they have no arm for the needle.
    private static bool Face(Transform friend, Animator theirs, InjectArm.Move move, Vector3 shoulder, Vector3 ahead, float apart,
        out Vector3 spot, out Vector3 inward)
    {
        friend.SetPositionAndRotation(ahead * apart, Quaternion.Euler(0f, 180f, 0f));
        if (!InjectArm.SpotOn(move, theirs, shoulder, null, null, out spot, out inward)) return false;
        Vector3 shift = shoulder + ahead * apart - spot;
        shift.y = 0f;
        friend.position += shift;
        return InjectArm.SpotOn(move, theirs, shoulder, null, null, out spot, out inward);
    }

    // How far the thing's tip is from where it is wanted, as the hand is now; the thing's own turn with it.
    private static float TipFrom(ArmBones arm, InjectArm.Move move, Vector3 want, out Quaternion turn)
    {
        turn = Quaternion.identity;
        return InjectArm.SeatOf(arm, move, out Vector3 at, out turn) ? (want - (at + turn * (Vector3.up * move.Tip))).magnitude : -1f;
    }

    // How far the needle is from pointing straight into the body (degrees): it runs from the thing's grip to its tip.
    private static float Askew(ArmBones arm, InjectArm.Move move, Vector3 inward)
    {
        if (!InjectArm.SeatOf(arm, move, out _, out Quaternion turn)) return 180f;
        Vector3 needle = turn * (Vector3.up * Mathf.Sign(move.Tip));
        return Vector3.Angle(needle, inward);
    }

    // The fingers closed round the thing by the path's curl (the game wears every finger joint from Blender on top).
    private static void Close(ArmBones arm, Quaternion frame, in GrenadeArmPath.Key key)
        => arm.Close(frame * key.Along, frame * key.Palm, key.Curl, key.Thumb);

    // The syringe or the roll, made of the game's own parts (DetenteLooks), for the pictures.
    private static GameObject Prop(string name, bool needle)
    {
        var prop = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
        if (needle) DetenteLooks.MakeSyringe(prop.transform, out _, out _);
        else DetenteLooks.MakeBandage(prop.transform);
        return prop;
    }

    // The thing of this motion in the fist, as the game seats it (the other one put out of sight).
    private static void Hold(ArmBones arm, InjectArm.Move move, GameObject syringe, GameObject roll, bool needle)
    {
        if (syringe == null || roll == null) return;
        syringe.SetActive(needle);
        roll.SetActive(!needle);
        if (InjectArm.SeatOf(arm, move, out Vector3 at, out Quaternion turn)) (needle ? syringe : roll).transform.SetPositionAndRotation(at, turn);
    }

    // Three pictures round a point: from the front, from the body's left (the arm's side) and from above, the body's front
    // at the picture's top.
    private static Texture2D[] Views(WardrobeBench.Stage stage, Vector3 middle, Vector3 right, Vector3 ahead, float half, int size)
    {
        const float away = 4f;
        return new[]
        {
            stage.Look(middle + ahead * away, middle, half, size),
            stage.Look(middle - right * away, middle, half, size),
            stage.Look(middle + Vector3.up * away, middle, half, size, ahead),
        };
    }
}
#endif
