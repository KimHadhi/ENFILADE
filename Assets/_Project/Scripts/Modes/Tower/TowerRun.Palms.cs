using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// TowerRun: Palms. One part of the class; the rest is in TowerRun.cs and the files beside it.
public sealed partial class TowerRun
{
    // This PC's own palm: [F] held at a risen scanner within reach while the hall is choosing. A guest tells the host.
    private void ReadPalm()
    {
        int was = palm;
        palm = -1;
        var mine = PlayerRoster.Local;
        bool able = plan != null && phase == Phase.Gate && mine != null && mine.IsInitialized && !mine.IsDead && !mine.IsDowned;
        // A risen scanner within reach: the palm is on it for as long as F is held. F is shared, so the scanner says so
        // to the key's one owner (InteractKey) and the palm counts only while the held key is the scanner's: a friend
        // down beside it is got up first and a pickup that is wanted comes first, by the key's order; a pickup that
        // refuses no longer lifts the palm (this asked FieldPickups.Targeting until 2026-11-18).
        int near = able ? NearestScanner(mine) : -1;
        if (near >= 0) InteractKey.Offer(ScannerOffer, held: true);
        if (near >= 0 && InteractKey.Held(ScannerOffer)) palm = near;
        if (palm >= 0 && palm != was) KitSound.Play(KitSound.Cue.TowerScan, .8f);
        if (IsHost || network == null || !network.IsListening) return;
        if (palm == sentPalm && (palm < 0 || Time.unscaledTime < nextPalm)) return;
        sentPalm = palm;
        nextPalm = Time.unscaledTime + PalmEvery;
        using var writer = new FastBufferWriter(sizeof(byte), Allocator.Temp);
        writer.WriteValueSafe((byte)(palm < 0 ? 255 : palm));
        network.CustomMessagingManager.SendNamedMessage(PalmMessage, NetworkManager.ServerClientId, writer);
    }

    private void ReceivePalm(ulong sender, FastBufferReader reader)
    {
        reader.ReadValueSafe(out byte lane);
        palms[sender] = (lane == 255 ? -1 : lane, Time.unscaledTime);
    }

    // The lane of the risen scanner of this stretch nearest this player's palm reach, or -1.
    private int NearestScanner(PlayerMovement mine)
    {
        int nearest = -1;
        float best = palmReach;
        foreach (var scanner in scanners)
        {
            if (scanner == null || scanner.Segment != segment || !scanner.Ready) continue;
            Vector3 apart = mine.transform.position - scanner.Palm;
            if (apart.y < -2.2f || apart.y > 1f) continue;
            float distance = Flat(apart);
            if (distance <= best) { best = distance; nearest = scanner.Lane; }
        }
        return nearest;
    }

    private TowerScanner ScannerOf(int stretch, int lane)
    {
        foreach (var scanner in scanners) if (scanner != null && scanner.Segment == stretch && scanner.Lane == lane) return scanner;
        return null;
    }

    private static float Flat(Vector3 apart) { apart.y = 0f; return apart.magnitude; }

    private ulong IdOf(PlayerMovement someone)
    {
        if (someone == null) return 0UL;
        if (someone.IsLocal) return MyId;
        return someone.TryGetComponent(out NetworkObject owner) && owner.IsSpawned ? owner.OwnerClientId + 1UL : 0UL;
    }

    private PlayerMovement PlayerById(ulong id)
    {
        if (id == 0UL) return null;
        foreach (var someone in PlayerRoster.All) if (someone != null && Counted(someone) && IdOf(someone) == id) return someone;
        return null;
    }

    private string NameOf(ulong id)
    {
        var who = PlayerById(id);
        string name = who != null && who.TryGetComponent(out PlayerTally tally) ? tally.DisplayName : "";
        return string.IsNullOrEmpty(name) ? "?" : name;
    }
}
