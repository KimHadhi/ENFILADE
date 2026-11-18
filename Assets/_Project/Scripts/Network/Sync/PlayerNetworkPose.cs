using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Carries a player's pose, health and skin to the other PCs. The owner writes them; every other PC plays them back on
/// its copy through MovementMirror, ahead of PlayerMovement. Alone it never spawns and does nothing.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(FrameOrder.NetworkPose)]
public sealed class PlayerNetworkPose : NetworkBehaviour
{
    // Every network tick (Network.csv tickRate, 120 a second since the 2026-11-11 playtest; Netcode's default was 30):
    // this carries where the player looks, and a turn has to read as it happens. 0 holds nothing back between ticks.
    private const float SendInterval = 0f;

    private readonly NetworkVariable<PlayerPose> pose = new NetworkVariable<PlayerPose>(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    // The skin, its colour set and the outfit in its slots (OutfitLook.Join). Nineteen slots with long item ids outgrow
    // the smaller sizes; it is only sent when it changes.
    private readonly NetworkVariable<FixedString4096Bytes> skin = new NetworkVariable<FixedString4096Bytes>(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    /// <summary>This player's look as the room knows it (empty before it is first sent): the results stand a copy of it.</summary>
    internal string Look => skin.Value.ToString();
    /// <summary>
    /// What a copy shows of its player's body, as one value: the health, how much it can hold (a perk's 145, not the base
    /// 100: friend feedback C14, where a teammate's card read 100 for 145) and the shield over it. They were two values
    /// sent apart, the maximum and the health, with a second look at the health whenever a bigger maximum came after it,
    /// and the shield was not sent at all, so no PC could show a friend's (the user, 2026-11-18: "좌측 팀원 HUD에 아군 쉴드
    /// 게이지 표시 추가"). One value arrives whole, so a copy never holds a health under the wrong maximum.
    /// </summary>
    private struct Vitals : INetworkSerializeByMemcpy
    {
        public float Health, Maximum, Shield;
    }

    private readonly NetworkVariable<Vitals> vitals = new NetworkVariable<Vitals>(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private PlayerMovement movement;

    /// <summary>
    /// On a copy, this player's profile picture as their PC sent it (ProfilePicture.SmallJpg); null for none, when
    /// their letter shows. Friend feedback C14: teammates' cards showed only a letter.
    /// </summary>
    public Texture2D Portrait { get; private set; }

    public override void OnNetworkSpawn()
    {
        movement = GetComponent<PlayerMovement>();
        pose.SetUpdateTraits(new NetworkVariableUpdateTraits { MinSecondsBetweenUpdates = SendInterval });
        if (IsOwner)
        {
            vitals.Value = VitalsNow();
            GameSettings.Changed += SendSkin;
            SendSkin();
            ProfilePicture.Changed += SendPortrait;
            SendPortrait();
            return;
        }
        // Someone who was here before this PC joined sent theirs already: it asks.
        AskPortraitRpc();
        pose.OnValueChanged += OnPoseChanged;
        skin.OnValueChanged += OnSkinChanged;
        vitals.OnValueChanged += OnVitalsChanged;
        ShowSkin();
        ShowVitals(vitals.Value, true);
    }

    public override void OnNetworkDespawn()
    {
        ProfilePicture.Changed -= SendPortrait;
        if (Portrait != null) { Destroy(Portrait); Portrait = null; }
        GameSettings.Changed -= SendSkin;
        pose.OnValueChanged -= OnPoseChanged;
        skin.OnValueChanged -= OnSkinChanged;
        vitals.OnValueChanged -= OnVitalsChanged;
    }

    // The owner's body as it is now.
    private Vitals VitalsNow()
    {
        var body = movement.DamageReceiver;
        return new Vitals { Health = body.Health, Maximum = body.MaximumHealth, Shield = body.Shield };
    }

    private void OnVitalsChanged(Vitals previous, Vitals current) => ShowVitals(current, false);

    // On a copy: its body shows what its owner's PC says, the maximum first so the health fits under it. A maximum of 0
    // is a value nobody has written yet (a copy made before its owner's first word): the copy keeps what it has. On a
    // copy health 0 is death (PlayerMovement.IsDead), so the first value's health is taken only when it is above 0, as it
    // always was here: an owner whose body is not set up at that moment must not read as dead for a frame.
    private void ShowVitals(in Vitals now, bool first)
    {
        if (now.Maximum <= 0f) return;
        var body = movement.DamageReceiver;
        body.SetMaximumHealth(now.Maximum);
        if (!first || now.Health > 0f) body.ShowHealth(now.Health);
        body.SetShield(now.Shield);
    }

    private void Update()
    {
        if (IsSpawned && !IsOwner) movement.Mirror.Apply(pose.Value, Time.deltaTime);
    }

    private void LateUpdate()
    {
        if (!IsSpawned || !IsOwner) return;
        // The tick goes on only when something else changed (Netcode compares the bytes), so a pose standing still still
        // sends nothing; a new one carries the tick it was taken on (NetSmoothing).
        var now = movement.Mirror.Capture();
        now.Tick = pose.Value.Tick;
        pose.Value = now;
        if (pose.IsDirty())
        {
            now.Tick = (uint)Mathf.Max(1, NetworkManager.ServerTime.Tick);
            pose.Value = now;
        }
        vitals.Value = VitalsNow();
    }

    // A copy's pose came in: how late, for the buffer (NetSmoothing). The host's own player comes the way zombies do.
    private void OnPoseChanged(PlayerPose previous, PlayerPose current)
        => NetSmoothing.Heard(NetworkManager, current.Tick, OwnerClientId == NetworkManager.ServerClientId);

    // The owner's picture, small, to everyone else: on joining, when it changes, and when a newcomer asks.
    private void SendPortrait()
    {
        if (IsSpawned && IsOwner) PortraitRpc(ProfilePicture.SmallJpg());
    }

    [Rpc(SendTo.Owner)]
    private void AskPortraitRpc() => SendPortrait();

    [Rpc(SendTo.NotMe)]
    private void PortraitRpc(byte[] jpg)
    {
        if (Portrait != null) Destroy(Portrait);
        Portrait = ProfilePicture.FromBytes(jpg);
    }

    // The skin and its colour set in one string (PlayerBodyProfile.LookId), then the character's outfit after it
    // (OutfitLook.Join); a build without colour sets reads the skin only.
    private void SendSkin()
    {
        var settings = GameSettings.Current;
        // Outfits are kept under the character actually worn, the profile's default when none was picked.
        string skinId = PlayerBodyProfile.Load().OrNull()?.CurrentSkin?.Id ?? settings.characterSkin;
        string look = OutfitLook.Join(PlayerBodyProfile.LookId(settings.characterSkin, settings.characterVariant),
            OutfitLook.SavedText(skinId));
        if (skin.Value.ToString() != look) skin.Value = look;
    }

    private void OnSkinChanged(FixedString4096Bytes previous, FixedString4096Bytes current) => ShowSkin();

    private void ShowSkin()
    {
        var body = GetComponent<PlayerBody>();
        if (body != null) body.ShowSkin(skin.Value.ToString());
    }
}
