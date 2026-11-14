using UnityEngine;

/// <summary>
/// The x-ray belt beside a security lane's metal detector (GimmickDetector; the map gimmick card "보안 검색대": "총을
/// 바구니에 담아 엑스레이 벨트로 넘기고 맨몸으로 지나가요"): [F] at its head puts this PC's player's weapons in a tray
/// (PlayerWeaponSlots.HoldGunsAway: nothing in the hands, number keys and grenades off; fists and takedowns still work),
/// which rides the belt to its end in rideSeconds; [F] there takes them back. Through the arch bare-handed meanwhile, the
/// detector stays quiet. Each player's tray is their own, on their own PC (nothing shared). With the tray still on the
/// belt, someone gone far from it (back at a rest stop after a wipe, say) or leaving the map gets their weapons back
/// at once, so nobody is ever stranded empty-handed.
/// </summary>
public sealed class GimmickTray : GimmickSwitch
{
    [SerializeField, Tooltip("The belt's head, where the tray goes on (and [F] is).")]
    private Transform head;
    [SerializeField, Tooltip("The belt's end, where the tray waits to be taken.")]
    private Transform end;
    [SerializeField, Tooltip("The tray seen riding the belt (hidden until used).")]
    private GameObject tray;
    [SerializeField, Min(.5f), Tooltip("How long the tray takes from the head to the end (s).")]
    private float rideSeconds = 6f;
    [SerializeField, Min(5f), Tooltip("Farther than this from the belt (m), the weapons come back by themselves.")]
    private float leaveRange = 40f;

    private bool away;
    private float placedAt;

    private bool Arrived => Time.time - placedAt >= rideSeconds;

    internal override Vector3 Handle => away && end != null ? end.position : head != null ? head.position : transform.position;

    private void Awake()
    {
        if (tray != null) tray.SetActive(false);
    }

    private void OnDisable() => Return();

    internal override string Offer(PlayerMovement mine)
    {
        if (!away) return mine.TryGetComponent(out PlayerWeaponSlots slots) && slots.HandsFull ? null : Texts.Get("gimmick.tray.put");
        return Texts.Get(Arrived ? "gimmick.tray.take" : "gimmick.tray.riding");
    }

    internal override int Use(PlayerMovement mine)
    {
        if (!away)
        {
            away = true;
            placedAt = Time.time;
            if (tray != null) tray.SetActive(true);
            GimmickSounds.PlayAt(GimmickSounds.Cue.Clunk, Handle, .5f, 2f, 14f);
        }
        else if (Arrived)
        {
            Return();
            GimmickSounds.PlayAt(GimmickSounds.Cue.Unlock, Handle, .6f, 2f, 14f);
        }
        return -1;
    }

    internal override void Tick(float dt, bool host)
    {
        var mine = Mine;
        if (away && mine != null && head != null && (mine.transform.position - head.position).sqrMagnitude > leaveRange * leaveRange) Return();
        if (mine != null && mine.TryGetComponent(out PlayerWeaponSlots slots)) slots.HoldGunsAway(away);
        if (!away || tray == null || head == null || end == null) return;
        float share = Mathf.Clamp01((Time.time - placedAt) / rideSeconds);
        tray.transform.position = Vector3.Lerp(head.position, end.position, Mathf.SmoothStep(0f, 1f, share));
    }

    // The weapons back in this PC's player's hands, the tray off the belt.
    private void Return()
    {
        if (!away) return;
        away = false;
        if (tray != null) tray.SetActive(false);
        var mine = PlayerRoster.Local;
        if (mine != null && mine.TryGetComponent(out PlayerWeaponSlots slots)) slots.HoldGunsAway(false);
    }
}
