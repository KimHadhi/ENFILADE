using System.Collections.Generic;
using KINEMATION.CharacterAnimationSystem.Integrations.FPSAnimationPack.Scripts;
using KINEMATION.FPSAnimationFramework.Runtime.Core;
using KINEMATION.FPSAnimationFramework.Runtime.Layers.AdditiveLayer;
using KINEMATION.FPSAnimationFramework.Runtime.Layers.AdsLayer;
using KINEMATION.FPSAnimationFramework.Runtime.Layers.AttachHandLayer;
using KINEMATION.FPSAnimationFramework.Runtime.Layers.LookLayer;
using KINEMATION.FPSAnimationFramework.Runtime.Layers.PoseSamplerLayer;
using KINEMATION.FPSAnimationFramework.Runtime.Layers.SwayLayer;
using KINEMATION.FPSAnimationFramework.Runtime.Playables;
using KINEMATION.FPSAnimationPack.Scripts.Sounds;
using KINEMATION.FPSAnimationPack.Scripts.Weapon;
using KINEMATION.ProceduralRecoilAnimationSystem.Runtime;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using KINEMATION.Shared.KAnimationCore.Runtime.Input;
using UnityEngine;

// FPSBody: Setting FPSAF up on the body (the demo's FPSHumanoidPlayer, as FPSAF's wizard builds it). One part of the class; the rest is in FPSBody.cs and the files beside it.
public sealed partial class FPSBody
{
    private void Install(Animator bodyAnimator)
    {
        Forget();
        animator = bodyAnimator;
        // PlayerBody has put the body's clips into the Animator's graph first; FPSAF takes them as the Animator output.
        if (animator == null || !animator.playableGraph.IsValid() || body.IsLimp) { animator = null; return; }
        // The gun clips made for this body keep KINEMATION's events.
        KinemationClipEvents.On(animator.gameObject);
        rig = FPSBodyRig.Build(animator, assets.rig);
        if (rig == null)
        {
            Report($"{animator.name} is not a humanoid body FPSAF can hold guns with.");
            return;
        }
        // FPSAF's Ads lines the sight up with the camera socket: the eyes, where the camera rides.
        rig.Camera.position = body.EyePosition;

        var host = animator.gameObject;
        input = host.AddComponent<UserInputController>();
        input.inputConfig = assets.inputConfig;
        playables = host.AddComponent<FPSPlayablesController>();
        playables.upperBodyMask = assets.upperBody;
        host.AddComponent<FPSBoneController>();
        recoil = host.AddComponent<RecoilAnimation>();
        if (view != null)
        {
            bodyCamera = view.GetComponent<FPSBodyCamera>();
            if (bodyCamera == null) bodyCamera = view.gameObject.AddComponent<FPSBodyCamera>();
            // Switched off: FPSAnimator calls it; its own Update would put the camera back where it first was.
            bodyCamera.enabled = false;
            bodyCamera.Bind(CameraPlaced);
        }
        framework = host.AddComponent<FPSBodyAnimator>();
        // Set up now rather than on its first frame, so FPSAF's outputs follow the body's clips in the Animator's graph.
        framework.Begin(assets.idleProfile, bodyCamera, AfterHands);
        // FPSAF's master mixer now plays the body's clips under its upper-body layer; the body's own output steps back.
        body.HandClipsToFramework();
        // Nothing plays in FPSAF's upper-body layer until a gun is drawn, and an empty layer at full weight would pull the
        // upper body to its bind pose; the gun brings it in.
        playables.SetControllerWeight(0f);
        hands = GetComponent<PlayerBodyHands>();
        foreach (var entry in assets.guns) AddGun(entry);
    }

    // The body went (a new skin, a rebuild): what was set up on it went with it.
    private void Forget()
    {
        guns.Clear();
        held = null;
        shownData = null;
        linked = aiming = firing = highReady = hasLook = hasGunInView = false;
        fingers.Clear();
        fingerGrip.Clear();
        holdWeight = cover = 0f;
        ForgetContacts();
        ForgetCloseHold();
        rig = null; input = null; playables = null; framework = null; recoil = null;
        if (body != null) body.UpperBodyFromFramework = false;
        if (bodyCamera != null) bodyCamera.SetGunWeight(0f);
        ForgetMade();
    }

    private void ForgetMade()
    {
        foreach (var copy in made) if (copy != null) Destroy(copy);
        made.Clear();
    }

    // One of KINEMATION's guns into the IK weapon bone, as the demo's FPSController spawns its weapons, with its
    // FPS Animation Pack scripts made quiet (their data stays; FPSAF plays them).
    private void AddGun(FPSBodyAssets.Gun entry)
    {
        if (entry.weapon == null || entry.prefab == null || entry.profile == null) return;
        var bakes = GunBakes.Instance;
        if (bakes == null || !bakes.TryGet(body.BodyModel, entry.weapon, out var bake, out bool exact))
        {
            Report($"No clips of KINEMATION's {entry.weapon.name} were made for the bodies yet: Unity's editor makes them with Retarget Pro "
                + "(ENFILADE → 총 → KINEMATION 총 클립 다시 굽기).");
            return;
        }
        // Any body but the reference plays the reference's humanoid clips, with the gun fitted to its own eyes (FittedGunPose).

        // Woken under a root of its own: KINEMATION's gun script looks up its sound source from transform.root when it
        // wakes (even switched off), and under the player it would find our footstep or voice source and play its own
        // baked reload sounds over ours from the clip's events. With nothing to find, it stays silent.
        var holder = new GameObject(name + " gun (building)");
        holder.SetActive(false);
        var instance = Instantiate(entry.prefab, holder.transform, false);
        instance.name = entry.prefab.name;
        var kinemation = instance.GetComponent<CasFPSWeapon>();
        // The gun-part clips carry KINEMATION's events too.
        foreach (var parts in instance.GetComponentsInChildren<Animator>(true)) KinemationClipEvents.On(parts.gameObject);
        // FPS Animation Pack's own weapon scripts expect its own player.
        foreach (var leftover in instance.GetComponentsInChildren<FPSWeaponSound>(true)) DestroyImmediate(leftover);
        foreach (var leftover in instance.GetComponentsInChildren<FPSWeapon>(true)) DestroyImmediate(leftover);
        // So does the PDW90's see-through magazine (its demo Pdw90Animation wants the pack's weapon script and an Animator graph
        // our guns don't have, and threw at every frame): ours shows the rounds instead (PackMagazineRounds).
        foreach (var demo in instance.GetComponentsInChildren<Pdw90Animation>(true)) PackMagazineRounds.Replace(demo, entry.weapon);
        if (kinemation != null) kinemation.enabled = false;
        SetLayer(instance.transform, weaponRoot.gameObject.layer);
        holder.SetActive(true);
        instance.transform.SetParent(rig.IkWeaponBone, false);
        instance.transform.localPosition = Vector3.zero;
        instance.transform.localRotation = Quaternion.identity;
        instance.transform.localScale = Vector3.one;
        Destroy(holder);

        var gun = instance.AddComponent<FPSGun>();
        Quaternion gunFrame = GunFrameOf(kinemation);
        // Where the Pose Sampler starts the gun: the bake's place (from its own eyes on any body but the reference), brought
        // within this body's reach (FitToReach).
        var start = exact || !bake.hasEyeFrame ? new KTransform(bake.gunPosition, bake.gunRotation, Vector3.one) : FittedGunPose(bake);
        var reach = FitToReach(entry, bake, gunFrame, start);
        start.position += reach.Move;
        var profile = ProfileFor(entry.profile, entry.weapon, bake, gunFrame, start);
        // A shell reload's starts and shells hand over to its next step, so only its end settles back into the hold.
        bool shells = entry.weapon.ShellReload;
        gun.Set(entry, kinemation, muzzleDepth, profile,
            shells ? Copy(entry.reloadTactical, bake.reloadTactical) : Settled(Copy(entry.reloadTactical, bake.reloadTactical)),
            shells ? Copy(entry.reloadEmpty, bake.reloadEmpty) : Settled(Copy(entry.reloadEmpty, bake.reloadEmpty)), gunFrame,
            Copy(entry.reloadShell, bake.reloadShell), Settled(Copy(entry.reloadEnd, bake.reloadEnd)));
        gun.SetGrip(bake, exact);
        gun.SetReachSlide(reach.Slide, reach.SlideAt, reachSlideFade);
        // Until FPSAF has held it on this body, the gun is held where the bake put it from the eyes (within this body's
        // reach), so a draw that follows KINEMATION's own clip (FPSBody.Contacts) comes up into a hold even the first time.
        // The IK weapon bone carries the gun's frame on KINEMATION's weapon bone.
        if (bake.hasEyeFrame) gun.SeedHold(bake.gunFromEyes + reach.MoveFromEyes, bake.gunTurnFromEyes * gunFrame);
        // This PC's own player's guns carry their kill counters (GunKills) and the skins picked on the title screen; a
        // friend's copy wears what their PC sent (WearSkins).
        bool mine = movement == null || (movement.IsLocal && !movement.IsNetworkDriven);
        if (mine) GunKillPlate.On(gun);
        GunSkins.Wear(instance, entry.weapon, mine ? GunSkins.ChosenFor(entry.weapon) : GunSkins.SkinIn(skinLook, entry.weapon));
        // Drawn at the clip's own pace: squeezed into the gun's Draw Duration it ran at up to three times its speed and looked
        // fast-forwarded (user, 2026-11-09). The gun is ready at Draw Duration all the same, and using it then cuts the rest
        // of the draw short (CutDraw). Put away in its Holster Duration, looked over at the clip's pace.
        gun.SetActions(Copy(entry.draw, bake.draw),
            Fitted(Copy(entry.holster, bake.holster), entry.weapon.HolsterDuration), Settled(Copy(entry.inspect, bake.inspect)));
        // A gun worked by hand after every shot (a bolt, a pump) has that clip made for this body too (FPSGun.Cycle), and the
        // rules learn when in it the hands are back, to hold the next shot until then (FPSBody.MeasureCycle, WeaponCycle).
        gun.SetCycle(Copy(entry.fire, bake.fire));
        MeasureCycle(gun);
        // From this body's profile: a stand-in's share has gone to the bone below it.
        gun.LeanChain = LeanChainOf(profile);
        // This gun's own copies of the layers that move it about, turned down as it is aimed (FPSGun.Steady).
        foreach (var layer in profile.settings)
        {
            if (layer is SwayLayerSettings || layer is AdditiveLayerSettings)
                gun.FeelLayers.Add((layer, layer.alpha, layer is AdditiveLayerSettings));
            else if (layer is GunConvergeLayerSettings converge) gun.Converge = converge;
            else if (layer is AdsLayerSettings ads) gun.Ads = ads;
        }
        // Where this gun's reload clips move the magazine and bolt, for WeaponFeedback's reload sounds (sound session). A
        // shell reload has a sound per step instead (WeaponData's reload sounds).
        if (kinemation != null && !shells) ReloadMoments.Measure(entry.weapon, kinemation);
        instance.SetActive(false);
        guns.Add(gun);
    }

    // KINEMATION's weapon bone (ik_hand_gun) has its axes a quarter turn from the gun's: its FPS Animation Pack turns the
    // bone by the gun's rotationOffset (FPSProceduralJob) before anything moves it, and the gun model hangs in that turned
    // frame. Without it the gun stood straight up in the hands (user, 2026-11-07: "high ready").
    public static Quaternion GunFrameOf(CasFPSWeapon kinemation)
        => kinemation != null && kinemation.weaponSettings != null ? kinemation.weaponSettings.rotationOffset : Quaternion.Euler(90f, 0f, 0f);

    // The demo's profile for this body: its Pose Sampler samples the pose made for this body and starts the gun where
    // Retarget Pro put it, KINEMATION's weapon bone. FPSAF's IK weapon bone, which the gun hangs from and every layer moves
    // (aim, sway, recoil, IK motions), takes the gun's own frame through the Pose Sampler's weapon bone offset, as FPSAF
    // expects: its demo's weapon bone faces the gun's way.
    // FPSAF's Ads brings the sight up at our weapon's aim time: our rule (WeaponController.AimBlend) zooms the view and
    // tightens the spread over AdsTransitionTime, and the demo's aimingSpeed (1.8 a second, 0.56 s) left the gun 0.4 s
    // behind the zoom. Before KINEMATION the gun rose with the rule (user picked it back, 2026-11-07).
    private FPSAnimatorProfile ProfileFor(FPSAnimatorProfile source, WeaponData weapon, GunBakes.Entry bake, Quaternion gunFrame, KTransform start)
    {
        var profile = Instantiate(source);
        Keep(profile);
        profile.settings = new List<FPSAnimatorLayerSettings>(source.settings.Count);
        foreach (var layer in source.settings)
        {
            // FPSAF's Attach Hand holds the left hand by a pose measured against the weapon bone (WeaponBone) when the
            // profile links, at full weight unless the clips carry a MaskAttachHand curve. Ours carry none and never move
            // that bone: it pulled the left hand 4 to 8 cm off the gun and laid its fingers flat (user, 2026-11-07). The
            // clips made for this body already hold the left hand on the gun (Retarget Pro's FPS feature), reloads too.
            if (layer is AttachHandLayerSettings) continue;
            if (layer is PoseSamplerLayerSettings sampler)
            {
                var mine = Instantiate(sampler);
                Keep(mine);
                mine.poseToSample = Copy(sampler.poseToSample, bake.pose);
                // The bake's place, or its place from the eyes on another body, within this body's reach (AddGun, FitToReach).
                mine.defaultWeaponPose = start;
                mine.weaponBoneOffset = new KTransform(Vector3.zero, gunFrame, Vector3.one);
                mine.overwriteWeaponBone = true;
                profile.settings.Add(mine);
            }
            else if (layer is LookLayerSettings look)
                profile.settings.Add(LookFor(look));
            else if (layer is AdsLayerSettings ads && weapon != null)
            {
                // The cheek weld goes just before Ads: the head comes to a shouldered gun before the sight is lined up.
                if (cheekWeld > 0f) profile.settings.Add(CheekWeldFor(ads));
                var mine = Instantiate(ads);
                Keep(mine);
                mine.aimingSpeed = 1f / weapon.AdsTransitionTime;
                // Down the sights the sight line goes through the eyes exactly. KINEMATION's Ads keeps positionBlend and
                // rotationBlend (0.21-0.25 in the pistol profile, 0.38-0.41 in the rifle's) of the gun's offset from the
                // eyes as it was when the profile linked: the last frame of whatever was held before, the view's pitch and
                // all, while Ads itself works before Look. So each draw left the sight line somewhere else, 1-5 cm off the
                // eyes: drawn after the MKR9, the R08's ran 2.2 cm above them, and its micro dot shows the dot only with the
                // eye within 1.5 cm of that line (user, 2026-11-12: "R08 리볼버 조준경 줌 했을 때 안 보임. 도트 사이트 고장난 듯").
                mine.positionBlend = default;
                mine.rotationBlend = default;
                profile.settings.Add(mine);
                // Then the barrel turns onto the crosshair's point, before sway and recoil go on top (GunConvergeLayer).
                profile.settings.Add(ConvergeFor(ads));
            }
            else if (layer is SwayLayerSettings || layer is AdditiveLayerSettings)
            {
                // Copies of their own: their alpha is turned down while aiming every frame (FPSGun.Steady), and the shared
                // asset must keep KINEMATION's.
                var mine = Instantiate(layer);
                Keep(mine);
                profile.settings.Add(mine);
            }
            else profile.settings.Add(layer);
        }
        return profile;
    }

    // FPSAF's Look shares the view's pitch, turn and lean down the spine by bone; a body without one of them (Hiro and Sil
    // have no UpperChest) has an empty stand-in there, and its share turned nothing: the gun followed a small turn only two
    // thirds of the way, and a lean fell a third short (probe, 2026-11-08). Here a missing bone's share goes to the one below.
    private LookLayerSettings LookFor(LookLayerSettings source)
    {
        var look = Instantiate(source);
        Keep(look);
        Fold(look.pitchOffsetElements);
        Fold(look.yawOffsetElements);
        Fold(look.rollOffsetElements);
        // A peek tips the body from the waist up: the hips' share of the lean goes to the bone above, so the pelvis and
        // the legs standing on it stay put. With the lean now as deep as the peek needs, the hips' share swung the feet
        // (user, 2026-11-08).
        var roll = look.rollOffsetElements;
        for (int i = 0; i + 1 < roll.Count; i++)
        {
            if (roll[i].rigElement.name != "Hips") continue;
            var hipsShare = roll[i];
            var above = roll[i + 1];
            above.clampedAngle += hipsShare.clampedAngle;
            above.cachedClampedAngle = above.clampedAngle;
            roll[i + 1] = above;
            hipsShare.clampedAngle = hipsShare.cachedClampedAngle = Vector2.zero;
            roll[i] = hipsShare;
            break;
        }
        return look;

        void Fold(List<LookLayerElement> elements)
        {
            for (int i = 0; i < elements.Count; i++)
            {
                if (rig.IsBodyBone(rig.Component.GetRigTransform(elements[i].rigElement))) continue;
                var standIn = elements[i];
                for (int j = i - 1; j >= 0; j--)
                {
                    if (!rig.IsBodyBone(rig.Component.GetRigTransform(elements[j].rigElement))) continue;
                    var below = elements[j];
                    below.clampedAngle += standIn.clampedAngle;
                    below.cachedClampedAngle = below.clampedAngle;
                    elements[j] = below;
                    break;
                }
                standIn.clampedAngle = standIn.cachedClampedAngle = Vector2.zero;
                elements[i] = standIn;
            }
        }
    }

    // Our cheek weld layer (CheekWeldLayerSettings), on the same rig and blended out by the same full-body moves as Ads.
    private CheekWeldLayerSettings CheekWeldFor(AdsLayerSettings ads)
    {
        var weld = ScriptableObject.CreateInstance<CheekWeldLayerSettings>();
        Keep(weld);
        weld.rigAsset = ads.rigAsset;
        weld.curveBlending = new List<CurveBlend>(ads.curveBlending);
        weld.share = cheekWeld;
        weld.limits = cheekWeldLimits;
        return weld;
    }

    // Our layer that turns the gun onto the crosshair's point (GunConvergeLayerSettings), on the same rig and blended out by
    // the same full-body moves as Ads.
    private GunConvergeLayerSettings ConvergeFor(AdsLayerSettings ads)
    {
        var converge = ScriptableObject.CreateInstance<GunConvergeLayerSettings>();
        Keep(converge);
        converge.rigAsset = ads.rigAsset;
        converge.curveBlending = new List<CurveBlend>(ads.curveBlending);
        return converge;
    }

    // How FPSAF's Look shares a lean down this body's spine for a gun's profile: each roll element's bone and its part of
    // the whole (its angle at a 90° lean).
    private List<(Transform bone, float share)> LeanChainOf(FPSAnimatorProfile profile)
    {
        var chain = new List<(Transform bone, float share)>();
        if (profile == null || rig == null || rig.Component == null) return chain;
        foreach (var layer in profile.settings)
            if (layer is LookLayerSettings look)
            {
                foreach (var element in look.rollOffsetElements)
                    if (rig.Component.GetRigTransform(element.rigElement) is Transform bone)
                        chain.Add((bone, Mathf.Abs(element.clampedAngle.x) / 90f));
                break;
            }
        return chain;
    }

    // Where the gun sits in this body's model space in the gun's pose, on a body the clips weren't baked for (another skin, a
    // mod's character): as far from its own eyes, in metres in game, and as turned from where its hips face, as the bake put
    // it on the reference body (GunBakes.Entry.gunFromEyes). The pose clip is sampled on this body for it and put back.
    private KTransform FittedGunPose(in GunBakes.Entry bake)
    {
        var made = new KTransform(bake.gunPosition, bake.gunRotation, Vector3.one);
        if (animator == null || bake.pose == null || !EyeFrame(bake.pose, out Vector3 eyes, out Quaternion frame)) return made;
        float scale = Mathf.Max(1e-4f, ModelScale);
        return new KTransform(eyes + frame * (bake.gunFromEyes / scale), frame * bake.gunTurnFromEyes, Vector3.one);
    }

    // The eyes in this body's model space and the frame they look out of (ahead the way the hips face, up the model's up),
    // as the gun's pose clip stands the body: GunClipRetarget.EyeFrame's rule on the reference body.
    private bool EyeFrame(AnimationClip pose, out Vector3 eyes, out Quaternion frame)
    {
        eyes = default;
        frame = Quaternion.identity;
        var root = animator.transform;
        var head = animator.GetBoneTransform(HumanBodyBones.Head);
        var leftLeg = animator.GetBoneTransform(HumanBodyBones.LeftUpperLeg);
        var rightLeg = animator.GetBoneTransform(HumanBodyBones.RightUpperLeg);
        if (head == null || leftLeg == null || rightLeg == null) return false;
        var bones = root.GetComponentsInChildren<Transform>();
        var saved = new (Vector3 position, Quaternion rotation)[bones.Length];
        for (int i = 0; i < bones.Length; i++) saved[i] = (bones[i].localPosition, bones[i].localRotation);
        try
        {
            pose.SampleAnimation(animator.gameObject, 0f);
            Vector3 ahead = Vector3.Cross(root.InverseTransformPoint(rightLeg.position) - root.InverseTransformPoint(leftLeg.position), Vector3.up);
            if (ahead.sqrMagnitude < 1e-8f) return false;
            ahead.Normalize();
            frame = Quaternion.LookRotation(ahead, Vector3.up);
            Vector3 headAt = root.InverseTransformPoint(head.position);
            eyes = headAt + Vector3.up * .08f + ahead * .1f;
            var leftEye = animator.GetBoneTransform(HumanBodyBones.LeftEye);
            var rightEye = animator.GetBoneTransform(HumanBodyBones.RightEye);
            if (leftEye != null && rightEye != null)
            {
                Vector3 l = root.InverseTransformPoint(leftEye.position), r = root.InverseTransformPoint(rightEye.position);
                Vector3 middle = (l + r) * .5f, fromHead = middle - headAt;
                float apart = Vector3.Distance(l, r), above = fromHead.y, forward = Vector3.Dot(fromHead, ahead);
                if (apart > .015f && apart < .2f && above > -.05f && above < .25f && forward > .01f && forward < .25f) eyes = middle;
            }
            return true;
        }
        finally
        {
            for (int i = 0; i < bones.Length; i++)
                if (bones[i] != null) bones[i].SetLocalPositionAndRotation(saved[i].position, saved[i].rotation);
        }
    }

    // A holster clip played over the gun's own time for it (s). No time keeps the clip's own pace.
    private static FPSAnimationAsset Fitted(FPSAnimationAsset action, float seconds)
    {
        if (action != null && action.clip != null && seconds > .01f) action.blendTime.rateScale = action.clip.length / seconds;
        return action;
    }

    // A reload eases back into the hold over reloadSettle (user, 2026-11-08: the arms jumped from the reload to the hold).
    private FPSAnimationAsset Settled(FPSAnimationAsset reload)
    {
        if (reload != null) reload.blendTime.blendOutTime = Mathf.Max(reload.blendTime.blendOutTime, reloadSettle);
        return reload;
    }

    // FPSAF's animation asset (blend times, curves) with the clip made for this body.
    private FPSAnimationAsset Copy(FPSAnimationAsset source, AnimationClip clip)
    {
        if (source == null || clip == null) return null;
        var copy = Instantiate(source);
        Keep(copy);
        copy.clip = clip;
        return copy;
    }

    private void Keep(Object copy)
    {
        copy.hideFlags = HideFlags.DontSave;
        made.Add(copy);
    }

    private static void SetLayer(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        for (int i = 0; i < root.childCount; i++) SetLayer(root.GetChild(i), layer);
    }
}
