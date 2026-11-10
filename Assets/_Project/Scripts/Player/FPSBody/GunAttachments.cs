using System;
using UnityEngine;

/// <summary>
/// The muzzle, grip and underbarrel parts a gun can wear, as Rainbow Six Siege lets an operator pick them (sights are
/// GunSights'). TacticalGunSetup puts every choice the Tactical Shooter Pack gives the gun on its mount, with nothing there
/// as the first choice of each; one shows at a time. They are picked in the customize menu (C, GunCustomizeMenu) and L
/// switches the flashlight, and what each part does is read from its kind: a suppressor quietens the shot and hides its
/// flash, a flashlight lights the way.
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
    [Tooltip("The choices it starts with: muzzle, grip, underbarrel.")]
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
    /// Slides each flashlight's light forward along its own beam until it starts just past the front of the gun
    /// (<paramref name="muzzleFront"/>, world space), keeping the beam's line. On the MX16A4 the LS3 hangs halfway down
    /// the handguard, and the barrel, the front sight post and the muzzle ahead of it threw a big shadow into the beam
    /// on the wall (user, 2026-11-09). With the beam starting in front of everything the gun wears, the gun shadows
    /// nothing; the world still does. The lamp's model and lens stay where they are, since the Light sits on its own
    /// child object ("Spotlight" in the pack's A_LS3_Flashlight). Call again when the muzzle part changes.
    /// </summary>
    public void LightAhead(Vector3 muzzleFront)
    {
        if (underbarrel == null) return;
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
            Vector3 beam = lamp.forward;
            float ahead = Vector3.Dot(muzzleFront - lamp.position, beam) + LightPastMuzzle;
            if (ahead > 0f) lamp.position += beam * ahead;
        }
    }

    private static Kind KindOf(Choice[] list, int index) => list != null && index >= 0 && index < list.Length ? list[index].kind : Kind.None;

    /// <summary>The starting choices, unless some are up already.</summary>
    public void Begin()
    {
        if (Muzzle < 0) SelectMuzzle(initialMuzzle);
        if (Grip < 0) SelectGrip(initialGrip);
        if (Underbarrel < 0) SelectUnderbarrel(initialUnderbarrel);
    }

    public void SelectMuzzle(int index) => Muzzle = Select(muzzle, index);
    public void SelectGrip(int index) => Grip = Select(grips, index);
    public void SelectUnderbarrel(int index)
    {
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
