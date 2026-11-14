using UnityEngine;

/// <summary>
/// A walk-through metal detector at an airport's security lane (the map gimmick card "보안 검색대", after Hitman's
/// detectors and PAYDAY 2 Golden Grin's guitar case): whoever walks into its arch carrying guns sets it off, and its
/// beep wakes everything round it (wakeBox: ZombieMind.WakeWithin, as a lift's chime wakes a floor). With the guns on the
/// x-ray belt beside it (GimmickTray) a player walks through bare-handed and it stays quiet; the staff gate beside the
/// belt is the other way past (a GimmickLock and its card). Each PC watches its own player (the actor decides).
/// States: how many times it went off.
/// </summary>
public sealed class GimmickDetector : GimmickSwitch
{
    private const float BeepSeconds = 1.4f;

    [SerializeField, Tooltip("The arch's way through (world box): walking into it with guns sets it off.")]
    private Bounds arch;
    [SerializeField, Tooltip("What its beep wakes (world box).")]
    private Bounds wakeBox;
    [SerializeField, Tooltip("The red lamp on its top, lit while it beeps; may be empty.")]
    private Light lamp;

    private bool inside;
    private float beepAt = -999f, lampFull;

    private void Awake()
    {
        if (lamp == null) return;
        lampFull = lamp.intensity;
        lamp.intensity = 0f;
        lamp.enabled = false;
    }

    // It has no [F]: walking through is all.
    internal override bool Accepts(byte now, byte proposed) => proposed == (byte)(now + 1);

    internal override void Tick(float dt, bool host)
    {
        var mine = Mine;
        bool now = mine != null && arch.Contains(mine.transform.position + Vector3.up * .9f);
        // Into the arch with guns (not on the belt), and not again within its own beep.
        if (now && !inside && Armed(mine) && Time.time - beepAt > BeepSeconds) Set((byte)(State + 1));
        inside = now;
        Glow();
    }

    private static bool Armed(PlayerMovement mine) => !(mine.TryGetComponent(out PlayerWeaponSlots slots) && slots.GunsAway);

    internal override void Show(byte state, bool fresh)
    {
        if (!fresh) return;
        beepAt = Time.time;
        Vector3 at = arch.center;
        GimmickSounds.PlayAt(GimmickSounds.Cue.Beep, at + Vector3.up * .6f, 1f, 4f, 55f);
        if (MapGimmicks.IsHost) ZombieMind.WakeWithin(wakeBox, PlayerRoster.Nearest(at));
        var mine = Mine;
        if (mine != null && wakeBox.Contains(mine.transform.position + Vector3.up)) AlertHUD.Danger(Texts.Get("gimmick.detector.beep"));
    }

    // The lamp rises and falls with the beep, one smooth swell (never a blink).
    private void Glow()
    {
        if (lamp == null) return;
        float since = Time.time - beepAt;
        float level = since < 0f || since > BeepSeconds + .4f ? 0f : Mathf.Sin(Mathf.Clamp01(since / (BeepSeconds + .4f)) * Mathf.PI);
        lamp.enabled = level > .001f;
        lamp.intensity = lampFull * level;
    }
}
