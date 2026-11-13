using UnityEngine;

// PlayerGrenades: Knives back. One part of the class; the rest is in PlayerGrenades.cs and the files beside it.
public sealed partial class PlayerGrenades
{
    // A Knight's own knives that have come to rest, within reach: back in the belt, while there is room.
    private void TakeKnivesBack()
    {
        if (knives.Count == 0) return;
        gone.Clear();
        float reach = Mathf.Max(.3f, Throwables.Rule("pickupReach", 1.6f));
        Vector3 chest = transform.position + Vector3.up;
        foreach (var pair in knives)
        {
            var knife = pair.Value;
            if (knife == null) { gone.Add(pair.Key); continue; }
            if (!knife.Landed || kind != Throwable.Knife || count >= Carry || movement.IsDead) continue;
            if ((knife.transform.position - chest).sqrMagnitude > reach * reach) continue;
            gone.Add(pair.Key);
            count++;
            knife.Take();
            KnifeTaken?.Invoke(pair.Key);
            KitSound.Play(KitSound.Cue.PickAmmo, .6f);
            GrenadeHUD.Blink(KitStyle.Ice);
        }
        foreach (ushort id in gone) knives.Remove(id);
    }
}
