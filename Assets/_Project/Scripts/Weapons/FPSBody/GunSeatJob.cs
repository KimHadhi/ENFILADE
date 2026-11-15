using UnityEngine;
using UnityEngine.Animations;

/// <summary>
/// First in the chain of FPSAF's layers on our body (FPSBodyBoneController), before the Pose Sampler: the bones the Pose
/// Sampler places when it sets up for a gun and then reads back from the animation stream every frame (its weapon bone,
/// and the weapon bone in each hand, which is where the gun sits in that hand) are put in the stream where that setup left
/// them, every frame, while a gun is held. KINEMATION made its layers anew at every draw and Unity then re-bound the body's
/// Animator, which took those bones' new places in with it; the layers kept on our body never re-bind, so this puts them in.
/// The places are numbers kept from the gun's setup (FPSBodyBoneController.SetUp), the same every frame, as they were in
/// KINEMATION's stream (a bone no clip moves stands in the stream where the last re-bind found it). They are not read from
/// the scene every frame. Read from the scene (0.9.110) the seat was lost: every gun sat on the right hand bone itself, as
/// that hand's weapon bone is before any gun is seated in it, turned the hand bone's way with its muzzle toward the player
/// (the user, 2026-11-15: "왜 총을 들고나서 내 쪽으로 기괴하게 꺾여?"). Measured: Logs/gun-hold-report.txt had both hands
/// 138 to 178 degrees off every gun, and the hold check (GunHoldProbe) gives those very angles for a gun on the bare hand
/// bone (Herrington 178 and 161, MX68 148 and 155, MKR9 150 and 172), against 1 to 17 for a gun seated. Read in the code,
/// not watched in play: the Animator writes its stream back onto these bones every frame, and FPSAF's blend at a draw
/// (AnimationBlendingJob, last in the chain) writes every rig bone, these among them, on its way from the pose before the
/// draw. A seat read back from the scene is then that blend's own last result, which starts at the old pose and so never
/// leaves it: for a first draw that is the bare hand bone. (Since 0.9.119 the blend at a draw is ours and leaves the gun's
/// bones alone, BodyCrossFadeJob; the Animator still writes them back every frame, so the seat stays numbers.)
/// The bones are FPSAF's own (none of them is the body's, and nothing hangs under them), so moving them in the stream moves
/// nothing else.
/// </summary>
public struct GunSeatJob : IAnimationJob
{
    // One bone: put in the stream where the gun's setup left it under its parent.
    private struct Seat
    {
        private bool bound;
        private TransformStreamHandle stream;
        private Vector3 position;
        private Quaternion rotation;

        public Seat(Animator animator, Transform bone)
        {
            bound = bone != null;
            stream = bound ? animator.BindStreamTransform(bone) : default;
            position = Vector3.zero;
            rotation = Quaternion.identity;
        }

        public void Sit(Pose at)
        {
            position = at.position;
            rotation = at.rotation;
        }

        public void Take(AnimationStream animation)
        {
            if (!bound) return;
            stream.SetLocalPosition(animation, position);
            stream.SetLocalRotation(animation, rotation);
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

    /// <summary>The held gun's places for the three bones, each under its own parent (as its setup left them).</summary>
    public void Sit(Pose weaponAt, Pose rightAt, Pose leftAt)
    {
        weapon.Sit(weaponAt);
        right.Sit(rightAt);
        left.Sit(leftAt);
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
