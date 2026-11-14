using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A terminal's fire alarm (the map gimmick card "꺼야 멈추는 화재 경보", after GTFO's error alarms and L4D2's Dark
/// Carnival coaster): a smoke detector under it hit by a shot (its GimmickTarget boxes) or caught in a blast sets it
/// ringing through the whole terminal, bells at its speakers and red beacons turning (GimmickBeacon). While it rings the
/// stretch is loud and the horde keeps coming however long nobody is seen (CampaignRun.Ring); [F] held at its panel in
/// the control room across the terminal silences it. A wipe that sends the run back to its rest stop resets it.
/// States: odd ringing, even quiet (how many times it was set off and silenced).
/// </summary>
public sealed class GimmickFireAlarm : GimmickSwitch, IGimmickShot
{
    private static readonly List<GimmickFireAlarm> all = new List<GimmickFireAlarm>();

    [SerializeField, Tooltip("The panel where [F] silences it.")]
    private Transform panel;
    [SerializeField, Tooltip("Where its bells ring (the terminal's speakers).")]
    private Transform[] bells = new Transform[0];
    [SerializeField, Tooltip("Its beacons, turning while it rings.")]
    private GimmickBeacon[] beacons = new GimmickBeacon[0];
    [SerializeField, Tooltip("Its smoke detectors (a blast near one sets it off as a shot does).")]
    private Transform[] detectors = new Transform[0];
    [SerializeField, Min(0f), Tooltip("How long [F] is held to silence it (s).")]
    private float silenceSeconds = 2f;

    private readonly List<AudioSource> loops = new List<AudioSource>();
    private int wipesSeen;

    private bool Ringing => (State & 1) == 1;

    internal override Vector3 Handle => panel != null ? panel.position : transform.position;

    internal override float HoldSeconds => Ringing ? silenceSeconds : 0f;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void Clear() => all.Clear();

    private void OnEnable() => all.Add(this);

    private void OnDisable()
    {
        all.Remove(this);
        CampaignRun.Ring(this, false);
    }

    private void Start()
    {
        foreach (var bell in bells)
            if (bell != null) loops.Add(GimmickSounds.Loop(bell.gameObject, GimmickSounds.Cue.FireBell, 6f, 70f));
        wipesSeen = CampaignRun.Wipes;
    }

    internal override string Offer(PlayerMovement mine) => Texts.Get(Ringing ? "gimmick.firealarm.off" : "gimmick.firealarm.fine");

    internal override int Use(PlayerMovement mine) => Ringing ? (byte)(State + 1) : -1;

    // Two at once (two shots, or a shot and the panel): one change.
    internal override bool Accepts(byte now, byte proposed) => proposed == (byte)(now + 1);

    /// <summary>A detector was hit by this PC's own player.</summary>
    public void Shot(PlayerMovement by, in DamageHit hit)
    {
        if (!Ringing) Set((byte)(State + 1));
    }

    /// <summary>The thrower's PC: a blast that reaches a detector sets its alarm off.</summary>
    public static void Blast(Vector3 at, float radius)
    {
        foreach (var alarm in all)
        {
            if (alarm == null || alarm.Ringing) continue;
            foreach (var detector in alarm.detectors)
                if (detector != null && (detector.position - at).sqrMagnitude <= radius * radius) { alarm.Set((byte)(alarm.State + 1)); break; }
        }
    }

    internal override void Tick(float dt, bool host)
    {
        // The host: a wipe sends the run back, and the terminal is quiet again.
        if (!host) return;
        int wipes = CampaignRun.Wipes;
        if (wipes == wipesSeen) return;
        wipesSeen = wipes;
        if (Ringing) Set((byte)(State + 1));
    }

    internal override void Show(byte state, bool fresh)
    {
        bool ring = (state & 1) == 1;
        foreach (var loop in loops)
        {
            if (loop == null) continue;
            if (ring && !loop.isPlaying) loop.Play();
            else if (!ring && loop.isPlaying) loop.Stop();
        }
        foreach (var beacon in beacons) if (beacon != null) beacon.On = ring;
        CampaignRun.Ring(this, ring);
        if (!fresh) return;
        if (ring) AlertHUD.Danger(Texts.Get("gimmick.firealarm.ringing"));
        else AlertHUD.Self(Texts.Get("gimmick.firealarm.silenced"));
    }
}
