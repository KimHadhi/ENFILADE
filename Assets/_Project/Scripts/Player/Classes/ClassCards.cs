using UnityEngine;

/// <summary>
/// The class cards that need more than a number (Docs/ClassCards.md; the numbers alone are PlayerPerks.AbilityBonus),
/// at work for this PC's own player (ClassKit adds it). What they do is judged here, where the player's own hits are;
/// how they look goes to every PC (CardNet, CardFx).
/// <list type="bullet">
/// <item>Momentum (기세, everyone's; the user's "때릴 때마다 갱신되는 강화 효과"): every hit this player lands on an
/// enemy keeps it going a few seconds more (PlayerPerks.Struck). A miss takes nothing; it only lets it run out.</item>
/// <item>Ghost, White Tiger (화이트 타이거, its final card; the user named it for the snipers called White Death and
/// White Feather): a headshot kill with the Head Hunter full rakes white claws across the head, staggers the enemies
/// round it, and for some seconds no hurt takes the stacks (ClassKit asks TigerGuards).</item>
/// <item>Point, Gunslinger (건슬링어): the gun just drawn hits harder a moment (ClassKit.DamageFactor); its time shows
/// on the class HUD's bar.</item>
/// </list>
/// The HUD's bar (ClassHUD) shows the card state under way: BarShare, BarWord, BarColor.
/// </summary>
[DisallowMultipleComponent]
public sealed class ClassCards : MonoBehaviour
{
    private const string GhostRow = PlayerClass.GhostRow, PointRow = "Point";

    /// <summary>This PC's own player's cards at work.</summary>
    public static ClassCards Local { get; private set; }

    private PlayerMovement movement;
    private ClassKit kit;
    // White Tiger: until when the stacks are safe and how long that lasts; when its claws may strike again; a headshot
    // kill waiting for its shot's stack to be counted (ClassKit counts it after the hit), and where.
    private float tigerUntil = -99f, tigerTotal = 1f, tigerAgain = -99f;
    private int tigerFrame = -1;
    private Vector3 tigerAt, tigerFrom;

    /// <summary>A Ghost's White Tiger holds: hurt takes no Head Hunter stack (ClassKit).</summary>
    public bool TigerGuards => tigerUntil > Time.time && PlayerClass.Is(PlayerClass.Ghost);
    public float TigerShare => TigerGuards ? Mathf.Clamp01((tigerUntil - Time.time) / tigerTotal) : 0f;

    /// <summary>The card state the class HUD's bar shows now, 0 to 1 (0: none), its word and its colour.</summary>
    public float BarShare
    {
        get
        {
            if (TigerShare > 0f) return TigerShare;
            return kit != null ? kit.GunslingerShare : 0f;
        }
    }

    public string BarWord => TigerShare > 0f ? Texts.Get("class.hud.tiger") : Texts.Get("class.hud.gunslinger");
    public Color BarColor => TigerShare > 0f ? KitStyle.Ice : KitStyle.Accent;

    private void Awake()
    {
        movement = GetComponent<PlayerMovement>();
        kit = GetComponent<ClassKit>();
    }

    private void OnEnable() => CombatDamageEvents.Damaged += OnDamaged;

    private void OnDisable()
    {
        CombatDamageEvents.Damaged -= OnDamaged;
        if (Local == this)
        {
            Local = null;
            CardNet.Stop();
        }
    }

    private void Update()
    {
        if (movement == null || !movement.IsLocal) return;
        Local = this;
        if (kit == null) kit = GetComponent<ClassKit>();
        // In a room, hear the others' card effects.
        var network = GameSession.Network;
        if (network != null && network.IsListening) CardNet.Listen();
    }

    // After the frame's shots are counted (ClassKit adds a headshot's stack once the shot is traced).
    private void LateUpdate()
    {
        if (tigerFrame < 0) return;
        tigerFrame = -1;
        if (kit != null && kit.Hunter >= kit.HunterMax) Tiger();
    }

    // Every hit of this player's on an enemy: Momentum goes on; a Ghost's headshot kill may wake the White Tiger.
    private void OnDamaged(CombatDamageReport report)
    {
        if (movement == null || !movement.IsLocal || report.Target == null || !PlayerPerks.IsMine(report.Hit.Source)) return;
        if (report.Target.GetComponentInParent<PlayerMovement>() != null) return;
        PlayerPerks.Struck();
        if (report.IsKillingBlow && report.Hit.Method == DamageMethod.Bullet && report.Hit.IsCritical
            && PlayerClass.Is(PlayerClass.Ghost) && PlayerPerks.Has(PlayerPerks.GhostTiger))
        {
            tigerFrame = Time.frameCount;
            tigerAt = report.Hit.Point;
            tigerFrom = report.Hit.Origin ?? transform.position + Vector3.up * 1.5f;
        }
    }

    // ---- Ghost: White Tiger ----

    // The stacks are safe a while; the claws, and the stagger round the kill, at most once in a while.
    private void Tiger()
    {
        tigerTotal = Mathf.Max(.1f, PlayerPerks.Total(PlayerPerks.GhostTiger));
        tigerUntil = Time.time + tigerTotal;
        if (Time.time < tigerAgain) return;
        tigerAgain = Time.time + Mathf.Max(0f, PlayerClass.Ability(GhostRow, "tigerRest", 1.5f));
        CardNet.Show(CardFx.Kind.Claws, tigerAt, tigerAt - tigerFrom, 1f);
        KitSound.Play(KitSound.Cue.Hunter, 1f);
        float reach = Mathf.Max(0f, PlayerClass.Ability(GhostRow, "tigerReach", 6f)), seconds = PlayerClass.Ability(GhostRow, "tigerStagger", 1.2f);
        if (reach <= 0f || seconds <= 0f) return;
        foreach (var enemy in EnemyHealth.Living)
        {
            if (enemy == null || !enemy.IsAlive || (enemy.transform.position - tigerAt).sqrMagnitude > reach * reach) continue;
            if (enemy.TryGetComponent(out MeleeCombatTarget target) && target.IsAvailable) target.Interrupt(seconds);
        }
    }
}
