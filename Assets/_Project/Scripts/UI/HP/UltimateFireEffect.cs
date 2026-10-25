using UnityEngine;
using UnityEngine.UI;

/// <summary>Plays the authored UI sprite clips according to ultimate readiness.</summary>
[ExecuteAlways, DisallowMultipleComponent]
public sealed class UltimateFireEffect : MonoBehaviour
{
    [SerializeField] private UltimateCharge source;
    [SerializeField] private Image fireImage;
    [SerializeField] private AnimationClip fireStart;
    [SerializeField] private AnimationClip fireLoop;
    [SerializeField] private AnimationClip fireEnd;
    [SerializeField, Min(0.01f)] private float playbackSpeed = 1f;

    public enum Phase { Hidden, Starting, Looping, Ending }
    public Phase CurrentPhase { get; private set; }
    private float elapsed;
    private Image owner;
    private Sprite originalSprite;
    private bool originalEnabled, originalRaycast, originalAnimatorEnabled;
    private Animator animator;

    private void LateUpdate()
    {
        if (Application.IsPlaying(gameObject)) RefreshNow(Time.unscaledDeltaTime);
    }

    public void RefreshNow(float deltaTime)
    {
        if (owner != null && owner != fireImage) StopPreview();
        if (source == null || fireImage == null || fireStart == null || fireLoop == null || fireEnd == null)
        {
            StopPreview();
            return;
        }
        if (owner == null)
        {
            owner = fireImage;
            originalSprite = owner.sprite;
            originalEnabled = owner.enabled;
            originalRaycast = owner.raycastTarget;
            animator = owner.GetComponent<Animator>();
            if (animator != null) { originalAnimatorEnabled = animator.enabled; animator.enabled = false; }
            owner.raycastTarget = false;
            CurrentPhase = Phase.Hidden;
            elapsed = 0f;
        }

        UltimateCharge charge = UltimateCharge.ResolveHUDSource(this, source);
        bool ready = charge.isActiveAndEnabled && charge.IsReady;
        bool changed = false;
        if (ready && (CurrentPhase == Phase.Hidden || CurrentPhase == Phase.Ending))
        { CurrentPhase = Phase.Starting; elapsed = 0f; changed = true; }
        else if (!ready && (CurrentPhase == Phase.Starting || CurrentPhase == Phase.Looping))
        { CurrentPhase = Phase.Ending; elapsed = 0f; changed = true; }

        float speed = float.IsNaN(playbackSpeed) || float.IsInfinity(playbackSpeed) ? 1f : Mathf.Max(0.01f, playbackSpeed);
        if (!changed && !float.IsNaN(deltaTime) && !float.IsInfinity(deltaTime))
            elapsed += Mathf.Max(0f, deltaTime) * speed;
        if (CurrentPhase == Phase.Starting && elapsed >= Duration(fireStart))
        { elapsed -= Duration(fireStart); CurrentPhase = Phase.Looping; }
        if (CurrentPhase == Phase.Ending && elapsed >= Duration(fireEnd))
        { elapsed = 0f; CurrentPhase = Phase.Hidden; }

        owner.enabled = CurrentPhase != Phase.Hidden;
        // Keep the GameObject alive so it can respond to the next charge-up.
        if (CurrentPhase == Phase.Hidden) return;
        AnimationClip clip = CurrentPhase == Phase.Starting ? fireStart : CurrentPhase == Phase.Looping ? fireLoop : fireEnd;
        if (CurrentPhase == Phase.Looping) elapsed = Mathf.Repeat(elapsed, Duration(clip));
        clip.SampleAnimation(owner.gameObject, Mathf.Min(elapsed, Duration(clip) - 0.00001f));
    }

    private static float Duration(AnimationClip clip) => Mathf.Max(0.0001f, clip.length);
    private void OnDisable() { StopPreview(); }
    private void OnDestroy() { StopPreview(); }

    public void StopPreview()
    {
        if (owner != null)
        {
            owner.sprite = originalSprite;
            owner.enabled = originalEnabled;
            owner.raycastTarget = originalRaycast;
        }
        if (animator != null) animator.enabled = originalAnimatorEnabled;
        owner = null;
        animator = null;
        elapsed = 0f;
        CurrentPhase = Phase.Hidden;
    }
}
