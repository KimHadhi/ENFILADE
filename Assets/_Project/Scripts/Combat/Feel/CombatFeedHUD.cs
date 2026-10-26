using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Call of Duty (MW3)-style kill rewards. Right of the crosshair a "110 XP" number slams in on each kill, with the
/// events listed beside it (☠ 처치, 헤드샷, 원거리 사격, 처형, 반격, 더블 킬 ...). The number accumulates over a kill
/// streak and the group fades after a quiet spell. Only kills score, so sustained fire stays quiet.
/// A rolling XP total and a kill feed sit at the top right. Created on first use during Play; GunFeel → Score.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(470)]
public sealed class CombatFeedHUD : MonoBehaviour
{
    private const int MaxLines = 4, MaxKills = 5;
    private const float KillLifetime = 5f, LineHeight = 25f, KillRowHeight = 32f, FadeSeconds = .3f, IconSize = 17f;
    private static readonly Color Soft = new Color(.86f, .88f, .92f), KillRed = new Color(1f, .2f, .14f), Number = new Color(1f, .96f, .86f);
    private static CombatFeedHUD instance;

    public static long TotalScore { get; private set; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetScore() => TotalScore = 0;

    private enum LineIcon { None, Skull, Headshot }
    private sealed class Line { public RectTransform root; public TextMeshProUGUI label; public RawImage icon; public float age; public bool accent; }
    private sealed class KillRow
    {
        public RectTransform root; public CanvasGroup group; public Image accent;
        public TextMeshProUGUI weapon, target; public RawImage icon; public float age, y;
    }

    private RectTransform streakRoot, linesRoot, totalRoot, feedRoot;
    private TextMeshProUGUI streakLabel, totalLabel;
    private CanvasGroup streakGroup;
    private readonly List<Line> lines = new List<Line>();
    private readonly Stack<Line> spareLines = new Stack<Line>();
    private readonly List<KillRow> kills = new List<KillRow>();
    private readonly Stack<KillRow> spareKills = new Stack<KillRow>();
    private int streakScore, multiKill;
    private bool fresh;
    private float streakIdle = 99f, popAge = 99f, totalShown, totalPop, lastKillTime = -99f;

    public string StreakText => streakLabel != null ? streakLabel.text : string.Empty;

    private static CombatFeedHUD Get()
    {
        if (instance == null)
        {
            var host = new GameObject("Combat Feed", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            instance = host.AddComponent<CombatFeedHUD>();
        }
        return instance;
    }

    /// <summary>distance: shooter to hit point, metres (0 when unknown); gun kills past Long Range Distance earn a bonus.</summary>
    public static void Kill(string weapon, string target, bool headshot, bool execution, bool counter, bool bullet, float distance)
    {
        if (!Application.isPlaying) return;
        var settings = GunFeelSettings.Instance;
        var self = Get();
        float now = Time.unscaledTime;
        self.multiKill = now - self.lastKillTime <= settings.multiKillWindow ? self.multiKill + 1 : 1;
        self.lastKillTime = now;
        if (settings.scoreFeed)
        {
            // A new streak starts clean; within a streak the newest kill's events replace the list, the number keeps adding.
            self.fresh = self.streakIdle > settings.scoreStreakSeconds + FadeSeconds || self.streakScore == 0;
            if (self.fresh) self.streakScore = 0;
            self.ClearLines();
            int gained = settings.killScore;
            self.AddLine("처치", LineIcon.Skull, false);
            if (headshot) { gained += settings.headshotBonus; self.AddLine("헤드샷", LineIcon.Headshot, true); }
            if (bullet && distance >= settings.longRangeDistance) { gained += settings.longRangeBonus; self.AddLine("원거리 사격", LineIcon.None, false); }
            if (execution) { gained += settings.executionBonus; self.AddLine("처형", LineIcon.None, true); }
            if (counter) { gained += settings.counterBonus; self.AddLine("반격", LineIcon.None, true); }
            if (self.multiKill >= 2)
            {
                gained += settings.multiKillBonus * (self.multiKill - 1);
                self.AddLine(MultiKillName(self.multiKill), LineIcon.None, true);
            }
            TotalScore += gained;
            self.streakScore += gained;
            self.streakIdle = 0f; self.popAge = 0f; self.totalPop = 1f;
            self.streakLabel.text = self.streakScore + "<size=62%><color=#" + ColorUtility.ToHtmlStringRGB(settings.scoreColor) + "> XP</color></size>";        }
        if (settings.killFeed) self.AddKill(weapon, target, headshot);
    }

    private static string MultiKillName(int count)
        => count == 2 ? "더블 킬" : count == 3 ? "트리플 킬" : count == 4 ? "쿼드 킬" : "연속 처치 x" + count;

    private void Awake()
    {
        if (instance != null && instance != this) { Destroy(gameObject); return; }
        instance = this;
        FeelUI.SetupOverlay(gameObject, 27);

        streakRoot = FeelUI.Rect("Streak", transform);
        streakGroup = streakRoot.gameObject.AddComponent<CanvasGroup>();
        streakGroup.interactable = streakGroup.blocksRaycasts = false;
        streakGroup.alpha = 0f;
        // Number right-aligned so it grows leftward; the event list starts just right of it.
        streakLabel = FeelUI.ScoreLabel("Streak Score", streakRoot, 30f, TextAlignmentOptions.Right);
        streakLabel.rectTransform.pivot = new Vector2(1f, .5f);
        streakLabel.rectTransform.sizeDelta = new Vector2(220f, 40f);
        streakLabel.color = Number;
        linesRoot = FeelUI.Rect("Lines", streakRoot);
        linesRoot.anchoredPosition = new Vector2(16f, 0f);

        totalRoot = FeelUI.Rect("Total", transform);
        totalRoot.anchorMin = totalRoot.anchorMax = totalRoot.pivot = Vector2.one;
        totalRoot.anchoredPosition = new Vector2(-36f, -28f);
        totalRoot.sizeDelta = new Vector2(320f, 40f);
        var caption = FeelUI.ScoreLabel("Caption", totalRoot, 13f, TextAlignmentOptions.Right);
        caption.rectTransform.anchoredPosition = new Vector2(0f, 13f);
        caption.rectTransform.sizeDelta = new Vector2(320f, 16f);
        caption.text = "XP"; caption.color = new Color(1f, 1f, 1f, .55f); caption.characterSpacing = 8f;
        totalLabel = FeelUI.ScoreLabel("Value", totalRoot, 28f, TextAlignmentOptions.Right);
        totalLabel.rectTransform.sizeDelta = new Vector2(320f, 34f);
        totalLabel.rectTransform.pivot = new Vector2(1f, .5f);
        totalLabel.rectTransform.anchoredPosition = new Vector2(160f, -8f);
        totalRoot.gameObject.SetActive(false);

        feedRoot = FeelUI.Rect("Kill Feed", transform);
        feedRoot.anchorMin = feedRoot.anchorMax = feedRoot.pivot = Vector2.one;
        feedRoot.anchoredPosition = new Vector2(-36f, -80f);
    }

    private void OnDestroy() { if (instance == this) instance = null; }

    private void LateUpdate()
    {
        float dt = Time.unscaledDeltaTime;
        var settings = GunFeelSettings.Instance;
        streakIdle += dt; popAge += dt;

        // A new streak slams in from far larger; each later kill punches the number again. Fades upward when quiet.
        float fadeT = Mathf.Clamp01((streakIdle - settings.scoreStreakSeconds) / FadeSeconds);
        streakGroup.alpha = streakScore > 0 ? Mathf.Clamp01(popAge / .04f + (fresh ? 0f : 1f)) * (1f - fadeT) : 0f;
        streakRoot.anchoredPosition = settings.scoreOffset + new Vector2(0f, fadeT * 10f);
        float pop = 1f - Mathf.Pow(1f - Mathf.Clamp01(popAge / .13f), 3f);
        streakLabel.rectTransform.localScale = Vector3.one * Mathf.Lerp(fresh ? 1.9f : 1.35f, 1f, pop);
        streakLabel.color = Color.Lerp(Color.white, Number, Mathf.Clamp01(popAge / .1f));
        if (fadeT >= 1f && streakScore > 0) { streakScore = 0; ClearLines(); }

        for (int i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            line.age += dt;
            // Events tick in one after another, each sliding from the left.
            float delayed = line.age - i * .05f;
            float enter = 1f - Mathf.Pow(1f - Mathf.Clamp01(delayed / .12f), 3f);
            line.root.anchoredPosition = new Vector2((1f - enter) * -14f, -i * LineHeight + (lines.Count - 1) * LineHeight * .5f - 2f);
            line.root.localScale = Vector3.one * Mathf.Lerp(1.25f, 1f, enter);
            Color color = Color.Lerp(Color.white, line.accent ? settings.scoreColor : Soft, Mathf.Clamp01(delayed / .12f));
            color.a = enter;
            line.label.color = color;
            line.icon.color = new Color(1f, 1f, 1f, enter);
        }

        if (TotalScore > 0 && !totalRoot.gameObject.activeSelf) totalRoot.gameObject.SetActive(true);
        totalShown = totalShown < TotalScore ? Mathf.Min(TotalScore, totalShown + Mathf.Max(80f * dt, (TotalScore - totalShown) * (1f - Mathf.Exp(-10f * dt)))) : TotalScore;
        totalPop = Mathf.MoveTowards(totalPop, 0f, dt * 3.5f);
        totalLabel.text = Mathf.RoundToInt(totalShown).ToString("N0");
        totalLabel.rectTransform.localScale = Vector3.one * (1f + totalPop * totalPop * .25f);
        totalLabel.color = Color.Lerp(Color.white, settings.scoreColor, totalPop);

        for (int i = kills.Count - 1; i >= 0; i--)
        {
            var row = kills[i];
            row.age += dt;
            if (row.age >= KillLifetime) { row.root.gameObject.SetActive(false); spareKills.Push(row); kills.RemoveAt(i); }
        }
        for (int i = 0; i < kills.Count; i++)
        {
            var row = kills[i];
            float targetY = -(kills.Count - 1 - i) * (KillRowHeight + 5f);
            row.y = Mathf.Lerp(row.y, targetY, 1f - Mathf.Exp(-18f * dt));
            float enter = 1f - Mathf.Pow(1f - Mathf.Clamp01(row.age / .18f), 3f);
            row.root.anchoredPosition = new Vector2((1f - enter) * 80f, row.y);
            row.group.alpha = enter * Mathf.Clamp01((KillLifetime - row.age) / .4f);
            // The accent bar flares wide on arrival.
            float flash = 1f - Mathf.Clamp01(row.age / .25f);
            row.accent.rectTransform.localScale = new Vector3(1f + flash * 1.5f, 1f, 1f);
        }
    }

    private void ClearLines()
    {
        foreach (var line in lines) { line.root.gameObject.SetActive(false); spareLines.Push(line); }
        lines.Clear();
    }

    private void AddLine(string text, LineIcon icon, bool accent)
    {
        if (lines.Count >= MaxLines) return;
        Line line;
        if (spareLines.Count > 0) line = spareLines.Pop();
        else
        {
            line = new Line { root = FeelUI.Rect("Line", linesRoot) };
            line.root.pivot = new Vector2(0f, .5f);
            line.icon = FeelUI.Rect("Icon", line.root).gameObject.AddComponent<RawImage>();
            line.icon.raycastTarget = false;
            line.icon.rectTransform.sizeDelta = new Vector2(IconSize, IconSize);
            line.label = FeelUI.ScoreLabel("Label", line.root, 17f, TextAlignmentOptions.Left);
            line.label.rectTransform.pivot = new Vector2(0f, .5f);
            line.label.rectTransform.sizeDelta = new Vector2(240f, LineHeight);
        }
        bool hasIcon = icon != LineIcon.None;
        line.icon.gameObject.SetActive(hasIcon);
        if (hasIcon) line.icon.texture = icon == LineIcon.Skull ? CombatUIAssets.Skull : CombatUIAssets.CriticalBurst;
        line.icon.rectTransform.anchoredPosition = new Vector2(IconSize * .5f, 0f);
        // Labels align in one column whether or not the line has an icon.
        line.label.rectTransform.anchoredPosition = new Vector2(IconSize + 6f, 0f);
        line.label.text = text; line.accent = accent; line.age = 0f;
        line.root.gameObject.SetActive(true);
        lines.Add(line);
    }

    private void AddKill(string weapon, string target, bool headshot)
    {
        if (kills.Count >= MaxKills)
        {
            var oldest = kills[0]; kills.RemoveAt(0);
            oldest.root.gameObject.SetActive(false); spareKills.Push(oldest);
        }
        var settings = GunFeelSettings.Instance;
        var row = spareKills.Count > 0 ? spareKills.Pop() : BuildRow();
        row.weapon.text = weapon;
        row.target.text = target;
        row.icon.texture = headshot ? CombatUIAssets.CriticalBurst : CombatUIAssets.Skull;
        row.icon.color = headshot ? settings.scoreColor : Color.white;
        row.accent.color = headshot ? settings.scoreColor : KillRed;

        // Right-aligned: [weapon] [icon] [target] with the accent bar at the far right.
        const float pad = 12f, icon = 18f, gap = 8f, accent = 4f;
        float targetWidth = row.target.GetPreferredValues(target).x, weaponWidth = row.weapon.GetPreferredValues(weapon).x;
        float width = pad + weaponWidth + gap + icon + gap + targetWidth + pad + accent;
        row.root.sizeDelta = new Vector2(width, KillRowHeight);
        float x = -accent - pad;
        Place(row.target.rectTransform, x - targetWidth * .5f, targetWidth + 4f); x -= targetWidth + gap;
        Place(row.icon.rectTransform, x - icon * .5f, icon, icon); x -= icon + gap;
        Place(row.weapon.rectTransform, x - weaponWidth * .5f, weaponWidth + 4f);
        row.age = 0f; row.y = 0f;
        row.root.gameObject.SetActive(true);
        kills.Add(row);
    }

    private KillRow BuildRow()
    {
        var row = new KillRow();
        row.root = FeelUI.Rect("Kill", feedRoot);
        row.root.anchorMin = row.root.anchorMax = row.root.pivot = Vector2.one;
        row.group = row.root.gameObject.AddComponent<CanvasGroup>();
        row.group.interactable = row.group.blocksRaycasts = false;
        var background = row.root.gameObject.AddComponent<Image>();
        background.color = new Color(.04f, .04f, .06f, .5f); background.raycastTarget = false;
        row.accent = FeelUI.Rect("Accent", row.root).gameObject.AddComponent<Image>();
        var accentRect = row.accent.rectTransform;
        accentRect.anchorMin = new Vector2(1f, 0f); accentRect.anchorMax = Vector2.one; accentRect.pivot = new Vector2(1f, .5f);
        accentRect.sizeDelta = new Vector2(4f, 0f); accentRect.anchoredPosition = Vector2.zero;
        row.accent.raycastTarget = false;
        row.weapon = FeelUI.ScoreLabel("Weapon", row.root, 15f, TextAlignmentOptions.Center);
        row.weapon.color = Soft;
        row.icon = FeelUI.Rect("Icon", row.root).gameObject.AddComponent<RawImage>();
        row.icon.raycastTarget = false;
        row.target = FeelUI.ScoreLabel("Target", row.root, 17f, TextAlignmentOptions.Center);
        row.target.color = Color.white;
        foreach (var rect in new[] { row.weapon.rectTransform, row.icon.rectTransform, row.target.rectTransform })
            rect.anchorMin = rect.anchorMax = new Vector2(1f, .5f);
        return row;
    }

    private static void Place(RectTransform rect, float x, float width, float height = KillRowHeight)
    {
        rect.anchoredPosition = new Vector2(x, 0f);
        rect.sizeDelta = new Vector2(width, height);
    }
}
