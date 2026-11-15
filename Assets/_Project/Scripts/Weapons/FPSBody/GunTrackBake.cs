#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using KINEMATION.CharacterAnimationSystem.Integrations.FPSAnimationPack.Scripts;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Reads every gun's actions from KINEMATION's own clips into numbers, in the editor: the hold, the draw, the holster, the
/// look over the gun, every reload and the bolt or pump worked after a shot, each played frame by frame on a hidden copy of
/// KINEMATION's arms (GunArms) and kept as how the clip moves the gun from its hold, where both palms are on it, how the
/// fingers bend and how the camera turns (GunTrackSet, one asset to a gun, listed in GunTracks).
/// The clips remade for our bodies (GunClipRetarget) keep joint angles only: the gun's path was dropped there, and the game
/// made up for it by sampling KINEMATION's clips on hidden arms every frame, beside the animation, with a clock and weights
/// of its own, and by holding the gun at a place read back from the scene. That second owner of the gun's place is what
/// stood a drawn gun still and then slid it home (the user, 2026-11-15: "샷건이랑 smg 들 때 처음에 멈췄다가 원자리로 돌아옴"),
/// and sampling at run time twice went wrong in builds only (ClipSampler's notes). With the action kept as an asset the
/// game only reads it (Docs/Features/GunHandling.md; the game takes it up in that design's third stage).
/// It needs no Retarget Pro and takes seconds. It runs by itself after scripts load for a gun whose clips, arms or model
/// changed or that has no tracks yet; ENFILADE → 총 → 총 동작 굽기 reads them all again, and so does making the file
/// Temp/enfilade-guntracks.request (a tool outside Unity asking). Logs/gun-tracks.txt then says, for every gun and action,
/// what was read: how far off its hold the clip has the gun over time, whether its end is the hold, when a palm is away from
/// its place, whether the camera moves.
/// It lives beside FPSBody because it reads with the game's own classes (GunArms).
/// </summary>
[InitializeOnLoad]
internal static class GunTrackBake
{
    private const string AssetsPath = ProjectFolders.DataByName + FPSBodyAssets.ResourcePath + ".asset";
    private const string ListPath = ProjectFolders.DataByName + GunTracks.ResourcePath + ".asset";
    private const string SetsFolder = "GunTracks";
    private const string Request = "Temp/enfilade-guntracks.request", ReportPath = "Logs/gun-tracks.txt";
    // Raised when what a track holds or how it is read changes, so every gun is read again. 2: each palm's gap from the
    // gun's surface. 3: that gap kept as far as a hand takes hold of the gun from (GunTrack.GapMost .12 to .3). 4: moments
    // between a slow clip's frames (RateLeast).
    private const int Version = 4;
    // A track has at least this many moments a second: a clip with fewer frames is read between them too, evenly, so its own
    // frames are all there and the game, which reads a track straight from one moment to the next, stays near the clip's
    // curves. Read at its 30 frames a second, a clip's palm was up to a centimetre from the track midway between two frames
    // (median of each clip's worst 5 mm, of 58 clips; the 60-frame clips' 1.5 mm: this report, 2026-11-15).
    private const float RateLeast = 60f;
    // KINEMATION's camera bone in its gun clips; the game plays them on the same two bones (FPSBodyCamera).
    private const string CameraRootName = "root", CameraBoneName = "camera_bone";
    // The report's own bands: how near its hold a gun is said to stay (m, degrees), and how far a palm is from its place in
    // the hold to be said away (m). They decide nothing in the game.
    private static readonly (float move, float turn)[] Bands = { (.005f, 1f), (.01f, 2f), (.02f, 5f) };
    // A palm further than this from the gun's surface is off the gun (m): the hold's own rule (GunHold.Rules' HoldsWithin,
    // Tables/FPSBody.csv), read as a report is written.
    private static float handAway = .04f;
    // The report counts the palms' gaps from the gun's surface between these (cm).
    private static readonly int[] GapEdges = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 10, 12, 16, 20, 24, 30 };
    // A draw's and a holster's table of the gun's way home is printed every this many seconds.
    private const float TableStep = .05f;

    // How KINEMATION's own arms did each track read in this run, for the report: nothing of it is kept in a track.
    private sealed class SourceArm
    {
        // How straight the arm is (shoulder joint to wrist over its two bones' length then), least and most and when the
        // most; the length of its two bones, least and most; how far its shoulder joint gets from where the pose has it (m).
        public float StraightLeast = float.MaxValue, StraightMost, MostAt, LengthLeast = float.MaxValue, LengthMost, ShoulderMoved;
    }

    private static readonly Dictionary<GunTrack, SourceArm[]> sourceArms = new Dictionary<GunTrack, SourceArm[]>();
    // How far each clip read in this run is from its track midway between the track's frames, where the track reads straight
    // from one frame to the next: the gun's move (m) and turn (degrees), a palm on the gun (m). What the game reads against
    // what the clip itself is there.
    private static readonly Dictionary<GunTrack, (float move, float turn, float palm)> betweenFrames = new Dictionary<GunTrack, (float, float, float)>();
    // Where KINEMATION's arms have their shoulder joints from the gun in its hold, for each gun read in this run.
    private static readonly Dictionary<WeaponData, string> sourceShoulders = new Dictionary<WeaponData, string>();

    private static double next;
    // Wanted once after every script load, and whenever the request file is made: tried every half second until the editor
    // is free. Right after a script reload the editor is still taking assets in, and one try made there read nothing (the
    // first load of this class, 2026-11-15: no tracks until the request file asked).
    private static bool dueAtLoad = true;

    static GunTrackBake() => EditorApplication.update += Tick;

    private static void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        if (now < next) return;
        next = now + .5;
        bool asked = File.Exists(Request);
        if (!dueAtLoad && !asked) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        if (asked)
        {
            try
            {
                File.Delete(Request);
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                return;
            }
        }
        dueAtLoad = false;
        Run(asked);
    }

    [MenuItem("ENFILADE/총/총 동작 굽기 (전부)")]
    private static void RunAll() => Run(true);

    private static void Run(bool all)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        var assets = AssetDatabase.LoadAssetAtPath<FPSBodyAssets>(AssetsPath);
        if (assets == null || assets.guns == null) return;
        var list = AssetDatabase.LoadAssetAtPath<GunTracks>(ListPath);
        var entries = new List<GunTracks.Entry>(list != null ? list.entries : Array.Empty<GunTracks.Entry>());

        var due = new List<(int gun, string source)>();
        for (int i = 0; i < assets.guns.Length; i++)
        {
            var gun = assets.guns[i];
            if (gun.weapon == null || gun.prefab == null || gun.sourcePose == null) continue;
            string source = SourceOf(assets, gun);
            int at = IndexOf(entries, gun.weapon);
            // Current: read from these very clips, or tried from them and failed (not tried again until they change: a gun
            // that can't be read once can't be read at every script reload either).
            bool current = at >= 0 && (entries[at].failedSource == source || (entries[at].set != null && entries[at].source == source));
            if (all || !current) due.Add((i, source));
        }
        // A gun no longer listed leaves the list (its asset stays until someone removes it).
        int gone = entries.RemoveAll(entry => entry.weapon == null || !Lists(assets, entry.weapon));
        if (due.Count == 0 && gone == 0) return;

        if (list == null)
        {
            list = ScriptableObject.CreateInstance<GunTracks>();
            Directory.CreateDirectory(Path.GetDirectoryName(ListPath));
            AssetDatabase.CreateAsset(list, ListPath);
        }
        var armsByModel = new Dictionary<GameObject, GunArms>();
        var camera = new CameraRig();
        var notes = new List<string>();
        sourceArms.Clear();
        sourceShoulders.Clear();
        betweenFrames.Clear();
        var watch = System.Diagnostics.Stopwatch.StartNew();
        int read = 0;
        try
        {
            for (int n = 0; n < due.Count; n++)
            {
                var gun = assets.guns[due[n].gun];
                EditorUtility.DisplayProgressBar("ENFILADE: 총 동작", gun.weapon.name, n / (float)due.Count);
                GunTrackSet set = null;
                try
                {
                    set = Bake(assets, gun, armsByModel, camera, notes);
                }
                catch (Exception error)
                {
                    notes.Add($"{gun.weapon.name}: {error.Message}");
                    Debug.LogException(error, gun.weapon);
                }
                var entry = new GunTracks.Entry
                {
                    weapon = gun.weapon, set = set, source = set != null ? due[n].source : "", failedSource = set != null ? "" : due[n].source
                };
                int at = IndexOf(entries, gun.weapon);
                // A gun that can't be read now keeps the tracks it had.
                if (set == null && at >= 0)
                {
                    entry.set = entries[at].set;
                    entry.source = entries[at].source;
                }
                if (at >= 0) entries[at] = entry;
                else entries.Add(entry);
                if (set != null) read++;
            }
        }
        finally
        {
            EditorUtility.ClearProgressBar();
            foreach (var arms in armsByModel.Values) arms?.ReleaseNow();
            camera.Release();
        }
        list.entries = entries.ToArray();
        EditorUtility.SetDirty(list);
        AssetDatabase.SaveAssets();
        WriteReport(assets, list, notes, read, due.Count, watch.Elapsed.TotalSeconds);
        sourceArms.Clear();
        sourceShoulders.Clear();
        betweenFrames.Clear();
        Debug.Log($"GunTrackBake: read {read} of {due.Count} guns' actions from KINEMATION's clips in {watch.Elapsed.TotalSeconds:0.0} s; "
            + $"every gun's numbers are in {ReportPath}.");
    }

    private static int IndexOf(List<GunTracks.Entry> entries, WeaponData weapon)
    {
        for (int i = 0; i < entries.Count; i++)
            if (entries[i].weapon == weapon) return i;
        return -1;
    }

    private static bool Lists(FPSBodyAssets assets, WeaponData weapon)
    {
        foreach (var gun in assets.guns)
            if (gun.weapon == weapon) return true;
        return false;
    }

    // What a gun's tracks are read from: the arms its clips are made for, its model and the settings that turn its frame, and
    // every clip, each by its file and that file's content.
    private static string SourceOf(FPSBodyAssets assets, FPSBodyAssets.Gun gun)
    {
        var text = new StringBuilder();
        text.Append(Version);
        Add(text, gun.ArmsOr(assets.armsModel));
        Add(text, gun.prefab);
        var kinemation = gun.prefab != null ? gun.prefab.GetComponent<CasFPSWeapon>() : null;
        Add(text, kinemation != null ? kinemation.weaponSettings : null);
        foreach (var (_, clip) in ClipsOf(gun)) Add(text, clip);
        return text.ToString();
    }

    private static void Add(StringBuilder text, Object asset)
    {
        text.Append('|');
        if (asset == null)
        {
            text.Append('-');
            return;
        }
        string path = AssetDatabase.GetAssetPath(asset);
        // A clip inside a model file is told from the file's other clips by its name; the file's hash covers its content.
        text.Append(AssetDatabase.AssetPathToGUID(path)).Append(':').Append(AssetDatabase.GetAssetDependencyHash(path)).Append(':').Append(asset.name);
    }

    private static (GunTrackKind kind, AnimationClip clip)[] ClipsOf(FPSBodyAssets.Gun gun) => new[]
    {
        (GunTrackKind.Hold, gun.sourcePose), (GunTrackKind.HoldNoGrip, gun.sourcePoseNoGrip), (GunTrackKind.Draw, gun.sourceDraw),
        (GunTrackKind.Holster, gun.sourceHolster), (GunTrackKind.Inspect, gun.sourceInspect),
        (GunTrackKind.ReloadTactical, gun.sourceReloadTactical), (GunTrackKind.ReloadEmpty, gun.sourceReloadEmpty),
        (GunTrackKind.ReloadShell, gun.sourceReloadShell), (GunTrackKind.ReloadEnd, gun.sourceReloadEnd), (GunTrackKind.Cycle, gun.sourceFire)
    };

    // One gun's tracks into its asset (made, or written over so what refers to it keeps it). Null when its pose can't be
    // read on KINEMATION's arms; a single clip that can't be read is left out and noted.
    private static GunTrackSet Bake(FPSBodyAssets assets, FPSBodyAssets.Gun gun, Dictionary<GameObject, GunArms> armsByModel, CameraRig camera,
        List<string> notes)
    {
        var prefab = gun.ArmsOr(assets.armsModel);
        if (prefab == null)
        {
            notes.Add($"{gun.weapon.name}: no arms model its clips are made for.");
            return null;
        }
        if (!armsByModel.TryGetValue(prefab, out var arms)) armsByModel[prefab] = arms = GunArms.Make(prefab);
        if (arms == null)
        {
            notes.Add($"{gun.weapon.name}: {prefab.name} has no weapon bone or hands to read.");
            return null;
        }
        // The gun's frame from KINEMATION's weapon bone, as the game's gun takes it (FPSBody.AddGun).
        Quaternion frame = FPSBody.GunFrameOf(gun.prefab.GetComponent<CasFPSWeapon>());
        if (!arms.Sample(gun.sourcePose, 0f))
        {
            notes.Add($"{gun.weapon.name}: its pose clip could not be played on {prefab.name}.");
            return null;
        }
        arms.Gun(frame, out Vector3 poseAt, out Quaternion poseTurn);
        var poseShoulders = new Vector3[2];
        var shoulders = new StringBuilder();
        for (int side = 0; side < 2; side++)
        {
            if (!arms.Arm(side == 1, out poseShoulders[side], out _, out float armLength)) continue;
            Vector3 from = Quaternion.Inverse(poseTurn) * (poseShoulders[side] - poseAt) * 100f;
            shoulders.Append($" {(side == 0 ? "right" : "left")} ({from.x:0.0}, {from.y:0.0}, {from.z:0.0}) cm, that arm {armLength * 100f:0.0} cm long;");
        }
        if (shoulders.Length > 0) sourceShoulders[gun.weapon] = shoulders.ToString(0, shoulders.Length - 1);
        // A hold at all: the support palm within reach of the gun (FPSBody.PoseHolds' rule).
        bool holds = arms.Palm(true, poseAt, Quaternion.Inverse(poseTurn), out Vector3 support, out _, out _) && support.magnitude < FPSBody.PoseReach;
        Quaternion cameraRest = camera.Turn(gun.sourcePose, 0f);

        var tracks = new List<GunTrack>();
        // The gun itself at rest, as the game's gun starts (FPSGun.Set: its first sight and parts on), for how near its
        // surface each palm is: a copy stood at the origin, so the gun's own frame is the world's.
        var model = Object.Instantiate(gun.prefab);
        model.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            model.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            model.transform.localScale = Vector3.one;
            var sights = model.GetComponent<GunSights>();
            if (sights != null && sights.Current < 0) sights.Select(sights.initial);
            var parts = model.GetComponent<GunAttachments>();
            if (parts != null) parts.Begin();
            var surface = GunSurface.Of(model.transform);
            if (surface.Empty) notes.Add($"{gun.weapon.name}: none of its meshes could be read; every palm counts as away from it.");
            foreach (var (kind, clip) in ClipsOf(gun))
            {
                if (clip == null) continue;
                var track = Read(kind, clip, arms, frame, poseAt, poseTurn, camera, cameraRest, surface, poseShoulders);
                if (track != null) tracks.Add(track);
                else notes.Add($"{gun.weapon.name}: {kind} ({clip.name}) could not be read on {prefab.name}; left out.");
            }
        }
        finally
        {
            Object.DestroyImmediate(model);
        }
        if (tracks.Count == 0) return null;

        string parent = ProjectFolders.Folder(ProjectFolders.Guns), folder = parent + "/" + SetsFolder;
        if (!AssetDatabase.IsValidFolder(folder)) AssetDatabase.CreateFolder(parent, SetsFolder);
        string path = $"{folder}/{gun.weapon.name}.asset";
        var set = AssetDatabase.LoadAssetAtPath<GunTrackSet>(path);
        if (set == null)
        {
            set = ScriptableObject.CreateInstance<GunTrackSet>();
            AssetDatabase.CreateAsset(set, path);
        }
        set.weapon = gun.weapon;
        set.posePosition = poseAt;
        set.poseRotation = poseTurn;
        set.holds = holds;
        set.tracks = tracks.ToArray();
        EditorUtility.SetDirty(set);
        return set;
    }

    // One clip, moment by moment at its own frames and between them (RateLeast; the last at its end): the gun from where
    // the pose clip holds it, both hands on the gun as it then is, the finger ways and the camera bone's turn from the pose.
    // As the game read a moment of it on these arms until 0.9.117 (FPSBody.SampleContact), so the numbers mean the same.
    private static GunTrack Read(GunTrackKind kind, AnimationClip clip, GunArms arms, Quaternion frame, Vector3 poseAt, Quaternion poseTurn,
        CameraRig camera, Quaternion cameraRest, GunSurface surface, Vector3[] poseShoulders)
    {
        var own = new[] { new SourceArm(), new SourceArm() };
        float clipRate = clip.frameRate > 0f ? clip.frameRate : 30f;
        float rate = clipRate * Mathf.Max(1, Mathf.CeilToInt(RateLeast / clipRate - 1e-3f));
        float length = Mathf.Max(0f, clip.length);
        int frames = Mathf.Max(1, Mathf.CeilToInt(length * rate - 1e-3f) + 1);
        const int digits = GunArms.DigitCount;
        var track = new GunTrack
        {
            kind = kind, clip = clip.name, length = length, frameRate = rate,
            gunMove = new Vector3[frames], gunTurn = new Quaternion[frames],
            right = new GunTrackHand[frames], left = new GunTrackHand[frames], camera = new Quaternion[frames]
        };
        var rightDigits = new Vector3[frames * digits];
        var leftDigits = new Vector3[frames * digits];
        var buffer = new Vector3[digits];
        bool hasRight = true, hasLeft = true;
        Quaternion fromPose = Quaternion.Inverse(poseTurn), restBack = Quaternion.Inverse(cameraRest);
        for (int i = 0; i < frames; i++)
        {
            float time = Mathf.Min(i / rate, length);
            if (!arms.Sample(clip, time)) return null;
            arms.Gun(frame, out Vector3 at, out Quaternion turn);
            Quaternion toGun = Quaternion.Inverse(turn);
            track.gunMove[i] = fromPose * (at - poseAt);
            track.gunTurn[i] = fromPose * turn;
            if (!Hand(arms, false, at, toGun, fromPose, out track.right[i]) || !Hand(arms, true, at, toGun, fromPose, out track.left[i])) return null;
            // The palms are in the gun's frame, which is the world's for the gun's copy at the origin.
            track.right[i].gap = surface.Gap(track.right[i].palm, GunTrack.GapMost);
            track.left[i].gap = surface.Gap(track.left[i].palm, GunTrack.GapMost);
            for (int side = 0; side < 2; side++)
            {
                if (!arms.Arm(side == 1, out Vector3 shoulder, out Vector3 wrist, out float armLength)) continue;
                var arm = own[side];
                float straight = Vector3.Distance(shoulder, wrist) / armLength;
                arm.StraightLeast = Mathf.Min(arm.StraightLeast, straight);
                if (straight > arm.StraightMost)
                {
                    arm.StraightMost = straight;
                    arm.MostAt = time;
                }
                arm.LengthLeast = Mathf.Min(arm.LengthLeast, armLength);
                arm.LengthMost = Mathf.Max(arm.LengthMost, armLength);
                arm.ShoulderMoved = Mathf.Max(arm.ShoulderMoved, Vector3.Distance(shoulder, poseShoulders[side]));
            }
            if (hasRight && arms.Digits(false, buffer)) Array.Copy(buffer, 0, rightDigits, i * digits, digits);
            else hasRight = false;
            if (hasLeft && arms.Digits(true, buffer)) Array.Copy(buffer, 0, leftDigits, i * digits, digits);
            else hasLeft = false;
            track.camera[i] = camera.Turn(clip, time) * restBack;
        }
        track.rightDigits = hasRight ? rightDigits : Array.Empty<Vector3>();
        track.leftDigits = hasLeft ? leftDigits : Array.Empty<Vector3>();
        sourceArms[track] = own;
        (float move, float turn, float palm) between = default;
        for (int i = 0; i + 1 < frames; i++)
        {
            float time = (track.TimeOf(i) + track.TimeOf(i + 1)) * .5f;
            if (!arms.Sample(clip, time) || !track.Sample(time, null, null, out GunContact read, out _)) break;
            arms.Gun(frame, out Vector3 at, out Quaternion turn);
            Quaternion toGun = Quaternion.Inverse(turn);
            between.move = Mathf.Max(between.move, Vector3.Distance(fromPose * (at - poseAt), read.GunMove));
            between.turn = Mathf.Max(between.turn, Quaternion.Angle(fromPose * turn, read.GunTurn));
            if (arms.Palm(false, at, toGun, out Vector3 rightPalm, out _, out _)) between.palm = Mathf.Max(between.palm, Vector3.Distance(rightPalm, read.RightPalm));
            if (arms.Palm(true, at, toGun, out Vector3 leftPalm, out _, out _)) between.palm = Mathf.Max(between.palm, Vector3.Distance(leftPalm, read.LeftPalm));
        }
        betweenFrames[track] = between;
        return track;
    }

    private static bool Hand(GunArms arms, bool isLeft, Vector3 gunAt, Quaternion toGun, Quaternion toPoseGun, out GunTrackHand hand)
    {
        hand = default;
        if (!arms.Palm(isLeft, gunAt, toGun, out hand.palm, out hand.fingers, out hand.facing)) return false;
        // The elbow in the frame of the gun as the pose clip holds it: KINEMATION's arms bend from their shoulders, which
        // don't move with the gun. Zero where the arm is too straight to tell.
        arms.ElbowWay(isLeft, toPoseGun, out hand.elbow);
        return true;
    }

    /// <summary>
    /// KINEMATION's camera bone on two bones of its own (root, then the camera bone), which its gun clips animate by name:
    /// the same two the game plays them on for the camera's motion (FPSBodyCamera), so what is read here is what the game shows.
    /// </summary>
    private sealed class CameraRig
    {
        private readonly GameObject rig;
        private readonly Transform root, bone;

        public CameraRig()
        {
            rig = new GameObject("KINEMATION camera bone (reading)") { hideFlags = HideFlags.HideAndDontSave };
            root = new GameObject(CameraRootName) { hideFlags = HideFlags.HideAndDontSave }.transform;
            root.SetParent(rig.transform, false);
            bone = new GameObject(CameraBoneName) { hideFlags = HideFlags.HideAndDontSave }.transform;
            bone.SetParent(root, false);
        }

        /// <summary>The camera bone's turn from the rig as <paramref name="clip"/> has it at <paramref name="time"/>.</summary>
        public Quaternion Turn(AnimationClip clip, float time)
        {
            // Put back first: a clip without the bone would leave it where the last clip had it.
            root.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            bone.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            clip.SampleAnimation(rig, time);
            return Quaternion.Inverse(rig.transform.rotation) * bone.rotation;
        }

        public void Release()
        {
            if (rig != null) Object.DestroyImmediate(rig);
        }
    }

    // ---- The report ----

    private static void WriteReport(FPSBodyAssets assets, GunTracks list, List<string> notes, int read, int due, double seconds)
    {
        handAway = FPSBody.TableRules().HoldsWithin;
        var text = new StringBuilder();
        text.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} gun actions as numbers (GunTrackSet), read from KINEMATION's own clips on KINEMATION's arms. "
            + $"This run read {read} of {due} guns in {seconds:0.0} s; the others were current. Lengths in cm in the gun's own frame, times in s "
            + "into the clip. 'off the hold' = how far the clip has the gun from where the gun's pose clip holds it. The 'stays within' "
            + $"bands are this report's own and decide nothing in the game; a palm is 'off the gun' over {handAway * 100f:0.#} cm from the "
            + "gun's surface, where the hold's rule says a hand no longer has full hold of it (Rig.holdsWithin).");
        foreach (string note in notes) text.AppendLine("! " + note);
        Gaps(assets, list, text);
        Between(text);
        foreach (var gun in assets.guns)
        {
            if (gun.weapon == null) continue;
            var set = list.For(gun.weapon);
            text.AppendLine();
            if (set == null)
            {
                text.AppendLine($"== {gun.weapon.name}: not read.");
                continue;
            }
            text.AppendLine($"== {gun.weapon.name}{(set.holds ? "" : " (its pose clip is no two-handed hold: the support palm is out of reach of the gun)")}");
            if (sourceShoulders.TryGetValue(gun.weapon, out string joints))
                text.AppendLine("KINEMATION's arms' shoulder joints in the hold, from the gun in the gun's own frame:" + joints);
            var hold = set.Track(GunTrackKind.Hold);
            foreach (var track in set.tracks)
                if (track != null) Describe(track, hold, text);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllText(ReportPath, text.ToString());
    }

    // How near the gun's surface the palms are, over every frame of every track: in the hold (where a hand has hold of the
    // gun for certain) and through the actions (where a hand is either at the gun or well away from it). What "has hold of
    // the gun" is taken to mean (Rig.holdsWithin) is read off this.
    private static void Gaps(FPSBodyAssets assets, GunTracks list, StringBuilder text)
    {
        int bins = GapEdges.Length;
        var holds = new int[bins];
        var acts = new int[bins];
        float holdMost = 0f;
        string holdMostOf = "";
        foreach (var gun in assets.guns)
        {
            var set = gun.weapon != null ? list.For(gun.weapon) : null;
            if (set == null) continue;
            foreach (var track in set.tracks)
            {
                if (track == null) continue;
                bool hold = track.kind == GunTrackKind.Hold || track.kind == GunTrackKind.HoldNoGrip;
                int frames = hold ? Mathf.Min(1, track.Frames) : track.Frames;
                for (int i = 0; i < frames; i++)
                    for (int side = 0; side < 2; side++)
                    {
                        float gap = (side == 0 ? track.right[i] : track.left[i]).gap;
                        int bin = bins - 1;
                        while (bin > 0 && gap * 100f < GapEdges[bin]) bin--;
                        (hold ? holds : acts)[bin]++;
                        if (hold && gap > holdMost && !(side == 1 && !set.holds))
                        {
                            holdMost = gap;
                            holdMostOf = $"{gun.weapon.name} {track.kind} {(side == 0 ? "right" : "left")}";
                        }
                    }
            }
        }
        text.AppendLine();
        text.AppendLine("== palms' gap from the gun's surface: how many frames' palms from each of these on (cm): " + string.Join(" ", GapEdges)
            + $" (the last: that far or further; looked for no further than {GunTrack.GapMost * 100f:0.#})");
        text.AppendLine("in the holds:   " + string.Join(" ", holds) + $"   (the widest in a two-handed hold: {holdMost * 100f:0.0} cm, {holdMostOf})");
        text.AppendLine("through actions: " + string.Join(" ", acts));
    }

    // The game reads a track straight from one frame to the next; the clip itself curves between them. The worst of that
    // over every clip read in this run.
    private static void Between(StringBuilder text)
    {
        if (betweenFrames.Count == 0) return;
        (float move, float turn, float palm) worst = default;
        string moveOf = "", turnOf = "", palmOf = "";
        foreach (var pair in betweenFrames)
        {
            string name = $"{pair.Key.clip} at {pair.Key.frameRate:0.#}/s";
            if (pair.Value.move > worst.move) { worst.move = pair.Value.move; moveOf = name; }
            if (pair.Value.turn > worst.turn) { worst.turn = pair.Value.turn; turnOf = name; }
            if (pair.Value.palm > worst.palm) { worst.palm = pair.Value.palm; palmOf = name; }
        }
        text.AppendLine();
        text.AppendLine("== midway between a track's frames, how far the clip itself is from the track's straight reading, at most over "
            + $"{betweenFrames.Count} clips: the gun {worst.move * 1000f:0.0} mm ({moveOf}) and {worst.turn:0.00}° ({turnOf}), a palm on the gun "
            + $"{worst.palm * 1000f:0.0} mm ({palmOf})");
    }

    private static void Describe(GunTrack track, GunTrack hold, StringBuilder text)
    {
        int frames = track.Frames;
        if (frames == 0)
        {
            text.AppendLine($"{track.kind} {track.clip}: empty.");
            return;
        }
        int last = frames - 1, most = 0;
        for (int i = 1; i < frames; i++)
            if (track.gunMove[i].sqrMagnitude > track.gunMove[most].sqrMagnitude) most = i;
        text.Append($"{track.kind} {track.clip}: {track.length:0.00} s, {frames} frames at {track.frameRate:0.#}/s; gun off the hold: start {Off(track, 0)}, "
            + $"most {Off(track, most)} at {track.TimeOf(most):0.00}, end {Off(track, last)}");
        if (frames > 1)
        {
            text.Append("; stays within");
            foreach (var (move, turn) in Bands) text.Append($" {move * 100f:0.#} cm {turn:0}° {Settles(track, move, turn)},");
            text.Length--;
        }
        if (hold != null && hold.Frames > 0)
            text.Append($"; right palm {Away(track, hold.right[0].palm, false)}, left palm {Away(track, hold.left[0].palm, true)}");
        float cameraMost = 0f;
        foreach (var turn in track.camera) cameraMost = Mathf.Max(cameraMost, Quaternion.Angle(Quaternion.identity, turn));
        text.Append(cameraMost > .01f ? $"; camera turned at most {cameraMost:0.0}°" : "; no camera motion in the clip");
        bool rightFingers = track.rightDigits.Length == frames * GunArms.DigitCount, leftFingers = track.leftDigits.Length == frames * GunArms.DigitCount;
        text.Append(rightFingers && leftFingers ? "; fingers read" : $"; fingers not read:{(rightFingers ? "" : " right")}{(leftFingers ? "" : " left")}");
        if (betweenFrames.TryGetValue(track, out var between) && frames > 1)
            text.Append($"; between frames the clip is at most {between.move * 1000f:0.0} mm {between.turn:0.00}° from the track, a palm {between.palm * 1000f:0.0} mm");
        // What the rules wait for after each shot of a gun worked by hand (FPSBody.MeasureCycle asks the same).
        if (track.kind == GunTrackKind.Cycle && FPSBody.HandsBack(track, hold, out float handsBack))
            text.Append($"; both palms are back on the hold from {handsBack:0.00} s");
        if (sourceArms.TryGetValue(track, out var own))
        {
            text.Append("; KINEMATION's own arms:");
            for (int side = 0; side < 2; side++)
            {
                var arm = own[side];
                if (arm.LengthMost <= 0f) continue;
                text.Append($" {(side == 0 ? "right" : "left")} straight {arm.StraightLeast:0.00}-{arm.StraightMost:0.00} (most at {arm.MostAt:0.00}), its bones "
                    + $"{arm.LengthLeast * 100f:0.0}-{arm.LengthMost * 100f:0.0} cm long, its shoulder joint up to {arm.ShoulderMoved * 100f:0.0} cm from where the pose has it;");
            }
            text.Length--;
        }
        text.AppendLine();
        if (track.kind == GunTrackKind.Draw || track.kind == GunTrackKind.Holster) Table(track, text);
    }

    private static string Off(GunTrack track, int frame)
        => $"{track.gunMove[frame].magnitude * 100f:0.0} cm {Quaternion.Angle(Quaternion.identity, track.gunTurn[frame]):0.0}°";

    // From when on the gun stays within a band of its hold to the clip's end.
    private static string Settles(GunTrack track, float move, float turn)
    {
        int outside = -1;
        for (int i = track.Frames - 1; i >= 0; i--)
            if (track.gunMove[i].magnitude > move || Quaternion.Angle(Quaternion.identity, track.gunTurn[i]) > turn)
            {
                outside = i;
                break;
            }
        if (outside == track.Frames - 1) return "never";
        return outside < 0 ? "throughout" : $"from {track.TimeOf(outside + 1):0.00}";
    }

    // How far a palm gets from its place in the hold, and between which moments it is off the gun (its first leaving of the
    // gun's surface to its last return).
    private static string Away(GunTrack track, Vector3 place, bool isLeft)
    {
        float most = 0f;
        int first = -1, last = -1;
        for (int i = 0; i < track.Frames; i++)
        {
            var hand = isLeft ? track.left[i] : track.right[i];
            most = Mathf.Max(most, Vector3.Distance(hand.palm, place));
            if (hand.gap <= handAway) continue;
            if (first < 0) first = i;
            last = i;
        }
        return $"at most {most * 100f:0.0} cm from its place" + (first >= 0 ? $" (off the gun {track.TimeOf(first):0.00}-{track.TimeOf(last):0.00})" : "");
    }

    // The gun's way home through a draw or a holster, read back through the track's own reader (GunTrack.Sample).
    private static void Table(GunTrack track, StringBuilder text)
    {
        text.Append("   off the hold by time (s:cm/°):");
        for (float time = 0f; time < track.length + TableStep * .5f; time += TableStep)
        {
            float at = Mathf.Min(time, track.length);
            if (!track.Sample(at, null, null, out GunContact moment, out _)) break;
            text.Append($" {at:0.00}:{moment.GunMove.magnitude * 100f:0.0}/{Quaternion.Angle(Quaternion.identity, moment.GunTurn):0}");
        }
        text.AppendLine();
    }
}
#endif
