using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// A long fall's faint screen warp, after Beatrun's fall overlay: the picture's edges bend, fringe and darken a little
/// while the centre stays clear, and the bend breathes slowly. It comes in over rampTime once a fall is a big one
/// (PlayerMovement.IsBigFall) and clears quickly on touchdown. A global post-processing volume built from
/// Resources/Rendering/FallWarp, which keeps those effects' shaders in the build; the strengths come from
/// Player.csv row FallScreen.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerFallScreen : MonoBehaviour, ITableBound
{
    [SerializeField, Range(-1f, 1f), Tooltip("Lens bend at the edges at full warp. Positive bulges, negative pinches.")]
    private float lensDistortion = .15f;
    [SerializeField, Range(0f, 1f), Tooltip("Colour fringing toward the edges at full warp.")]
    private float chromaticAberration = .3f;
    [SerializeField, Range(0f, 1f), Tooltip("Edge darkening at full warp.")]
    private float vignette = .35f;
    [SerializeField, Range(0f, 1f), Tooltip("Share of the lens bend that breathes in and out.")]
    private float breathe = .4f;
    [SerializeField, Min(0f), Tooltip("Breaths of the lens bend per second.")]
    private float breathRate = 1.2f;
    [SerializeField, Min(.01f), Tooltip("How long the warp takes to come in; to clear (s).")]
    private float rampTime = 2f, clearTime = .2f;

    private const string ProfilePath = "Rendering/FallWarp";
    // Above the scene's and the pipeline's own volumes.
    private const float Priority = 100f;

    private PlayerMovement player;
    private Volume volume;
    private VolumeProfile profile;
    private LensDistortion lens;
    private ChromaticAberration fringe;
    private Vignette edge;
    private float weight, breathTime;
    private bool missing;

    void ITableBound.OnTableApplied() => Apply();

    private void Awake() => player = GetComponent<PlayerMovement>();

    private void OnDisable()
    {
        weight = 0f;
        if (volume != null) volume.enabled = false;
    }

    private void OnDestroy()
    {
        if (volume != null) Destroy(volume.gameObject);
        if (profile == null) return;
        foreach (var component in profile.components) Destroy(component);
        Destroy(profile);
    }

    private void LateUpdate()
    {
        float dt = Time.deltaTime;
        if (GamePause.IsPaused || dt <= 0f || player == null || !player.IsInitialized) return;
        bool falling = player.IsBigFall;
        weight = Mathf.MoveTowards(weight, falling ? 1f : 0f, dt / (falling ? rampTime : clearTime));
        if (weight <= 0f)
        {
            if (volume != null) volume.enabled = false;
            breathTime = 0f;
            return;
        }
        if (!Build()) return;
        breathTime += dt;
        float breath = .5f - .5f * Mathf.Cos(breathTime * breathRate * 2f * Mathf.PI);
        lens.intensity.value = lensDistortion * (1f - breathe * breath);
        volume.weight = weight;
        volume.enabled = true;
    }

    private bool Build()
    {
        if (volume != null) return true;
        if (missing) return false;
        var asset = Resources.Load<VolumeProfile>(ProfilePath);
        if (asset == null)
        {
            missing = true;
            Debug.LogWarning($"[PlayerFallScreen] Resources/{ProfilePath} is missing; long falls have no screen warp.", this);
            return false;
        }
        // A global volume counts only on a layer the player's camera listens to.
        var view = GetComponentInChildren<Camera>();
        int mask = view != null ? view.GetUniversalAdditionalCameraData().volumeLayerMask.value : 1;
        var host = new GameObject("Fall Warp");
        RuntimeSceneHierarchy.Group(host, "EFFECTS", "Screen", gameObject);
        host.layer = LowestLayer(mask);
        volume = host.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.priority = Priority;
        volume.weight = 0f;
        volume.sharedProfile = asset;
        // The volume's own deep copy, so the breathing never writes into the asset.
        profile = volume.profile;
        if (!profile.TryGet(out lens)) lens = profile.Add<LensDistortion>();
        if (!profile.TryGet(out fringe)) fringe = profile.Add<ChromaticAberration>();
        if (!profile.TryGet(out edge)) edge = profile.Add<Vignette>();
        Apply();
        return true;
    }

    private void Apply()
    {
        if (profile == null) return;
        lens.intensity.Override(lensDistortion);
        fringe.intensity.Override(chromaticAberration);
        edge.intensity.Override(vignette);
    }

    private static int LowestLayer(int mask)
    {
        for (int layer = 0; layer < 32; layer++)
            if ((mask & (1 << layer)) != 0) return layer;
        return 0;
    }
}
