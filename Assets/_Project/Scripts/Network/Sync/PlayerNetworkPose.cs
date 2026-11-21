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
    // The face this player picked in the wardrobe to show when looked at (CharacterFace.SavedShowFace): their copy holds it
    // on every PC, where it used to pose at random (SyncAudit.md, item 13).
    private readonly NetworkVariable<FixedString128Bytes> showFace = new NetworkVariable<FixedString128Bytes>(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    /// <summary>The face their player picked to show when looked at ("" for the cute face), as the room knows it.</summary>
    internal string ShowFace => showFace.Value.ToString();
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
        /// <summary>
        /// What the body is to the fight, said by its owner once it is set up there: NotSaid, In, Fallen or Waiting. A copy
        /// could not tell "nobody has spoken yet" from "dead" by a health of 0 alone, so it used to take a first health
        /// only above 0, and a PC that joined late saw a teammate who had died before it came as alive.
        /// </summary>
        public byte Presence;
    }

    private const byte NotSaid = 0, In = 1, Fallen = 2, Waiting = 3;

    private readonly NetworkVariable<Vitals> vitals = new NetworkVariable<Vitals>(default,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    // The host's word as it makes the player: they came into a run already under way and wait to come in
    // (PlayerLife.Arrive). How someone arrived is the host's to say, once, and it travels with the spawn itself: their
    // own PC stands them out of the fight from its first frame, and every copy is born unseen, with no frame of a body
    // standing at the map's start.
    private readonly NetworkVariable<bool> arrival = new NetworkVariable<bool>(false,
        NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private PlayerMovement movement;

    /// <summary>The host, between making the player and sending them out to the room (GameSession): they come into a run under way.</summary>
    internal bool ArrivesWaiting { get; set; }

    /// <summary>
    /// On a copy, this player's profile picture as their PC sent it (ProfilePicture.SmallJpg); null for none, when
    /// their letter shows. Friend feedback C14: teammates' cards showed only a letter.
    /// </summary>
    public Texture2D Portrait { get; private set; }

    public override void OnNetworkSpawn()
    {
        movement = GetComponent<PlayerMovement>();
        pose.SetUpdateTraits(new NetworkVariableUpdateTraits { MinSecondsBetweenUpdates = SendInterval });
        if (IsServer) arrival.Value = ArrivesWaiting;
        if (IsOwner)
        {
            if (arrival.Value && TryGetComponent(out PlayerLife life)) life.Arrive();
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
        // Born waiting, before their own PC has said anything.
        if (arrival.Value && TryGetComponent(out PlayerDamageReceiver body)) body.ShowWaiting(true);
        ShowVitals(vitals.Value);
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

    // The owner's body as it is now. Until it is set up on this PC nothing is said of what it is to the fight.
    private Vitals VitalsNow()
    {
        var body = movement.DamageReceiver;
        byte presence = !movement.IsInitialized ? NotSaid : body.IsWaiting ? Waiting : body.IsAlive ? In : Fallen;
        return new Vitals { Health = body.Health, Maximum = body.MaximumHealth, Shield = body.Shield, Presence = presence };
    }

    private void OnVitalsChanged(Vitals previous, Vitals current) => ShowVitals(current);

    // On a copy: its body shows what its owner's PC says, the maximum first so the health fits under it. Nothing said
    // yet (a copy made before its owner's first word, or an owner whose body is not set up at that moment): the copy
    // keeps what it has, so it does not read as dead for a frame. Once the owner has spoken, a health of 0 is what it
    // says it is: fallen, or waiting to come in (PlayerDamageReceiver.IsWaiting), on the first word as on any other.
    private void ShowVitals(in Vitals now)
    {
        if (now.Maximum <= 0f || now.Presence == NotSaid) return;
        var body = movement.DamageReceiver;
        body.SetMaximumHealth(now.Maximum);
        body.ShowHealth(now.Health);
        body.ShowWaiting(now.Presence == Waiting);
        body.SetShield(now.Shield);
    }

    private void Update()
    {
        if (IsSpawned && !IsOwner) movement.Mirror.Apply(PoseDrawn(), Time.deltaTime);
    }

    // The poses come in as their owner takes them, each with its tick, and wait here until this PC draws the copy's place at
    // that tick (NetworkTransform's buffer, NetSmoothing): shown the moment they came, a vault, a slide, a turn or a shot's
    // stance began before the body was drawn where it began (SyncAudit.md, item 15). At most MostWaiting wait (the oldest
    // goes on to be shown past that); with none old enough yet, the last shown stays, or the oldest waiting at first.
    private const int MostWaiting = 48;
    private readonly PlayerPose[] waiting = new PlayerPose[MostWaiting];
    private int waitingFirst, waitingCount;
    private PlayerPose drawn;
    private bool drawnAny;

    private PlayerPose PoseDrawn()
    {
        var network = NetworkManager;
        if (network == null || !network.IsListening)
            return pose.Value;
        bool guestOnGuest = !network.IsServer && OwnerClientId != NetworkManager.ServerClientId;
        long due = (long)network.ServerTime.Tick - NetSmoothing.DrawnBehind(network, guestOnGuest);
        while (waitingCount > 0 && (long)waiting[waitingFirst].Tick <= due)
        {
            drawn = waiting[waitingFirst];
            drawnAny = true;
            waitingFirst = (waitingFirst + 1) % MostWaiting;
            waitingCount--;
        }
        if (!drawnAny)
        {
            if (waitingCount == 0) return pose.Value;
            drawn = waiting[waitingFirst];
            drawnAny = true;
        }
        return drawn;
    }

    private void Wait(in PlayerPose next)
    {
        // A pose with no tick (before the first stamp) or behind the last one is shown as it comes.
        if (next.Tick == 0 || (waitingCount > 0 && next.Tick < waiting[(waitingFirst + waitingCount - 1) % MostWaiting].Tick))
        {
            waitingCount = 0;
            drawn = next;
            drawnAny = true;
            return;
        }
        if (waitingCount == MostWaiting)
        {
            drawn = waiting[waitingFirst];
            drawnAny = true;
            waitingFirst = (waitingFirst + 1) % MostWaiting;
            waitingCount--;
        }
        waiting[(waitingFirst + waitingCount) % MostWaiting] = next;
        waitingCount++;
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
    {
        NetSmoothing.Heard(NetworkManager, current.Tick, OwnerClientId == NetworkManager.ServerClientId);
        Wait(current);
    }

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
        string face = CharacterFace.SavedShowFace(skinId) ?? "";
        if (face.Length > 120) face = face.Substring(0, 120);
        if (showFace.Value.ToString() != face) showFace.Value = face;
    }

    private void OnSkinChanged(FixedString4096Bytes previous, FixedString4096Bytes current) => ShowSkin();

    private void ShowSkin()
    {
        var body = GetComponent<PlayerBody>();
        if (body != null) body.ShowSkin(skin.Value.ToString());
    }
}
