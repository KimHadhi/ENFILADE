using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// One's own screen at low health: the colour drains toward grey as health falls, and every hit that lands brings it
/// back for a moment with a red cast, so a hit still reads when the edges are already full of blood. It sits over the
/// kill screen and lets only part of it through (killShowThrough), so a kill still pulses but never brings the full
/// colour back and passes for being healthy again. Takes a PostProcessLoan only while it shows. Tuning: Tables/LowHealth.
/// </summary>
[DisallowMultipleComponent]
public sealed class LowHealthGrade : MonoBehaviour
{
    public const string TableName = "LowHealth", TableRow = "Default";
    // Over the kill screen (200). The weight is how drained the screen is, so what lies under it shows through in step.
    private const float Priority = 200.5f;

    [SerializeField, Range(0f, 1f), Tooltip("Health share where the colour starts to drain.")]
    private float greyFrom = .35f;
    [SerializeField, Range(0f, 1f), Tooltip("Health share at and below which it is fully drained.")]
    private float greyFull = .1f;
    [SerializeField, Range(-100f, 0f), Tooltip("Saturation when fully drained (-100 is black and white).")]
    private float greySaturation = -75f;
    [SerializeField, Min(.05f), Tooltip("How long a hit's colour lasts (real seconds).")]
    private float flashSeconds = .35f;
    [SerializeField, Tooltip("Colour filter at the peak of a hit, as strong as the drain is.")]
    private Color flashColor = new Color(1f, .48f, .48f);
    [SerializeField, Range(0f, 1f), Tooltip("Share of a kill screen's look let through at its peak (1 shows it as when healthy).")]
    private float killShowThrough = .4f;

    private PlayerDamageReceiver receiver;
    private Volume volume;
    private ColorAdjustments color;
    private float grey, flash;

    private void Awake()
    {
        receiver = GetComponent<PlayerDamageReceiver>();
        GameTables.Bind(TableName, TableRow, this);
        var host = new GameObject("Low Health Grade");
        host.transform.SetParent(transform, false);
        volume = host.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = Priority;
        volume.weight = 1f;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "Low Health Grade (Runtime)";
        profile.hideFlags = HideFlags.HideAndDontSave;
        color = profile.Add<ColorAdjustments>(false);
        color.saturation.overrideState = color.colorFilter.overrideState = true;
        volume.sharedProfile = profile;
        volume.enabled = false;
    }

    private void OnEnable() { if (receiver != null) receiver.HitResolved += OnHit; }

    private void OnDisable()
    {
        if (receiver != null) receiver.HitResolved -= OnHit;
        grey = flash = 0f;
        Show(false);
    }

    private void OnDestroy() { if (volume != null && volume.sharedProfile != null) Destroy(volume.sharedProfile); }

    private void OnHit(DamageHit hit, DamageResult result)
    {
        if (result == DamageResult.Damaged && grey > 0f) flash = 1f;
    }

    private void Update()
    {
        if (receiver == null) return;
        float dt = Time.unscaledDeltaTime;
        float share = receiver.Health / Mathf.Max(1f, receiver.MaximumHealth);
        float target = receiver.IsAlive ? 1f - Mathf.InverseLerp(greyFull, Mathf.Max(greyFull + .01f, greyFrom), share) : 0f;
        grey = Mathf.MoveTowards(grey, target, dt * 2f);
        flash = Mathf.Max(0f, flash - dt / flashSeconds);
        bool showing = grey > .001f;
        Show(showing);
        if (!showing) return;
        float pop = flash * flash;
        volume.weight = grey * (1f - killShowThrough * KillConfirmEffect.Showing);
        color.saturation.value = Mathf.Lerp(greySaturation, 0f, pop);
        color.colorFilter.value = Color.Lerp(Color.white, flashColor, pop);
    }

    private void Show(bool on)
    {
        if (volume == null || volume.enabled == on) return;
        volume.enabled = on;
        if (on) PostProcessLoan.Take(this); else PostProcessLoan.Return(this);
    }
}
