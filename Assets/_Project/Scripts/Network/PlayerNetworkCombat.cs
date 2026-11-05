using Unity.Netcode;
using UnityEngine;

/// <summary>
/// What a player does with their hands, for the other PCs: the weapon out, aiming, reloading and guarding as a stance,
/// and shots, punches, shoves, reload kicks and the start of an execution as they happen. Each PC plays these on its own copy, whose
/// gun and hands then work as they always do, and nothing here deals damage. Alone it never spawns and does nothing.
/// </summary>
[DisallowMultipleComponent]
public sealed class PlayerNetworkCombat : NetworkBehaviour
{
    private struct Stance : INetworkSerializeByMemcpy
    {
        public sbyte Slot;
        public bool Aiming, Guarding, Reloading, EmptyReload, Downed;
        /// <summary>How much of their time slow motion has taken, in percent. 0 is normal, and the default before any report.</summary>
        public byte Slowed;
        /// <summary>Whose feet they're helping back to: that client's id plus one, or 0 for nobody. It ends with them, so it can't stick.</summary>
        public ushort Reviving;
        public byte ReviveProgress;
    }

    private const float AimingShare = .5f;
    private readonly NetworkVariable<Stance> stance = new NetworkVariable<Stance>(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private PlayerWeaponSlots slots;
    private WeaponController weapon;
    private PlayerMeleeCombat melee;
    private PlayerFistAudio fists;
    private PlayerShoveAudio shoves;
    private ShoveMoves kicks;
    private PlayerExecution execution;
    private PlayerDamageReceiver receiver;
    private CombatTimeEffects time;
    private PlayerBody body;
    private PlayerRevive revive;

    /// <summary>Someone on this PC finished getting this copy's player up: their own PC does it.</summary>
    public void Revive() => ReviveRpc();
    /// <summary>Whether this player is getting that client up right now, and how far along (0-1).</summary>
    public bool IsReviving(ulong client, out float progress)
    {
        var now = stance.Value;
        progress = now.ReviveProgress / 255f;
        return IsSpawned && now.Reviving == (ushort)(client + 1);
    }
    /// <summary>The host saw nobody left standing.</summary>
    internal void AnnounceWipe() => WipeRpc();

    public override void OnNetworkSpawn()
    {
        receiver = GetComponent<PlayerDamageReceiver>();
        time = GetComponent<CombatTimeEffects>();
        body = GetComponent<PlayerBody>();
        revive = GetComponent<PlayerRevive>();
        slots = GetComponent<PlayerWeaponSlots>();
        weapon = GetComponent<WeaponController>();
        melee = GetComponent<PlayerMeleeCombat>();
        fists = GetComponent<PlayerFistAudio>();
        execution = GetComponent<PlayerExecution>();
        if (!TryGetComponent(out shoves)) shoves = gameObject.AddComponent<PlayerShoveAudio>();
        kicks = ShoveMoves.For(GetComponent<PlayerMovement>());
        if (IsOwner)
        {
            weapon.ShotFired += OnShot;
            melee.Punched += OnPunched;
            fists.Played += OnFistSound;
            shoves.Played += OnShoveSound;
            // Only the owner sends: a copy's Kicked comes from KickRpc itself.
            if (kicks != null) kicks.Kicked += OnKicked;
            execution.Started += OnExecutionStarted;
            receiver.HitResolved += OnHit;
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
        if (kicks != null) kicks.Kicked -= OnKicked;
        if (execution != null) execution.Started -= OnExecutionStarted;
        if (receiver != null) receiver.HitResolved -= OnHit;
        stance.OnValueChanged -= OnStanceChanged;
    }

    private void LateUpdate()
    {
        if (!IsSpawned || !IsOwner) return;
        var gun = weapon.Runtime;
        bool helping = revive != null && revive.Progress > 0f && revive.Candidate != null;
        NetworkObject helped = null;
        if (helping) revive.Candidate.TryGetComponent(out helped);
        stance.Value = new Stance
        {
            Reviving = helped != null ? (ushort)(helped.OwnerClientId + 1) : (ushort)0,
            ReviveProgress = helped != null ? (byte)Mathf.RoundToInt(revive.Progress * 255f) : (byte)0,
            Slot = (sbyte)slots.SelectedSlot, Aiming = weapon.AimBlend > AimingShare, Guarding = melee.IsGuarding || melee.IsParryActive,
            Reloading = gun != null && gun.IsReloading, EmptyReload = gun != null && gun.IsReloading && gun.IsEmptyReload,
            Downed = receiver.IsDowned, Slowed = time != null ? (byte)Mathf.RoundToInt((1f - time.Pace) * 100f) : (byte)0
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
        if (body != null) body.SetPace(1f - now.Slowed / 100f);
    }

    private void OnShot(WeaponShot shot) => ShotRpc(shot.Origin, shot.Direction);
    private void OnPunched(float charge) => PunchRpc(charge);
    private void OnFistSound(FistSound sound) => FistSoundRpc(sound);
    private void OnShoveSound(ShoveImpactKind impact) => ShoveSoundRpc(impact);
    private void OnKicked() => KickRpc();
    private void OnExecutionStarted(MeleeCombatTarget victim) => ExecutionStartedRpc();
    private void OnHit(DamageHit hit, DamageResult result)
    {
        if (result == DamageResult.Damaged) HurtRpc(hit.Point, hit.Direction, hit.Kind == DamageKind.Fall);
    }

    [Rpc(SendTo.NotMe)]
    private void ShotRpc(Vector3 origin, Vector3 direction) => weapon.ShowShot(origin, direction);

    [Rpc(SendTo.NotMe)]
    private void PunchRpc(float charge) => melee.ShowPunch(charge);

    [Rpc(SendTo.NotMe)]
    private void FistSoundRpc(FistSound sound) => fists.Play(sound);

    [Rpc(SendTo.NotMe)]
    private void ShoveSoundRpc(ShoveImpactKind impact) => shoves.Play(impact);

    // The copy's legs play the kick (PlayerBody.Kick); the kicker's PC judged what it hit, and its sounds come as above.
    [Rpc(SendTo.NotMe)]
    private void KickRpc()
    {
        if (kicks != null) kicks.ShowKick();
    }

    [Rpc(SendTo.NotMe)]
    private void ExecutionStartedRpc() => CombatCueAudio.Play(gameObject, CombatCue.Grab, 1f, true);

    [Rpc(SendTo.NotMe)]
    private void HurtRpc(Vector3 point, Vector3 direction, bool fall)
    {
        CombatHitAudio.PlayOtherPlayerHit(point, fall);
        if (!fall) ImpactVfx.Blood(point, direction, false, false, false, receiver);
    }

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
