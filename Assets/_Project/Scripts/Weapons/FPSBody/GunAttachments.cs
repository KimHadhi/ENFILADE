using System;
using UnityEngine;

/// <summary>
/// The muzzle, grip and underbarrel parts a gun can wear, as Rainbow Six Siege lets an operator pick them (sights are
/// GunSights'). TacticalGunSetup embeds every part the gun's sockets offer, with nothing there as the first choice of each;
/// one shows at a time. They are picked in the customize menu (C, GunCustomizeMenu) and L switches the flashlight, and what
/// each part does is read from its kind: a suppressor quietens the shot and hides its flash, a flashlight lights the way.
/// Where each part sits is the tables' (GunMounts): the gun's muzzle, grip and under sockets in GunMounts.csv and each
/// part's foot in GunParts.csv, put there as the gun is first shown. A light sits at its rail's front end, a grip at its
/// rail's rear until the hand that holds it says where (SeatGrips), a muzzle part at the socket. A part's kind and which
/// parts the menu offers are the tables' too (GunParts.csv kind, GunMounts.csv fits and starts).
/// </summary>
[DisallowMultipleComponent]
public sealed class GunAttachments : MonoBehaviour
{
    public enum Kind { None, Suppressor, VerticalGrip, AngledGrip, Flashlight }

    [Serializable]
    public struct Choice
    {
        public Kind kind;
        [Tooltip("The part on its mount; none for nothing there.")]
        public GameObject part;
        [Tooltip("A part of the gun's own model, shown by its size instead (the Herrington's built-in suppressor).")]
        public Transform bone;
        [Tooltip("A flashlight's light.")]
        public Light light;
        [Tooltip("A flashlight's lens, lit while it is on, and how brightly (the pack's _EmissionStrength).")]
        public Renderer lens;
        public float lensGlow;
        [Tooltip("A grip: the support hand goes on it (the pack's LeftHand, made into directions any hand can take).")]
        public bool hasHand;
        [Tooltip("Where the middle of the support hand's palm goes, in the gun's own space.")]
        public Vector3 handPalm;
        [Tooltip("The way its fingers point from the wrist and the way its palm faces, in the gun's own space.")]
        public Vector3 handFingers, handFacing;
    }

    public Choice[] muzzle = Array.Empty<Choice>();
    public Choice[] grips = Array.Empty<Choice>();
    public Choice[] underbarrel = Array.Empty<Choice>();
    [Tooltip("The choices it starts with where its GunMounts rows name none: muzzle, grip, underbarrel.")]
    public int initialMuzzle, initialGrip, initialUnderbarrel;

    public int Muzzle { get; private set; } = -1;
    public int Grip { get; private set; } = -1;
    public int Underbarrel { get; private set; } = -1;
    public bool LightOn { get; private set; }

    public bool Suppressed => KindOf(muzzle, Muzzle) == Kind.Suppressor;
    public Kind GripKind => KindOf(grips, Grip);
    /// <summary>The grip on now, if the support hand goes on it.</summary>
    public bool GripHand(out Choice grip)
    {
        grip = grips != null && Grip >= 0 && Grip < grips.Length ? grips[Grip] : default;
        return grip.hasHand;
    }
    public bool HasLight => KindOf(underbarrel, Underbarrel) == Kind.Flashlight;

    private static readonly int EmissionStrength = Shader.PropertyToID("_EmissionStrength");
    private MaterialPropertyBlock glow;

    // How far past the front of the gun a flashlight's beam starts (m): a little more than GunMuzzle's front-face depth.
    private const float LightPastMuzzle = .03f;
    // Where each underbarrel light sat as the setup put it (its local position), before LightAhead moved it.
    private Vector3[] lightHomes;

    /// <summary>
    /// Slides each flashlight's light forward along the gun's barrel (<paramref name="barrel"/>, world space) until it
    /// starts just past the front of the gun (<paramref name="muzzleFront"/>, world space), and turns its beam the way the
    /// barrel points. On the MX16A4 the LS3 hangs halfway down the handguard, and the barrel, the front sight post and the
    /// muzzle ahead of it threw a big shadow into the beam on the wall (user, 2026-11-09). With the beam starting in front
    /// of everything the gun wears, the gun shadows nothing; the world still does. The lamp's model and lens stay where they
    /// are, since the Light sits on its own child object ("Spotlight" in the pack's A_LS3_Flashlight). The beam goes where
    /// the gun does even under a handguard whose underside slopes (the MPS5's, 8 degrees): a lamp clamped to that slope
    /// would light the floor short of what the gun is on. Call again when the muzzle part changes.
    /// </summary>
    public void LightAhead(Vector3 muzzleFront, Vector3 barrel)
    {
        if (underbarrel == null) return;
        Seat();
        if (lightHomes == null || lightHomes.Length != underbarrel.Length)
        {
            lightHomes = new Vector3[underbarrel.Length];
            for (int i = 0; i < underbarrel.Length; i++)
                if (underbarrel[i].light != null) lightHomes[i] = underbarrel[i].light.transform.localPosition;
        }
        for (int i = 0; i < underbarrel.Length; i++)
        {
            var light = underbarrel[i].light;
            if (light == null) continue;
            var lamp = light.transform;
            // A Light on the part's own root would carry the whole lamp along with it.
            if (underbarrel[i].part != null && lamp == underbarrel[i].part.transform) continue;
            lamp.localPosition = lightHomes[i];
            Vector3 beam = barrel.sqrMagnitude > 1e-8f ? barrel.normalized : lamp.forward;
            lamp.rotation = Quaternion.LookRotation(beam, transform.up);
            float ahead = Vector3.Dot(muzzleFront - lamp.position, beam) + LightPastMuzzle;
            if (ahead > 0f) lamp.position += beam * ahead;
        }
    }

    private static Kind KindOf(Choice[] list, int index) => list != null && index >= 0 && index < list.Length ? list[index].kind : Kind.None;

    /// <summary><paramref name="piece"/> is one of the parts this switches, or inside one (not the gun's own body).</summary>
    public bool Carries(Transform piece)
    {
        if (piece == null) return false;
        foreach (var list in new[] { muzzle, grips, underbarrel })
        {
            if (list == null) continue;
            foreach (var choice in list)
                if (choice.part != null && piece.IsChildOf(choice.part.transform)) return true;
        }
        return false;
    }

    // ---- Each part on its socket (GunMounts) ----

    /// <summary>A choice's id in the tables: its part's name, a model's own bone's name, or nothing for "none".</summary>
    public static string IdOf(in Choice choice) => choice.part != null ? choice.part.name : choice.bone != null ? choice.bone.name : "";

    /// <summary>The gun's id in the tables (GunMounts.IdOf: its object's name).</summary>
    public string GunId => GunMounts.IdOf(gameObject);

    /// <summary>The socket's row offers this choice (GunMounts.csv fits). "None" is always offered; a gun without a row
    /// offers all it carries.</summary>
    public bool Offered(string socket, in Choice choice)
    {
        string id = IdOf(choice);
        if (id.Length == 0) return true;
        var row = GunMounts.Of(GunId, socket);
        return !row.Found || row.Offers(id);
    }

    private int seatedFor = -1;
    // Each grip's support hand as its part carries it (the part's own space), so it goes where the part goes.
    private Vector3[] palmOnGrip, fingersOnGrip, facingOnGrip;
    // How far along its rail each grip sits (m), for the hand to move it from (SeatGrips).
    private float[] gripAlong;

    /// <summary>
    /// Puts the gun's muzzle, grip and under sockets and every part on them where the tables have them (GunMounts), and
    /// takes each part's kind from its row: once as the gun is first shown, and again after the tables were read anew. A
    /// socket without a row stays as the prefab has it.
    /// </summary>
    public void Seat()
    {
        if (seatedFor == GunMounts.Version) return;
        seatedFor = GunMounts.Version;
        string id = GunId;
        // A grip's hand is kept in the gun's space (the setup measured it there): into the part's own before it moves.
        int gripCount = grips != null ? grips.Length : 0;
        if (palmOnGrip == null || palmOnGrip.Length != gripCount)
        {
            palmOnGrip = new Vector3[gripCount];
            fingersOnGrip = new Vector3[gripCount];
            facingOnGrip = new Vector3[gripCount];
            gripAlong = new float[gripCount];
            for (int i = 0; i < gripCount; i++)
            {
                if (!grips[i].hasHand || grips[i].part == null) continue;
                var part = grips[i].part.transform;
                palmOnGrip[i] = part.InverseTransformPoint(transform.TransformPoint(grips[i].handPalm));
                fingersOnGrip[i] = part.InverseTransformDirection(transform.TransformDirection(grips[i].handFingers));
                facingOnGrip[i] = part.InverseTransformDirection(transform.TransformDirection(grips[i].handFacing));
            }
        }
        SeatList(muzzle, GunMounts.Of(id, GunMounts.Muzzle), GunMounts.Muzzle);
        SeatList(grips, GunMounts.Of(id, GunMounts.Grip), GunMounts.Grip);
        SeatList(underbarrel, GunMounts.Of(id, GunMounts.Under), GunMounts.Under);
        for (int i = 0; i < gripCount; i++) HandFollowsGrip(i);
        // A light's place as the tables now have it is its home again.
        lightHomes = null;
    }

    private void SeatList(Choice[] list, in GunMounts.Socket socket, string slot)
    {
        if (list == null) return;
        Transform mount = null;
        foreach (var choice in list)
            if (choice.part != null && choice.part.transform.parent != null) { mount = choice.part.transform.parent; break; }
        if (socket.Found && mount != null) GunMounts.Place(transform, mount, socket);
        for (int i = 0; i < list.Length; i++)
        {
            string id = IdOf(list[i]);
            if (id.Length == 0) continue;
            var data = GunMounts.PartOf(id);
            if (data.Found) list[i].kind = KindFrom(data.Kind, list[i].kind);
            var part = list[i].part;
            if (!socket.Found || part == null || part.transform.parent != mount) continue;
            float along = slot == GunMounts.Under ? GunMounts.SeatFront(socket, data.FootFront)
                : slot == GunMounts.Grip ? GunMounts.Kept(socket, 0f, data.FootBack, data.FootFront) : 0f;
            part.transform.localPosition = new Vector3(0f, 0f, along);
            part.transform.localRotation = Quaternion.identity;
            if (slot == GunMounts.Grip && gripAlong != null && i < gripAlong.Length) gripAlong[i] = along;
        }
    }

    // A part's kind as its GunParts row words it; a word the game doesn't know leaves what the prefab says.
    private static Kind KindFrom(string word, Kind kept)
    {
        switch ((word ?? "").Trim().ToLowerInvariant())
        {
            case "suppressor": return Kind.Suppressor;
            case "vertical": return Kind.VerticalGrip;
            case "angled": return Kind.AngledGrip;
            case "light": return Kind.Flashlight;
            default: return kept;
        }
    }

    // The support hand's place on grip i, in the gun's space, from where its part now is.
    private void HandFollowsGrip(int i)
    {
        if (grips == null || i < 0 || i >= grips.Length || !grips[i].hasHand || grips[i].part == null || palmOnGrip == null) return;
        var part = grips[i].part.transform;
        grips[i].handPalm = transform.InverseTransformPoint(part.TransformPoint(palmOnGrip[i]));
        grips[i].handFingers = transform.InverseTransformDirection(part.TransformDirection(fingersOnGrip[i]));
        grips[i].handFacing = transform.InverseTransformDirection(part.TransformDirection(facingOnGrip[i]));
    }

    // The choice the gun's socket starts with (GunMounts.csv starts), else the prefab's own.
    private int StartOf(Choice[] list, string slot, int kept)
    {
        if (list == null || list.Length == 0) return kept;
        var socket = GunMounts.Of(GunId, slot);
        if (socket.Found && !string.IsNullOrEmpty(socket.Starts))
            for (int i = 0; i < list.Length; i++)
                if (string.Equals(IdOf(list[i]), socket.Starts, StringComparison.OrdinalIgnoreCase)) return i;
        // A row that names no start, or a part the gun doesn't carry: nothing there, unless the prefab's own pick is still
        // offered (a gun without rows keeps what its setup chose).
        if (!socket.Found) return kept;
        return kept >= 0 && kept < list.Length && string.IsNullOrEmpty(socket.Starts) && socket.Fits.Length == 0 ? kept : 0;
    }

    /// <summary>The starting choices, unless some are up already.</summary>
    public void Begin()
    {
        Seat();
        if (Muzzle < 0) SelectMuzzle(StartOf(muzzle, GunMounts.Muzzle, initialMuzzle));
        if (Grip < 0) SelectGrip(StartOf(grips, GunMounts.Grip, initialGrip));
        if (Underbarrel < 0) SelectUnderbarrel(StartOf(underbarrel, GunMounts.Under, initialUnderbarrel));
    }

    public void SelectMuzzle(int index)
    {
        Seat();
        Muzzle = Select(muzzle, index);
    }

    public void SelectGrip(int index)
    {
        Seat();
        Grip = Select(grips, index);
    }

    /// <summary>
    /// Slides every grip whose support palm sits behind <paramref name="palm"/> (world: where KINEMATION's own support
    /// hand holds this gun's bare handguard, FPSBody) forward along its rail until it is there, as far as its rail goes
    /// (GunMounts.Kept); <paramref name="root"/> is the gun, whose space the hands' places are kept in. A grip is held
    /// where KINEMATION's arms hold the gun, so the arm is theirs: left at its rail's rear end, an angled grip sat by the
    /// magazine with the arm folded to it (user, 2026-11-12: "원래 M4 앵글 손잡이 달면 이렇게 내 몸 쪽에 가까이 달아서 이렇게
    /// 꺾이게 잡음?"). A grip at or ahead of that hand, or on a point KINEMATION put it at (the Tactical Shooter Pack's own
    /// guns), stays. Returns how far the farthest one went (m).
    /// </summary>
    public float SeatGrips(Transform root, Vector3 palm, Vector3 barrel)
    {
        if (grips == null || root == null || barrel.sqrMagnitude < 1e-8f) return 0f;
        Seat();
        barrel.Normalize();
        var socket = GunMounts.Of(GunId, GunMounts.Grip);
        float most = 0f;
        for (int i = 0; i < grips.Length; i++)
        {
            if (!grips[i].hasHand || grips[i].part == null) continue;
            Vector3 hand = root.TransformPoint(grips[i].handPalm);
            float behind = Vector3.Dot(palm - hand, barrel);
            // Under half a centimetre is where it is already.
            if (behind <= .005f) continue;
            most = Mathf.Max(most, MoveGrip(i, behind, barrel, socket, true));
        }
        return most;
    }

    /// <summary>
    /// Draws every grip whose support palm sits ahead of <paramref name="palm"/> (world: as far up the gun as this body's
    /// support arm holds it, FPSGun.SeatGrips) back along its rail until it is there: a grip is mounted where the hand that
    /// holds it reaches. Left ahead of that, a shorter arm held it locked straight with the palm short of it. This is the
    /// body's reach, so it goes past a rail's rear end if it must. Returns how far the farthest one came (m).
    /// </summary>
    public float DrawGripsBack(Transform root, Vector3 palm, Vector3 barrel)
    {
        if (grips == null || root == null || barrel.sqrMagnitude < 1e-8f) return 0f;
        Seat();
        barrel.Normalize();
        var socket = GunMounts.Of(GunId, GunMounts.Grip);
        float most = 0f;
        for (int i = 0; i < grips.Length; i++)
        {
            if (!grips[i].hasHand || grips[i].part == null) continue;
            Vector3 hand = root.TransformPoint(grips[i].handPalm);
            float ahead = Vector3.Dot(hand - palm, barrel);
            // Under half a centimetre is where it is already.
            if (ahead <= .005f) continue;
            most = Mathf.Max(most, MoveGrip(i, -ahead, barrel, socket, false));
        }
        return most;
    }

    // Moves grip i so its hand goes `alongBarrel` (m, forward positive) along the barrel, sliding it along its own rail (a
    // sloped handguard's grip stays on the handguard); kept on the rail when asked. Returns how far the hand went.
    private float MoveGrip(int i, float alongBarrel, Vector3 barrel, in GunMounts.Socket socket, bool keptOnRail)
    {
        var part = grips[i].part.transform;
        Vector3 rail = part.parent != null ? part.parent.forward : barrel;
        float lean = Vector3.Dot(rail, barrel);
        if (lean < .2f) { rail = barrel; lean = 1f; }
        float slide = alongBarrel / lean;
        if (keptOnRail && socket.Found && gripAlong != null && i < gripAlong.Length)
        {
            var data = GunMounts.PartOf(IdOf(grips[i]));
            float kept = GunMounts.Kept(socket, gripAlong[i] + slide, data.FootBack, data.FootFront);
            slide = kept - gripAlong[i];
        }
        if (Mathf.Abs(slide) < 1e-5f) return 0f;
        part.position += rail * slide;
        if (gripAlong != null && i < gripAlong.Length) gripAlong[i] += slide;
        HandFollowsGrip(i);
        return Mathf.Abs(slide * lean);
    }

    public void SelectUnderbarrel(int index)
    {
        Seat();
        Underbarrel = Select(underbarrel, index);
        ShowLight();
    }

    /// <summary>Turns the flashlight on or off. False when the gun wears none.</summary>
    public bool SetLight(bool on)
    {
        if (!HasLight) return false;
        LightOn = on;
        ShowLight();
        return true;
    }

    // Shows choice index (wrapped round) of the list and hides the others. -1 for an empty list.
    private static int Select(Choice[] list, int index)
    {
        if (list == null || list.Length == 0) return -1;
        int current = ((index % list.Length) + list.Length) % list.Length;
        for (int i = 0; i < list.Length; i++)
        {
            if (list[i].part != null) list[i].part.SetActive(i == current);
            if (list[i].bone != null) list[i].bone.localScale = i == current ? Vector3.one : Vector3.zero;
        }
        return current;
    }

    private void ShowLight()
    {
        if (underbarrel == null) return;
        for (int i = 0; i < underbarrel.Length; i++)
        {
            var choice = underbarrel[i];
            bool lit = LightOn && i == Underbarrel && choice.kind == Kind.Flashlight;
            if (choice.light != null) choice.light.enabled = lit;
            if (choice.lens == null) continue;
            glow ??= new MaterialPropertyBlock();
            choice.lens.GetPropertyBlock(glow);
            glow.SetFloat(EmissionStrength, lit ? choice.lensGlow : 0f);
            choice.lens.SetPropertyBlock(glow);
        }
    }
}
