using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Carries a player's pose, health and skin to the other PCs. The owner writes them; every other PC plays them back on
/// its copy through MovementMirror, ahead of PlayerMovement. Alone it never spawns and does nothing.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(-110)]
public sealed class PlayerNetworkPose : NetworkBehaviour
{
    // Every network tick (30 a second): this carries where the player looks, and a turn has to read as it happens.
    private const float SendInterval = .03f;

    private readonly NetworkVariable<PlayerPose> pose = new NetworkVariable<PlayerPose>(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<FixedString64Bytes> skin = new NetworkVariable<FixedString64Bytes>(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<float> health = new NetworkVariable<float>(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private PlayerMovement movement;

    public override void OnNetworkSpawn()
    {
        movement = GetComponent<PlayerMovement>();
        pose.SetUpdateTraits(new NetworkVariableUpdateTraits { MinSecondsBetweenUpdates = SendInterval });
        if (IsOwner)
        {
            health.Value = movement.DamageReceiver.Health;
            GameSettings.Changed += SendSkin;
            SendSkin();
            return;
        }
        skin.OnValueChanged += OnSkinChanged;
        health.OnValueChanged += OnHealthChanged;
        ShowSkin();
        if (health.Value > 0f) movement.DamageReceiver.ShowHealth(health.Value);
    }

    public override void OnNetworkDespawn()
    {
        GameSettings.Changed -= SendSkin;
        skin.OnValueChanged -= OnSkinChanged;
        health.OnValueChanged -= OnHealthChanged;
    }

    private void Update()
    {
        if (IsSpawned && !IsOwner) movement.Mirror.Apply(pose.Value, Time.deltaTime);
    }

    private void LateUpdate()
    {
        if (!IsSpawned || !IsOwner) return;
        pose.Value = movement.Mirror.Capture();
        health.Value = movement.DamageReceiver.Health;
    }

    private void OnHealthChanged(float previous, float current) => movement.DamageReceiver.ShowHealth(current);

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
