using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using KINEMATION.FPSAnimationFramework.Runtime.Core;
using KINEMATION.FPSAnimationFramework.Runtime.Layers.PoseSamplerLayer;
using KINEMATION.FPSAnimationFramework.Runtime.Playables;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// FPSAF's bone controller on our body, made to keep each gun's layers rather than make them again at every draw.
/// KINEMATION's (FPSBoneController.LinkAnimationLayers) destroyed every layer job of the gun put away and made the drawn
/// gun's anew: each new job bound its bones again, the Pose Sampler sampled the gun's pose clip on the whole body, and the
/// new playables in the graph made Unity re-bind the BOOTH body's Animator (hundreds of bones). Every draw cost 25 to 50 ms
/// in the editor: Animator.SetupAvatarDataSet 10-27 ms, Animator.Initialize 6-18 ms, GameObject.SampleAnimation 6-16 ms
/// (Logs/Hitches, 2026-11-14: hub 323-342 s, chapter 2 69-83 s, one per draw). Most layers take their gun's settings only
/// when they are set up, so swapping settings into old layers would have held every gun with the last gun's.
/// Here a gun's layers are set up once on this body, KINEMATION's own way (Prepare: behind a loading screen for every gun
/// built there, else at the gun's first draw), and kept as they stood right after. The body has one chain of layer
/// playables, made once. A draw puts the drawn gun's kept layers into it, each a fresh copy, and does again what setting
/// them up did outside them: the bones it moved, the gun's seat in the hands (kept as numbers and put in the animation
/// stream every frame, GunSeatJob) and the gun's pose clip and mask on FPSAF's playables. So every layer of a
/// draw starts where KINEMATION's new one started, and the graph keeps its shape. KINEMATION's order and timing stay: the
/// blend from the last pose, the entity update, the layer links that wait for the new layers.
/// With no gun held the chain stays, its layers at weight 0, which leaves the body's bones as they are.
/// </summary>
public sealed class FPSBodyBoneController : FPSBoneController
{
    // A gun's layers as they stood right after they were set up on this body, and what setting them up did outside them:
    // the transforms it moved (where it left them) and its calls on FPSAF's playables, in order.
    private sealed class Chain
    {
        public IAnimationLayerJob[] fresh;
        public Placed[] placed;
        public List<Action<IPlayablesController>> calls;
        // The Pose Sampler's own bones, and where its setup left each under its parent: where the gun sits in each hand
        // (GunSeatJob puts them in the stream there).
        public Transform weaponBone, weaponBoneRight, weaponBoneLeft;
        public Pose weaponSeat, rightSeat, leftSeat;

        // What KINEMATION's new layers left in the scene and on the playables as they were made at a draw.
        public void Replay(IPlayablesController playables)
        {
            foreach (var moved in placed)
                if (moved.part != null) moved.part.SetLocalPositionAndRotation(moved.position, moved.rotation);
            if (calls != null)
                foreach (var call in calls) call(playables);
        }

        // Each job's native data once: the copies in play share it.
        public void Destroy()
        {
            foreach (var job in fresh) job.Destroy();
        }
    }

    private readonly struct Placed
    {
        public readonly Transform part;
        public readonly Vector3 position;
        public readonly Quaternion rotation;

        public Placed(Transform part, Vector3 position, Quaternion rotation)
        {
            this.part = part;
            this.position = position;
            this.rotation = rotation;
        }
    }

    private readonly Dictionary<FPSAnimatorProfile, Chain> chains = new();
    // The chain the layers run in: FPSAF's IK targets (its virtual elements), the seat, one playable per layer in the
    // profile's order, FPSAF's blend over the last.
    private readonly List<AnimationScriptPlayable> slots = new();
    private readonly List<Type> slotTypes = new();
    private AnimationScriptPlayable seatPlayable;
    private GunSeatJob seat;
    private Transform seatWeapon, seatRight, seatLeft;
    private HeldCalls held;
    // A layer's data couldn't be read or written as its own type here (JobType): this body holds its guns KINEMATION's way.
    private bool fallback;

    /// <summary>
    /// Sets <paramref name="profile"/>'s layers up on this body now, if they aren't yet, and leaves everything as it was:
    /// what the setup moves is put back, and its calls on FPSAF's playables wait for the gun's draw.
    /// </summary>
    public void Prepare(FPSAnimatorProfile profile)
    {
        if (fallback || profile == null || !profile.IsValid()) return;
        var chain = ChainOf(profile);
        // The first gun set up makes the chain too, seat bound, quiet until a gun is held: then no draw binds anything.
        if (chain != null && slots.Count == 0)
        {
            BuildSlots(chain.fresh);
            Seat(chain, false);
            Quiet();
        }
    }

    /// <summary>
    /// A gun drawn: KINEMATION's LinkAnimatorProfile as it is, its layers made by HoldLayers. The blend from the pose so far
    /// is cached at once and the layers go in on the next frame, or at once with nothing held before or no blend.
    /// </summary>
    public void Hold(FPSAnimatorProfile newProfile)
    {
        if (fallback)
        {
            LinkAnimatorProfile(newProfile);
            return;
        }
        if (newProfile == null)
        {
            Debug.LogWarning("FPSBoneController: Profile is null, use UnlinkAnimatorProfile instead");
            return;
        }
        if (newProfile.Equals(_newProfile)) return;
        if (!newProfile.IsValid())
        {
            LetGo();
            return;
        }
        _newProfile = newProfile;
        if (_activeProfile == null || Mathf.Approximately(newProfile.blendInTime, 0f))
        {
            HoldLayers();
            _blendingJob.cachePose = false;
            _blendingJob.blendTime = 0f;
            _blendingPlayable.SetJobData(_blendingJob);
            return;
        }
        var job = _blendingPlayable.GetJobData<AnimationBlendingJob>();
        _blendingJob.cachePose = true;
        job.cachePose = true;
        job.blendTime = newProfile.blendInTime;
        job.easeMode = newProfile.easeMode;
        _blendingPlayable.SetJobData(job);
    }

    /// <summary>The gun put away: KINEMATION's UnlinkAnimatorProfile as it is, its layers let go by LetGoLayers.</summary>
    public void LetGo()
    {
        if (fallback)
        {
            UnlinkAnimatorProfile();
            return;
        }
        if (_activeProfile == null) return;
        _newProfile = null;
        if (Mathf.Approximately(_activeProfile.blendOutTime, 0f))
        {
            _activeProfile = null;
            LetGoLayers();
            _blendingJob.cachePose = false;
            _blendingJob.blendTime = 0f;
            _blendingPlayable.SetJobData(_blendingJob);
            return;
        }
        var job = _blendingPlayable.GetJobData<AnimationBlendingJob>();
        _blendingJob.cachePose = true;
        job.cachePose = true;
        job.blendTime = _activeProfile.blendInTime;
        _blendingPlayable.SetJobData(job);
    }

    // KINEMATION's UpdateController, with its layers made and let go by HoldLayers and LetGoLayers.
    public override void UpdateController()
    {
        if (fallback)
        {
            base.UpdateController();
            return;
        }
        if (_linkLayers)
        {
            if (_newProfile == null) LetGoLayers();
            else HoldLayers();
            _linkLayers = false;
        }
        int count = _animationLayers.Count;
        for (int i = 0; i < count; i++) _animationLayers[i].job.OnPreGameThreadUpdate();
        for (int i = 0; i < count; i++)
        {
            var layer = _animationLayers[i];
            layer.job.UpdatePlayableJobData(layer.playable, GetLayerWeight(layer.job.GetSettingAsset()));
        }
    }

    /// <summary>
    /// The body is going: every kept gun's layers free their native data, and the chain goes. Called before FPSAF's own
    /// Dispose (FPSBodyAnimator.OnDestroy), which then finds no layers of ours to destroy twice.
    /// </summary>
    public void Release()
    {
        if (fallback) return;
        foreach (var chain in chains.Values) chain.Destroy();
        chains.Clear();
        _animationLayers?.Clear();
        DropSlots();
    }

    // KINEMATION's LinkAnimationLayers, making nothing: what setting the drawn gun's layers up did outside them is done
    // again, then fresh copies of its kept layers, told which gun they hold (UpdateEntity, as KINEMATION's new ones were),
    // go into the chain, and the layer links that waited for them go in.
    private void HoldLayers()
    {
        var chain = ChainOf(_newProfile);
        if (chain == null)
        {
            LinkAnimationLayers();
            return;
        }
        _activeProfile = _newProfile;
        chain.Replay(_playablesController);
        int count = chain.fresh.Length;
        var jobs = new IAnimationLayerJob[count];
        for (int i = 0; i < count; i++)
        {
            // A boxed copy of the kept job (its native data is shared and only read): the kept one stays as set up.
            jobs[i] = (IAnimationLayerJob)RuntimeHelpers.GetObjectValue(chain.fresh[i]);
            jobs[i].UpdateEntity(_entity);
        }
        if (Fits(jobs))
            for (int i = 0; i < count; i++) JobType.Of(slotTypes[i]).write(slots[i], jobs[i]);
        else BuildSlots(jobs);
        Seat(chain, true);
        _animationLayers.Clear();
        for (int i = 0; i < count; i++) _animationLayers.Add(new AnimationLayer { job = jobs[i], playable = slots[i] });
        foreach (var setting in _settingsToLink) LinkAnimatorLayer(setting);
        _settingsToLink.Clear();
    }

    // KINEMATION's UnlinkAnimationLayers, destroying nothing: the layers in play are let go and the chain goes quiet.
    private void LetGoLayers()
    {
        _animationLayers.Clear();
        Quiet();
    }

    private Chain ChainOf(FPSAnimatorProfile profile)
    {
        if (chains.TryGetValue(profile, out var chain)) return chain;
        chain = SetUp(profile);
        if (chain != null) chains[profile] = chain;
        return chain;
    }

    // KINEMATION's setting up of a profile's layers (each job made and Initialized, as LinkAnimationLayers does), with the
    // body's transforms put back after it and its calls on FPSAF's playables held, so it changes nothing now. Null when
    // this body can't keep layers (fallback).
    private Chain SetUp(FPSAnimatorProfile profile)
    {
        var owner = _layerJobData.animator != null ? _layerJobData.animator.transform : transform;
        var parts = owner.GetComponentsInChildren<Transform>(true);
        var before = new Pose[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            parts[i].GetLocalPositionAndRotation(out var position, out var rotation);
            before[i] = new Pose(position, rotation);
        }
        held ??= new HeldCalls(_playablesController);
        var data = _layerJobData;
        data.playablesController = held;
        var made = new List<IAnimationLayerJob>();
        List<Action<IPlayablesController>> calls;
        Placed[] placed;
        var chain = new Chain();
        held.Hold();
        try
        {
            foreach (var setting in profile.settings)
            {
                var job = setting.CreateAnimationJob();
                if (job == null) continue;
                made.Add(job);
                job.Initialize(data, setting);
                // The gun's seat, kept as the Pose Sampler leaves it: its hands' weapon bones stand where the gun is in the
                // pose it has just stood the body in, and that is read now, before anything else moves a bone. The weapon
                // bone itself is kept where the Pose Sampler puts it every frame (PoseSamplerJob.UpdatePlayableJobData: the
                // gun's default pose from the body), which is what KINEMATION's re-bind read; its setup leaves it turned by
                // the gun's offset besides, for the IK weapon bone, and that turn was never in the stream.
                if (setting is PoseSamplerLayerSettings sampler && chain.weaponBoneRight == null)
                {
                    chain.weaponBone = _rigComponent.GetRigTransform(sampler.weaponBone);
                    chain.weaponBoneRight = _rigComponent.GetRigTransform(sampler.weaponBoneRight);
                    chain.weaponBoneLeft = _rigComponent.GetRigTransform(sampler.weaponBoneLeft);
                    chain.weaponSeat = SeatAt(chain.weaponBone, data.Owner.TransformPoint(sampler.defaultWeaponPose.position),
                        data.Owner.rotation * sampler.defaultWeaponPose.rotation);
                    chain.rightSeat = SeatOf(chain.weaponBoneRight);
                    chain.leftSeat = SeatOf(chain.weaponBoneLeft);
                }
            }
        }
        catch
        {
            foreach (var job in made) job.Destroy();
            throw;
        }
        finally
        {
            calls = held.Release();
            placed = PutBack(parts, before);
        }

        var fresh = made.ToArray();
        try
        {
            // From here on each job holds FPSAF's playables themselves, as KINEMATION's own do; the held ones only stood
            // in while it set up.
            foreach (var job in fresh) JobType.Of(job.GetType()).SetPlayables(job, _playablesController);
        }
        catch (Exception e)
        {
            foreach (var job in fresh) job.Destroy();
            if (chains.Count > 0 || slots.Count > 0) throw;
            fallback = true;
            Debug.LogWarning($"FPSBodyBoneController: FPSAF's layers can't be kept on {owner.name} ({e.Message}); its guns are drawn "
                + "KINEMATION's way, each draw re-binding the body.", this);
            return null;
        }

        chain.fresh = fresh;
        chain.placed = placed;
        chain.calls = calls;
        return chain;
    }

    // Where a bone stands under its parent now.
    private static Pose SeatOf(Transform bone)
    {
        if (bone == null) return Pose.identity;
        bone.GetLocalPositionAndRotation(out var position, out var rotation);
        return new Pose(position, rotation);
    }

    // A place in the world as a bone would have it under its parent.
    private static Pose SeatAt(Transform bone, Vector3 position, Quaternion rotation)
    {
        if (bone == null) return Pose.identity;
        var parent = bone.parent;
        return parent == null ? new Pose(position, rotation)
            : new Pose(parent.InverseTransformPoint(position), Quaternion.Inverse(parent.rotation) * rotation);
    }

    // The transforms setting up moved, each where it was left; each is put back where it was before.
    private static Placed[] PutBack(Transform[] parts, Pose[] before)
    {
        var placed = new List<Placed>();
        for (int i = 0; i < parts.Length; i++)
        {
            var part = parts[i];
            if (part == null) continue;
            part.GetLocalPositionAndRotation(out var position, out var rotation);
            if (Same(position, before[i].position) && Same(rotation, before[i].rotation)) continue;
            placed.Add(new Placed(part, position, rotation));
            part.SetLocalPositionAndRotation(before[i].position, before[i].rotation);
        }
        return placed.ToArray();
    }

    // The very same numbers (Unity's == on vectors and rotations lets small moves through).
    private static bool Same(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
    private static bool Same(Quaternion a, Quaternion b) => a.x == b.x && a.y == b.y && a.z == b.z && a.w == b.w;

    private bool Fits(IAnimationLayerJob[] jobs)
    {
        if (!seatPlayable.IsValid() || slots.Count != jobs.Length) return false;
        for (int i = 0; i < jobs.Length; i++)
            if (!slots[i].IsValid() || slotTypes[i] != jobs[i].GetType()) return false;
        return true;
    }

    // The chain, linked as KINEMATION links a profile's layers, with the seat first. Made once per body; made again only
    // for a profile whose layers are of other kinds than the chain's (a playable runs one kind of job).
    private void BuildSlots(IAnimationLayerJob[] jobs)
    {
        DropSlots();
        var graph = _playablesController.GetPlayableGraph();
        seat = default;
        seatWeapon = seatRight = seatLeft = null;
        seatPlayable = AnimationScriptPlayable.Create(graph, seat);
        seatPlayable.AddInput(_virtualElementPlayable, 0);
        var below = seatPlayable;
        foreach (var job in jobs)
        {
            var playable = job.CreatePlayable(graph);
            playable.AddInput(below, 0);
            slots.Add(playable);
            slotTypes.Add(job.GetType());
            below = playable;
        }
        _blendingPlayable.ConnectInput(0, below, 0, 1f);
        _proceduralOutput.SetSourcePlayable(_blendingPlayable);
    }

    private void DropSlots()
    {
        if (_blendingPlayable.IsValid() && _blendingPlayable.GetInput(0).IsValid()) _blendingPlayable.DisconnectInput(0);
        foreach (var slot in slots)
            if (slot.IsValid()) slot.Destroy();
        slots.Clear();
        slotTypes.Clear();
        if (seatPlayable.IsValid()) seatPlayable.Destroy();
    }

    // The seat on a gun's Pose Sampler bones (bound again only when they are other bones than the last gun's), with this
    // gun's places for them, on while it is held.
    private void Seat(Chain chain, bool on)
    {
        if (chain.weaponBone != seatWeapon || chain.weaponBoneRight != seatRight || chain.weaponBoneLeft != seatLeft)
        {
            seat = new GunSeatJob(_layerJobData.animator, chain.weaponBone, chain.weaponBoneRight, chain.weaponBoneLeft);
            seatWeapon = chain.weaponBone;
            seatRight = chain.weaponBoneRight;
            seatLeft = chain.weaponBoneLeft;
        }
        seat.Sit(chain.weaponSeat, chain.rightSeat, chain.leftSeat);
        seat.on = on;
        if (seatPlayable.IsValid()) seatPlayable.SetJobData(seat);
    }

    // No gun held: every layer in the chain at weight 0, where each one returns at once or moves its bones by nothing, as if
    // KINEMATION's chain were gone; the seat off.
    private void Quiet()
    {
        for (int i = 0; i < slots.Count; i++)
        {
            if (!slots[i].IsValid()) continue;
            var type = JobType.Of(slotTypes[i]);
            object data = type.read(slots[i]);
            type.SetWeight(data, 0f);
            type.write(slots[i], data);
        }
        seat.on = false;
        if (seatPlayable.IsValid()) seatPlayable.SetJobData(seat);
    }

    /// <summary>
    /// A layer job type, read and written on its playable as the type it is (AnimationScriptPlayable.GetJobData and SetJobData
    /// check the type the playable was made with), and its LayerJobData (the layer's weight, FPSAF's playables). Made once per
    /// type by reflection, which the editor and our Mono builds run; where it can't be made, the body falls back.
    /// </summary>
    private sealed class JobType
    {
        private static readonly Dictionary<Type, JobType> known = new();
        private static readonly MethodInfo SetMethod = typeof(JobType).GetMethod(nameof(SetTyped), BindingFlags.NonPublic | BindingFlags.Static);
        private static readonly MethodInfo GetMethod = typeof(JobType).GetMethod(nameof(GetTyped), BindingFlags.NonPublic | BindingFlags.Static);

        public readonly Action<AnimationScriptPlayable, object> write;
        public readonly Func<AnimationScriptPlayable, object> read;
        private readonly FieldInfo data;

        public static JobType Of(Type type)
        {
            if (!known.TryGetValue(type, out var found)) known[type] = found = new JobType(type);
            return found;
        }

        private JobType(Type type)
        {
            write = (Action<AnimationScriptPlayable, object>)SetMethod.MakeGenericMethod(type).CreateDelegate(typeof(Action<AnimationScriptPlayable, object>));
            read = (Func<AnimationScriptPlayable, object>)GetMethod.MakeGenericMethod(type).CreateDelegate(typeof(Func<AnimationScriptPlayable, object>));
            foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                if (field.FieldType == typeof(LayerJobData))
                {
                    data = field;
                    break;
                }
            if (data == null) throw new ArgumentException($"{type.Name} keeps no LayerJobData");
        }

        private static void SetTyped<T>(AnimationScriptPlayable playable, object job) where T : struct, IAnimationJob => playable.SetJobData((T)job);

        private static object GetTyped<T>(AnimationScriptPlayable playable) where T : struct, IAnimationJob => playable.GetJobData<T>();

        // On a boxed job: FieldInfo.SetValue changes the box itself.
        public void SetWeight(object job, float weight)
        {
            var value = (LayerJobData)data.GetValue(job);
            value.weight = weight;
            data.SetValue(job, value);
        }

        public void SetPlayables(object job, IPlayablesController playables)
        {
            var value = (LayerJobData)data.GetValue(job);
            value.playablesController = playables;
            data.SetValue(job, value);
        }
    }

    /// <summary>
    /// FPSAF's playables as the layers see them while they set up ahead of a draw: the calls that would change them (the
    /// Pose Sampler plays the gun's pose clip and sets its mask) wait, in order, and are made when the gun is drawn
    /// (Chain.Replay). The rest go through, and everything goes through when nothing is held.
    /// </summary>
    private sealed class HeldCalls : IPlayablesController
    {
        private readonly IPlayablesController playables;
        private List<Action<IPlayablesController>> waiting;

        public HeldCalls(IPlayablesController playables) => this.playables = playables;

        public void Hold() => waiting = new List<Action<IPlayablesController>>();

        public List<Action<IPlayablesController>> Release()
        {
            var calls = waiting;
            waiting = null;
            return calls;
        }

        private bool Wait(Action<IPlayablesController> call)
        {
            if (waiting == null) return false;
            waiting.Add(call);
            return true;
        }

        public bool InitializeController() => playables.InitializeController();

        public void SetControllerWeight(float weight)
        {
            if (!Wait(p => p.SetControllerWeight(weight))) playables.SetControllerWeight(weight);
        }

        public bool PlayPose(FPSAnimationAsset asset)
            => Wait(p => p.PlayPose(asset)) ? asset != null && asset.clip != null : playables.PlayPose(asset);

        public bool PlayAnimation(FPSAnimationAsset asset, float startTime = 0f)
            => Wait(p => p.PlayAnimation(asset, startTime)) ? asset != null && asset.clip != null : playables.PlayAnimation(asset, startTime);

        public void UpdateAnimatorController(RuntimeAnimatorController newController)
        {
            if (!Wait(p => p.UpdateAnimatorController(newController))) playables.UpdateAnimatorController(newController);
        }

        public void UpdateAvatarMask(AvatarMask newMask)
        {
            if (!Wait(p => p.UpdateAvatarMask(newMask))) playables.UpdateAvatarMask(newMask);
        }

        public void StopAnimation(float blendOutTime)
        {
            if (!Wait(p => p.StopAnimation(blendOutTime))) playables.StopAnimation(blendOutTime);
        }

        public bool IsPlaying() => playables.IsPlaying();
        public float GetCurveValue(string curveName, bool isAnimator = false) => playables.GetCurveValue(curveName, isAnimator);
        public Animator GetAnimator() => playables.GetAnimator();
        public PlayableGraph GetPlayableGraph() => playables.GetPlayableGraph();
        public void RebuildPlayables() => playables.RebuildPlayables();

        // The editor's preview members of IPlayablesController (KINEMATION declares them only in the editor). Nothing previews
        // through this stand-in; declared always, so it fits the interface built either way.
        public void StartEditorPreview() { }
        public void StopEditorPreview() { }
    }
}
