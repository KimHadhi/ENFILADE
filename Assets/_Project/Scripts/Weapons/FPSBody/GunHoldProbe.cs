#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Text;
using KINEMATION.CharacterAnimationSystem.Integrations.FPSAnimationPack.Scripts;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The editor's check of how every character holds every gun, without playing: each character's model is stood in each gun's
/// pose clip at the size the game draws it and asked for its hold through the very classes the game builds a gun with
/// (HoldBody, GunHandles, GunHold). Logs/gun-hold-probe.txt then has every gun as a thing to hold (its butt, its grips, its
/// sight), one line per character and gun (the kind of hold, where the gun then is from the eyes, how straight each arm
/// is, where the sight line runs, and for a handgun the eye relief fully aimed), and per gun how far apart the characters
/// hold it.
/// So a hold that breaks on one character shows as a number before anyone plays it, a mod's character too, and a change to
/// a rule or a table number is read on every body at once (the user, 2026-11-14: "캐릭터마다 총 드는 게 달라서 고장나는데 이거
/// 근본적인 원인 고쳐 주세요": the causes were found by measuring every body here first, and the hold was rebuilt against it).
/// A character is measured as its model stands (the look it comes in); in play it is measured in what it then wears.
/// ENFILADE → 총 → 쥐기 점검 runs it; so does making the file Temp/enfilade-holdprobe.request (a tool outside Unity asking,
/// as RefreshOnRequest is asked). It lives beside FPSBody because it measures with the game's own classes.
/// </summary>
[InitializeOnLoad]
internal static class GunHoldProbe
{
    private const string Request = "Temp/enfilade-holdprobe.request", ReportPath = "Logs/gun-hold-probe.txt";
    private static double next;

    static GunHoldProbe() => EditorApplication.update += Tick;

    private static void Tick()
    {
        double now = EditorApplication.timeSinceStartup;
        if (now < next) return;
        next = now + .5;
        if (!File.Exists(Request)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        try
        {
            File.Delete(Request);
        }
        catch (System.Exception error) when (error is IOException || error is System.UnauthorizedAccessException)
        {
            return;
        }
        Run();
    }

    // One gun for every body: its hold, and its starting sight's eye point in the gun's frame (m).
    private struct Gun
    {
        public GunHold Hold;
        public bool HasSight;
        public Vector3 SightAt;
        public Quaternion SightTurn;
    }

    // Where each character holds one gun, from its eyes (cm), for the spread between characters.
    private sealed class Spread
    {
        public Vector3 Least = Vector3.positiveInfinity, Most = Vector3.negativeInfinity;
        public int Bodies;

        public void Add(Vector3 at)
        {
            Least = Vector3.Min(Least, at);
            Most = Vector3.Max(Most, at);
            Bodies++;
        }
    }

    [MenuItem("ENFILADE/총/쥐기 점검 (모든 캐릭터)")]
    private static void Run()
    {
        var profile = PlayerBodyProfile.Load();
        var assets = FPSBodyAssets.Instance;
        var bakes = GunBakes.Instance;
        if (profile == null || assets == null || bakes == null)
        {
            Debug.LogWarning("GunHoldProbe: the body profile, the gun list or the gun bakes are missing; nothing to check.");
            return;
        }
        // Read afresh: outside play nothing else forgets the guns' handles.
        GunHandles.Forget();
        var rules = FPSBody.TableRules();
        var text = new StringBuilder();
        text.AppendLine($"{System.DateTime.Now:yyyy-MM-dd HH:mm:ss} gun hold check. Lengths in cm in game; places from the eyes as the body looks "
            + "(x right, y up, z ahead). straight = shoulder joint to wrist over the arm's own length (1 = locked straight). "
            + $"Rules: no arm straighter than {rules.ReachMost:0.00}; a handgun's trigger arm no more folded than {rules.BendLeast:0.00}; a stock's "
            + $"butt on the body's own shoulder, pressed up to {rules.StockPress * 100f:0.#} into it; the support hand no nearer the trigger hand "
            + $"than {rules.SupportNearest * 100f:0.#}; a stocked gun's trigger wrist at least {rules.WristUnderShoulder * 100f:0.#} under its shoulder joint.");
        var tracks = GunTracks.Instance;
        var guns = new Gun[assets.guns.Length];
        var spreads = new Spread[assets.guns.Length];
        try
        {
            text.AppendLine();
            text.AppendLine("== the guns as things to hold (the gun's own frame: x right, y up, z along the barrel)");
            for (int i = 0; i < assets.guns.Length; i++)
            {
                guns[i] = GunOf(assets.guns[i], rules, tracks);
                spreads[i] = new Spread();
                Describe(assets.guns[i], guns[i], text);
            }
            float camera = CameraHeight();
            foreach (var skin in profile.Skins)
            {
                var model = skin != null ? profile.ModelOf(skin) : null;
                if (model != null) Check(skin.Id, model, camera, assets, bakes, guns, spreads, text);
            }
            text.AppendLine();
            text.AppendLine("== how far apart the characters hold each gun (the gun from the eyes, least to most)");
            for (int i = 0; i < assets.guns.Length; i++)
            {
                if (spreads[i].Bodies < 2 || assets.guns[i].weapon == null) continue;
                Vector3 a = spreads[i].Least, b = spreads[i].Most;
                text.AppendLine($"{assets.guns[i].weapon.name} ({guns[i].Hold.Name}): x {a.x:0.0}..{b.x:0.0}, y {a.y:0.0}..{b.y:0.0}, z {a.z:0.0}..{b.z:0.0}");
            }
        }
        finally
        {
            GunHandles.Forget();
        }
        Directory.CreateDirectory(Path.GetDirectoryName(ReportPath));
        File.WriteAllText(ReportPath, text.ToString());
        Debug.Log($"GunHoldProbe: every character's hold of every gun is in {ReportPath}.");
    }

    // The standing camera height the game sizes every body to (Player.prefab's CrouchPivot, as GunClipRetarget takes it).
    private static float CameraHeight()
    {
        var player = Resources.Load<GameObject>("Characters/Player");
        var pivot = player != null ? player.transform.Find("CrouchPivot") : null;
        return pivot != null ? pivot.localPosition.y : 1.6f;
    }

    // The gun's hold, from a copy of its model as a body wears it (FPSBody.AddGun reads the handles the same way).
    private static Gun GunOf(in FPSBodyAssets.Gun entry, in GunHold.Rules rules, GunTracks tracks)
    {
        var gun = default(Gun);
        if (entry.weapon == null || entry.prefab == null) return gun;
        var copy = Object.Instantiate(entry.prefab);
        copy.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            copy.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            copy.transform.localScale = Vector3.one;
            var kinemation = copy.GetComponent<CasFPSWeapon>();
            Transform own = kinemation != null ? kinemation.aimPoint : null;
            gun.Hold = GunHold.For(GunHandles.Of(entry, tracks != null ? tracks.For(entry.weapon) : null, FPSBody.GunFrameOf(kinemation), copy.transform, own),
                rules);
            // The sight the gun starts with, else KINEMATION's own aim point.
            var sights = copy.GetComponent<GunSights>();
            Transform sight = sights != null && sights.Count > 0 ? sights.sights[Mathf.Clamp(sights.initial, 0, sights.Count - 1)].aimPoint : null;
            if (sight == null) sight = own;
            if (sight != null)
            {
                gun.SightAt = copy.transform.InverseTransformPoint(sight.position);
                gun.SightTurn = Quaternion.Inverse(copy.transform.rotation) * sight.rotation;
                gun.HasSight = true;
            }
            return gun;
        }
        finally
        {
            Object.DestroyImmediate(copy);
        }
    }

    // One gun's own points, the same for every body.
    private static void Describe(in FPSBodyAssets.Gun entry, in Gun gun, StringBuilder text)
    {
        if (entry.weapon == null) return;
        if (gun.Hold == null)
        {
            text.AppendLine($"{entry.weapon.name}: its pose could not be read on KINEMATION's arms; held as its clip alone has it.");
            return;
        }
        var h = gun.Hold.Gun;
        string P(Vector3 v) => $"({v.x * 100f:0.0}, {v.y * 100f:0.0}, {v.z * 100f:0.0})";
        text.Append($"{entry.weapon.name} ({gun.Hold.Name}): butt {P(h.Butt)}, trigger palm {P(h.RightPalm)}, support palm {P(h.LeftPalm)}"
            + $"{(h.SupportOnGun ? "" : " (off the gun)")}; the butt {h.StockBehindGrip * 100f:0.0} behind the trigger palm, the palms "
            + $"{h.GripsApart * 100f:0.0} apart along the barrel; barrel ({h.Barrel.x:0.00}, {h.Barrel.y:0.00}, {h.Barrel.z:0.00})");
        if (gun.HasSight) text.Append($"; sight's eye point {P(gun.SightAt)}, {(gun.SightAt.y - h.Butt.y) * 100f:0.0} over the butt's middle");
        text.AppendLine();
    }

    private static void Check(string id, GameObject model, float camera, FPSBodyAssets assets, GunBakes bakes, Gun[] guns, Spread[] spreads,
        StringBuilder text)
    {
        var instance = Object.Instantiate(model);
        instance.hideFlags = HideFlags.HideAndDontSave;
        try
        {
            instance.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            instance.transform.localScale = Vector3.one;
            var animator = instance.GetComponentInChildren<Animator>();
            if (animator == null || !animator.isHuman)
            {
                text.AppendLine();
                text.AppendLine($"== {id}: no humanoid avatar, not checked.");
                return;
            }
            // As a player's body plays (PlayerBody.Build): HoldBody.Measure says why.
            animator.applyRootMotion = false;
            instance.transform.localScale = Vector3.one * PlayerBody.DrawnScaleOf(animator, instance.transform, camera);
            var watch = System.Diagnostics.Stopwatch.StartNew();
            var shoulder = HoldBody.ShoulderOf(animator);
            double readTime = watch.Elapsed.TotalMilliseconds, poseTime = 0d;
            int posed = 0;
            for (int i = 0; i < assets.guns.Length; i++)
            {
                var entry = assets.guns[i];
                var gun = guns[i];
                if (gun.Hold == null || !bakes.TryGet(model, entry.weapon, out var bake, out bool own) || bake.pose == null) continue;
                watch.Restart();
                var body = HoldBody.Measure(animator, bake.pose, shoulder, posed == 0);
                poseTime += watch.Elapsed.TotalMilliseconds;
                if (body == null) continue;
                if (posed++ == 0)
                {
                    Vector3 r = body.FromEyes(body.Right.Shoulder) * 100f, l = body.FromEyes(body.Left.Shoulder) * 100f, p = body.FromEyes(body.Pocket) * 100f;
                    text.AppendLine();
                    text.AppendLine($"== {id} ({model.name}{(own ? ", the body the gun clips were made on" : "")}): drawn x{body.Scale:0.000}; arms right "
                        + $"{body.Right.Length * body.Scale * 100f:0.0} left {body.Left.Length * body.Scale * 100f:0.0}; hand (wrist to middle knuckle) "
                        + $"{body.Right.HandRun * body.Scale * 100f:0.0}; in {entry.weapon.name}'s pose the shoulder joints sit right ({r.x:0.0}, {r.y:0.0}, {r.z:0.0}) "
                        + $"left ({l.x:0.0}, {l.y:0.0}, {l.z:0.0}), the shoulder pocket ({p.x:0.0}, {p.y:0.0}, {p.z:0.0}) "
                        + $"on {body.PocketOn}, {(body.Pocket.z - body.Right.Shoulder.z) * body.Scale * 100f:0.0} ahead of the joint; the line "
                        + $"from inside the shoulder passes through: {body.PocketThrough}");
                }
                spreads[i].Add(Line(entry.weapon.name, gun, body, body.PlaceOf(bake, own), own, text));
            }
            if (posed > 0)
                text.AppendLine($"(measuring: the shoulder's surface read once in {readTime:0.0} ms, {(shoulder != null ? shoulder.Points : 0)} vertices of "
                    + $"{(shoulder != null ? shoulder.Meshes : 0)} meshes; each gun's pose then {poseTime / posed:0.0} ms)");
        }
        finally
        {
            Object.DestroyImmediate(instance);
        }
    }

    // One character's hold of one gun; returns where the gun then is from the eyes (cm).
    private static Vector3 Line(string name, in Gun gun, HoldBody body, KTransform clip, bool own, StringBuilder text)
    {
        var hold = gun.Hold;
        var handles = hold.Gun;
        float cm = body.Scale * 100f;
        var pose = hold.On(body, clip);
        hold.WristsAt(body, clip, out Vector3 clipRight, out Vector3 clipLeft);
        hold.WristsAt(body, pose.Gun, out Vector3 right, out _);
        Vector3 at = body.FromEyes(pose.Gun.position) * 100f;
        Quaternion axes = pose.Gun.rotation * handles.Frame;
        text.Append($"{name} ({hold.Name}): gun ({at.x:0.0}, {at.y:0.0}, {at.z:0.0}); as its clip alone has it R {body.Right.Straight(clipRight):0.00} "
            + $"L {body.Left.Straight(clipLeft):0.00} -> held R {pose.Right:0.00} L {pose.Left:0.00}, {pose.Fit * 100f:0.0} toward the body, support "
            + $"palm {pose.Slide * 100f:0.0} back; trigger wrist {(body.Right.Shoulder.y - right.y) * cm:0.0} under its shoulder joint");
        // Where the clip's own hands are against the gun: alone as the clip has it, and as it is held (turned from the clip).
        hold.ClipHandsOff(body, clip, out float aloneRight, out float aloneRightTurn, out float aloneLeft, out float aloneLeftTurn);
        hold.ClipHandsOff(body, pose.Gun, out float heldRight, out float heldRightTurn, out float heldLeft, out float heldLeftTurn);
        text.Append($"; the clip's own hands off the gun: alone R {aloneRight * cm:0.0} cm {aloneRightTurn:0}° L {aloneLeft * cm:0.0} cm {aloneLeftTurn:0}°, "
            + $"held R {heldRight * cm:0.0} cm {heldRightTurn:0}° L {heldLeft * cm:0.0} cm {heldLeftTurn:0}°, the gun turned "
            + $"{Quaternion.Angle(clip.rotation, pose.Gun.rotation):0}° from its clip");
        if (hold is ShoulderedHold)
        {
            // How far the clip alone leaves the butt from this body's pocket. On the body the clips were made on the bake put
            // it on that body's surface by the same measure, so it is near nothing there unless the measures disagree.
            Vector3 clipButt = clip.position + clip.rotation * handles.Frame * (handles.Butt / body.Scale);
            text.Append($"; butt {pose.Lowered * 100f:0.0} under the pocket (on {body.PocketOn}, {(body.Pocket.z - body.Right.Shoulder.z) * cm:0.0} ahead of "
                + $"the joint); its clip alone has the butt {Vector3.Distance(clipButt, body.Pocket) * cm:0.0} from the pocket{(own ? " (the bake's own)" : "")}");
        }
        if (gun.HasSight)
        {
            // The sight's eye point as the gun is held: how far the eyes are from it, which aiming closes.
            Vector3 eye = body.FromEyes(pose.Gun.position + axes * (gun.SightAt / body.Scale)) * 100f;
            text.Append($"; sight's eye point ({eye.x:0.0}, {eye.y:0.0}, {eye.z:0.0})");
            if (hold is ShoulderedHold) text.Append(", closed by the head coming to the stock and the gun the rest of the way");
            else
            {
                // Fully aimed: the eye point on the eyes, further out by the hold's relief.
                float relief = hold.EyeRelief(body, gun.SightAt, gun.SightTurn);
                Quaternion aimedAxes = Quaternion.LookRotation(HoldBody.Ahead, HoldBody.Up) * Quaternion.Inverse(gun.SightTurn);
                Vector3 eyePoint = gun.SightAt - gun.SightTurn * Vector3.forward * relief;
                var aimed = new KTransform(body.Eyes - aimedAxes * (eyePoint / body.Scale), aimedAxes * Quaternion.Inverse(handles.Frame), Vector3.one);
                hold.WristsAt(body, aimed, out Vector3 aimedRight, out Vector3 aimedLeft);
                text.Append($"; aimed with the eye {relief * 100f:0.0} behind it R {body.Right.Straight(aimedRight):0.00} L {body.Left.Straight(aimedLeft):0.00}");
            }
        }
        text.AppendLine();
        return at;
    }
}
#endif
