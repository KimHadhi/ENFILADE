using UnityEngine;

/// <summary>Confirmed defenses drive sound and a one-use, real-time counter opportunity.</summary>
[DisallowMultipleComponent, DefaultExecutionOrder(-180)]
public sealed class PlayerPerfectDefense : MonoBehaviour
{
    private PlayerMovement player;
    private PlayerDamageReceiver receiver;
    private CombatTimeEffects timeEffects;
    private MeleeCombatTarget attacker;
    private float remaining;
    private int rewardedDodge = -1;
    public bool CounterAvailable => remaining > 0f;
    public float Remaining => remaining;
    public void Initialize(PlayerMovement owner)
    {
        if (receiver != null) receiver.HitResolved -= OnResolved;
        player = owner; receiver = owner.DamageReceiver;
        receiver.HitResolved += OnResolved;
        timeEffects = GetComponent<CombatTimeEffects>() ?? gameObject.AddComponent<CombatTimeEffects>();
        if (Application.isPlaying && GetComponent<PerfectDefenseHUD>() == null) gameObject.AddComponent<PerfectDefenseHUD>();
    }
    private void OnEnable() { var owner = GetComponent<PlayerMovement>(); if (owner?.DamageReceiver != null) Initialize(owner); }
    private void OnResolved(DamageHit hit, DamageResult result)
    {
        if (result == DamageResult.Blocked) { CombatCueAudio.Play(gameObject, CombatCue.Block); return; }
        if (result == DamageResult.Parried)
        {
            CombatCueAudio.Play(gameObject, CombatCue.Parry);
            if (hit.IsComboFinisher) OpenCounter(hit);
        }
        else if (result == DamageResult.Evaded && rewardedDodge != player.Abilities.Dodge.Activation)
        {
            rewardedDodge = player.Abilities.Dodge.Activation;
            CombatCueAudio.Play(gameObject, CombatCue.PerfectEvade); OpenCounter(hit);
        }
        else if (result == DamageResult.Damaged) Cancel();
    }
    private void OpenCounter(DamageHit hit)
    {
        attacker = hit.Source != null ? hit.Source.GetComponentInParent<MeleeCombatTarget>() : null;
        var settings = CombatFeedbackSettings.Instance;
        remaining = settings != null ? settings.counterWindowSeconds : 1f;
        timeEffects.RequestSlowdown(this, settings != null ? settings.perfectTimeScale : .08f, remaining);
    }
    private void Update()
    {
        if (Time.timeScale <= 0f) return;
        if (player == null || !player.InputAvailable || player.CombatInputLocked || player.IsParkour || player.IsDiving) { Cancel(); return; }
        var input = GetComponent<PlayerWeaponInput>();
        if (input != null && input.TryReadMeleeInput(out var frame) && frame.AttackPressed) TryCounter();
        Tick(Time.unscaledDeltaTime);
    }
    public void Tick(float realDeltaTime)
    {
        if (!CounterAvailable) return;
        if (player == null || !player.DamageReceiver.IsAlive || player.Execution?.IsExecuting == true
            || (attacker != null && !attacker.IsAvailable)) { Cancel(); return; }
        remaining = Mathf.Max(0f, remaining - Mathf.Max(0f, realDeltaTime));
        if (remaining <= 0f) Cancel();
    }
    public bool TryCounter()
    {
        if (Time.timeScale <= 0f || !CounterAvailable || attacker == null || !attacker.IsAvailable || player.CombatInputLocked
            || player.IsParkour || player.IsDiving || !player.DamageReceiver.IsAlive) return false;
        var settings = CombatFeedbackSettings.Instance;
        var camera = GetComponentInChildren<Camera>(); Transform aim = camera != null ? camera.transform : transform;
        var collider = attacker.GetComponentInChildren<Collider>();
        if (collider == null) return false;
        Vector3 point = collider.bounds.center, delta = point - aim.position;
        if (delta.magnitude > (settings != null ? settings.counterReach : 4f)) return false;
        var target = attacker; var targetReceiver = target.GetComponent<IDamageReceiver>();
        if (targetReceiver == null) return false;
        Cancel(); // Spend once before dispatching events or damage.
        player.MeleeCombat?.CancelAction(); player.Abilities.Combat.CancelShove();
        player.Abilities.Dodge.CancelWindow();
        if (player.CurrentState == PlayerMovement.MovementState.StepDodge) player.Abilities.ReturnToLocomotion();
        player.DodgePassThrough?.Finish();
        GetComponent<WeaponController>()?.Runtime?.Interrupt();
        GetComponent<PlayerWeaponInput>()?.RequireTriggerRelease();
        float posture = settings != null ? settings.counterPostureDamage : 60f;
        var result = targetReceiver.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), settings != null ? settings.counterDamage : 80f,
            DamageKind.Attack, point, delta.normalized, gameObject, false, posture, isCounter: true));
        if (result == DamageResult.Damaged) target.ApplyPostureDamage(posture, .65f);
        player.MeleeCombat?.ShowCounterPunch(result == DamageResult.Damaged);
        return true;
    }
    public void Cancel() { remaining = 0f; attacker = null; timeEffects?.Release(this); }
    private void OnDisable() { Cancel(); if (receiver != null) receiver.HitResolved -= OnResolved; }
}
