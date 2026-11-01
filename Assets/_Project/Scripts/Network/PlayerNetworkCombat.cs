using Unity.Netcode;
using UnityEngine;

/// <summary>
/// What a player does with their hands, for the other PCs: the weapon out, aiming, reloading and guarding as a stance,
/// and shots, punches, shoves and the start of an execution as they happen. Each PC plays these on its own copy, whose
/// gun and hands then work as they always do, and nothing here deals damage. Alone it never spawns and does nothing.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerNetworkCombat : NetworkBehaviour
{
    private struct Stance : INetworkSerializeByMemcpy
    {
        public sbyte Slot;
        public bool Aiming, Guarding, Reloading, EmptyReload, Downed;
    }

    private const float AimingShare = .5f;
    private readonly NetworkVariable<Stance> stance = new NetworkVariable<Stance>(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private PlayerWeaponSlots slots;
    private WeaponController weapon;
    private PlayerMeleeCombat melee;
    private PlayerFistAudio fists;
    private PlayerShoveAudio shoves;
    private PlayerExecution execution;
    private PlayerDamageReceiver receiver;

    /// <summary>Someone on this PC finished getting this copy's player up: their own PC does it.</summary>
    public void Revive() => ReviveRpc();
    /// <summary>The host saw nobody left standing.</summary>
    internal void AnnounceWipe() => WipeRpc();

    public override void OnNetworkSpawn()
    {
        receiver = GetComponent<PlayerDamageReceiver>();
        slots = GetComponent<PlayerWeaponSlots>();
        weapon = GetComponent<WeaponController>();
        melee = GetComponent<PlayerMeleeCombat>();
        fists = GetComponent<PlayerFistAudio>();
        execution = GetComponent<PlayerExecution>();
        if (!TryGetComponent(out shoves)) shoves = gameObject.AddComponent<PlayerShoveAudio>();
        if (IsOwner)
        {
            weapon.ShotFired += OnShot;
            melee.Punched += OnPunched;
            fists.Played += OnFistSound;
            shoves.Played += OnShoveSound;
            execution.Started += OnExecutionStarted;
            return;
        }
        stance.OnValueChanged += OnStanceChanged;
        Show(stance.Value);
    }

    public override void OnNetworkDespawn()
    {
        if (weapon != null) weapon.ShotFired -= OnShot;
        if (melee != null) melee.Punched -= OnPunched;
        if (fists != null) fists.Played -= OnFistSound;
        if (shoves != null) shoves.Played -= OnShoveSound;
        if (execution != null) execution.Started -= OnExecutionStarted;
        stance.OnValueChanged -= OnStanceChanged;
    }

    private void LateUpdate()
    {
        if (!IsSpawned || !IsOwner) return;
        var gun = weapon.Runtime;
        stance.Value = new Stance
        {
            Slot = (sbyte)slots.SelectedSlot, Aiming = weapon.AimBlend > AimingShare, Guarding = melee.IsGuarding || melee.IsParryActive,
            Reloading = gun != null && gun.IsReloading, EmptyReload = gun != null && gun.IsReloading && gun.IsEmptyReload,
            Downed = receiver.IsDowned
        };
    }

    private void OnStanceChanged(Stance previous, Stance current) => Show(current);

    private void Show(in Stance now)
    {
        slots.SelectSlot(now.Slot);
        weapon.ShowAiming(now.Aiming);
        weapon.ShowReload(now.Reloading, now.EmptyReload);
        melee.ShowGuard(now.Guarding);
        receiver.ShowDowned(now.Downed);
    }

    private void OnShot(WeaponShot shot) => ShotRpc(shot.Origin, shot.Direction);
    private void OnPunched(float charge) => PunchRpc(charge);
    private void OnFistSound(FistSound sound) => FistSoundRpc(sound);
    private void OnShoveSound(ShoveImpactKind impact) => ShoveSoundRpc(impact);
    private void OnExecutionStarted(MeleeCombatTarget victim) => ExecutionStartedRpc();

    [Rpc(SendTo.NotMe)]
    private void ShotRpc(Vector3 origin, Vector3 direction) => weapon.ShowShot(origin, direction);

    [Rpc(SendTo.NotMe)]
    private void PunchRpc(float charge) => melee.ShowPunch(charge);

    [Rpc(SendTo.NotMe)]
    private void FistSoundRpc(FistSound sound) => fists.Play(sound);

    [Rpc(SendTo.NotMe)]
    private void ShoveSoundRpc(ShoveImpactKind impact) => shoves.Play(impact);

    [Rpc(SendTo.NotMe)]
    private void ExecutionStartedRpc() => CombatCueAudio.Play(gameObject, CombatCue.Grab, 1f, true);

    [Rpc(SendTo.Owner)]
    private void ReviveRpc()
    {
        if (TryGetComponent(out PlayerLife life)) life.Revived();
    }

    [Rpc(SendTo.Everyone)]
    private void WipeRpc()
    {
        var mine = PlayerRoster.Local;
        if (mine != null && mine.TryGetComponent(out PlayerLife life)) life.BeginWipe();
    }
}
