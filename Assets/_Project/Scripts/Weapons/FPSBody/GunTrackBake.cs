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
    // Raised when what a track holds or how it is read changes, so every gun is read again.
    private const int Version = 1;
    // KINEMATION's camera bone in its gun clips; the game plays them on the same two bones (FPSBodyCamera).
    private const string CameraRootName = "root", CameraBoneName = "camera_bone";
    // The report's own bands: how near its hold a gun is said to stay (m, degrees), and how far a palm is from its place in
    // the hold to be said away (m). They decide nothing in the game.
    private static readonly (float move, float turn)[] Bands = { (.005f, 1f), (.01f, 2f), (.02f, 5f) };
    /// <summary>A palm further than this from its place in the hold is said to be away from the gun (m). The reports' band
    /// (this one's and the bench's, GunBench); it decides nothing in the game.</summary>
    internal const float HandAway = .04f;
    // A draw's and a holster's table of the gun's way home is printed every this many seconds.
    private const float TableStep = .05f;

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
        // A hold at all: the support palm within reach of the gun (FPSBody.PoseHolds' rule).
        bool holds = arms.Palm(true, poseAt, Quaternion.Inverse(poseTurn), out Vector3 support, out _, out _) && support.magnitude < FPSBody.PoseReach;
        Quaternion cameraRest = camera.Turn(gun.sourcePose, 0f);

        var tracks = new List<GunTrack>();
        foreach (var (kind, clip) in ClipsOf(gun))
        {
            if (clip == null) continue;
            var track = Read(kind, clip, arms, frame, poseAt, poseTurn, camera, cameraRest);
            if (track != null) tracks.Add(track);
            else notes.Add($"{gun.weapon.name}: {kind} ({clip.name}) could not be read on {prefab.name}; left out.");
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

    // One clip, frame by frame at its own frames (the last at its end): the gun from where the pose clip holds it, both
    // hands on the gun as it then is, the finger ways and the camera bone's turn from the pose. As the game read a moment of
    // it until now (FPSBody.SampleContact), so the numbers mean the same.
    private static GunTrack Read(GunTrackKind kind, AnimationClip clip, GunArms arms, Quaternion frame, Vector3 poseAt, Quaternion poseTurn,
        CameraRig camera, Quaternion cameraRest)
    {
        float rate = clip.frameRate > 0f ? clip.frameRate : 30f;
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
            if (hasRight && arms.Digits(false, buffer)) Array.Copy(buffer, 0, rightDigits, i * digits, digits);
            else hasRight = false;
            if (hasLeft && arms.Digits(true, buffer)) Array.Copy(buffer, 0, leftDigits, i * digits, digits);
            else hasLeft = false;
            track.camera[i] = camera.Turn(clip, time) * restBack;
        }
        track.rightDigits = hasRight ? rightDigits : Array.Empty<Vector3>();
        track.leftDigits = hasLeft ? leftDigits : Array.Empty<Vector3>();
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
        var text = new StringBuilder();
        text.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm:ss} gun actions as numbers (GunTrackSet), read from KINEMATION's own clips on KINEMATION's arms. "
            + $"This run read {read} of {due} guns in {seconds:0.0} s; the others were current. Lengths in cm in the gun's own frame, times in s "
            + "into the clip. 'off the hold' = how far the clip has the gun from where the gun's pose clip holds it. The 'stays within' "
            + $"bands and 'away' (a palm over {HandAway * 100f:0.#} cm from its place in the hold) are this report's own and decide nothing in the game.");
        foreach (string note in notes) text.AppendLine("! " + note);
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
            var hold = set.Track(GunTrackKind.Hold);
            foreach (var track in set.tracks)
                if (track != null) Describe(track, hold, text);
        }
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllText(ReportPath, text.ToString());
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

    // How far a palm gets from its place in the hold, and between which moments it is away from it.
    private static string Away(GunTrack track, Vector3 place, bool isLeft)
    {
        float most = 0f;
        int first = -1, last = -1;
        for (int i = 0; i < track.Frames; i++)
        {
            float gap = Vector3.Distance((isLeft ? track.left[i] : track.right[i]).palm, place);
            most = Mathf.Max(most, gap);
            if (gap <= HandAway) continue;
            if (first < 0) first = i;
            last = i;
        }
        return $"at most {most * 100f:0.0} cm from its place" + (first >= 0 ? $" (away {track.TimeOf(first):0.00}-{track.TimeOf(last):0.00})" : "");
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
