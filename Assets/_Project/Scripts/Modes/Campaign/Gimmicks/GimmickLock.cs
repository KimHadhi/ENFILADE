using UnityEngine;
using UnityEngine.AI;

/// <summary>
/// A locked door (the map gimmick card "관리실 마스터키", with the user's note: "급하면 걍 데미지 넣어서 부술 수 있게"): the
/// item it wants (GimmickItem) opens it quietly with [F], sliding it aside; without it, shots, blows and blasts break it,
/// loud: everything not yet awake within its wake box gets up (ZombieMind.WakeWithin, the floor it is on) and the noise
/// carries. It blocks players with its collider and zombies with a carving obstacle (the bake leaves it out), so once it
/// is open or down they come through. Each PC counts the damage its own player does (the actor decides); the PC that
/// breaks it says so. States: 0 shut, 1 opened, 2 broken.
/// </summary>
[RequireComponent(typeof(Collider))]
public sealed class GimmickLock : GimmickSwitch, IDamageReceiver
{
    private const byte Shut = 0, Opened = 1, Broken = 2;

    [SerializeField, Tooltip("The item that opens it (GimmickItem.Id).")]
    private string item = "";
    [SerializeField, Tooltip("Texts id of the item's name, for the prompt (\"마스터키\").")]
    private string itemTitle = "";
    [SerializeField, Min(1f), Tooltip("How much damage breaks it (a rifle's shot is some 30-40, a blow 60-90).")]
    private float toughness = 320f;
    [SerializeField, Tooltip("Where things wake when it is smashed in (world box): its floor.")]
    private Bounds wakeBox;
    [SerializeField, Min(0f), Tooltip("How far the crash carries to zombies beyond that box (m).")]
    private float crashNoise = 30f;
    [SerializeField, Tooltip("How far it slides when opened (local).")]
    private Vector3 openOffset = new Vector3(0f, 0f, 1f);

    private Vector3 shutAt;
    private float taken, shown;
    private Collider shape;
    private NavMeshObstacle carve;

    private void Awake()
    {
        shutAt = transform.localPosition;
        shape = GetComponent<Collider>();
        carve = GetComponent<NavMeshObstacle>();
    }

    internal override Vector3 Handle => shape != null ? shape.bounds.center : transform.position;

    internal override string Offer(PlayerMovement mine)
    {
        if (State != Shut) return null;
        return MapGimmicks.Has(item) ? Texts.Format("gimmick.lock.open", Texts.Get(itemTitle))
            : Texts.Format("gimmick.lock.locked", Texts.Get(itemTitle));
    }

    internal override int Use(PlayerMovement mine) => State == Shut && MapGimmicks.Has(item) ? Opened : -1;

    // Opened and broken at once: whichever word came first stands.
    internal override bool Accepts(byte now, byte proposed) => now == Shut;

    public DamageResult ReceiveHit(DamageHit hit)
    {
        // Only this PC's own player's blows count here, as only this PC saw them land.
        var by = hit.Source != null ? hit.Source.GetComponentInParent<PlayerMovement>() : null;
        if (State != Shut || hit.Kind != DamageKind.Attack || by == null || !by.IsLocal) return DamageResult.Ignored;
        taken += hit.Amount;
        if (taken >= toughness) Set(Broken);
        return DamageResult.Ignored;
    }

    /// <summary>A blast on this PC from its own player (a frag, a claymore): what it does to every locked door near.</summary>
    internal static void Blast(Vector3 at, float radius, float amount, GameObject source)
    {
        var map = MapGimmicks.Active;
        if (map == null || amount <= 0f) return;
        foreach (var each in map.Switches)
        {
            if (!(each is GimmickLock door) || door.State != Shut) continue;
            float distance = Vector3.Distance(door.Handle, at);
            if (distance > radius) continue;
            door.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), amount * (1f - distance / radius * .5f), DamageKind.Attack,
                door.Handle, door.Handle - at, source, method: DamageMethod.Blast, origin: at));
        }
    }

    internal override void Show(byte state, bool fresh)
    {
        if (state == Broken)
        {
            gameObject.SetActive(false);
            if (!fresh) return;
            GimmickSounds.PlayAt(GimmickSounds.Cue.Smash, Handle, 1f, 6f, 70f);
            // The crash is the host's business for the zombies: the floor wakes and the noise goes beyond it.
            if (MapGimmicks.IsHost)
            {
                var near = PlayerRoster.Nearest(Handle);
                if (wakeBox.size != Vector3.zero) ZombieMind.WakeWithin(wakeBox, near);
                if (crashNoise > 0f && near != null) Noise.Make(Handle, crashNoise, near.gameObject);
            }
            return;
        }
        if (state == Opened)
        {
            if (shape != null) shape.enabled = false;
            if (carve != null) carve.enabled = false;
            if (fresh) GimmickSounds.PlayAt(GimmickSounds.Cue.Unlock, Handle, .9f, 2f, 14f);
            else
            {
                shown = 1f;
                transform.localPosition = shutAt + openOffset;
            }
        }
    }

    internal override void Tick(float dt, bool host)
    {
        // Opened, it slides aside over half a second.
        if (State != Opened || shown >= 1f) return;
        shown = Mathf.MoveTowards(shown, 1f, dt * 2f);
        transform.localPosition = shutAt + openOffset * shown;
    }
}
