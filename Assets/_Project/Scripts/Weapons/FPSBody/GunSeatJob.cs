using UnityEngine;
using UnityEngine.Animations;

/// <summary>
/// First in the chain of FPSAF's layers on our body (FPSBodyBoneController), before the Pose Sampler: the bones the Pose
/// Sampler places when it sets up for a gun and then reads back from the animation stream every frame (its weapon bone,
/// and the weapon bone in each hand, which is where the gun sits in that hand) go from the scene into the stream, every
/// frame, while a gun is held. KINEMATION made its layers anew at every draw and Unity then re-bound the body's Animator,
/// which took those bones' new places in with it; the layers kept on our body never re-bind, so this takes them in.
/// The bones are FPSAF's own (none of them is the body's, and nothing hangs under them), so moving them in the stream moves
/// nothing else.
/// </summary>
public struct GunSeatJob : IAnimationJob
{
    // One bone: read where the scene has it, written into the stream there.
    private struct Seat
    {
        private bool bound;
        private TransformSceneHandle scene;
        private TransformStreamHandle stream;

        public Seat(Animator animator, Transform bone)
        {
            bound = bone != null;
            scene = bound ? animator.BindSceneTransform(bone) : default;
            stream = bound ? animator.BindStreamTransform(bone) : default;
        }

        public void Take(AnimationStream animation)
        {
            if (!bound) return;
            stream.SetLocalPosition(animation, scene.GetLocalPosition(animation));
            stream.SetLocalRotation(animation, scene.GetLocalRotation(animation));
        }
    }

    private Seat weapon, right, left;

    /// <summary>A gun is held: its bones go into the stream. Off, the stream keeps whatever it has for them.</summary>
    public bool on;

    public GunSeatJob(Animator animator, Transform weaponBone, Transform weaponBoneRight, Transform weaponBoneLeft)
    {
        weapon = new Seat(animator, weaponBone);
        right = new Seat(animator, weaponBoneRight);
        left = new Seat(animator, weaponBoneLeft);
        on = false;
    }

    public void ProcessAnimation(AnimationStream stream)
    {
        if (!on) return;
        weapon.Take(stream);
        right.Take(stream);
        left.Take(stream);
    }

    public void ProcessRootMotion(AnimationStream stream) { }
}
