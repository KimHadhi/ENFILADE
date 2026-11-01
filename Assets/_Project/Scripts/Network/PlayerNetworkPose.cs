using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Carries a player's pose and skin to the other PCs. The owner writes them; every other PC plays them back on its copy
/// through MovementMirror, ahead of PlayerMovement. Alone it never spawns and does nothing.
/// </summary>
[DisallowMultipleComponent, DefaultExecutionOrder(-110)]
public sealed class PlayerNetworkPose : NetworkBehaviour
{
    // The pose is what animates the copy, and the eye can't tell more than this many updates a second apart.
    private const float SendInterval = .05f;

    private readonly NetworkVariable<PlayerPose> pose = new NetworkVariable<PlayerPose>(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private readonly NetworkVariable<FixedString64Bytes> skin = new NetworkVariable<FixedString64Bytes>(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    private PlayerMovement movement;

    public override void OnNetworkSpawn()
    {
        movement = GetComponent<PlayerMovement>();
        pose.SetUpdateTraits(new NetworkVariableUpdateTraits { MinSecondsBetweenUpdates = SendInterval });
        if (IsOwner)
        {
            GameSettings.Changed += SendSkin;
            SendSkin();
            return;
        }
        skin.OnValueChanged += OnSkinChanged;
        ShowSkin();
    }

    public override void OnNetworkDespawn()
    {
        GameSettings.Changed -= SendSkin;
        skin.OnValueChanged -= OnSkinChanged;
    }

    private void Update()
    {
        if (IsSpawned && !IsOwner) movement.Mirror.Apply(pose.Value, Time.deltaTime);
    }

    private void LateUpdate()
    {
        if (IsSpawned && IsOwner) pose.Value = movement.Mirror.Capture();
    }

    private void SendSkin() => skin.Value = GameSettings.Current.characterSkin;

    private void OnSkinChanged(FixedString64Bytes previous, FixedString64Bytes current) => ShowSkin();

    private void ShowSkin()
    {
        var body = GetComponent<PlayerBody>();
        if (body != null) body.ShowSkin(skin.Value.ToString());
    }
}
