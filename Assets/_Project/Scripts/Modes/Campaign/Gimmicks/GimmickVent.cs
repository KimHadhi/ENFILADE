using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A vent into one flat (the map gimmick card "환기구로 소독 가스", after PAYDAY 2's Golden Grin and PAYDAY 3's Touch the
/// Sky): with the team's gas can (GimmickItem, used up by it) [F] lets the gas in, and over a few seconds everything in
/// the flat chokes and drops without a sound: no call, no stir to the rooms beside it. The can is one for the map, so it
/// is a choice of which flat. The acting PC judges whom it reaches (its own player's silent kills, as a takedown's are);
/// every PC sees the gas. States: 0 ready, 1 gassed.
/// </summary>
public sealed class GimmickVent : GimmickSwitch
{
    private const byte Ready = 0, Gassed = 1;

    [SerializeField, Tooltip("The item it takes (GimmickItem.Id), used up.")]
    private string item = "";
    [SerializeField, Tooltip("Texts id of the item's name, for the prompt (\"소독 가스통\").")]
    private string itemTitle = "";
    [SerializeField, Tooltip("The flat it fills (world box).")]
    private Bounds room;
    [SerializeField, Min(.1f), Tooltip("How long the gas takes to fill it, the drops spread over it (s).")]
    private float fill = 3f;
    [SerializeField, Tooltip("The gas's look: a particle system or a tinted box in the flat, shown while it hangs; may be empty.")]
    private GameObject cloud;

    private readonly List<EnemyHealth> choking = new List<EnemyHealth>();
    private PlayerMovement gasser;
    private float gassedAt = -1f;
    private AudioSource hiss;

    internal override string Offer(PlayerMovement mine)
    {
        if (State != Ready) return null;
        if (MapGimmicks.Has(item)) return Texts.Format("gimmick.vent.use", Texts.Get(itemTitle));
        // Its hint only while the can is still to be found: once it is used up the vents say nothing.
        var can = MapGimmicks.ItemCalled(item);
        return can != null && can.Lies ? Texts.Format("gimmick.vent.need", Texts.Get(itemTitle)) : null;
    }

    internal override int Use(PlayerMovement mine)
    {
        if (State != Ready || !MapGimmicks.Has(item)) return -1;
        MapGimmicks.UseUp(item);
        // This PC's player let it in: its kills are theirs (Tick).
        gasser = mine;
        choking.Clear();
        foreach (var enemy in EnemyHealth.Living)
            if (enemy != null && enemy.IsAlive && room.Contains(enemy.transform.position)) choking.Add(enemy);
        return Gassed;
    }

    internal override bool Accepts(byte now, byte proposed) => proposed > now;

    internal override void Show(byte state, bool fresh)
    {
        if (state != Gassed) return;
        gassedAt = Time.time;
        if (cloud != null) cloud.SetActive(true);
        if (!fresh) return;
        if (hiss == null) hiss = GimmickSounds.Loop(gameObject, GimmickSounds.Cue.Hiss, 2f, 20f);
        hiss.Play();
    }

    internal override void Tick(float dt, bool host)
    {
        if (State != Gassed || gassedAt < 0f) return;
        float since = Time.time - gassedAt;
        if (hiss != null && hiss.isPlaying && since > fill + 2f) hiss.Stop();
        // The gas hangs a while after, then thins away.
        if (cloud != null && cloud.activeSelf && since > fill + 12f) cloud.SetActive(false);
        if (gasser == null || choking.Count == 0) return;
        // The acting PC drops them one by one as the gas reaches them, silently: nobody beside the flat hears a thing.
        for (int i = choking.Count - 1; i >= 0; i--)
        {
            var enemy = choking[i];
            if (enemy == null || !enemy.IsAlive) { choking.RemoveAt(i); continue; }
            if (since < fill * (i + 1) / (choking.Count + 1f)) continue;
            choking.RemoveAt(i);
            Vector3 chest = enemy.transform.position + Vector3.up;
            enemy.ReceiveHit(new DamageHit(DamageHit.NewAttackId(), enemy.Health + 1f, DamageKind.Attack, chest, Vector3.down,
                gasser.gameObject, origin: transform.position, silent: true));
        }
    }
}
