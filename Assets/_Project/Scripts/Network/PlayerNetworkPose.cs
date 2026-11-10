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
    private readonly NetworkVariable<FixedString64Bytes> skin = new NetworkVariable<FixedString64Bytes>(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    // The maximum before the health, so a copy knows how much it can hold (a perk's 145, not the base 100: friend
    // feedback C14, where a teammate's card read 100 for 145).
    private readonly NetworkVariable<float> maximumHealth = new NetworkVariable<float>(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<float> health = new NetworkVariable<float>(default,
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
            maximumHealth.Value = movement.DamageReceiver.MaximumHealth;
            health.Value = movement.DamageReceiver.Health;
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
        maximumHealth.OnValueChanged += OnMaximumChanged;
        health.OnValueChanged += OnHealthChanged;
        ShowSkin();
        if (maximumHealth.Value > 0f) movement.DamageReceiver.SetMaximumHealth(maximumHealth.Value);
        if (health.Value > 0f) movement.DamageReceiver.ShowHealth(health.Value);
    }

    public override void OnNetworkDespawn()
    {
        ProfilePicture.Changed -= SendPortrait;
        if (Portrait != null) { Destroy(Portrait); Portrait = null; }
        GameSettings.Changed -= SendSkin;
        pose.OnValueChanged -= OnPoseChanged;
        skin.OnValueChanged -= OnSkinChanged;
        maximumHealth.OnValueChanged -= OnMaximumChanged;
        health.OnValueChanged -= OnHealthChanged;
    }

    // A bigger maximum may come after the health it holds: the health is shown again under it.
    private void OnMaximumChanged(float previous, float current)
    {
        if (current <= 0f) return;
        movement.DamageReceiver.SetMaximumHealth(current);
        if (health.Value > 0f) movement.DamageReceiver.ShowHealth(health.Value);
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
        maximumHealth.Value = movement.DamageReceiver.MaximumHealth;
        health.Value = movement.DamageReceiver.Health;
    }

    // A copy's pose came in: how late, for the buffer (NetSmoothing). The host's own player comes the way zombies do.
    private void OnPoseChanged(PlayerPose previous, PlayerPose current)
        => NetSmoothing.Heard(NetworkManager, current.Tick, OwnerClientId == NetworkManager.ServerClientId);

    private void OnHealthChanged(float previous, float current) => movement.DamageReceiver.ShowHealth(current);

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

    // The skin and its colour set in one string (PlayerBodyProfile.LookId); a build without colour sets reads the skin only.
    private void SendSkin()
    {
        var settings = GameSettings.Current;
        string look = PlayerBodyProfile.LookId(settings.characterSkin, settings.characterVariant);
        if (skin.Value.ToString() != look) skin.Value = look;
    }

    private void OnSkinChanged(FixedString64Bytes previous, FixedString64Bytes current) => ShowSkin();

    private void ShowSkin()
    {
        var body = GetComponent<PlayerBody>();
        if (body != null) body.ShowSkin(skin.Value.ToString());
    }
}
