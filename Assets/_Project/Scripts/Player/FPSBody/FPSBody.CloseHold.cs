using System;
using KINEMATION.Shared.KAnimationCore.Runtime.Core;
using UnityEngine;

/// <summary>
/// The close hold: a pistol pulled in to the chest and canted, as John Wick fights at arm's length (a center axis relock
/// hold), so it still fires however close an enemy is (the user, 2026-11-13: "권총은 아무리 가까워도 … 가슴쪽에 가까이 대는
/// 존 윅 자세 있지 않나? 그 자세 취해서 아무리 가까워도 총 쏠 수 있는 상태가 돼서 CQB하는 데 쓰이게"). Before it a zombie in the
/// face was a wall to the wall rule: the muzzle in its body, or the crosshair's point at or behind the muzzle, raised the gun
/// to high ready and it couldn't fire (WeaponAimSolver.IsObstructed).
/// No clip of it exists (the L4D2 Glock addons and KINEMATION's packs were looked through, 2026-11-13), so the pose is made
/// here, on top of wherever FPSAF and the clips have the gun this frame (sway, recoil and all), as the bash is
/// (FPSBody.Bash): the gun comes back toward the eyes and in toward the middle, its muzzle never nearer the eyes than
/// `nearest`; it rolls its top toward the support hand about its bore (`cant`); then its barrel is turned back onto the
/// crosshair's point about the muzzle, as GunConvergeLayer turns it. The hands stay on it at their contacts
/// (FPSBody.Contacts, PlayerBodyHands) and the elbows turn with the roll (CloseElbow): the gun hand's comes out to the side
/// and the support hand's goes under the gun. So it fits any character and any pistol, a modder's too.
/// WeaponController decides when (an enemy close before the eyes, or the usual hold blocked while this one is clear) and
/// fires from the muzzle to what the crosshair is on, as ever; held in, enemies never hold the gun back. Every PC runs the
/// rule on its own copy of the player and of the enemies, so friends see the same hold. Numbers: Tables/CloseHold.csv.
/// </summary>
public sealed partial class FPSBody
{
    /// <summary>Tables/CloseHold.csv (row Pistol): when a pistol comes in close and how it is held there.</summary>
    public const string CloseHoldTable = "CloseHold";
    private const string CloseHoldRow = "Pistol";

    [Serializable]
    private sealed class CloseHoldSettings
    {
        [Tooltip("Pistols (the pistol sound profile) come in to the chest when an enemy is close or the usual hold is blocked.")]
        public bool on = true;
        [Tooltip("Pistols that never come in (WeaponData names, the Weapons.csv ids).")]
        public string[] except = new string[0];
        [Min(0f), Tooltip("A living enemy's body this close to the eyes (m), before them, brings the gun in.")]
        public float foeNear = 1.2f;
        [Min(0f), Tooltip("Held in, the gun goes back out once no enemy is this close (m).")]
        public float foeLetGo = 1.5f;
        [Range(0f, 180f), Tooltip("Only enemies within this angle of the view count (degrees).")]
        public float foeCone = 60f;
        [Min(0f), Tooltip("The gun stays in this long after its reason went (s).")]
        public float linger = .35f;
        [Min(.005f), Tooltip("How fast it comes in: the half-life of the way left (s).")]
        public float inHalfLife = .035f;
        [Min(.005f), Tooltip("How fast it goes back out: the half-life of the way left (s).")]
        public float outHalfLife = .06f;
        [Tooltip("How far the gun comes back toward the eyes from its hip hold (m).")]
        public float back = .07f;
        [Tooltip("How much lower it is held (m).")]
        public float down = .02f;
        [Range(0f, 1f), Tooltip("How much of the muzzle's way off the middle of the view it comes in (0-1).")]
        public float inward = .5f;
        [Min(0f), Tooltip("The muzzle comes no nearer the eyes than this (m), so the gun stays out of the face and the chest.")]
        public float nearest = .2f;
        [Range(-90f, 90f), Tooltip("How far the gun rolls about its bore (degrees); positive lays its top toward the support hand.")]
        public float cant = 40f;
    }

    [SerializeField, Tooltip("The close hold's numbers (Tables/CloseHold.csv, row Pistol).")]
    private CloseHoldSettings closeHold = new CloseHoldSettings();
    // How far the gun is held in (0-1, eased), and whether WeaponController holds it in now.
    private float closeWeight;
    private bool closeWanted;
    // An enemy is looked at closely only if its root is within this much more than the reach of the eyes (m): a capsule's
    // middle is about a metre up from it, a brute's further.
    private const float FoeRootSlack = 3f;
    // An enemy without a capsule: a man's height and girth (m).
    private const float ManHeight = 1.75f, ManGirth = .3f;

    // From Begin: the numbers, kept up to date when the table is saved.
    private void BindCloseHold() => GameTables.BindNested(CloseHoldTable, CloseHoldRow, this, closeHold);

    /// <summary>
    /// The held gun can be held in close now: a pistol (not in CloseHold.csv's except list) drawn and held by FPSAF, standing
    /// or crouched, with the hands free (no reload, draw, holster or look over it) and not shown off in the customize menu.
    /// muzzle: where its muzzle is held in close, from its clean hip hold (for the cover probes); foeClose: a living enemy's
    /// body is within foeNear of the eyes (foeLetGo while <paramref name="holding"/>, so it doesn't flick at the edge) and
    /// foeCone of the view, nothing solid between; linger: how long it stays in after its reason went (s).
    /// </summary>
    public bool CanHoldClose(Transform probeView, bool holding, int layers, out Vector3 muzzle, out bool foeClose, out float linger)
    {
        muzzle = probeView != null ? probeView.position : Vector3.zero;
        foeClose = false;
        linger = Mathf.Max(0f, closeHold.linger);
        if (!closeHold.on || !HoldsGun || eyes == null || !HeldPistol || held.HandsBusy || Framework < .999f
            || controller.Customizing || Excepted(held.Data)) return false;
        Vector3 clean = CleanMuzzle();
        muzzle = eyes.TransformPoint(clean + CloseMove(clean));
        foeClose = FoeClose(holding ? Mathf.Max(closeHold.foeNear, closeHold.foeLetGo) : closeHold.foeNear, layers);
        return true;
    }

    /// <summary>WeaponController holds the gun in close this frame (true) or lets it out; it eases at the hold's speeds.</summary>
    public void HoldClose(bool close) => closeWanted = close;

    private bool Excepted(WeaponData data)
    {
        if (data == null || closeHold.except == null) return false;
        foreach (var id in closeHold.except)
            if (id != null && string.Equals(id.Trim(), data.name, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    // The muzzle held clean at the hip, from the eyes (LearnHipMuzzle). A pistol drawn with an enemy already close is held in
    // before it was ever held clean, and the hip muzzle isn't learnt while it is in: then where its hip hold puts the muzzle
    // (the bake's place from the eyes until FPSAF has held it), else on the sight line at the gun's length as Prepare has it.
    private Vector3 CleanMuzzle()
    {
        if (held.HasHipMuzzle) return held.HipMuzzle;
        if (held.HoldFromEyes(0f, out KTransform hip)) return hip.position + hip.rotation * (held.MuzzlePosition * ModelScale);
        return Vector3.forward * held.MuzzlePosition.magnitude;
    }

    // How the close hold moves the muzzle, in the eyes' frame (m): back toward the eyes, down, and in toward the middle of
    // the view by `inward` of its way off it, but never nearer the eyes than `nearest` (nor further out than it was).
    private Vector3 CloseMove(Vector3 muzzle)
    {
        var move = new Vector3(-muzzle.x * Mathf.Clamp01(closeHold.inward), -closeHold.down, -closeHold.back);
        float ahead = muzzle.z + move.z;
        if (ahead < closeHold.nearest) move.z += Mathf.Min(closeHold.nearest - ahead, Mathf.Max(0f, -move.z));
        return move;
    }

    // A living enemy's body within reach of the eyes (m, to its skin) and foeCone of the view, the first thing the eyes
    // meet on the way to it being it or nothing (not a wall). Every PC lists its own copies of the enemies
    // (EnemyHealth.Living, on the host and on copies), so a friend's PC finds the same.
    private bool FoeClose(float reach, int layers)
    {
        if (reach <= 0f) return false;
        Vector3 from = eyes.position, look = eyes.forward;
        float cone = Mathf.Cos(Mathf.Clamp(closeHold.foeCone, 0f, 180f) * Mathf.Deg2Rad);
        float rough = (reach + FoeRootSlack) * (reach + FoeRootSlack);
        var owner = movement != null ? movement.gameObject : gameObject;
        var foes = EnemyHealth.Living;
        for (int i = 0; i < foes.Count; i++)
        {
            var foe = foes[i];
            if (foe == null || !foe.IsAlive || (foe.transform.position - from).sqrMagnitude > rough) continue;
            Vector3 to = NearestOnBody(foe, from, out float radius) - from;
            float distance = to.magnitude;
            if (distance - radius > reach) continue;
            // Inside its girth (its body against the face) any way counts; otherwise only before the eyes.
            if (distance > radius && Vector3.Dot(to, look) < cone * distance) continue;
            if (distance > 1e-3f && AttackTrace.FirstExternalHit(owner, from, to / distance, distance, layers, out RaycastHit hit)
                && !WeaponAimSolver.IsFoe(hit.collider)) continue;
            return true;
        }
        return false;
    }

    // The point on the line through the middle of an enemy's body nearest a point, and the body's girth round that line:
    // its capsule as ZombieBody fits it to the character (a crawler's lies low), else a man's height and girth.
    private static Vector3 NearestOnBody(EnemyHealth foe, Vector3 point, out float radius)
    {
        var body = foe.transform;
        if (foe.TryGetComponent(out CapsuleCollider capsule))
        {
            Vector3 scale = body.lossyScale;
            int axisIndex = Mathf.Clamp(capsule.direction, 0, 2);
            Vector3 axis = axisIndex == 0 ? body.right : axisIndex == 2 ? body.forward : body.up;
            float along = Mathf.Abs(scale[axisIndex]);
            float across = Mathf.Max(Mathf.Abs(scale[(axisIndex + 1) % 3]), Mathf.Abs(scale[(axisIndex + 2) % 3]));
            radius = capsule.radius * across;
            float half = Mathf.Max(0f, capsule.height * along * .5f - radius);
            Vector3 middle = body.TransformPoint(capsule.center);
            return middle + axis * Mathf.Clamp(Vector3.Dot(point - middle, axis), -half, half);
        }
        radius = ManGirth;
        Vector3 feet = body.position;
        return feet + Vector3.up * Mathf.Clamp(point.y - feet.y, ManGirth, ManHeight - ManGirth);
    }

    // How far the gun is held in this frame (0-1): eased, and only where FPSAF holds it (not in a vault or a slide) and not
    // while the customize menu holds it out.
    private float CloseShare => Mathf.SmoothStep(0f, 1f, closeWeight) * Framework * (1f - presentWeight);

    // After the clips have placed the gun (FollowContacts), before the bash goes on top, the weapon root rides it (RideGun)
    // and the hands take it (320): the gun held in close, as far as it is in.
    private void FollowCloseHold()
    {
        float dt = Time.deltaTime;
        bool wanted = closeWanted && HoldsGun && HeldPistol && !held.HandsBusy;
        float halfLife = Mathf.Max(.005f, wanted ? closeHold.inHalfLife : closeHold.outHalfLife);
        closeWeight = Mathf.Lerp(closeWeight, wanted ? 1f : 0f, 1f - Mathf.Exp(-Mathf.Log(2f) * Mathf.Max(0f, dt) / halfLife));
        if (!wanted && closeWeight < .001f) closeWeight = 0f;
        float weight = CloseShare;
        if (weight <= 0f || !HoldsGun || rig == null || eyes == null) return;
        var gun = rig.IkWeaponBone;
        var model = held.transform;
        Vector3 muzzleAt = model.TransformPoint(held.MuzzlePosition);
        Vector3 bore = model.rotation * (held.MuzzleRotation * Vector3.forward);
        Quaternion roll = Quaternion.AngleAxis(closeHold.cant, bore);
        Vector3 move = eyes.TransformVector(CloseMove(CleanMuzzle()));
        Vector3 at = muzzleAt + move + roll * (gun.position - muzzleAt);
        gun.SetPositionAndRotation(Vector3.Lerp(gun.position, at, weight), Quaternion.Slerp(gun.rotation, roll * gun.rotation, weight));

        // Moved, the barrel is off the crosshair's point: back onto it about the muzzle, as GunConvergeLayer turns it (the
        // point no nearer than its NearestAim, so a zombie pressed against the gun doesn't swing it about), and as far as
        // that layer turns it (not mid-reload or mid-draw).
        float onto = weight * convergeWeight;
        if (onto <= 0f) return;
        muzzleAt = model.TransformPoint(held.MuzzlePosition);
        bore = model.rotation * (held.MuzzleRotation * Vector3.forward);
        float distance = hasAimDistance ? 1f / Mathf.Max(1e-4f, inverseAimDistance) : DefaultAim;
        Vector3 toPoint = eyes.position + eyes.forward * distance - muzzleAt;
        if (toPoint.sqrMagnitude < 1e-4f || Vector3.Dot(toPoint, bore) <= 0f) return;
        Quaternion turn = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(bore, toPoint.normalized), onto);
        gun.SetPositionAndRotation(muzzleAt + turn * (gun.position - muzzleAt), turn * gun.rotation);
    }

    // An elbow's way (ElbowWay) turned with the close hold's roll, as far as the gun is held in: the gun hand's elbow comes
    // out to the side and the support hand's goes down under the gun, as a CAR hold has them.
    private Vector3 CloseElbow(Vector3 way)
    {
        float share = CloseShare;
        return share <= 0f || eyes == null ? way : Quaternion.AngleAxis(closeHold.cant * share, eyes.forward) * way;
    }

    // The body went, or WeaponController reset the gun's pose (a draw, death): nothing held in.
    private void ForgetCloseHold()
    {
        closeWeight = 0f;
        closeWanted = false;
    }
}
