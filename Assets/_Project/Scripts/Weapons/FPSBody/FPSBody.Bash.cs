using UnityEngine;

/// <summary>
/// The gun bash: a shove with a gun out (V, the crowd shove) swings the gun itself, a long gun thrust across and canted so
/// its body leads, a pistol chopped down (the user, 2026-11-12: "밀치기 할 때 총기 휘두르는 거나 발차기 모션이 없어서 … 네가 1차 시안
/// 만들고 내가 그거 후보정"). The move is a GunMove (Resources/Combat/GunMoves, from the Blender shove workspace): how far the
/// gun moves and turns off where it is held, in its own frame. It goes on top of wherever FPSAF and the clips have the gun
/// this frame (aimed, swaying, mid-recoil), and the hands stay on it at their contacts (FPSBody.Contacts), so it fits any
/// character and any gun of its kind. The shove hits from the press (ShoveSweep, 0.2 s), so the drafts strike within a
/// few frames of it. A shot, a reload or the gun going away cuts it short and the gun eases back over bashBlendOut. Every PC
/// plays it (ShoveMoves.Bashed: the shover's own PC, and copies through PlayerNetworkCombat), so friends see the swing too.
/// </summary>
public sealed partial class FPSBody
{
    [SerializeField, Tooltip("The bash with a long gun: its file in Resources/Combat/GunMoves.")]
    private string bashLong = "enf_bash_long";
    [SerializeField, Tooltip("The bash with a pistol: its file in Resources/Combat/GunMoves.")]
    private string bashPistol = "enf_bash_pistol";
    [SerializeField, Min(.1f), Tooltip("How fast the bash plays (1: as made).")]
    private float bashSpeed = 1f;
    [SerializeField, Min(.01f), Tooltip("How quickly a bash cut short lets the gun go back (s).")]
    private float bashBlendOut = .1f;

    private ShoveMoves bashes;
    private GunMove bash;
    private float bashClock, bashWeight;
    private bool bashCut;

    // From Begin: every shove with a gun swings it, on this PC's player and on friends' copies alike.
    private void BeginBash()
    {
        bashes = movement != null ? ShoveMoves.For(movement) : null;
        if (bashes != null) bashes.Bashed += OnBashed;
    }

    private void EndBash()
    {
        if (bashes != null) bashes.Bashed -= OnBashed;
        bashes = null;
        bash = null;
    }

    private void OnBashed()
    {
        if (!HoldsGun) return;
        var move = GunMoves.Get(HeldPistol ? bashPistol : bashLong);
        if (move == null) return;
        bash = move;
        bashClock = 0f;
        bashWeight = 1f;
        bashCut = false;
    }

    /// <summary>A shot, a reload or the gun going away: the bash lets the gun go back over bashBlendOut.</summary>
    private void CutBash()
    {
        if (bash != null) bashCut = true;
    }

    // After the clips have placed the gun (FollowContacts), before the weapon root rides it and the hands take it (320).
    private void FollowBash()
    {
        if (bash == null) return;
        float dt = Time.deltaTime;
        if (bashCut) bashWeight = Mathf.MoveTowards(bashWeight, 0f, dt / bashBlendOut);
        else
        {
            bashClock += dt * bashSpeed;
            // Its last key is the hold again, so a bash played out ends where it began.
            if (bashClock >= bash.Length) bashWeight = 0f;
        }
        if (bashWeight <= 0f || !HoldsGun || rig == null)
        {
            bash = null;
            return;
        }
        bash.Sample(bashClock, out Vector3 move, out Quaternion turn);
        float weight = bashWeight * Framework;
        if (weight <= 0f) return;
        var gun = rig.IkWeaponBone;
        Quaternion hold = gun.rotation;
        gun.SetPositionAndRotation(gun.position + hold * (move * weight), hold * Quaternion.Slerp(Quaternion.identity, turn, weight));
    }
}
