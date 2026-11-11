using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Dual pistols on the body (AkimboGuns). The right hand's gun is the single gun FPSAF holds as ever: its pose clip,
/// recoil, sway, the gun held back from walls; once the left hand has its own gun, it is set apart to the right (the pair's
/// `spread`). The left hand's gun is a still copy of it, shown where the right one rests as seen in a mirror down the middle
/// of the view (the eyes), set apart to the left, with the right one's motion on it as it is (so both swing the same way as
/// the view turns and the legs walk) but not its kick, and a kick and a slide of its own on its own shots. Being the right
/// one's mirror it points at the same crosshair point, follows the gun held back from a wall and leans with the view.
/// Our hand IK (PlayerBodyHands.Akimbo) then puts the right hand back on its gun where it now is and the left on the copy,
/// the way the right holds its own; the copy stays in the hand where the arm comes short. The copy is put away while the
/// hands are busy with the right gun (its reload clip, a look over it) and fetched again as the clip ends, and while the
/// left hand is elsewhere (a vault's plant). A left shot leaves from the copy's muzzle (AkimboMuzzle, which
/// WeaponController fires from): its flame, light and brass come from there.
/// The pair's row may name another model for both guns (AkimboModels, Akimbo.csv `model`): the right gun's own meshes are
/// hidden and both hands hold that model, set in the gun's sight line by the row's offset, turn and size; the muzzles are
/// found on it, and a part named Slider or Slide works as its slide.
/// All of this is the shown gun only: ammunition and fire are WeaponController's, the hit is the shot's.
/// </summary>
public sealed partial class FPSBody
{
    // The left gun shows, and its shots leave from it, once the left hand is this far onto it (0-1).
    private const float LeftShown = .5f;
    // A shot's slide is back for about this long (s): the right gun isn't copied for the left until then.
    private const float RestAfterShot = .3f;

    // A part of a still copy that moves along the sight line as a slide does.
    private struct Slide
    {
        public Transform Bone;
        public Vector3 Rest, Back;

        // Back: `amount` gun-space units back from its rest.
        public void Set(float amount)
        {
            if (Bone != null) Bone.localPosition = Rest + Back * amount;
        }
    }

    // What a copy was made from, so it is made again when that changes (another gun, another model, the table's fitting).
    private struct Made
    {
        public Object Source;
        public FPSGun Gun;
        public Vector3 Offset, Turn;
        public float Scale;

        public bool Same(in Made other) => Source == other.Source && Gun == other.Gun && Offset == other.Offset
            && Turn == other.Turn && Mathf.Approximately(Scale, other.Scale);
    }

    // The pair in hand (its Akimbo.csv row), or null for any other gun.
    private AkimboGuns.Look akimboLook;
    // 0-1: the left gun in the left hand. How far the right gun is set apart this frame (m).
    private float akimboHold, akimboApart;
    // The left gun's root (where the right gun's weapon bone is in the mirror) and what it was made from.
    private Transform leftGun;
    private Made leftMade;
    // The left gun's muzzle in its root's space.
    private Vector3 leftMuzzleAt;
    private Quaternion leftMuzzleTurn = Quaternion.identity;
    // Where the left gun's shots leave from. Kept for the player's life: a shot's flame hangs on it (MuzzleFlash).
    private Transform leftMuzzle;
    // The right gun's sight frame in the eyes' space: where it rests (followed slowly) and what it does about that.
    private Vector3 restAt, swayAt;
    private Quaternion restTurn = Quaternion.identity, swayTurn = Quaternion.identity;
    private bool akimboKnown;
    // The left gun's kick and slide (0-1), its slide held back by the last round, and when the right one last fired.
    private float leftKick, leftSlide, rightShotAt = float.NegativeInfinity;
    private bool leftSlideHeld;
    private Slide leftSlideBone;
    // The pair's model in the right hand: made from what, the right gun's own renderers hidden for it, its muzzle in the
    // gun's space, and its slide.
    private GameObject rightModel;
    private Made rightMade;
    private readonly List<Renderer> rightHidden = new();
    private Vector3 rightMuzzleAt;
    private Quaternion rightMuzzleTurn = Quaternion.identity;
    private float rightSlide;
    private bool rightSlideHeld;
    private Slide rightSlideBone;
    // When the right gun's reload clip is over: the left hand goes back for its gun then.
    private float reloadClipEnds;
    // The model the table named that isn't there yet, said once.
    private string missingModel;

    /// <summary>How far the left hand holds a dual pistol's left gun (0-1); 0 for any other gun.</summary>
    internal float AkimboHold => akimboLook != null ? akimboHold : 0f;

    /// <summary>The left gun's muzzle while that gun is up in the left hand, else null (WeaponController fires from it).</summary>
    internal Transform AkimboMuzzle => akimboLook != null && HoldsGun && leftGun != null && leftGun.gameObject.activeSelf ? leftMuzzle : null;

    /// <summary>
    /// For the hands (PlayerBodyHands.Akimbo): a dual pistol is held, how far the left hand has its gun, and whether the
    /// right gun has been set apart from where FPSAF put the right hand on it.
    /// </summary>
    internal bool AkimboHands(out float hold, out bool apart)
    {
        hold = akimboHold;
        apart = akimboApart > 1e-4f;
        return akimboLook != null && HoldsGun && hasGunInView && leftGun != null && rig != null && rig.IkWeaponBone != null;
    }

    /// <summary>
    /// A point on the right gun to the same point on the left one, across the guns' middle (the mirror): in the guns' sight
    /// frame, from one side of the plane through the sight line to the other.
    /// </summary>
    internal Vector3 AkimboAcross(Vector3 point)
    {
        var gun = rig.IkWeaponBone;
        Quaternion sight = held.MuzzleRotation;
        Quaternion toSight = Quaternion.Inverse(sight);
        float middle = (toSight * held.MuzzlePosition).x;
        Vector3 local = toSight * gun.InverseTransformPoint(point);
        local.x = 2f * middle - local.x;
        return leftGun.TransformPoint(sight * local);
    }

    /// <summary>A way on the right gun to the same way on the left one (AkimboAcross for a direction).</summary>
    internal Vector3 AkimboAcrossWay(Vector3 way)
    {
        Quaternion sight = held.MuzzleRotation;
        Vector3 local = Quaternion.Inverse(sight) * rig.IkWeaponBone.InverseTransformDirection(way);
        local.x = -local.x;
        return leftGun.TransformDirection(sight * local);
    }

    /// <summary>
    /// The left hand was asked to be at <paramref name="askedAt"/> turned <paramref name="askedTurn"/> and ended up at
    /// <paramref name="at"/> turned <paramref name="turn"/> (out of the arm's reach, or still on its way): the left gun goes
    /// with the hand as it holds it.
    /// </summary>
    internal void KeepLeftGunInHand(Vector3 askedAt, Quaternion askedTurn, Vector3 at, Quaternion turn)
    {
        if (leftGun == null) return;
        Quaternion change = turn * Quaternion.Inverse(askedTurn);
        leftGun.SetPositionAndRotation(at + change * (leftGun.position - askedAt), change * leftGun.rotation);
        PlaceLeftMuzzle();
    }

    // ---- From FPSBody ----

    // A gun drawn (Equip): a pair's row, or none.
    private void AkimboEquip(WeaponData data)
    {
        var look = AkimboGuns.LookOf(data);
        if (look == akimboLook) return;
        akimboLook = look;
        akimboHold = akimboApart = leftKick = leftSlide = rightSlide = 0f;
        akimboKnown = leftSlideHeld = rightSlideHeld = false;
        ShowLeft(false);
        if (look == null) ShowOwnModel();
    }

    // The gun isn't held (put away, a climb, the body gone limp).
    private void AkimboOff()
    {
        akimboHold = akimboApart = 0f;
        akimboKnown = false;
        ShowLeft(false);
    }

    // The body was rebuilt (a new skin): the guns were made again, so the copies are made again from them.
    private void ForgetAkimbo()
    {
        ShowOwnModel();
        if (leftGun != null) Destroy(leftGun.gameObject);
        leftGun = null;
        leftMade = default;
        AkimboOff();
    }

    // A shot of a pair: the left gun's kicks it and its slide (true: the right gun's KINEMATION fire stays out); the
    // right gun's is KINEMATION's, its time kept so the left gun doesn't take its kick, and the pair's model's slide.
    private bool AkimboShot(WeaponShot shot)
    {
        if (akimboLook == null) return false;
        bool last = shot.AmmoAfterShot == 0;
        if (!shot.Left)
        {
            rightShotAt = Time.time;
            if (rightModel != null) { rightSlide = 1f; rightSlideHeld = last; }
            return false;
        }
        leftKick = leftSlide = 1f;
        leftSlideHeld = last;
        return true;
    }

    // The right gun's reload clip began, this long: the left gun waits it out, and comes back loaded.
    private void AkimboReloading(float clip)
    {
        reloadClipEnds = Time.time + clip;
        leftSlideHeld = rightSlideHeld = false;
    }

    // The left hand wants its gun: the gun is up (its draw time over) and the hands aren't busy with the right one, or
    // only with the end of its draw clip, or a reload whose clip is over.
    private bool AkimboWanted
    {
        get
        {
            if (!HoldsGun || controller.IsSwitching) return false;
            if (!held.HandsBusy) return true;
            if (held.Reloading) return Time.time >= reloadClipEnds;
            return Time.time - drawnAt < drawSettle;
        }
    }

    // Before RideGun (LateUpdate, 295), after KeepGunInView: sets the right gun apart and puts the left one in place.
    private void PoseAkimbo(float dt)
    {
        var look = akimboLook;
        var gun = rig != null ? rig.IkWeaponBone : null;
        akimboApart = 0f;
        if (look == null || gun == null || eyes == null) { AkimboOff(); return; }
        KeepRightModel(look);
        EnsureLeft(look);
        // The left hand takes its gun up as far as it is free to, once the gun is there (it waits for the right one to be
        // at rest to be copied).
        akimboHold = Mathf.MoveTowards(akimboHold, AkimboWanted && leftGun != null ? BothHands : 0f, dt / Mathf.Max(.01f, look.reach));

        // The right gun's sight frame in the eyes' space, before it is set apart: its muzzle (the middle of its front face,
        // on the gun's own middle line) and its measured turn (the sight line ahead, the gun's top up).
        Quaternion sight = held.MuzzleRotation;
        Vector3 at = eyes.InverseTransformPoint(gun.TransformPoint(held.MuzzlePosition));
        Quaternion turn = Quaternion.Inverse(eyes.rotation) * gun.rotation * sight;
        if (!akimboKnown)
        {
            restAt = at;
            restTurn = turn;
            swayAt = Vector3.zero;
            swayTurn = Quaternion.identity;
            akimboKnown = true;
        }
        // Where it rests follows slowly. What it does about that (sway, the walk's bob) comes over as it is, so both guns
        // swing the same way; just after the right one fired it is held, so the right gun's kick stays its own.
        float settle = Follow(look.settle, dt);
        restAt = Vector3.Lerp(restAt, at, settle);
        restTurn = Quaternion.Slerp(restTurn, turn, settle);
        float follow = Time.time - rightShotAt < look.steady ? 0f : Follow(look.follow, dt);
        swayAt = Vector3.Lerp(swayAt, at - restAt, follow);
        swayTurn = Quaternion.Slerp(swayTurn, turn * Quaternion.Inverse(restTurn), follow);

        if (leftGun == null) return;
        // Each gun out to its own side, as far as the left hand has its gun.
        akimboApart = look.spread * Mathf.SmoothStep(0f, 1f, akimboHold);
        gun.position += eyes.right * akimboApart;

        // The left gun: the right one's rest seen in a mirror down the middle of the view (its muzzle and sight line
        // reflected, its right side still its right), set apart the other way, with the right one's motion on it as it is.
        // As big as the right one (the body is fitted to its camera height, and the gun with it).
        Vector3 leftAt = new Vector3(-restAt.x - akimboApart, restAt.y, restAt.z) + swayAt;
        Quaternion leftSight = swayTurn * new Quaternion(restTurn.x, -restTurn.y, -restTurn.z, restTurn.w);
        Quaternion rotation = eyes.rotation * leftSight * Quaternion.Inverse(sight);
        float size = Mathf.Abs(gun.lossyScale.x);
        Vector3 position = eyes.TransformPoint(leftAt) - rotation * (held.MuzzlePosition * size);
        // Its own kick: back along its sight line and up about its grip, settling.
        leftKick *= Mathf.Exp(-look.kickReturn * dt);
        if (!leftSlideHeld) leftSlide *= Mathf.Exp(-look.slideReturn * dt);
        if (leftKick > 1e-3f)
        {
            Vector3 ahead = rotation * leftMuzzleTurn * Vector3.forward;
            Vector3 axis = Vector3.Cross(ahead, eyes.up);
            if (axis.sqrMagnitude > 1e-6f) rotation = Quaternion.AngleAxis(look.kickUp * leftKick, axis.normalized) * rotation;
            position -= ahead * (look.kickBack * leftKick);
        }
        Transform parent = leftGun.parent;
        leftGun.localScale = Vector3.one * (size / Mathf.Max(1e-4f, parent != null ? Mathf.Abs(parent.lossyScale.x) : 1f));
        leftGun.SetPositionAndRotation(position, rotation);
        leftSlideBone.Set(leftSlide * look.slideTravel);
        PlaceLeftMuzzle();
        ShowLeft(akimboHold >= LeftShown);
    }

    // After RideGun: the pair's model's muzzle in the right hand, and its slide.
    private void RideAkimbo()
    {
        if (akimboLook == null || rightModel == null) return;
        if (!rightSlideHeld) rightSlide *= Mathf.Exp(-akimboLook.slideReturn * Time.deltaTime);
        rightSlideBone.Set(rightSlide * akimboLook.slideTravel);
        if (muzzle == null) return;
        var gun = held.transform;
        muzzle.SetPositionAndRotation(gun.TransformPoint(rightMuzzleAt), gun.rotation * rightMuzzleTurn);
    }

    // The share of the way to go this frame to halve the distance every halfLife seconds; all of it for none.
    private static float Follow(float halfLife, float dt) => halfLife <= 0f ? 1f : 1f - Mathf.Exp(-.6931472f * dt / halfLife);

    private void ShowLeft(bool on)
    {
        if (leftGun != null && leftGun.gameObject.activeSelf != on) leftGun.gameObject.SetActive(on);
    }

    private void PlaceLeftMuzzle()
    {
        if (leftGun == null || leftMuzzle == null) return;
        leftMuzzle.SetPositionAndRotation(leftGun.TransformPoint(leftMuzzleAt), leftGun.rotation * leftMuzzleTurn);
    }

    // Under the eyes, beside the gun root but not in it (the HUD measures the root's renderers, GunScoreAnchor), and out
    // of the camera's shake.
    private Transform AkimboParent => weaponRoot != null && weaponRoot.parent != null ? weaponRoot.parent : transform;

    // The left gun as it should be: a still copy of the right gun as it is drawn, or the pair's model.
    private void EnsureLeft(AkimboGuns.Look look)
    {
        var model = AkimboModels.Find(look.model);
        var wanted = model != null
            ? new Made { Source = model, Gun = held, Offset = look.modelOffset, Turn = look.modelTurn, Scale = look.modelScale }
            : new Made { Source = held.gameObject, Gun = held, Scale = 1f };
        if (leftGun != null && wanted.Same(leftMade)) return;
        // The right gun is copied as it is, so only at rest: not in a clip of its own (a draw, a reload with its magazine
        // out), not just fired (its slide back), not empty (its slide held back).
        if (model == null && (held.HandsBusy || Time.time - lastShot < RestAfterShot
            || (controller.Runtime != null && controller.Runtime.Ammo == 0))) return;
        if (leftGun != null) Destroy(leftGun.gameObject);
        var parent = AkimboParent;
        leftGun = new GameObject("Akimbo Left Gun").transform;
        leftGun.SetParent(parent, false);
        if (model != null) ModelOnGun(model, leftGun, look, out leftMuzzleAt, out leftMuzzleTurn);
        else
        {
            StillCopy(held.gameObject, leftGun);
            leftMuzzleAt = held.MuzzlePosition;
            leftMuzzleTurn = held.MuzzleRotation;
        }
        leftSlideBone = FindSlide(leftGun, held.MuzzleRotation * Vector3.back);
        SetLayer(leftGun, weaponRoot != null ? weaponRoot.gameObject.layer : gameObject.layer);
        leftGun.gameObject.SetActive(false);
        leftMade = wanted;
        if (leftMuzzle == null)
        {
            leftMuzzle = new GameObject("Akimbo Left Muzzle").transform;
            leftMuzzle.SetParent(parent, false);
        }
    }

    // The pair's model in the right hand in place of the gun's own, when its row names one; the gun's own otherwise.
    private void KeepRightModel(AkimboGuns.Look look)
    {
        var model = AkimboModels.Find(look.model);
        if (model == null && !string.IsNullOrWhiteSpace(look.model) && look.model != missingModel)
        {
            missingModel = look.model;
            Report($"Akimbo.csv names the model '{look.model}', but no Art/Models/Weapons/{look.model} folder holds one yet: the gun's own is shown.");
        }
        var wanted = model != null
            ? new Made { Source = model, Gun = held, Offset = look.modelOffset, Turn = look.modelTurn, Scale = look.modelScale }
            : default;
        if (wanted.Same(rightMade) && (model == null || rightModel != null)) return;
        ShowOwnModel();
        if (model == null) return;
        var gun = held.transform;
        rightModel = ModelOnGun(model, gun, look, out rightMuzzleAt, out rightMuzzleTurn);
        rightSlideBone = FindSlide(gun, held.MuzzleRotation * Vector3.back, rightModel.transform);
        foreach (var renderer in held.GetComponentsInChildren<Renderer>(true))
        {
            if (renderer.transform.IsChildOf(rightModel.transform)) continue;
            renderer.forceRenderingOff = true;
            rightHidden.Add(renderer);
        }
        rightMade = wanted;
    }

    // The right gun shows its own model again.
    private void ShowOwnModel()
    {
        foreach (var renderer in rightHidden) if (renderer != null) renderer.forceRenderingOff = false;
        rightHidden.Clear();
        if (rightModel != null) Destroy(rightModel);
        rightModel = null;
        rightMade = default;
        rightSlideBone = default;
    }

    // A model in a gun's sight line (the right gun's measured muzzle turn, in the gun's space): the table's offset, turn
    // and size inside that frame, its grip meant to sit at the frame's origin where the gun's own grip is. Its muzzle is
    // then found on its own front face along that line (GunMuzzle), in the gun's space.
    private GameObject ModelOnGun(GameObject model, Transform gun, AkimboGuns.Look look, out Vector3 muzzleAt, out Quaternion muzzleTurn)
    {
        var frame = new GameObject("Akimbo Model").transform;
        frame.SetParent(gun, false);
        frame.localPosition = Vector3.zero;
        frame.localRotation = held.MuzzleRotation;
        var copy = StillCopy(model, frame);
        copy.transform.localPosition = look.modelOffset;
        copy.transform.localRotation = Quaternion.Euler(look.modelTurn);
        copy.transform.localScale = Vector3.one * Mathf.Max(.001f, look.modelScale);
        SetLayer(frame, gun.gameObject.layer);
        GunMuzzle.Measure(frame, null, muzzleDepth, out Vector3 at, out Quaternion turn);
        muzzleAt = gun.InverseTransformPoint(frame.TransformPoint(at));
        muzzleTurn = Quaternion.Inverse(gun.rotation) * frame.rotation * turn;
        return frame.gameObject;
    }

    // A still copy of a gun model under parent: its scripts, animators, physics, lights, cameras and sound taken off
    // before it ever wakes, as PickupProps does for a gun on the floor, and as FPSBody wakes KINEMATION's guns under a root
    // of their own. Its skinned parts keep their bones, so its slide can still move.
    private static GameObject StillCopy(GameObject source, Transform parent)
    {
        var holder = new GameObject("Akimbo Gun (building)");
        holder.SetActive(false);
        var copy = Instantiate(source, holder.transform, false);
        copy.name = source.name;
        foreach (var system in copy.GetComponentsInChildren<ParticleSystem>(true)) if (system != null) DestroyImmediate(system.gameObject);
        // Twice: a script another one requires goes on the second pass (a light's or a camera's URP data before them).
        for (int pass = 0; pass < 2; pass++)
            foreach (var script in copy.GetComponentsInChildren<MonoBehaviour>(true))
                if (script != null) DestroyImmediate(script);
        foreach (var part in copy.GetComponentsInChildren<Light>(true)) DestroyImmediate(part);
        foreach (var part in copy.GetComponentsInChildren<Camera>(true)) DestroyImmediate(part);
        foreach (var part in copy.GetComponentsInChildren<Animator>(true)) DestroyImmediate(part);
        foreach (var part in copy.GetComponentsInChildren<Joint>(true)) DestroyImmediate(part);
        foreach (var part in copy.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(part);
        foreach (var part in copy.GetComponentsInChildren<Collider>(true)) DestroyImmediate(part);
        foreach (var part in copy.GetComponentsInChildren<AudioSource>(true)) DestroyImmediate(part);
        foreach (var part in copy.GetComponentsInChildren<LineRenderer>(true)) DestroyImmediate(part);
        foreach (var part in copy.GetComponentsInChildren<TrailRenderer>(true)) DestroyImmediate(part);
        copy.transform.SetParent(parent, false);
        copy.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        copy.transform.localScale = Vector3.one;
        copy.SetActive(true);
        Destroy(holder);
        return copy;
    }

    // The part of a copy under `root` named Slider or Slide (KINEMATION's M1911 calls its slide bone Slider), with the way
    // back along the sight line in its parent's space for one unit of the root's; none when there is no such part. `within`
    // narrows the search to the pair's model in the right hand.
    private static Slide FindSlide(Transform root, Vector3 backInRoot, Transform within = null)
    {
        foreach (var part in (within != null ? within : root).GetComponentsInChildren<Transform>(true))
        {
            string name = part.name.ToLowerInvariant();
            if (part.parent == null || (name != "slider" && name != "slide" && !name.EndsWith("_slide") && !name.EndsWith(".slide"))) continue;
            return new Slide { Bone = part, Rest = part.localPosition, Back = part.parent.InverseTransformVector(root.TransformVector(backInRoot)) };
        }
        return default;
    }
}
