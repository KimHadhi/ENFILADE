using Unity.Netcode;
using UnityEngine;

/// <summary>
/// What a player does with their hands, for the other PCs: the weapon out, aiming, reloading and guarding as a stance,
/// and shots, punches, counter elbows, shoves, reload kicks and the start of an execution as they happen. Each PC plays these on its own copy, whose
/// gun and hands then work as they always do, and nothing here deals damage, except a teammate's blow passed on to its
/// owner when friendly fire is on (FriendlyHit). The few faces only their PC knows the reason for come too (FaceRpc;
/// PlayerFace reads the rest on every PC). Alone it never spawns and does nothing.
/// </summary>
[DisallowMultipleComponent]
public sealed partial class PlayerNetworkCombat : NetworkBehaviour
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
        /// <summary>
        /// The gun in the slot they hold: its place in Resources/Combat/FPSBody's guns plus one, or 0 for none of those.
        /// A gun put in a slot (/gun, later pickups and the shop) showed as the slot's own gun on other PCs (2026-11-09).
        /// </summary>
        public ushort Gun;
        /// <summary>A runner pins them to the ground (PlayerHold): their copy stays lying where it fell.</summary>
        public bool Pinned;
        /// <summary>Some zombie holds them, any way (PlayerHold.IsHeld): friends see them through walls (DownedAura).</summary>
        public bool Held;
        /// <summary>
        /// The sight on that gun (its GunSights index) plus one, 0 for a gun without a choice of sights; and its fire mode
        /// (WeaponFireMode) plus one, 0 for none. T and B changed them only on their own PC (2026-11-09).
        /// </summary>
        public byte Sight, Mode;
        /// <summary>
        /// The muzzle and underbarrel parts on that gun (GunAttachments) plus one, 0 for no choice; whether its flashlight
        /// is on.
        /// </summary>
        public byte Muzzle, Under;
        public bool Light;
        /// <summary>The grip on that gun plus one, 0 for no choice; their support hand goes on it there too.</summary>
        public byte Grip;
        /// <summary>How far into its clip their reload began, 0-255: theirs going on from a cut-short one (WeaponRuntime.ReloadStartShare).</summary>
        public byte ReloadFrom;
        /// <summary>The guns in their first, second and special slots (Gun ids), held or not: their body wears the others (HolsteredGuns).</summary>
        public ushort GunA, GunB, GunD;
    }

    // The gun's place in FPSBody's guns plus one (the same list on every PC of a build), or 0.
    private static ushort GunId(WeaponData data)
    {
        var guns = FPSBodyAssets.Instance != null ? FPSBodyAssets.Instance.guns : null;
        if (data == null || guns == null) return 0;
        for (int i = 0; i < guns.Length; i++)
            if (guns[i].weapon == data) return (ushort)(i + 1);
        return 0;
    }

    private static WeaponData GunOf(ushort id)
    {
        var guns = FPSBodyAssets.Instance != null ? FPSBodyAssets.Instance.guns : null;
        return guns != null && id > 0 && id <= guns.Length ? guns[id - 1].weapon : null;
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
    private DodgeDash dodgeLook;
    private GuardPose guardPose;
    private PlayerExecution execution;
    private ExecutionPairPlayer executionPairs;
    private ExecutionKnife executionKnife;
    private PlayerThrow throws;
    private PlayerGrenades grenades;
    private PlayerKnock knocks;
    private ClassKit kit;
    private DetenteKit detente;
    private KnightCover cover;
    private PlayerPing ping;
    private PlayerCallouts callouts;
    private SupportKit support;
    private PlayerDamageReceiver receiver;
    private CombatTimeEffects time;
    private PlayerBody body;
    private PlayerRevive revive;
    private PlayerMovement mover;
    private PlayerFace face;

    /// <summary>Someone on this PC finished getting this copy's player up: their own PC does it.</summary>
    public void Revive() { PlayerTally.Raised(); ReviveRpc(); }
    /// <summary>A Détente's dose or treatment for this copy's player (DetenteKit): their own PC adds the health.</summary>
    public void Heal(float amount) { if (!IsSpawned) return; PlayerTally.Healed(this, amount); HealRpc(amount); }
    /// <summary>On this Knight's copy: its cover turned a blow on that friend (their PC judged it); its own PC steps in.</summary>
    public void SendCovered(PlayerMovement ally, Vector3 attacker)
    {
        if (IsSpawned && ally != null && ally.TryGetComponent(out NetworkObject friend)) CoveredRpc(friend.NetworkObjectId, attacker);
    }
    /// <summary>A Détente offers this copy's player a treatment by hand (DetenteKit): their own PC asks them for F.</summary>
    public void OfferTreatment(PlayerMovement detente)
    {
        if (IsSpawned && detente != null && detente.TryGetComponent(out NetworkObject from)) OfferTreatmentRpc(from.NetworkObjectId);
    }
    /// <summary>On this Détente's copy: the patient took its offer (DetenteKit, on the patient's PC); its own PC begins.</summary>
    public void AcceptTreatment(PlayerMovement patient)
    {
        if (IsSpawned && patient != null && patient.TryGetComponent(out NetworkObject from)) AcceptTreatmentRpc(from.NetworkObjectId);
    }
    /// <summary>A Détente began treating this copy's player: their own PC says so.</summary>
    public void TreatmentBegun(PlayerMovement detente)
    {
        if (IsSpawned && detente != null && detente.TryGetComponent(out NetworkObject from)) TreatmentBegunRpc(from.NetworkObjectId);
    }
    /// <summary>A treatment's end: the rest of this copy's player's health comes back over seconds, on their own PC.</summary>
    public void HealOverTime(float seconds) { if (IsSpawned) HealOverTimeRpc(seconds); }
    /// <summary>A Support's resupply for this copy's player (SupportKit): their own PC fills their guns.</summary>
    public void Resupply(int magazines, float overfill, float syringe) { if (IsSpawned) ResupplyRpc(magazines, overfill, syringe); }
    /// <summary>On the host: barricade id's health, for every client's copy of it (Barricade).</summary>
    public void ShareBarricadeHealth(ushort id, float health) { if (IsSpawned) BarricadeStateRpc(id, health); }
    /// <summary>On a client: a zombie's blow landed on its copy of barricade id; the host's is the true one (Barricade).</summary>
    public void SendBarricadeHit(ushort id, float damage) { if (IsSpawned) BarricadeHitRpc(id, damage); }
    /// <summary>On a client: this PC's player took from ammo crate id; the host's is the true one (its stock: Barricade.Take).</summary>
    public void TakeCrate(ushort id) { if (IsSpawned) TakeCrateRpc(id); }
    /// <summary>On a client: this PC's Support's hammer mended barricade id by this much; the host's is the true one (SupportKit).</summary>
    public void SendBarricadeMend(ushort id, float amount) { if (IsSpawned) BarricadeMendRpc(id, amount); }
    /// <summary>
    /// On a client: this PC's Support asks for barricade id here, facing this way, with the most of theirs that may stand
    /// (SupportKit). The host checks the spot against its own world and puts it up for everyone, or says why not.
    /// </summary>
    public void AskBarricade(ushort id, Vector3 at, float yaw, int most)
    {
        if (IsSpawned) BuildBarricadeRpc(id, at, yaw, (byte)Mathf.Clamp(most, 1, 255));
    }
    /// <summary>A Détente's Resurrection for this copy's dead player, back with this share of their health.</summary>
    public void Resurrect(float share) { if (!IsSpawned) return; PlayerTally.Raised(); ResurrectRpc(share); }
    /// <summary>Whether this player is getting that client up right now, and how far along (0-1).</summary>
    public bool IsReviving(ulong client, out float progress)
    {
        var now = stance.Value;
        progress = now.ReviveProgress / 255f;
        return IsSpawned && now.Reviving == (ushort)(client + 1);
    }
    /// <summary>The host saw nobody left standing.</summary>
    internal void AnnounceWipe() => WipeRpc();

    /// <summary>
    /// A zombie took hold of this PC's player (a shambler's grab, kneeling to eat them, a runner's pin), or let go
    /// (PlayerHold): the host, which runs it, makes it hold on there. On the host itself PlayerHold already told the zombie.
    /// </summary>
    internal void ShareHold(EnemyNetwork enemy, bool holding, HoldKind kind)
    {
        if (!IsSpawned || IsServer || enemy == null || !enemy.IsSpawned) return;
        HoldRpc(enemy.NetworkObjectId, holding, (byte)kind);
    }

    [Rpc(SendTo.Server)]
    private void HoldRpc(ulong enemy, bool holding, byte kind)
    {
        if (!NetworkManager.SpawnManager.SpawnedObjects.TryGetValue(enemy, out NetworkObject found)) return;
        if (!found.TryGetComponent(out ZombieGrip grip)) return;
        var player = GetComponent<PlayerMovement>();
        if (holding) grip.Hold(player, (HoldKind)kind);
        else if (grip.Held == player) grip.LetGo();
    }

    /// <summary>
    /// This PC's player hit this copy with friendly fire on (backlog 721, FriendlyFire): their own PC takes the blow. The id
    /// carries the hitter's client so it can never be taken for one of that PC's own attacks.
    /// </summary>
    internal void FriendlyHit(in DamageHit hit, float share, Vector3 from)
    {
        if (!IsSpawned) return;
        long id = (long)(NetworkManager.LocalClientId + 1) << 40 | (hit.Id & ((1L << 40) - 1));
        FriendlyHitRpc(id, hit.Amount * share, hit.Point, hit.Direction, hit.Origin ?? from, hit.Method, hit.CanBeGuarded,
            hit.IsCritical, hit.Pierced);
    }

    public override void OnNetworkSpawn()
    {
        receiver = GetComponent<PlayerDamageReceiver>();
        FriendlyFire.Listen();
        if (IsServer && !IsOwner) FriendlyFire.Greet(OwnerClientId);
        time = GetComponent<CombatTimeEffects>();
        body = GetComponent<PlayerBody>();
        revive = GetComponent<PlayerRevive>();
        mover = GetComponent<PlayerMovement>();
        slots = GetComponent<PlayerWeaponSlots>();
        weapon = GetComponent<WeaponController>();
        melee = GetComponent<PlayerMeleeCombat>();
        fists = GetComponent<PlayerFistAudio>();
        execution = GetComponent<PlayerExecution>();
        // The paired execution the owner's PC chose (ExecutionPairPlayer): every other PC plays it there by PairBeganRpc.
        executionPairs = ExecutionPairPlayer.For(gameObject);
        // The class knife of an execution (ExecutionKnife): the owner's stroke, copied on every other PC by ExecutionKnifeRpc.
        executionKnife = ExecutionKnife.On(gameObject);
        if (!TryGetComponent(out shoves)) shoves = gameObject.AddComponent<PlayerShoveAudio>();
        kicks = ShoveMoves.For(GetComponent<PlayerMovement>());
        // The dodge's look (DodgeDash, added by PlayerMovement): every PC draws a dodge from its state; an evade's red comes by EvadedRpc.
        dodgeLook = GetComponent<DodgeDash>();
        // The guard's pose struck as a look (GuardPose, a Knight's cover): the owner strikes it, every PC's copy shows it.
        guardPose = GuardPose.For(mover);
        // Stones (PlayerThrow): thrown by the owner, copied on every other PC.
        throws = GetComponent<PlayerThrow>();
        if (throws == null) throws = gameObject.AddComponent<PlayerThrow>();
        // Knocks on walls (PlayerKnock), likewise: the host's is the one its zombies hear.
        if (!TryGetComponent(out knocks)) knocks = gameObject.AddComponent<PlayerKnock>();
        // The class throwable on G (PlayerGrenades): thrown by the owner, copied on every other PC, burst where its PC says.
        if (!TryGetComponent(out grenades)) grenades = gameObject.AddComponent<PlayerGrenades>();
        // The class (ClassKit): an Assault's CHARGE reaches friends near it on their PCs.
        kit = GetComponent<ClassKit>();
        if (kit == null) kit = gameObject.AddComponent<ClassKit>();
        // A Détente's healing (DetenteKit): its offers and treatments go between PCs, and its syringe shows on the others.
        if (!TryGetComponent(out detente)) detente = gameObject.AddComponent<DetenteKit>();
        // A Knight's cover (KnightCover): the friend's PC judges the blows it turns.
        if (!TryGetComponent(out cover)) cover = gameObject.AddComponent<KnightCover>();
        // F on an enemy (PlayerPing): the whole team sees it marked.
        ping = GetComponent<PlayerPing>();
        if (ping == null) ping = gameObject.AddComponent<PlayerPing>();
        // Callouts (PlayerCallouts): the owner shouts, every other PC hears.
        if (!TryGetComponent(out callouts)) callouts = gameObject.AddComponent<PlayerCallouts>();
        // The face (PlayerFace): every PC reads its moods itself; the few only the owner knows come by FaceRpc.
        if (!TryGetComponent(out face)) face = gameObject.AddComponent<PlayerFace>();
        // A Support's barricades (SupportKit): every PC puts them up and takes them down.
        support = GetComponent<SupportKit>();
        if (support == null) support = gameObject.AddComponent<SupportKit>();
        if (IsOwner)
        {
            throws.Thrown += OnThrown;
            grenades.Thrown += OnGrenadeThrown;
            grenades.Burst += OnGrenadeBurst;
            grenades.KnifeTaken += OnKnifeTaken;
            grenades.Handed += OnGrenadeHanded;
            grenades.Placed += OnMinePlaced;
            knocks.Knocked += OnKnocked;
            kit.Charged += OnCharged;
            kit.Taunted += OnTaunted;
            kit.Claimed += OnClaimed;
            kit.Dueled += OnDueled;
            cover.Opened += OnCoverOpened;
            cover.Stepped += OnCoverStepped;
            detente.Injected += OnInjected;
            detente.TreatBegan += OnTreatBegan;
            detente.TreatStopped += OnTreatStopped;
            ping.Pinged += OnPinged;
            callouts.Called += OnCalled;
            face.Shared += OnFaceShared;
            support.Built += OnBuilt;
            support.TookDown += OnTookDown;
            support.ToolHeld += OnToolHeld;
            weapon.ShotFired += OnShot;
            melee.Punched += OnPunched;
            melee.Elbowed += OnElbowed;
            fists.Played += OnFistSound;
            shoves.Played += OnShoveSound;
            // Only the owner sends: a copy's Kicked comes from KickRpc itself, its Bashed from BashRpc.
            if (kicks != null) kicks.Kicked += OnKicked;
            if (kicks != null) kicks.Bashed += OnBashed;
            if (dodgeLook != null) dodgeLook.Evaded += OnEvaded;
            if (guardPose != null) guardPose.Posed += OnGuardPosed;
            execution.Started += OnExecutionStarted;
            if (executionPairs != null) { executionPairs.Began += OnPairBegan; executionPairs.Stopped += OnPairStopped; }
            if (executionKnife != null) executionKnife.Phased += OnExecutionKnife;
            receiver.HitResolved += OnHit;
            gunSkins.Value = GunSkins.LookText();
            return;
        }
        stance.OnValueChanged += OnStanceChanged;
        Show(stance.Value);
        gunSkins.OnValueChanged += OnGunSkins;
        OnGunSkins(default, gunSkins.Value);
    }

    // The skins their guns wear, picked on their title screen (GunSkins): their copy's guns wear them too.
    private readonly NetworkVariable<Unity.Collections.FixedString512Bytes> gunSkins = new NetworkVariable<Unity.Collections.FixedString512Bytes>(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);

    private void OnGunSkins(Unity.Collections.FixedString512Bytes previous, Unity.Collections.FixedString512Bytes current)
    {
        if (weapon != null && weapon.TryGetComponent(out FPSBody gunBody)) gunBody.WearSkins(current.ToString());
    }

    public override void OnNetworkDespawn()
    {
        if (weapon != null) weapon.ShotFired -= OnShot;
        if (melee != null) { melee.Punched -= OnPunched; melee.Elbowed -= OnElbowed; }
        if (fists != null) fists.Played -= OnFistSound;
        if (shoves != null) shoves.Played -= OnShoveSound;
        if (kicks != null) kicks.Kicked -= OnKicked;
        if (kicks != null) kicks.Bashed -= OnBashed;
        if (dodgeLook != null) dodgeLook.Evaded -= OnEvaded;
        if (guardPose != null) guardPose.Posed -= OnGuardPosed;
        if (execution != null) execution.Started -= OnExecutionStarted;
        if (executionPairs != null) { executionPairs.Began -= OnPairBegan; executionPairs.Stopped -= OnPairStopped; }
        if (executionKnife != null) executionKnife.Phased -= OnExecutionKnife;
        if (throws != null) throws.Thrown -= OnThrown;
        if (grenades != null)
        {
            grenades.Thrown -= OnGrenadeThrown; grenades.Burst -= OnGrenadeBurst; grenades.KnifeTaken -= OnKnifeTaken;
            grenades.Handed -= OnGrenadeHanded;
            grenades.Placed -= OnMinePlaced;
        }
        if (knocks != null) knocks.Knocked -= OnKnocked;
        if (kit != null) { kit.Charged -= OnCharged; kit.Taunted -= OnTaunted; kit.Claimed -= OnClaimed; kit.Dueled -= OnDueled; }
        if (detente != null) { detente.Injected -= OnInjected; detente.TreatBegan -= OnTreatBegan; detente.TreatStopped -= OnTreatStopped; }
        if (cover != null) { cover.Opened -= OnCoverOpened; cover.Stepped -= OnCoverStepped; }
        if (ping != null) ping.Pinged -= OnPinged;
        if (callouts != null) callouts.Called -= OnCalled;
        if (face != null) face.Shared -= OnFaceShared;
        if (support != null) { support.Built -= OnBuilt; support.TookDown -= OnTookDown; support.ToolHeld -= OnToolHeld; }
        if (receiver != null) receiver.HitResolved -= OnHit;
        stance.OnValueChanged -= OnStanceChanged;
        gunSkins.OnValueChanged -= OnGunSkins;
    }

    private void LateUpdate()
    {
        if (!IsSpawned) return;
        if (!IsOwner)
        {
            KeepTheirGun(stance.Value);
            return;
        }
        var gun = weapon.Runtime;
        bool helping = revive != null && revive.Progress > 0f && revive.Candidate != null;
        NetworkObject helped = null;
        if (helping) revive.Candidate.TryGetComponent(out helped);
        stance.Value = new Stance
        {
            Reviving = helped != null ? (ushort)(helped.OwnerClientId + 1) : (ushort)0,
            ReviveProgress = helped != null ? (byte)Mathf.RoundToInt(revive.Progress * 255f) : (byte)0,
            Slot = (sbyte)slots.SelectedSlot, Gun = GunId(slots.SelectedWeapon),
            GunA = GunId(slots.GetWeapon(0)), GunB = GunId(slots.GetWeapon(1)), GunD = GunId(slots.GetWeapon(3)),
            Sight = (byte)Mathf.Clamp(weapon.SightIndex + 1, 0, 255), Mode = gun != null ? (byte)((int)gun.FireMode + 1) : (byte)0,
            Muzzle = (byte)Mathf.Clamp(weapon.MuzzleIndex + 1, 0, 255), Under = (byte)Mathf.Clamp(weapon.UnderbarrelIndex + 1, 0, 255),
            Light = weapon.LightOn, Grip = (byte)Mathf.Clamp(weapon.GripIndex + 1, 0, 255),
            Aiming = weapon.AimBlend > AimingShare, Guarding = melee.IsGuarding || melee.IsParryActive,
            Reloading = gun != null && gun.IsReloading, EmptyReload = gun != null && gun.IsReloading && gun.IsEmptyReload,
            ReloadFrom = gun != null && gun.IsReloading ? (byte)Mathf.RoundToInt(gun.ReloadStartShare * 255f) : (byte)0,
            Downed = receiver.IsDowned, Slowed = time != null ? (byte)Mathf.RoundToInt((1f - time.Pace) * 100f) : (byte)0,
            Pinned = mover != null && mover.IsPinned, Held = mover != null && mover.IsHeld
        };
    }

    private void OnStanceChanged(Stance previous, Stance current) => Show(current);

    // A slot they aren't holding gets the gun their PC has there, for their body to wear (HolsteredGuns); one with no gun of
    // ours (0) clears only a gun, never some other special item their PC put there.
    private void Carry(int slot, ushort id)
    {
        if (slot == slots.SelectedSlot) return;
        var gun = GunOf(id);
        var now = slots.GetWeapon(slot);
        if (now == gun || (gun == null && (now == null || now.Category != WeaponCategory.Ranged))) return;
        slots.TrySetWeapon(slot, gun);
    }

    // The sight and fire mode they have on the gun they hold, on this copy's same gun. Kept every frame, not only when the
    // stance changes: the copy's body may build its guns, or draw this one, after the change came.
    private void KeepTheirGun(in Stance now)
    {
        if (weapon == null || slots == null || now.Slot < 0 || weapon.EquippedData == null
            || weapon.EquippedData != slots.GetWeapon(now.Slot)) return;
        if (now.Sight > 0 && weapon.SightIndex != now.Sight - 1) weapon.ShowSight(now.Sight - 1);
        var runtime = weapon.Runtime;
        if (now.Mode > 0 && runtime != null && (int)runtime.FireMode != now.Mode - 1) weapon.ShowFireMode((WeaponFireMode)(now.Mode - 1));
        if (now.Muzzle > 0 && weapon.MuzzleIndex != now.Muzzle - 1) weapon.ShowMuzzle(now.Muzzle - 1);
        if (now.Under > 0 && weapon.UnderbarrelIndex != now.Under - 1) weapon.ShowUnderbarrel(now.Under - 1);
        if (now.Grip > 0 && weapon.GripIndex != now.Grip - 1) weapon.ShowGrip(now.Grip - 1);
        if (weapon.LightOn != now.Light) weapon.ShowLight(now.Light);
    }

    private void Show(in Stance now)
    {
        // Their slot holds the gun they hold, before it is drawn.
        var gun = GunOf(now.Gun);
        if (gun != null && now.Slot >= 0 && slots.GetWeapon(now.Slot) != gun) slots.TrySetWeapon(now.Slot, gun);
        slots.SelectSlot(now.Slot);
        Carry(0, now.GunA); Carry(1, now.GunB); Carry(3, now.GunD);
        weapon.ShowAiming(now.Aiming);
        weapon.ShowReload(now.Reloading, now.EmptyReload, now.ReloadFrom / 255f);
        melee.ShowGuard(now.Guarding);
        receiver.ShowDowned(now.Downed);
        if (mover != null) { mover.ShowPinned(now.Pinned); mover.ShowHeld(now.Held); }
        if (body != null) body.SetPace(1f - now.Slowed / 100f);
    }

    private void OnShot(WeaponShot shot) => ShotRpc(shot.Origin, shot.Direction, (byte)Mathf.Clamp(shot.Pellet, 0, 255));
    private void OnPunched(float charge) => PunchRpc(charge);
    private void OnElbowed(int side) => ElbowRpc(side >= 0);
    private void OnFistSound(FistSound sound) => FistSoundRpc(sound);
    private void OnShoveSound(ShoveImpactKind impact) => ShoveSoundRpc(impact);
    private void OnKicked() => KickRpc();
    private void OnBashed() => BashRpc();
    private void OnEvaded(int side) => EvadedRpc((sbyte)Mathf.Clamp(side, -1, 1));
    private void OnGuardPosed(Vector3 threat) => GuardPoseRpc(threat);
    private void OnThrown(Vector3 from, Vector3 velocity) => ThrowRpc(from, velocity);
    private void OnGrenadeThrown(Throwable kind, ushort id, Vector3 from, Vector3 velocity, float fuse) => GrenadeRpc((byte)kind, id, from, velocity, fuse);
    private void OnGrenadeBurst(Throwable kind, ushort id, Vector3 at) => GrenadeBurstRpc((byte)kind, id, at);
    private void OnKnifeTaken(ushort id) => KnifeTakenRpc(id);
    private void OnGrenadeHanded(Throwable kind, PlayerGrenades.HandPhase phase) => GrenadeHandRpc((byte)kind, (byte)phase);
    private void OnMinePlaced(Throwable kind, ushort id, Vector3 at, Quaternion facing, bool wall) => MineRpc(id, at, facing, wall);
    private void OnKnocked(Vector3 at, Vector3 normal) => KnockRpc(at, normal);
    private void OnCharged(Vector3 at) => ChargeRpc(at);
    private void OnTaunted(Vector3 at) => TauntRpc(at);
    private void OnClaimed(Vector3 head, Vector3 from, Vector3 ground, bool grand) => BountyRpc(head, from, ground, grand);
    private void OnBuilt(ushort id, Vector3 at, float yaw) => BarricadeRpc(id, at, yaw);
    private void OnTookDown(ushort id) => CollapseRpc(id);
}
