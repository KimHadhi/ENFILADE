using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// What the screen says about staying alive: the wipe, being down, waiting to come back, and getting someone up. While
/// someone is being got up, both of them see a panel with who it is, the seconds left and a filling bar. Dead in a room,
/// the bottom of the screen says who they watch and which keys change it (PlayerSpectate, backlog 722).
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerLifeHUD : MonoBehaviour
{
    private const float PromptSize = 27f, WipeSize = 96f, PanelWidth = 440f, PanelHeight = 62f, BarHeight = 5f, Inset = 24f, Shear = 9f;
    private static readonly Color Pale = new Color(1f, .94f, .9f), WipeRed = new Color(.86f, .16f, .12f);
    private static readonly Color Plate = new Color(0f, 0f, 0f, .6f), Track = new Color(.34f, .91f, .72f, .18f);
    private static readonly Color Keys = new Color(1f, .94f, .9f, .72f);
    private const float WatchingSize = 30f, KeysSize = 19f, WatchingY = -380f, KeysY = -416f;
    private PlayerLife life;
    private PlayerRevive revive;
    private PlayerDamageReceiver receiver;
    private PlayerSpectate spectate;
    private DeathScreen screen;
    private TextMeshProUGUI label, who, secondsLeft, watching, keys;
    private RectTransform panel, fill;
    private GameObject canvas;

    private void Awake()
    {
        life = GetComponent<PlayerLife>();
        revive = GetComponent<PlayerRevive>();
        receiver = GetComponent<PlayerDamageReceiver>();
    }

    private void LateUpdate()
    {
        string text = Message(out float size, out Color color);
        if (text == null && canvas == null) return;
        if (canvas == null) Build();
        canvas.SetActive(text != null);
        if (text == null) return;
        // While the death screen is up its card says this (DeathScreen): the middle line waits under it, the spectate keys
        // at the bottom stay.
        if (screen == null) TryGetComponent(out screen);
        label.enabled = screen == null || !screen.Covers;
        label.text = text;
        label.fontSize = size;
        label.color = color;
        ShowRescue();
        ShowSpectate();
    }

    private string Message(out float size, out Color color)
    {
        size = PromptSize;
        color = Pale;
        if (life.IsWiped) { size = WipeSize; color = WipeRed; return Texts.Get("life.wipe"); }
        if (receiver.IsDowned)
            return life.Rescuer != null ? Texts.Get("life.downed.helped") : Texts.Format("life.downed.waiting", Mathf.CeilToInt(life.BleedLeft));
        if (!receiver.IsAlive)
            return TowerRun.KeepsDead ? Texts.Get("life.respawn.tower")
                : life.WaitsForWave ? Texts.Get("life.respawn.next_wave") : Texts.Format("life.respawn.seconds", Mathf.Max(1, Mathf.CeilToInt(life.RespawnLeft)));
        return revive != null && revive.Candidate != null ? Texts.Get("life.revive.prompt") : null;
    }

    private void ShowRescue()
    {
        bool beingHelped = !life.IsWiped && life.Rescuer != null;
        bool helping = !life.IsWiped && !beingHelped && revive != null && revive.Candidate != null && revive.Progress > 0f;
        panel.gameObject.SetActive(beingHelped || helping);
        if (!beingHelped && !helping) return;
        float progress = beingHelped ? life.RescueProgress : revive.Progress;
        who.text = beingHelped ? Texts.Format("life.rescued_by", PlayerTally.ColoredNameOf(life.Rescuer)) : Texts.Format("life.rescuing", PlayerTally.ColoredNameOf(revive.Candidate));
        secondsLeft.text = Texts.Format("hud.seconds", ((1f - progress) * revive.Seconds).ToString("0.0"));
        fill.sizeDelta = new Vector2((PanelWidth - Inset * 2f) * progress, BarHeight);
    }

    private void ShowSpectate()
    {
        if (spectate == null) TryGetComponent(out spectate);
        bool shown = spectate != null && !life.IsWiped && !receiver.IsAlive
            && (spectate.Waiting || spectate.Current != PlayerSpectate.Mode.None);
        watching.gameObject.SetActive(shown);
        keys.gameObject.SetActive(shown);
        if (!shown) return;
        switch (spectate.Current)
        {
            case PlayerSpectate.Mode.Follow:
                // The one watched may have just left; the next frame moves on.
                var target = spectate.Target;
                watching.text = target != null ? Texts.Format("life.spectate.watching", PlayerTally.ColoredNameOf(target)) : "";
                keys.text = Texts.Get("life.spectate.follow_keys");
                break;
            case PlayerSpectate.Mode.Free:
                watching.text = Texts.Get("life.spectate.free");
                keys.text = Texts.Get("life.spectate.free_keys");
                break;
            default:
                watching.text = "";
                keys.text = Texts.Get("life.spectate.wait_keys");
                break;
        }
    }

    private void Build()
    {
        canvas = new GameObject("Life HUD", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
        canvas.hideFlags = HideFlags.DontSave;
        canvas.transform.SetParent(transform, false);
        var layer = canvas.GetComponent<Canvas>();
        layer.renderMode = RenderMode.ScreenSpaceOverlay;
        layer.sortingOrder = 80;
        var scaler = canvas.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = .5f;

        label = new GameObject("Message", typeof(RectTransform), typeof(TextMeshProUGUI)).GetComponent<TextMeshProUGUI>();
        label.transform.SetParent(canvas.transform, false);
        label.rectTransform.sizeDelta = new Vector2(1200f, 160f);
        label.rectTransform.anchoredPosition = new Vector2(0f, -120f);
        var settings = CombatFeedbackSettings.Instance;
        if (settings != null && settings.promptFont != null) label.font = settings.promptFont;
        label.alignment = TextAlignmentOptions.Center;
        label.fontStyle = FontStyles.Bold;
        label.raycastTarget = false;
        BuildPanel();
        watching = BottomLabel("Watching", WatchingSize, WatchingY, Pale);
        keys = BottomLabel("Keys", KeysSize, KeysY, Keys);
    }

    private TextMeshProUGUI BottomLabel(string name, float size, float y, Color color)
    {
        var text = FeelUI.ScoreLabel(name, canvas.transform, size, TextAlignmentOptions.Center);
        text.rectTransform.sizeDelta = new Vector2(1200f, size * 1.6f);
        text.rectTransform.anchoredPosition = new Vector2(0f, y);
        text.color = color;
        text.raycastTarget = false;
        text.gameObject.SetActive(false);
        return text;
    }

    private void BuildPanel()
    {
        panel = FeelUI.Rect("Rescue", canvas.transform);
        panel.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        panel.anchoredPosition = new Vector2(0f, -182f);
        panel.gameObject.AddComponent<SlantedHealthGraphic>().Configure(1f, 1f, 1d, false, Shear, 0f, Plate, Color.clear, Color.clear);

        float halfWidth = PanelWidth * .5f, textWidth = PanelWidth - Inset * 2f - 96f;
        who = FeelUI.ScoreLabel("Who", panel, 20f, TextAlignmentOptions.MidlineLeft);
        who.rectTransform.sizeDelta = new Vector2(textWidth, 34f);
        who.rectTransform.anchoredPosition = new Vector2(Inset + textWidth * .5f - halfWidth, 8f);
        secondsLeft = FeelUI.ScoreLabel("Seconds", panel, 22f, TextAlignmentOptions.MidlineRight);
        secondsLeft.rectTransform.sizeDelta = new Vector2(96f, 34f);
        secondsLeft.rectTransform.anchoredPosition = new Vector2(halfWidth - Inset - 48f, 8f);
        secondsLeft.color = ObjectiveLogPanel.Accent;

        var track = FeelUI.Rect("Track", panel);
        track.sizeDelta = new Vector2(PanelWidth - Inset * 2f, BarHeight);
        track.anchoredPosition = new Vector2(0f, -17f);
        var trackFill = track.gameObject.AddComponent<Image>();
        trackFill.color = Track;
        trackFill.raycastTarget = false;
        fill = FeelUI.Rect("Fill", track);
        fill.anchorMin = fill.anchorMax = fill.pivot = new Vector2(0f, .5f);
        var barFill = fill.gameObject.AddComponent<Image>();
        barFill.color = ObjectiveLogPanel.Accent;
        barFill.raycastTarget = false;
    }

    private void OnDestroy()
    {
        if (canvas != null) Destroy(canvas);
    }
}
