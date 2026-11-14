using UnityEngine;

/// <summary>
/// A heavy shutter two people open together (the map gimmick card "둘이 같이 하는 일", after PAYDAY 3's paired actions;
/// the user's note "좋아, 근데 솔플할 때는 어떻게 해?", answered "첫 레버를 당기면 걸쇠가 걸려 8초 동안 버텨요"): two
/// levers stand apart, and a lever pulled latches for latchSeconds; both latched at once roll the shutter up for good.
/// Two players pull both together; alone, one pulls the first and runs to the second. A lone latch slips when its time
/// is up (the host's clock). Two levers pulled at the same moment on two PCs both count (Merge). States: bits, 1 the
/// first lever latched, 2 the second; 3 open.
/// </summary>
public sealed class GimmickTwoHands : GimmickSwitch
{
    private const byte Open = 3;
    private const float LeverDown = 60f;

    [SerializeField, Tooltip("The first lever (turned down about its X when latched).")]
    private Transform first;
    [SerializeField, Tooltip("The second lever.")]
    private Transform second;
    [SerializeField, Tooltip("The shutter's solid part, rolled up when open.")]
    private Transform shutter;
    [SerializeField, Min(.5f), Tooltip("How far the shutter goes up when open (m).")]
    private float rise = 2.6f;
    [SerializeField, Min(1f), Tooltip("How long a lone lever stays latched (s).")]
    private float latchSeconds = 8f;
    [SerializeField, Min(.2f), Tooltip("How long the shutter takes to roll up (s).")]
    private float riseSeconds = 1.6f;

    private Vector3 shut;
    private Quaternion firstUp, secondUp;
    private float latchedAt = -999f, opened, firstDown, secondDown;

    private void Awake()
    {
        if (shutter != null) shut = shutter.localPosition;
        if (first != null) firstUp = first.localRotation;
        if (second != null) secondUp = second.localRotation;
    }

    // The lever nearer this PC's player: the one [F] works.
    private Transform Near
    {
        get
        {
            var mine = Mine;
            if (first == null || second == null || mine == null) return first != null ? first : second;
            Vector3 at = mine.transform.position;
            return (first.position - at).sqrMagnitude <= (second.position - at).sqrMagnitude ? first : second;
        }
    }

    private byte BitOf(Transform lever) => lever == second ? (byte)2 : (byte)1;

    private float Left => Mathf.Max(0f, latchSeconds - (Time.time - latchedAt));

    internal override Vector3 Handle
    {
        get
        {
            var lever = Near;
            return lever != null ? lever.position : transform.position;
        }
    }

    internal override string Offer(PlayerMovement mine)
    {
        if (State == Open) return null;
        if ((State & BitOf(Near)) != 0) return Texts.Format("gimmick.twohands.latched", Mathf.CeilToInt(Left));
        return Texts.Get(State == 0 ? "gimmick.twohands.first" : "gimmick.twohands.second");
    }

    internal override int Use(PlayerMovement mine)
    {
        byte bit = BitOf(Near);
        return State == Open || (State & bit) != 0 ? -1 : (byte)(State | bit);
    }

    internal override bool Accepts(byte now, byte proposed) => now != Open && proposed != 0 && (proposed & ~Open) == 0;

    internal override byte Merge(byte now, byte proposed) => (byte)(now | proposed);

    internal override void Tick(float dt, bool host)
    {
        // The host: a lone latch slips once its time is up.
        if (host && State != 0 && State != Open && Time.time - latchedAt >= latchSeconds) Set(0);
        opened = Mathf.MoveTowards(opened, State == Open ? 1f : 0f, dt / riseSeconds);
        if (shutter != null) shutter.localPosition = shut + Vector3.up * (rise * Mathf.SmoothStep(0f, 1f, opened));
        firstDown = Mathf.MoveTowards(firstDown, (State & 1) != 0 ? 1f : 0f, dt * 5f);
        secondDown = Mathf.MoveTowards(secondDown, (State & 2) != 0 ? 1f : 0f, dt * 5f);
        if (first != null) first.localRotation = firstUp * Quaternion.Euler(LeverDown * firstDown, 0f, 0f);
        if (second != null) second.localRotation = secondUp * Quaternion.Euler(LeverDown * secondDown, 0f, 0f);
    }

    internal override void Show(byte state, bool fresh)
    {
        // A lever latched now: each PC times the latch from when it heard (the host's own clock decides the slip).
        if (state != 0 && state != Open) latchedAt = Time.time;
        if (!fresh)
        {
            if (state == Open) opened = 1f;
            return;
        }
        var lever = (state & 2) != 0 && (state & 1) == 0 ? second : first;
        if (state != 0 && lever != null) GimmickSounds.PlayAt(GimmickSounds.Cue.Lever, lever.position, .9f, 3f, 25f);
        if (state == Open && shutter != null)
        {
            GimmickSounds.PlayAt(GimmickSounds.Cue.Shutter, shutter.position, 1f, 4f, 40f);
            var mine = Mine;
            if (mine != null && (mine.transform.position - shutter.position).sqrMagnitude < 30f * 30f) AlertHUD.Self(Texts.Get("gimmick.twohands.open"));
        }
    }
}
