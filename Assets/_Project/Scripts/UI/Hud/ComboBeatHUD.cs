using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The fists' beat round the crosshair (MeleeCombos, the user's #31 timed chain): when a punch lands, a white ring closes
/// in on a thin one at the crosshair and meets it on the beat; while a press would be on the beat the thin ring lights.
/// A press on the beat bursts a pink ring outward, the finisher a wide white-cored one, and the chain's steps show as
/// small diamonds under the crosshair, filling pink as they come. A press off the beat, or none, lets it all fade: no
/// mark of a miss, only the chance gone. Short and bright, in the HUD's black, white and pink. Real time, so a hit
/// stop doesn't hold the flash. Made by MeleeCombos when the fists first come out.
/// </summary>
[DisallowMultipleComponent]
public sealed class ComboBeatHUD : MonoBehaviour
{
    // Canvas px at 1080p: the thin ring the beat closes on, where the closing ring starts, the diamonds under them.
    private const float Meet = 22f, From = 58f, PipY = -40f, PipSize = 9f, PipGap = 14f;
    private const int MaxPips = 6;
    // The burst on the beat: how far it grows (a step, the finisher) and how long (real s).
    private const float BurstStep = 44f, BurstFinish = 86f, BurstSeconds = .18f, FinishSeconds = .26f;
    // How fast the whole fades in and out (alpha per real s).
    private const float FadeIn = 20f, FadeOut = 9f;

    private MeleeCombos combos;
    private GameObject root;
    private CanvasGroup group;
    private CrosshairRing meet, closing, burst;
    private readonly DiamondGraphic[] pips = new DiamondGraphic[MaxPips];
    private float burstTime = 1f, burstSeconds = BurstSeconds, burstReach = BurstStep, alpha;
    private bool burstFinisher;
    private int shownSteps;

    private void Awake()
    {
        combos = GetComponent<MeleeCombos>();
        root = new GameObject("Combo beat", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(CanvasGroup));
        root.transform.SetParent(transform, false);
        var canvas = root.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 42;
        var scaler = root.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f); scaler.matchWidthOrHeight = 1f;
        group = root.GetComponent<CanvasGroup>();
        group.alpha = 0f; group.blocksRaycasts = group.interactable = false;
        meet = CrosshairRing.Create(root.transform);
        closing = CrosshairRing.Create(root.transform);
        burst = CrosshairRing.Create(root.transform);
        for (int i = 0; i < pips.Length; i++)
        {
            pips[i] = HudShapes.Make<DiamondGraphic>("Beat step " + (i + 1), root.transform, KitStyle.Faint);
            pips[i].rectTransform.sizeDelta = Vector2.one * PipSize;
            pips[i].Shape(0f, .3f);
            pips[i].enabled = false;
        }
        burst.Set(0f, 1f, Color.clear, 0f, Color.clear);
    }

    private void OnEnable()
    {
        if (combos == null) combos = GetComponent<MeleeCombos>();
        if (combos == null) return;
        combos.BeatHit -= OnBeatHit; combos.BeatHit += OnBeatHit;
        combos.BeatLost -= OnBeatLost; combos.BeatLost += OnBeatLost;
    }

    private void OnDisable()
    {
        if (combos != null) { combos.BeatHit -= OnBeatHit; combos.BeatLost -= OnBeatLost; }
        if (group != null) group.alpha = alpha = 0f;
    }

    private void OnDestroy() { if (root != null) Destroy(root); }

    private void OnBeatHit(int step, bool finisher)
    {
        burstTime = 0f; burstFinisher = finisher;
        burstSeconds = finisher ? FinishSeconds : BurstSeconds;
        burstReach = finisher ? BurstFinish : BurstStep;
        shownSteps = step;
        alpha = 1f;
    }

    private void OnBeatLost() => shownSteps = 0;

    private void LateUpdate()
    {
        if (combos == null || group == null || Time.timeScale <= 0f) return;
        float dt = Mathf.Min(.05f, Time.unscaledDeltaTime);
        bool open = combos.BeatOpen;
        bool bursting = burstTime < 1f;
        float wanted = open || bursting ? 1f : 0f;
        alpha = Mathf.MoveTowards(alpha, wanted, dt * (wanted > alpha ? FadeIn : FadeOut));
        group.alpha = alpha;
        if (alpha <= 0f && !open) { shownSteps = 0; return; }

        if (open)
        {
            float lead = combos.BeatLead, span = combos.BeatSpan;
            bool onBeat = lead <= combos.BeatEarly && lead >= -combos.BeatLate;
            // Closing in linearly to meet the thin ring on the beat, then a little past it as the window runs out.
            float radius = lead >= 0f ? Mathf.Lerp(Meet, From, Mathf.Clamp01(lead / span))
                : Mathf.Lerp(Meet, Meet * .7f, Mathf.Clamp01(-lead / Mathf.Max(.01f, combos.BeatLate)));
            meet.Set(Meet, onBeat ? 3f : 1.5f, KitStyle.With(onBeat ? KitStyle.Ice : KitStyle.Soft, onBeat ? .95f : .45f), 0f, Color.clear);
            closing.Set(radius, 2f, KitStyle.With(KitStyle.Ice, .85f), 0f, Color.clear);
        }
        else
        {
            meet.Set(Meet, 1.5f, KitStyle.With(KitStyle.Soft, .3f), 0f, Color.clear);
            closing.Set(0f, 1f, Color.clear, 0f, Color.clear);
        }

        if (bursting)
        {
            burstTime = Mathf.Min(1f, burstTime + dt / burstSeconds);
            float grow = 1f - (1f - burstTime) * (1f - burstTime);
            var tint = burstFinisher && burstTime < .35f ? KitStyle.Ice : KitStyle.Accent;
            burst.Set(Mathf.Lerp(Meet, burstReach, grow), Mathf.Lerp(burstFinisher ? 7f : 4f, 1f, burstTime), KitStyle.With(tint, 1f - burstTime),
                0f, Color.clear);
        }
        else burst.Set(0f, 1f, Color.clear, 0f, Color.clear);

        // The chain's steps: filled pink as they come, the rest faint, only while a chain is under way.
        int steps = Mathf.Clamp(combos.BeatSteps, 1, pips.Length);
        bool chain = open || shownSteps > 0;
        float left = -(steps - 1) * PipGap * .5f;
        for (int i = 0; i < pips.Length; i++)
        {
            bool shown = chain && i < steps;
            pips[i].enabled = shown;
            if (!shown) continue;
            bool done = i < shownSteps;
            pips[i].rectTransform.anchoredPosition = new Vector2(left + i * PipGap, PipY);
            pips[i].color = done ? KitStyle.Accent : KitStyle.Faint;
            pips[i].Shape(done ? .6f : 0f, .3f);
        }
        // The finisher's burst done, the chain clears.
        if (!bursting && burstFinisher && !open) { shownSteps = 0; burstFinisher = false; }
    }
}
