using System;
using UnityEngine;

/// <summary>
/// The death flow and our death card over the MW3 Death Screen addon (2026-11-22, the user, after seeing the addon:
/// "여기에 우리 데스 정보 보여주는 거 합치면 돼"). The screen itself (the world's colour drained, the blur, the vignette,
/// the blood, the whistle, clearing in .3 s on coming back) is the addon's, drawn as it is by HudAddons.DeathScreenModule;
/// this decides when it shows and puts the card over the HUD (DeathCard): 사망 (전멸 for a wipe), what killed them (the
/// zombie's kind, an elite marked, how it did it), how long they lasted, their kills this life, the money they hold, and
/// what comes next. The card leaves with the screen, in its clearing time.
/// Only at a real death, never on going down. In a room it plays while the view is still in the fallen body's eyes and
/// hands over as soon as spectating starts (PlayerSpectate: a click, or Spectate.csv deathViewSeconds); the screen clears
/// then and its whistle plays out. Alone, death is the wipe, so it holds until everyone is back. A wipe that comes later
/// shows it again as 전멸 for everyone, the downed and the spectating included. Coming back (a respawn, a wave, a
/// Détente's Resurrection) clears it as the addon does, the whistle cut.
/// While it is up, PlayerLifeHUD's own middle line waits under it (Covers); its spectate keys stay. Teammates already
/// hear of a death: "X 사망" at the top of their screen, a kill feed row and a sound (PlayerTally).
/// Settings → HUD → 데스 스크린: the addon's own panel (its Enable switches the screen and the card together).
/// Numbers of ours: Tables/DeathScreen (how long it holds, the preview's time). Console: /death previews it without dying.
/// This PC's own player only.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.DeathScreen)]
public sealed partial class DeathScreen : MonoBehaviour, IHudPreview
{
    public const string TableName = "DeathScreen";
    private const string Row = "Full";
    // A blow this recent (s) is what killed them; older, it only put them down and they bled out.
    private const float RecentBlow = 1.5f;

    /// <summary>Tables/DeathScreen: how long the card holds. The look and the sound are the addon's
    /// (Tables/Hud/AddonDeathScreen).</summary>
    [Serializable]
    public sealed class Look
    {
        [Tooltip("Seconds it holds before leaving; 0 holds until spectating starts, they live again or the wipe ends.")]
        public float holdSeconds;
        [Tooltip("How long /death's preview holds (s; the addon's preview is 5).")]
        public float previewSeconds = 5f;
    }

    private enum Phase : byte { Hidden, Showing, Leaving }

    private readonly Look look = new Look();
    private readonly DeathCard card = new DeathCard();

    private PlayerMovement player;
    private PlayerDamageReceiver receiver;
    private PlayerLife life;
    private PlayerSpectate spectate;
    private PlayerTally tally;
    private bool held;

    private Phase phase;
    private bool wipe, previewing, previewWipe;
    private float shownAt, leaveAt, leaveFor = .3f, previewUntil;
    // What the countdown line's hairline runs down from (s).
    private float respawnFrom, wipeFrom;

    // This life: since when (game time) and with how many kills already.
    private float lifeStart;
    private int killsAtStart;
    private bool started, wasDead, wasWiped, wasDowned;
    // The last blow that hurt them, and who put them down (in their colour), for a death by bleeding.
    private DamageHit lastBlow;
    private float lastBlowAt = float.NegativeInfinity;
    private string downedBy;
    // Their own death's card, for a wipe that comes while they are watching.
    private DeathCard.Facts lastFacts;
    private bool hasFacts;

    /// <summary>The death screen is up (or leaving): PlayerLifeHUD keeps its own middle line under it.</summary>
    public bool Covers => phase != Phase.Hidden;

    private void Awake()
    {
        player = GetComponent<PlayerMovement>();
        receiver = GetComponent<PlayerDamageReceiver>();
        life = GetComponent<PlayerLife>();
        GameTables.BindNested(TableName, Row, this, look);
    }

    private void OnEnable()
    {
        if (receiver != null) receiver.HitResolved += OnBlow;
    }

    private void OnDisable()
    {
        if (receiver != null) receiver.HitResolved -= OnBlow;
        if (phase != Phase.Hidden) Finish();
        // The player is going: the addon's screen goes with them at once, its whistle too (the addon's ShutDown).
        HudAddons.DeathScreenModule.Stop();
        held = false;
    }

    private void OnDestroy() => card.Destroy();
}
